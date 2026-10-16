using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using JustBeBetter.ItemStats;
using RoR2;
using Console = System.Console;
using Path = System.IO.Path;

// Runs the ported stat table and our tooltip text outside the game, against the installed game's assemblies
// Covers the numbers and the text, not anything drawn on screen
internal static partial class Program
{
    // Past this a few items overflow, Shaped Glass reaches infinity at 128
    private const int MaxStacks = 100;

    private static int failures;
    private static readonly Dictionary<int, string> itemNames = new();
    private static readonly Dictionary<int, string> equipmentNames = new();

    private static int Main()
    {
        string managed = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "GameManaged").Value!;
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(managed, name.Name + ".dll");
            return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
        };
        Run();
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        FillContent();
        Test("RegisterAll builds the table without throwing", RegisterAllRuns);
        Test("Tougher Times 6 held reads 47.37% -> 7-stack value", TougherTimesArrow);
        Test("Harvester's Scythe 2 stacks matches LookingGlass (12 HP, 5%)", ScytheTwo);
        Test("Gesture 6 stacks matches LookingGlass (77.81%)", GestureSix);
        Test("No dangling arrow when the after value can't be shown", NoDanglingArrow);
        Test("Numbers use a dot under a comma culture", CultureInvariant);
        Test("Equal now and after values show once, no arrow", EqualValuesNoArrow);
        Test($"Every flat and luck calculator gives finite values up to {MaxStacks} stacks, no throws", SweepCalculators);
        Test("Every equipment calculator runs as DescribeEquipment calls it", SweepEquipment);
        Test("Chance stays within 100% with the cap on, any luck", CapHolds);
        Test("Table rows have matching description, type and unit counts", RowShapes);
        Test("Pickup card reads the change, 47.37% -> 51.22%", PickupChangeLine);
        Test("Card stats for a first stack show its value with no heading", StatsFirstStackNoHeading);
        Test("Picker first stack keeps its With this item heading", PickerFirstStackHeading);
        Test("Pickup card with nothing held adds no lines", PickupNothingHeld);
        Test("Card queued before its count change gets that change", ChangeAfterCard);
        Test("Count change before its card still reaches the card", ChangeBeforeCard);
        Test("Two queued cards of one item keep their own steps", QueuedCardsKeepTheirOwn);
        Test("A merged card covers both steps", MergedCardSpansBoth);
        Test("Old, other-item and falling changes aren't matched", StaleOrOtherNotMatched);
        Test("A change reaching a card already queued says so, so the card can redraw", MatchedAnnounced);
        FillCatalog();
        Test("Pickup card for a 7th Tougher Times reads 47.37% → 51.22%", PickupSeventhBear);
        Test("Pickup card for a first stack shows the value alone, no arrow or heading", PickupFirstStack);
        Test("Pickup card for an item held as its void item shows text only", PickupCorruptedTextOnly);
        Test("Pickup card for a Hex keeps the game's Hallowed Shrine hint", PickupHexKeepsSuffix);
        Test("Pickup card uses the change matched to it, not the latest stack", PickupUsesMatchedChange);
        RunHover();
        RunSkills();
        RunSkillEdge();
        RunWindowFit();
        RunHoverEdge();
        RunPickerSort();
        RunPickerSortEdge();
        RunCraftingSortEdge();
        RunInventorySort();
        RunInventorySortEdge();
        RunPing();
        RunPingEdge();
        RunTransformEdge();
        RunConfigNames();
        RunRiskOfOptionsLayout();
        RunCooldowns();
        RunCooldownEdge();
        RunBuffs();
        RunBuffEdge();
        RunStats();
        RunStatsEdge();
        RunScoreboard();
        RunScoreboardEdge();
        RunNotifications();
        RunNotificationEdge();
        RunHiddenEdge();
        RunInspectEdge();
        RunImmuneBar();
        RunImmuneBarEdge();
        RunIntroSkip();
        RunIntroSkipEdge();
        RunChestTweaks();
        RunChestTweaksEdge();
        RunToggleFire();
        RunToggleFireEdge();
        RunAllyCards();
        RunLuck();
        RunStackText();
        RunModEntries();
        RunAllyCardEdge();
        RunAllyOthersEdge();
        RunNewFeatureEdge();
        RunHuntLuckBinding();
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            failures++;
            Console.WriteLine("FAIL " + name + "\n     " + (e is TargetInvocationException t ? t.InnerException : e)?.ToString().Split('\n')[0]);
            if (e is AssertFail a && a.Detail != null)
            {
                Console.WriteLine(a.Detail);
            }
        }
    }

    private sealed class AssertFail : Exception
    {
        public AssertFail(string m, string? detail = null) : base(m) { Detail = detail; }
        public string? Detail { get; }
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual)
        {
            throw new AssertFail($"expected [{expected}] got [{actual}]");
        }
    }

    // Content fields are only filled by the game's loader, so give each a bare def with a unique index
    private static void FillContent()
    {
        Assembly ror2 = typeof(ItemDef).Assembly;
        int nextItem = 0, nextEquipment = 0, nextBuff = 0;
        FieldInfo itemIndexField = typeof(ItemDef).GetField("_itemIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo equipIndexField = typeof(EquipmentDef).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(f => f.FieldType == typeof(EquipmentIndex));
        FieldInfo buffIndexField = typeof(BuffDef).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(f => f.FieldType == typeof(BuffIndex));
        foreach (string owner in new[] { "RoR2.RoR2Content", "RoR2.DLC1Content", "RoR2.DLC2Content", "RoR2.DLC3Content", "RoR2.DLC4Content", "RoR2.JunkContent" })
        {
            Type? t = ror2.GetType(owner);
            if (t == null)
            {
                continue;
            }

            foreach (Type nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (FieldInfo f in nested.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.IsInitOnly || f.IsLiteral)
                    {
                        continue;
                    }

                    if (f.FieldType == typeof(ItemDef))
                    {
                        object def = RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
                        itemIndexField.SetValue(def, (ItemIndex)nextItem);
                        itemNames[nextItem] = owner + "." + f.Name;
                        nextItem++;
                        f.SetValue(null, def);
                    }
                    else if (f.FieldType == typeof(EquipmentDef))
                    {
                        object def = RuntimeHelpers.GetUninitializedObject(typeof(EquipmentDef));
                        equipIndexField.SetValue(def, (EquipmentIndex)nextEquipment);
                        equipmentNames[nextEquipment] = owner + "." + f.Name;
                        nextEquipment++;
                        f.SetValue(null, def);
                    }
                    else if (f.FieldType == typeof(BuffDef))
                    {
                        object def = RuntimeHelpers.GetUninitializedObject(typeof(BuffDef));
                        buffIndexField.SetValue(def, (BuffIndex)nextBuff++);
                        f.SetValue(null, def);
                    }
                }
            }
        }

        Console.WriteLine($"filled {nextItem} items, {nextEquipment} equipment, {nextBuff} buffs");
    }

    private static void RegisterAllRuns()
    {
        ItemDefinitions.RegisterAll();
        Console.WriteLine($"     table: {ItemDefinitions.allItemDefinitions.Count} items, {ItemDefinitions.allEquipmentDefinitions.Count} equipment");
    }

    private static readonly MethodInfo FormatM = typeof(ItemStatText).GetMethod("Format", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo AppendLinesM = typeof(ItemStatText).GetMethod("AppendLines", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo StackLinesM = typeof(ItemStatText).GetMethod("AppendStackLines", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ValuesM = typeof(ItemStatText).GetMethod("Values", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static CharacterMaster Master(float luck)
    {
        var m = (CharacterMaster)RuntimeHelpers.GetUninitializedObject(typeof(CharacterMaster));
        m.luck = luck;
        return m;
    }

    // At the master's own luck, the value a stat line shows
    private static List<float>? Values(ItemStatsDef def, CharacterMaster m, int count) => (List<float>?)ValuesM.Invoke(null, new object[] { def, m, count, m.luck });

    // DescribeItem's lines after the description, for picking up one more
    private static string Lines(ItemStatsDef def, CharacterMaster m, int held, string arrow = " → ")
    {
        var sb = new StringBuilder();
        StackLinesM.Invoke(null, new object?[] { sb, def, m, held, held + 1, arrow });
        return sb.ToString();
    }

    private static void TougherTimesArrow()
    {
        Utils.CapChance = true;
        ItemStatsDef def = ItemDefinitions.allItemDefinitions[(int)RoR2Content.Items.Bear.itemIndex];
        string s = Lines(def, Master(0), 6);
        Console.WriteLine("     " + s.Replace("\n", "\\n"));
        Equal("\n<color=white>Block Chance: </color><style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>", s);
    }

    private static void ScytheTwo()
    {
        Utils.CapChance = true;
        ItemStatsDef def = ItemDefinitions.allItemDefinitions[(int)RoR2Content.Items.HealOnCrit.itemIndex];
        List<float> v = Values(def, Master(0), 2)!;
        Equal("12 HP", (string)FormatM.Invoke(null, new object[] { v[0], def.measurementUnits[0] })!);
        Equal("5%", (string)FormatM.Invoke(null, new object[] { v[1], def.measurementUnits[1] })!);
        Console.WriteLine("     held 2: " + Lines(def, Master(0), 2).Replace("\n", "\\n"));
    }

    private static void GestureSix()
    {
        ItemStatsDef def = ItemDefinitions.allItemDefinitions[(int)RoR2Content.Items.AutoCastEquipment.itemIndex];
        List<float> v = Values(def, Master(0), 6)!;
        Equal("77.81%", (string)FormatM.Invoke(null, new object[] { v[0], def.measurementUnits[0] })!);
        Console.WriteLine("     held 6: " + Lines(def, Master(0), 6).Replace("\n", "\\n"));
    }

    private static void NoDanglingArrow()
    {
        var def = new ItemStatsDef();
        def.descriptions.Add("Thing: ");
        def.valueTypes.Add(ItemStatsDef.ValueType.Utility);
        def.measurementUnits.Add(ItemStatsDef.MeasurementUnits.Number);
        var sb = new StringBuilder();
        AppendLinesM.Invoke(null, new object?[] { sb, def, new List<float> { 3f }, new List<float> { float.PositiveInfinity }, " → ", null, null });
        string s = sb.ToString();
        Console.WriteLine("     " + s.Replace("\n", "\\n"));
        if (s.TrimEnd().EndsWith("→", StringComparison.Ordinal))
        {
            throw new AssertFail("line ends with a bare arrow: " + s.Replace("\n", "\\n"));
        }
    }

    private static void CultureInvariant()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        CultureInfo oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            Equal("47.37%", (string)FormatM.Invoke(null, new object[] { 0.47368422f, ItemStatsDef.MeasurementUnits.Percentage })!);
            Equal("1.5 seconds", (string)FormatM.Invoke(null, new object[] { 1.5f, ItemStatsDef.MeasurementUnits.Seconds })!);
            Equal("1234.5$", (string)FormatM.Invoke(null, new object[] { 1234.5f, ItemStatsDef.MeasurementUnits.Money })!);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    // The card path, AppendStats as DescribePickup calls it
    private static readonly MethodInfo AppendStatsM = typeof(ItemStatText).GetMethod("AppendStats", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string Stats(ItemDef item, int from, int to, bool heading)
    {
        var sb = new StringBuilder();
        AppendStatsM.Invoke(null, new object[] { sb, item, Master(0), from, to, " → ", heading });
        return sb.ToString();
    }

    private static void PickupChangeLine()
    {
        Utils.CapChance = true;
        Equal("\n\n<color=white>Block Chance: </color><style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>",
            Stats(RoR2Content.Items.Bear, 6, 7, false));
    }

    private static void StatsFirstStackNoHeading()
    {
        Utils.CapChance = true;
        Equal("\n\n<color=white>Block Chance: </color><style=cIsUtility>13.04%</style>", Stats(RoR2Content.Items.Bear, 0, 1, false));
    }

    private static void PickerFirstStackHeading()
    {
        Utils.CapChance = true;
        Equal("\n\n<style=cStack>With this item:</style>\n<color=white>Block Chance: </color><style=cIsUtility>13.04%</style>",
            Stats(RoR2Content.Items.Bear, 0, 1, true));
    }

    private static void PickupNothingHeld()
    {
        Equal(string.Empty, Stats(RoR2Content.Items.Bear, -1, 0, false));
    }

    private static void ChangeAfterCard()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object card = new();
        changes.CardQueued(card, 5, 10f, false);
        changes.CountChanged(5, 6, 7, 10.2f);
        ExpectChange(changes, card, 6, 7);
    }

    private static void ChangeBeforeCard()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object card = new();
        changes.CountChanged(5, 6, 7, 10f);
        changes.CardQueued(card, 5, 10.3f, false);
        ExpectChange(changes, card, 6, 7);
    }

    // Two quick pickups of one item queue two cards, each keeps its own step
    private static void QueuedCardsKeepTheirOwn()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object first = new(), second = new();
        changes.CountChanged(5, 6, 7, 10f);
        changes.CardQueued(first, 5, 10f, false);
        changes.CountChanged(5, 7, 8, 10.1f);
        changes.CardQueued(second, 5, 10.1f, false);
        ExpectChange(changes, first, 6, 7);
        ExpectChange(changes, second, 7, 8);
    }

    // The game folds a repeat into the card queued just before, which then covers both steps
    private static void MergedCardSpansBoth()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object card = new();
        changes.CountChanged(5, 6, 7, 10f);
        changes.CardQueued(card, 5, 10f, false);
        changes.CountChanged(5, 7, 8, 10.1f);
        changes.CardQueued(card, 5, 10.1f, true);
        ExpectChange(changes, card, 6, 8);
    }

    private static void StaleOrOtherNotMatched()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object card = new();
        changes.CountChanged(5, 6, 7, 10f);
        changes.CountChanged(9, 0, 1, 12f);
        changes.CountChanged(5, 3, 2, 12f);
        changes.CardQueued(card, 5, 12f, false);
        if (changes.TryGet(card, out int before, out int after))
        {
            throw new AssertFail($"matched a change it shouldn't: {before} to {after}");
        }
    }

    private static void MatchedAnnounced()
    {
        var changes = new JustBeBetter.ItemInfo.PickupChanges<object>();
        object card = new();
        var heard = new List<object>();
        changes.Matched += heard.Add;
        changes.CardQueued(card, 5, 10f, false);
        Equal("0", heard.Count.ToString(CultureInfo.InvariantCulture));
        changes.CountChanged(5, 0, 1, 10.1f);
        Equal("1", heard.Count.ToString(CultureInfo.InvariantCulture));
        if (!ReferenceEquals(heard[0], card))
        {
            throw new AssertFail("announced a different card");
        }
    }

    private static void ExpectChange(JustBeBetter.ItemInfo.PickupChanges<object> changes, object card, int before, int after)
    {
        if (!changes.TryGet(card, out int b, out int a))
        {
            throw new AssertFail("no change matched");
        }

        Equal($"{before} to {after}", $"{b} to {a}");
    }

    private static void EqualValuesNoArrow()
    {
        var def = new ItemStatsDef();
        def.descriptions.Add("Proc: ");
        def.valueTypes.Add(ItemStatsDef.ValueType.Damage);
        def.measurementUnits.Add(ItemStatsDef.MeasurementUnits.ProcCoeff);
        var sb = new StringBuilder();
        AppendLinesM.Invoke(null, new object?[] { sb, def, new List<float> { 0.5f }, new List<float> { 0.5f }, " → ", null, null });
        Equal("\n<color=white>Proc: </color><style=cIsDamage>0.5</style>", sb.ToString());
    }

    // A few rows read Run.instance or Stage.instance, which only exist in a run, so those are noted, not failed
    private static void SweepCalculators()
    {
        var problems = new List<string>();
        var needRun = new SortedSet<string>();
        foreach (bool cap in new[] { true, false })
        {
            Utils.CapChance = cap;
            foreach (KeyValuePair<int, ItemStatsDef> kv in ItemDefinitions.allItemDefinitions)
            {
                ItemStatsDef def = kv.Value;
                if (def.calculateValuesFlat == null && def.calculateValuesNew == null)
                {
                    continue;
                }

                for (int luck = -5; luck <= 5; luck++)
                {
                    for (int count = 1; count <= MaxStacks; count++)
                    {
                        List<float>? v;
                        try
                        {
                            v = def.calculateValuesFlat != null ? def.calculateValuesFlat(count) : def.calculateValuesNew!(luck, count, 1f);
                        }
                        catch (NullReferenceException) when (RoR2.Run.instance == null)
                        {
                            needRun.Add(itemNames[kv.Key]);
                            goto nextItem;
                        }
                        catch (Exception e)
                        {
                            problems.Add($"{itemNames[kv.Key]} count {count} luck {luck}: throws {e.GetType().Name}");
                            break;
                        }

                        if (v == null)
                        {
                            continue;
                        }

                        for (int i = 0; i < v.Count; i++)
                        {
                            if (float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                            {
                                problems.Add($"{itemNames[kv.Key]} count {count} luck {luck} cap {cap}: value {i} = {v[i]}");
                                goto nextItem;
                            }
                        }
                    }
                }
            nextItem:;
            }
        }

        Utils.CapChance = true;
        if (needRun.Count > 0)
        {
            Console.WriteLine($"     note, need a running game so not checked: {string.Join(", ", needRun)}");
        }

        if (problems.Count > 0)
        {
            var byItem = problems.GroupBy(p => p.Split(' ')[0]).Select(g => g.First() + $"  (x{g.Count()})");
            throw new AssertFail($"{problems.Distinct().Count()} problems", string.Join("\n", byItem.Select(p => "     " + p)));
        }
    }

    private static void SweepEquipment()
    {
        var problems = new List<string>();
        foreach (KeyValuePair<int, ItemStatsDef> kv in ItemDefinitions.allEquipmentDefinitions)
        {
            ItemStatsDef def = kv.Value;
            try
            {
                List<float>? v = def.calculateValuesFlat != null ? def.calculateValuesFlat(0) : def.calculateValuesNew?.Invoke(0f, 1, 1f);
                if (v == null)
                {
                    problems.Add($"{equipmentNames[kv.Key]}: no values (calculateValues/Body only? {def.calculateValues != null}/{def.calculateValuesBody != null})");
                }
            }
            catch (Exception e)
            {
                problems.Add($"{equipmentNames[kv.Key]}: throws {e.GetType().Name}");
            }
        }

        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} problems", string.Join("\n", problems.Select(p => "     " + p)));
        }
    }

    private static void CapHolds()
    {
        Utils.CapChance = true;
        for (int luck = -3; luck <= 10; luck++)
        {
            foreach (float b in new[] { 0f, 0.05f, 0.5f, 0.99f, 1f, 1.5f, 3f })
            {
                float r = Utils.CalculateChanceWithLuck(b, luck);
                if (r > 1.0001f || r < 0f || float.IsNaN(r))
                {
                    throw new AssertFail($"base {b} luck {luck} -> {r}");
                }
            }
        }
    }

    private static void RowShapes()
    {
        var problems = new List<string>();
        void Check(Dictionary<int, ItemStatsDef> table, Dictionary<int, string> names)
        {
            foreach (KeyValuePair<int, ItemStatsDef> kv in table)
            {
                ItemStatsDef d = kv.Value;
                if (d.descriptions.Count != d.valueTypes.Count || d.descriptions.Count != d.measurementUnits.Count)
                {
                    problems.Add($"{names[kv.Key]}: {d.descriptions.Count} descriptions, {d.valueTypes.Count} types, {d.measurementUnits.Count} units");
                }
            }
        }

        Check(ItemDefinitions.allItemDefinitions, itemNames);
        Check(ItemDefinitions.allEquipmentDefinitions, equipmentNames);
        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} problems", string.Join("\n", problems.Select(p => "     " + p)));
        }
    }

    // ItemCatalog and the void pairs only fill in a real run, so they get the filled defs and one pair,
    // Tougher Times to Safer Spaces
    private static readonly FieldInfo AliveField = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void FillCatalog()
    {
        var defs = new ItemDef[itemNames.Count];
        Assembly ror2 = typeof(ItemDef).Assembly;
        foreach (string owner in new[] { "RoR2.RoR2Content", "RoR2.DLC1Content", "RoR2.DLC2Content", "RoR2.DLC3Content", "RoR2.DLC4Content", "RoR2.JunkContent" })
        {
            foreach (Type nested in ror2.GetType(owner)?.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic) ?? Type.EmptyTypes)
            {
                foreach (FieldInfo f in nested.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType == typeof(ItemDef) && f.GetValue(null) is ItemDef d)
                    {
                        d.nameToken = f.Name + "_NAME";
                        d.pickupToken = f.Name + "_PICKUP";
                        d.descriptionToken = f.Name + "_DESC";
                        d.tags = Array.Empty<ItemTag>();
                        defs[(int)d.itemIndex] = d;

                        // Alive to Unity's null checks, as a loaded game's are
                        AliveField.SetValue(d, (IntPtr)1);
                    }
                }
            }
        }

        typeof(RoR2.ContentManagement.ContentManager).GetField("_itemDefs", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, defs);
        Type manager = typeof(RoR2.Items.ContagiousItemManager);
        var toVoid = Enumerable.Repeat(ItemIndex.None, defs.Length).ToArray();
        toVoid[(int)RoR2Content.Items.Bear.itemIndex] = DLC1Content.Items.BearVoid.itemIndex;
        manager.GetField("originalToTransformed", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, toVoid);
        var pairs = new[]
        {
            new RoR2.Items.ContagiousItemManager.TransformationInfo
            {
                originalItem = RoR2Content.Items.Bear.itemIndex,
                transformedItem = DLC1Content.Items.BearVoid.itemIndex,
            },
        };
        manager.GetField("_transformationInfos", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, pairs);
        manager.GetField("transformationInfos", BindingFlags.Static | BindingFlags.Public)!
            .SetValue(null, new HG.ReadOnlyArray<RoR2.Items.ContagiousItemManager.TransformationInfo>(pairs));
    }

    // A master whose inventory holds these effective counts, marked alive so Unity's null checks pass
    // Never hand it to anything that calls into the engine
    private static CharacterMaster Holding(params (ItemDef item, int count)[] held)
    {
        FieldInfo cachedPtr = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        cachedPtr.SetValue(inventory, (IntPtr)1);
        ItemCollection stacks = ItemCollection.Create();
        foreach ((ItemDef item, int count) in held)
        {
            stacks.SetStackValue(item.itemIndex, count);
        }

        typeof(Inventory).GetField("effectiveItemStacks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(inventory, stacks);
        CharacterMaster master = Master(0);
        cachedPtr.SetValue(master, (IntPtr)1);
        typeof(CharacterMaster).GetField("<inventory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(master, inventory);
        return master;
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new AssertFail($"expected [{expected}] in [{actual.Replace("\n", "\\n")}]");
        }
    }

    private static void Lacks(string unwanted, string actual)
    {
        if (actual.Contains(unwanted, StringComparison.Ordinal))
        {
            throw new AssertFail($"didn't expect [{unwanted}] in [{actual.Replace("\n", "\\n")}]");
        }
    }

    private static void PickupSeventhBear()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribePickup(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 7)), "Bear_PICKUP", false, true, " → ", null)!;
        Console.WriteLine("     " + s.Replace("\n", "\\n"));
        Contains("<style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>", s);
    }

    private static void PickupFirstStack()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribePickup(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 1)), "Bear_PICKUP", false, true, " → ", null)!;
        Console.WriteLine("     " + s.Replace("\n", "\\n"));
        Contains("<style=cIsUtility>13.04%</style>", s);
        Lacks("→", s);
        Lacks("With this item", s);
    }

    private static void PickupCorruptedTextOnly()
    {
        CharacterMaster m = Holding((RoR2Content.Items.Bear, 1), (DLC1Content.Items.BearVoid, 2));
        Equal("Bear_PICKUP", ItemStatText.DescribePickup(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ", null)!);
    }

    // GenericNotification.SetItem wraps a Hex's one-liner in HEXTIER_PICKUP_SUFFIX, the pointer to the
    // Hallowed Shrine, so with full text off and no stats to add the card should read the same
    private static void PickupHexKeepsSuffix()
    {
        var hex = (ItemDef)RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
        typeof(ItemDef).GetField("deprecatedTier", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hex, ItemTier.Hex);
        typeof(ItemDef).GetField("_itemIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(hex, (ItemIndex)0);
        hex.tags = Array.Empty<ItemTag>();
        hex.nameToken = "ITEM_HEXCURSE_NAME";
        hex.pickupToken = "ITEM_HEXCURSE_PICKUP";
        hex.descriptionToken = "ITEM_HEXCURSE_DESC";
        string game = Language.GetStringFormatted("HEXTIER_PICKUP_SUFFIX", Language.GetString(hex.pickupToken));
        Equal(game, ItemStatText.DescribePickup(hex, null, game, false, true, " → ", null)!);
    }

    // A card shown after two more stacks landed still reads its own step, 6 to 8 for a merged card
    private static void PickupUsesMatchedChange()
    {
        Utils.CapChance = true;
        string s = ItemStatText.DescribePickup(RoR2Content.Items.Bear, Holding((RoR2Content.Items.Bear, 9)), "Bear_PICKUP", false, true, " → ", (6, 8))!;
        Contains("<style=cIsUtility>47.37%</style> → <style=cIsUtility>54.55%</style>", s);
    }
}
