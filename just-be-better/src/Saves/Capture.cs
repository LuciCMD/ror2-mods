using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BepInEx.Logging;
using RoR2;
using RoR2.Karma;
using UnityEngine;

namespace JustBeBetter.Saves
{
    // Builds a RunSave from the live run on the server
    // Each area is captured on its own, so one failure is logged by name and the rest of the save still happens
    internal sealed class Capture
    {
        private readonly ManualLogSource log;

        internal Capture(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal static List<RngSave> RunRngs(Run run)
        {
            return GameFields.RunRngNames
                .Select(name => GameFields.ReadRng(run, name))
                .Where(rng => rng != null)
                .Select(rng => rng!)
                .ToList();
        }

        // Areas the last snapshot left out after an error
        internal int FailedAreas { get; private set; }

        // The run or a player failed, so the snapshot can't be continued and mustn't replace a good save
        internal bool Unusable { get; private set; }

        internal RunSave Snapshot(Run run, Stage stage, List<RngSave> stageRngs)
        {
            FailedAreas = 0;
            Unusable = false;
            var save = new RunSave { Rngs = stageRngs };
            Area("run", () => CaptureRun(save, run, stage), true);
            Area("event flags", () => CaptureEventFlags(save, run));
            Area("suppressed items", () => CaptureSuppressed(save));
            Area("karma", () => CaptureKarma(save));
            Area("team experience", () => CaptureExperience(save));
            if (run is InfiniteTowerRun simulacrum)
            {
                Area("simulacrum", () => CaptureSimulacrum(save, simulacrum));
            }

            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                Area("player", () => CapturePlayer(save, player), true);
            }

            return save;
        }

        private void Area(string name, Func<string> capture, bool essential = false)
        {
            try
            {
                log.LogInfo($"[Save] {name}: {capture()}");
            }
            catch (Exception e)
            {
                FailedAreas++;
                Unusable |= essential;
                log.LogError($"[Save] {name} failed and is left out: {e}");
            }
        }

        private static string CaptureRun(RunSave save, Run run, Stage stage)
        {
            save.GameMode = GameModeCatalog.GetGameModeName(run.gameModeIndex);
            save.Scene = stage.sceneDef ? stage.sceneDef.cachedName : string.Empty;
            save.Seed = run.seed;
            save.StageClearCount = run.stageClearCount;
            save.LoopClearCount = run.Network_loopClearCount;
            save.StageClearCountAtLoopStart = run.NetworkstageClearCountAtLoopStart;
            save.Stopwatch = stage.entryStopwatchValue;
            save.ShopPortalCount = run.shopPortalCount;
            save.PrestigeValue = run.prestiegeArtifactMountainValue;
            save.RuleBook = new XElement("Rules");
            RuleBook.ToXml(save.RuleBook, run.ruleBook);
            return $"{save.GameMode}, stage {save.StageNumber} ({save.Scene}), loop {save.LoopClearCount}, "
                + $"timer {save.Stopwatch:0} s, {save.Rngs.Count} random generators";
        }

        private static string CaptureEventFlags(RunSave save, Run run)
        {
            if (GameFields.RunEventFlags?.GetValue(run) is not HashSet<string> flags)
            {
                return "not available";
            }

            save.EventFlags = flags.ToList();
            return $"{save.EventFlags.Count} flags";
        }

        private static string CaptureSuppressed(RunSave save)
        {
            if (GameFields.SuppressionMap?.GetValue(null) is not Dictionary<ItemIndex, ItemIndex> map)
            {
                return "not available";
            }

            save.SuppressedItems = map
                .Select(pair => new KeyValuePair<string, string>(ItemName(pair.Key), ItemName(pair.Value)))
                .Where(pair => pair.Key.Length > 0)
                .ToList();
            return $"{save.SuppressedItems.Count} items";
        }

        private static string CaptureKarma(RunSave save)
        {
            KarmaManager karma = KarmaManager.instance;
            if (!karma || !karma.IsKarmaEnabled())
            {
                return "off";
            }

            save.Karma = (int)karma.NetworkactualKarma;
            if (GameFields.KarmaRunTriggers?.GetValue(karma) is not List<BaseKarmaTrigger> triggers)
            {
                return $"{save.Karma}, triggers not available";
            }

            save.KarmaTriggers = triggers
                .Where(t => t != null && t.karmaTriggerDef != null)
                .Select(t => new KarmaTriggerSave
                {
                    Identifier = t.karmaTriggerDef.identifier,
                    Progress = t.progress,
                    TimesFlipped = t.timesFlipped,
                    IsComplete = t.isComplete,
                })
                .ToList();
            return $"{save.Karma}, {save.KarmaTriggers.Count} triggers ({save.KarmaTriggers.Count(t => t.IsComplete)} done)";
        }

        private static string CaptureExperience(RunSave save)
        {
            TeamManager teams = TeamManager.instance;
            if (!teams)
            {
                return "no team manager";
            }

            save.TeamExperience = teams.GetTeamExperience(TeamIndex.Player);
            return $"{save.TeamExperience} (level {teams.GetTeamLevel(TeamIndex.Player)})";
        }

        private static string CaptureSimulacrum(RunSave save, InfiniteTowerRun run)
        {
            var simulacrum = new SimulacrumSave
            {
                WaveIndex = run.waveIndex,
                Rngs = GameFields.SimulacrumRngNames
                    .Select(name => GameFields.ReadRng(run, name))
                    .Where(rng => rng != null)
                    .Select(rng => rng!)
                    .ToList(),
            };
            if (GameFields.SimulacrumPatternIndex?.GetValue(run) is int pattern)
            {
                simulacrum.EnemyItemPatternIndex = pattern;
            }

            if (GameFields.SimulacrumEnemyInventory?.GetValue(run) is Inventory enemies && enemies)
            {
                simulacrum.EnemyInventory = CaptureInventory(enemies);
            }

            save.Simulacrum = simulacrum;
            return $"wave {simulacrum.WaveIndex}, {simulacrum.EnemyInventory.Permanent.Count} enemy item kinds";
        }

        private string CapturePlayer(RunSave save, PlayerCharacterMasterController player)
        {
            NetworkUser user = player.networkUser;
            CharacterMaster master = player.master;
            if (!user || !master)
            {
                return "skipped a player with no network user or master (disconnected?)";
            }

            NetworkUserId id = user.id;
            var p = new PlayerSave
            {
                IdValue = id.value,
                IdString = id.strValue ?? string.Empty,
                IdSubId = id.subId,
                DisplayName = user.userName,
                Body = master.bodyPrefab ? BodyCatalog.GetBodyName(BodyCatalog.FindBodyIndex(master.bodyPrefab)) : string.Empty,
                Loadout = master.loadout?.ToXml("Loadout"),
                Money = master.money,
                VoidCoins = master.voidCoins,
                Inventory = master.inventory ? CaptureInventory(master.inventory) : new InventorySave(),
            };
            CaptureMinions(p, master);
            CaptureDevotionInventory(p, master);
            save.Players.Add(p);
            int skillDrones = p.Minions.Count(m => m.SkillSlot.Length > 0);
            int lemurians = p.Minions.Count(m => m.DevotionItem.Length > 0);
            return $"{p.DisplayName} as {p.Body}: {p.Inventory.Permanent.Values.Sum()} items, "
                + $"{p.Inventory.Temporary.Count} temporary, {p.Inventory.Equipment.Count} equipment, "
                + $"${p.Money}, {p.Minions.Count} minions ({skillDrones} from skills, {lemurians} Devotion lemurians)";
        }

        // Minions that carry over between stages are the ones kept DontDestroyOnLoad with their owner
        // Devotion lemurians are told apart by an initialized DevotedLemurianController, since the egg never
        // sets CharacterMaster.isDevotedMinion
        private void CaptureMinions(PlayerSave p, CharacterMaster owner)
        {
            foreach (CharacterMaster minion in CharacterMaster.readOnlyInstancesList)
            {
                if (!minion || minion == owner || minion.playerCharacterMasterController
                    || !minion.minionOwnership || minion.minionOwnership.ownerMaster != owner)
                {
                    continue;
                }

                DroneRepairMaster repair = minion.GetComponent<DroneRepairMaster>();
                DevotedLemurianController lemurian = minion.GetComponent<DevotedLemurianController>();
                bool devoted = lemurian && lemurian.DevotionItem != ItemIndex.None;
                bool kept = Util.IsDontDestroyOnLoad(minion.gameObject);

                // Timed summons such as Goobo Jr.'s gummy clones, restoring one would make it permanent
                bool temporary = minion.GetComponent<MasterSuicideOnTimer>();
                string devotion = devoted ? $"{ItemName(lemurian.DevotionItem)} level {lemurian.DevotedEvolutionLevel}" : "-";
                log.LogInfo(
                    $"[Save]   minion {MasterCatalog.GetMasterName(minion.masterIndex)}: carried over {kept}, "
                    + $"skill slot \"{(repair ? repair.requestedBySlotName : "-")}\", body alive {(bool)minion.GetBody()}, "
                    + $"devotion {devotion}, temporary {temporary}");
                if (!kept || temporary)
                {
                    continue;
                }

                p.Minions.Add(new MinionSave
                {
                    Master = MasterCatalog.GetMasterName(minion.masterIndex),
                    SkillSlot = repair ? repair.requestedBySlotName ?? string.Empty : string.Empty,
                    DevotionItem = devoted ? ItemName(lemurian.DevotionItem) : string.Empty,
                    DevotionLevel = devoted ? lemurian.DevotedEvolutionLevel : 0,
                    Body = minion.bodyPrefab ? BodyCatalog.GetBodyName(BodyCatalog.FindBodyIndex(minion.bodyPrefab)) : string.Empty,
                    Inventory = minion.inventory ? CaptureInventory(minion.inventory) : new InventorySave(),
                });
            }
        }

        private static void CaptureDevotionInventory(PlayerSave p, CharacterMaster owner)
        {
            DevotionInventoryController? controller = UnityEngine.Object.FindObjectsOfType<DevotionInventoryController>()
                .FirstOrDefault(c => c.SummonerMaster == owner);
            Inventory? shared = controller ? controller!.GetComponent<Inventory>() : null;
            if (shared)
            {
                p.DevotionInventory = CaptureInventory(shared!);
            }
        }

        internal static InventorySave CaptureInventory(Inventory inventory)
        {
            var inv = new InventorySave();
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                var item = (ItemIndex)i;
                string name = ItemName(item);
                if (name.Length == 0)
                {
                    continue;
                }

                AddIfPositive(inv.Permanent, name, inventory.GetItemCountPermanent(item));
                AddIfPositive(inv.Rented, name, inventory.GetItemCountRented(item));
                float raw = inventory.GetTempItemRawValue(item);
                if (raw > 0f)
                {
                    inv.Temporary[name] = raw;
                }
            }

            CaptureEquipment(inv, inventory);
            KeyItemIndex keyItem = inventory.GetKeyItemIndex();
            KeyItemDef? keyDef = keyItem != KeyItemIndex.None ? KeyItemCatalog.GetKeyItemDef(keyItem) : null;
            inv.KeyItem = keyDef ? keyDef!.name : string.Empty;
            inv.InfusionBonus = inventory.infusionBonus;
            inv.AcquisitionOrder = inventory.itemAcquisitionOrder.Select(ItemName).Where(n => n.Length > 0).ToList();
            return inv;
        }

        private static void CaptureEquipment(InventorySave inv, Inventory inventory)
        {
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
            {
                for (uint set = 0; set < inventory.equipmentSetCount; set++)
                {
                    var location = new EquipmentLocation { slot = slot, set = set };
                    EquipmentState state = inventory.GetEquipment(in location);
                    if (state.equipmentIndex == EquipmentIndex.None)
                    {
                        continue;
                    }

                    float left = state.chargeFinishTime.timeUntil;
                    inv.Equipment.Add(new EquipmentSave
                    {
                        Slot = slot,
                        Set = set,
                        Name = EquipmentCatalog.GetEquipmentDef(state.equipmentIndex)?.name ?? string.Empty,
                        Charges = state.charges,
                        CooldownLeft = float.IsNaN(left) || float.IsInfinity(left) ? 0f : Mathf.Max(0f, left),
                    });
                }
            }

            EquipmentLocation active = inventory.activeEquipmentLocation;
            inv.ActiveSlot = active.slot;
            inv.ActiveSet = active.set;
        }

        private static void AddIfPositive(Dictionary<string, int> into, string name, int count)
        {
            if (count > 0)
            {
                into[name] = count;
            }
        }

        private static string ItemName(ItemIndex item)
        {
            ItemDef def = ItemCatalog.GetItemDef(item);
            return def ? def.name : string.Empty;
        }
    }
}
