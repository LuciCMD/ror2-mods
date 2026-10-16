using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using RoR2;
using RoR2.Items;
using RoR2.Karma;

namespace JustBeBetter.Saves
{
    // Private game fields read by reflection
    // Their names can change with any patch, so each is looked up once and reported by name when missing,
    // rather than failing somewhere deep in a save
    internal static class GameFields
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // Run's own random number generators, all public Xoroshiro128Plus fields
        internal static readonly string[] RunRngNames =
        {
            "runRNG", "loopRngGenerator", "nextStageRng", "rerollStageRng", "stageRngGenerator", "stageRng",
            "bossRewardRng", "treasureRng", "spawnRng", "randomSurvivorOnRespawnRng",
        };

        internal static readonly string[] SimulacrumRngNames = { "waveRng", "enemyItemRng", "safeWardRng" };

        internal static readonly FieldInfo? RngState0 = typeof(Xoroshiro128Plus).GetField("state0", Instance);
        internal static readonly FieldInfo? RngState1 = typeof(Xoroshiro128Plus).GetField("state1", Instance);
        internal static readonly FieldInfo? RunEventFlags = typeof(Run).GetField("eventFlags", Instance);
        internal static readonly FieldInfo? SimulacrumEnemyInventory = typeof(InfiniteTowerRun).GetField("enemyInventory", Instance);
        internal static readonly FieldInfo? SimulacrumPatternIndex = typeof(InfiniteTowerRun).GetField("enemyItemPatternIndex", Instance);
        internal static readonly FieldInfo? SuppressionMap = typeof(SuppressedItemManager).GetField("transformationMap", Static);
        internal static readonly FieldInfo? KarmaRunTriggers = typeof(KarmaManager).GetField("runTriggers", Instance);

        // Unlike the public NetworkactualKarma setter, this also updates the effective karma the game reads
        internal static readonly MethodInfo? KarmaSetServer = typeof(KarmaManager).GetMethod("SetKarmaServer", Instance);
        internal static readonly PropertyInfo? KarmaTriggerProgress = typeof(BaseKarmaTrigger).GetProperty("progress", Instance);

        internal static void ReportMissing(ManualLogSource log)
        {
            var missing = new List<string>();
            Check(missing, RngState0, "Xoroshiro128Plus.state0");
            Check(missing, RngState1, "Xoroshiro128Plus.state1");
            Check(missing, RunEventFlags, "Run.eventFlags");
            Check(missing, SimulacrumEnemyInventory, "InfiniteTowerRun.enemyInventory");
            Check(missing, SimulacrumPatternIndex, "InfiniteTowerRun.enemyItemPatternIndex");
            Check(missing, SuppressionMap, "SuppressedItemManager.transformationMap");
            Check(missing, KarmaRunTriggers, "KarmaManager.runTriggers");
            Check(missing, KarmaSetServer, "KarmaManager.SetKarmaServer");
            Check(missing, KarmaTriggerProgress?.GetSetMethod(true), "BaseKarmaTrigger.progress setter");
            foreach (string name in RunRngNames)
            {
                Check(missing, typeof(Run).GetField(name, Instance), "Run." + name);
            }

            foreach (string name in SimulacrumRngNames)
            {
                Check(missing, typeof(InfiniteTowerRun).GetField(name, Instance), "InfiniteTowerRun." + name);
            }

            if (missing.Count > 0)
            {
                log.LogWarning($"Game fields not found, the parts using them won't save or restore: {string.Join(", ", missing)}");
            }
        }

        internal static Xoroshiro128Plus? GetRng(object owner, string fieldName)
        {
            return owner.GetType().GetField(fieldName, Instance)?.GetValue(owner) as Xoroshiro128Plus;
        }

        internal static RngSave? ReadRng(object owner, string fieldName)
        {
            Xoroshiro128Plus? rng = GetRng(owner, fieldName);
            if (rng == null || RngState0 == null || RngState1 == null)
            {
                return null;
            }

            return new RngSave
            {
                Name = fieldName,
                State0 = (ulong)RngState0.GetValue(rng),
                State1 = (ulong)RngState1.GetValue(rng),
            };
        }

        // Writes into the existing generator so anything already holding a reference sees the restored state
        internal static bool WriteRng(object owner, RngSave saved)
        {
            Xoroshiro128Plus? rng = GetRng(owner, saved.Name);
            if (rng == null || RngState0 == null || RngState1 == null)
            {
                return false;
            }

            RngState0.SetValue(rng, saved.State0);
            RngState1.SetValue(rng, saved.State1);
            return true;
        }

        private static void Check(List<string> missing, MemberInfo? member, string name)
        {
            if (member == null)
            {
                missing.Add(name);
            }
        }
    }
}
