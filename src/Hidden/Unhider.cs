using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace JustBeBetter.Hidden
{
    // Hidden items and buffs shown with the game's own display code, so their order and stacking stay vanilla
    // Both settings apply at once, without a restart
    internal sealed class Unhider : IFeature
    {
        private readonly HiddenSettings settings;
        private readonly ManualLogSource log;

        // Hidden buffs we gave the placeholder, set back to no icon when Unhide Buffs goes off
        private readonly List<BuffDef> filled = new List<BuffDef>();

        // Hidden buffs already reported on a bar, so each is logged once
        private readonly HashSet<BuffIndex> told = new HashSet<BuffIndex>();

        private BuffIndex[]? allBuffs;
        private BuffIndex[]? hiddenBuffs;
        private FieldInfo? sourceField;
        private Sprite? placeholder;
        private bool failed;

        internal Unhider(HiddenSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            sourceField = typeof(BuffDisplay).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic);
            On.RoR2.UI.ItemInventoryDisplay.ItemIsVisible += OnItemIsVisible;
            On.RoR2.UI.BuffDisplay.AllocateIcons += OnAllocateIcons;
            settings.UnhideItems.SettingChanged += OnItemsChanged;
            settings.UnhideBuffs.SettingChanged += OnBuffsChanged;
            BuffCatalog.availability.CallWhenAvailable(OnBuffCatalog);
        }

        public void Disable()
        {
            On.RoR2.UI.ItemInventoryDisplay.ItemIsVisible -= OnItemIsVisible;
            On.RoR2.UI.BuffDisplay.AllocateIcons -= OnAllocateIcons;
            settings.UnhideItems.SettingChanged -= OnItemsChanged;
            settings.UnhideBuffs.SettingChanged -= OnBuffsChanged;
            ClearIcons();
        }

        private bool OnItemIsVisible(On.RoR2.UI.ItemInventoryDisplay.orig_ItemIsVisible orig, ItemIndex itemIndex)
        {
            return orig(itemIndex) || (settings.UnhideItems.Value && ItemCatalog.GetItemDef(itemIndex) != null);
        }

        // The game only lists an inventory's items again when it changes, so open ones are redrawn now
        private void OnItemsChanged(object sender, EventArgs e)
        {
            try
            {
                foreach (ItemInventoryDisplay display in UnityEngine.Object.FindObjectsOfType<ItemInventoryDisplay>())
                {
                    display.UpdateDisplay();
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"Couldn't redraw the open inventories, they change on the next pickup: {ex.Message}");
            }
        }

        // The game's bar only walks the buffs that aren't hidden, so it's handed every buff while it runs
        private void OnAllocateIcons(On.RoR2.UI.BuffDisplay.orig_AllocateIcons orig, BuffDisplay self)
        {
            if (failed || allBuffs == null || !settings.UnhideBuffs.Value)
            {
                orig(self);
                return;
            }

            BuffIndex[] shown = BuffCatalog.nonHiddenBuffIndices;
            BuffCatalog.nonHiddenBuffIndices = allBuffs;
            try
            {
                orig(self);
            }
            finally
            {
                BuffCatalog.nonHiddenBuffIndices = shown;
            }

            TellShown(self);
        }

        // Once per buff, so a test run shows which hidden buffs reached a bar and whether they can be seen
        private void TellShown(BuffDisplay display)
        {
            if (!Logs.VerboseOn || hiddenBuffs == null || told.Count == hiddenBuffs.Length || sourceField?.GetValue(display) is not CharacterBody body || !body)
            {
                return;
            }

            foreach (BuffIndex index in hiddenBuffs)
            {
                if (body.HasBuff(index) && told.Add(index))
                {
                    BuffDef def = BuffCatalog.GetBuffDef(index);
                    string alpha = def.buffColor.a.ToString("0.00", CultureInfo.InvariantCulture);
                    log.Verbose($"Hidden buff {def.name} shown on {body.name}, icon {(def.iconSprite ? def.iconSprite.name : "none")}, colour alpha {alpha}");
                }
            }
        }

        private void OnBuffCatalog()
        {
            try
            {
                var all = new BuffIndex[BuffCatalog.buffCount];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = (BuffIndex)i;
                }

                allBuffs = all;
                var hidden = new List<BuffIndex>();
                foreach (BuffIndex index in all)
                {
                    BuffDef def = BuffCatalog.GetBuffDef(index);
                    if (def && def.isHidden)
                    {
                        hidden.Add(index);
                    }
                }

                hiddenBuffs = hidden.ToArray();
                BuffDef geode = DLC2Content.Buffs.GeodeBuff;
                placeholder = geode && geode.iconSprite ? geode.iconSprite : null;
                if (placeholder == null)
                {
                    log.LogWarning("The placeholder buff icon wasn't found, hidden buffs without an icon don't show");
                }

                ApplyIcons();
            }
            catch (Exception e)
            {
                failed = true;
                log.LogError($"Couldn't read the buff list, hidden buffs stay hidden: {e}");
            }
        }

        private void OnBuffsChanged(object sender, EventArgs e)
        {
            try
            {
                ApplyIcons();
            }
            catch (Exception ex)
            {
                log.LogWarning($"Couldn't change the hidden buffs' icons: {ex.Message}");
            }
        }

        // The bar drops any buff without an icon, so hidden ones get the placeholder while they're shown
        private void ApplyIcons()
        {
            if (allBuffs == null || placeholder == null)
            {
                return;
            }

            if (!settings.UnhideBuffs.Value)
            {
                ClearIcons();
                return;
            }

            for (int i = 0; i < allBuffs.Length; i++)
            {
                BuffDef def = BuffCatalog.GetBuffDef((BuffIndex)i);
                if (def && def.isHidden && !def.iconSprite)
                {
                    def.iconSprite = placeholder;
                    filled.Add(def);
                }
            }

            log.Verbose($"Unhide Buffs on: {hiddenBuffs?.Length ?? 0} hidden buffs in the game, {filled.Count} of them use the placeholder icon");
        }

        private void ClearIcons()
        {
            foreach (BuffDef def in filled)
            {
                // Left alone if something else gave it an icon since
                if (def && def.iconSprite == placeholder)
                {
                    def.iconSprite = null;
                }
            }

            filled.Clear();
        }
    }
}
