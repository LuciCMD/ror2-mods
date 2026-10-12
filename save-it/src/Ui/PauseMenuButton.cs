using System;
using System.Linq;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace SaveIt.Ui
{
    // A Save Run button in the pause menu, host only. A saved run then autosaves at every stage start
    internal sealed class PauseMenuButton
    {
        private readonly SaveSession session;
        private readonly ManualLogSource log;

        internal PauseMenuButton(SaveSession session, ManualLogSource log)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.UI.PauseScreenController.OnEnable += OnPauseScreenEnable;
        }

        internal void Disable()
        {
            On.RoR2.UI.PauseScreenController.OnEnable -= OnPauseScreenEnable;
        }

        private void OnPauseScreenEnable(On.RoR2.UI.PauseScreenController.orig_OnEnable orig, PauseScreenController self)
        {
            orig(self);
            try
            {
                if (NetworkServer.active && Run.instance)
                {
                    AddButton(self);
                }
            }
            catch (Exception e)
            {
                log.LogError($"[Ui] Couldn't add the Save Run button: {e}");
            }
        }

        private void AddButton(PauseScreenController screen)
        {
            if (!screen.mainPanel || screen.mainPanel.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SaveItSaveRun"))
            {
                return;
            }

            MPButton? template = screen.mainPanel.GetComponentsInChildren<MPButton>(true).FirstOrDefault(b => b.gameObject.activeSelf);
            if (!template)
            {
                log.LogWarning("[Ui] No button in the pause menu to copy; Save Run not added");
                return;
            }

            MPButton button = ButtonCopy.Create(template!, "SaveItSaveRun", "Save Run");
            button.transform.SetSiblingIndex(template!.transform.GetSiblingIndex() + 1);
            Refresh(button);
            button.onClick.AddListener(() => OnClick(button));
        }

        private void Refresh(MPButton button)
        {
            if (session.BoundName != null)
            {
                UiText.SetLabel(button, $"Autosaving: {session.BoundName}");
                button.interactable = false;
                return;
            }

            string? unsupported = Run.instance ? SaveSession.UnsupportedMode(Run.instance) : null;
            if (unsupported != null)
            {
                UiText.SetLabel(button, $"No Saving in {unsupported}");
                button.interactable = false;
                return;
            }

            UiText.SetLabel(button, "Save Run");
            button.interactable = session.Latest != null;
        }

        private void OnClick(MPButton button)
        {
            MPEventSystem? eventSystem = button.GetComponentInParent<MPEventSystemLocator>()?.eventSystem;
            if (!eventSystem || session.Latest == null)
            {
                return;
            }

            NamePrompt.Show(
                eventSystem!,
                "Name This Run",
                "Saves the run as it was at the start of this stage. After that it saves itself at every new stage.",
                DefaultName(session.Latest),
                "Save",
                name =>
                {
                    bool saved = session.SaveCurrentAs(name);
                    Chat.AddMessage(saved ? $"Run saved as \"{name}\"." : "Couldn't save the run; see the log.");
                    if (button)
                    {
                        Refresh(button);
                    }
                });
        }

        private static string DefaultName(RunSave latest)
        {
            string survivor = latest.Players.FirstOrDefault()?.Body.Replace("Body", string.Empty) ?? "Run";
            return $"{survivor} {DateTime.Now:yyyy-MM-dd HH.mm}";
        }
    }
}
