using System;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.ItemStats;
using RoR2;
using RoR2.UI;

namespace JustBeBetter.ItemInfo
{
    // The panel a click on an item or equipment opens, under the scoreboard or the inventory, gets the holder's
    // stats as hover tooltips do
    // The game already writes the full description there, so only the stats are added
    // Only the game's own description is changed, a disabled, undiscovered or another mod's text stays
    internal sealed class InspectDetails : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ItemInfoSettings settings;
        private readonly ManualLogSource log;

        private FieldInfo? descriptionField;
        private FieldInfo? displayInventoryField;
        private FieldInfo? equipmentInventoryField;
        private FieldInfo? equipmentDataField;
        private FieldInfo? equipmentDefField;
        private bool failedOnce;

        internal InspectDetails(ItemInfoSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            descriptionField = typeof(InspectPanelController).GetField("InspectDescription", Private);
            displayInventoryField = typeof(ItemInventoryDisplay).GetField("inventory", Private);
            equipmentInventoryField = typeof(EquipmentIcon).GetField("targetInventory", Private);
            equipmentDataField = typeof(EquipmentIcon).GetField("currentDisplayData", Private);
            equipmentDefField = equipmentDataField?.FieldType.GetField("equipmentDef", Any);
            if (descriptionField == null || displayInventoryField == null)
            {
                log.LogWarning("The inspect panel's internals have changed, clicked items show the game's text");
                return;
            }

            if (equipmentInventoryField == null || equipmentDataField == null || equipmentDefField == null)
            {
                log.LogWarning("The equipment icon's internals have changed, clicked equipment shows the game's text");
            }

            On.RoR2.UI.ItemIcon.ItemClicked += OnItemClicked;
            On.RoR2.UI.EquipmentIcon.ItemClicked += OnEquipmentClicked;
        }

        public void Disable()
        {
            On.RoR2.UI.ItemIcon.ItemClicked -= OnItemClicked;
            On.RoR2.UI.EquipmentIcon.ItemClicked -= OnEquipmentClicked;
        }

        private void OnItemClicked(On.RoR2.UI.ItemIcon.orig_ItemClicked orig, ItemIcon self)
        {
            orig(self);
            Guard(() => FillItem(self));
        }

        private void OnEquipmentClicked(On.RoR2.UI.EquipmentIcon.orig_ItemClicked orig, EquipmentIcon self)
        {
            orig(self);
            Guard(() => FillEquipment(self));
        }

        private void Guard(Action fill)
        {
            try
            {
                fill();
            }
            catch (Exception e)
            {
                if (!failedOnce)
                {
                    failedOnce = true;
                    log.LogError($"Couldn't add stats to the inspect panel, it shows the game's text: {e}");
                }
            }
        }

        private void FillItem(ItemIcon icon)
        {
            ItemDef? item = icon ? ItemCatalog.GetItemDef(icon.itemIndex) : null;
            LanguageTextMeshController? label = item != null && settings.Stats ? Description(icon!.inspectPanel) : null;
            if (item == null || label == null || !IsGameDescription(label.token, item))
            {
                return;
            }

            ItemInventoryDisplay? display = icon!.GetComponentInParent<ItemInventoryDisplay>();
            CharacterMaster? master = MasterOf(display != null ? displayInventoryField!.GetValue(display) as Inventory : null);
            if (master == null)
            {
                return;
            }

            Prepare();

            // Not full, as the game's text is the full one already and a Hex item's hint must stay
            label.token = ItemStatText.DescribeHeld(item, master, false, true, Language.GetString(label.token));
        }

        private void FillEquipment(EquipmentIcon icon)
        {
            if (!icon || !settings.Stats || equipmentDataField == null || equipmentDefField == null || equipmentInventoryField == null)
            {
                return;
            }

            object? data = equipmentDataField.GetValue(icon);
            var equipment = data != null ? equipmentDefField.GetValue(data) as EquipmentDef : null;
            LanguageTextMeshController? label = equipment != null ? Description(icon.inspectPanel) : null;
            if (equipment == null || label == null || label.token != equipment.descriptionToken)
            {
                return;
            }

            CharacterMaster? master = MasterOf(equipmentInventoryField.GetValue(icon) as Inventory);
            if (master == null)
            {
                return;
            }

            Prepare();
            label.token = ItemStatText.DescribeHeld(equipment, master, true, true);
        }

        private LanguageTextMeshController? Description(InspectPanelController? panel)
        {
            if (panel == null || !panel)
            {
                return null;
            }

            var label = descriptionField!.GetValue(panel) as LanguageTextMeshController;
            return label ? label : null;
        }

        // A Hex item's description comes wrapped in its Hallowed Shrine hint
        private static bool IsGameDescription(string? token, ItemDef item)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(item.descriptionToken))
            {
                return false;
            }

            if (token == item.descriptionToken)
            {
                return true;
            }

            return item.tier == ItemTier.Hex
                && token == Language.GetStringFormatted("HEXTIER_DESC_SUFFIX", Language.GetString(item.descriptionToken));
        }

        private void Prepare()
        {
            ItemStatText.EnsureLoaded(log);
            ItemStats.Utils.CapChance = settings.CapChance.Value;
        }

        // The run report's icons have no inventory behind them, those keep the game's text
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
