using System;
using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;
using UnityEngine;

namespace JustBeBetter.Loading
{
    // Speeds up the effect scan, skips the splash and intro, measures startup and writes a report once the main menu shows, then times each
    // stage load
    internal sealed class LoadingFeature : IFeature
    {
        private readonly LoadingSettings settings;
        private readonly GameObject host;
        private readonly ManualLogSource log;
        private StartupHooks? hooks;
        private IntroSkip? introSkip;
        private FreezeWatcher? freezeWatcher;
        private StageWatcher? stageWatcher;
        private bool reported;

        internal LoadingFeature(LoadingSettings settings, GameObject host, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.host = host ? host : throw new ArgumentNullException(nameof(host));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            if (settings.FasterEffectScan.Value)
            {
                EffectScan.Apply(log);
            }

            hooks = new StartupHooks(log, OnMainMenu);
            hooks.Enable();
            introSkip = new IntroSkip(settings, log);
            introSkip.Enable();
            freezeWatcher = host.AddComponent<FreezeWatcher>();
            log.LogInfo("Timing startup; the report follows once the main menu shows");
        }

        public void Disable()
        {
            hooks?.Disable();
            introSkip?.Disable();
            if (freezeWatcher)
            {
                UnityEngine.Object.Destroy(freezeWatcher);
            }

            if (stageWatcher)
            {
                UnityEngine.Object.Destroy(stageWatcher);
            }
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
                try
                {
                    StartupReport.Write(log);
                }
                catch (Exception e)
                {
                    log.LogError($"Couldn't write the startup report: {e}");
                }

                hooks?.Disable();
                introSkip?.Disable();
                DisposePatcherHooks();

                // Bundle timing stops and the bundle cache starts making its copies
                (AppDomain.CurrentDomain.GetData(PatcherKeys.MainMenu) as Action)?.Invoke();
                CacheNotice.ShowIfBuilding();
                Timeline.Freezes.Clear();
                stageWatcher = host.AddComponent<StageWatcher>();
                stageWatcher.Init(log);
                log.LogInfo("Startup report done; timing stage loads from here");
            };
        }

        private static void DisposePatcherHooks()
        {
            if (AppDomain.CurrentDomain.GetData(PatcherKeys.Hooks) is not List<IDisposable> patcherHooks)
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
