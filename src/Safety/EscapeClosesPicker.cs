using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using RoR2;
using UnityEngine;

namespace JustBeBetter.Safety
{
    // Escape runs the "pause" console command (MPEventSystem.Update, action 25)
    // While a picker is shown it closes the picker instead, the way its Cancel button does
    // Local UI, so it runs on every machine
    internal sealed class EscapeClosesPicker
    {
        // Each picker sends "cancelled" to the server when its panel is destroyed (OnPanelDestroyed)
        // Their KillPanel destroys the whole interactable instead, so it must not be used here
        private static readonly FieldInfo?[] PanelFields =
        {
            typeof(PickupPickerController).GetField("panelInstance", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(TieredPickupPickerController).GetField("panelInstance", BindingFlags.Instance | BindingFlags.NonPublic),
            typeof(DroneScrapperPickerController).GetField("panelInstance", BindingFlags.Instance | BindingFlags.NonPublic),
        };

        private readonly HashSet<NetworkUIPromptController> shownPickers = new HashSet<NetworkUIPromptController>();
        private readonly ManualLogSource log;

        // LookingGlass can close the picker on the same key press before the pause command runs
        private int lastPickerClosedFrame = -10;

        internal EscapeClosesPicker(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            if (Array.Exists(PanelFields, field => field == null))
            {
                log.LogWarning("A picker's panelInstance field wasn't found; Escape may not close that picker");
            }

            On.RoR2.NetworkUIPromptController.OnControlBegin += OnControlBegin;
            On.RoR2.NetworkUIPromptController.OnControlEnd += OnControlEnd;
            On.RoR2.PauseManager.CCTogglePause += OnTogglePause;
        }

        internal void Disable()
        {
            On.RoR2.NetworkUIPromptController.OnControlBegin -= OnControlBegin;
            On.RoR2.NetworkUIPromptController.OnControlEnd -= OnControlEnd;
            On.RoR2.PauseManager.CCTogglePause -= OnTogglePause;
            shownPickers.Clear();
        }

        private void OnControlBegin(
            On.RoR2.NetworkUIPromptController.orig_OnControlBegin orig,
            NetworkUIPromptController self)
        {
            orig(self);
            if (self && self.isDisplaying && PickerTypes.IsPicker(self))
            {
                shownPickers.Add(self);
            }
        }

        private void OnControlEnd(
            On.RoR2.NetworkUIPromptController.orig_OnControlEnd orig,
            NetworkUIPromptController self)
        {
            if (shownPickers.Remove(self))
            {
                lastPickerClosedFrame = Time.frameCount;
            }

            orig(self);
        }

        private void OnTogglePause(On.RoR2.PauseManager.orig_CCTogglePause orig, ConCommandArgs args)
        {
            // Escape still closes an open pause menu
            if (PauseManager.PauseScreenIsOpen())
            {
                orig(args);
                return;
            }

            shownPickers.RemoveWhere(prompt => !prompt);
            bool closedJustNow = Time.frameCount - lastPickerClosedFrame <= 1;
            if (shownPickers.Count == 0 && !closedJustNow)
            {
                orig(args);
                return;
            }

            try
            {
                foreach (NetworkUIPromptController prompt in shownPickers)
                {
                    ClosePanel(prompt);
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't close the picker on Escape: {e}");
            }
        }

        private static void ClosePanel(NetworkUIPromptController prompt)
        {
            foreach (FieldInfo? field in PanelFields)
            {
                Component? picker = field == null ? null : prompt.GetComponent(field.DeclaringType);
                if (picker && field!.GetValue(picker) is GameObject panel && panel)
                {
                    UnityEngine.Object.Destroy(panel);
                }
            }
        }
    }
}
