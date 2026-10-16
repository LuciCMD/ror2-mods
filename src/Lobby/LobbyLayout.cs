using System;
using System.Collections.Generic;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Lobby
{
    // Sets up the lobby's scrolling tabs and wrapping loadout rows when the lobby is built
    internal sealed class LobbyLayout
    {
        // The game's own strip height with its five Seeker strips, strips keep at least this when more don't fit
        private const float SkillStripHeight = 87f;

        private readonly ManualLogSource log;

        internal LobbyLayout(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.UI.CharacterSelectController.Awake += OnCharacterSelectAwake;
            On.RoR2.UI.CharacterSelectController.RebuildLocal += OnRebuildLocal;
            On.RoR2.UI.LoadoutPanelController.Rebuild += OnLoadoutRebuild;
            TabViewport.Enable();
        }

        internal void Disable()
        {
            On.RoR2.UI.CharacterSelectController.Awake -= OnCharacterSelectAwake;
            On.RoR2.UI.CharacterSelectController.RebuildLocal -= OnRebuildLocal;
            On.RoR2.UI.LoadoutPanelController.Rebuild -= OnLoadoutRebuild;
            TabViewport.Disable();
        }

        private void OnCharacterSelectAwake(On.RoR2.UI.CharacterSelectController.orig_Awake orig, CharacterSelectController self)
        {
            orig(self);
            try
            {
                AttachScrolling(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't make the lobby tabs scroll: {e}");
            }
        }

        private void AttachScrolling(CharacterSelectController lobby)
        {
            RectTransform skills = lobby.skillStripContainer;
            var contentPanel = skills ? skills.parent as RectTransform : null;
            if (contentPanel == null)
            {
                log.LogWarning("Lobby tab area not found; tabs won't scroll");
                return;
            }

            var panels = new List<RectTransform>();
            for (int i = 0; i < contentPanel.childCount; i++)
            {
                if (contentPanel.GetChild(i) is RectTransform panel && panel.name != "PanelHighlightHolder")
                {
                    panels.Add(panel);
                }
            }

            TabScroller? scroller = TabScroller.Attach(contentPanel, lobby.GetComponent<MPEventSystemLocator>(), panels);
            if (scroller != null)
            {
                log.Verbose($"Lobby tabs scroll: {string.Join(", ", panels.ConvertAll(p => p.name))}");
            }
        }

        // The strip data type is private, so heights are set after the whole skill list is rebuilt
        private static void OnRebuildLocal(On.RoR2.UI.CharacterSelectController.orig_RebuildLocal orig, CharacterSelectController self, bool deselectButtons)
        {
            orig(self, deselectButtons);
            RectTransform container = self.skillStripContainer;
            if (!container)
            {
                return;
            }

            for (int i = 0; i < container.childCount; i++)
            {
                Transform strip = container.GetChild(i);
                if (strip.gameObject.activeSelf && strip.GetComponent<HGButton>() && strip.TryGetComponent(out LayoutElement element))
                {
                    element.minHeight = SkillStripHeight;
                }
            }
        }

        private void OnLoadoutRebuild(On.RoR2.UI.LoadoutPanelController.orig_Rebuild orig, LoadoutPanelController self)
        {
            orig(self);
            try
            {
                LoadoutWrap.Apply(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't wrap the loadout rows: {e}");
            }
        }
    }
}
