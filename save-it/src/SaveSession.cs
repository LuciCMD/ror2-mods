using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RoR2;
using SaveIt.Ui;
using UnityEngine.Networking;

namespace SaveIt
{
    // Ties saving and restoring to the run's lifecycle on the server (host). Clients do nothing
    internal sealed class SaveSession
    {
        private readonly SaveStore store;
        private readonly Capture capture;
        private readonly Restore restore;
        private readonly ManualLogSource log;

        private readonly Dictionary<CharacterMaster, PlayerSave> minionsToRestore = new Dictionary<CharacterMaster, PlayerSave>();
        private readonly List<(CharacterMaster owner, MinionSave drone, string ownerName)> skillDronesToRestore =
            new List<(CharacterMaster owner, MinionSave drone, string ownerName)>();

        // Taken where the game generates each stage's random generators, so a restore replays the same stage
        private List<RngSave> stageRngs = new List<RngSave>();

        private RunSave? armed;
        private RunSave? restoring;
        private bool forceFirstScene;
        private string? boundId;
        private string? boundName;
        private int latestFailedAreas;

        internal SaveSession(SaveStore store, ManualLogSource log)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            capture = new Capture(log);
            restore = new Restore(log);
        }

        // Snapshot from the start of the current stage; what Save Run writes
        internal RunSave? Latest { get; private set; }

        internal string? BoundName => boundName;

        internal RunSave? Armed => armed;

        internal SaveStore Store => store;

        // Set by Retry on the game-over screen, so the lobby readies up as soon as it opens
        internal bool ReadyOnLobby { get; set; }

        // As on disk, so a run already marked Died or Won shows that
        internal RunSave? BoundSave()
        {
            return boundId != null ? store.Load(boundId) : null;
        }

        // Eclipse and Simulacrum aren't saved for now; the mode's name, or null when the run can be saved
        internal static string? UnsupportedMode(Run run)
        {
            return run is EclipseRun ? "Eclipse" : run is InfiniteTowerRun ? "Simulacrum" : null;
        }

        internal void Enable()
        {
            On.RoR2.NetworkSession.BeginRun += OnBeginRun;
            On.RoR2.Run.PickNextStageScene += OnPickNextStageScene;
            On.RoR2.Run.GenerateStageRNG += OnGenerateStageRng;
            On.RoR2.DroneTechRepairQueue.SummonNewMasterDrone += OnSummonSkillDrone;
            Run.onRunStartGlobal += OnRunStart;
            Run.onPlayerFirstCreatedServer += OnPlayerFirstCreated;
            Stage.onServerStageBegin += OnServerStageBegin;
            CharacterBody.onBodyStartGlobal += OnBodyStart;
            Run.onServerGameOver += OnServerGameOver;
            Run.onRunDestroyGlobal += OnRunDestroy;
        }

        internal void Disable()
        {
            On.RoR2.NetworkSession.BeginRun -= OnBeginRun;
            On.RoR2.Run.PickNextStageScene -= OnPickNextStageScene;
            On.RoR2.Run.GenerateStageRNG -= OnGenerateStageRng;
            On.RoR2.DroneTechRepairQueue.SummonNewMasterDrone -= OnSummonSkillDrone;
            Run.onRunStartGlobal -= OnRunStart;
            Run.onPlayerFirstCreatedServer -= OnPlayerFirstCreated;
            Stage.onServerStageBegin -= OnServerStageBegin;
            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            Run.onServerGameOver -= OnServerGameOver;
            Run.onRunDestroyGlobal -= OnRunDestroy;
        }

        internal bool SaveCurrentAs(string name)
        {
            if (Latest == null || !NetworkServer.active)
            {
                return false;
            }

            Latest.Id = Guid.NewGuid().ToString("N");
            Latest.Name = name;
            Latest.Status = SaveStatus.Active;
            Latest.SavedUtc = DateTime.UtcNow;
            if (!store.Write(Latest))
            {
                SaveIndicator.Post($"Couldn't save \"{name}\"; see the log", SaveNotice.Failed);
                return false;
            }

            PostSaved(Latest);
            boundId = Latest.Id;
            boundName = name;
            log.LogInfo($"[Save] Saved as \"{name}\" ({SaveStore.Folder}); autosaves at every stage start from now on");
            return true;
        }

        internal void Arm(RunSave save)
        {
            armed = save;
            log.LogInfo($"[Load] \"{save.Name}\" will continue when the run starts");
        }

        internal void Disarm()
        {
            if (armed != null)
            {
                log.LogInfo($"[Load] \"{armed.Name}\" no longer continues");
                armed = null;
            }
        }

        // Saved players not in the lobby, and lobby players not in the save
        internal static (List<string> missing, List<string> extra) CompareLobby(RunSave save)
        {
            var users = NetworkUser.readOnlyInstancesList.Where(user => user).ToList();
            var missing = save.Players
                .Where(p => !users.Any(user => Matches(user.id, p)))
                .Select(p => p.DisplayName)
                .ToList();
            var extra = users
                .Where(user => !save.Players.Any(p => Matches(user.id, p)))
                .Select(user => user.userName)
                .ToList();
            return (missing, extra);
        }

        private static bool Matches(NetworkUserId id, PlayerSave p)
        {
            return id.value == p.IdValue && (id.strValue ?? string.Empty) == p.IdString && id.subId == p.IdSubId;
        }

        private PlayerSave? SavedPlayerFor(NetworkUser? user)
        {
            return user && restoring != null ? restoring.Players.FirstOrDefault(p => Matches(user!.id, p)) : null;
        }

        private Run OnBeginRun(
            On.RoR2.NetworkSession.orig_BeginRun orig,
            NetworkSession self,
            Run runPrefabComponent,
            RuleBook ruleBook,
            ulong seed)
        {
            if (armed == null || !NetworkServer.active)
            {
                return orig(self, runPrefabComponent, ruleBook, seed);
            }

            RunSave save = armed;
            armed = null;
            restoring = save;
            forceFirstScene = true;
            Run prefab = runPrefabComponent;
            RuleBook rules = ruleBook;
            try
            {
                Run? savedMode = GameModeCatalog.FindGameModePrefabComponent(save.GameMode);
                prefab = savedMode ? savedMode! : runPrefabComponent;
                var savedRules = new RuleBook();
                if (save.RuleBook != null && RuleBook.FromXml(save.RuleBook, ref savedRules))
                {
                    rules = savedRules;
                }

                SetBodyPreferences(save);
                log.LogInfo($"[Load] Continuing \"{save.Name}\": {save.GameMode}, stage {save.StageNumber} ({save.Scene})");
            }
            catch (Exception e)
            {
                log.LogError($"[Load] Couldn't prepare \"{save.Name}\"; game mode, rules or survivors may be the lobby's: {e}");
            }

            return orig(self, prefab, rules, save.Seed);
        }

        // Set before the masters link to their users, so the normal path spawns the saved survivor
        private void SetBodyPreferences(RunSave save)
        {
            foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
            {
                PlayerSave? p = save.Players.FirstOrDefault(saved => user && Matches(user.id, saved));
                BodyIndex body = p != null ? BodyCatalog.FindBodyIndex(p.Body) : BodyIndex.None;
                if (body != BodyIndex.None)
                {
                    user.NetworkbodyIndexPreference = body;
                }
                else if (p != null)
                {
                    log.LogWarning($"[Load] Survivor {p.Body} for {p.DisplayName} isn't installed; they keep the lobby's choice");
                }
            }
        }

        private void OnPickNextStageScene(
            On.RoR2.Run.orig_PickNextStageScene orig,
            Run self,
            WeightedSelection<SceneDef> choices)
        {
            if (!forceFirstScene || restoring == null)
            {
                orig(self, choices);
                return;
            }

            forceFirstScene = false;
            SceneDef scene = SceneCatalog.FindSceneDef(restoring.Scene);
            if (!scene)
            {
                log.LogError($"[Load] Stage {restoring.Scene} isn't installed; starting on the run's own first stage instead");
                orig(self, choices);
                return;
            }

            self.nextStageScene = scene;
        }

        private void OnGenerateStageRng(On.RoR2.Run.orig_GenerateStageRNG orig, Run self)
        {
            orig(self);
            if (NetworkServer.active)
            {
                stageRngs = Capture.RunRngs(self);
            }
        }

        private void OnRunStart(Run run)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (restoring != null)
            {
                restore.ApplyRun(run, restoring);
                boundId = restoring.Id;
                boundName = restoring.Name;

                // A retried save is Died on disk until the next autosave; it's in play again now
                store.Write(restoring);
            }

            // Stage 1's generators: Run.Start calls OnLoopBeginServer after GenerateStageRNG
            stageRngs = Capture.RunRngs(run);
            string? unsupported = UnsupportedMode(run);
            if (unsupported != null)
            {
                log.LogInfo($"[Save] {unsupported} runs aren't saved; Save Run stays off for this run");
            }
        }

        private void OnPlayerFirstCreated(Run run, PlayerCharacterMasterController player)
        {
            if (restoring == null || !NetworkServer.active || !player)
            {
                return;
            }

            PlayerSave? saved = SavedPlayerFor(player.networkUser);
            if (saved == null)
            {
                string name = player.networkUser ? player.networkUser.userName : "a player";
                log.LogWarning($"[Load] No saved data for {name}; they start fresh");
                return;
            }

            restore.ApplyPlayerMaster(player.master, saved);
            if (saved.Minions.Count > 0 || saved.DevotionInventory != null)
            {
                minionsToRestore[player.master] = saved;
            }

            foreach (MinionSave drone in saved.Minions.Where(m => m.SkillSlot.Length > 0))
            {
                skillDronesToRestore.Add((player.master, drone, saved.DisplayName));
            }
        }

        private void OnServerStageBegin(Stage stage)
        {
            Run run = Run.instance;
            if (!run)
            {
                return;
            }

            // The restored stage keeps the save as it is; capturing now would miss the minions, which only
            // come back once their owners' bodies spawn
            if (restoring != null)
            {
                restore.ApplyStageStart(restoring);
                Latest = restoring;
                latestFailedAreas = 0;
                restoring = null;
                return;
            }

            if (UnsupportedMode(run) != null)
            {
                return;
            }

            Latest = capture.Snapshot(run, stage, stageRngs);
            latestFailedAreas = capture.FailedAreas;
            if (boundId == null || boundName == null)
            {
                return;
            }

            Latest.Id = boundId;
            Latest.Name = boundName;
            Latest.SavedUtc = DateTime.UtcNow;
            if (store.Write(Latest))
            {
                log.LogInfo($"[Save] Autosaved \"{boundName}\" at stage {Latest.StageNumber}");
                PostSaved(Latest);
            }
            else
            {
                SaveIndicator.Post($"Couldn't save \"{boundName}\"; see the log", SaveNotice.Failed);
            }
        }

        private void PostSaved(RunSave save)
        {
            string saved = $"Saved \"{save.Name}\" at stage {save.StageNumber}";
            if (latestFailedAreas == 0)
            {
                SaveIndicator.Post(saved, SaveNotice.Saved);
                return;
            }

            string parts = latestFailedAreas == 1 ? "1 part" : $"{latestFailedAreas} parts";
            SaveIndicator.Post($"{saved}, but {parts} couldn't be saved; see the log", SaveNotice.Partial);
        }

        private void OnBodyStart(CharacterBody body)
        {
            if (!NetworkServer.active || !body || !body.master)
            {
                return;
            }

            if (minionsToRestore.TryGetValue(body.master, out PlayerSave saved))
            {
                minionsToRestore.Remove(body.master);
                restore.SpawnMinions(body, saved);
            }
        }

        // The slot name is only set after MasterSummon.Perform returns, so body start is too early to match on
        private DroneRepairMaster OnSummonSkillDrone(
            On.RoR2.DroneTechRepairQueue.orig_SummonNewMasterDrone orig,
            DroneTechRepairQueue self,
            UnityEngine.GameObject masterPrefab,
            string slotName)
        {
            DroneRepairMaster repair = orig(self, masterPrefab, slotName);

            // Enemy Operators come through here too, so only a load waiting on skill drones looks
            if (!NetworkServer.active || !repair || skillDronesToRestore.Count == 0)
            {
                return repair;
            }

            // DroneRepairMaster sits on the drone's master, whose droneMaster and ownerMaster fields are only
            // filled a frame later; the owner is the body whose queue asked for the drone
            CharacterMaster drone = repair.GetComponent<CharacterMaster>();
            CharacterBody ownerBody = self.GetComponent<CharacterBody>();
            CharacterMaster? owner = ownerBody ? ownerBody.master : null;
            string droneType = drone ? MasterCatalog.GetMasterName(drone.masterIndex) : "?";
            log.LogInfo($"[Load] Skill drone rebuilt by the game: {droneType} for slot \"{slotName}\" of {(owner ? owner!.name : "?")}");
            if (!drone)
            {
                return repair;
            }

            // Saves made before slot names were recorded only know the drone's type
            int match = skillDronesToRestore.FindIndex(entry => entry.owner == owner && entry.drone.SkillSlot == slotName);
            if (match < 0)
            {
                match = skillDronesToRestore.FindIndex(entry => entry.owner == owner && entry.drone.Master == droneType);
            }
            if (match >= 0)
            {
                (CharacterMaster _, MinionSave saved, string ownerName) = skillDronesToRestore[match];
                skillDronesToRestore.RemoveAt(match);
                restore.ApplySkillDrone(drone, saved, ownerName);
            }

            return repair;
        }

        private void OnServerGameOver(Run run, GameEndingDef ending)
        {
            if (boundId == null)
            {
                return;
            }

            RunSave? save = store.Load(boundId);
            if (save == null)
            {
                return;
            }

            save.Status = ending && ending.isWin ? SaveStatus.Won : SaveStatus.Died;
            save.SavedUtc = DateTime.UtcNow;
            if (store.Write(save))
            {
                log.LogInfo($"[Save] \"{save.Name}\" marked {save.Status} at stage {save.StageNumber}");
            }
        }

        private void OnRunDestroy(Run run)
        {
            Latest = null;
            restoring = null;
            forceFirstScene = false;
            boundId = null;
            boundName = null;
            minionsToRestore.Clear();
            skillDronesToRestore.Clear();
            stageRngs = new List<RngSave>();
        }
    }
}
