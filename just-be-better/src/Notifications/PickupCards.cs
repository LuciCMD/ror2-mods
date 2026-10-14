using System;
using BepInEx.Logging;
using JustBeBetter.ItemInfo;
using RoR2;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;

namespace JustBeBetter.Notifications
{
    // The card at the bottom of the screen after a pickup, switched off or shown for longer or shorter
    // Hidden where the game makes each kind of pickup card, so corruption, artifact and trial cards still show,
    // and before the queue, where Item Info would take a card that never came for a merged one
    internal sealed class PickupCards : IFeature
    {
        private readonly NotificationSettings settings;
        private readonly PingCards pings;
        private readonly SkippedCards skipped;
        private readonly ManualLogSource log;
        private bool toldHidden;

        internal PickupCards(NotificationSettings settings, PingCards pings, SkippedCards skipped, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.pings = pings ?? throw new ArgumentNullException(nameof(pings));
            this.skipped = skipped ?? throw new ArgumentNullException(nameof(skipped));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            On.RoR2.CharacterMasterNotificationQueue.PushItemNotification_PushItemNotificationArgs += OnPushItem;
            On.RoR2.CharacterMasterNotificationQueue.PushEquipmentNotification += OnPushEquipment;
            On.RoR2.CharacterMasterNotificationQueue.PushDroneNotification += OnPushDrone;
            On.RoR2.CharacterMasterNotificationQueue.PushKeyItemNotification += OnPushKeyItem;
            On.RoR2.CharacterMasterNotificationQueue.PushNotification += OnPushNotification;
        }

        public void Disable()
        {
            On.RoR2.CharacterMasterNotificationQueue.PushItemNotification_PushItemNotificationArgs -= OnPushItem;
            On.RoR2.CharacterMasterNotificationQueue.PushEquipmentNotification -= OnPushEquipment;
            On.RoR2.CharacterMasterNotificationQueue.PushDroneNotification -= OnPushDrone;
            On.RoR2.CharacterMasterNotificationQueue.PushKeyItemNotification -= OnPushKeyItem;
            On.RoR2.CharacterMasterNotificationQueue.PushNotification -= OnPushNotification;
        }

        private void OnPushItem(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushItemNotification_PushItemNotificationArgs orig,
            CharacterMasterNotificationQueue.PushItemNotificationArgs args)
        {
            if (!Hidden())
            {
                orig(args);
                return;
            }

            // Only items, as the pickup card pairs only item counts
            if (args.characterMaster)
            {
                skipped.Skip(args.characterMaster, args.itemIndex);
            }
        }

        private void OnPushEquipment(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushEquipmentNotification orig,
            CharacterMaster characterMaster,
            EquipmentIndex equipmentIndex,
            int upgradeCount)
        {
            if (!Hidden())
            {
                orig(characterMaster, equipmentIndex, upgradeCount);
            }
        }

        private void OnPushDrone(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushDroneNotification orig,
            CharacterMaster characterMaster,
            DroneIndex droneIndex,
            int upgradeCount)
        {
            if (!Hidden())
            {
                orig(characterMaster, droneIndex, upgradeCount);
            }
        }

        private void OnPushKeyItem(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushKeyItemNotification orig,
            CharacterMaster characterMaster,
            KeyItemIndex keyItemIndex)
        {
            if (!Hidden())
            {
                orig(characterMaster, keyItemIndex);
            }
        }

        private void OnPushNotification(
            On.RoR2.CharacterMasterNotificationQueue.orig_PushNotification orig,
            CharacterMasterNotificationQueue self,
            NotificationInfo info,
            float duration)
        {
            orig(self, info, CardRules.Duration(duration, settings.CardDuration.Value, pings.Pushing));
        }

        private bool Hidden()
        {
            bool hide = CardRules.Hide(settings.ShowPickupCards.Value, pings.Pushing);

            // Once, so a test run shows the switch took
            if (hide && !toldHidden)
            {
                toldHidden = true;
                log.LogInfo("A pickup card was left out, Show Pickup Cards is off");
            }

            return hide;
        }
    }
}
