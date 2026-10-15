using System;
using System.Collections.Generic;
using System.Linq;
using JustBeBetter.InventorySort;
using JustBeBetter.Pickers;
using RoR2;

// Crafting Sorting at its edges: unusable items against every order and Inventory Sort rule, the re-attach the
// game does on every ingredient and inventory change, the live Usable Items First toggle and the recipe walk
// PickerFilter and PickerSearch.Attach are Unity-bound, so MirrorCraftFilter copies their caching and sibling
// moves line for line, the sorting itself is the real PickerSort
internal static partial class Program
{
    private static void RunCraftingSortEdge()
    {
        Test("Crafting sort edge: slots filling and clearing (availability only) never move a button", CraftEdgeAvailabilityOnly);
        Test("Crafting sort edge: a temporary stack decaying while the menu is open doesn't move buttons", CraftEdgeTemporaryDecay);
        Test("Crafting sort edge: picking up a stack while the menu is open doesn't move buttons", CraftEdgePickupWhileOpen);
        Test("Crafting sort edge: Usable Items First toggled live moves the unusable ones and settles in one frame", CraftEdgeLiveToggle);
        Test("Crafting sort edge: changing the order or Inventory Sort while open re-sorts with unusable still last", CraftEdgeLiveOrderAndRules);
        Test("Crafting sort edge: starring an unusable item lifts it to the front, unstarring drops it back last", CraftEdgeFavouriteUnusable);
        Test("Crafting sort edge: unusable never reshuffles the usable ones, in every order and Inventory Sort rule", CraftEdgeSubsequences);
        Test("Crafting sort edge: every order and rule with unusable is a permutation, favourites then usable first", CraftEdgeLargeRandom);
        Test("Crafting sort edge: every option unusable sorts as if none were", CraftEdgeAllUnusable);
        Test("Crafting sort edge: equipment a recipe takes stays with the usable ones, ahead of unusable items", CraftEdgeEquipmentUsable);
        Test("Crafting sort edge: the recipe walk keeps equipment, skips null recipes and empty ones, merges duplicates", CraftEdgeRecipeWalk);
        Test("Crafting sort edge: held equipment goes after the items in Most Held First, as in LookingGlass", CraftEdgeEquipmentCount);
    }

    private static readonly SortRules?[] CraftRules = AllCraftRules();

    private static SortRules?[] AllCraftRules()
    {
        var all = new List<SortRules?> { null };
        foreach (TierOrder t in Enum.GetValues(typeof(TierOrder)))
        {
            foreach (StackOrder s in Enum.GetValues(typeof(StackOrder)))
            {
                foreach (ScrapPosition p in Enum.GetValues(typeof(ScrapPosition)))
                {
                    all.Add(new SortRules(t, false, s, p));
                    all.Add(new SortRules(t, true, s, p));
                }
            }
        }

        return all.ToArray();
    }

    // One option as CraftingController lists it, equipment has no tier
    private sealed class CraftOption
    {
        internal CraftOption(int pickup, string name, ItemTier? tier, int permanent, int temporary = 0, bool scrap = false)
        {
            Pickup = new PickupIndex(pickup);
            Name = name;
            Tier = tier;
            Permanent = permanent;
            Temporary = temporary;
            Scrap = scrap;
        }

        internal PickupIndex Pickup { get; }

        internal string Name { get; }

        internal ItemTier? Tier { get; }

        internal int Permanent { get; set; }

        internal int Temporary { get; set; }

        internal bool Scrap { get; }

        internal bool Available { get; set; } = true;
    }

    // PickerFilter's Wanted, Arranged, Sorted, Order and InOrder, buttons are the option indices the allocator keeps
    private sealed class MirrorCraftFilter
    {
        private readonly List<(int Button, string Name, int TierRank, int Count, ItemTier? Tier, bool Scrap, bool Unusable)> entries = new();
        private readonly int[] sibling;
        private readonly HashSet<string> favourites = new();
        private readonly OpenCounts openCounts = new();
        private List<int>? arranged;
        private PickerOrder arrangedFor;
        private SortRules? arrangedRules;
        private bool arrangedUsable;

        internal MirrorCraftFilter(int buttons)
        {
            sibling = Enumerable.Range(0, buttons).ToArray();
        }

        internal PickerOrder Sorting { get; set; } = PickerOrder.ByTier;

        internal SortRules? Rules { get; set; }

        internal bool UsableFirst { get; set; } = true;

        internal int Moves { get; private set; }

        // PickerSearch.Attach, counts as HeldCount reads them now kept at their opening value as PickerFilter.Add does,
        // unusable from the recipe set
        internal void Attach(IReadOnlyList<CraftOption> options, HashSet<PickupIndex>? usable)
        {
            entries.Clear();
            arranged = null;
            for (int i = 0; i < options.Count; i++)
            {
                CraftOption o = options[i];
                entries.Add((
                    i,
                    o.Name,
                    o.Tier is ItemTier t ? PickerSort.TierRank(t) : PickerSort.NotAnItem,
                    openCounts.Keep(o.Name, o.Tier.HasValue ? o.Permanent + o.Temporary : 0),
                    o.Tier,
                    o.Scrap,
                    usable != null && !usable.Contains(o.Pickup)));
            }

            Order();
        }

        internal void ToggleFavourite(string name)
        {
            if (!favourites.Remove(name))
            {
                favourites.Add(name);
            }

            arranged = null;
            Order();
        }

        // True when the frame had to move something
        internal bool LateUpdate()
        {
            if (Wanted().SequenceEqual(SortedButtons()))
            {
                return false;
            }

            Order();
            return true;
        }

        internal string Shown()
        {
            return string.Join(",", SortedButtons().Select(b => entries.First(e => e.Button == b).Name));
        }

        private List<int> SortedButtons()
        {
            return entries.Select(e => e.Button).OrderBy(b => sibling[b]).ToList();
        }

        private List<int> Wanted()
        {
            bool usable = UsableFirst;
            if (arranged == null || arrangedFor != Sorting || !Nullable.Equals(arrangedRules, Rules) || arrangedUsable != usable)
            {
                arrangedFor = Sorting;
                arrangedRules = Rules;
                arrangedUsable = usable;
                List<SortKey> keys = entries
                    .Select(e => new SortKey(e.Name, e.TierRank, e.Count, favourites.Contains(e.Name), e.Tier, e.Scrap, usable && e.Unusable))
                    .ToList();
                arranged = PickerSort.Arrange(keys, arrangedFor, Rules).Select(i => entries[i].Button).ToList();
            }

            return arranged;
        }

        private void Order()
        {
            List<int> current = SortedButtons();
            List<int> slots = current.Select(b => sibling[b]).ToList();
            List<int> wanted = Wanted();
            for (int i = 0; i < wanted.Count; i++)
            {
                if (sibling[wanted[i]] != slots[i])
                {
                    Moves++;
                }

                sibling[wanted[i]] = slots[i];
            }
        }
    }

    // A Wandering Chef menu: the held equipment first, then items in pickup order, as GetGeneratedOptionsFromInteractor
    private static List<CraftOption> ChefOptions()
    {
        return new List<CraftOption>
        {
            new CraftOption(900, "Preon Accumulator", null, 1),
            new CraftOption(1, "Bison Steak", ItemTier.Tier1, 2),
            new CraftOption(2, "Soldier's Syringe", ItemTier.Tier1, 2),
            new CraftOption(3, "Ukulele", ItemTier.Tier2, 1),
            new CraftOption(4, "Scrap, White", ItemTier.Tier1, 3, 0, true),
            new CraftOption(5, "Brilliant Behemoth", ItemTier.Tier3, 1),
            new CraftOption(6, "Lost Seer's Lenses", ItemTier.VoidTier1, 4),
        };
    }

    // Steak and Lenses no recipe takes, the equipment one does
    private static HashSet<PickupIndex> ChefUsable()
    {
        return new HashSet<PickupIndex> { new PickupIndex(900), new PickupIndex(2), new PickupIndex(3), new PickupIndex(4), new PickupIndex(5) };
    }

    private static void CraftEdgeAvailabilityOnly()
    {
        foreach (PickerOrder order in AllOrders)
        {
            foreach (SortRules? rules in CraftRules)
            {
                foreach (bool usableFirst in new[] { true, false })
                {
                    List<CraftOption> options = ChefOptions();
                    var filter = new MirrorCraftFilter(options.Count) { Sorting = order, Rules = rules, UsableFirst = usableFirst };
                    string where = $"{order} {Describe(rules)} usableFirst {usableFirst}";

                    // OnDisplayBegin, OnIngredientsChanged and ClearAllSlots each set the options once
                    filter.Attach(options, ChefUsable());
                    string opened = filter.Shown();
                    filter.Attach(options, ChefUsable());
                    filter.Attach(options, ChefUsable());
                    Equal(opened, filter.Shown());

                    // First slot filled, only the other ingredients of its recipes stay available
                    foreach (CraftOption o in options)
                    {
                        o.Available = o.Name == "Ukulele" || o.Name == "Scrap, White";
                    }

                    filter.Attach(options, ChefUsable());
                    Expect(filter.Shown() == opened, $"{where}: moved after a slot filled, {opened} -> {filter.Shown()}");

                    // All slots filled locks everything
                    options.ForEach(o => o.Available = false);
                    filter.Attach(options, ChefUsable());
                    Expect(filter.Shown() == opened, $"{where}: moved with every slot filled, {opened} -> {filter.Shown()}");

                    options.ForEach(o => o.Available = true);
                    filter.Attach(options, ChefUsable());
                    Expect(filter.Shown() == opened, $"{where}: moved after the slots cleared, {opened} -> {filter.Shown()}");
                    Expect(!filter.LateUpdate(), $"{where}: the frame check moved buttons after the re-attach");
                }
            }
        }
    }

    // CraftingController subscribes OnIngredientsChanged to inventory.onInventoryChanged, so each decayed temporary
    // stack re-sets the options, and Attach reads HeldCount's effective count again
    private static void CraftEdgeTemporaryDecay()
    {
        var failures = new List<string>();
        foreach (PickerOrder order in new[] { PickerOrder.MostHeldFirst, PickerOrder.ByTier })
        {
            foreach (SortRules? rules in new SortRules?[] { null, new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.WithItsTier) })
            {
                var options = new List<CraftOption>
                {
                    new CraftOption(2, "Soldier's Syringe", ItemTier.Tier1, 2),
                    new CraftOption(7, "Armor-Piercing Rounds", ItemTier.Tier1, 1, 2),
                };
                var usable = new HashSet<PickupIndex> { new PickupIndex(2), new PickupIndex(7) };
                var filter = new MirrorCraftFilter(options.Count) { Sorting = order, Rules = rules };
                filter.Attach(options, usable);
                string opened = filter.Shown();

                options[1].Temporary = 0;
                filter.Attach(options, usable);
                if (filter.Shown() != opened)
                {
                    failures.Add($"{order} {Describe(rules)}: {opened} -> {filter.Shown()}");
                }
            }
        }

        Expect(failures.Count == 0, "buttons moved while the menu stayed open: " + string.Join("; ", failures));
    }

    private static void CraftEdgePickupWhileOpen()
    {
        var options = new List<CraftOption>
        {
            new CraftOption(2, "Soldier's Syringe", ItemTier.Tier1, 3),
            new CraftOption(3, "Ukulele", ItemTier.Tier2, 1),
            new CraftOption(7, "Armor-Piercing Rounds", ItemTier.Tier1, 2),
        };
        var usable = new HashSet<PickupIndex> { new PickupIndex(2), new PickupIndex(3), new PickupIndex(7) };
        var filter = new MirrorCraftFilter(options.Count) { Sorting = PickerOrder.MostHeldFirst };
        filter.Attach(options, usable);
        string opened = filter.Shown();

        // Two Armor-Piercing Rounds land while the menu is open, Close on Movement off
        options[2].Permanent = 4;
        filter.Attach(options, usable);
        Expect(filter.Shown() == opened, $"buttons moved while the menu stayed open: {opened} -> {filter.Shown()}");
    }

    private static void CraftEdgeLiveToggle()
    {
        foreach (PickerOrder order in AllOrders)
        {
            List<CraftOption> options = ChefOptions();
            var filter = new MirrorCraftFilter(options.Count) { Sorting = order, UsableFirst = false };
            filter.Attach(options, ChefUsable());
            string off = filter.Shown();

            filter.UsableFirst = true;
            Expect(filter.LateUpdate(), $"{order}: turning Usable Items First on didn't move anything");
            Expect(!filter.LateUpdate(), $"{order}: still moving a frame after turning it on");
            string on = filter.Shown();
            string[] names = on.Split(',');
            Expect(
                names.Skip(names.Length - 2).OrderBy(n => n).SequenceEqual(new[] { "Bison Steak", "Lost Seer's Lenses" }),
                $"{order}: on reads {on}, the unusable two aren't last");

            filter.UsableFirst = false;
            filter.LateUpdate();
            Expect(!filter.LateUpdate(), $"{order}: still moving a frame after turning it off");
            Equal(off, filter.Shown());

            // Flipped twice between frames is no change at all
            filter.UsableFirst = true;
            filter.UsableFirst = false;
            Expect(!filter.LateUpdate(), $"{order}: an on-off flip between frames moved buttons");
        }
    }

    private static void CraftEdgeLiveOrderAndRules()
    {
        List<CraftOption> options = ChefOptions();
        var filter = new MirrorCraftFilter(options.Count) { Sorting = PickerOrder.GamesOrder };
        filter.Attach(options, ChefUsable());
        foreach (PickerOrder order in AllOrders.Reverse())
        {
            foreach (SortRules? rules in CraftRules)
            {
                filter.Sorting = order;
                filter.Rules = rules;
                filter.LateUpdate();
                Expect(!filter.LateUpdate(), $"{order} {Describe(rules)}: didn't settle in one frame");
                string[] names = filter.Shown().Split(',');
                Expect(
                    names.Skip(names.Length - 2).OrderBy(n => n).SequenceEqual(new[] { "Bison Steak", "Lost Seer's Lenses" }),
                    $"{order} {Describe(rules)}: reads {filter.Shown()}, the unusable two aren't last");
            }
        }
    }

    private static void CraftEdgeFavouriteUnusable()
    {
        foreach (PickerOrder order in AllOrders)
        {
            List<CraftOption> options = ChefOptions();
            var filter = new MirrorCraftFilter(options.Count) { Sorting = order };
            filter.Attach(options, ChefUsable());
            string before = filter.Shown();

            filter.ToggleFavourite("Lost Seer's Lenses");
            Expect(filter.Shown().StartsWith("Lost Seer's Lenses,", StringComparison.Ordinal), $"{order}: starred unusable reads {filter.Shown()}");
            Expect(filter.Shown().EndsWith(",Bison Steak", StringComparison.Ordinal), $"{order}: the other unusable one left the end, {filter.Shown()}");

            // Re-attach on the next ingredient change keeps the star's place
            string starred = filter.Shown();
            filter.Attach(options, ChefUsable());
            Equal(starred, filter.Shown());

            filter.ToggleFavourite("Lost Seer's Lenses");
            Equal(before, filter.Shown());
        }
    }

    private static void CraftEdgeSubsequences()
    {
        var rng = new Random(77);
        ItemTier[] tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar, ItemTier.VoidTier2, ItemTier.FoodTier, ItemTier.NoTier };
        for (int round = 0; round < 40; round++)
        {
            var keys = new List<SortKey>();
            int n = rng.Next(1, 30);
            for (int i = 0; i < n; i++)
            {
                bool equipment = rng.Next(8) == 0;
                ItemTier tier = tiers[rng.Next(tiers.Length)];
                keys.Add(new SortKey(
                    "x" + rng.Next(6),
                    equipment ? PickerSort.NotAnItem : PickerSort.TierRank(tier),
                    equipment ? 0 : rng.Next(4),
                    rng.Next(6) == 0,
                    equipment ? null : tier,
                    !equipment && rng.Next(5) == 0,
                    rng.Next(3) == 0));
            }

            foreach (PickerOrder order in AllOrders)
            {
                foreach (SortRules? rules in CraftRules)
                {
                    List<int> all = PickerSort.Arrange(keys, order, rules);
                    foreach (bool favourite in new[] { true, false })
                    {
                        foreach (bool unusable in new[] { true, false })
                        {
                            List<int> group = Enumerable.Range(0, keys.Count).Where(i => keys[i].Favourite == favourite && keys[i].Unusable == unusable).ToList();
                            List<SortKey> alone = group.Select(i => keys[i]).ToList();
                            List<int> expected = PickerSort.Arrange(alone, order, rules).Select(j => group[j]).ToList();
                            List<int> got = all.Where(i => keys[i].Favourite == favourite && keys[i].Unusable == unusable).ToList();
                            Expect(
                                expected.SequenceEqual(got),
                                $"round {round} {order} {Describe(rules)} favourite {favourite} unusable {unusable}: {string.Join(",", got)} not {string.Join(",", expected)}");
                        }
                    }
                }
            }
        }
    }

    private static void CraftEdgeLargeRandom()
    {
        var rng = new Random(4242);
        ItemTier[] tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar, ItemTier.VoidTier1, ItemTier.NoTier };
        var keys = new List<SortKey>();
        for (int i = 0; i < 1500; i++)
        {
            ItemTier tier = tiers[rng.Next(tiers.Length)];
            bool equipment = rng.Next(20) == 0;
            keys.Add(new SortKey(
                "item " + (char)('a' + rng.Next(5)),
                equipment ? PickerSort.NotAnItem : PickerSort.TierRank(tier),
                rng.Next(5),
                rng.Next(10) == 0,
                equipment ? null : tier,
                rng.Next(7) == 0,
                rng.Next(4) == 0));
        }

        foreach (PickerOrder order in AllOrders.Concat(new[] { (PickerOrder)99 }))
        {
            foreach (SortRules? rules in CraftRules)
            {
                List<int> result = PickerSort.Arrange(keys, order, rules);
                Expect(result.Count == keys.Count && result.Distinct().Count() == keys.Count, $"{order} {Describe(rules)} is a permutation");
                int Group(int i) => (keys[i].Favourite ? 0 : 2) + (keys[i].Unusable ? 1 : 0);
                for (int p = 1; p < result.Count; p++)
                {
                    Expect(Group(result[p - 1]) <= Group(result[p]), $"{order} {Describe(rules)} at {p}: group {Group(result[p - 1])} before {Group(result[p])}");
                }
            }
        }
    }

    private static void CraftEdgeAllUnusable()
    {
        List<SortKey> plain = Mixed();
        List<SortKey> unusable = plain.Select(k => new SortKey(k.Name, k.TierRank, k.Count, k.Favourite, k.Tier, k.Scrap, true)).ToList();
        foreach (PickerOrder order in AllOrders)
        {
            foreach (SortRules? rules in CraftRules)
            {
                Equal(string.Join(",", PickerSort.Arrange(plain, order, rules)), string.Join(",", PickerSort.Arrange(unusable, order, rules)));
            }
        }
    }

    private static void CraftEdgeEquipmentUsable()
    {
        foreach (PickerOrder order in AllOrders)
        {
            foreach (SortRules? rules in CraftRules)
            {
                List<CraftOption> options = ChefOptions();
                var filter = new MirrorCraftFilter(options.Count) { Sorting = order, Rules = rules };
                filter.Attach(options, ChefUsable());
                string[] names = filter.Shown().Split(',');
                int equipment = Array.IndexOf(names, "Preon Accumulator");
                Expect(
                    equipment < Array.IndexOf(names, "Bison Steak") && equipment < Array.IndexOf(names, "Lost Seer's Lenses"),
                    $"{order} {Describe(rules)}: {filter.Shown()} puts the usable equipment among the unusable");

                // Equipment no recipe takes goes with the unusable ones
                var withoutEquipment = new HashSet<PickupIndex>(ChefUsable());
                withoutEquipment.Remove(new PickupIndex(900));
                filter.Attach(options, withoutEquipment);
                names = filter.Shown().Split(',');
                Expect(
                    names.Skip(names.Length - 3).Contains("Preon Accumulator"),
                    $"{order} {Describe(rules)}: {filter.Shown()} keeps unusable equipment among the usable");
            }
        }
    }

    // PickerSearch.UsableIngredients after FilterByEntitlement, on the game's own RecipeEntry.GetAllPickups
    private static HashSet<PickupIndex>? MirrorUsable(CraftableCatalog.RecipeEntry?[] recipes)
    {
        var found = new HashSet<PickupIndex>();
        try
        {
            foreach (CraftableCatalog.RecipeEntry? recipe in recipes)
            {
                List<PickupIndex>? pickups = recipe?.GetAllPickups();
                if (pickups != null)
                {
                    found.UnionWith(pickups);
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return found;
    }

    private static CraftableCatalog.RecipeEntry Recipe(params int[][] slots)
    {
        return new CraftableCatalog.RecipeEntry
        {
            possibleIngredients = slots
                .Select((pickups, i) => new CraftableCatalog.IngredientSlotEntry(i) { pickups = pickups.Select(p => new PickupIndex(p)).ToArray() })
                .ToArray(),
        };
    }

    private static void CraftEdgeRecipeWalk()
    {
        var recipes = new CraftableCatalog.RecipeEntry?[]
        {
            Recipe(new[] { 900, 2 }, new[] { 3 }),
            null,
            Recipe(),
            Recipe(new[] { 2, 2 }, new[] { 5 }),
        };
        HashSet<PickupIndex>? usable = MirrorUsable(recipes);
        Expect(usable != null, "the walk failed");
        Equal("2,3,5,900", string.Join(",", usable!.Select(p => p.value).OrderBy(v => v)));

        List<CraftOption> options = ChefOptions();
        var filter = new MirrorCraftFilter(options.Count) { Sorting = PickerOrder.GamesOrder };
        filter.Attach(options, usable);
        Equal("Preon Accumulator,Soldier's Syringe,Ukulele,Brilliant Behemoth,Bison Steak,Scrap, White,Lost Seer's Lenses", filter.Shown());

        // No recipes at all, the set is empty so everything counts as unusable and keeps its sorted place
        filter.Attach(options, MirrorUsable(Array.Empty<CraftableCatalog.RecipeEntry?>()));
        Equal("Preon Accumulator,Bison Steak,Soldier's Syringe,Ukulele,Scrap, White,Brilliant Behemoth,Lost Seer's Lenses", filter.Shown());
    }

    // HeldCount gives 0 to anything that isn't an item, so held equipment goes after the items as LookingGlass has it
    private static void CraftEdgeEquipmentCount()
    {
        var options = new List<CraftOption>
        {
            new CraftOption(900, "Preon Accumulator", null, 1),
            new CraftOption(2, "Soldier's Syringe", ItemTier.Tier1, 3),
            new CraftOption(3, "Ukulele", ItemTier.Tier2, 1),
            new CraftOption(5, "Brilliant Behemoth", ItemTier.Tier3, 1),
        };
        var usable = new HashSet<PickupIndex> { new PickupIndex(900), new PickupIndex(2), new PickupIndex(3), new PickupIndex(5) };
        var filter = new MirrorCraftFilter(options.Count) { Sorting = PickerOrder.MostHeldFirst };
        filter.Attach(options, usable);
        Equal("Soldier's Syringe,Ukulele,Brilliant Behemoth,Preon Accumulator", filter.Shown());
    }

    private static string Describe(SortRules? rules)
    {
        return rules is SortRules r ? $"[{r.Tiers} voids mixed {r.MixVoids} {r.Stacks} scrap {r.Scrap}]" : "[no Inventory Sort]";
    }
}
