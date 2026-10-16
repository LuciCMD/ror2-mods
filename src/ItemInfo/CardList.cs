using System;
using System.Collections.Generic;

namespace JustBeBetter.ItemInfo
{
    // The cards we've seen, so a setting change can refresh them while the HUD is hidden and a search wouldn't find them
    // Matched by reference, as Unity counts two destroyed objects as equal
    internal sealed class CardList<T> where T : class
    {
        private readonly List<T> cards = new List<T>();
        private readonly Func<T, bool> alive;

        internal CardList(Func<T, bool> alive)
        {
            this.alive = alive ?? throw new ArgumentNullException(nameof(alive));
        }

        internal bool Contains(T card)
        {
            foreach (T seen in cards)
            {
                if (ReferenceEquals(seen, card))
                {
                    return true;
                }
            }

            return false;
        }

        // Prunes as it goes, so the list doesn't grow with every stage's HUD
        internal bool Add(T card)
        {
            if (card == null || Contains(card))
            {
                return false;
            }

            cards.RemoveAll(seen => !alive(seen));
            cards.Add(card);
            return true;
        }

        internal List<T> Live()
        {
            cards.RemoveAll(card => !alive(card));
            return new List<T>(cards);
        }
    }
}
