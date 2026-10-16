using System;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.ItemStats;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;

namespace JustBeBetter.ItemInfo
{
    // Item and equipment icons outside the pickers, the HUD's inventory bar, the scoreboard and the run report,
    // get the full description and their holder's stats in their tooltip
    // Filled in as the tooltip opens, so the counts are current and nothing runs while nobody hovers
    // Only a tooltip still showing the game's own one-liner is changed, a disabled item's or another mod's text
    // stays as it is
    internal sealed class HoverDetails : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ItemInfoSettings settings;
        private readonly ManualLogSource log;

        private readonly OwnedTooltips owned = new OwnedTooltips();

        private FieldInfo? displayInventoryField;
        private FieldInfo? equipmentInventoryField;
        private FieldInfo? equipmentDataField;
        private FieldInfo? equipmentDefField;
        private FieldInfo? equipmentDisabledField;
        private bool failedOnce;

        internal HoverDetails(ItemInfoSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            displayInventoryField = typeof(ItemInventoryDisplay).GetField("inventory", Private);
            equipmentInventoryField = typeof(EquipmentIcon).GetField("targetInventory", Private);
            equipmentDataField = typeof(EquipmentIcon).GetField("currentDisplayData", Private);
            equipmentDefField = equipmentDataField?.FieldType.GetField("equipmentDef", Any);
            equipmentDisabledField = equipmentDataField?.FieldType.GetField("equipmentDisabled", Any);
            if (displayInventoryField == null || equipmentInventoryField == null)
            {
                log.LogWarning("An inventory's owner can't be found, hover tooltips show descriptions without stats");
            }

            if (equipmentDataField == null || equipmentDefField == null || equipmentDisabledField == null)
            {
                log.LogWarning("The equipment icon's internals have changed, equipment tooltips stay as the game has them");
            }

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
                    log.LogError($"Couldn't add details to an item tooltip, it shows the game's text: {e}");
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

            ItemIcon? itemIcon = provider.GetComponentInParent<ItemIcon>();
            if (itemIcon != null && itemIcon.tooltipProvider == provider)
            {
                FillItem(itemIcon, provider);
                return;
            }

            EquipmentIcon? equipmentIcon = provider.GetComponentInParent<EquipmentIcon>();
            if (equipmentIcon != null && equipmentIcon.tooltipProvider == provider)
            {
                FillEquipment(equipmentIcon, provider);
            }
        }

        private void FillItem(ItemIcon icon, TooltipProvider provider)
        {
            ItemDef? item = ItemCatalog.GetItemDef(icon.itemIndex);
            string? gameText = item != null ? GameText(item, provider) : null;
            if (item == null || gameText == null || !Wanted())
            {
                owned.Release(provider);
                return;
            }

            string key = $"item {(int)item.itemIndex}";
            if (!owned.Ours(provider) || owned.IsFresh(provider, key))
            {
                return;
            }

            ItemInventoryDisplay? display = icon.GetComponentInParent<ItemInventoryDisplay>();
            var inventory = display != null ? displayInventoryField?.GetValue(display) as Inventory : null;
            owned.Write(provider, key, ItemStatText.DescribeHeld(item, MasterOf(inventory), settings.FullText, settings.Stats, gameText));
        }

        // The game's tooltip text when it's still built on the item's own one-liner, else null
        // A Hex item's comes wrapped in its Hallowed Shrine hint, a disabled item's is the disabled notice
        private static string? GameText(ItemDef item, TooltipProvider provider)
        {
            if (provider.bodyToken == item.pickupToken)
            {
                return Language.GetString(item.pickupToken);
            }

            string shortText = Language.GetString(item.pickupToken);
            string body = Language.GetString(provider.bodyToken ?? string.Empty);
            return !string.IsNullOrEmpty(shortText) && body.Contains(shortText) ? body : null;
        }

        private void FillEquipment(EquipmentIcon icon, TooltipProvider provider)
        {
            // The game's display data is private, so read through reflection
            object? data = equipmentDataField?.GetValue(icon);
            var equipment = data != null ? equipmentDefField?.GetValue(data) as EquipmentDef : null;
            bool disabled = data != null && equipmentDisabledField?.GetValue(data) is bool off && off;
            if (equipment == null || disabled || provider.bodyToken != equipment.pickupToken || !Wanted())
            {
                owned.Release(provider);
                return;
            }

            string key = $"equipment {(int)equipment.equipmentIndex}";
            if (!owned.Ours(provider) || owned.IsFresh(provider, key))
            {
                return;
            }

            var inventory = equipmentInventoryField?.GetValue(icon) as Inventory;
            owned.Write(provider, key, ItemStatText.DescribeHeld(equipment, MasterOf(inventory), settings.FullText, settings.Stats));
        }

        // With neither full text nor stats there's nothing to add to the game's one-liner
        private bool Wanted()
        {
            if (!settings.FullText && !settings.Stats)
            {
                return false;
            }

            if (settings.Stats)
            {
                ItemStatText.EnsureLoaded(log);
                ItemStats.Utils.CapChance = settings.CapChance.Value;
            }

            return true;
        }

        // The run report's icons have no inventory behind them, those get the description alone
        private static CharacterMaster? MasterOf(Inventory? inventory)
        {
            if (inventory == null || !inventory)
            {
                return null;
            }

            CharacterMaster master = inventory.GetComponent<CharacterMaster>();
            return master ? master : null;
        }
    }
}
