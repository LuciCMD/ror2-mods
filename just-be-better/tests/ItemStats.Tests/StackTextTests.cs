using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JustBeBetter.ItemStats;

// Reading per-stack values from an item's description, for items the stat table doesn't have
internal static partial class Program
{
    private static void RunStackText()
    {
        Test("Stack text: the game's own form reads with its label and colour", StackVanillaForm);
        Test("Stack text: plain text without style tags reads too", StackPlainText);
        Test("Stack text: the label comes from before the value when nothing follows it", StackLabelBefore);
        Test("Stack text: hyperbolic, capped or 'more per stack' wording is left out", StackRejects);
        Test("Stack text: a step in another unit is left out, a step with none takes the value's", StackUnits);
        Test("Stack text: seconds, metres, signs and falling values", StackOtherUnits);
        Test("Stack text: two values with the same words get told apart", StackDuplicateLabels);
        Test("Stack text: a condition after the value gives way to the words before it", StackLabelConditions);
        Test("Stack text: a table entry built from it shows like the table's", StackAsDef);
        Test("Stack text: every English item description reads without throwing", StackEnglishCorpus);
        Test("Modded stats: an item with no description token has none, without throwing", ModdedNoToken);
    }

    private static void ModdedNoToken()
    {
        var item = (RoR2.ItemDef)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RoR2.ItemDef));
        Expect(ModdedStats.For(item, out string source) == null && source.Length == 0, "null token");
        item.descriptionToken = string.Empty;
        Expect(ModdedStats.For(item, out _) == null, "empty token");
    }

    private static void StackVanillaForm()
    {
        List<StackText.Stat> stats = StackText.Read(
            "<style=cIsDamage>10%</style> <style=cStack>(+10% per stack)</style> chance to <style=cIsDamage>bleed</style> an enemy for <style=cIsDamage>240%</style> base damage.");
        Expect(stats.Count == 1, "one value with a per-stack note, the 240% has none");
        Equal("Chance to Bleed an Enemy", stats[0].Label);
        Expect(stats[0].Type == ItemStatsDef.ValueType.Damage, "keeps the damage colour");
        Expect(stats[0].Unit == ItemStatsDef.MeasurementUnits.Percentage, "a percentage");
        Expect(Near(stats[0].At(1), 0.1f) && Near(stats[0].At(3), 0.3f), "10% then 30% at 3");
    }

    private static void StackPlainText()
    {
        List<StackText.Stat> stats = StackText.Read("Gain 15% (+15% per stack) attack speed.");
        Expect(stats.Count == 1, "found");
        Equal("Attack Speed", stats[0].Label);
        Expect(stats[0].Type == ItemStatsDef.ValueType.None, "no style, plain colour");
        Expect(Near(stats[0].At(2), 0.3f), "30% at 2");
    }

    private static void StackLabelBefore()
    {
        List<StackText.Stat> stats = StackText.Read("Increase movement speed by 14% (+14% per stack).");
        Equal("Increase Movement Speed", stats.Single().Label);
    }

    private static void StackRejects()
    {
        Expect(StackText.Read("Killing an enemy restores 10% max shield (+10% per stack hyperbolically.)").Count == 0, "hyperbolic");
        Expect(StackText.Read("explodes for 32x30% of your damage (+50% more per stack).").Count == 0, "more per stack, Nail Bomb's wording");
        Expect(StackText.Read("a 20% chance (+20% hyperbolically up to a maximum of 100% chance)").Count == 0, "hyperbolic with a cap");
        Expect(StackText.Read("Deals 100% damage.").Count == 0, "no per-stack note");
        Expect(StackText.Read(null).Count == 0 && StackText.Read(string.Empty).Count == 0, "nothing to read");
    }

    private static void StackUnits()
    {
        Expect(StackText.Read("Gain 10 (+5% per stack) armor.").Count == 0, "a number growing by a percentage");
        Expect(StackText.Read("Spawns orbs every 10s (-10% per stack).").Count == 0, "seconds cut by a percentage, which isn't a straight line");
        List<StackText.Stat> stats = StackText.Read("Gain 10% (+5 per stack) crit chance.");
        Expect(stats.Count == 1 && Near(stats[0].At(2), 0.15f), "the step takes the value's unit");
        Expect(StackText.Read("Gain 10% crit chance (+5% per stack).").Count == 0, "words between the value and its note, so which value it belongs to isn't sure");
    }

    private static void StackOtherUnits()
    {
        StackText.Stat seconds = StackText.Read("Stun enemies for 2s (+1s per stack).").Single();
        Expect(seconds.Unit == ItemStatsDef.MeasurementUnits.Seconds && Near(seconds.At(3), 4f), "seconds");
        Equal("Stun Enemies", seconds.Label);
        StackText.Stat spelled = StackText.Read("lasts 3 seconds (+1 second per stack)").Single();
        Expect(spelled.Unit == ItemStatsDef.MeasurementUnits.Seconds && Near(spelled.At(2), 4f), "spelled-out seconds");
        StackText.Stat metres = StackText.Read("in a 12m (+4m per stack) radius").Single();
        Expect(metres.Unit == ItemStatsDef.MeasurementUnits.Meters && Near(metres.At(2), 16f), "metres");
        StackText.Stat plus = StackText.Read("rolled <style=cIsUtility>+1</style> <style=cStack>(+1 per stack)</style> times").Single();
        Expect(plus.Unit == ItemStatsDef.MeasurementUnits.Number && Near(plus.At(3), 3f) && plus.Type == ItemStatsDef.ValueType.Utility, "a signed first value keeps its colour");
        StackText.Stat falling = StackText.Read("Reduce cooldowns by 30% (-10% per stack).").Single();
        Expect(Near(falling.At(2), 0.2f) && falling.At(9) == 0f, "a falling value stops at 0");
    }

    private static void StackDuplicateLabels()
    {
        List<StackText.Stat> stats = StackText.Read("Gain 10% (+10% per stack) damage. Gain 5% (+5% per stack) damage.");
        Expect(stats.Count == 2, "both read");
        Equal("Damage", stats[0].Label);
        Equal("Damage 2", stats[1].Label);
    }

    // Taken from the game's own descriptions, tags dropped
    private static void StackLabelConditions()
    {
        List<StackText.Stat> speed = StackText.Read(
            "Increase your attack speed by 10% (+3.5% per stack) for up to 3 (+1 per stack) enemies and allies within 20 meters.");
        Equal("Increase Your Attack Speed", speed[0].Label);
        Equal("Enemies and Allies", speed[1].Label);
        List<StackText.Stat> multikill = StackText.Read(
            "Killing an enemy increases your damage by 3.5% (+1% per stack), up to 10 (+5 per stack), for 7s.");
        Expect(Near(multikill[0].At(2), 0.045f), "the damage value pairs with its own note");
        Equal("Up To", multikill[1].Label);
        Equal("Chance to Get Higher Rarity", StackText.Read("On Shrine of Chance success, 40% (+10% per stack) chance to get higher rarity items.")[0].Label);
        Equal("Bonus Stats", StackText.Read("storing 20% (+5% per stack) bonus stats (health, regeneration, and damage).")[0].Label);
        Equal("Base Damage per Second", StackText.Read("deals an additional 120% (+120% per stack) base damage per second and stuns all enemies hit.")[0].Label);
        Equal("Within", StackText.Read("transfer 33% of every debuff stack to 1 enemy (+1 per stack) within 20m (+5m per stack).").Single().Label);
    }

    private static void StackAsDef()
    {
        ItemStatsDef def = StackText.ToDef(StackText.Read("<style=cIsDamage>10%</style> <style=cStack>(+10% per stack)</style> chance to bleed"))!;
        Equal("\n<color=white>Chance to Bleed: </color><style=cIsDamage>10%</style> → <style=cIsDamage>20%</style>", Lines(def, Master(0), 1));
        Expect(StackText.ToDef(new List<StackText.Stat>()) == null, "nothing read, no entry");
    }

    private static void StackEnglishCorpus()
    {
        string folder = Path.Combine(GameManagedFolder(), "..", "StreamingAssets", "Language", "en");
        int descriptions = 0, values = 0;
        var sample = new List<string>();
        var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file), options);
            if (!doc.RootElement.TryGetProperty("strings", out JsonElement strings))
            {
                continue;
            }

            foreach (JsonProperty entry in strings.EnumerateObject())
            {
                if (!entry.Name.StartsWith("ITEM_") || !entry.Name.EndsWith("_DESC"))
                {
                    continue;
                }

                descriptions++;
                List<StackText.Stat> stats = StackText.Read(entry.Value.GetString());
                values += stats.Count;
                foreach (StackText.Stat stat in stats.Take(1))
                {
                    if (sample.Count < 12)
                    {
                        sample.Add($"{entry.Name}: {stat.Label} {stat.At(1)} then {stat.At(2)}");
                    }
                }
            }
        }

        Console.WriteLine($"     {descriptions} descriptions, {values} per-stack values read");
        foreach (string line in sample)
        {
            Console.WriteLine("     " + line);
        }

        Expect(descriptions > 100 && values > 50, "read a good share of the game's own");
    }

    private static bool Near(float a, float b) => System.Math.Abs(a - b) < 0.0001f;

    private static string GameManagedFolder() => typeof(Program).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .First(a => a.Key == "GameManaged").Value!;
}
