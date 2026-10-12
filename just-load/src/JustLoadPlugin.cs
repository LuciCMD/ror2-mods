using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using RoR2;

namespace JustLoad
{
    // Speeds up the effect scan, measures startup and writes a report once the main menu shows, then times each
    // stage load
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsSupport.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class JustLoadPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Just_Load";
        public const string PluginName = "Just_Load";
        public const string PluginVersion = "0.1.0";

        private const string PatcherHooksKey = "mephit.Just_Load.PatcherHooks";
        private const string PatcherMainMenuKey = "mephit.Just_Load.MainMenuReached";

        private StartupHooks? hooks;
        private bool reported;

        private void Awake()
        {
            var settings = new Settings(Config);
            if (Chainloader.PluginInfos.ContainsKey(RiskOfOptionsSupport.PluginGuid))
            {
                RiskOfOptionsSupport.Register(settings);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            if (settings.FasterEffectScan.Value)
            {
                EffectScan.Apply(Logger);
            }

            hooks = new StartupHooks(Logger, OnMainMenu);
            hooks.Enable();
            gameObject.AddComponent<FreezeWatcher>();
            Logger.LogInfo("Timing startup; the report follows once the main menu shows");
        }

        private void OnDestroy()
        {
            hooks?.Disable();
        }

        private void OnMainMenu()
        {
            if (reported)
            {
                return;
            }

            reported = true;

            // Unhooked a frame later, outside the hook that's calling this
            RoR2Application.onNextUpdate += () =>
            {
                StartupReport.Write(Logger);
                hooks?.Disable();
                DisposePatcherHooks();

                // Bundle timing stops and the bundle cache starts making its copies
                (AppDomain.CurrentDomain.GetData(PatcherMainMenuKey) as Action)?.Invoke();
                CacheNotice.ShowIfBuilding();
                Timeline.Freezes.Clear();
                gameObject.AddComponent<StageWatcher>().Init(Logger);
                Logger.LogInfo("Startup report done; timing stage loads from here");
            };
        }

        private static void DisposePatcherHooks()
        {
            if (AppDomain.CurrentDomain.GetData(PatcherHooksKey) is not List<IDisposable> patcherHooks)
            {
                return;
            }

            foreach (IDisposable hook in patcherHooks)
            {
                hook.Dispose();
            }

            patcherHooks.Clear();
        }
    }
}
