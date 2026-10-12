using System;
using BepInEx.Logging;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace CommandGrace
{
    // Freezes the world while the only player has a picker open
    internal sealed class SoloPause
    {
        private readonly Settings settings;
        private readonly PickerTracker pickers;
        private readonly ManualLogSource log;

        // Our freeze and the pause menu's are tracked apart, since the menu saves Time.timeScale when it
        // takes hold and restores it on release; it must never save our 0
        private bool frozen;
        private bool menuHoldsTime;
        private float savedTimeScale = 1f;

        internal SoloPause(Settings settings, PickerTracker pickers, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.pickers = pickers ?? throw new ArgumentNullException(nameof(pickers));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            pickers.Opened += OnPickerOpened;
            pickers.Closed += OnPickerClosed;
            Run.onRunStartGlobal += OnRunStart;
            Run.onRunDestroyGlobal += OnRunDestroy;
            On.RoR2.PauseStopController.ClientPauseStop += OnClientPauseStop;
        }

        internal void Disable()
        {
            pickers.Opened -= OnPickerOpened;
            pickers.Closed -= OnPickerClosed;
            Run.onRunStartGlobal -= OnRunStart;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            On.RoR2.PauseStopController.ClientPauseStop -= OnClientPauseStop;
            Unfreeze();
        }

        private static bool IsSolo()
        {
            return NetworkServer.active && PlayerCharacterMasterController.instances.Count == 1;
        }

        // The only place the pause menu changes Time.timeScale. In solo the menu's pause reaches here an
        // RPC later than PauseManager.isPaused and PauseStopController.isPaused turn true, so those flags
        // can't say whether the menu has saved the time scale yet
        private void OnClientPauseStop(
            On.RoR2.PauseStopController.orig_ClientPauseStop orig,
            PauseStopController self,
            bool shouldPause)
        {
            if (shouldPause && frozen)
            {
                // Let the menu save the real speed rather than our 0; it sets 0 itself right after
                Time.timeScale = savedTimeScale;
            }

            orig(self, shouldPause);
            menuHoldsTime = shouldPause;

            if (!shouldPause && frozen)
            {
                Time.timeScale = 0f;
            }
        }

        private void OnPickerOpened(CharacterMaster master)
        {
            if (frozen || menuHoldsTime || settings.SoloMode.Value != SoloMode.Pause || !IsSolo())
            {
                return;
            }

            // Something else already has the world stopped; leave it alone
            if (Time.timeScale <= 0f)
            {
                return;
            }

            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            frozen = true;
            log.LogInfo("World paused while picking");
        }

        private void OnPickerClosed(CharacterMaster master)
        {
            if (pickers.OpenCount == 0)
            {
                Unfreeze();
            }
        }

        // A run never starts frozen; catches anything that slipped past the rest
        private void OnRunStart(Run run)
        {
            frozen = false;
            menuHoldsTime = false;
            if (Time.timeScale <= 0f)
            {
                Time.timeScale = 1f;
                log.LogWarning("Run started with the world frozen; time restored");
            }
        }

        private void OnRunDestroy(Run run)
        {
            Unfreeze();
        }

        private void Unfreeze()
        {
            if (!frozen)
            {
                return;
            }

            frozen = false;

            // The menu already holds the real speed and gives it back when it closes
            if (menuHoldsTime)
            {
                log.LogInfo("Picker closed during the pause menu; the world resumes when the menu closes");
                return;
            }

            Time.timeScale = savedTimeScale;
            log.LogInfo("World resumed");
        }
    }
}
