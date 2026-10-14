using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;

namespace JustBeBetter.InventorySort
{
    // Sorts what an item display draws, then puts its pickup order back, so ties and the next sort still go by
    // pickup order and nothing else reading the display sees a change
    internal sealed class InventorySorter : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly InventorySortSettings settings;
        private readonly ManualLogSource log;
        private readonly List<ItemKey> keys = new List<ItemKey>();
        private FieldInfo? orderField;
        private FieldInfo? countField;
        private FieldInfo? stacksField;
        private bool warned;
        private bool told;

        internal InventorySorter(InventorySortSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // So the Scrapper only follows a sort that's actually drawing the inventories
        internal bool Hooked { get; private set; }

        public void Enable()
        {
            Type type = typeof(ItemInventoryDisplay);
            orderField = type.GetField("itemOrder", Private);
            countField = type.GetField("itemOrderCount", Private);
            stacksField = type.GetField("itemStacks", Private);
            if (orderField == null || countField == null || stacksField == null)
            {
                log.LogWarning("The item display's internals have changed, inventories keep the game's order");
                return;
            }

            On.RoR2.UI.ItemInventoryDisplay.UpdateDisplay += OnUpdateDisplay;
            Hooked = true;
            settings.ScrapPosition.SettingChanged += OnSettingChanged;
            settings.TierOrder.SettingChanged += OnSettingChanged;
            settings.MixVoids.SettingChanged += OnSettingChanged;
            settings.StackSize.SettingChanged += OnSettingChanged;
            settings.SortRunReports.SettingChanged += OnSettingChanged;
        }

        public void Disable()
        {
            On.RoR2.UI.ItemInventoryDisplay.UpdateDisplay -= OnUpdateDisplay;
            Hooked = false;
            settings.ScrapPosition.SettingChanged -= OnSettingChanged;
            settings.TierOrder.SettingChanged -= OnSettingChanged;
            settings.MixVoids.SettingChanged -= OnSettingChanged;
            settings.StackSize.SettingChanged -= OnSettingChanged;
            settings.SortRunReports.SettingChanged -= OnSettingChanged;
        }

        private void OnUpdateDisplay(On.RoR2.UI.ItemInventoryDisplay.orig_UpdateDisplay orig, ItemInventoryDisplay self)
        {
            if (!self || !Wanted(self) || orderField!.GetValue(self) is not ItemIndex[] order
                || countField!.GetValue(self) is not int count || stacksField!.GetValue(self) is not int[] stacks)
            {
                orig(self);
                return;
            }

            count = Math.Max(0, Math.Min(count, order.Length));
            var picked = new ItemIndex[count];
            Array.Copy(order, picked, count);
            try
            {
                Sort(order, picked, stacks);
            }
            catch (Exception e)
            {
                Array.Copy(picked, order, count);
                if (!warned)
                {
                    warned = true;
                    log.LogError($"Couldn't sort an inventory, it shows in pickup order: {e}");
                }
            }

            try
            {
                orig(self);
            }
            finally
            {
                Array.Copy(picked, order, count);
            }
        }

        // Run reports go by their own setting, and the logbook shows them through the same panel
        private bool Wanted(ItemInventoryDisplay display)
        {
            return settings.SortRunReports.Value || !display.GetComponentInParent<GameEndReportPanelController>();
        }

        private void Sort(ItemIndex[] order, ItemIndex[] picked, int[] stacks)
        {
            keys.Clear();
            foreach (ItemIndex index in picked)
            {
                ItemDef? def = ItemCatalog.GetItemDef(index);
                int held = (int)index >= 0 && (int)index < stacks.Length ? stacks[(int)index] : 0;
                keys.Add(new ItemKey(def ? def!.tier : ItemTier.NoTier, def && IsScrap(def!), held));
            }

            SortRules rules = settings.Rules;
            List<int> wanted = InventoryOrder.Arrange(keys, rules);
            for (int i = 0; i < picked.Length; i++)
            {
                order[i] = picked[wanted[i]];
            }

            if (!told)
            {
                told = true;
                log.LogInfo($"First inventory sorted, {picked.Length} items, tiers {rules.Tiers}, voids mixed {rules.MixVoids}, stacks {rules.Stacks}, scrap {rules.Scrap}");
            }
        }

        // Regenerating Scrap once used has no scrap tag but still reads as scrap
        internal static bool IsScrap(ItemDef def)
        {
            return def.ContainsTag(ItemTag.Scrap) || def.ContainsTag(ItemTag.PriorityScrap)
                || (DLC1Content.Items.RegeneratingScrapConsumed && def == DLC1Content.Items.RegeneratingScrapConsumed);
        }

        // The game only redraws an inventory when it changes, so open ones are redrawn now
        private void OnSettingChanged(object sender, EventArgs e)
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
                log.LogWarning($"Couldn't redraw the open inventories, they re-sort on the next pickup: {ex.Message}");
            }
        }
    }
}
