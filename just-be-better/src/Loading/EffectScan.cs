using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine.Rendering.PostProcessing;

namespace JustBeBetter.Loading
{
    // The first PostProcessVolume to switch on makes PostProcessManager look for effect types by calling GetTypes
    // on every loaded assembly, 11 s in Topia
    // An effect type can only live in an assembly that references the post-processing one, so the list it
    // searches is swapped for one built from just those
    // MMHOOK assemblies reference it too (hooks for RoR2's own effects) and hold 36 MB of hook types but never
    // an effect, so they're left out by name
    internal static class EffectScan
    {
        private const string PostProcessingAssembly = "Unity.Postprocessing.Runtime";
        private const string HookGenPrefix = "MMHOOK_";

        private static ManualLogSource? log;

        internal static void Apply(ManualLogSource log)
        {
            FieldInfo? field = typeof(RuntimeUtilities).GetField("m_AssemblyTypes", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(IEnumerable<Type>))
            {
                log.LogWarning("RuntimeUtilities.m_AssemblyTypes not found; the effect scan stays as the game has it");
                return;
            }

            if (field.GetValue(null) != null)
            {
                log.LogWarning("Post-processing already scanned for effects before Just_Be_Better loaded; nothing to speed up");
                return;
            }

            EffectScan.log = log;
            field.SetValue(null, EffectAssemblyTypes());
            log.LogInfo("Effect scan limited to assemblies that reference post-processing");
        }

        // For the report: the effects the game ended up with, to compare with the setting off
        internal static string Found()
        {
            var types = PostProcessManager.instance.settingsTypes.Keys.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return $"{types.Count} ({string.Join(", ", types)})";
        }

        // Lazy like the original, so assemblies loaded after this still count
        private static IEnumerable<Type> EffectAssemblyTypes()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!CanHoldEffects(assembly))
                {
                    continue;
                }

                Type[] types;
                long start = Stopwatch.GetTimestamp();
                try
                {
                    types = assembly.GetTypes();
                }
                // As the original: an assembly that can't list its types adds none
                catch (Exception)
                {
                    continue;
                }

                log?.LogDebug($"Effect scan: {assembly.GetName().Name}, {types.Length} types, {Timeline.Seconds(Stopwatch.GetTimestamp() - start):0.00} s");

                foreach (Type type in types)
                {
                    yield return type;
                }
            }
        }

        private static bool CanHoldEffects(Assembly assembly)
        {
            try
            {
                string name = assembly.GetName().Name;
                return name == PostProcessingAssembly
                    || (!name.StartsWith(HookGenPrefix, StringComparison.Ordinal)
                        && assembly.GetReferencedAssemblies().Any(r => r.Name == PostProcessingAssembly));
            }
            // Dynamic assemblies can refuse, and they hold hook stubs, not effects
            catch (Exception)
            {
                return false;
            }
        }
    }
}
