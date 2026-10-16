using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using JustBeBetter.ItemStats;
using JustBeBetter.SkillInfo;
using RoR2;
using Console = System.Console;

// Edge cases for From Luck and for stats read from modded descriptions or LookingGlass
internal static partial class Program
{
    private static void RunNewFeatureEdge()
    {
        Test("Luck edge: CapChance off, a chance past 100% still gets its lucky line", LuckEdgeUncapped);
        Test("Luck edge: luck that rounds to the same text adds no line", LuckEdgeRoundsEqual);
        Test("Luck edge: a def that ignores luck gets no line at any luck", LuckEdgeIgnored);
        Test("Luck edge: fractional luck behaves like the game's ceiling", LuckEdgeFractional);
        Test("Luck edge: when the next stack caps both, the lucky change still shows", LuckEdgeCappedTarget);
        Test("Luck edge: Skill Info with luck at a capped chance adds no line", LuckEdgeSkillCapped);
        Test("Luck edge: Skill Info with negative luck shows the lower chance", LuckEdgeSkillNegative);
        Test("Stack edge: a thousands separator isn't read as its last group", StackEdgeThousands);
        Test("Stack edge: a comma decimal isn't read as its fraction", StackEdgeCommaDecimal);
        Test("Stack edge: a value that starts negative keeps its sign", StackEdgeNegativeStart);
        Test("Stack edge: a range like 2-4 isn't read as minus 4", StackEdgeRange);
        Test("Stack edge: a bare decimal like .5 isn't read as 5", StackEdgeBareDecimal);
        Test("Stack edge: an x multiplier isn't read", StackEdgeMultiplier);
        Test("Stack edge: unclosed tags don't leak into the label", StackEdgeUnclosedTag);
        Test("Stack edge: a value inside nested tags keeps its colour", StackEdgeNestedTags);
        Test("Stack edge: a label of only punctuation falls back to a plain name", StackEdgePunctuationLabel);
        Test("Stack edge: three of the same label stay unique", StackEdgeTripleLabels);
        Test("Stack edge: adversarial text reads quickly", StackEdgeAdversarial);
        Test("Stack edge: many values in one long text read quickly", StackEdgeManyValues);
        Test("Modded edge: a description is re-read when its text changes", ModdedEdgeTextChange);
        Test("Modded edge: LookingGlass's entry converts by enum name", ModdedEdgeLookingGlassConvert);
        Test("Modded edge: LookingGlass's entry with null lists falls back to the description", ModdedEdgeLookingGlassNulls);
        Test("Modded edge: an item in our table never takes LookingGlass's entry", ModdedEdgeTableFirst);
        Test("Modded edge: a modded item with no description token doesn't throw out of the stats", ModdedEdgeNullToken);
        Test("Modded edge: a broken LookingGlass entry doesn't throw out of the stats", ModdedEdgeBrokenLookingGlass);
        Test("Modded edge: which vanilla items outside the table now read stats from their text", ModdedEdgeVanillaOutsideTable);
    }

    private static string Probe(string s) => s.Replace("\n", "\\n");

    private static ItemStatsDef ChanceDef(float chance)
    {
        var def = new ItemStatsDef();
        def.descriptions.Add("Chance: ");
        def.valueTypes.Add(ItemStatsDef.ValueType.Damage);
        def.measurementUnits.Add(ItemStatsDef.MeasurementUnits.Percentage);
        def.calculateValuesNew = (luck, count, proc) => new List<float> { Utils.CalculateChanceWithLuck(chance * count, luck) };
        return def;
    }

    private static void LuckEdgeUncapped()
    {
        Utils.CapChance = false;
        try
        {
            string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(1), 14);
            Console.WriteLine("     " + Probe(s));
            Contains("<style=cIsDamage>164%</style> → <style=cIsDamage>175%</style>", s);
            Contains("From Luck:</style> <style=cIsDamage>+24%</style> → <style=cIsDamage>+25%</style>", s);
        }
        finally
        {
            Utils.CapChance = true;
        }
    }

    private static void LuckEdgeRoundsEqual()
    {
        var def = new ItemStatsDef();
        def.descriptions.Add("Chance: ");
        def.valueTypes.Add(ItemStatsDef.ValueType.Damage);
        def.measurementUnits.Add(ItemStatsDef.MeasurementUnits.Percentage);
        def.calculateValuesNew = (luck, count, proc) => new List<float> { 0.1f * count + (luck * 0.000001f) };
        Lacks("From Luck", Lines(def, Master(2), 1));
    }

    private static void LuckEdgeIgnored()
    {
        var def = new ItemStatsDef();
        def.descriptions.Add("Damage: ");
        def.valueTypes.Add(ItemStatsDef.ValueType.Damage);
        def.measurementUnits.Add(ItemStatsDef.MeasurementUnits.Percentage);
        def.calculateValuesNew = (luck, count, proc) => new List<float> { 0.2f * count };
        foreach (float luck in new[] { -3f, -0.5f, 0.5f, 7f })
        {
            Lacks("From Luck", Lines(def, Master(luck), 1));
        }
    }

    // The game's ceiling makes 0.5 count as 1 and -0.5 as 0
    private static void LuckEdgeFractional()
    {
        Utils.CapChance = true;
        string half = Lines(ChanceDef(0.1f), Master(0.5f), 0);
        Contains("<style=cIsDamage>19%</style>", half);
        Contains("From Luck:</style> <style=cIsDamage>+9%</style>", half);
        Lacks("From Luck", Lines(ChanceDef(0.1f), Master(-0.5f), 0));
    }

    // At 9 stacks plain is 90% and lucky 99%, at 10 both are 100%, so luck's share falls from 9 points to none
    private static void LuckEdgeCappedTarget()
    {
        Utils.CapChance = true;
        string s = Lines(ChanceDef(0.1f), Master(1), 9);
        Console.WriteLine("     " + Probe(s));
        Contains("<style=cIsDamage>99%</style> → <style=cIsDamage>100%</style>", s);
        Contains("From Luck:</style> <style=cIsDamage>+9%</style> → <style=cIsDamage>0%</style>", s);
    }

    private static void LuckEdgeSkillCapped()
    {
        Utils.CapChance = true;
        CharacterMaster master = Holding((RoR2Content.Items.BleedOnHit, 12));
        master.luck = 2f;
        string s = SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, master, AllLines);
        Contains("<style=cIsDamage>100%</style> <style=cStack>(10 to cap)</style>", s);
        Lacks("From Luck", s);
    }

    private static void LuckEdgeSkillNegative()
    {
        Utils.CapChance = true;
        CharacterMaster master = Holding((RoR2Content.Items.BleedOnHit, 2));
        master.luck = -1f;
        string s = SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, master, AllLines);
        Contains("<style=cIsDamage>4%</style> <style=cStack>(10 to cap)</style>", s);
        Contains("\n<style=cStack>  From Luck:</style> <style=cIsDamage>-16%</style>", s);
    }

    private static void StackEdgeThousands()
    {
        List<StackText.Stat> stats = StackText.Read("Gain 1,000 (+500 per stack) gold.");
        Console.WriteLine("     " + string.Join(", ", stats.Select(x => $"{x.Label} {x.At(1)}")));
        Expect(stats.Count == 0 || Near(stats[0].At(1), 1000f), "1,000 read as 1000 or not at all");
    }

    private static void StackEdgeCommaDecimal()
    {
        List<StackText.Stat> stats = StackText.Read("Dauer 2,5 (+1 per stack) Sekunden.");
        Console.WriteLine("     " + string.Join(", ", stats.Select(x => $"{x.Label} {x.At(1)}")));
        Expect(stats.Count == 0 || Near(stats[0].At(1), 2.5f), "2,5 read as 2.5 or not at all");
    }

    private static void StackEdgeNegativeStart()
    {
        StackText.Stat falling = StackText.Read("Reduces enemy armor by -10% (-5% per stack).").Single();
        Console.WriteLine($"     at 1 {falling.At(1)} at 2 {falling.At(2)}");
        Expect(Near(falling.At(1), -0.1f) && Near(falling.At(2), -0.15f), "a negative value stays negative");
        StackText.Stat rising = StackText.Read("Movement speed -20% (+5% per stack).").Single();
        Expect(Near(rising.At(1), -0.2f), "-20% at one stack, not 0");
    }

    private static void StackEdgeRange()
    {
        List<StackText.Stat> stats = StackText.Read("Hits 2-4 (+1 per stack) enemies.");
        Console.WriteLine("     " + string.Join(", ", stats.Select(x => $"{x.Label} {x.First}")));
        Expect(stats.Count == 0 || stats[0].First > 0f, "a range's upper end isn't negative");
    }

    private static void StackEdgeBareDecimal()
    {
        List<StackText.Stat> stats = StackText.Read("Heal .5 (+.5 per stack) health.");
        Expect(stats.Count == 0 || Near(stats[0].At(1), 0.5f), ".5 isn't read as 5");
        List<StackText.Stat> second = StackText.Read("Heal 0.5 (+.5 per stack) health.");
        Expect(second.Count == 0 || Near(second[0].At(2), 1f), "a bare decimal step isn't read as 5");
    }

    private static void StackEdgeMultiplier()
    {
        Expect(StackText.Read("Fires 3x (+1x per stack) missiles.").Count == 0, "x multiplier");
    }

    private static void StackEdgeUnclosedTag()
    {
        foreach (string text in new[]
        {
            "<style=cIsDamage>10% (+5% per stack) <b chance to burn",
            "Gain <style=cIsUtility10% (+5% per stack)",
        })
        {
            foreach (StackText.Stat stat in StackText.Read(text))
            {
                Console.WriteLine("     " + stat.Label);
                Lacks("<", stat.Label);
                Lacks(">", stat.Label);
            }
        }
    }

    private static void StackEdgeNestedTags()
    {
        List<StackText.Stat> stats = StackText.Read(
            "<style=cIsDamage><color=#fff>10%</color></style> <style=cStack>(+5% per stack)</style> crit chance.");
        Expect(stats.Count == 1, "read");
        Expect(stats[0].Type == ItemStatsDef.ValueType.Damage, $"damage colour, got {stats[0].Type}");
    }

    private static void StackEdgePunctuationLabel()
    {
        foreach (string text in new[] { "— 10% (+5% per stack) —", "*** 10% (+5% per stack) ***", "10% (+5% per stack)" })
        {
            StackText.Stat stat = StackText.Read(text).Single();
            Console.WriteLine($"     [{stat.Label}]");
            Expect(stat.Label.Any(char.IsLetter), $"label has a word, got [{stat.Label}]");
        }
    }

    private static void StackEdgeTripleLabels()
    {
        List<StackText.Stat> stats = StackText.Read("Gain 1% (+1% per stack) damage. Gain 2% (+1% per stack) damage. Gain 3% (+1% per stack) damage. Gain 4% (+1% per stack) damage 2.");
        Expect(stats.Select(x => x.Label).Distinct().Count() == stats.Count, string.Join("|", stats.Select(x => x.Label)));
    }

    private static void StackEdgeAdversarial()
    {
        var inputs = new[]
        {
            "1" + new string(' ', 50000) + "x",
            string.Concat(Enumerable.Repeat("1 <", 20000)),
            string.Concat(Enumerable.Repeat("1<a", 20000)) + "(",
            "1" + string.Concat(Enumerable.Repeat(" <b>", 20000)) + " (+1",
            "1 (" + new string('+', 50000),
            "1 (+" + new string('1', 50000) + " per",
            string.Concat(Enumerable.Repeat("1.", 30000)),
            "<style=" + new string('a', 50000) + "1",
        };
        foreach (string input in inputs)
        {
            var watch = Stopwatch.StartNew();
            StackText.Read(input);
            watch.Stop();
            Expect(watch.ElapsedMilliseconds < 2000, $"{input.Length} chars took {watch.ElapsedMilliseconds} ms");
        }
    }

    private static void StackEdgeManyValues()
    {
        // Each label re-reads the rest of the text, so time grows with the square of its length
        // 400 values is far past any real description
        string text = string.Concat(Enumerable.Repeat("Gain 1% (+1% per stack) damage. ", 400));
        var watch = Stopwatch.StartNew();
        int count = StackText.Read(text).Count;
        watch.Stop();
        Console.WriteLine($"     {count} values from {text.Length} chars in {watch.ElapsedMilliseconds} ms");
        Expect(count == 400 && watch.ElapsedMilliseconds < 500, $"took {watch.ElapsedMilliseconds} ms");
    }

    private static readonly FieldInfo ItemIndexField = typeof(ItemDef).GetField("_itemIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static ItemDef FakeItem(int index, string? token)
    {
        var def = (ItemDef)RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
        ItemIndexField.SetValue(def, (ItemIndex)index);
        def.descriptionToken = token;
        return def;
    }

    private static string Stats(ItemDef item, CharacterMaster master, int from, int to)
    {
        var sb = new StringBuilder();
        AppendStatsM.Invoke(null, new object[] { sb, item, master, from, to, " → ", false });
        return sb.ToString();
    }

    // Language.GetString hands back an unknown token, so a token stands in for the text
    private static void ModdedEdgeTextChange()
    {
        ItemDef item = FakeItem(90001, "Gain 10% (+10% per stack) attack speed.");
        Contains("Attack Speed: </color>10%", Stats(item, Master(0), 1, 1));
        item.descriptionToken = "Gain 20% (+10% per stack) crit chance.";
        string s = Stats(item, Master(0), 1, 1);
        Console.WriteLine("     " + Probe(s));
        Contains("Crit Chance: </color>20%", s);
    }

    // Stand-ins for LookingGlass's types, matched by field and enum names
    internal enum FakeValueType { Damage, Healing, SomethingNew }

    internal enum FakeUnit { Percentage, Seconds, Furlongs }

    internal sealed class FakeLookingGlassDef
    {
        public List<string>? descriptions = new List<string>();
        public List<FakeValueType>? valueTypes = new List<FakeValueType>();
        public List<FakeUnit>? measurementUnits = new List<FakeUnit>();
        public Func<int, List<float>>? calculateValuesFlat;
        public Func<float, int, float, List<float>>? calculateValuesNew;
    }

    internal static class FakeLookingGlass
    {
        public static IDictionary allItemDefinitions = new Dictionary<int, FakeLookingGlassDef>();
    }

    private static readonly Type ModdedType = typeof(ModdedStats);

    private static void WithFakeLookingGlass(Action body)
    {
        FieldInfo field = ModdedType.GetField("lookingGlassField", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo searched = ModdedType.GetField("searched", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? oldField = field.GetValue(null);
        object? oldSearched = searched.GetValue(null);
        field.SetValue(null, typeof(FakeLookingGlass).GetField("allItemDefinitions", BindingFlags.Public | BindingFlags.Static));
        searched.SetValue(null, true);
        try
        {
            body();
        }
        finally
        {
            field.SetValue(null, oldField);
            searched.SetValue(null, oldSearched);
            FakeLookingGlass.allItemDefinitions = new Dictionary<int, FakeLookingGlassDef>();
        }
    }

    private static void ModdedEdgeLookingGlassConvert()
    {
        WithFakeLookingGlass(() =>
        {
            var theirs = new FakeLookingGlassDef();
            theirs.descriptions!.AddRange(new[] { "Proc: ", "Odd: " });
            theirs.valueTypes!.AddRange(new[] { FakeValueType.Damage, FakeValueType.SomethingNew });
            theirs.measurementUnits!.AddRange(new[] { FakeUnit.Percentage, FakeUnit.Furlongs });
            theirs.calculateValuesNew = (luck, count, proc) => new List<float> { Utils.CalculateChanceWithLuck(0.1f * count, luck), 3f };
            FakeLookingGlass.allItemDefinitions[90002] = theirs;
            string s = Stats(FakeItem(90002, "no per-stack text"), Master(1), 1, 1);
            Console.WriteLine("     " + Probe(s));
            Contains("Proc: </color><style=cIsDamage>19%</style>", s);
            Contains("From Luck:</style> <style=cIsDamage>+9%</style>", s);
            Contains("Odd: </color>3", s);
        });
    }

    private static void ModdedEdgeLookingGlassNulls()
    {
        WithFakeLookingGlass(() =>
        {
            FakeLookingGlass.allItemDefinitions[90003] = new FakeLookingGlassDef
            {
                descriptions = null,
                valueTypes = null,
                measurementUnits = null,
                calculateValuesFlat = count => new List<float> { 1f },
            };
            FakeLookingGlass.allItemDefinitions[90004] = null!;
            Contains("Attack Speed: </color>10%", Stats(FakeItem(90003, "Gain 10% (+10% per stack) attack speed."), Master(0), 1, 1));
            Contains("Attack Speed: </color>10%", Stats(FakeItem(90004, "Gain 10% (+10% per stack) attack speed."), Master(0), 1, 1));
        });
    }

    private static void ModdedEdgeTableFirst()
    {
        WithFakeLookingGlass(() =>
        {
            ItemDef bear = RoR2Content.Items.Bear;
            var theirs = new FakeLookingGlassDef { calculateValuesFlat = count => new List<float> { 9f } };
            theirs.descriptions!.Add("Fake: ");
            theirs.valueTypes!.Add(FakeValueType.Damage);
            theirs.measurementUnits!.Add(FakeUnit.Percentage);
            FakeLookingGlass.allItemDefinitions[(int)bear.itemIndex] = theirs;
            string s = Stats(bear, Master(0), 1, 1);
            Lacks("Fake", s);
            Contains("Block Chance", s);
        });
    }

    private static void ModdedEdgeNullToken()
    {
        ItemDef item = FakeItem(90005, null);
        try
        {
            Stats(item, Master(0), 1, 1);
        }
        catch (TargetInvocationException e)
        {
            throw new AssertFail("threw out of AppendStats: " + e.InnerException?.GetType().Name + " " + e.InnerException?.Message);
        }
    }

    internal sealed class ThrowingList : IEnumerable
    {
        public IEnumerator GetEnumerator() => throw new InvalidOperationException("collection was modified");
    }

    internal sealed class BrokenLookingGlassDef
    {
        public ThrowingList descriptions = new ThrowingList();
        public Func<int, List<float>> calculateValuesFlat = count => new List<float> { 1f };
    }

    private static void ModdedEdgeBrokenLookingGlass()
    {
        FakeLookingGlass.allItemDefinitions = new Hashtable();
        WithFakeLookingGlass(() =>
        {
            FieldInfo field = ModdedType.GetField("lookingGlassField", BindingFlags.Static | BindingFlags.NonPublic)!;
            var table = new Hashtable { [90006] = new BrokenLookingGlassDef() };
            FakeLookingGlass.allItemDefinitions = table;
            try
            {
                Stats(FakeItem(90006, "Gain 10% (+10% per stack) attack speed."), Master(0), 1, 1);
            }
            catch (TargetInvocationException e)
            {
                throw new AssertFail("threw out of AppendStats: " + e.InnerException?.GetType().Name + " " + e.InnerException?.Message);
            }
        });
    }

    // A report, not a check, the field name matches the token for most items
    private static void ModdedEdgeVanillaOutsideTable()
    {
        string folder = System.IO.Path.Combine(GameManagedFolder(), "..", "StreamingAssets", "Language", "en");
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip };
        foreach (string file in System.IO.Directory.GetFiles(folder, "*.json"))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file), options);
            if (doc.RootElement.TryGetProperty("strings", out var strings))
            {
                foreach (var entry in strings.EnumerateObject())
                {
                    texts[entry.Name] = entry.Value.GetString() ?? string.Empty;
                }
            }
        }

        foreach (var pair in itemNames.Where(p => !ItemDefinitions.allItemDefinitions.ContainsKey(p.Key)))
        {
            string field = pair.Value.Substring(pair.Value.LastIndexOf('.') + 1);
            if (texts.TryGetValue("ITEM_" + field + "_DESC", out string? text))
            {
                List<StackText.Stat> stats = StackText.Read(text);
                if (stats.Count > 0)
                {
                    Console.WriteLine($"     {pair.Value}: " + string.Join(", ", stats.Select(x => $"{x.Label} {x.At(1)}")));
                }
            }
        }
    }
}
