using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using JustBeBetter.ItemStats;
using JustBeBetter.SkillInfo;
using RoR2;
using Console = System.Console;

// Stand-ins for a mod's item classes, bound by ModBinding from this test assembly
namespace JbbHuntMod
{
    public sealed class Option<T>
    {
        private readonly T value;

        public Option(T value) => this.value = value;

        public static implicit operator T(Option<T> option) => option.value;
    }

    // A subclass, typed as its base in the field
    public sealed class FloatOption : OptionBase<float>
    {
        public FloatOption(float value) : base(value) { }
    }

    public class OptionBase<T>
    {
        private readonly T value;

        public OptionBase(T value) => this.value = value;

        public static implicit operator T(OptionBase<T> option) => option.value;
    }

    public abstract class ItemBase
    {
        public ItemDef? ItemDef;
    }

    public abstract class ItemBase<T> : ItemBase where T : ItemBase<T>
    {
        public static T? instance { get; set; }
    }

    public sealed class GoodItem : ItemBase<GoodItem>
    {
        public static Option<float> Amount = new Option<float>(0.25f);
        public static Option<bool> Enabled = new Option<bool>(true);
        public static Option<int> Count = new Option<int>(3);
        public static Option<float>? Unset;
        public static OptionBase<float> Derived = new FloatOption(0.5f);
        public Option<float> PerHit = new Option<float>(0.75f);
    }

    public sealed class NoInstanceItem : ItemBase<NoInstanceItem>
    {
        public static Option<float> Amount = new Option<float>(1f);
    }

    // Its singleton's getter throws, as a static constructor that fails would
    public sealed class ThrowingItem : ItemBase
    {
        public static ThrowingItem instance => throw new InvalidOperationException("not ready");
    }

    public sealed class SecondItem : ItemBase<SecondItem>
    {
        public static Option<float> Amount = new Option<float>(2f);
    }
}

internal static partial class Program
{
    private static void RunHuntLuckBinding()
    {
        Test("Binding hunt: float, bool, int and instance options read through their implicit operator", HuntBindingReads);
        Test("Binding hunt: an option not set yet is a problem, not a throw", HuntBindingUnset);
        Test("Binding hunt: a missing config field is a problem", HuntBindingMissingField);
        Test("Binding hunt: an item the mod switched off is skipped quietly", HuntBindingNullInstance);
        Test("Binding hunt: an instance getter that throws doesn't take the other entries down", HuntBindingThrowingInstance);
        Test("Binding hunt: problems aren't reported again on later hovers when one entry throws", HuntBindingProblemsOnce);
        Test("Binding hunt: an option held as a subclass reads through its base operator", HuntBindingDerivedOption);
        Test("Modded hunt: a binding that throws still falls back to LookingGlass", HuntModdedThrowFallsBack);
        Test("Binding hunt: Aetherium's real shapes match what ModBinding looks for", HuntAetheriumShapes);
        Test("Modded hunt: an item with our entry and a LookingGlass entry takes ours", HuntModdedOrder);
        Test("Aetherium hunt: no entry throws at odd configs or huge stacks", HuntAetheriumOddNoThrow);
        Test("Aetherium hunt: a value with no number at odd configs is caught before it shows", HuntAetheriumOddFinite);
        Test("Luck hunt: a sliver of luck either way reads as plain 0 in every unit", HuntSignedSlivers);
        Test("Luck hunt: Skill Info's share of a sliver reads 0 and adds no line", HuntSkillSliver);
        Test("Luck hunt: fractional negative luck the game ignores adds no line", HuntLuckFractionalNegative);
    }

    private static readonly string HuntAssembly = typeof(JbbHuntMod.GoodItem).Assembly.GetName().Name!;

    private static ItemDef HuntItem(int index)
    {
        var item = (ItemDef)RuntimeHelpers.GetUninitializedObject(typeof(ItemDef));
        typeof(ItemDef).GetField("_itemIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, (ItemIndex)index);
        typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, (IntPtr)1);
        return item;
    }

    private static ModEntry HuntEntry(Type type, params string[] fields) => new ModEntry(
        type.FullName!,
        fields.Select(f => new ModLine(f, ItemStatsDef.MeasurementUnits.Number, ItemStatsDef.ValueType.Utility, (c, n) => c(f))).ToArray());

    private static (ItemDef Good, ItemDef Second) HuntSetUp()
    {
        var good = new JbbHuntMod.GoodItem { ItemDef = HuntItem(9001) };
        var second = new JbbHuntMod.SecondItem { ItemDef = HuntItem(9002) };
        JbbHuntMod.GoodItem.instance = good;
        JbbHuntMod.SecondItem.instance = second;
        return (good.ItemDef, second.ItemDef);
    }

    private static void HuntBindingReads()
    {
        (ItemDef good, _) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.GoodItem), "Amount", "Enabled", "Count", "PerHit") });
        ItemStatsDef? def = binding.For(good);
        Expect(binding.Problems.Count == 0, "no problems: " + string.Join("|", binding.Problems));
        Expect(def != null, "bound");
        List<float> v = def!.calculateValuesNew(0f, 1, 1f);
        Expect(Near(v[0], 0.25f) && Near(v[1], 1f) && Near(v[2], 3f) && Near(v[3], 0.75f), "values " + string.Join(",", v));
    }

    private static void HuntBindingUnset()
    {
        (ItemDef good, _) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.GoodItem), "Unset") });
        Expect(binding.For(good) == null, "not bound");
        Expect(binding.Problems.Count == 1 && binding.Problems[0].Contains("Unset"), "named: " + string.Join("|", binding.Problems));
    }

    private static void HuntBindingMissingField()
    {
        (ItemDef good, _) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.GoodItem), "Renamed") });
        Expect(binding.For(good) == null, "not bound");
        Expect(binding.Problems.Count == 1 && binding.Problems[0].Contains("Renamed"), "named: " + string.Join("|", binding.Problems));
    }

    private static void HuntBindingNullInstance()
    {
        (_, ItemDef second) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[]
        {
            HuntEntry(typeof(JbbHuntMod.NoInstanceItem), "Amount"),
            HuntEntry(typeof(JbbHuntMod.SecondItem), "Amount"),
        });
        Expect(binding.For(second) != null, "the other entry still binds");
        Expect(binding.Problems.Count == 0, "an item the mod switched off isn't a problem: " + string.Join("|", binding.Problems));
    }

    private static void HuntBindingThrowingInstance()
    {
        (_, ItemDef second) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[]
        {
            HuntEntry(typeof(JbbHuntMod.ThrowingItem), "Amount"),
            HuntEntry(typeof(JbbHuntMod.SecondItem), "Amount"),
        });
        ItemStatsDef? def = null;
        Exception? thrown = null;
        try
        {
            def = binding.For(second);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        Expect(thrown == null, "For threw " + thrown?.GetType().Name + ": " + thrown?.GetBaseException().Message);
        Expect(def != null, "Second Item still binds");
        Expect(binding.Problems.Any(p => p.Contains("ThrowingItem")), "the throwing one is a problem: " + string.Join("|", binding.Problems));
    }

    // AppendStats catches the throw and marks the item broken, so each new modded item hovered binds again
    private static void HuntBindingProblemsOnce()
    {
        HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[]
        {
            HuntEntry(typeof(JbbHuntMod.NoInstanceItem), "Amount"),
            HuntEntry(typeof(JbbHuntMod.ThrowingItem), "Amount"),
        });
        for (int hover = 0; hover < 3; hover++)
        {
            try
            {
                binding.For(HuntItem(9100 + hover));
            }
            catch (Exception)
            {
            }
        }

        int throwing = binding.Problems.Count(p => p.Contains("ThrowingItem"));
        Expect(throwing == 1, $"ThrowingItem reported {throwing} times: " + string.Join("|", binding.Problems));
        Expect(!binding.Problems.Any(p => p.Contains("NoInstanceItem")), "a switched-off item isn't reported");
    }

    // Aetherium 0.9.0 has no ConfigOption subclass, so this only has to fail cleanly
    private static void HuntBindingDerivedOption()
    {
        (ItemDef good, _) = HuntSetUp();
        var binding = new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.GoodItem), "Derived") });
        ItemStatsDef? def = binding.For(good);
        Expect(def != null && binding.Problems.Count == 0, "read through its base's operator: " + string.Join("|", binding.Problems));
    }

    // A throw out of the binding skips LookingGlass and the description too
    private static void HuntModdedThrowFallsBack()
    {
        (_, ItemDef second) = HuntSetUp();
        var field = typeof(ModdedStats).GetField("Bindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bindings = (Array)field.GetValue(null)!;
        object? old = bindings.GetValue(0);
        bindings.SetValue(("Aetherium", new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.ThrowingItem), "Amount") })), 0);
        try
        {
            WithFakeLookingGlass(() =>
            {
                var theirs = new FakeLookingGlassDef();
                theirs.descriptions!.Add("LG: ");
                theirs.valueTypes!.Add(FakeValueType.Damage);
                theirs.measurementUnits!.Add(FakeUnit.Percentage);
                theirs.calculateValuesFlat = n => new List<float> { 0.5f };
                FakeLookingGlass.allItemDefinitions[9002] = theirs;
                ItemStatsDef? def = null;
                Exception? thrown = null;
                try
                {
                    def = ModdedStats.For(second, out _);
                }
                catch (Exception e)
                {
                    thrown = e;
                }

                Expect(thrown == null, "ModdedStats.For threw " + thrown?.GetBaseException().Message);
                Expect(def != null && def.descriptions[0] == "LG: ", "LookingGlass's entry");
            });
        }
        finally
        {
            bindings.SetValue(old, 0);
        }
    }

    // Metadata only, nothing loaded or run
    private static void HuntAetheriumShapes()
    {
        if (!File.Exists(AetheriumDll))
        {
            Console.WriteLine("     Aetherium isn't installed in Topia, skipped");
            return;
        }

        using var stream = File.OpenRead(AetheriumDll);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();
        var types = md.TypeDefinitions.Select(h => md.GetTypeDefinition(h)).ToList();
        string Name(TypeDefinition t) => md.GetString(t.Namespace) + "." + md.GetString(t.Name);

        TypeDefinition? Find(string name) => types.Where(t => md.GetString(t.Name) == name).Cast<TypeDefinition?>().FirstOrDefault();

        TypeDefinition? itemBaseT = Find("ItemBase`1");
        Expect(itemBaseT != null, "ItemBase`1 exists");
        bool staticInstance = itemBaseT!.Value.GetProperties().Select(p => md.GetPropertyDefinition(p))
            .Any(p => md.GetString(p.Name) == "instance"
                && (md.GetMethodDefinition(p.GetAccessors().Getter).Attributes & System.Reflection.MethodAttributes.Static) != 0);
        Expect(staticInstance, "ItemBase`1.instance is a static property");

        TypeDefinition? itemBase = Find("ItemBase");
        Expect(itemBase != null, "ItemBase exists");
        bool itemDefField = itemBase!.Value.GetFields().Select(f => md.GetFieldDefinition(f))
            .Any(f => md.GetString(f.Name) == "ItemDef"
                && (f.Attributes & System.Reflection.FieldAttributes.Public) == System.Reflection.FieldAttributes.Public
                && (f.Attributes & System.Reflection.FieldAttributes.Static) == 0);
        Expect(itemDefField, "ItemBase.ItemDef is a public instance field");

        TypeDefinition? option = Find("ConfigOption`1");
        Expect(option != null, "ConfigOption`1 exists");
        Console.WriteLine($"     ConfigOption`1 is {Name(option!.Value)}, sealed {(option.Value.Attributes & System.Reflection.TypeAttributes.Sealed) != 0}");
        Expect(option.Value.GetMethods().Any(m => md.GetString(md.GetMethodDefinition(m).Name) == "op_Implicit"), "ConfigOption`1 has op_Implicit");

        // A subclass of ConfigOption`1 wouldn't match op_Implicit's parameter type
        Console.WriteLine("     option-like types: " + string.Join(", ", types.Select(Name).Where(n => n.Contains("Option"))));
    }

    private static void HuntModdedOrder()
    {
        (ItemDef good, _) = HuntSetUp();
        var field = typeof(ModdedStats).GetField("Bindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bindings = (Array)field.GetValue(null)!;
        object? old = bindings.GetValue(0);
        var ours = new ModBinding(HuntAssembly, new[] { HuntEntry(typeof(JbbHuntMod.GoodItem), "Amount") });
        bindings.SetValue(("Aetherium", ours), 0);
        try
        {
            WithFakeLookingGlass(() =>
            {
                var theirs = new FakeLookingGlassDef();
                theirs.descriptions!.Add("LG: ");
                theirs.valueTypes!.Add(FakeValueType.Damage);
                theirs.measurementUnits!.Add(FakeUnit.Percentage);
                theirs.calculateValuesFlat = n => new List<float> { 9f };
                FakeLookingGlass.allItemDefinitions[9001] = theirs;
                ItemStatsDef? def = ModdedStats.For(good, out string source);
                Expect(def != null && def.descriptions[0] == "Amount: ", "ours first, got " + (def?.descriptions.FirstOrDefault() ?? "null"));
                Expect(source.Contains("Aetherium"), "source " + source);
            });
        }
        finally
        {
            bindings.SetValue(old, 0);
        }
    }

    private static readonly int[] HuntCounts = { 1, 2, 10, 1000, int.MaxValue };

    private static Func<string, float> HuntConfig(float value, params string[] keepDefault) =>
        name => keepDefault.Contains(name) ? AetheriumConfig(name) : value;

    private static void HuntAetheriumOddNoThrow()
    {
        foreach (float value in new[] { 0f, -1f, 1e30f, float.MaxValue })
        {
            foreach (ModEntry entry in AetheriumEntries.All)
            {
                ItemStatsDef def = entry.ToDef(HuntConfig(value));
                foreach (int n in HuntCounts)
                {
                    foreach (float luck in new[] { 0f, 1f, -1f })
                    {
                        List<float> v = def.calculateValuesNew(luck, n, 1f);
                        Expect(v.Count == entry.Lines.Length, $"{entry.TypeName} at {value} x{n}");
                    }
                }
            }
        }
    }

    // A non-finite value shows as its label with nothing after it
    private static void HuntAetheriumOddFinite()
    {
        var bad = new List<string>();
        foreach (float value in new[] { 0f, -1f })
        {
            foreach (ModEntry entry in AetheriumEntries.All)
            {
                // A gap is fine as long as the check the binding runs finds it, so the entry is left out
                ItemStatsDef def = entry.ToDef(HuntConfig(value));
                bool caught = entry.FirstGap(def, 20) != null;
                foreach (int n in new[] { 1, 2, 3 })
                {
                    List<float> v = def.calculateValuesNew(0f, n, 1f);
                    for (int i = 0; i < v.Count; i++)
                    {
                        if ((float.IsNaN(v[i]) || float.IsInfinity(v[i])) && !caught)
                        {
                            bad.Add($"{entry.TypeName.Replace("Aetherium.Items.", string.Empty)} {entry.Lines[i].Label} at all {value} x{n} = {v[i]}");
                        }
                    }
                }
            }
        }

        // One config off at a time, the rest at defaults
        ModEntry zenith = AetheriumEntries.All.Single(e => e.TypeName.EndsWith("ZenithAccelerator"));
        Func<string, float> noPerHit = name => name == "PercentageIncreasePerHit" ? 0f : AetheriumConfig(name);
        List<float> z = zenith.ToDef(noPerHit).calculateValuesNew(0f, 1, 1f);
        if ((float.IsInfinity(z[3]) || float.IsNaN(z[3])) && zenith.FirstGap(zenith.ToDef(noPerHit), 20) == null)
        {
            bad.Add($"Zenith Stacks to Max with only PercentageIncreasePerHit 0 = {z[3]}, shows as [{Lines(zenith.ToDef(noPerHit), Master(0), 0).Replace("\n", "\\n")}]");
        }

        ModEntry ring = AetheriumEntries.All.Single(e => e.TypeName.EndsWith("WitchesRing"));
        Func<string, float> negative = name => name == "AdditionalCooldownReduction" ? -0.5f : AetheriumConfig(name);
        List<float> r = ring.ToDef(negative).calculateValuesNew(0f, 3, 1f);
        if ((float.IsInfinity(r[1]) || float.IsNaN(r[1])) && ring.FirstGap(ring.ToDef(negative), 20) == null)
        {
            bad.Add($"Witches Ring Cooldown with AdditionalCooldownReduction -0.5 at 3 = {r[1]}");
        }

        Expect(bad.Count == 0, bad.Count + " non-finite values", string.Join("\n", bad.Select(b => "     " + b)));
    }

    private static readonly MethodInfo SignedM = typeof(ItemStatText).GetMethod("Signed", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void HuntSignedSlivers()
    {
        foreach (ItemStatsDef.MeasurementUnits unit in Enum.GetValues(typeof(ItemStatsDef.MeasurementUnits)))
        {
            string? zero = (string?)FormatM.Invoke(null, new object[] { 0f, unit });
            if (zero == null)
            {
                continue;
            }

            foreach (float sliver in new[] { -1e-6f, 1e-6f, -0f, -0.00004f, 0.00004f })
            {
                string? signed = (string?)SignedM.Invoke(null, new object[] { sliver, unit });
                Expect(signed == zero, $"{unit} {sliver} gave [{signed}], wanted [{zero}]");
            }

            string? gain = (string?)SignedM.Invoke(null, new object[] { 0.5f, unit });
            string? loss = (string?)SignedM.Invoke(null, new object[] { -0.5f, unit });
            Expect(gain != null && gain.StartsWith("+", StringComparison.Ordinal), $"{unit} gain [{gain}]");
            Expect(loss != null && loss.StartsWith("-", StringComparison.Ordinal) && !loss.StartsWith("--", StringComparison.Ordinal), $"{unit} loss [{loss}]");
        }
    }

    private static readonly MethodInfo ProcValueM = typeof(SkillText).GetMethod("ProcValue", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void HuntSkillSliver()
    {
        foreach (ItemStatsDef.ChanceScaling scaling in Enum.GetValues(typeof(ItemStatsDef.ChanceScaling)))
        {
            var def = new ItemStatsDef { chanceScaling = scaling };
            string zero = (string)ProcValueM.Invoke(null, new object[] { def, 0f, true })!;
            foreach (float sliver in new[] { -1e-6f, 1e-6f, -0f, -0.0004f })
            {
                string got = (string)ProcValueM.Invoke(null, new object[] { def, sliver, true })!;
                Expect(got == zero, $"{scaling} {sliver} gave [{got}]");
            }

            string plainNeg = (string)ProcValueM.Invoke(null, new object[] { def, -1e-6f, false })!;
            Lacks("-", plainNeg);
        }
    }

    private static void HuntLuckFractionalNegative()
    {
        Utils.CapChance = true;
        string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(-0.5f), 1);
        Contains("<style=cIsDamage>10%</style> → <style=cIsDamage>20%</style>", s);
        Lacks("From Luck", s);
        CharacterMaster master = Holding((RoR2Content.Items.BleedOnHit, 2));
        master.luck = -0.5f;
        Lacks("From Luck", SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, master, AllLines));
    }
}
