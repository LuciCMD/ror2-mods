using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace JustBeBetter.Patcher
{
    // BepInEx preloader patcher that patches nothing, Finish runs before the chainloader and arranges for each
    // plugin to be timed inside the chainloader's own loop
    // The plugin half reads the results from AppDomain data, so the two assemblies never reference each other
    //
    // Nothing that calls into Unity may be hooked from here, since Unity registers its native calls after the
    // preloader and a method compiled now keeps a broken native call for the whole session
    // So Finish only hooks Chainloader.Initialize (no Unity calls), and that hook times Chainloader.Start and
    // hooks asset bundle loads once Unity is up
    // The bundle hooks also serve the bundle cache, so they stay for the session
    public static class JustBeBetterPatcher
    {
        // Shared with the plugin half, entries are "name\tversion\tloadStart\tawakeStart\tend" in Stopwatch ticks
        public const string PluginTimesKey = "mephit.Just_Be_Better.PluginTimes";
        public const string PreloaderDoneKey = "mephit.Just_Be_Better.PreloaderDone";

        // Entries are "label\tstart\tend"
        // Mods open most bundles in their Awake, before the plugin half loads
        public const string BundleTimesKey = "mephit.Just_Be_Better.BundleTimes";
        public const string HooksKey = "mephit.Just_Be_Better.PatcherHooks";

        // An Action the plugin half calls once the main menu shows: bundle timing stops, cache copies start
        public const string MainMenuKey = "mephit.Just_Be_Better.MainMenuReached";

        // A Func<string> giving the bundle cache's progress (BundleCache.Status)
        public const string CacheStatusKey = "mephit.Just_Be_Better.BundleCacheStatus";

        private const string ConfigName = "mephit.Just_Be_Better.cfg";
        private const string Section = "Loading";
        private const string GeneralSection = "General";

        private static readonly List<string> PluginTimes = new List<string>();
        private static readonly List<string> BundleTimes = new List<string>();
        private static readonly List<IDisposable> Hooks = new List<IDisposable>();
        private static readonly List<IDisposable> BundleHooks = new List<IDisposable>();
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Just_Be_Better Patcher");
        private static bool startHooked;
        private static long loadStart;
        private static bool timingBundles = true;

        public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        public static void Finish()
        {
            ReadSettings(out bool enabled, out bool bundleCache, out bool verbose);
            if (!enabled)
            {
                if (verbose)
                {
                    Log.LogInfo("Loading is off; nothing timed or cached");
                }

                return;
            }

            AppDomain.CurrentDomain.SetData(PreloaderDoneKey, Stopwatch.GetTimestamp());
            AppDomain.CurrentDomain.SetData(PluginTimesKey, PluginTimes);
            AppDomain.CurrentDomain.SetData(BundleTimesKey, BundleTimes);

            // The plugin half disposes these once the report is written
            AppDomain.CurrentDomain.SetData(HooksKey, Hooks);
            AppDomain.CurrentDomain.SetData(MainMenuKey, new Action(OnMainMenu));
            AppDomain.CurrentDomain.SetData(CacheStatusKey, new Func<string>(BundleCache.Status));
            BundleCache.Init(bundleCache, verbose, Log);
            try
            {
                Hooks.Add(new Hook(
                    typeof(Chainloader).GetMethod(nameof(Chainloader.Initialize)),
                    new Action<Action<string, bool, ICollection<LogEventArgs>>, string, bool, ICollection<LogEventArgs>>(OnInitialize)));
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't time plugin startup; the report will leave it out: {e}");
            }
        }

        // Read only, from the plugin half's file, which writes the entries and their descriptions
        // Verbose Logging is read once here, the plugin half follows later changes to it
        private static void ReadSettings(out bool enabled, out bool bundleCache, out bool verbose)
        {
            enabled = true;
            bundleCache = false;
            verbose = false;
            try
            {
                var config = new ConfigFile(Path.Combine(Paths.ConfigPath, ConfigName), false) { SaveOnConfigSet = false };
                enabled = config.Bind(Section, "Enabled", true, string.Empty).Value;
                bundleCache = config.Bind(Section, "Bundle Cache", true, string.Empty).Value;
                verbose = config.Bind(GeneralSection, "Verbose Logging", false, string.Empty).Value;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't read the Loading settings; the bundle cache is off this launch: {e.Message}");
            }
        }

        private static void OnMainMenu()
        {
            timingBundles = false;
            BundleCache.StartBuilding();
        }

        private static void OnInitialize(
            Action<string, bool, ICollection<LogEventArgs>> orig, string gameExePath, bool startConsole, ICollection<LogEventArgs> preloaderLogEvents)
        {
            orig(gameExePath, startConsole, preloaderLogEvents);
            if (startHooked)
            {
                return;
            }

            startHooked = true;
            try
            {
                Hooks.Add(new ILHook(typeof(Chainloader).GetMethod(nameof(Chainloader.Start)), TimePluginLoads));
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't time plugin startup; the report will leave it out: {e}");
            }

            try
            {
                HookBundles();
            }
            catch (Exception e)
            {
                Log.LogWarning($"Couldn't time asset bundles; the report will leave them out: {e}");
            }
        }

        private static void HookBundles()
        {
            Type bundle = typeof(AssetBundle);
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromFile), new[] { typeof(string) }),
                new Func<Func<string, AssetBundle>, string, AssetBundle>((orig, path) => FromFile(() => orig(path), path, 0))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromFile), new[] { typeof(string), typeof(uint) }),
                new Func<Func<string, uint, AssetBundle>, string, uint, AssetBundle>((orig, path, crc) => FromFile(() => orig(path, crc), path, crc))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromFile), new[] { typeof(string), typeof(uint), typeof(ulong) }),
                new Func<Func<string, uint, ulong, AssetBundle>, string, uint, ulong, AssetBundle>((orig, path, crc, offset) =>
                    offset == 0 ? FromFile(() => orig(path, crc, offset), path, crc) : TimeBundle(() => orig(path, crc, offset), Path.GetFileName(path)))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromMemory), new[] { typeof(byte[]) }),
                new Func<Func<byte[], AssetBundle>, byte[], AssetBundle>((orig, bytes) => FromMemory(() => orig(bytes), bytes, 0))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromMemory), new[] { typeof(byte[]), typeof(uint) }),
                new Func<Func<byte[], uint, AssetBundle>, byte[], uint, AssetBundle>((orig, bytes, crc) => FromMemory(() => orig(bytes, crc), bytes, crc))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromStream), new[] { typeof(Stream) }),
                new Func<Func<Stream, AssetBundle>, Stream, AssetBundle>((orig, stream) => FromStream(() => orig(stream), stream, 0))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromStream), new[] { typeof(Stream), typeof(uint) }),
                new Func<Func<Stream, uint, AssetBundle>, Stream, uint, AssetBundle>((orig, stream, crc) => FromStream(() => orig(stream, crc), stream, crc))));
            BundleHooks.Add(new Hook(bundle.GetMethod(nameof(AssetBundle.LoadFromStream), new[] { typeof(Stream), typeof(uint), typeof(uint) }),
                new Func<Func<Stream, uint, uint, AssetBundle>, Stream, uint, uint, AssetBundle>((orig, stream, crc, buffer) => FromStream(() => orig(stream, crc, buffer), stream, crc))));
        }

        // The cache opening its own copy comes back through here, and that one isn't timed separately
        private static AssetBundle FromFile(Func<AssetBundle> load, string path, uint crc)
        {
            if (BundleCache.IsCopy(path))
            {
                return load();
            }

            Assembly? caller = Caller();
            return TimeBundle(() => BundleCache.FromFile(path, crc, caller, load), Path.GetFileName(path), caller);
        }

        private static AssetBundle FromMemory(Func<AssetBundle> load, byte[] bytes, uint crc)
        {
            Assembly? caller = Caller();
            return TimeBundle(() => BundleCache.FromMemory(bytes, crc, caller, load), Megabytes(bytes), caller);
        }

        private static AssetBundle FromStream(Func<AssetBundle> load, Stream stream, uint crc)
        {
            Assembly? caller = Caller();
            return TimeBundle(() => BundleCache.FromStream(stream, crc, caller, load), "stream", caller);
        }

        private static string Megabytes(byte[]? bytes)
        {
            return $"memory, {(bytes?.Length ?? 0) / 1e6:0.0} MB";
        }

        private static AssetBundle TimeBundle(Func<AssetBundle> load, string source, Assembly? caller = null)
        {
            if (!timingBundles)
            {
                return load();
            }

            long start = Stopwatch.GetTimestamp();
            AssetBundle loaded = load();
            long end = Stopwatch.GetTimestamp();
            string copy = BundleCache.TakeServedCopy() ? " (LZ4 copy)" : string.Empty;
            string name = (caller ?? Caller())?.GetName().Name ?? "?";
            BundleTimes.Add($"{name}: {source}{copy}\t{start}\t{end}");
            return loaded;
        }

        // The first frame outside Unity, MonoMod and Just_Be_Better is the mod that asked for the bundle
        private static Assembly? Caller()
        {
            foreach (StackFrame frame in new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                Assembly? assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                string name = assembly?.GetName().Name ?? string.Empty;
                if (name.Length > 0 && !name.StartsWith("UnityEngine", StringComparison.Ordinal)
                    && !name.StartsWith("MonoMod", StringComparison.Ordinal) && !name.StartsWith("Just_Be_Better", StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            return null;
        }

        // Marks each "Loading [plugin]" and routes the plugin's AddComponent, which runs its Awake, through a timer
        private static void TimePluginLoads(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdstr("Loading [{0}]"))
                || !cursor.TryGotoNext(MoveType.After, i => i.MatchCall(typeof(BepInEx.Logging.Logger), "LogInfo")))
            {
                Log.LogWarning("Chainloader.Start has changed; plugin startup isn't timed");
                return;
            }

            cursor.Emit(OpCodes.Call, Own(nameof(MarkLoading)));
            if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<GameObject>(nameof(GameObject.AddComponent))))
            {
                Log.LogWarning("Chainloader.Start has changed; plugin startup isn't timed");
                return;
            }

            cursor.Next.OpCode = OpCodes.Call;
            cursor.Next.Operand = il.Import(Own(nameof(TimedAddComponent)));
        }

        private static MethodInfo Own(string name)
        {
            return typeof(JustBeBetterPatcher).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        }

        private static void MarkLoading()
        {
            loadStart = Stopwatch.GetTimestamp();
        }

        private static Component TimedAddComponent(GameObject self, Type type)
        {
            long awakeStart = Stopwatch.GetTimestamp();
            Component component = self.AddComponent(type);
            long end = Stopwatch.GetTimestamp();
            var meta = (BepInPlugin?)Attribute.GetCustomAttribute(type, typeof(BepInPlugin));
            string name = meta?.Name ?? type.FullName ?? type.Name;
            string version = meta?.Version?.ToString() ?? string.Empty;
            long start = loadStart != 0 ? loadStart : awakeStart;
            PluginTimes.Add($"{name}\t{version}\t{start}\t{awakeStart}\t{end}");
            loadStart = 0;
            return component;
        }
    }
}
