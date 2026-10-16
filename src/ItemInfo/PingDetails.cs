using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using UnityEngine;

namespace JustBeBetter.ItemInfo
{
    // Pinging an item, shop item or drone shows its pickup card at once, which PickupDetails then fills
    // SetCurrentPing runs once per ping on each machine, the pinger's own from AttemptPing and everyone else's
    // from the synced ping, so solo, host and client each see one card
    internal sealed class PingDetails : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const float CardSeconds = 5f;

        private readonly ItemInfoSettings settings;
        private readonly ManualLogSource log;
        private readonly PingCards pings;
        private readonly HashSet<string> logged = new HashSet<string>();

        private FieldInfo? queueListField;
        private FieldInfo? durationField;

        // The last pinged thing and its card, so pinging it again while the card is up adds nothing
        private GameObject? lastTarget;
        private object? lastCard;

        internal PingDetails(ItemInfoSettings settings, ManualLogSource log, PingCards pings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.pings = pings ?? throw new ArgumentNullException(nameof(pings));
        }

        public void Enable()
        {
            queueListField = typeof(CharacterMasterNotificationQueue).GetField("notifications", Private);
            durationField = typeof(CharacterMasterNotificationQueue).GetNestedType("TimedNotificationInfo", Any)?.GetField("duration", Any);
            if (queueListField == null || durationField == null)
            {
                log.LogWarning("The pickup queue's internals have changed, a ping's card waits behind the one showing");
            }

            On.RoR2.PingerController.SetCurrentPing += OnSetCurrentPing;
        }

        public void Disable()
        {
            On.RoR2.PingerController.SetCurrentPing -= OnSetCurrentPing;
        }

        private void OnSetCurrentPing(On.RoR2.PingerController.orig_SetCurrentPing orig, PingerController self, PingerController.PingInfo newPingInfo)
        {
            orig(self, newPingInfo);
            try
            {
                Show(self, newPingInfo.targetGameObject);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't show the card for a ping: {e}");
            }
        }

        private void Show(PingerController pinger, GameObject? target)
        {
            if (!settings.PingInfo.Value || target == null || !target)
            {
                return;
            }

            CharacterMaster? viewer = ViewerFor(pinger.GetComponent<CharacterMaster>());
            if (viewer == null || !viewer.hasAuthority)
            {
                return;
            }

            string? kind = PickupOf(target, out UniquePickup pickup);
            if (kind == null || pickup.pickupIndex == PickupIndex.none || !pickup.pickupIndex.isValid)
            {
                return;
            }

            CharacterMasterNotificationQueue queue = CharacterMasterNotificationQueue.GetNotificationQueueForMaster(viewer);
            IList? list = queue && durationField != null ? queueListField?.GetValue(queue) as IList : null;
            if (target == lastTarget && lastCard != null && list != null && list.Contains(lastCard))
            {
                return;
            }

            Push(viewer, pickup, list, target, kind);
        }

        private void Push(CharacterMaster viewer, UniquePickup pickup, IList? list, GameObject target, string kind)
        {
            int before = list?.Count ?? -1;
            pings.Pushing = true;
            try
            {
                CharacterMasterNotificationQueue.PushPickupNotification(viewer, pickup);
            }
            finally
            {
                pings.Pushing = false;
            }

            // Not added means the game folded it into an equal card queued just before, or has no card for it
            if (list == null || list.Count <= before)
            {
                Note(kind, list == null ? "queued behind the card showing" : "no new card, an equal one was queued or the game has none for it");
                return;
            }

            object card = list[list.Count - 1]!;
            FieldInfo duration = durationField!;
            duration.SetValue(card, CardSeconds);
            PingQueue.BringForward(list, card, (c, seconds) => duration.SetValue(c, seconds));
            lastTarget = target;
            lastCard = card;
            Note(kind, "shown");
        }

        // Your own ping shows on your card, another player's only with the setting, on the first local player's
        private CharacterMaster? ViewerFor(CharacterMaster? pinger)
        {
            foreach (LocalUser user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (pinger && user.cachedMaster == pinger)
                {
                    return pinger;
                }
            }

            if (!settings.OtherPlayersPings.Value)
            {
                return null;
            }

            CharacterMaster? first = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            return first ? first : null;
        }

        // What a pinged thing would give, and a name for the log, or null for anything else
        // Shops hide a "?" item unless the setting says otherwise, and a shop with no display has nothing to show
        private string? PickupOf(GameObject target, out UniquePickup pickup)
        {
            pickup = UniquePickup.none;
            bool showHidden = settings.ShowHiddenItems.Value;
            if (target.TryGetComponent(out GenericPickupController item))
            {
                pickup = item.pickup;
                return "item";
            }

            if (target.TryGetComponent(out DroneAvailability drone) && drone.droneDef)
            {
                pickup = new UniquePickup(PickupCatalog.FindPickupIndex(drone.droneDef.droneIndex));
                if (target.TryGetComponent(out SummonMasterBehavior summon))
                {
                    pickup.upgradeValue = summon.droneUpgradeCount;
                }

                return "drone";
            }

            if (target.TryGetComponent(out ShopTerminalBehavior shop))
            {
                if (shop.pickupDisplay && (showHidden || !shop.Networkhidden))
                {
                    pickup = shop.CurrentPickup();
                }

                return "shop";
            }

            if (target.TryGetComponent(out DroneVendorTerminalBehavior vendor))
            {
                if (showHidden || !vendor.Networkhidden)
                {
                    pickup = vendor.NetworkcurrentPickup;
                }

                return "drone shop";
            }

            if (target.TryGetComponent(out PickupDistributorBehavior distributor))
            {
                if (showHidden || !distributor.Networkhidden)
                {
                    pickup = distributor.GetPickup();
                }

                return "item shop";
            }

            return null;
        }

        // Once per kind and outcome, so a ping that shows nothing says why
        private void Note(string kind, string outcome)
        {
            if (Logs.VerboseOn && logged.Add($"{kind} {outcome}"))
            {
                log.Verbose($"Ping card for a pinged {kind}: {outcome}");
            }
        }
    }
}
