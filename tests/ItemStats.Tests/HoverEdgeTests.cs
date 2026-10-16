using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JustBeBetter.ItemStats;
using RoR2;
using Console = System.Console;

// Inventory hover text at its edges, after FillCatalog, run last since a formula that throws marks its row broken
// for the rest of the run
internal static partial class Program
{
    private static void RunHoverEdge()
    {
        Test("Hover over a held void item shows its own stats, nothing about corrupting", HoverHeldVoidNoCorruptLine);
        Test("Hover over an item also held as its void item shows its own count, not the corrupted text", HoverHeldBothOwnCount);
        Test("Hover over an item held at zero shows the description alone", HoverZeroHeld);
        Test("Hover over a first stack shows its value with no heading", HoverFirstStackNoHeading);
        Test("Hover with stats off is exactly the description", HoverStatsOffExact);
        Test("Hover over Drifter scrap without the scrap item adds no lines", HoverScrapWithoutConverter);
        Test("Hover over every equipment, with and without a holder, never throws or shows an arrow", HoverEquipmentSweep);
        Test("Hover over every held item at 1, 2 and 10 never shows an arrow or heading", HoverItemSweep);
    }

    private static void HoverHeldVoidNoCorruptLine()
    {
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 2));
        string s = ItemStatText.DescribeHeld(DLC1Content.Items.BearVoid, m, true, true);
        Lacks("corrupts", s);
        Lacks("Corrupted", s);
        Lacks("→", s);
        Contains("Recharge Time", s);
    }

    private static void HoverHeldBothOwnCount()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((RoR2Content.Items.Bear, 3), (DLC1Content.Items.BearVoid, 2));
        string s = ItemStatText.DescribeHeld(RoR2Content.Items.Bear, m, true, true);
        Lacks("Corrupted", s);
        Contains("<style=cIsUtility>31.03%</style>", s);
    }

    private static void HoverZeroHeld()
    {
        Equal(Description(RoR2Content.Items.Bear), ItemStatText.DescribeHeld(RoR2Content.Items.Bear, Holding(), true, true));
    }

    private static void HoverFirstStackNoHeading()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribeHeld(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 1)), true, true);
        Contains("<style=cIsUtility>13.04%</style>", s);
        Lacks("With this item", s);
        Lacks("→", s);
    }

    private static void HoverStatsOffExact()
    {
        Equal(Description(RoR2Content.Items.Bear),
            ItemStatText.DescribeHeld(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 6)), true, false));
    }

    private static void HoverScrapWithoutConverter()
    {
        Equal(Description(RoR2Content.Items.ScrapWhite),
            ItemStatText.DescribeHeld(RoR2Content.Items.ScrapWhite, Holding((RoR2Content.Items.ScrapWhite, 3)), true, true));
    }

    private static readonly FieldInfo BrokenStatsF = typeof(ItemStatText).GetField("brokenStats", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static HashSet<int> BrokenStats() => (HashSet<int>)BrokenStatsF.GetValue(null)!;

    private static void HoverEquipmentSweep()
    {
        var problems = new List<string>();
        var before = new HashSet<int>(BrokenStats());
        foreach (FieldInfo f in EquipmentFields())
        {
            if (f.GetValue(null) is not EquipmentDef e)
            {
                continue;
            }

            e.pickupToken ??= f.Name + "_PICKUP";
            e.descriptionToken ??= f.Name + "_DESC";
            foreach (CharacterMaster? m in new[] { null, Holding() })
            {
                try
                {
                    string s = ItemStatText.DescribeHeld(e, m, true, true);
                    if (s.Contains('→'))
                    {
                        problems.Add($"{f.Name}: arrow in [{s.Replace("\n", "\\n")}]");
                    }
                }
                catch (Exception ex)
                {
                    problems.Add($"{f.Name} holder {(m == null ? "none" : "yes")}: throws {ex.GetType().Name}");
                }
            }
        }

        foreach (int key in BrokenStats().Except(before))
        {
            problems.Add($"equipment key {key}: its formula threw, so it's marked broken");
        }

        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} problems", string.Join("\n", problems.Select(p => "     " + p)));
        }
    }

    // Formulas that read the holder's body or a running stage throw here and are only noted, the text must still hold
    private static void HoverItemSweep()
    {
        Utils.CapChance = true;
        var problems = new List<string>();
        var before = new HashSet<int>(BrokenStats());
        foreach (int key in ItemDefinitions.allItemDefinitions.Keys)
        {
            ItemDef item = ItemCatalog.GetItemDef((ItemIndex)key);
            if (item == null)
            {
                continue;
            }

            foreach (int count in new[] { 1, 2, 10 })
            {
                try
                {
                    string s = ItemStatText.DescribeHeld(item, Holding((item, count)), true, true);
                    if (s.Contains('→') || s.Contains("With this item", StringComparison.Ordinal))
                    {
                        problems.Add($"{itemNames[key]} x{count}: [{s.Replace("\n", "\\n")}]");
                    }

                    if (!s.StartsWith(Description(item), StringComparison.Ordinal))
                    {
                        problems.Add($"{itemNames[key]} x{count}: doesn't start with its description");
                    }
                }
                catch (Exception ex)
                {
                    problems.Add($"{itemNames[key]} x{count}: throws {ex.GetType().Name}");
                }
            }
        }

        List<string> broke = BrokenStats().Except(before).Where(k => k >= 0).Select(k => itemNames[k]).ToList();
        if (broke.Count > 0)
        {
            Console.WriteLine($"     note, threw with a test holder and fell back to text: {string.Join(", ", broke)}");
        }

        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} problems", string.Join("\n", problems.Select(p => "     " + p)));
        }
    }

    private static IEnumerable<FieldInfo> EquipmentFields()
    {
        Assembly ror2 = typeof(ItemDef).Assembly;
        foreach (string owner in new[] { "RoR2.RoR2Content", "RoR2.DLC1Content", "RoR2.DLC2Content", "RoR2.DLC3Content", "RoR2.DLC4Content", "RoR2.JunkContent" })
        {
            foreach (Type nested in ror2.GetType(owner)?.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic) ?? Type.EmptyTypes)
            {
                foreach (FieldInfo f in nested.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(EquipmentDef))
                    {
                        yield return f;
                    }
                }
            }
        }
    }
}
