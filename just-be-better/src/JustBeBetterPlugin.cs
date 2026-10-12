using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.Loading;
using JustBeBetter.Lobby;
using JustBeBetter.Mouse;
using JustBeBetter.Pickers;
using JustBeBetter.Safety;
using JustBeBetter.Saves;
using JustBeBetter.Shared;
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
                RiskOfOptionsSupport.Register(settings);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            Start(settings.Loading.Enabled, LoadingSettings.Section, log => new LoadingFeature(settings.Loading, gameObject, log));
            Start(settings.Safety.Enabled, SafetySettings.Section, log => new SafetyFeature(settings.Safety, log));
            Start(settings.PickersEnabled, Settings.PickersSection, log => new PickerSearch(log, new Favourites("Item Favourites", log)));
            Start(settings.LobbyEnabled, Settings.LobbySection, log => new LobbyFeature(settings.RepeatSurvivors, log));
            Start(settings.SavesEnabled, Settings.SavesSection, log => new SavesFeature(gameObject, log));
            Start(settings.Cursor.Enabled, CursorSettings.Section, log => new CursorRules(settings.Cursor, log));
            Start(settings.Sounds.Enabled, SoundSettings.Section, log => new EquipmentSounds(settings.Sounds, log));
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
        private void Start(ConfigEntry<bool> enabled, string name, Func<ManualLogSource, IFeature> create)
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
