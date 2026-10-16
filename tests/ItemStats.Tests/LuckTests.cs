using System.Linq;
using JustBeBetter.ItemStats;
using JustBeBetter.SkillInfo;
using JustBeBetter.Sounds;
using RoR2;

// A stat line shows the value with luck, and luck's share of it on its own line below
internal static partial class Program
{
    private static void RunLuck()
    {
        Test("From Luck: a chance shows with luck, and luck's share on a line below", LuckOwnLine);
        Test("From Luck: no luck, no line", LuckNone);
        Test("From Luck: negative luck shows the lower chance and a minus share", LuckNegative);
        Test("From Luck: a value luck doesn't change gets no line", LuckOnlyChances);
        Test("From Luck: picking one more shows the change on both lines", LuckArrow);
        Test("From Luck: Skill Info's item procs show it below the chance", LuckSkillProcs);
        Test("War Horn: its sound's ID matches the sound bank's listing", WarHornId);
        Test("Stat values: a sliver below zero reads as 0, not -0", FormatNoNegativeZero);
    }

    private static void FormatNoNegativeZero()
    {
        string? Show(float value, ItemStatsDef.MeasurementUnits unit) => (string?)FormatM.Invoke(null, new object[] { value, unit });
        Equal("0%", Show(-0.00001f, ItemStatsDef.MeasurementUnits.Percentage)!);
        Equal("0", Show(-0.0001f, ItemStatsDef.MeasurementUnits.Number)!);
        Equal("-5%", Show(-0.05f, ItemStatsDef.MeasurementUnits.Percentage)!);
        Equal("0 seconds", Show(-0.001f, ItemStatsDef.MeasurementUnits.Seconds)!);
    }

    private static ItemStatsDef Def(ItemDef item) => ItemDefinitions.allItemDefinitions[(int)item.itemIndex];

    private static void LuckOwnLine()
    {
        Utils.CapChance = true;
        string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(1), 0);
        Contains("<style=cIsDamage>19%</style>", s);
        Contains("\n<style=cStack>  From Luck:</style> <style=cIsDamage>+9%</style>", s);
        Expect(s.IndexOf("19%", System.StringComparison.Ordinal) < s.IndexOf("From Luck", System.StringComparison.Ordinal), "the value first");
    }

    private static void LuckNone()
    {
        string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(0), 0);
        Contains("<style=cIsDamage>10%</style>", s);
        Lacks("From Luck", s);
    }

    private static void LuckNegative()
    {
        Utils.CapChance = true;
        string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(-1), 0);
        Contains("<style=cIsDamage>1%</style>", s);
        Contains("From Luck:</style> <style=cIsDamage>-9%</style>", s);
    }

    // Dropping the luck lines leaves the lines a holder with no luck sees, apart from the values luck changed
    private static void LuckOnlyChances()
    {
        Utils.CapChance = true;
        int lines = 0;
        foreach (var pair in ItemDefinitions.allItemDefinitions.Where(d => d.Value.calculateValuesNew != null))
        {
            // A few read the run, Ghor's Tome its gold cost, which needs a game running
            string plain;
            try
            {
                plain = Lines(pair.Value, Master(0), 0);
            }
            catch (System.Reflection.TargetInvocationException)
            {
                continue;
            }

            string[] lucky = Lines(pair.Value, Master(3), 0).Split('\n');
            string[] noLuck = plain.Split('\n');
            Expect(lucky.Count(l => !l.Contains("From Luck")) == noLuck.Length, itemNames[pair.Key] + " keeps its lines");
            for (int i = 1; i < lucky.Length; i++)
            {
                Expect(!lucky[i].Contains("From Luck") || !lucky[i - 1].Contains("From Luck"), itemNames[pair.Key] + " one luck line per value");
            }

            lines += lucky.Count(l => l.Contains("From Luck"));
        }

        Expect(lines > 10, $"luck lines found across the table ({lines})");
        Lacks("From Luck", Lines(Def(RoR2Content.Items.Bear), Master(3), 0));
    }

    private static void LuckArrow()
    {
        Utils.CapChance = true;
        string s = Lines(Def(RoR2Content.Items.BleedOnHit), Master(1), 1);
        Contains("<style=cIsDamage>19%</style> → <style=cIsDamage>36%</style>", s);
        Contains("From Luck:</style> <style=cIsDamage>+9%</style> → <style=cIsDamage>+16%</style>", s);
    }

    private static void LuckSkillProcs()
    {
        Utils.CapChance = true;
        CharacterMaster master = Holding((RoR2Content.Items.BleedOnHit, 2));
        master.luck = 1f;
        string s = SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, master, AllLines);
        Contains(Language.GetString(RoR2Content.Items.BleedOnHit.nameToken) + ": <style=cIsDamage>36%</style> <style=cStack>(10 to cap)</style>", s);
        Contains("\n<style=cStack>  From Luck:</style> <style=cIsDamage>+16%</style>", s);
        master.luck = 0f;
        Lacks("From Luck", SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, master, AllLines));
    }

    // From proc_warhorn.txt in the game's sound banks
    private static void WarHornId()
    {
        Expect(EquipmentEvents.WarHornId == 245801894u, $"got {EquipmentEvents.WarHornId}");
    }
}
