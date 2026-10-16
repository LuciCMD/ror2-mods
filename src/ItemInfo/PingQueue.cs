using System;
using System.Collections;

namespace JustBeBetter.ItemInfo
{
    // The game shows its card queue front first, and drops the front card once its time is up
    internal static class PingQueue
    {
        // Short enough that the front card goes on the game's next tick
        internal const float CutShort = 0.01f;

        // Moves a just-queued card to second and cuts the front one short, so it shows next
        // A card alone in the queue is already showing, so it's left as it is
        internal static bool BringForward(IList queue, object card, Action<object, float> setDuration)
        {
            if (queue == null || card == null || setDuration == null)
            {
                return false;
            }

            int at = queue.IndexOf(card);
            if (at <= 0)
            {
                return at == 0;
            }

            if (at > 1)
            {
                queue.RemoveAt(at);
                queue.Insert(1, card);
            }

            setDuration(queue[0]!, CutShort);
            return true;
        }
    }
}
