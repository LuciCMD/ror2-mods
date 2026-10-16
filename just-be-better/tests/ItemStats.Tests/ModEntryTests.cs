using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using JustBeBetter.ItemStats;

// Our entries for Aetherium's items, worked out at its default config, and checked against the mod's own fields
internal static partial class Program
{
    private const string AetheriumDll = @"C:\Users\Eli\AppData\Roaming\r2modmanPlus-local\RiskOfRain2\profiles\Topia\BepInEx\plugins\KomradeSpectre-Aetherium\Aetherium.dll";

    // Aetherium 0.9.0's ActiveBind defaults
    private static readonly Dictionary<string, float> AetheriumDefaults = new Dictionary<string, float>
    {
        ["MoveSpeedPercentageBonusPerBuffStack"] = 0.07f, ["BuffStacksPerFeatheredPlume"] = 3f, ["BaseDurationOfBuffInSeconds"] = 5f,
        ["AdditionalDurationOfBuffInSeconds"] = 0.5f, ["PercentDamageThresholdRequiredToActivate"] = 1.2f, ["AmountOfNailsPerNailBomb"] = 32f,
        ["PercentDamagePerNailInNailBomb"] = 0.3f, ["PercentDamageBonusOfAdditionalStacks"] = 0.5f, ["NailBombAbsurdityLimiterCooldown"] = 2f,
        ["ShieldPercentageRestoredPerKill"] = 0.1f, ["AdditionalShieldPercentageRestoredPerKillDiminishing"] = 0.1f,
        ["MaximumPercentageShieldRestoredPerKill"] = 0.5f, ["BaseGrantShieldMultiplier"] = 0.08f, ["BaseDuplicationPercentChance"] = 0.2f,
        ["AdditionalDuplicationPercentChance"] = 0.2f, ["BaseRevivalPercentChance"] = 0.1f, ["AdditionalRevivalPercentChance"] = 0.1f,
        ["BaseDamageSpreadPercentage"] = 0.25f, ["AdditionalDamageSpreadPercentage"] = 0.25f, ["MaxDamageSpreadPercentage"] = 0.75f,
        ["DurationOfDamageSpread"] = 5f, ["EnableCleanseOnHit"] = 1f, ["CleansePercentage"] = 0.1f, ["BaseShieldingCoreArmorGrant"] = 15f,
        ["AdditionalShieldingCoreArmorGrant"] = 10f, ["BaseSwordDamageMultiplier"] = 2f, ["AdditionalSwordDamageMultiplier"] = 0.5f,
        ["AllStatValueGrantedPercentage"] = 0.5f, ["EnableDeathExplosion"] = 1f, ["WitchesRingTriggerThreshold"] = 5f,
        ["BaseCooldownDuration"] = 10f, ["AdditionalCooldownReduction"] = 0.1f, ["BaseSipCooldownDuration"] = 30f,
        ["AdditionalStackSipCooldownReductionPercentage"] = 0.75f, ["BaseRadiusGranted"] = 20f, ["AdditionalRadiusGranted"] = 5f,
        ["MaxEffectsAccrued"] = 8f, ["UseOldFunctionality"] = 0f, ["OldStartingForceMultiplier"] = 3f, ["OldAdditionalForceMultiplier"] = 2f,
        ["OldMinimumForceMultiplier"] = 3f, ["OldMaximumForceMultiplier"] = 10f, ["LunarChimeraBaseDamageBoost"] = 40f,
        ["LunarChimeraAdditionalDamageBoost"] = 10f, ["LunarChimeraBaseHPBoost"] = 10f, ["LunarChimeraBaseAttackSpeedBoost"] = 30f,
        ["LunarChimeraBaseMovementSpeedBoost"] = 2f, ["LunarChimeraResummonCooldownDuration"] = 30f, ["VoidImplosionDamageMultiplier"] = 120f,
        ["VoidImplosionBaseRadius"] = 15f, ["VoidImplosionAdditionalRadius"] = 7.5f, ["VoidHeartBaseTickingTimeBombHealthThreshold"] = 0.3f,
        ["VoidHeartAdditionalTickingTimeBombHealthThreshold"] = 0.05f, ["VoidHeartMaxTickingTimeBombHealthThreshold"] = 0.99f,
        ["VoidHeartCooldownDebuffDuration"] = 30f, ["BaseAttackSpeedReductionPercentage"] = 0.1f, ["AttackSpeedReductionPercentageCap"] = 0.1f,
        ["BaseMovementSpeedReductionPercentage"] = 0.1f, ["MovementSpeedReductionPercentageCap"] = 0.1f, ["BaseKnockbackReductionPercentage"] = 0.25f,
        ["ExponentialBaseMultiplier"] = 0.5f, ["PercentageIncreasePerHit"] = 0.1f, ["BaseMaximumPercentageCap"] = 3f,
        ["AdditionalMaximumPercentageCap"] = 1f,
    };

    private static void RunModEntries()
    {
        Test("Aetherium: every entry works out at its defaults, one to twenty stacks", AetheriumAllEvaluate);
        Test("Aetherium: values match its code at its defaults", AetheriumValues);
        Test("Aetherium: Toolbelt's chances take luck, as the mod rolls them with CheckRoll", AetheriumLuck);
        Test("Aetherium: a renamed config field makes the entry throw, so it's reported and skipped", AetheriumMissingField);
        Test("Aetherium: every config field and item class the entries name is in the installed mod", AetheriumFieldsExist);
        Test("Mod binding: a mod not loaded yet is tried again on a later hover", BindingRetries);
    }

    private static void BindingRetries()
    {
        var binding = new ModBinding("JbbLateMod", new[] { new ModEntry("JbbLateMod.Items.Thing") });
        var item = (RoR2.ItemDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RoR2.ItemDef));
        Expect(binding.For(item) == null && binding.Problems.Count == 0, "nothing bound and nothing wrong before the mod loads");
        System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("JbbLateMod"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        Expect(binding.For(item) == null, "still nothing for an item it doesn't know");
        Expect(binding.Problems.Count == 1 && binding.Problems[0].Contains("JbbLateMod.Items.Thing"), "tried once the mod loaded: " + string.Join("|", binding.Problems));
    }

    private static float AetheriumConfig(string name) =>
        AetheriumDefaults.TryGetValue(name, out float value) ? value : throw new MissingFieldException("Aetherium", name);

    private static List<float> AetheriumValues(string type, int count, float luck = 0f) =>
        AetheriumEntries.All.Single(e => e.TypeName == "Aetherium.Items." + type).ToDef(AetheriumConfig).calculateValuesNew(luck, count, 1f);

    private static void AetheriumAllEvaluate()
    {
        foreach (ModEntry entry in AetheriumEntries.All)
        {
            ItemStatsDef def = entry.ToDef(AetheriumConfig);
            Expect(def.descriptions.Count == entry.Lines.Length, entry.TypeName + " has a label per line");
            for (int n = 1; n <= 20; n++)
            {
                List<float> values = def.calculateValuesNew(0f, n, 1f);
                Expect(values.Count == entry.Lines.Length && values.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), $"{entry.TypeName} at {n}");
            }
        }
    }

    private static void AetheriumValues()
    {
        Expect(Near(AetheriumValues("Tier2.BloodthirstyShield", 1)[0], 0.1f), "Blood Soaked Shield 10% at one");
        Expect(Near(AetheriumValues("Tier2.BloodthirstyShield", 2)[0], 0.1f + (0.4f * (1f - (1f / 1.1f)))), "Blood Soaked Shield hyperbolic at two");
        Expect(Near(AetheriumValues("Tier2.SharkTeeth", 1)[0], 0.25f), "Shark Teeth 25% at one");
        Expect(Near(AetheriumValues("Tier2.SharkTeeth", 2)[0], 0.4f), "Shark Teeth 40% at two, not the text's 50%");
        Expect(Near(AetheriumValues("Tier2.SharkTeeth", 5)[0], 0.625f), "Shark Teeth 62.5% at five");
        Expect(Near(AetheriumValues("Tier2.SharkTeeth", 100)[0], 0.9f), "Shark Teeth stops at 90%");
        Expect(Near(AetheriumValues("Tier1.NailBomb", 2)[2], 0.45f), "Nail Bomb 45% a nail at two, 1.5 times");
        Expect(Near(AetheriumValues("Tier2.ShieldingCore", 3)[0], 35f), "Shielding Core 35 armor at three");
        Expect(Near(AetheriumValues("TierLunar.UnstableDesign", 1)[0], 4.9f), "Unstable Design 490% as its code gives");
        Expect(Near(AetheriumValues("TierLunar.ZenithAccelerator", 1)[3], 25f), "Zenith Accelerator 25 stacks to max");
        Expect(Near(AetheriumValues("Tier3.InspiringDrone", 2)[1], 6f), "Inspiring Drone 600% at two, its code not its text");
        Expect(Near(AetheriumValues("TierLunar.WeightedAnklet", 4)[0], 0.1f), "Weighted Anklet held at its cap");
    }

    private static void AetheriumLuck()
    {
        Utils.CapChance = true;
        Expect(Near(AetheriumValues("Tier2.EngineersToolbelt", 1)[0], 0.2f), "20% with no luck");
        Expect(Near(AetheriumValues("Tier2.EngineersToolbelt", 1, 1f)[0], 0.36f), "36% with one luck");
        ItemStatsDef def = AetheriumEntries.All.Single(e => e.TypeName.EndsWith("EngineersToolbelt")).ToDef(AetheriumConfig);
        Expect(def.hasChance, "marked as a chance");
        Contains("From Luck:</style> <style=cIsUtility>+16%</style>", Lines(def, Master(1), 0));
    }

    private static void AetheriumMissingField()
    {
        ItemStatsDef def = AetheriumEntries.All[0].ToDef(name => throw new MissingFieldException("Aetherium", name));
        bool threw = false;
        try
        {
            def.calculateValuesNew(0f, 1, 1f);
        }
        catch (MissingFieldException)
        {
            threw = true;
        }

        Expect(threw, "throws for the binding to catch");
    }

    // Read from the installed mod's metadata only, nothing is loaded or run
    private static void AetheriumFieldsExist()
    {
        if (!File.Exists(AetheriumDll))
        {
            Console.WriteLine("     Aetherium isn't installed in Topia, skipped");
            return;
        }

        using var stream = File.OpenRead(AetheriumDll);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();
        var fieldsByType = new Dictionary<string, HashSet<string>>();
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            string name = md.GetString(type.Namespace) + "." + md.GetString(type.Name);
            fieldsByType[name] = new HashSet<string>(type.GetFields().Select(f => md.GetString(md.GetFieldDefinition(f).Name)));
        }

        foreach (ModEntry entry in AetheriumEntries.All)
        {
            Expect(fieldsByType.TryGetValue(entry.TypeName, out HashSet<string>? fields), entry.TypeName + " is in the mod");
            var used = new HashSet<string>();
            ItemStatsDef def = entry.ToDef(name =>
            {
                used.Add(name);
                return AetheriumConfig(name);
            });
            def.calculateValuesNew(0f, 1, 1f);
            def.calculateValuesNew(0f, 2, 1f);
            foreach (string field in used)
            {
                Expect(fields!.Contains(field), $"{entry.TypeName}.{field} is a field of the mod's");
            }
        }
    }
}
