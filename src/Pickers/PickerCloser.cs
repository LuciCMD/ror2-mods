using System;
using BepInEx.Logging;
using JustBeBetter.Shared;
using Rewired;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Action = RewiredConsts.Action;

namespace JustBeBetter.Pickers
{
    // Lives on an item picker panel: moving, jumping or sprinting closes it, read through the player's own bindings
    // The game still reads those while a menu is open, it only ignores them for the body
    // Destroying the panel tells the server the pick was cancelled, as Escape and the Cancel button do
    internal sealed class PickerCloser : MonoBehaviour
    {
        private const float Pressed = 0.5f;

        private static readonly (int Id, string Name)[] Inputs =
        {
            (Action.MoveHorizontal, "Move"),
            (Action.MoveVertical, "Move"),
            (Action.Jump, "Jump"),
            (Action.Sprint, "Sprint"),
        };

        private readonly bool[] held = new bool[Inputs.Length];
        private PickerSettings? settings;
        private ManualLogSource? log;
        private LocalUser? user;
        private PickerFilter? filter;

        // Off for a key held when the picker opened, such as walking up to the cube, until it's let go
        private bool primed;
        private bool failed;

        internal static void Attach(PickupPickerPanel panel, PickerSettings settings, ManualLogSource log)
        {
            if (panel.TryGetComponent(out PickerCloser _))
            {
                return;
            }

            PickerCloser closer = panel.gameObject.AddComponent<PickerCloser>();
            closer.settings = settings;
            closer.log = log;
            HUD? hud = panel.GetComponentInParent<HUD>();
            closer.user = hud != null ? hud.localUserViewer : null;
            closer.user ??= LocalUserManager.GetFirstLocalUser();
            closer.filter = panel.GetComponent<PickerFilter>();
        }

        private void OnEnable()
        {
            primed = false;
        }

        private void Update()
        {
            if (failed)
            {
                return;
            }

            try
            {
                Check();
            }
            catch (Exception e)
            {
                failed = true;
                log?.LogError($"Couldn't check for movement to close the item picker, it stays open until closed: {e}");
            }
        }

        private void Check()
        {
            Player? player = user?.inputPlayer;
            if (player == null || settings == null)
            {
                return;
            }

            string? pressed = null;
            for (int i = 0; i < Inputs.Length; i++)
            {
                bool now = Mathf.Abs(player.GetAxisRaw(Inputs[i].Id)) >= Pressed || player.GetButton(Inputs[i].Id);
                if (now && !held[i] && primed)
                {
                    pressed ??= Inputs[i].Name;
                }

                held[i] = now;
            }

            primed = true;
            if (pressed == null || !settings.CloseOnMovement.Value || Busy())
            {
                return;
            }

            log?.Verbose($"Closed the item picker on {pressed}");
            Destroy(gameObject);
        }

        // Typing, the console, the pause menu, or a controller, where the stick moves around the picker
        private bool Busy()
        {
            MPEventSystem? events = user?.eventSystem;
            if (events != null && events.currentInputSource == MPEventSystem.InputSource.Gamepad)
            {
                return true;
            }

            return (filter != null && filter.IsTyping)
                || Typing(events)
                || Typing(EventSystem.current)
                || ConsoleWindow.instance
                || PauseManager.PauseScreenIsOpen();
        }

        private static bool Typing(EventSystem? events)
        {
            GameObject? selected = events != null ? events.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent(out TMP_InputField field) && field.isFocused;
        }
    }
}
