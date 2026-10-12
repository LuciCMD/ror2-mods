using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;

namespace JustBeBetter.Saves.Ui
{
    // Saved Runs button beside Ready in the character select lobby, host only
    // Lists saves in the game's own dialog boxes
    // Continue arms the save and readies up, and the run starts from it
    internal sealed class SavesPanel
    {
        private const int MaxListed = 12;

        private readonly SaveSession session;
        private readonly ManualLogSource log;

        internal SavesPanel(SaveSession session, ManualLogSource log)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.UI.CharacterSelectController.OnEnable += OnCharacterSelectEnable;
            On.RoR2.UI.CharacterSelectController.ClientSetUnready += OnClientSetUnready;
        }

        internal void Disable()
        {
            On.RoR2.UI.CharacterSelectController.OnEnable -= OnCharacterSelectEnable;
            On.RoR2.UI.CharacterSelectController.ClientSetUnready -= OnClientSetUnready;
        }

        private void OnCharacterSelectEnable(
            On.RoR2.UI.CharacterSelectController.orig_OnEnable orig,
            CharacterSelectController self)
        {
            orig(self);
            try
            {
                if (NetworkServer.active)
                {
                    AddButton(self);
                    if (session.ReadyOnLobby)
                    {
                        session.ReadyOnLobby = false;

                        // The lobby's own setup finishes after OnEnable
                        RoR2Application.onNextUpdate += () => TryReadyForRetry(self);
                    }
                }
            }
            catch (Exception e)
            {
                log.LogError($"[Ui] Couldn't add the Saved Runs button: {e}");
            }
        }

        private void OnClientSetUnready(
            On.RoR2.UI.CharacterSelectController.orig_ClientSetUnready orig,
            CharacterSelectController self)
        {
            orig(self);
            session.Disarm();
        }

        private void AddButton(CharacterSelectController controller)
        {
            MPButton ready = controller.readyButton;
            if (!ready || ready.transform.parent.Find("Just_Be_Better Saved Runs"))
            {
                return;
            }

            MPButton button = ButtonCopy.Create(ready, "Just_Be_Better Saved Runs", "Saved Runs");
            button.transform.SetSiblingIndex(ready.transform.GetSiblingIndex());
            button.onClick.AddListener(() => ShowList(controller, button));
        }

        private void ShowList(CharacterSelectController controller, MPButton source)
        {
            MPEventSystem? eventSystem = source.GetComponentInParent<MPEventSystemLocator>()?.eventSystem;
            if (!eventSystem)
            {
                return;
            }

            List<RunSave> saves = session.Store.List();
            SimpleDialogBox dialog = SimpleDialogBox.Create(eventSystem);
            dialog.headerToken = UiText.Pair("Saved Runs");
            string description = saves.Count == 0
                ? "No saved runs yet. Use Save Run in the pause menu during a run."
                : saves.Count > MaxListed
                    ? $"Showing the {MaxListed} most recent of {saves.Count}. Pick one to continue, rename or delete it."
                    : "Pick a run to continue, rename or delete it.";
            dialog.descriptionToken = UiText.Pair(description);
            foreach (RunSave save in saves.Take(MaxListed))
            {
                RunSave chosen = save;
                dialog.AddActionButton(() => ShowDetails(controller, eventSystem!, chosen), UiText.Token(ListLabel(save)), true);
            }

            dialog.AddCancelButton(UiText.Token("Close"));
        }

        private void ShowDetails(CharacterSelectController controller, MPEventSystem eventSystem, RunSave save)
        {
            (List<string> missing, List<string> extra) = SaveSession.CompareLobby(save);
            bool lobbyMatches = missing.Count == 0 && extra.Count == 0;
            bool canContinue = save.Status == SaveStatus.Active && lobbyMatches;
            bool canRetry = save.Status == SaveStatus.Died && lobbyMatches;

            SimpleDialogBox dialog = SimpleDialogBox.Create(eventSystem);
            dialog.headerToken = UiText.Pair(save.Name);
            dialog.descriptionToken = UiText.Pair(Details(save, missing, extra));
            if (canContinue)
            {
                dialog.AddActionButton(() => Continue(controller, save), UiText.Token("Continue"), true);
            }

            if (canRetry)
            {
                dialog.AddActionButton(() => Continue(controller, save), UiText.Token($"Retry From Stage {save.StageNumber}"), true);
            }

            dialog.AddActionButton(() => Rename(controller, eventSystem, save), UiText.Token("Rename"), true);
            dialog.AddActionButton(() => ConfirmDelete(controller, eventSystem, save), UiText.Token("Delete"), true);
            dialog.AddActionButton(() => ShowList(controller, controller.readyButton), UiText.Token("Back"), true);
        }

        // Runs inside the game's update, where an escaping error would skip the other callbacks queued for the frame
        private void TryReadyForRetry(CharacterSelectController controller)
        {
            try
            {
                ReadyForRetry(controller);
            }
            catch (Exception e)
            {
                log.LogError($"[Load] Couldn't ready up for the retry: {e}");
            }
        }

        private void ReadyForRetry(CharacterSelectController controller)
        {
            RunSave? save = session.Armed;
            if (!controller || save == null)
            {
                return;
            }

            (List<string> missing, List<string> extra) = SaveSession.CompareLobby(save);
            if (missing.Count > 0 || extra.Count > 0)
            {
                Chat.AddMessage($"\"{save.Name}\" can't retry until the lobby has the same players as the save.");
                session.Disarm();
                return;
            }

            controller.ClientSetReady();
            Chat.AddMessage($"Retrying \"{save.Name}\" from stage {save.StageNumber}.");
        }

        // Also retries a run that ended in death, from the start of the last stage it reached
        private void Continue(CharacterSelectController controller, RunSave save)
        {
            save.Status = SaveStatus.Active;
            session.Arm(save);
            if (controller)
            {
                controller.ClientSetReady();
            }

            Chat.AddMessage($"Continuing \"{save.Name}\" once everyone is ready.");
        }

        private void Rename(CharacterSelectController controller, MPEventSystem eventSystem, RunSave save)
        {
            NamePrompt.Show(eventSystem, "Rename Run", "Only the name changes; the save stays as it is.", save.Name, "Rename", name =>
            {
                save.Name = name;
                if (session.Armed != null && session.Armed.Id == save.Id)
                {
                    session.Armed.Name = name;
                }

                if (session.Store.Write(save))
                {
                    log.LogInfo($"[Store] Renamed to \"{name}\"");
                }

                ShowDetails(controller, eventSystem, save);
            });
        }

        private void ConfirmDelete(CharacterSelectController controller, MPEventSystem eventSystem, RunSave save)
        {
            SimpleDialogBox dialog = SimpleDialogBox.Create(eventSystem);
            dialog.headerToken = UiText.Pair("Delete Run");
            dialog.descriptionToken = UiText.Pair($"Delete \"{save.Name}\"? This can't be undone.");
            dialog.AddActionButton(
                () =>
                {
                    if (session.Armed != null && session.Armed.Id == save.Id)
                    {
                        session.Disarm();
                    }

                    session.Store.Delete(save.Id);
                    log.LogInfo($"[Store] Deleted \"{save.Name}\"");
                    ShowList(controller, controller.readyButton);
                },
                UiText.Token("Delete"),
                true);
            dialog.AddActionButton(() => ShowDetails(controller, eventSystem, save), UiText.Token("Cancel"), true);
        }

        private static string ListLabel(RunSave save)
        {
            string status = save.Status == SaveStatus.Active ? string.Empty : $" ({save.Status})";
            return $"{save.Name}{status}: stage {save.StageNumber}";
        }

        private static string Details(RunSave save, List<string> missing, List<string> extra)
        {
            string players = string.Join(", ", save.Players.Select(p => $"{p.DisplayName} as {Survivor(p.Body)}"));
            string text = $"{ModeName(save.GameMode)}, stage {save.StageNumber}, {save.Stopwatch / 60f:0} min in. "
                + $"Players: {players}. Last saved {save.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}.";
            if (save.Status == SaveStatus.Won)
            {
                return text + " This run was won, so it can't be continued.";
            }

            if (save.Status == SaveStatus.Died)
            {
                text += $" This run ended in death on stage {save.StageNumber}. Retry starts it again from the start of that stage.";
            }

            if (missing.Count > 0)
            {
                text += $" Waiting for {string.Join(", ", missing)} to join.";
            }

            if (extra.Count > 0)
            {
                text += $" {string.Join(", ", extra)} {(extra.Count == 1 ? "isn't" : "aren't")} in this run.";
            }

            return text;
        }

        private static string Survivor(string body)
        {
            return body.EndsWith("Body", StringComparison.Ordinal) ? body.Substring(0, body.Length - 4) : body;
        }

        private static string ModeName(string mode)
        {
            return mode switch
            {
                "ClassicRun" => "Classic",
                "EclipseRun" => "Eclipse",
                "InfiniteTowerRun" => "Simulacrum",
                _ => mode,
            };
        }
    }
}
