using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.Items;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace JustBeBetter.Pickers
{
    // Lives on an item picker panel: keeps each button's count and corrupted mark in step with the inventory
    // of the player looking at it, and with the settings while the picker is open
    internal sealed class PickerCounts : MonoBehaviour
    {
        private readonly List<(ItemBadge Badge, ItemIndex Item)> badges = new List<(ItemBadge, ItemIndex)>();
        private PickerSettings? settings;
        private TextMeshProUGUI? style;
        private HUD? hud;
        private ManualLogSource? log;
        private Inventory? watched;
        private bool watching;

        internal static PickerCounts For(PickupPickerPanel panel, PickerSettings settings, ManualLogSource log)
        {
            if (!panel.TryGetComponent(out PickerCounts counts))
            {
                counts = panel.gameObject.AddComponent<PickerCounts>();
                counts.settings = settings;
                counts.log = log;
                counts.hud = panel.GetComponentInParent<HUD>();
                counts.style = StyleOf(counts.hud, panel);

                // An inactive panel watches from OnEnable, so one destroyed unseen leaves nothing subscribed
                if (counts.isActiveAndEnabled)
                {
                    counts.Watch();
                }
            }

            return counts;
        }

        internal void Clear()
        {
            badges.Clear();
        }

        internal void Add(MPButton button, PickupDef? def)
        {
            badges.Add((ItemBadge.For(button, style), def != null ? def.itemIndex : ItemIndex.None));
        }

        internal void Refresh()
        {
            if (settings == null)
            {
                return;
            }

            Inventory? inventory = InventoryOf(hud);
            if (watching && inventory != watched)
            {
                Unwatch();
                Watch();
            }

            foreach ((ItemBadge badge, ItemIndex item) in badges)
            {
                if (!badge)
                {
                    continue;
                }

                // One bad button shouldn't leave the rest stale
                try
                {
                    Show(badge, item, inventory);
                }
                catch (Exception e)
                {
                    log?.LogError($"Couldn't update the count for item {item}: {e}");
                }
            }
        }

        private void Show(ItemBadge badge, ItemIndex item, Inventory? inventory)
        {
            if (item == ItemIndex.None || inventory == null)
            {
                badge.Hide();
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
            badge.Show(showCount ? count : (int?)null, corrupted, mark);
        }

        private void OnEnable()
        {
            Watch();
        }

        private void OnDisable()
        {
            Unwatch();
        }

        private static ItemIndex CorruptorOf(ItemIndex item, Inventory inventory)
        {
            ItemIndex voidItem = ContagiousItemManager.GetTransformedItemIndex(item);
            return voidItem != ItemIndex.None && inventory.GetItemCountEffective(voidItem) > 0 ? voidItem : ItemIndex.None;
        }

        private void Watch()
        {
            if (settings == null || watching)
            {
                return;
            }

            watching = true;
            watched = InventoryOf(hud);
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
                log?.LogError($"Couldn't update the item counts: {e}");
            }
        }

        // The picker opens on the HUD of the local player using it, so that's whose items count
        private static Inventory? InventoryOf(HUD? hud)
        {
            LocalUser? user = hud != null ? hud.localUserViewer : null;
            user ??= LocalUserManager.GetFirstLocalUser();
            CharacterMaster? master = user?.cachedMaster;
            return master ? master!.inventory : null;
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
    }
}
