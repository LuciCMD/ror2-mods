using System;
using System.Collections.Generic;
using System.Linq;
using JustBeBetter.Pickers;
using RoR2;

// Picker Sorting at its edges: duplicate options, odd names and counts, big lists, odd tiers
internal static partial class Program
{
    private static readonly PickerOrder[] AllOrders = { PickerOrder.GamesOrder, PickerOrder.MostHeldFirst, PickerOrder.AToZ, PickerOrder.ByTier };

    private static void RunPickerSortEdge()
    {
        Test("Picker sort edge: the Scrapper's permanent, temporary and rental options of one item stay in the game's order", SortEdgeTemporaryDuplicates);
        Test("Picker sort edge: names equal but for case keep the game's order", SortEdgeCaseOnlyNames);
        Test("Picker sort edge: every option a favourite sorts as if none were", SortEdgeAllFavourites);
        Test("Picker sort edge: a missing name sorts first in A to Z and never throws", SortEdgeNullNames);
        Test("Picker sort edge: negative and extreme counts sort by value", SortEdgeExtremeCounts);
        Test("Picker sort edge: accented names sort among the plain ones, not after Z", SortEdgeAccents);
        Test("Picker sort edge: equipment goes after every item, tierless ones included, in By Tier", SortEdgeEquipmentLast);
        Test("Picker sort edge: a 2000 option list is a permutation, favourites first, ties in the game's order", SortEdgeLargeList);
        Test("Picker sort edge: the same keys give the same order every call, so the frame check settles", SortEdgeDeterministic);
        Test("Picker sort edge: a negative unknown order keeps the game's order", SortEdgeNegativeOrder);
        Test("Picker sort edge: tiers far outside the game's range still rank after the voids", SortEdgeTierOverflow);
    }

    private static string ArrangedIndices(IReadOnlyList<SortKey> keys, PickerOrder order)
    {
        return string.Join(",", PickerSort.Arrange(keys, order));
    }

    private static void SortEdgeTemporaryDuplicates()
    {
        // Same name, tier and badge count, as PickerSearch builds them for one item held three ways
        var keys = new List<SortKey>
        {
            Key("Tougher Times", ItemTier.Tier1, 3),
            Key("Tougher Times", ItemTier.Tier1, 3),
            Key("Tougher Times", ItemTier.Tier1, 3),
            Key("Armor-Piercing Rounds", ItemTier.Tier1, 1),
        };
        Equal("0,1,2,3", ArrangedIndices(keys, PickerOrder.MostHeldFirst));
        Equal("0,1,2,3", ArrangedIndices(keys, PickerOrder.ByTier));
        Equal("3,0,1,2", ArrangedIndices(keys, PickerOrder.AToZ));
        Equal("0,1,2,3", ArrangedIndices(keys, PickerOrder.GamesOrder));
    }

    private static void SortEdgeCaseOnlyNames()
    {
        var keys = new List<SortKey>
        {
            Key("UKULELE", ItemTier.Tier2, 1),
            Key("ukulele", ItemTier.Tier2, 1),
            Key("Ukulele", ItemTier.Tier2, 1),
        };
        Equal("0,1,2", ArrangedIndices(keys, PickerOrder.AToZ));
    }

    private static void SortEdgeAllFavourites()
    {
        List<SortKey> plain = Mixed();
        List<SortKey> starred = plain.Select(k => new SortKey(k.Name, k.TierRank, k.Count, true)).ToList();
        foreach (PickerOrder order in AllOrders)
        {
            Equal(ArrangedIndices(plain, order), ArrangedIndices(starred, order));
        }
    }

    private static void SortEdgeNullNames()
    {
        var keys = new List<SortKey>
        {
            Key("Bison Steak", ItemTier.Tier1, 0),
            new SortKey(null, PickerSort.TierRank(ItemTier.Tier1), 0, false),
            Key("", ItemTier.Tier1, 0),
            Key("Armor-Piercing Rounds", ItemTier.Tier1, 0),
        };
        Equal("1,2,3,0", ArrangedIndices(keys, PickerOrder.AToZ));
        foreach (PickerOrder order in AllOrders)
        {
            Expect(PickerSort.Arrange(keys, order).Count == keys.Count, $"{order} kept every option");
        }
    }

    private static void SortEdgeExtremeCounts()
    {
        var keys = new List<SortKey>
        {
            Key("a", ItemTier.Tier1, -5),
            Key("b", ItemTier.Tier1, int.MaxValue),
            Key("c", ItemTier.Tier1, int.MinValue),
            Key("d", ItemTier.Tier1, 0),
            Key("e", ItemTier.Tier1, -5),
        };
        Equal("1,3,0,4,2", ArrangedIndices(keys, PickerOrder.MostHeldFirst));
        Equal("1,3,0,4,2", ArrangedIndices(keys, PickerOrder.ByTier));
    }

    private static void SortEdgeAccents()
    {
        var keys = new List<SortKey>
        {
            Key("Zeta", ItemTier.Tier1, 0),
            Key("Élan", ItemTier.Tier1, 0),
            Key("alpha", ItemTier.Tier1, 0),
            Key("Ödem", ItemTier.Tier1, 0),
        };
        Equal("alpha,Élan,Ödem,Zeta", Arranged(keys, PickerOrder.AToZ));
    }

    private static void SortEdgeEquipmentLast()
    {
        var keys = new List<SortKey>
        {
            new SortKey("Preon Accumulator", PickerSort.NotAnItem, 5, false),
            Key("Tierless", ItemTier.NoTier, 0),
            Key("Beads of Fealty", ItemTier.Lunar, 1),
            Key("Bison Steak", ItemTier.Tier1, 0),
        };
        Equal("Bison Steak,Beads of Fealty,Tierless,Preon Accumulator", Arranged(keys, PickerOrder.ByTier));
    }

    private static void SortEdgeLargeList()
    {
        var rng = new Random(1234);
        ItemTier[] tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar, ItemTier.VoidTier1, ItemTier.NoTier };
        var keys = new List<SortKey>();
        for (int i = 0; i < 2000; i++)
        {
            string name = "item " + (char)('a' + rng.Next(5)) + rng.Next(10);
            keys.Add(Key(name, tiers[rng.Next(tiers.Length)], rng.Next(4), rng.Next(10) == 0));
        }

        foreach (PickerOrder order in AllOrders)
        {
            List<int> result = PickerSort.Arrange(keys, order);
            Expect(result.Count == keys.Count && result.Distinct().Count() == keys.Count, $"{order} is a permutation");
            int favourites = keys.Count(k => k.Favourite);
            Expect(result.Take(favourites).All(i => keys[i].Favourite), $"{order} puts favourites first");
            for (int p = 1; p < result.Count; p++)
            {
                int a = result[p - 1], b = result[p];
                if (keys[a].Favourite != keys[b].Favourite)
                {
                    continue;
                }

                int cmp = Compare(keys[a], keys[b], order);
                Expect(cmp < 0 || (cmp == 0 && a < b), $"{order} at {p}: {keys[a].Name} ({a}) before {keys[b].Name} ({b})");
            }
        }
    }

    // The order as the setting's description states it, ties left to the caller
    private static int Compare(SortKey x, SortKey y, PickerOrder order)
    {
        switch (order)
        {
            case PickerOrder.MostHeldFirst:
                return y.Count.CompareTo(x.Count);
            case PickerOrder.AToZ:
                return StringComparer.InvariantCultureIgnoreCase.Compare(x.Name ?? string.Empty, y.Name ?? string.Empty);
            case PickerOrder.ByTier:
                int tier = x.TierRank.CompareTo(y.TierRank);
                return tier != 0 ? tier : y.Count.CompareTo(x.Count);
            default:
                return 0;
        }
    }

    private static void SortEdgeDeterministic()
    {
        List<SortKey> keys = Mixed();
        keys[2] = Key("Lost Seer's Lenses", ItemTier.VoidTier1, 3, true);
        foreach (PickerOrder order in AllOrders)
        {
            string first = ArrangedIndices(keys, order);
            for (int i = 0; i < 5; i++)
            {
                Equal(first, ArrangedIndices(keys, order));
            }
        }
    }

    private static void SortEdgeNegativeOrder()
    {
        Equal("0,1,2,3,4,5,6", ArrangedIndices(Mixed(), (PickerOrder)(-1)));
    }

    // Failed while TierRank added the raw enum value to Ranked.Length unchecked
    private static void SortEdgeTierOverflow()
    {
        int voidBoss = PickerSort.TierRank(ItemTier.VoidBoss);
        int huge = PickerSort.TierRank((ItemTier)int.MaxValue);
        int negative = PickerSort.TierRank((ItemTier)(-1));
        Expect(huge > voidBoss, $"tier int.MaxValue ranked {huge}, before the voids at {voidBoss}");
        Expect(negative > voidBoss, $"tier -1 ranked {negative}, not after the voids at {voidBoss}");
        Expect(huge < PickerSort.NotAnItem, $"tier int.MaxValue ranked {huge}, not before equipment");
    }
}
