using System.Collections.Generic;
using System.Linq;
using JustBeBetter.InventorySort;
using JustBeBetter.Pickers;
using RoR2;

// Inventory Sort's order, keys given in pickup order, and the Scrapper's By Tier following it
internal static partial class Program
{
    private static readonly SortRules Defaults = new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.First);

    private static void RunInventorySort()
    {
        Test("Inventory sort: Rarest First runs special tiers, boss to white, then the voids, tierless last", InvRarestFirst);
        Test("Inventory sort: Commonest First runs white to boss, the voids, then the special tiers", InvCommonestFirst);
        Test("Inventory sort: mixed voids sort in with their normal tier", InvMixVoids);
        Test("Inventory sort: scrap first, last or with its tier", InvScrap);
        Test("Inventory sort: stack size most, fewest or off, ties in pickup order", InvStacks);
        Test("Inventory sort: everything off keeps pickup order", InvAllOff);
        Test("Inventory sort: unknown values from a hand-edited config keep pickup order", InvUnknownValues);
        Test("Inventory sort: modded and stray tiers land before tierless items", InvStrayTiers);
        Test("Inventory sort: no items or no keys give an empty order", InvEmpty);
        Test("Scrapper By Tier follows the inventory rules, favourites still first", InvScrapperFollows);
        Test("Scrapper By Tier with no inventory rules keeps its own order", InvScrapperOwnOrder);
    }

    private static string InvArranged(IReadOnlyList<(string Name, ItemKey Key)> items, SortRules rules)
    {
        return string.Join(",", InventoryOrder.Arrange(items.Select(i => i.Key).ToList(), rules).Select(i => items[i].Name));
    }

    private static (string, ItemKey) Inv(string name, ItemTier tier, int count, bool scrap = false)
    {
        return (name, new ItemKey(tier, scrap, count));
    }

    private static List<(string Name, ItemKey Key)> EveryTier()
    {
        return new List<(string Name, ItemKey Key)>
        {
            Inv("t1", ItemTier.Tier1, 1),
            Inv("none", ItemTier.NoTier, 1),
            Inv("v1", ItemTier.VoidTier1, 1),
            Inv("t2", ItemTier.Tier2, 1),
            Inv("lunar", ItemTier.Lunar, 1),
            Inv("v2", ItemTier.VoidTier2, 1),
            Inv("t3", ItemTier.Tier3, 1),
            Inv("v3", ItemTier.VoidTier3, 1),
            Inv("boss", ItemTier.Boss, 1),
            Inv("vboss", ItemTier.VoidBoss, 1),
            Inv("food", ItemTier.FoodTier, 1),
            Inv("cursed", ItemTier.Cursed, 1),
            Inv("page", ItemTier.Page, 1),
            Inv("hex", ItemTier.Hex, 1),
            Inv("modded", (ItemTier)40, 1),
        };
    }

    private static void InvRarestFirst()
    {
        Equal(
            "hex,lunar,food,cursed,page,boss,t3,t2,t1,vboss,v3,v2,v1,modded,none",
            InvArranged(EveryTier(), Defaults));
    }

    private static void InvCommonestFirst()
    {
        Equal(
            "t1,t2,t3,boss,v1,v2,v3,vboss,page,cursed,food,lunar,hex,modded,none",
            InvArranged(EveryTier(), new SortRules(TierOrder.CommonestFirst, false, StackOrder.MostFirst, ScrapPosition.First)));
    }

    private static void InvMixVoids()
    {
        var items = new List<(string Name, ItemKey Key)>
        {
            Inv("syringe", ItemTier.Tier1, 2),
            Inv("lenses", ItemTier.VoidTier1, 5),
            Inv("ukulele", ItemTier.Tier2, 1),
            Inv("polylute", ItemTier.VoidTier2, 1),
            Inv("crowbar", ItemTier.Tier1, 3),
        };
        var mixed = new SortRules(TierOrder.RarestFirst, true, StackOrder.MostFirst, ScrapPosition.First);
        Equal("ukulele,polylute,lenses,crowbar,syringe", InvArranged(items, mixed));
        Equal("ukulele,crowbar,syringe,polylute,lenses", InvArranged(items, Defaults));
    }

    private static void InvScrap()
    {
        var items = new List<(string Name, ItemKey Key)>
        {
            Inv("behemoth", ItemTier.Tier3, 1),
            Inv("white scrap", ItemTier.Tier1, 4, true),
            Inv("syringe", ItemTier.Tier1, 2),
            Inv("red scrap", ItemTier.Tier3, 1, true),
        };
        Equal("red scrap,white scrap,behemoth,syringe", InvArranged(items, Defaults));
        Equal(
            "behemoth,syringe,red scrap,white scrap",
            InvArranged(items, new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.Last)));
        Equal(
            "behemoth,red scrap,white scrap,syringe",
            InvArranged(items, new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.WithItsTier)));
    }

    private static void InvStacks()
    {
        var items = new List<(string Name, ItemKey Key)>
        {
            Inv("a", ItemTier.Tier1, 2),
            Inv("b", ItemTier.Tier1, 5),
            Inv("c", ItemTier.Tier1, 2),
            Inv("d", ItemTier.Tier1, 1),
        };
        Equal("b,a,c,d", InvArranged(items, Defaults));
        Equal("d,a,c,b", InvArranged(items, new SortRules(TierOrder.RarestFirst, false, StackOrder.FewestFirst, ScrapPosition.First)));
        Equal("a,b,c,d", InvArranged(items, new SortRules(TierOrder.RarestFirst, false, StackOrder.Off, ScrapPosition.First)));
    }

    private static void InvAllOff()
    {
        var off = new SortRules(TierOrder.Off, true, StackOrder.Off, ScrapPosition.WithItsTier);
        List<(string Name, ItemKey Key)> items = EveryTier();
        items.Add(Inv("scrap", ItemTier.Tier1, 9, true));
        Equal(string.Join(",", items.Select(i => i.Name)), InvArranged(items, off));
    }

    private static void InvUnknownValues()
    {
        var odd = new SortRules((TierOrder)7, false, (StackOrder)7, (ScrapPosition)7);
        List<(string Name, ItemKey Key)> items = EveryTier();
        items.Add(Inv("scrap", ItemTier.Tier1, 9, true));
        Equal(string.Join(",", items.Select(i => i.Name)), InvArranged(items, odd));
    }

    // Failed if a stray tier added its raw value to the list length unchecked
    private static void InvStrayTiers()
    {
        foreach (TierOrder order in new[] { TierOrder.RarestFirst, TierOrder.CommonestFirst })
        {
            var rules = new SortRules(order, false, StackOrder.MostFirst, ScrapPosition.First);
            int none = InventoryOrder.TierRank(ItemTier.NoTier, rules);
            int hex = InventoryOrder.TierRank(ItemTier.Hex, rules);
            int voidWhite = InventoryOrder.TierRank(ItemTier.VoidTier1, rules);
            foreach (ItemTier stray in new[] { (ItemTier)int.MaxValue, (ItemTier)(-1), ItemTier.AssignedAtRuntime, (ItemTier)40 })
            {
                int rank = InventoryOrder.TierRank(stray, rules);
                Expect(rank < none, $"{order}: tier {(int)stray} ranked {rank}, not before tierless {none}");
                Expect(rank > hex && rank > voidWhite, $"{order}: tier {(int)stray} ranked {rank}, ahead of a listed tier");
            }
        }

        Expect(InventoryOrder.StackRank(int.MaxValue, StackOrder.MostFirst) < InventoryOrder.StackRank(0, StackOrder.MostFirst), "a huge stack overflowed");
    }

    private static void InvEmpty()
    {
        Expect(InventoryOrder.Arrange(new List<ItemKey>(), Defaults).Count == 0, "no items");
        Expect(InventoryOrder.Arrange(null!, Defaults).Count == 0, "no keys");
    }

    private static SortKey Scrapper(string name, ItemTier? tier, int count, bool favourite = false, bool scrap = false)
    {
        int rank = tier is ItemTier t ? PickerSort.TierRank(t) : PickerSort.NotAnItem;
        return new SortKey(name, rank, count, favourite, tier, scrap);
    }

    private static List<SortKey> ScrapperOptions()
    {
        return new List<SortKey>
        {
            Scrapper("syringe", ItemTier.Tier1, 2),
            Scrapper("lenses", ItemTier.VoidTier1, 3),
            Scrapper("behemoth", ItemTier.Tier3, 1),
            Scrapper("transcendence", ItemTier.Lunar, 1),
            Scrapper("knurl", ItemTier.Boss, 1, true),
            Scrapper("crowbar", ItemTier.Tier1, 4),
            Scrapper("preon", null, 9),
        };
    }

    private static string ScrapperArranged(List<SortKey> keys, SortRules? rules)
    {
        return string.Join(",", PickerSort.Arrange(keys, PickerOrder.ByTier, rules).Select(i => keys[i].Name));
    }

    private static void InvScrapperFollows()
    {
        Equal("knurl,transcendence,behemoth,crowbar,syringe,lenses,preon", ScrapperArranged(ScrapperOptions(), Defaults));
        Equal(
            "knurl,syringe,lenses,crowbar,behemoth,transcendence,preon",
            ScrapperArranged(ScrapperOptions(), new SortRules(TierOrder.CommonestFirst, true, StackOrder.FewestFirst, ScrapPosition.First)));
    }

    private static void InvScrapperOwnOrder()
    {
        Equal("knurl,crowbar,syringe,behemoth,lenses,transcendence,preon", ScrapperArranged(ScrapperOptions(), null));
    }
}
