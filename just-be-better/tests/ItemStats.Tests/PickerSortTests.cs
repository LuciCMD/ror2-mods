using System.Collections.Generic;
using System.Linq;
using JustBeBetter.Pickers;
using RoR2;

// Picker Sorting's order, keys given in the game's order
internal static partial class Program
{
    private static void RunPickerSort()
    {
        Test("Picker sort: Game's Order keeps the game's order after the favourites", SortGamesOrder);
        Test("Picker sort: Most Held First, ties in the game's order", SortMostHeld);
        Test("Picker sort: A to Z ignores case", SortAToZ);
        Test("Picker sort: By Tier runs common to rare, then voids, most held first in each", SortByTier);
        Test("Picker sort: favourites come first and are sorted among themselves", SortFavouritesFirst);
        Test("Picker sort: tier ranks put the voids after yellow and other tiers after them", SortTierRanks);
        Test("Picker sort: no items or no keys give an empty order", SortEmpty);
        Test("Picker sort: an unknown order from a hand-edited config keeps the game's order", SortUnknownOrder);
        Test("Picker sort: unusable crafting items go last in every order, favourites still first", SortUnusableLast);
    }

    private static string Arranged(IReadOnlyList<SortKey> keys, PickerOrder order)
    {
        return string.Join(",", PickerSort.Arrange(keys, order).Select(i => keys[i].Name));
    }

    private static SortKey Key(string name, ItemTier tier, int count, bool favourite = false)
    {
        return new SortKey(name, PickerSort.TierRank(tier), count, favourite);
    }

    private static List<SortKey> Mixed()
    {
        return new List<SortKey>
        {
            Key("Soldier's Syringe", ItemTier.Tier1, 2),
            Key("ukulele", ItemTier.Tier2, 5),
            Key("Lost Seer's Lenses", ItemTier.VoidTier1, 3),
            Key("Brilliant Behemoth", ItemTier.Tier3, 1),
            Key("Armor-Piercing Rounds", ItemTier.Tier1, 2),
            Key("Titanic Knurl", ItemTier.Boss, 0),
            Key("Tougher Times", ItemTier.Tier1, 6),
        };
    }

    private static void SortGamesOrder()
    {
        List<SortKey> keys = Mixed();
        keys[3] = Key("Brilliant Behemoth", ItemTier.Tier3, 1, true);
        Equal(
            "Brilliant Behemoth,Soldier's Syringe,ukulele,Lost Seer's Lenses,Armor-Piercing Rounds,Titanic Knurl,Tougher Times",
            Arranged(keys, PickerOrder.GamesOrder));
    }

    private static void SortMostHeld()
    {
        Equal(
            "Tougher Times,ukulele,Lost Seer's Lenses,Soldier's Syringe,Armor-Piercing Rounds,Brilliant Behemoth,Titanic Knurl",
            Arranged(Mixed(), PickerOrder.MostHeldFirst));
    }

    private static void SortAToZ()
    {
        Equal(
            "Armor-Piercing Rounds,Brilliant Behemoth,Lost Seer's Lenses,Soldier's Syringe,Titanic Knurl,Tougher Times,ukulele",
            Arranged(Mixed(), PickerOrder.AToZ));
    }

    private static void SortByTier()
    {
        Equal(
            "Tougher Times,Soldier's Syringe,Armor-Piercing Rounds,ukulele,Brilliant Behemoth,Titanic Knurl,Lost Seer's Lenses",
            Arranged(Mixed(), PickerOrder.ByTier));
    }

    private static void SortFavouritesFirst()
    {
        List<SortKey> keys = Mixed();
        keys[0] = Key("Soldier's Syringe", ItemTier.Tier1, 2, true);
        keys[5] = Key("Titanic Knurl", ItemTier.Boss, 0, true);
        Equal(
            "Soldier's Syringe,Titanic Knurl,Tougher Times,Armor-Piercing Rounds,ukulele,Brilliant Behemoth,Lost Seer's Lenses",
            Arranged(keys, PickerOrder.ByTier));
        Equal(
            "Soldier's Syringe,Titanic Knurl,Armor-Piercing Rounds,Brilliant Behemoth,Lost Seer's Lenses,Tougher Times,ukulele",
            Arranged(keys, PickerOrder.AToZ));
    }

    private static void SortTierRanks()
    {
        ItemTier[] wanted =
        {
            ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss,
            ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss,
            ItemTier.Lunar, ItemTier.FoodTier, ItemTier.AssignedAtRuntime, ItemTier.NoTier,
        };
        for (int i = 1; i < wanted.Length; i++)
        {
            Expect(
                PickerSort.TierRank(wanted[i - 1]) < PickerSort.TierRank(wanted[i]),
                $"{wanted[i - 1]} before {wanted[i]}");
        }

        Expect(PickerSort.TierRank(ItemTier.NoTier) < PickerSort.NotAnItem, "a tierless item before equipment");
        Expect(PickerSort.TierRank((ItemTier)40) > PickerSort.TierRank(ItemTier.VoidBoss), "a modded tier after the voids");
        Expect(PickerSort.TierRank((ItemTier)40) < PickerSort.TierRank(ItemTier.NoTier), "a modded tier before tierless items");
    }

    private static void SortEmpty()
    {
        Expect(PickerSort.Arrange(new List<SortKey>(), PickerOrder.ByTier).Count == 0, "no items");
        Expect(PickerSort.Arrange(null!, PickerOrder.ByTier).Count == 0, "no keys");
    }

    private static void SortUnknownOrder()
    {
        Equal(
            "Soldier's Syringe,ukulele,Lost Seer's Lenses,Brilliant Behemoth,Armor-Piercing Rounds,Titanic Knurl,Tougher Times",
            Arranged(Mixed(), (PickerOrder)99));
    }

    private static SortKey Craft(string name, ItemTier tier, int count, bool unusable, bool favourite = false)
    {
        return new SortKey(name, PickerSort.TierRank(tier), count, favourite, tier, false, unusable);
    }

    // Crafting menu, unusable items after the rest in every order, favourites still first
    private static void SortUnusableLast()
    {
        var keys = new List<SortKey>
        {
            Craft("Bison Steak", ItemTier.Tier1, 3, true),
            Craft("Syringe", ItemTier.Tier1, 1, false),
            Craft("Behemoth", ItemTier.Tier3, 1, true, true),
            Craft("Ukulele", ItemTier.Tier2, 2, false),
            Craft("Crowbar", ItemTier.Tier1, 4, true),
        };
        Equal("Behemoth,Syringe,Ukulele,Bison Steak,Crowbar", Arranged(keys, PickerOrder.GamesOrder));
        Equal("Behemoth,Ukulele,Syringe,Crowbar,Bison Steak", Arranged(keys, PickerOrder.MostHeldFirst));
        Equal("Behemoth,Syringe,Ukulele,Bison Steak,Crowbar", Arranged(keys, PickerOrder.AToZ));
        Equal("Behemoth,Syringe,Ukulele,Crowbar,Bison Steak", Arranged(keys, PickerOrder.ByTier));
        var rules = new JustBeBetter.InventorySort.SortRules(
            JustBeBetter.InventorySort.TierOrder.RarestFirst, false, JustBeBetter.InventorySort.StackOrder.MostFirst, JustBeBetter.InventorySort.ScrapPosition.First);
        Equal(
            "Behemoth,Ukulele,Syringe,Crowbar,Bison Steak",
            string.Join(",", PickerSort.Arrange(keys, PickerOrder.ByTier, rules).Select(i => keys[i].Name)));
    }
}
