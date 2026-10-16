using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.Items;
using RoR2.Karma;
using UnityEngine;

namespace JustBeBetter.Saves
{
    // Applies a RunSave onto a freshly started run on the server
    // Each area is applied on its own and logs one line, so the log names whatever didn't come back
    internal sealed class Restore
    {
        private const int MaxMinionsPerPlayer = 64;

        private readonly ManualLogSource log;

        internal Restore(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // Right after Run.Start: the first scene is requested but not loaded, so nothing has drawn from the
        // stage's random generators yet
        internal void ApplyRun(Run run, RunSave save)
        {
            Area("run counters", () => ApplyCounters(run, save));
            Area("random generators", () => ApplyRngs(run, save.Rngs));
            Area("event flags", () => ApplyEventFlags(run, save));
            Area("suppressed items", () => ApplySuppressed(save));
            if (save.Simulacrum != null)
            {
                Area("simulacrum", () => ApplySimulacrum(run, save.Simulacrum));
            }
        }

        // From Run.onPlayerFirstCreatedServer: the master exists, the body doesn't yet
        internal void ApplyPlayerMaster(CharacterMaster master, PlayerSave p)
        {
            Area($"player {p.DisplayName}", () => ApplyMaster(master, p));
        }

        // First stage start of a restored run: managers that start a frame after the run are up by now
        internal void ApplyStageStart(RunSave save)
        {
            Area("team experience", () => ApplyExperience(save));
            Area("karma", () => ApplyKarma(save));
        }

        // Needs the owner's body, since MasterSummon takes team and ownership from it
        internal void SpawnMinions(CharacterBody owner, PlayerSave p)
        {
            Area($"minions of {p.DisplayName}", () => SpawnAll(owner, p));
            if (p.DevotionInventory != null || p.Minions.Any(m => m.DevotionItem.Length > 0))
            {
                Area($"Devotion lemurians of {p.DisplayName}", () => SpawnLemurians(owner, p));
            }
        }

        private void Area(string name, Func<string> apply)
        {
            try
            {
                log.Verbose($"[Load] {name}: {apply()}");
            }
            catch (Exception e)
            {
                log.LogError($"[Load] {name} failed and stays as the new run set it: {e}");
            }
        }

        // Stage counts go in after Run.Start, whose OnLoopBeginServer would otherwise add a loop
        private static string ApplyCounters(Run run, RunSave save)
        {
            run.NetworkstageClearCount = save.StageClearCount;
            run.Network_loopClearCount = save.LoopClearCount;
            run.NetworkstageClearCountAtLoopStart = save.StageClearCountAtLoopStart;
            run.SetRunStopwatch(save.Stopwatch);
            run.shopPortalCount = save.ShopPortalCount;
            run.prestiegeArtifactMountainValue = save.PrestigeValue;
            return $"stage {save.StageNumber}, loop {save.LoopClearCount}, timer {save.Stopwatch:0} s";
        }

        private static string ApplyRngs(object owner, List<RngSave> rngs)
        {
            int restored = rngs.Count(rng => GameFields.WriteRng(owner, rng));
            return $"{restored} of {rngs.Count}";
        }

        private static string ApplyEventFlags(Run run, RunSave save)
        {
            if (GameFields.RunEventFlags?.GetValue(run) is not HashSet<string> flags)
            {
                return "not available";
            }

            flags.Clear();
            foreach (string flag in save.EventFlags)
            {
                run.SetEventFlag(flag);
            }

            return $"{save.EventFlags.Count} flags";
        }

        private static string ApplySuppressed(RunSave save)
        {
            int applied = 0;
            foreach (KeyValuePair<string, string> pair in save.SuppressedItems)
            {
                ItemIndex item = ItemCatalog.FindItemIndex(pair.Key);
                ItemIndex into = ItemCatalog.FindItemIndex(pair.Value);
                if (item != ItemIndex.None && SuppressedItemManager.SuppressItem(item, into))
                {
                    applied++;
                }
            }

            return $"{applied} of {save.SuppressedItems.Count}";
        }

        private static string ApplySimulacrum(Run run, SimulacrumSave saved)
        {
            if (run is not InfiniteTowerRun simulacrum)
            {
                return "saved as Simulacrum but the run isn't one; skipped";
            }

            simulacrum.Network_waveIndex = saved.WaveIndex;
            GameFields.SimulacrumPatternIndex?.SetValue(simulacrum, saved.EnemyItemPatternIndex);
            string rngs = ApplyRngs(simulacrum, saved.Rngs);
            string enemies = GameFields.SimulacrumEnemyInventory?.GetValue(simulacrum) is Inventory inventory && inventory
                ? ApplyInventory(inventory, saved.EnemyInventory)
                : "enemy inventory not available";
            return $"wave {saved.WaveIndex}, random generators {rngs}, enemies: {enemies}";
        }

        private static string ApplyMaster(CharacterMaster master, PlayerSave p)
        {
            string body = master.bodyPrefab ? BodyCatalog.GetBodyName(BodyCatalog.FindBodyIndex(master.bodyPrefab)) : "none";
            string bodyNote = body == p.Body ? string.Empty : $" (body is {body}, save says {p.Body})";

            if (p.Loadout != null)
            {
                Loadout loadout = Loadout.RequestInstance();
                if (loadout.FromXml(p.Loadout))
                {
                    master.SetLoadoutServer(loadout);
                }

                Loadout.ReturnInstance(loadout);
            }

            master.money = p.Money;
            master.voidCoins = p.VoidCoins;
            string inventory = master.inventory ? ApplyInventory(master.inventory, p.Inventory) : "no inventory";
            return $"as {p.Body}{bodyNote}, ${p.Money}, {inventory}";
        }

        private static string ApplyExperience(RunSave save)
        {
            TeamManager teams = TeamManager.instance;
            if (!teams)
            {
                return "no team manager";
            }

            ulong current = teams.GetTeamExperience(TeamIndex.Player);
            if (save.TeamExperience > current)
            {
                teams.GiveTeamExperience(TeamIndex.Player, save.TeamExperience - current);
            }

            return $"level {teams.GetTeamLevel(TeamIndex.Player)}";
        }

        private static string ApplyKarma(RunSave save)
        {
            KarmaManager karma = KarmaManager.instance;
            if (!save.Karma.HasValue)
            {
                return "not in save";
            }

            if (!karma || !karma.IsKarmaEnabled())
            {
                return "karma is off in this run";
            }

            // Triggers first: restoring inventories can flip some, and the karma set after overrides what they gave
            string triggers = ApplyKarmaTriggers(karma, save.KarmaTriggers);
            int value = Mathf.Clamp(save.Karma.Value, KarmaManager.minKarma, KarmaManager.maxKarma);
            if (GameFields.KarmaSetServer != null)
            {
                GameFields.KarmaSetServer.Invoke(karma, new object[] { value });
            }
            else
            {
                karma.NetworkactualKarma = (uint)value;
            }

            return $"{karma.NetworkactualKarma}, triggers {triggers}";
        }

        private static string ApplyKarmaTriggers(KarmaManager karma, List<KarmaTriggerSave> saved)
        {
            if (GameFields.KarmaRunTriggers?.GetValue(karma) is not List<BaseKarmaTrigger> live || GameFields.KarmaTriggerProgress == null)
            {
                return "not available";
            }

            int applied = 0;
            var unknown = new List<string>();
            foreach (KarmaTriggerSave entry in saved)
            {
                BaseKarmaTrigger? trigger = live.FirstOrDefault(t =>
                    t != null && t.karmaTriggerDef != null && t.karmaTriggerDef.identifier == entry.Identifier);
                if (trigger == null)
                {
                    unknown.Add(entry.Identifier);
                    continue;
                }

                // Straight to the property: SetProgress would flip a full trigger and pay its karma again
                GameFields.KarmaTriggerProgress.SetValue(trigger, entry.Progress);
                trigger.timesFlipped = entry.TimesFlipped;
                trigger.isComplete = entry.IsComplete;
                if (entry.IsComplete && !trigger.IsRepeatable())
                {
                    trigger.Deactivate();
                }
                else
                {
                    trigger.Activate();
                }

                applied++;
            }

            string unknownNote = unknown.Count > 0 ? $"; not in this run, skipped: {string.Join(", ", unknown)}" : string.Empty;
            return $"{applied} of {saved.Count}{unknownNote}";
        }

        private static string SpawnAll(CharacterBody owner, PlayerSave p)
        {
            int spawned = 0;
            var missing = new List<string>();
            // Skill drones are rebuilt by the survivor's own skills, their items go back in
            // SaveSession.OnSummonSkillDrone
            int fromSkills = p.Minions.Count(m => m.SkillSlot.Length > 0);
            var summonable = p.Minions.Where(m => m.SkillSlot.Length == 0 && m.DevotionItem.Length == 0).ToList();
            foreach (MinionSave minion in summonable.Take(MaxMinionsPerPlayer))
            {
                GameObject prefab = MasterCatalog.FindMasterPrefab(minion.Master);
                if (!prefab)
                {
                    missing.Add(minion.Master);
                    continue;
                }

                MinionSave saved = minion;
                CharacterMaster summoned = new MasterSummon
                {
                    masterPrefab = prefab,
                    position = owner.footPosition + Offset(),
                    rotation = Quaternion.identity,
                    summonerBodyObject = owner.gameObject,
                    ignoreTeamMemberLimit = true,
                    preSpawnSetupCallback = master => ApplyInventory(master.inventory, saved.Inventory),
                }.Perform();
                if (summoned)
                {
                    spawned++;
                }
            }

            string missingNote = missing.Count > 0 ? $"; unknown, skipped: {string.Join(", ", missing)}" : string.Empty;
            return $"summoned {spawned} of {summonable.Count}, {fromSkills} left to the survivor's skills{missingNote}";
        }

        // Replays LemurianEggController.SummonLemurian, then puts back the evolution the game added since
        private static string SpawnLemurians(CharacterBody owner, PlayerSave p)
        {
            Interactor interactor = owner.GetComponent<Interactor>();
            if (!interactor)
            {
                return "the survivor has no Interactor; skipped";
            }

            DevotionInventoryController controller = DevotionInventoryController.GetOrCreateDevotionInventoryController(interactor);
            Inventory sharedInventory = controller.GetComponent<Inventory>();
            string shared = p.DevotionInventory != null && sharedInventory
                ? ApplyInventory(sharedInventory, p.DevotionInventory)
                : "not restored";

            var lemurians = p.Minions.Where(m => m.DevotionItem.Length > 0).ToList();
            var missing = new List<string>();
            int spawned = 0;
            foreach (MinionSave lemurian in lemurians.Take(MaxMinionsPerPlayer))
            {
                GameObject prefab = MasterCatalog.FindMasterPrefab(lemurian.Master);
                ItemIndex item = ItemCatalog.FindItemIndex(lemurian.DevotionItem);
                if (!prefab || item == ItemIndex.None)
                {
                    missing.Add(prefab ? lemurian.DevotionItem : lemurian.Master);
                    continue;
                }

                CharacterMaster summoned = new MasterSummon
                {
                    masterPrefab = prefab,
                    position = owner.footPosition + Offset(),
                    rotation = Quaternion.identity,
                    summonerBodyObject = owner.gameObject,
                    ignoreTeamMemberLimit = true,
                    useAmbientLevel = true,
                }.Perform();
                if (!summoned)
                {
                    continue;
                }

                DevotedLemurianController devoted = summoned.GetComponent<DevotedLemurianController>();
                if (devoted)
                {
                    devoted.InitializeDevotedLemurian(item, controller);
                    devoted.DevotedEvolutionLevel = lemurian.DevotionLevel;
                }

                // Same call the game makes when a lemurian evolves into a Bruiser
                string body = summoned.bodyPrefab ? BodyCatalog.GetBodyName(BodyCatalog.FindBodyIndex(summoned.bodyPrefab)) : string.Empty;
                if (lemurian.Body.Length > 0 && lemurian.Body != body && BodyCatalog.FindBodyIndex(lemurian.Body) != BodyIndex.None)
                {
                    summoned.TransformBody(lemurian.Body, true);
                }

                ApplyInventory(summoned.inventory, lemurian.Inventory);
                spawned++;
            }

            string missingNote = missing.Count > 0 ? $"; unknown, skipped: {string.Join(", ", missing)}" : string.Empty;
            return $"summoned {spawned} of {lemurians.Count}, shared inventory: {shared}{missingNote}";
        }

        internal void ApplySkillDrone(CharacterMaster drone, MinionSave saved, string ownerName)
        {
            Area($"skill drone {saved.SkillSlot} of {ownerName}", () =>
                drone.inventory ? ApplyInventory(drone.inventory, saved.Inventory) : "no inventory");
        }

        // A small ring around the owner so restored minions don't spawn inside each other
        private static Vector3 Offset()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * 3f;
            return new Vector3(offset.x, 0f, offset.y);
        }

        internal static string ApplyInventory(Inventory inventory, InventorySave saved)
        {
            var missing = new List<string>();
            inventory.CleanInventory();

            // These change the decay duration and equipment set count, so they go in first
            IEnumerable<KeyValuePair<string, int>> permanent = saved.Permanent
                .OrderBy(pair => pair.Key == "Duplicator" || pair.Key == "ExtraEquipment" ? 0 : 1);
            foreach (KeyValuePair<string, int> pair in permanent)
            {
                Give(pair.Key, missing, item => inventory.GiveItemPermanent(item, pair.Value));
            }

            foreach (KeyValuePair<string, int> pair in saved.Rented)
            {
                Give(pair.Key, missing, item => inventory.GiveItemRented(item, pair.Value));
            }

            foreach (KeyValuePair<string, float> pair in saved.Temporary)
            {
                Give(pair.Key, missing, item => inventory.GiveItemTemp(item, pair.Value));
            }

            int equipment = ApplyEquipment(inventory, saved, missing);
            if (saved.KeyItem.Length > 0)
            {
                KeyItemIndex keyItem = KeyItemCatalog.FindKeyItemIndex(saved.KeyItem);
                if (keyItem != KeyItemIndex.None)
                {
                    inventory.SetKeyItem(keyItem);
                }
                else
                {
                    missing.Add(saved.KeyItem);
                }
            }

            if (saved.InfusionBonus > inventory.infusionBonus)
            {
                inventory.AddInfusionBonus(saved.InfusionBonus - inventory.infusionBonus);
            }

            RestoreOrder(inventory, saved);
            string missingNote = missing.Count > 0 ? $"; unknown, skipped: {string.Join(", ", missing)}" : string.Empty;
            return $"{saved.Permanent.Values.Sum()} items, {saved.Temporary.Count} temporary, "
                + $"{saved.Rented.Count} rented, {equipment} equipment{missingNote}";
        }

        private static void Give(string name, List<string> missing, Action<ItemIndex> give)
        {
            ItemIndex item = ItemCatalog.FindItemIndex(name);
            if (item == ItemIndex.None)
            {
                missing.Add(name);
                return;
            }

            give(item);
        }

        private static int ApplyEquipment(Inventory inventory, InventorySave saved, List<string> missing)
        {
            int applied = 0;
            foreach (EquipmentSave eq in saved.Equipment)
            {
                EquipmentIndex index = EquipmentCatalog.FindEquipmentIndex(eq.Name);
                if (index == EquipmentIndex.None)
                {
                    missing.Add(eq.Name);
                    continue;
                }

                Run.FixedTimeStamp ready = eq.CooldownLeft > 0f
                    ? Run.FixedTimeStamp.now + eq.CooldownLeft
                    : Run.FixedTimeStamp.negativeInfinity;
                var state = new EquipmentState(index, ready, eq.Charges, false);
                var location = new EquipmentLocation { slot = eq.Slot, set = eq.Set };
                inventory.SetEquipment(in state, in location);
                applied++;
            }

            var active = new EquipmentLocation { slot = saved.ActiveSlot, set = saved.ActiveSet };
            inventory.TrySetActiveEquipmentLocation(in active);
            return applied;
        }

        // Only reorders what's there, the gives above already filled the list
        private static void RestoreOrder(Inventory inventory, InventorySave saved)
        {
            List<ItemIndex> order = inventory.itemAcquisitionOrder;
            var wanted = saved.AcquisitionOrder
                .Select(ItemCatalog.FindItemIndex)
                .Where(item => item != ItemIndex.None && order.Contains(item))
                .Distinct()
                .ToList();
            var rest = order.Where(item => !wanted.Contains(item)).ToList();
            order.Clear();
            order.AddRange(wanted);
            order.AddRange(rest);
        }
    }
}
