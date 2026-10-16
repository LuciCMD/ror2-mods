using System;
using RoR2;

namespace JustBeBetter.ItemInfo
{
    // Item cards Pickup Notifications left out, so the pickup card can drop the count change each stood for
    // Else that change waits for the next card of the item, such as the corruption card after a void pickup
    internal sealed class SkippedCards
    {
        internal event Action<CharacterMaster, ItemIndex>? Skipped;

        internal void Skip(CharacterMaster master, ItemIndex item)
        {
            Skipped?.Invoke(master, item);
        }
    }
}
