using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.BuffInfo;
using JustBeBetter.ChestTweaks;
using JustBeBetter.Cooldowns;
using JustBeBetter.Hidden;
using JustBeBetter.ImmuneBar;
using JustBeBetter.InventorySort;
using JustBeBetter.ItemInfo;
using JustBeBetter.Loading;
using JustBeBetter.Lobby;
using JustBeBetter.Mouse;
using JustBeBetter.Notifications;
using JustBeBetter.Pickers;
using JustBeBetter.Safety;
using JustBeBetter.Saves;
using JustBeBetter.Scoreboard;
using JustBeBetter.Shared;
using JustBeBetter.SkillInfo;
using JustBeBetter.StatsDisplay;
using JustBeBetter.Sounds;

namespace JustBeBetter
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsSupport.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class JustBeBetterPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Just_Be_Better";
        public const string PluginName = "Just_Be_Better";
        public const string PluginVersion = "0.1.0";

        private readonly List<IFeature> features = new List<IFeature>();
        private readonly List<ManualLogSource> logs = new List<ManualLogSource>();

        private void Awake()
        {
            var settings = new Settings(Config);
            if (Chainloader.PluginInfos.ContainsKey(RiskOfOptionsSupport.PluginGuid))
            {
                RiskOfOptionsSupport.Register(settings, Logger);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            StartPart(settings.Loading.Enabled, LoadingSettings.Section, log => new LoadingFeature(settings.Loading, gameObject, log));
            StartPart(settings.Safety.Enabled, SafetySettings.Section, log => new SafetyFeature(settings.Safety, log));
            // Started further down, the Scrapper only follows it once it's hooked
            InventorySorter? sorter = null;
            Func<SortRules?> inventoryRules = () => sorter != null && sorter.Hooked ? settings.InventorySort.Rules : (SortRules?)null;
            StartPart(
                settings.Pickers.Enabled,
                PickerSettings.Section,
                log => new PickerSearch(settings.Pickers, settings.ItemInfo, inventoryRules, log, new Favourites("Item Favourites", log)));
            var pings = new PingCards();
            var skipped = new SkippedCards();
            StartPart(settings.ItemInfo.Enabled, ItemInfoSettings.Section, log => new ItemInfoFeature(settings.ItemInfo, pings, skipped, log));
            StartPart(settings.Notifications.Enabled, NotificationSettings.Section, log => new PickupCards(settings.Notifications, pings, skipped, log));
            StartPart(settings.Hidden.Enabled, HiddenSettings.Section, log => new Unhider(settings.Hidden, log));
            StartPart(settings.InventorySort.Enabled, InventorySortSettings.Section, log => sorter = new InventorySorter(settings.InventorySort, log));
            StartPart(settings.SkillInfo.Enabled, SkillInfoSettings.Section, log => new SkillDetails(settings.SkillInfo, settings.ItemInfo, log));
            StartPart(settings.Cooldowns.Enabled, CooldownSettings.Section, log => new CooldownTimers(settings.Cooldowns, log));
            StartPart(settings.Buffs.Enabled, BuffInfoSettings.Section, log => new BuffDetails(settings.Buffs, log));
            StartPart(settings.Stats.Enabled, StatsDisplaySettings.Section, log => new StatsPanel(settings.Stats, log));
            // Enabled is read at launch, so Unhide Items only counts while its part is running
            bool unhiding = settings.Hidden.Enabled.Value;
            StartPart(
                settings.Scoreboard.Enabled,
                ScoreboardSettings.Section,
                log => new ItemCounters(settings.Scoreboard, () => unhiding && settings.Hidden.UnhideItems.Value, log));
            StartPart(settings.ImmuneBar.Enabled, ImmuneBarSettings.Section, log => new ImmuneHealthBar(settings.ImmuneBar, log));
            StartPart(settings.Chests.Enabled, ChestTweaksSettings.Section, log => new ChestTweaksFeature(settings.Chests, log));
            StartPart(settings.LobbyEnabled, Settings.LobbySection, log => new LobbyFeature(settings.RepeatSurvivors, log));
            StartPart(settings.SavesEnabled, Settings.SavesSection, log => new SavesFeature(gameObject, log));
            StartPart(settings.Cursor.Enabled, CursorSettings.Section, log => new CursorRules(settings.Cursor, log));
            StartPart(settings.Sounds.Enabled, SoundSettings.Section, log => new EquipmentSounds(settings.Sounds, log));
        }

        private void OnDestroy()
        {
            for (int i = features.Count - 1; i >= 0; i--)
            {
                TryDisable(features[i], Logger);
            }

            features.Clear();
            foreach (ManualLogSource log in logs)
            {
                log.Dispose();
            }

            logs.Clear();
        }

        // Each part logs under its own name, and one that fails to start leaves the others running
        private void StartPart(ConfigEntry<bool> enabled, string name, Func<ManualLogSource, IFeature> create)
        {
            if (!enabled.Value)
            {
                Logger.LogInfo($"{name} is off");
                return;
            }

            ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource($"{PluginName} {name}");
            logs.Add(log);
            IFeature? feature = null;
            try
            {
                feature = create(log);
                feature.Enable();
                features.Add(feature);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't start, so it stays off: {e}");

                // Takes back whatever hooks went in before the failure
                TryDisable(feature, log);
            }
        }

        private static void TryDisable(IFeature? feature, ManualLogSource log)
        {
            try
            {
                feature?.Disable();
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't fully switch {feature?.GetType().Name} off: {e.Message}");
            }
        }
    }
}
