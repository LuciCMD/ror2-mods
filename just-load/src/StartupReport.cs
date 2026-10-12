using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;

namespace JustLoad
{
    internal readonly struct PluginSpan
    {
        internal PluginSpan(Span span, long awakeTicks)
        {
            Span = span;
            AwakeTicks = awakeTicks;
        }

        // From "Loading [plugin]" to its Awake returning
        internal Span Span { get; }

        internal long AwakeTicks { get; }
    }

    // Writes the startup breakdown to the log as [Report] lines, longest first in each part
    internal static class StartupReport
    {
        // Same keys as the patcher half
        private const string PluginTimesKey = "mephit.Just_Load.PluginTimes";
        private const string PreloaderDoneKey = "mephit.Just_Load.PreloaderDone";
        private const string BundleTimesKey = "mephit.Just_Load.BundleTimes";

        private const double ListedSeconds = 0.05;
        private const int TopPlugins = 12;
        private const int TopStretches = 8;
        private const int TopSystems = 10;
        private const int TopBundles = 6;
        private const int TopFreezes = 6;

        internal static void Write(ManualLogSource log)
        {
            long menu = Stopwatch.GetTimestamp();
            List<PluginSpan> plugins = ReadPluginTimes();
            long preloaderDone = AppDomain.CurrentDomain.GetData(PreloaderDoneKey) is long done ? done : 0;
            long modsEnd = plugins.Count > 0 ? plugins.Max(p => p.Span.End) : 0;

            log.LogInfo($"[Report] Launch to main menu: {Timeline.SinceLaunch(menu):0.0} s");
            if (preloaderDone != 0)
            {
                log.LogInfo($"[Report] Unity starting and the BepInEx preloader: {Timeline.SinceLaunch(preloaderDone):0.0} s");
            }

            if (plugins.Count == 0 || preloaderDone == 0)
            {
                log.LogInfo("[Report] Mods starting: not measured (the Just_Load patcher isn't installed or failed)");
            }
            else
            {
                double own = plugins.Sum(p => Timeline.Seconds(p.AwakeTicks));
                log.LogInfo(
                    $"[Report] Mods starting: {Timeline.Seconds(modsEnd - preloaderDone):0.0} s for {plugins.Count} mods; "
                    + $"{own:0.0} s of that is their own startup, the rest is loading their files. Slowest:");
                List(log, plugins.Select(p => p.Span), TopPlugins);
            }

            long gameStart = modsEnd != 0 ? modsEnd : Timeline.LaunchTicks;
            log.LogInfo($"[Report] Game starting: {Timeline.Seconds(menu - gameStart):0.0} s. Longest stretches between the game's own markers:");
            if (Timeline.Milestones.Count < 2)
            {
                log.LogInfo("[Report]   the game's markers didn't fire, so stretches can't be named");
            }
            else
            {
                List(log, Stretches(gameStart), TopStretches);
            }

            List<Span> systems;
            lock (Timeline.Systems)
            {
                systems = Timeline.Systems.ToList();
            }

            log.LogInfo($"[Report] Systems set up: {systems.Count}, {systems.Sum(s => s.Seconds):0.0} s in total. Slowest:");
            List(log, systems, TopSystems);

            log.LogInfo($"[Report] Screen effect types found: {EffectScan.Found()}");

            List<Span> bundles = ReadBundleTimes();
            log.LogInfo($"[Report] Asset bundles opened: {bundles.Count}, {bundles.Sum(b => b.Seconds):0.0} s in total. Slowest:");
            List(log, bundles, TopBundles);

            log.LogInfo(
                $"[Report] Freezes (no new frame for over 0.25 s): {Timeline.Freezes.Count}, "
                + $"{Timeline.Freezes.Sum(f => f.Seconds):0.0} s in total. Longest:");
            List(log, Timeline.Freezes, TopFreezes);

            foreach (Span milestone in Timeline.Milestones)
            {
                log.LogDebug($"[Timeline] {Timeline.SinceLaunch(milestone.Start),7:0.00} s  {milestone.Label}");
            }
        }

        private static void List(ManualLogSource log, IEnumerable<Span> spans, int count)
        {
            var listed = spans.Where(s => s.Seconds >= ListedSeconds).OrderByDescending(s => s.Seconds).Take(count).ToList();
            if (listed.Count == 0)
            {
                log.LogInfo($"[Report]   none over {ListedSeconds:0.00} s");
                return;
            }

            foreach (Span span in listed)
            {
                log.LogInfo($"[Report]   {span.Seconds,6:0.00} s  {span.Label}");
            }
        }

        // The time from each marker to the next, named after both
        private static IEnumerable<Span> Stretches(long gameStart)
        {
            var marks = Timeline.Milestones.Where(m => m.Start >= gameStart).OrderBy(m => m.Start).ToList();
            if (marks.Count == 0)
            {
                yield break;
            }

            yield return new Span($"mods done → \"{marks[0].Label}\"", gameStart, marks[0].Start);
            for (int i = 1; i < marks.Count; i++)
            {
                yield return new Span($"\"{marks[i - 1].Label}\" → \"{marks[i].Label}\"", marks[i - 1].Start, marks[i].Start);
            }
        }

        // Loading the mod's files counts when it's noticeable, so a huge DLL shows apart from a slow Awake
        private static string PluginLabel(string name, string version, long start, long awake)
        {
            double files = Timeline.Seconds(awake - start);
            return files >= ListedSeconds ? $"{name} {version} ({files:0.00} s of it loading files)" : $"{name} {version}";
        }

        private static List<Span> ReadBundleTimes()
        {
            var spans = new List<Span>();
            if (AppDomain.CurrentDomain.GetData(BundleTimesKey) is not List<string> entries)
            {
                return spans;
            }

            foreach (string entry in entries)
            {
                string[] parts = entry.Split('\t');
                if (parts.Length == 3
                    && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)
                    && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long end))
                {
                    spans.Add(new Span(parts[0], start, end));
                }
            }

            return spans;
        }

        private static List<PluginSpan> ReadPluginTimes()
        {
            var spans = new List<PluginSpan>();
            if (AppDomain.CurrentDomain.GetData(PluginTimesKey) is not List<string> entries)
            {
                return spans;
            }

            foreach (string entry in entries)
            {
                string[] parts = entry.Split('\t');
                if (parts.Length == 5
                    && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)
                    && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long awake)
                    && long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long end))
                {
                    spans.Add(new PluginSpan(new Span(PluginLabel(parts[0], parts[1], start, awake), start, end), end - awake));
                }
            }

            return spans;
        }
    }
}
