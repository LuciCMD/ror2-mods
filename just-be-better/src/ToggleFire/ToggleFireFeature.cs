using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace JustBeBetter.ToggleFire
{
    // Runs on each player's own game after it reads their buttons, so a latched primary reaches the body, and
    // the server, the same way a held one does
    internal sealed class ToggleFireFeature : IFeature
    {
        // The loading bar's blue, as the settings headings
        private static readonly Color MarkColour = new Color32(0x7F, 0xB8, 0xFF, 0xFF);

        private readonly ToggleFireSettings settings;
        private readonly ManualLogSource log;
        private readonly ConditionalWeakTable<PlayerCharacterMasterController, Player> players = new ConditionalWeakTable<PlayerCharacterMasterController, Player>();
        private readonly ConditionalWeakTable<SkillIcon, GameObject> marks = new ConditionalWeakTable<SkillIcon, GameObject>();
        private readonly List<GameObject> made = new List<GameObject>();
        private bool failed;
        private bool markFailed;

        internal ToggleFireFeature(ToggleFireSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            On.RoR2.PlayerCharacterMasterController.Update += OnUpdate;
            On.RoR2.PlayerCharacterMasterController.PollButtonInput += OnPollButtonInput;
            On.RoR2.UI.SkillIcon.LateUpdate += OnSkillIconLateUpdate;
        }

        public void Disable()
        {
            On.RoR2.PlayerCharacterMasterController.Update -= OnUpdate;
            On.RoR2.PlayerCharacterMasterController.PollButtonInput -= OnPollButtonInput;
            On.RoR2.UI.SkillIcon.LateUpdate -= OnSkillIconLateUpdate;
            foreach (GameObject mark in made)
            {
                if (mark)
                {
                    UnityEngine.Object.Destroy(mark);
                }
            }

            made.Clear();
        }

        // Every frame, as the fixed step stops while paused and a menu can open and close between two of them
        private void OnUpdate(On.RoR2.PlayerCharacterMasterController.orig_Update orig, PlayerCharacterMasterController self)
        {
            orig(self);
            if (failed || !players.TryGetValue(self, out Player player) || !player.Latch.Latched)
            {
                return;
            }

            try
            {
                string? why = MenuOpen(self);
                if (why != null)
                {
                    Stop(player, false, why);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void OnPollButtonInput(On.RoR2.PlayerCharacterMasterController.orig_PollButtonInput orig, PlayerCharacterMasterController self)
        {
            orig(self);
            if (failed)
            {
                return;
            }

            try
            {
                Tick(self);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void Tick(PlayerCharacterMasterController self)
        {
            CharacterBody? body = self.master ? self.master.GetBody() : null;
            InputBankTest? inputs = body ? body!.inputBank : null;
            if (body == null || inputs == null || !inputs)
            {
                if (players.TryGetValue(self, out Player gone))
                {
                    Stop(gone, false, "you died or left the stage");
                }

                return;
            }

            Player player = players.GetValue(self, _ => new Player());
            bool down = inputs.skill1.down;

            // By reference, as a destroyed body equals null in Unity and a stage change destroys it before the next spawns
            if (!ReferenceEquals(player.Body, body))
            {
                if (!ReferenceEquals(player.Body, null))
                {
                    Stop(player, down, "your body changed");
                }

                player.Body = body;
            }

            string? why = !body.healthComponent || !body.healthComponent.alive ? "you died"
                : inputs.skill2.justPressed || inputs.skill3.justPressed || inputs.skill4.justPressed ? "another skill was used"
                : MenuOpen(self);
            if (why != null)
            {
                Stop(player, down, why);
                return;
            }

            GenericSkill primary = body.skillLocator ? body.skillLocator.primary : null!;
            bool canLatch = primary && primary.skillDef
                && FireLatch.CanLatch(primary.skillDef.mustKeyPress, primary.skillDef.activationState.stateType?.FullName);

            // A primary swapped mid-latch, such as by Visions of Heresy, would fire every tick if it needs fresh presses
            if (!canLatch && player.Latch.Latched)
            {
                Stop(player, down, "your primary changed");
            }

            bool wasLatched = player.Latch.Latched;
            bool held = player.Latch.Step(down, Time.unscaledTime, settings.TapSeconds.Value, canLatch);
            if (player.Latch.Latched != wasLatched)
            {
                log.LogInfo(player.Latch.Latched ? $"Firing {Name(primary)} on its own" : "Clicked again, firing stopped");
            }

            // wasDown stays as the game set it, so a latch turning on reads as one continuous hold
            if (held && !down)
            {
                inputs.skill1.down = true;
            }
        }

        // The game takes the buttons away while a menu, chat or the console has focus, the scoreboard counts too
        private static string? MenuOpen(PlayerCharacterMasterController self)
        {
            LocalUser? user = self.networkUser ? self.networkUser.localUser : null;
            if (user == null)
            {
                return null;
            }

            if (user.isUIFocused)
            {
                return "a menu opened";
            }

            CameraRigController rig = self.networkUser.cameraRigController;
            if (!rig)
            {
                return null;
            }

            if (!rig.isControlAllowed)
            {
                return "control was taken away";
            }

            HUD hud = rig.hud;
            return hud && hud.scoreboardPanel && hud.scoreboardPanel.activeSelf ? "the scoreboard opened" : null;
        }

        private void Stop(Player player, bool down, string why)
        {
            if (player.Latch.Latched)
            {
                log.LogInfo($"Firing stopped, {why}");
            }

            player.Latch.Stop(down);
        }

        private void Fail(Exception e)
        {
            failed = true;
            log.LogError($"Toggle Fire stopped working, your primary fires only while held: {e}");
        }

        private static string Name(GenericSkill skill)
        {
            return skill && skill.skillDef ? Language.GetString(skill.skillNameToken) : "your primary";
        }

        private void OnSkillIconLateUpdate(On.RoR2.UI.SkillIcon.orig_LateUpdate orig, SkillIcon self)
        {
            orig(self);
            if (markFailed)
            {
                return;
            }

            try
            {
                bool show = !failed
                    && settings.ShowMark.Value
                    && self.targetSkillSlot == SkillSlot.Primary
                    && self.playerCharacterMasterController
                    && players.TryGetValue(self.playerCharacterMasterController, out Player player)
                    && player.Latch.Latched;
                if (marks.TryGetValue(self, out GameObject mark) && !mark)
                {
                    marks.Remove(self);
                    mark = null!;
                }

                if (mark)
                {
                    if (mark.activeSelf != show)
                    {
                        mark.SetActive(show);
                    }
                }
                else if (show)
                {
                    marks.Add(self, MakeMark(self));
                }
            }
            catch (Exception e)
            {
                // Once, as it runs every frame for every icon
                markFailed = true;
                log.LogError($"Couldn't show the AUTO mark, firing still toggles: {e}");
            }
        }

        // Above the icon in the game's own font, taken from the icon's stock count
        private GameObject MakeMark(SkillIcon icon)
        {
            var mark = new GameObject("JustBeBetter Toggle Fire Mark", typeof(RectTransform));
            made.Add(mark);
            var rect = (RectTransform)mark.transform;
            rect.SetParent(icon.transform, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 2f);
            rect.sizeDelta = new Vector2(0f, 16f);

            var text = mark.AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI? like = icon.stockText ? icon.stockText : icon.cooldownText;
            if (like)
            {
                text.font = like!.font;
            }

            text.fontSize = 14f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Bottom;
            text.color = MarkColour;
            text.raycastTarget = false;
            text.text = "AUTO";
            return mark;
        }

        private sealed class Player
        {
            internal FireLatch Latch { get; } = new FireLatch();

            internal CharacterBody? Body { get; set; }
        }
    }
}
