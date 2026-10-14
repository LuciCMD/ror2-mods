using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using JustBeBetter.ItemStats;
using RoR2;
using RoR2.UI;
using UnityEngine;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;

namespace JustBeBetter.ItemInfo
{
    // The card that appears after a pickup gets the full description and what the pickup changed
    // Set once the game has built the card, so its own special texts still win
    // A card for an item turning into another, such as its void version, gets the new item's change, an
    // equipment one keeps the game's text
    internal sealed class PickupDetails : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ItemInfoSettings settings;
        private readonly ManualLogSource log;
        private readonly PingCards pings;
        private readonly SkippedCards skipped;
        private readonly PickupChanges<NotificationInfo> changes = new PickupChanges<NotificationInfo>();

        // Each local player's item counts as last seen, to tell what a change added
        private readonly ConditionalWeakTable<Inventory, int[]> lastCounts = new ConditionalWeakTable<Inventory, int[]>();

        private FieldInfo? cardField;
        private FieldInfo? masterField;
        private FieldInfo? queueMasterField;
        private FieldInfo? queueListField;
        private FieldInfo? timedInfoField;

        // The card on screen, for a change that only turns up once it's showing
        private NotificationUIController? shownController;
        private NotificationInfo? shownInfo;
        private string? shownGameText;

        internal PickupDetails(ItemInfoSettings settings, ManualLogSource log, PingCards pings, SkippedCards skipped)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.pings = pings ?? throw new ArgumentNullException(nameof(pings));
            this.skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
        }

        public void Enable()
        {
            cardField = typeof(NotificationUIController).GetField("currentNotification", Private);
            masterField = typeof(NotificationUIController).GetField("targetMaster", Private);
            if (cardField == null)
            {
                log.LogWarning("The pickup card's internals have changed, it stays as the game has it");
                return;
            }

            if (masterField == null)
            {
                log.LogWarning("The pickup card's player can't be found, cards show descriptions without stats");
            }

            On.RoR2.UI.NotificationUIController.SetUpNotification += OnSetUpNotification;

            queueMasterField = typeof(CharacterMasterNotificationQueue).GetField("master", Private);
            queueListField = typeof(CharacterMasterNotificationQueue).GetField("notifications", Private);
            timedInfoField = typeof(CharacterMasterNotificationQueue).GetNestedType("TimedNotificationInfo", Any)?.GetField("notification", Any);

            // Hooked either way, as it also marks a ping's card
            On.RoR2.CharacterMasterNotificationQueue.PushNotification += OnPushNotification;
            if (!PairsChanges)
            {
                log.LogWarning("The pickup queue's internals have changed, cards read the change as of when they show");
                return;
            }

            Inventory.onInventoryChangedGlobal += OnInventoryChanged;
            changes.Matched += OnMatched;
            skipped.Skipped += OnSkipped;
        }

        public void Disable()
        {
            On.RoR2.UI.NotificationUIController.SetUpNotification -= OnSetUpNotification;
            On.RoR2.CharacterMasterNotificationQueue.PushNotification -= OnPushNotification;
            Inventory.onInventoryChangedGlobal -= OnInventoryChanged;
            changes.Matched -= OnMatched;
            skipped.Skipped -= OnSkipped;
        }

        // Stands in for the card that was left out, so it takes that pickup's change
        private void OnSkipped(CharacterMaster master, ItemIndex index)
        {
            try
            {
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (PairsChanges && item && !item.hidden && IsLocal(master))
                {
                    changes.CardQueued(new NotificationInfo(item, null, false, 0), (int)index, Time.unscaledTime, false);
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't drop a hidden pickup's change, the next card of that item may read it: {e}");
            }
        }

        private void OnPushNotification(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushNotification orig,
            CharacterMasterNotificationQueue self,
            NotificationInfo info,
            float duration)
        {
            // Marked first, as a card pushed to an empty queue is built inside orig
            // A ping's card isn't a pickup, so it mustn't take a pickup's count change either
            bool ping = pings.Pushing;
            if (ping)
            {
                pings.Add(info);
            }

            int before = Queued(self)?.Count ?? -1;
            orig(self, info, duration);
            if (ping)
            {
                return;
            }

            try
            {
                NoteQueued(self, info, before);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't note a queued pickup card: {e}");
            }
        }

        private bool PairsChanges => queueMasterField != null && queueListField != null && timedInfoField != null;

        private void NoteQueued(CharacterMasterNotificationQueue queue, NotificationInfo info, int before)
        {
            if (!PairsChanges || info?.data is not ItemDef item || !IsLocal(queueMasterField!.GetValue(queue) as CharacterMaster))
            {
                return;
            }

            // Not added means the game folded it into the card queued just before
            IList? list = Queued(queue);
            bool merged = list != null && list.Count == before && before > 0;
            NotificationInfo card = merged ? timedInfoField!.GetValue(list![before - 1]) as NotificationInfo ?? info : info;
            if (merged)
            {
                pings.PickedUp(card);
            }

            changes.CardQueued(card, (int)item.itemIndex, Time.unscaledTime, merged);
        }

        private IList? Queued(CharacterMasterNotificationQueue queue)
        {
            return queueListField?.GetValue(queue) as IList;
        }

        // Runs for every inventory in the game, so anything not a local player's is dropped first
        private void OnInventoryChanged(Inventory inventory)
        {
            try
            {
                if (inventory && IsLocalInventory(inventory))
                {
                    NoteCounts(inventory);
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't note an inventory change for the pickup card: {e}");
            }
        }

        // The first look only records, there's nothing to compare it with yet
        private void NoteCounts(Inventory inventory)
        {
            int itemCount = ItemCatalog.itemCount;
            bool seen = lastCounts.TryGetValue(inventory, out int[] last) && last.Length == itemCount;
            if (!seen)
            {
                lastCounts.Remove(inventory);
                last = new int[itemCount];
                lastCounts.Add(inventory, last);
            }

            float now = Time.unscaledTime;
            for (int i = 0; i < itemCount; i++)
            {
                int count = inventory.GetItemCountEffective((ItemIndex)i);
                if (seen && count > last[i])
                {
                    changes.CountChanged(i, last[i], count, now);
                }

                last[i] = count;
            }
        }

        private static bool IsLocal(CharacterMaster? master)
        {
            if (!master)
            {
                return false;
            }

            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (user.cachedMaster == master)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLocalInventory(Inventory inventory)
        {
            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                CharacterMaster master = user.cachedMaster;
                if (master && master.inventory == inventory)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnSetUpNotification(
            On.RoR2.UI.NotificationUIController.orig_SetUpNotification orig,
            NotificationUIController self,
            NotificationInfo notificationInfo)
        {
            orig(self, notificationInfo);
            try
            {
                Describe(self, notificationInfo);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't add details to the pickup card: {e}");
            }
        }

        private void Describe(NotificationUIController controller, NotificationInfo info)
        {
            // With neither full text nor stats there's nothing to add to the game's one-liner
            // A ping's card follows Ping Info, not Pickup Details
            if (!(settings.PickupDetails.Value || pings.Contains(info)) || !(settings.FullText || settings.Stats) || info == null
                || (info.transformation != null && info.data is not ItemDef) || !string.IsNullOrEmpty(info.overrides?.descriptionText))
            {
                return;
            }

            var card = cardField!.GetValue(controller) as GenericNotification;
            if (card == null || card.descriptionText == null)
            {
                return;
            }

            // Items get resolved text, equipment and drones a token, and a string that isn't a token resolves to itself
            // Kept so a late change can redo the card from the game's text rather than ours
            string gameText = Language.GetString(card.descriptionText.token ?? string.Empty);
            shownController = controller;
            shownInfo = info;
            shownGameText = gameText;
            Write(controller, card, info, gameText);
        }

        private void Write(NotificationUIController controller, GenericNotification card, NotificationInfo info, string gameText)
        {
            var master = masterField?.GetValue(controller) as CharacterMaster;
            if (settings.Stats)
            {
                ItemStatText.EnsureLoaded(log);
                ItemStats.Utils.CapChance = settings.CapChance.Value;
            }

            string arrow = StatArrow.Text(log, card.titleTMP != null ? card.titleTMP.font : null);
            string? text = TextFor(info, master ? master : null, gameText, arrow);

            // The game sets resolved text through the token too, a string that isn't a token shows as it is
            if (text != null)
            {
                card.descriptionText.token = text;
            }
        }

        // A ping's card says what picking the thing up would give, a pickup's what it changed
        private string? TextFor(NotificationInfo info, CharacterMaster? master, string gameText, string arrow)
        {
            if (pings.Contains(info))
            {
                return ItemStatText.DescribePing(info.data, master, gameText, settings.FullText, settings.Stats, arrow);
            }

            (int, int)? change = changes.TryGet(info, out int before, out int after) ? (before, after) : null;
            return ItemStatText.DescribePickup(info.data, master, gameText, settings.FullText, settings.Stats, arrow, change);
        }

        private void OnMatched(NotificationInfo info)
        {
            try
            {
                // By reference, as the game counts any two cards of one item equal
                if (!ReferenceEquals(info, shownInfo) || !shownController || shownGameText == null)
                {
                    return;
                }

                if (cardField!.GetValue(shownController) is GenericNotification card && card && card.descriptionText != null)
                {
                    Write(shownController!, card, info, shownGameText);
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't update the pickup card with its change: {e}");
            }
        }
    }
}
