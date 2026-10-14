using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using JustBeBetter.InventorySort;
using JustBeBetter.Pickers;
using RoR2;
using RoR2.UI;
using Console = System.Console;
using Path = System.IO.Path;

// Inventory Sort at its edges
// InventorySorter needs Unity, so the IL tests check the game still does what its hook leans on, and the model
// mirrors OnUpdateDisplay's copy, sort, orig and restore as written
// A change to InventorySorter has to be mirrored here
internal static partial class Program
{
    private static readonly TierOrder[] AllTierOrders = { TierOrder.RarestFirst, TierOrder.CommonestFirst, TierOrder.Off, (TierOrder)7, (TierOrder)(-1) };
    private static readonly StackOrder[] AllStackOrders = { StackOrder.MostFirst, StackOrder.FewestFirst, StackOrder.Off, (StackOrder)7 };
    private static readonly ScrapPosition[] AllScrapPositions = { ScrapPosition.First, ScrapPosition.Last, ScrapPosition.WithItsTier, (ScrapPosition)7 };

    private static readonly ItemTier[] EdgeTiers =
    {
        ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Lunar, ItemTier.Boss, ItemTier.NoTier,
        ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss,
        ItemTier.FoodTier, ItemTier.Cursed, ItemTier.Page, ItemTier.Hex, ItemTier.AssignedAtRuntime, (ItemTier)1003, (ItemTier)(-4),
    };

    private static void RunInventorySortEdge()
    {
        Test("Inventory sort edge: sorting an already sorted list moves nothing, for every rule", InvEdgeIdempotent);
        Test("Inventory sort edge: 5000 items come back a permutation, ties in pickup order", InvEdgeLargePermutation);
        Test("Inventory sort edge: mixed voids tie with their normal tier in pickup order", InvEdgeMixedTies);
        Test("Inventory sort edge: Mix Voids does nothing with Tier Order off", InvEdgeMixWithTiersOff);
        Test("Inventory sort edge: extreme stack counts don't overflow either way", InvEdgeExtremeStacks);
        Test("Inventory sort edge: the Scrapper's By Tier gives the bar's order for the same items, every rule", InvEdgeScrapperMatchesBar);
        Test("Inventory sort edge: Scrapper equipment stays after scrap set Last, favourites before scrap set First", InvEdgeScrapperEquipment);
        Test("Inventory sort edge: inventory rules change nothing but By Tier, so the Command menu is untouched", InvEdgeRulesOnlyByTier);
        Test("Inventory sort edge: rules compare equal as the picker cache needs, odd values included", InvEdgeRulesEquality);
        Test("Inventory sort edge model: the picker cache re-sorts once per rule change, not every frame", InvEdgePickerCacheSettles);
        Test("Inventory sort edge model: orig draws the sorted order, the display keeps pickup order after", InvEdgeHookRestores);
        Test("Inventory sort edge model: pickup order comes back when the game's draw throws", InvEdgeHookOrigThrows);
        Test("Inventory sort edge model: a throw part way through sorting draws pickup order, not half sorted", InvEdgeHookSortThrows);
        Test("Inventory sort edge model: negative or oversized counts never throw in the hook or touch the tail", InvEdgeHookOddCounts);
        Test("Inventory sort edge model: a redraw inside a redraw still leaves pickup order", InvEdgeHookNested);
        Test("Inventory sort edge model: a rule change after a sort re-sorts from pickup order, Off shows pickup order", InvEdgeHookRuleChange);
        Test("Inventory sort edge model: missing items and indices past the stack array sort as tierless with no stack", InvEdgeHookBadIndices);
        Test("Inventory sort edge: the display's itemOrder, itemOrderCount and itemStacks are still private fields of those types", InvEdgeDisplayFields);
        Test("Inventory sort edge: UpdateDisplay reads the order and stacks and never writes or refills them", InvEdgeUpdateDisplayReadOnly);
        Test("Inventory sort edge: SetItems copies into the display's buffer, so a sort never reaches the caller's array", InvEdgeSetItemsCopies);
        Test("Inventory sort edge: the game over screen and logbook both draw run reports under GameEndReportPanelController", InvEdgeRunReportsUnderController);
        Test("Inventory sort edge: the choice labels match their enums in count and order", InvEdgeLabelsMatchEnums);
    }

    private static IEnumerable<SortRules> EveryRule()
    {
        foreach (TierOrder tiers in AllTierOrders)
        {
            foreach (bool mix in new[] { false, true })
            {
                foreach (StackOrder stacks in AllStackOrders)
                {
                    foreach (ScrapPosition scrap in AllScrapPositions)
                    {
                        yield return new SortRules(tiers, mix, stacks, scrap);
                    }
                }
            }
        }
    }

    private static List<ItemKey> RandomKeys(Random rng, int count)
    {
        var keys = new List<ItemKey>();
        for (int i = 0; i < count; i++)
        {
            keys.Add(new ItemKey(EdgeTiers[rng.Next(EdgeTiers.Length)], rng.Next(5) == 0, rng.Next(0, 6)));
        }

        return keys;
    }

    private static string RuleName(SortRules r)
    {
        return $"{r.Tiers}/{(r.MixVoids ? "mixed" : "apart")}/{r.Stacks}/{r.Scrap}";
    }

    private static void InvEdgeIdempotent()
    {
        var rng = new Random(31);
        foreach (SortRules rules in EveryRule())
        {
            for (int round = 0; round < 20; round++)
            {
                List<ItemKey> keys = RandomKeys(rng, rng.Next(0, 40));
                List<int> once = InventoryOrder.Arrange(keys, rules);
                List<ItemKey> sorted = once.Select(i => keys[i]).ToList();
                List<int> again = InventoryOrder.Arrange(sorted, rules);
                Expect(again.SequenceEqual(Enumerable.Range(0, sorted.Count)), $"{RuleName(rules)}: re-sorting moved {string.Join(",", again)}");
            }
        }
    }

    private static void InvEdgeLargePermutation()
    {
        var rng = new Random(77);
        List<ItemKey> keys = RandomKeys(rng, 5000);
        foreach (SortRules rules in EveryRule())
        {
            List<int> order = InventoryOrder.Arrange(keys, rules);
            Expect(order.Count == keys.Count && order.OrderBy(i => i).SequenceEqual(Enumerable.Range(0, keys.Count)), $"{RuleName(rules)}: not a permutation");
            for (int n = 1; n < order.Count; n++)
            {
                ItemKey a = keys[order[n - 1]], b = keys[order[n]];
                var ka = (InventoryOrder.ScrapRank(a.Scrap, rules.Scrap), InventoryOrder.TierRank(a.Tier, rules), InventoryOrder.StackRank(a.Count, rules.Stacks));
                var kb = (InventoryOrder.ScrapRank(b.Scrap, rules.Scrap), InventoryOrder.TierRank(b.Tier, rules), InventoryOrder.StackRank(b.Count, rules.Stacks));
                int cmp = ka.CompareTo(kb);
                Expect(cmp < 0 || (cmp == 0 && order[n - 1] < order[n]), $"{RuleName(rules)}: positions {n - 1} and {n} out of order");
            }
        }
    }

    private static void InvEdgeMixedTies()
    {
        var items = new List<(string Name, ItemKey Key)>
        {
            Inv("lenses", ItemTier.VoidTier1, 2),
            Inv("crowbar", ItemTier.Tier1, 2),
            Inv("needletick", ItemTier.VoidTier1, 2),
            Inv("syringe", ItemTier.Tier1, 2),
        };
        foreach (TierOrder tiers in new[] { TierOrder.RarestFirst, TierOrder.CommonestFirst })
        {
            Equal("lenses,crowbar,needletick,syringe", InvArranged(items, new SortRules(tiers, true, StackOrder.MostFirst, ScrapPosition.First)));
            Equal("lenses,crowbar,needletick,syringe", InvArranged(items, new SortRules(tiers, true, StackOrder.FewestFirst, ScrapPosition.Last)));
        }
    }

    private static void InvEdgeMixWithTiersOff()
    {
        var rng = new Random(5);
        List<ItemKey> keys = RandomKeys(rng, 200);
        foreach (StackOrder stacks in AllStackOrders)
        {
            foreach (ScrapPosition scrap in AllScrapPositions)
            {
                List<int> apart = InventoryOrder.Arrange(keys, new SortRules(TierOrder.Off, false, stacks, scrap));
                List<int> mixed = InventoryOrder.Arrange(keys, new SortRules(TierOrder.Off, true, stacks, scrap));
                Expect(apart.SequenceEqual(mixed), $"Off/{stacks}/{scrap}: Mix Voids changed the order");
            }
        }
    }

    private static void InvEdgeExtremeStacks()
    {
        var items = new List<(string Name, ItemKey Key)>
        {
            Inv("zero", ItemTier.Tier1, 0),
            Inv("max", ItemTier.Tier1, int.MaxValue),
            Inv("min", ItemTier.Tier1, int.MinValue),
            Inv("one", ItemTier.Tier1, 1),
        };
        Equal("max,one,zero,min", InvArranged(items, Defaults));
        Equal("min,zero,one,max", InvArranged(items, new SortRules(TierOrder.RarestFirst, false, StackOrder.FewestFirst, ScrapPosition.First)));
    }

    private static void InvEdgeScrapperMatchesBar()
    {
        var rng = new Random(2026);
        foreach (SortRules rules in EveryRule())
        {
            for (int round = 0; round < 10; round++)
            {
                List<ItemKey> items = RandomKeys(rng, rng.Next(1, 30));
                List<SortKey> options = items.Select((k, i) => new SortKey("item" + i, PickerSort.TierRank(k.Tier), k.Count, false, k.Tier, k.Scrap)).ToList();
                string bar = string.Join(",", InventoryOrder.Arrange(items, rules));
                string scrapper = string.Join(",", PickerSort.Arrange(options, PickerOrder.ByTier, rules));
                Expect(bar == scrapper, $"{RuleName(rules)}: bar {bar}, Scrapper {scrapper}");
            }
        }
    }

    private static void InvEdgeScrapperEquipment()
    {
        var keys = new List<SortKey>
        {
            Scrapper("preon", null, 0),
            Scrapper("white scrap", ItemTier.Tier1, 3, false, true),
            Scrapper("syringe", ItemTier.Tier1, 1, true),
            Scrapper("dio", ItemTier.Tier3, 1),
            Scrapper("fuel array", null, 0, true),
        };
        Equal(
            "syringe,fuel array,dio,white scrap,preon",
            ScrapperArranged(keys, new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.Last)));
        Equal(
            "syringe,fuel array,white scrap,dio,preon",
            ScrapperArranged(keys, new SortRules(TierOrder.RarestFirst, false, StackOrder.MostFirst, ScrapPosition.First)));
    }

    private static void InvEdgeRulesOnlyByTier()
    {
        List<SortKey> keys = ScrapperOptions();
        keys.Add(Scrapper("red scrap", ItemTier.Tier3, 2, false, true));
        foreach (PickerOrder order in new[] { PickerOrder.GamesOrder, PickerOrder.MostHeldFirst, PickerOrder.AToZ, (PickerOrder)9 })
        {
            string plain = string.Join(",", PickerSort.Arrange(keys, order));
            foreach (SortRules rules in EveryRule())
            {
                Expect(string.Join(",", PickerSort.Arrange(keys, order, rules)) == plain, $"{order} with {RuleName(rules)} changed the order");
            }
        }
    }

    private static void InvEdgeRulesEquality()
    {
        foreach (SortRules a in EveryRule())
        {
            var copy = new SortRules(a.Tiers, a.MixVoids, a.Stacks, a.Scrap);
            Expect(Nullable.Equals((SortRules?)a, (SortRules?)copy), $"{RuleName(a)} not equal to its copy");
            Expect(a.GetHashCode() == copy.GetHashCode(), $"{RuleName(a)} hash differs from its copy");
            Expect(!Nullable.Equals((SortRules?)a, (SortRules?)null) && !Nullable.Equals((SortRules?)null, (SortRules?)a), $"{RuleName(a)} equals no rules");
        }

        int distinct = EveryRule().Distinct().Count();
        Expect(distinct == EveryRule().Count(), $"only {distinct} distinct rule sets, a field is left out of Equals");
        Expect(Nullable.Equals((SortRules?)null, (SortRules?)null), "no rules isn't equal to no rules");
    }

    // Mirrors PickerFilter.Wanted's cache test, with an arrange counter in place of the sort
    private sealed class MirrorPickerCache
    {
        private bool arranged;
        private PickerOrder arrangedFor;
        private SortRules? arrangedRules;

        internal int Arranges { get; private set; }

        internal void Frame(PickerOrder sorting, Func<SortRules?>? inventoryRules)
        {
            SortRules? rules = inventoryRules?.Invoke();
            if (!arranged || arrangedFor != sorting || !Nullable.Equals(arrangedRules, rules))
            {
                arrangedFor = sorting;
                arrangedRules = rules;
                arranged = true;
                Arranges++;
            }
        }
    }

    private static void InvEdgePickerCacheSettles()
    {
        var cache = new MirrorPickerCache();
        SortRules live = Defaults;
        Func<SortRules?> follow = () => new SortRules(live.Tiers, live.MixVoids, live.Stacks, live.Scrap);
        for (int f = 0; f < 100; f++)
        {
            cache.Frame(PickerOrder.ByTier, follow);
        }

        Expect(cache.Arranges == 1, $"{cache.Arranges} sorts over 100 frames with steady rules");
        live = new SortRules(TierOrder.CommonestFirst, true, StackOrder.Off, ScrapPosition.Last);
        for (int f = 0; f < 100; f++)
        {
            cache.Frame(PickerOrder.ByTier, follow);
        }

        Expect(cache.Arranges == 2, $"{cache.Arranges} sorts after one rule change");
        var off = new MirrorPickerCache();
        for (int f = 0; f < 100; f++)
        {
            off.Frame(PickerOrder.ByTier, () => null);
            off.Frame(PickerOrder.ByTier, null);
        }

        Expect(off.Arranges == 1, $"{off.Arranges} sorts over 100 frames with the part off");
    }

    // Mirrors InventorySorter.OnUpdateDisplay and Sort, the display's private fields as plain ones
    private sealed class MirrorItemDisplay
    {
        internal object? ItemOrder;
        internal object? ItemOrderCount;
        internal object? ItemStacks;
    }

    private sealed class MirrorSorter
    {
        private readonly List<ItemKey> keys = new List<ItemKey>();
        private readonly Func<ItemIndex, (ItemTier Tier, bool Scrap)?> defs;

        internal MirrorSorter(Func<ItemIndex, (ItemTier Tier, bool Scrap)?> defs, SortRules rules)
        {
            this.defs = defs;
            Rules = rules;
        }

        internal SortRules Rules { get; set; }

        internal bool Wanted { get; set; } = true;

        internal List<string> Log { get; } = new List<string>();

        internal void UpdateDisplay(MirrorItemDisplay self, Action orig)
        {
            if (!Wanted || self.ItemOrder is not ItemIndex[] order || self.ItemOrderCount is not int count || self.ItemStacks is not int[] stacks)
            {
                orig();
                return;
            }

            count = Math.Max(0, Math.Min(count, order.Length));
            var picked = new ItemIndex[count];
            Array.Copy(order, picked, count);
            try
            {
                Sort(order, picked, stacks);
            }
            catch (Exception e)
            {
                Array.Copy(picked, order, count);
                Log.Add("ERROR " + e.Message);
            }

            try
            {
                orig();
            }
            finally
            {
                Array.Copy(picked, order, count);
            }
        }

        private void Sort(ItemIndex[] order, ItemIndex[] picked, int[] stacks)
        {
            keys.Clear();
            foreach (ItemIndex index in picked)
            {
                (ItemTier Tier, bool Scrap)? def = defs(index);
                int held = (int)index >= 0 && (int)index < stacks.Length ? stacks[(int)index] : 0;
                keys.Add(new ItemKey(def?.Tier ?? ItemTier.NoTier, def?.Scrap ?? false, held));
            }

            List<int> wanted = InventoryOrder.Arrange(keys, Rules);
            for (int i = 0; i < picked.Length; i++)
            {
                order[i] = picked[wanted[i]];
            }
        }
    }

    // Item i: tier and scrap as below, stack i+1
    private static readonly (ItemTier Tier, bool Scrap)[] MirrorCatalog =
    {
        (ItemTier.Tier1, false), (ItemTier.Tier3, false), (ItemTier.Tier1, true), (ItemTier.VoidTier1, false),
        (ItemTier.Lunar, false), (ItemTier.Tier2, false), (ItemTier.Boss, false), (ItemTier.NoTier, false),
    };

    private static (ItemTier Tier, bool Scrap)? MirrorDef(ItemIndex i)
    {
        return (int)i >= 0 && (int)i < MirrorCatalog.Length ? MirrorCatalog[(int)i] : null;
    }

    private static MirrorItemDisplay MirrorFilled(params int[] pickup)
    {
        var order = new ItemIndex[MirrorCatalog.Length + 2];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = (ItemIndex)(-1);
        }

        for (int i = 0; i < pickup.Length; i++)
        {
            order[i] = (ItemIndex)pickup[i];
        }

        return new MirrorItemDisplay
        {
            ItemOrder = order,
            ItemOrderCount = pickup.Length,
            ItemStacks = Enumerable.Range(1, MirrorCatalog.Length).ToArray(),
        };
    }

    private static string MirrorOrder(MirrorItemDisplay d, int count)
    {
        return string.Join(",", ((ItemIndex[])d.ItemOrder!).Take(count).Select(i => (int)i));
    }

    private static void InvEdgeHookRestores()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        MirrorItemDisplay d = MirrorFilled(0, 1, 2, 3, 4, 5, 6, 7);
        string tail = string.Join(",", ((ItemIndex[])d.ItemOrder!).Skip(8).Select(i => (int)i));
        string drawn = "";
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 8));
        Equal("2,4,6,1,5,0,3,7", drawn);
        Equal("0,1,2,3,4,5,6,7", MirrorOrder(d, 8));
        Equal(tail, string.Join(",", ((ItemIndex[])d.ItemOrder!).Skip(8).Select(i => (int)i)));
    }

    private static void InvEdgeHookOrigThrows()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        MirrorItemDisplay d = MirrorFilled(0, 1, 2, 3);
        bool threw = false;
        try
        {
            sorter.UpdateDisplay(d, () => throw new InvalidOperationException("draw failed"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Expect(threw, "the game's own throw was swallowed");
        Equal("0,1,2,3", MirrorOrder(d, 4));
    }

    private static void InvEdgeHookSortThrows()
    {
        int seen = 0;
        var sorter = new MirrorSorter(i => ++seen == 3 ? throw new InvalidOperationException("bad def") : MirrorDef(i), Defaults);
        MirrorItemDisplay d = MirrorFilled(0, 1, 2, 3, 4);
        string drawn = "";
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 5));
        Equal("0,1,2,3,4", drawn);
        Equal("0,1,2,3,4", MirrorOrder(d, 5));
        Expect(sorter.Log.Count == 1, "the failure wasn't logged");
    }

    private static void InvEdgeHookOddCounts()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        foreach (int count in new[] { -5, int.MinValue, 0, 1000, int.MaxValue })
        {
            MirrorItemDisplay d = MirrorFilled(3, 2, 1, 0);
            d.ItemOrderCount = count;
            string before = MirrorOrder(d, ((ItemIndex[])d.ItemOrder!).Length);
            int calls = 0;
            sorter.UpdateDisplay(d, () => calls++);
            Expect(calls == 1, $"count {count}: orig ran {calls} times");
            Equal(before, MirrorOrder(d, ((ItemIndex[])d.ItemOrder!).Length));
        }

        foreach (MirrorItemDisplay d in new[]
        {
            new MirrorItemDisplay { ItemOrder = null, ItemOrderCount = 2, ItemStacks = new int[8] },
            new MirrorItemDisplay { ItemOrder = new ItemIndex[2], ItemOrderCount = null, ItemStacks = new int[8] },
            new MirrorItemDisplay { ItemOrder = new ItemIndex[2], ItemOrderCount = 2, ItemStacks = null },
        })
        {
            int calls = 0;
            sorter.UpdateDisplay(d, () => calls++);
            Expect(calls == 1, "a destroyed display's draw didn't run once");
        }
    }

    private static void InvEdgeHookNested()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        MirrorItemDisplay d = MirrorFilled(0, 1, 2, 3, 4, 5);
        var drawn = new List<string>();
        int depth = 0;
        Action orig = null!;
        orig = () =>
        {
            drawn.Add(MirrorOrder(d, 6));
            if (depth++ == 0)
            {
                sorter.UpdateDisplay(d, orig);
            }
        };
        sorter.UpdateDisplay(d, orig);
        Expect(drawn.Count == 2 && drawn[0] == drawn[1], $"the inner draw saw {string.Join(" then ", drawn)}");
        Equal("0,1,2,3,4,5", MirrorOrder(d, 6));
    }

    private static void InvEdgeHookRuleChange()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        MirrorItemDisplay d = MirrorFilled(0, 1, 2, 3, 4, 5, 6);
        string drawn = "";
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 7));
        sorter.Rules = new SortRules(TierOrder.Off, false, StackOrder.Off, ScrapPosition.WithItsTier);
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 7));
        Equal("0,1,2,3,4,5,6", drawn);
        sorter.Rules = new SortRules(TierOrder.CommonestFirst, true, StackOrder.FewestFirst, ScrapPosition.Last);
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 7));
        Equal("0,3,5,1,6,4,2", drawn);
        sorter.Wanted = false;
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 7));
        Equal("0,1,2,3,4,5,6", drawn);
    }

    private static void InvEdgeHookBadIndices()
    {
        var sorter = new MirrorSorter(MirrorDef, Defaults);
        MirrorItemDisplay d = MirrorFilled(7, -1, 0, 50, 1);
        string drawn = "";
        sorter.UpdateDisplay(d, () => drawn = MirrorOrder(d, 5));
        Equal("1,0,7,-1,50", drawn);
        Equal("7,-1,0,50,1", MirrorOrder(d, 5));
    }

    private const BindingFlags InvAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // Every field a method stores to, by name
    private static List<string> FieldsWritten(MethodBase method)
    {
        Callees(method);
        var found = new List<string>();
        byte[]? il = RawIl(method);
        if (il == null)
        {
            return found;
        }

        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op = il[pos] == 0xfe ? TwoByte[il[++pos]] : OneByte[il[pos]];
            pos++;
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, pos),
                _ => 4,
            };
            if (op.OperandType == OperandType.InlineField && (op == OpCodes.Stfld || op == OpCodes.Stsfld || op == OpCodes.Ldflda))
            {
                try
                {
                    FieldInfo? f = method.Module.ResolveField(BitConverter.ToInt32(il, pos));
                    if (f != null)
                    {
                        found.Add(f.DeclaringType!.Name + "." + f.Name);
                    }
                }
                catch (Exception)
                {
                    // A generic or missing reference, not what's being looked for
                }
            }

            pos += size;
        }

        return found;
    }

    // The method and everything it reaches inside ItemInventoryDisplay
    private static List<MethodBase> DisplayReach(MethodBase start)
    {
        var seen = new List<MethodBase> { start };
        for (int i = 0; i < seen.Count; i++)
        {
            foreach (MethodBase callee in Callees(seen[i]))
            {
                if (callee.DeclaringType == typeof(ItemInventoryDisplay) && !seen.Contains(callee))
                {
                    seen.Add(callee);
                }
            }
        }

        return seen;
    }

    private static void InvEdgeDisplayFields()
    {
        foreach ((string name, Type type) in new[] { ("itemOrder", typeof(ItemIndex[])), ("itemOrderCount", typeof(int)), ("itemStacks", typeof(int[])) })
        {
            FieldInfo? f = typeof(ItemInventoryDisplay).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Expect(f != null && f.FieldType == type, $"ItemInventoryDisplay.{name} is gone or no longer a {type.Name}");
        }

        MethodInfo? update = typeof(ItemInventoryDisplay).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        Expect(update != null && !update.IsVirtual, "UpdateDisplay is gone or overridable, the hook may not cover every display");
        Expect((RawIl(update!)?.Length ?? 0) >= 20, "UpdateDisplay is small enough for Mono to inline past the hook");
    }

    private static void InvEdgeUpdateDisplayReadOnly()
    {
        MethodInfo update = typeof(ItemInventoryDisplay).GetMethod("UpdateDisplay", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)!;
        List<string> read = FieldsRead(update);
        foreach (string field in new[] { "itemOrder", "itemOrderCount", "itemStacks" })
        {
            Expect(read.Contains("ItemInventoryDisplay." + field), $"UpdateDisplay no longer reads {field}, it reads {string.Join(", ", read.Distinct())}");
        }

        // So a scanner that finds nothing can't pass
        MethodInfo refill = typeof(ItemInventoryDisplay).GetMethod("OnInventoryChanged", InvAll)!;
        Expect(FieldsWritten(refill).Contains("ItemInventoryDisplay.itemOrderCount"), "the store scanner misses OnInventoryChanged's itemOrderCount write");
        List<MethodBase> reach = DisplayReach(update);
        Expect(reach.Any(m => m.Name == "AllocateIcons") && reach.Any(m => m.Name == "LayoutAllIcons"), $"UpdateDisplay's reach is only {string.Join(", ", reach.Select(m => m.Name))}");

        string[] refills = { "SetItems", "ResetItems", "OnInventoryChanged", "SetSubscribedInventory" };
        foreach (MethodBase m in reach)
        {
            List<string> written = FieldsWritten(m);
            foreach (string field in new[] { "ItemInventoryDisplay.itemOrder", "ItemInventoryDisplay.itemOrderCount", "ItemInventoryDisplay.itemStacks" })
            {
                Expect(!written.Contains(field), $"{m.Name}, reached from UpdateDisplay, writes {field}, the hook's restore would undo it");
            }

            Expect(!refills.Contains(m.Name), $"UpdateDisplay reaches {m.Name}, which refills the order the hook restores over");
        }
    }

    private static void InvEdgeSetItemsCopies()
    {
        MethodInfo[] setters = typeof(ItemInventoryDisplay).GetMethods(InvAll).Where(m => m.Name == "SetItems").ToArray();
        Expect(setters.Length >= 2, $"{setters.Length} SetItems overloads, expected the list and array ones");
        foreach (MethodInfo m in setters)
        {
            List<string> written = FieldsWritten(m);
            Expect(!written.Contains("ItemInventoryDisplay.itemOrder"), $"SetItems({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) stores the caller's array");
            Expect(Callees(m).Any(c => c.Name == "RequestUpdateDisplay"), "SetItems no longer redraws through RequestUpdateDisplay");
        }
    }

    private static void InvEdgeRunReportsUnderController()
    {
        Type? builder = typeof(ItemDef).Assembly.GetType("RoR2.UI.LogBook.PageBuilder");
        MethodInfo? add = builder?.GetMethod("AddRunReportPanel", InvAll);
        Expect(add != null, "the logbook's PageBuilder.AddRunReportPanel is gone");
        Expect(
            Callees(add!).Any(c => c.Name == "SetDisplayData" && c.DeclaringType == typeof(GameEndReportPanelController)),
            "the logbook no longer fills its run report through GameEndReportPanelController");

        MethodInfo? generate = typeof(GameOverController).GetMethod("GenerateReportScreen", InvAll);
        Expect(generate != null && Callees(generate).Any(c => c.Name == "SetDisplayData" && c.DeclaringType == typeof(GameEndReportPanelController)),
            "the game over screen no longer fills its report through GameEndReportPanelController");

        // Anything else filling an item display by hand could be a run report the setting misses
        Type[] types;
        try
        {
            types = typeof(ItemDef).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray()!;
        }

        var known = new HashSet<string> { "EnemyInfoPanel.SetItems", "GameEndReportPanelController.SetPlayerInfo" };
        List<string> callers = CallersOf(types, typeof(ItemInventoryDisplay), "SetItems").Distinct().ToList();
        Console.WriteLine("     note, ItemInventoryDisplay.SetItems callers: " + string.Join(", ", callers));
        foreach (string caller in callers)
        {
            Expect(known.Contains(caller), $"{caller} fills an item display by hand, check whether it's a run report");
        }
    }

    private static void InvEdgeLabelsMatchEnums()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        string text = File.ReadAllText(Path.Combine(root, "InventorySort", "InventorySortSettings.cs"));
        foreach ((string field, Type type) in new[] { ("ScrapLabels", typeof(ScrapPosition)), ("TierLabels", typeof(TierOrder)), ("StackLabels", typeof(StackOrder)) })
        {
            Match m = Regex.Match(text, field + @"\s*=\s*\{([^}]*)\}");
            Expect(m.Success, $"{field} not found");
            string[] labels = Regex.Matches(m.Groups[1].Value, "\"([^\"]*)\"").Select(x => x.Groups[1].Value.Replace(" ", string.Empty)).ToArray();
            string[] names = Enum.GetNames(type);
            Expect(labels.SequenceEqual(names, StringComparer.OrdinalIgnoreCase), $"{field} [{string.Join(",", labels)}] vs {type.Name} [{string.Join(",", names)}]");
        }
    }
}
