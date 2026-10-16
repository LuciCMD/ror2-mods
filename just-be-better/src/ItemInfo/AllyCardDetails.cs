using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.ItemInfo
{
    // An ally's card in the ally list says what it does on hover, a drone's own text or what summoned it
    // Set as the game refreshes the card, which it does when the ally behind it changes, as the manager
    // reuses cards for whoever is in the list
    internal sealed class AllyCardDetails : IFeature
    {
        private readonly ItemInfoSettings settings;
        private readonly ManualLogSource log;

        // Only the ally list's, the Drifter's bag overlay card is an AllyCardController too
        private readonly CardList<AllyCardController> cards = new CardList<AllyCardController>(card => card);

        // Tooltips we added, the only ones changed or turned off
        private readonly ConditionalWeakTable<AllyCardController, TooltipProvider> tooltips =
            new ConditionalWeakTable<AllyCardController, TooltipProvider>();

        // Logged once per name, as every card shares one
        private readonly HashSet<string> hoverCanvases = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> shownKinds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> unknownBodies = new HashSet<string>(StringComparer.Ordinal);
        private bool failedOnce;
        private bool loggedTaken;
        private bool loggedNoTarget;

        internal AllyCardDetails(ItemInfoSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            On.RoR2.UI.AllyCardController.UpdateInfo += OnUpdateInfo;
            settings.AllyCardInfo.SettingChanged += OnSettingChanged;
            settings.FullDescriptions.SettingChanged += OnSettingChanged;
        }

        public void Disable()
        {
            On.RoR2.UI.AllyCardController.UpdateInfo -= OnUpdateInfo;
            settings.AllyCardInfo.SettingChanged -= OnSettingChanged;
            settings.FullDescriptions.SettingChanged -= OnSettingChanged;
            foreach (AllyCardController card in cards.Live())
            {
                if (tooltips.TryGetValue(card, out TooltipProvider tooltip) && tooltip)
                {
                    tooltip.enabled = false;
                }
            }
        }

        private void OnUpdateInfo(On.RoR2.UI.AllyCardController.orig_UpdateInfo orig, AllyCardController self)
        {
            orig(self);
            try
            {
                Update(self);
            }
            catch (Exception e)
            {
                // The card may now be another ally's, so the last one's text goes
                if (self && tooltips.TryGetValue(self, out TooltipProvider tooltip) && tooltip)
                {
                    tooltip.enabled = false;
                }

                // Runs whenever a card's ally changes, so a lasting fault is reported once
                if (!failedOnce)
                {
                    failedOnce = true;
                    log.LogError($"Couldn't set an ally card's tooltip, so it has none for now: {e}");
                }
            }
        }

        private void Update(AllyCardController card)
        {
            if (!card)
            {
                return;
            }

            // Includes inactive parents, as a setting change refreshes cards while the pause menu hides the HUD
            if (!cards.Contains(card))
            {
                if (!card.GetComponentInParent<AllyCardManager>(true))
                {
                    return;
                }

                cards.Add(card);
            }

            // Worked out before the tooltip is touched, so a failure can't leave the last ally's text under this one's name
            string? unknownBody = null;
            AllyTip? tip = settings.AllyCardInfo.Value && card.sourceMaster
                ? AllyText.Describe(card.sourceMaster, settings.FullText, out unknownBody)
                : null;
            if (!string.IsNullOrEmpty(unknownBody) && unknownBodies.Add(unknownBody!))
            {
                log.LogInfo($"No text for the ally {unknownBody}, so its card has no tooltip");
            }

            bool ours = tooltips.TryGetValue(card, out TooltipProvider tooltip) && tooltip;
            if (tip == null)
            {
                if (ours)
                {
                    tooltip.enabled = false;
                }

                return;
            }

            if (!ours)
            {
                // Left alone when the game or another mod gave the card one
                if (card.TryGetComponent(out TooltipProvider _))
                {
                    if (!loggedTaken)
                    {
                        loggedTaken = true;
                        log.LogInfo("An ally card already has a tooltip from somewhere else, so it's left as it is");
                    }

                    return;
                }

                if (!CanHover(card))
                {
                    return;
                }

                tooltip = card.gameObject.AddComponent<TooltipProvider>();
                tooltips.Remove(card);
                tooltips.Add(card, tooltip);
            }

            tooltip.enabled = true;
            tooltip.titleColor = tip.Colour;
            tooltip.overrideTitleText = card.nameLabel && !string.IsNullOrEmpty(card.nameLabel.text) ? card.nameLabel.text : tip.Name;
            tooltip.overrideBodyText = tip.Body;
            if (shownKinds.Add(tip.What))
            {
                log.LogInfo($"Ally card tooltip for {tip.What}");
            }
        }

        // The card's background catches the pointer, and its canvas needs a raycaster for that to reach it
        private bool CanHover(AllyCardController card)
        {
            if (card.TryGetComponent(out Image background))
            {
                background.raycastTarget = true;
            }
            else if (!loggedNoTarget)
            {
                loggedNoTarget = true;
                log.LogWarning("An ally card has no background image, so hovering it may only work over its portrait");
            }

            Canvas canvas = card.GetComponentInParent<Canvas>(true);
            if (!canvas)
            {
                log.LogWarning("An ally card sits outside any canvas, so it can't show a tooltip");
                return false;
            }

            if (!canvas.GetComponent<GraphicRaycaster>())
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                if (hoverCanvases.Add(canvas.name))
                {
                    log.LogDebug($"Added pointer events to {canvas.name} so ally cards can show tooltips");
                }
            }

            return true;
        }

        // The game only refreshes a card when its ally changes, so the cards we've seen are refreshed here
        // A search for them found none, as the pause menu, where the setting changes, hides the HUD
        private void OnSettingChanged(object sender, EventArgs e)
        {
            List<AllyCardController> live = cards.Live();
            int refreshed = 0;
            string? failure = null;

            // One card failing, say in another mod's hook, leaves the rest refreshing
            foreach (AllyCardController card in live)
            {
                try
                {
                    card.ForceUpdate();
                    refreshed++;
                }
                catch (Exception ex)
                {
                    failure ??= ex.Message;
                }
            }

            log.LogInfo($"Ally Card Info is {(settings.AllyCardInfo.Value ? "on" : "off")}, refreshed {refreshed} of {live.Count} ally cards");
            if (failure != null)
            {
                log.LogWarning($"Couldn't refresh {live.Count - refreshed} ally cards, they change when their allies next do: {failure}");
            }
        }
    }
}
