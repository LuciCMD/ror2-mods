using System;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine.Networking;

namespace SaveIt.Ui
{
    // Retry From Stage N beside Continue on the game-over screen, host only, for a saved run that ended in
    // death. It arms the save and casts the Continue vote; the lobby then readies up on its own
    internal sealed class GameOverRetry
    {
        private readonly SaveSession session;
        private readonly ManualLogSource log;

        internal GameOverRetry(SaveSession session, ManualLogSource log)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.GameOverController.GenerateReportScreen += OnGenerateReportScreen;
        }

        internal void Disable()
        {
            On.RoR2.GameOverController.GenerateReportScreen -= OnGenerateReportScreen;
        }

        private GameEndReportPanelController OnGenerateReportScreen(
            On.RoR2.GameOverController.orig_GenerateReportScreen orig,
            GameOverController self,
            HUD hud)
        {
            GameEndReportPanelController panel = orig(self, hud);
            try
            {
                if (NetworkServer.active && panel)
                {
                    AddButton(panel);
                }
            }
            catch (Exception e)
            {
                log.LogError($"[Ui] Couldn't add the Retry button: {e}");
            }

            return panel;
        }

        private void AddButton(GameEndReportPanelController panel)
        {
            RunSave? save = session.BoundSave();
            MPButton continueButton = panel.continueButton;
            if (save == null || save.Status != SaveStatus.Died || !continueButton)
            {
                return;
            }

            MPButton button = ButtonCopy.Create(continueButton, "SaveItRetry", $"Retry From Stage {save.StageNumber}");
            button.transform.SetSiblingIndex(continueButton.transform.GetSiblingIndex());
            button.onClick.AddListener(() =>
            {
                button.interactable = false;
                save.Status = SaveStatus.Active;
                session.Arm(save);
                session.ReadyOnLobby = true;
                if (continueButton)
                {
                    continueButton.onClick.Invoke();
                }
            });
        }
    }
}
