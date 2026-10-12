using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace JustBeBetter.ItemInfo
{
    // Pairs each pickup card with the count change it stands for, so a card shown seconds later in the queue
    // still reads what that pickup did, not whatever you hold by then
    // The card is queued and the inventory changes at about the same moment, in either order on a client, so
    // whichever comes second within the window finds the other
    // The game merges a repeat of the card just queued into it, so that card takes the later change as well
    // Plain C# with the time passed in, so the tests can drive it
    internal sealed class PickupChanges<TCard>
        where TCard : class
    {
        internal const float Window = 1f;

        private readonly ConditionalWeakTable<TCard, Change> matched = new ConditionalWeakTable<TCard, Change>();
        private readonly List<WaitingCard> waitingCards = new List<WaitingCard>();
        private readonly List<WaitingChange> waitingChanges = new List<WaitingChange>();

        // A card can be on screen before its change arrives, on a client the inventory syncs a moment later
        internal event Action<TCard>? Matched;

        internal void CardQueued(TCard card, int item, float time, bool merged)
        {
            Prune(time);
            int index = waitingChanges.FindIndex(c => c.Item == item);
            if (index >= 0)
            {
                Attach(card, waitingChanges[index].Before, waitingChanges[index].After, merged);
                waitingChanges.RemoveAt(index);
                return;
            }

            waitingCards.Add(new WaitingCard(card, item, time, merged));
        }

        internal void CountChanged(int item, int before, int after, float time)
        {
            if (after <= before)
            {
                return;
            }

            Prune(time);
            int index = waitingCards.FindIndex(c => c.Item == item);
            if (index >= 0)
            {
                Attach(waitingCards[index].Card, before, after, waitingCards[index].Merged);
                waitingCards.RemoveAt(index);
                return;
            }

            waitingChanges.Add(new WaitingChange(item, before, after, time));
        }

        internal bool TryGet(TCard card, out int before, out int after)
        {
            if (matched.TryGetValue(card, out Change change))
            {
                before = change.Before;
                after = change.After;
                return true;
            }

            before = after = 0;
            return false;
        }

        // A merged card keeps where it started and moves on to the newer count
        private void Attach(TCard card, int before, int after, bool merged)
        {
            if (merged && matched.TryGetValue(card, out Change existing))
            {
                existing.After = Math.Max(existing.After, after);
            }
            else
            {
                matched.Remove(card);
                matched.Add(card, new Change(before, after));
            }

            Matched?.Invoke(card);
        }

        private void Prune(float now)
        {
            waitingCards.RemoveAll(c => now - c.Time > Window);
            waitingChanges.RemoveAll(c => now - c.Time > Window);
        }

        private sealed class Change
        {
            internal Change(int before, int after)
            {
                Before = before;
                After = after;
            }

            internal int Before { get; }

            internal int After { get; set; }
        }

        private readonly struct WaitingCard
        {
            internal WaitingCard(TCard card, int item, float time, bool merged)
            {
                Card = card;
                Item = item;
                Time = time;
                Merged = merged;
            }

            internal TCard Card { get; }

            internal int Item { get; }

            internal float Time { get; }

            internal bool Merged { get; }
        }

        private readonly struct WaitingChange
        {
            internal WaitingChange(int item, int before, int after, float time)
            {
                Item = item;
                Before = before;
                After = after;
                Time = time;
            }

            internal int Item { get; }

            internal int Before { get; }

            internal int After { get; }

            internal float Time { get; }
        }
    }
}
