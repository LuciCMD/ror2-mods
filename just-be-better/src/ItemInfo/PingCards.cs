using System.Runtime.CompilerServices;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;

namespace JustBeBetter.ItemInfo
{
    // Cards pushed for a ping, so the card shows what picking the thing up would give rather than a pickup
    internal sealed class PingCards
    {
        private static readonly object Mark = new object();

        private readonly ConditionalWeakTable<NotificationInfo, object> marked = new ConditionalWeakTable<NotificationInfo, object>();

        // True only while a ping's card is being pushed, so the queue hook can tell it from a pickup
        internal bool Pushing { get; set; }

        internal void Add(NotificationInfo info)
        {
            if (info != null && !marked.TryGetValue(info, out _))
            {
                marked.Add(info, Mark);
            }
        }

        // The game folds a pickup into an equal card queued just before, so a ping card it lands on becomes
        // that pickup's card and reads what the pickup did
        internal void PickedUp(NotificationInfo? card)
        {
            if (card != null)
            {
                marked.Remove(card);
            }
        }

        internal bool Contains(NotificationInfo? info)
        {
            return info != null && marked.TryGetValue(info, out _);
        }
    }
}
