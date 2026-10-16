using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;
using RoR2.Items;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace JustBeBetter.Pickers
{
    // Lives on an item picker panel: keeps each button's count, corrupted mark and tooltip in step with the
    // inventory of the player looking at it, and with the settings while the picker is open
    internal sealed class PickerDetails : MonoBehaviour
    {
        private readonly List<Entry> entries = new List<Entry>();
        private PickerSettings? settings;
        private ItemInfoSettings? info;
        private TextMeshProUGUI? style;
        private HUD? hud;
        private ManualLogSource? log;
        private PickupPickerController.Option[]? options;
        private bool inspectTooltips;
        private bool givesItem;
        private string arrow = " to ";
        private Inventory? watched;
        private bool watching;

        internal static PickerDetails For(PickupPickerPanel panel, PickerSettings settings, ItemInfoSettings info, ManualLogSource log)
        {
            if (!panel.TryGetComponent(out PickerDetails details))
            {
                details = panel.gameObject.AddComponent<PickerDetails>();
                details.settings = settings;
                details.info = info;
                details.log = log;
                details.hud = panel.GetComponentInParent<HUD>();
                details.style = StyleOf(details.hud, panel);
                details.arrow = StatArrow.Text(log, details.style != null ? details.style.font : null);
                details.givesItem = GivesItem(panel);

                // An inactive panel watches from OnEnable, so one destroyed unseen leaves nothing subscribed
                if (details.isActiveAndEnabled)
                {
                    details.Watch();
                }
            }

            return details;
        }

        // The game's own tooltip shows only with its Inspect option on, and only for available options
        internal void Clear(PickupPickerController.Option[] currentOptions, bool inspectFeature)
        {
            entries.Clear();
            options = currentOptions;
            inspectTooltips = inspectFeature;
        }

        internal void Add(MPButton button, int index, PickupDef? def)
        {
            entries.Add(new Entry(ItemBadge.For(button, style), button.GetComponent<TooltipProvider>(), index, def));
        }

        internal void Refresh()
        {
            if (settings == null || info == null)
            {
                return;
            }

            CharacterMaster? master = MasterOf(hud);
            Inventory? inventory = master != null ? master.inventory : null;
            if (watching && inventory != watched)
            {
                Unwatch();
                Watch();
            }

            if (info!.Stats && log != null)
            {
                ItemStatText.EnsureLoaded(log);
            }

            ItemStats.Utils.CapChance = info.CapChance.Value;
            foreach (Entry entry in entries)
            {
                if (!entry.Badge)
                {
                    continue;
                }

                // One bad button shouldn't leave the rest stale
                try
                {
                    ShowCount(entry, inventory);
                    ShowTooltip(entry, master);
                }
                catch (Exception e)
                {
                    log?.LogError($"Couldn't update the details for {entry.Def?.internalName ?? "an option"}: {e}");
                }
            }
        }

        private void OnEnable()
        {
            Watch();
        }

        private void OnDisable()
        {
            Unwatch();
        }

        private void ShowCount(Entry entry, Inventory? inventory)
        {
            ItemIndex item = entry.Def != null ? entry.Def.itemIndex : ItemIndex.None;
            if (item == ItemIndex.None || inventory == null)
            {
                entry.Badge.Hide();
                return;
            }

            // A corrupted item is held as its void item, so that's the count worth showing
            ItemIndex voidItem = CorruptorOf(item, inventory);
            bool corrupted = voidItem != ItemIndex.None;
            int count = inventory.GetItemCountEffective(corrupted ? voidItem : item);
            bool showCount = settings!.ItemCounts.Value && !(settings.HideZeroCounts.Value && count == 0);
            PickupDef? mark = corrupted && settings.CorruptedMark.Value
                ? PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(voidItem))
                : null;
            entry.Badge.Show(showCount ? count : (int?)null, corrupted, mark);
        }

        // Ours replaces the text whenever a tooltip shows, Hover Tooltips only decides whether it shows without
        // the game's Inspect option, anything that isn't an item, equipment or drone keeps the game's tooltip
        private void ShowTooltip(Entry entry, CharacterMaster? master)
        {
            TooltipProvider? tooltip = entry.Tooltip;
            if (!tooltip || entry.Def == null)
            {
                return;
            }

            bool ours = entry.Def.itemIndex != ItemIndex.None || entry.Def.equipmentIndex != EquipmentIndex.None
                || entry.Def.droneIndex != DroneIndex.None;
            if (!ours)
            {
                return;
            }

            // With Item Info off the text stays the game's, Hover Tooltips still decides whether it shows
            bool gains = givesItem && IsAvailable(entry.Index);
            tooltip!.overrideBodyText = info!.EnabledAtLaunch
                ? ItemStatText.Describe(entry.Def, master, info.FullText, info.Stats, gains, arrow)
                : null;
            tooltip.enabled = settings!.HoverTooltips.Value || (inspectTooltips && IsAvailable(entry.Index));
        }

        private bool IsAvailable(int index)
        {
            return options != null && index < options.Length && options[index].available;
        }

        // The count on the badge, whether or not it shows
        internal static int HeldCount(PickupDef? def, Inventory? inventory)
        {
            ItemIndex item = def != null ? def.itemIndex : ItemIndex.None;
            if (item == ItemIndex.None || inventory == null)
            {
                return 0;
            }

            ItemIndex voidItem = CorruptorOf(item, inventory);
            return inventory.GetItemCountEffective(voidItem != ItemIndex.None ? voidItem : item);
        }

        private static ItemIndex CorruptorOf(ItemIndex item, Inventory inventory)
        {
            ItemIndex voidItem = ContagiousItemManager.GetTransformedItemIndex(item);
            return voidItem != ItemIndex.None && inventory.GetItemCountEffective(voidItem) > 0 ? voidItem : ItemIndex.None;
        }

        private void Watch()
        {
            if (settings == null || info == null || watching)
            {
                return;
            }

            watching = true;
            CharacterMaster? master = MasterOf(hud);
            watched = master != null ? master.inventory : null;
            if (watched != null)
            {
                watched.onInventoryChanged += OnInventoryChanged;
            }

            foreach (ConfigEntry<bool> entry in Entries())
            {
                entry.SettingChanged += OnSettingChanged;
            }
        }

        private void Unwatch()
        {
            if (!watching)
            {
                return;
            }

            watching = false;
            if (watched != null)
            {
                watched.onInventoryChanged -= OnInventoryChanged;
                watched = null;
            }

            foreach (ConfigEntry<bool> entry in Entries())
            {
                entry.SettingChanged -= OnSettingChanged;
            }
        }

        private IEnumerable<ConfigEntry<bool>> Entries()
        {
            yield return settings!.ItemCounts;
            yield return settings.HideZeroCounts;
            yield return settings.CorruptedMark;
            yield return settings.HoverTooltips;
            yield return info!.FullDescriptions;
            yield return info.ItemStats;
            yield return info.CapChance;
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            SafeRefresh();
        }

        // Both run inside other code's events, where a throw would skip the listeners after ours
        private void OnInventoryChanged()
        {
            SafeRefresh();
        }

        private void SafeRefresh()
        {
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                log?.LogError($"Couldn't update the item picker details: {e}");
            }
        }

        // The picker opens on the HUD of the local player using it, so that's whose items count
        internal static CharacterMaster? MasterOf(HUD? hud)
        {
            LocalUser? user = hud != null ? hud.localUserViewer : null;
            user ??= LocalUserManager.GetFirstLocalUser();
            CharacterMaster? master = user?.cachedMaster;
            return master ? master : null;
        }

        // The HUD item bar's count text, so ours reads the same, else any text on the panel
        private static TextMeshProUGUI? StyleOf(HUD? hud, PickupPickerPanel panel)
        {
            GameObject? prefab = hud != null && hud.itemInventoryDisplay ? hud.itemInventoryDisplay.itemIconPrefab : null;
            ItemIcon? icon = prefab ? prefab!.GetComponent<ItemIcon>() : null;
            if (icon && icon!.stackText)
            {
                return icon.stackText;
            }

            return panel.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        // Only these pickers hand you the item, the Scrapper and the rest take or trade it
        private static bool GivesItem(PickupPickerPanel panel)
        {
            string name = panel.gameObject.name;
            return name.StartsWith("CommandPickerPanel", StringComparison.Ordinal)
                || name.StartsWith("OptionPickerPanel", StringComparison.Ordinal);
        }

        private readonly struct Entry
        {
            internal Entry(ItemBadge badge, TooltipProvider? tooltip, int index, PickupDef? def)
            {
                Badge = badge;
                Tooltip = tooltip;
                Index = index;
                Def = def;
            }

            internal ItemBadge Badge { get; }

            internal TooltipProvider? Tooltip { get; }

            internal int Index { get; }

            internal PickupDef? Def { get; }
        }
    }
}
