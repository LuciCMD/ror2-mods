using System;
using System.Collections.Generic;
using BepInEx.Logging;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using JustBeBetter.Shared;
using RoR2;
using RoR2.Skills;
using RoR2.UI;

namespace JustBeBetter.SkillInfo
{
    // Skill tooltips get cooldown, proc coefficient, notes and item procs under the game's description
    // In a run from the HUD's skill icon, with the player's own values, on the loadout screen from the skill's
    // name and description, with base values
    // Filled in as the tooltip opens, and only over the game's own description
    internal sealed class SkillDetails : IFeature
    {
        private readonly SkillInfoSettings settings;
        private readonly ItemInfoSettings itemInfo;
        private readonly ManualLogSource log;
        private readonly OwnedTooltips owned = new OwnedTooltips();
        private Dictionary<string, List<SkillDef>>? skillsByTitle;
        private string? titleLanguage;
        private bool failedOnce;

        internal SkillDetails(SkillInfoSettings settings, ItemInfoSettings itemInfo, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.itemInfo = itemInfo ?? throw new ArgumentNullException(nameof(itemInfo));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            SkillText.Log = log;
            On.RoR2.UI.TooltipProvider.SetTooltip += OnSetTooltip;
        }

        public void Disable()
        {
            On.RoR2.UI.TooltipProvider.SetTooltip -= OnSetTooltip;
        }

        private void OnSetTooltip(On.RoR2.UI.TooltipProvider.orig_SetTooltip orig, TooltipProvider self, UnityEngine.EventSystems.EventSystem eventSystem)
        {
            try
            {
                Fill(self);
            }
            catch (Exception e)
            {
                // Runs on every hover, so a lasting fault is reported once
                if (!failedOnce)
                {
                    failedOnce = true;
                    log.LogError($"Couldn't add details to a skill tooltip, it shows the game's text: {e}");
                }
            }

            orig(self, eventSystem);
        }

        private void Fill(TooltipProvider provider)
        {
            if (!provider)
            {
                return;
            }

            SkillIcon? icon = provider.GetComponentInParent<SkillIcon>();
            if (icon != null && icon.tooltipProvider == provider)
            {
                FillInRun(icon.targetSkill, provider);
                return;
            }

            FillByText(provider);
        }

        // An emptied slot clears its tokens, so its old text is handed back with them
        private void FillInRun(GenericSkill? skill, TooltipProvider provider)
        {
            SkillDef? def = skill != null ? skill.skillDef : null;
            if (skill == null || def == null || string.IsNullOrEmpty(provider.bodyToken) || provider.bodyToken != skill.skillDescriptionToken)
            {
                owned.Release(provider);
                return;
            }

            // The skill's own base, which Heresy and the like change from the SkillDef's
            float now = skill.GetFinalRechargeInterval();
            string key = $"skill {skill.skillNameToken} {now}";
            string extra = SkillText.Extra(skill.skillNameToken, skill.baseRechargeInterval, now, MasterOf(skill), Lines(true));
            if (extra.Length == 0)
            {
                owned.Release(provider);
                return;
            }

            if (owned.Ours(provider) && !owned.IsFresh(provider, key))
            {
                owned.Write(provider, key, Language.GetString(provider.bodyToken) + extra);
            }
        }

        // The loadout screen writes finished text rather than tokens, so the skill is found by its name and
        // description as they read in the current language
        private void FillByText(TooltipProvider provider)
        {
            string title = provider.overrideTitleText;
            if (string.IsNullOrEmpty(title) || !SkillsTitled().TryGetValue(title, out List<SkillDef> skills))
            {
                return;
            }

            foreach (SkillDef skill in skills)
            {
                string key = $"skill {skill.skillNameToken}";
                string description = Language.GetString(skill.skillDescriptionToken);
                bool mine = owned.Holds(provider, key);
                if (provider.overrideBodyText != description && !mine)
                {
                    continue;
                }

                string extra = SkillText.Extra(skill.skillNameToken, skill.baseRechargeInterval, null, null, Lines(false));
                if (extra.Length == 0)
                {
                    if (mine)
                    {
                        provider.overrideBodyText = description;
                    }

                    owned.Forget(provider);
                }
                else if (!owned.IsFresh(provider, key))
                {
                    owned.Write(provider, key, description + extra);
                }

                return;
            }
        }

        private static CharacterMaster? MasterOf(GenericSkill skill)
        {
            CharacterBody? body = skill.characterBody;
            CharacterMaster? master = body != null ? body.master : null;
            return master ? master : null;
        }

        // Item procs only in a run, where there's an inventory to read
        private SkillText.Lines Lines(bool inRun)
        {
            bool itemProcs = inRun && settings.ItemProcs.Value;
            if (itemProcs)
            {
                ItemStatText.EnsureLoaded(log);
                ItemStats.Utils.CapChance = itemInfo.CapChance.Value;
            }

            return new SkillText.Lines(settings.Cooldown.Value, settings.ProcCoefficient.Value, settings.ExtraNotes.Value, itemProcs);
        }

        // Skills by their name in the current language, rebuilt when it changes
        private Dictionary<string, List<SkillDef>> SkillsTitled()
        {
            string language = Language.currentLanguageName ?? string.Empty;
            if (skillsByTitle != null && language == titleLanguage)
            {
                return skillsByTitle;
            }

            titleLanguage = language;
            skillsByTitle = new Dictionary<string, List<SkillDef>>();
            foreach (SkillDef skill in SkillCatalog.allSkillDefs)
            {
                if (skill == null || string.IsNullOrEmpty(skill.skillNameToken) || string.IsNullOrEmpty(skill.skillDescriptionToken))
                {
                    continue;
                }

                string name = Language.GetString(skill.skillNameToken);
                if (!skillsByTitle.TryGetValue(name, out List<SkillDef> list))
                {
                    list = new List<SkillDef>();
                    skillsByTitle.Add(name, list);
                }

                list.Add(skill);
            }

            return skillsByTitle;
        }
    }
}
