using System;
using BepInEx.Logging;
using JustBeBetter.Saves.Ui;
using UnityEngine;

namespace JustBeBetter.Saves
{
    // Named saves of runs, autosaved at each stage start and continued from the lobby
    // Only the host needs it
    internal sealed class SavesFeature : IFeature
    {
        private readonly GameObject host;
        private readonly ManualLogSource log;
        private readonly SaveSession session;
        private readonly PauseMenuButton pauseMenuButton;
        private readonly SavesPanel savesPanel;
        private readonly GameOverRetry gameOverRetry;
        private SaveIndicator? indicator;

        internal SavesFeature(GameObject host, ManualLogSource log)
        {
            this.host = host ? host : throw new ArgumentNullException(nameof(host));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            GameFields.ReportMissing(log);
            session = new SaveSession(new SaveStore(log), log);
            pauseMenuButton = new PauseMenuButton(session, log);
            savesPanel = new SavesPanel(session, log);
            gameOverRetry = new GameOverRetry(session, log);
        }

        public void Enable()
        {
            session.Enable();
            pauseMenuButton.Enable();
            savesPanel.Enable();
            gameOverRetry.Enable();
            indicator = host.AddComponent<SaveIndicator>();
            log.LogInfo($"Saves are kept in {SaveStore.Folder}");
        }

        public void Disable()
        {
            if (indicator)
            {
                UnityEngine.Object.Destroy(indicator);
            }

            gameOverRetry.Disable();
            savesPanel.Disable();
            pauseMenuButton.Disable();
            session.Disable();
        }
    }
}
