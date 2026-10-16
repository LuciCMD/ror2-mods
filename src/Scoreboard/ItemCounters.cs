using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace JustBeBetter.Scoreboard
{
    // Your item counts by tier after the scoreboard's Items header, and each row's total with its temp items
    // The game only rewrites a row on a change of total, so a void corruption would leave tiers stale,
    // we follow the inventory's own change event instead
    internal sealed class ItemCounters : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ItemHeaderPath = "Container/HeaderGroup/Item Header";

        // Temp items run down without always raising a change
        private const float RefreshSeconds = 1f;

        private readonly ScoreboardSettings settings;
        private readonly Func<bool> countHidden;
        private readonly ManualLogSource log;
        private readonly ConditionalWeakTable<ScoreboardStrip, Row> rows = new ConditionalWeakTable<ScoreboardStrip, Row>();
        private readonly ConditionalWeakTable<ScoreboardController, Header> headers = new ConditionalWeakTable<ScoreboardController, Header>();
        private readonly ConditionalWeakTable<Inventory, Changes> changes = new ConditionalWeakTable<Inventory, Changes>();
        private readonly CounterText.Tally tally = new CounterText.Tally();

        private FieldInfo? inventoryField;
        private bool failed;
        private bool handBackFailed;

        // countHidden follows Unhide Items, so the total matches the icons the row shows
        internal ItemCounters(ScoreboardSettings settings, Func<bool> countHidden, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.countHidden = countHidden ?? throw new ArgumentNullException(nameof(countHidden));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            inventoryField = typeof(ScoreboardStrip).GetField("inventory", Private);
            if (inventoryField == null)
            {
                log.LogWarning("The scoreboard row's internals have changed, item counts stay as the game shows them");
                return;
            }

            Inventory.onInventoryChangedGlobal += OnInventoryChanged;
            On.RoR2.UI.ScoreboardStrip.UpdateItemCountText += OnUpdateItemCountText;
        }

        public void Disable()
        {
            Inventory.onInventoryChangedGlobal -= OnInventoryChanged;
            On.RoR2.UI.ScoreboardStrip.UpdateItemCountText -= OnUpdateItemCountText;
        }

        private void OnInventoryChanged(Inventory inventory)
        {
            // Every inventory raises this, enemies' too, so only ones we show are counted
            if (inventory && changes.TryGetValue(inventory, out Changes seen))
            {
                seen.Count++;
            }
        }

        private void OnUpdateItemCountText(On.RoR2.UI.ScoreboardStrip.orig_UpdateItemCountText orig, ScoreboardStrip self)
        {
            orig(self);
            if (failed)
            {
                HandBack(self);
                return;
            }

            try
            {
                UpdateRow(self);
                UpdateHeader(self);
            }
            catch (Exception e)
            {
                // Once, as it runs every frame for every row while the scoreboard is open
                failed = true;
                log.LogError($"Couldn't count the scoreboard's items, counts go back to the game's from here: {e}");
                HandBack(self);
            }
        }

        private void UpdateRow(ScoreboardStrip strip)
        {
            if (!strip || !strip.itemCountText)
            {
                return;
            }

            if (inventoryField!.GetValue(strip) is not Inventory inventory || !inventory)
            {
                return;
            }

            Row row = rows.GetOrCreateValue(strip);

            // A hidden inventory gets the game's own count, even when it equals the last player's total
            if (inventory.inventoryDisabled)
            {
                HandBackRow(strip, row);
                return;
            }

            CounterText.Options options = settings.Current();
            if (row.Stale(inventory, changes.GetOrCreateValue(inventory).Count, options))
            {
                row.Text = CounterText.Row(Count(inventory), options);
            }

            // Also puts ours back after the game writes its plain total
            if (!string.Equals(strip.itemCountText.text, row.Text, StringComparison.Ordinal))
            {
                strip.itemCountText.text = row.Text;
            }
        }

        // From every row, the game's label can be written between two rows on the frame the scoreboard opens
        private void UpdateHeader(ScoreboardStrip strip)
        {
            Header? header = HeaderOf(strip);
            if (header == null)
            {
                return;
            }

            if (!header.Searched)
            {
                header.Searched = true;
                header.Label = header.Board!.transform.Find(ItemHeaderPath)?.GetComponent<TMP_Text>();
                header.Wrap = header.Label && header.Label!.enableWordWrapping;
                header.Hud = header.Board.GetComponentInParent<HUD>();
                if (!header.Label || !header.Hud)
                {
                    log.LogWarning("The scoreboard's Items header wasn't found, your counts by tier don't show");
                }
            }

            if (!header.Label || !header.Hud)
            {
                return;
            }

            // The game writes its label each time the scoreboard opens and on a language change
            string current = header.Label!.text;
            if (header.Text == null || !string.Equals(current, header.Text, StringComparison.Ordinal))
            {
                header.GameLabel = current;
                header.Text = null;
                header.Label.enableWordWrapping = header.Wrap;
            }

            CounterText.Options options = settings.Current();
            CharacterMaster? master = header.Hud!.localUserViewer?.cachedMaster;
            Inventory? inventory = master ? master!.inventory : null;
            if (!inventory || inventory!.inventoryDisabled || (options & CounterText.Options.TierCounts) == 0)
            {
                HandBackHeader(header);
                return;
            }

            if (header.Stale(inventory, changes.GetOrCreateValue(inventory).Count, options))
            {
                header.Text = CounterText.Header(header.GameLabel, Count(inventory), options);
            }

            if (header.Text != null && !string.Equals(header.Label.text, header.Text, StringComparison.Ordinal))
            {
                // On one line however many tiers, the game's label wraps
                header.Label.enableWordWrapping = false;
                header.Label.text = header.Text;
            }
        }

        private Header? HeaderOf(ScoreboardStrip strip)
        {
            Row row = rows.GetOrCreateValue(strip);
            if (!row.Board)
            {
                row.Board = strip.GetComponentInParent<ScoreboardController>();
                if (!row.Board)
                {
                    return null;
                }
            }

            Header header = headers.GetOrCreateValue(row.Board!);
            header.Board = row.Board;
            return header;
        }

        // The game only writes a row's count when its total moves and the header when the scoreboard opens,
        // so ours would stay until then
        private void HandBack(ScoreboardStrip strip)
        {
            if (handBackFailed || !strip || !rows.TryGetValue(strip, out Row row))
            {
                return;
            }

            HandBackRow(strip, row);
            if (row.Board && headers.TryGetValue(row.Board!, out Header header))
            {
                HandBackHeader(header);
            }
        }

        private void HandBackRow(ScoreboardStrip strip, Row row)
        {
            if (handBackFailed || row.Text == null)
            {
                return;
            }

            try
            {
                if (strip.itemCountText && strip.itemInventoryDisplay
                    && string.Equals(strip.itemCountText.text, row.Text, StringComparison.Ordinal))
                {
                    strip.itemCountText.text = strip.itemInventoryDisplay.GetTotalVisibleItemCount().ToString(CultureInfo.InvariantCulture);
                }

                row.Text = null;
            }
            catch (Exception e)
            {
                handBackFailed = true;
                log.LogError($"Couldn't put the game's item count back on the scoreboard: {e}");
            }
        }

        private void HandBackHeader(Header header)
        {
            if (handBackFailed || !header.Label)
            {
                return;
            }

            try
            {
                if (header.Text != null && string.Equals(header.Label!.text, header.Text, StringComparison.Ordinal))
                {
                    header.Label.text = header.GameLabel ?? string.Empty;
                }

                header.Text = null;

                // Also after the game wrote its label over ours, which leaves our wrapping behind
                if (header.Label!.enableWordWrapping != header.Wrap)
                {
                    header.Label.enableWordWrapping = header.Wrap;
                }
            }
            catch (Exception e)
            {
                handBackFailed = true;
                log.LogError($"Couldn't put the scoreboard's Items header back: {e}");
            }
        }

        private CounterText.Tally Count(Inventory inventory)
        {
            tally.Clear();
            bool hiddenToo = countHidden();
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                var index = (ItemIndex)i;
                ItemDef def = ItemCatalog.GetItemDef(index);
                if (!def || (def.hidden && !hiddenToo))
                {
                    continue;
                }

                // Rented and shared items count as held, as in the game's own count
                long held = (long)inventory.GetItemCountPermanent(index)
                    + inventory.GetItemCountChanneled(index)
                    + inventory.GetItemCountRented(index);
                int permanent = (int)Math.Min(int.MaxValue, held);
                int temp = inventory.GetItemCountTemp(index);
                if (permanent != 0 || temp != 0)
                {
                    tally.Add(def.tier, permanent, temp);
                }
            }

            return tally;
        }

        // Our text for one place and what it was built from, so it's rebuilt only when that moves
        private class Shown
        {
            private Inventory? inventory;
            private int seenChanges;
            private CounterText.Options options;
            private float nextRefresh;

            internal string? Text { get; set; }

            internal bool Stale(Inventory current, int changeCount, CounterText.Options currentOptions)
            {
                float now = Time.unscaledTime;
                if (Text != null
                    && ReferenceEquals(inventory, current)
                    && seenChanges == changeCount
                    && options == currentOptions
                    && now < nextRefresh)
                {
                    return false;
                }

                inventory = current;
                seenChanges = changeCount;
                options = currentOptions;
                nextRefresh = now + RefreshSeconds;
                return true;
            }
        }

        private sealed class Row : Shown
        {
            internal ScoreboardController? Board { get; set; }
        }

        private sealed class Header : Shown
        {
            internal ScoreboardController? Board { get; set; }

            internal bool Searched { get; set; }

            internal TMP_Text? Label { get; set; }

            // The label's own wrapping, put back whenever ours isn't showing
            internal bool Wrap { get; set; }

            internal HUD? Hud { get; set; }

            internal string? GameLabel { get; set; }
        }

        private sealed class Changes
        {
            internal int Count { get; set; }
        }
    }
}
