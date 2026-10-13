using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;

namespace JustBeBetter.Pickers
{
    internal enum PickerOrder
    {
        GamesOrder,
        MostHeldFirst,
        AToZ,
        ByTier,
    }

    // The order of an item picker's buttons, favourites always first
    // Ties keep the game's order, so equal items never swap places
    internal static class PickerSort
    {
        internal const int NotAnItem = int.MaxValue;

        // Common to rare, then the voids the same way, then every other tier in the game's own order
        private static readonly ItemTier[] Ranked =
        {
            ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss,
            ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss,
        };

        internal static int TierRank(ItemTier tier)
        {
            int ranked = Array.IndexOf(Ranked, tier);
            if (ranked >= 0)
            {
                return ranked;
            }

            if (tier == ItemTier.NoTier)
            {
                return NotAnItem - 1;
            }

            // Clamped, so a stray value can't overflow ahead of the common tiers or past tierless items
            return Ranked.Length + Math.Clamp((int)tier, 0, NotAnItem - 2 - Ranked.Length);
        }

        // Keys in the game's order, the result is their indices in the wanted order
        internal static List<int> Arrange(IReadOnlyList<SortKey> keys, PickerOrder order)
        {
            if (keys == null)
            {
                return new List<int>();
            }

            IOrderedEnumerable<int> sorted = Enumerable.Range(0, keys.Count).OrderBy(i => keys[i].Favourite ? 0 : 1);
            switch (order)
            {
                case PickerOrder.MostHeldFirst:
                    sorted = sorted.ThenByDescending(i => keys[i].Count);
                    break;
                case PickerOrder.AToZ:
                    sorted = sorted.ThenBy(i => keys[i].Name ?? string.Empty, StringComparer.InvariantCultureIgnoreCase);
                    break;
                case PickerOrder.ByTier:
                    sorted = sorted.ThenBy(i => keys[i].TierRank).ThenByDescending(i => keys[i].Count);
                    break;
                default:
                    break;
            }

            return sorted.ThenBy(i => i).ToList();
        }
    }

    internal readonly struct SortKey
    {
        internal SortKey(string? name, int tierRank, int count, bool favourite)
        {
            Name = name;
            TierRank = tierRank;
            Count = count;
            Favourite = favourite;
        }

        internal string? Name { get; }

        internal int TierRank { get; }

        internal int Count { get; }

        internal bool Favourite { get; }
    }
}
