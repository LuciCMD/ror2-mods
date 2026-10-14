using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;

namespace JustBeBetter.InventorySort
{
    internal enum TierOrder
    {
        RarestFirst,
        CommonestFirst,
        Off,
    }

    internal enum StackOrder
    {
        MostFirst,
        FewestFirst,
        Off,
    }

    internal enum ScrapPosition
    {
        First,
        Last,
        WithItsTier,
    }

    internal readonly struct SortRules : IEquatable<SortRules>
    {
        internal SortRules(TierOrder tiers, bool mixVoids, StackOrder stacks, ScrapPosition scrap)
        {
            Tiers = tiers;
            MixVoids = mixVoids;
            Stacks = stacks;
            Scrap = scrap;
        }

        internal TierOrder Tiers { get; }

        internal bool MixVoids { get; }

        internal StackOrder Stacks { get; }

        internal ScrapPosition Scrap { get; }

        public bool Equals(SortRules other)
        {
            return Tiers == other.Tiers && MixVoids == other.MixVoids && Stacks == other.Stacks && Scrap == other.Scrap;
        }

        public override bool Equals(object? obj)
        {
            return obj is SortRules other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Tiers, MixVoids, Stacks, Scrap);
        }
    }

    internal readonly struct ItemKey
    {
        internal ItemKey(ItemTier tier, bool scrap, int count)
        {
            Tier = tier;
            Scrap = scrap;
            Count = count;
        }

        internal ItemTier Tier { get; }

        internal bool Scrap { get; }

        internal int Count { get; }
    }

    // How Inventory Sort orders items, ties keep pickup order so equal items never swap places
    internal static class InventoryOrder
    {
        internal const int Tierless = int.MaxValue;

        // LookingGlass's tier order with the voids moved after the normal tiers (author)
        private static readonly ItemTier[] Rarest =
        {
            ItemTier.Hex, ItemTier.Lunar, ItemTier.FoodTier, ItemTier.Cursed, ItemTier.Page,
            ItemTier.Boss, ItemTier.Tier3, ItemTier.Tier2, ItemTier.Tier1,
            ItemTier.VoidBoss, ItemTier.VoidTier3, ItemTier.VoidTier2, ItemTier.VoidTier1,
        };

        private static readonly ItemTier[] Commonest =
        {
            ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss,
            ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss,
            ItemTier.Page, ItemTier.Cursed, ItemTier.FoodTier, ItemTier.Lunar, ItemTier.Hex,
        };

        // Keys in pickup order, the result is their indices in the wanted order
        internal static List<int> Arrange(IReadOnlyList<ItemKey> keys, SortRules rules)
        {
            if (keys == null)
            {
                return new List<int>();
            }

            return Enumerable.Range(0, keys.Count)
                .OrderBy(i => ScrapRank(keys[i].Scrap, rules.Scrap))
                .ThenBy(i => TierRank(keys[i].Tier, rules))
                .ThenBy(i => StackRank(keys[i].Count, rules.Stacks))
                .ThenBy(i => i)
                .ToList();
        }

        internal static int ScrapRank(bool scrap, ScrapPosition position)
        {
            if (!scrap)
            {
                return 0;
            }

            return position switch
            {
                ScrapPosition.First => -1,
                ScrapPosition.Last => 1,
                _ => 0,
            };
        }

        // Tierless items always last, modded tiers just before them in the game's order
        internal static int TierRank(ItemTier tier, SortRules rules)
        {
            if (rules.Tiers != TierOrder.RarestFirst && rules.Tiers != TierOrder.CommonestFirst)
            {
                return 0;
            }

            if (tier == ItemTier.NoTier)
            {
                return Tierless;
            }

            if (rules.MixVoids)
            {
                tier = NormalOf(tier);
            }

            ItemTier[] order = rules.Tiers == TierOrder.CommonestFirst ? Commonest : Rarest;
            int ranked = Array.IndexOf(order, tier);
            if (ranked >= 0)
            {
                return ranked;
            }

            // Clamped, so a stray value can't overflow ahead of the listed tiers or past tierless items
            return order.Length + Math.Clamp((int)tier, 0, Tierless - 1 - order.Length);
        }

        internal static long StackRank(int count, StackOrder order)
        {
            return order switch
            {
                StackOrder.MostFirst => -(long)count,
                StackOrder.FewestFirst => count,
                _ => 0,
            };
        }

        private static ItemTier NormalOf(ItemTier tier)
        {
            return tier switch
            {
                ItemTier.VoidTier1 => ItemTier.Tier1,
                ItemTier.VoidTier2 => ItemTier.Tier2,
                ItemTier.VoidTier3 => ItemTier.Tier3,
                ItemTier.VoidBoss => ItemTier.Boss,
                _ => tier,
            };
        }
    }
}
