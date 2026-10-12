using System.Globalization;
using JustBeBetter.ItemStats;
using JustBeBetter.SkillInfo;
using RoR2;

// Edge cases for the skill tooltip lines, after FillCatalog so held items resolve
internal static partial class Program
{
    private static void RunSkillEdge()
    {
        Test("Skill edge: a cooldown above its base doesn't read as a negative reduction", SkillCooldownAboveBase);
        Test("Skill edge: a null name token gives the cooldown alone, no throw", SkillNullName);
        Test("Skill edge: cooldown numbers keep a dot under a comma culture", SkillCultureInvariant);
        Test("Skill edge: a 0 coefficient skill shows 0.0 and lists no item procs", SkillZeroCoefficient);
        Test("Skill edge: Tri-Tip's stacks to cap match exact arithmetic for every coefficient", SkillToCapExact);
        Test("Skill edge: every note's style tags open and close in pairs", SkillNoteTagsBalance);
    }

    // Two Visions of Heresy double the replacement's interval, the game's base stays at the SkillDef's value
    // Seeker's failed Meditate adds a temporary penalty on top too
    private static void SkillCooldownAboveBase()
    {
        string s = SkillText.Extra("SKILL_LUNAR_PRIMARY_REPLACEMENT_NAME", 2f, 4f, null, AllLines);
        Lacks("-100% reduction", s);
        Lacks("% reduction", s.Contains("(base 2 s, -", System.StringComparison.Ordinal) ? s : string.Empty);
    }

    private static void SkillNullName()
    {
        Equal("\n\nCooldown: <style=cIsUtility>3 s</style>", SkillText.Extra(null!, 3f, null, Holding((RoR2Content.Items.BleedOnHit, 2)), AllLines));
    }

    private static void SkillCultureInvariant()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        CultureInfo oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            string s = SkillText.Extra("MAGE_UTILITY_ALT_NAME", 8.5f, 6.25f, null, AllLines);
            Contains("6.25 s", s);
            Contains("base 8.5 s, 26.5% reduction", s);
            Contains("Proc Coefficient: <style=cIsDamage>0.5</style>", s);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    private static void SkillZeroCoefficient()
    {
        Utils.CapChance = true;
        string s = SkillText.Extra("CAPTAIN_SPECIAL_NAME", 0f, null, Holding((RoR2Content.Items.BleedOnHit, 2)), AllLines);
        Contains("Proc Coefficient: <style=cIsDamage>0.0</style>", s);
        Lacks("Your item procs", s);
    }

    // 10% per stack times the coefficient, so the stacks for 100% are the ceiling of 1 / (0.1 * c)
    // A per-stack value that lands a hair under an exact fraction pushes the ceiling one too high
    private static void SkillToCapExact()
    {
        Utils.CapChance = true;
        var cases = new (string Name, int ToCap)[]
        {
            ("HUNTRESS_SPECIAL_NAME", 50),
            ("CROCO_SPECIAL_ALT_NAME", 25),
            ("FALSESON_SPECIAL_NAME", 23),
            ("MAGE_UTILITY_ALT_NAME", 20),
            ("ENGI_SPECIAL_ALT1_NAME", 17),
            ("CORRUPTED_VOIDSURVIVOR_PRIMARY_NAME", 16),
            ("ENGI_PRIMARY_ALT_NAME", 15),
            ("CAPTAIN_PRIMARY_NAME", 14),
            ("HUNTRESS_SECONDARY_NAME", 13),
            ("CROCO_PRIMARY_NAME", 10),
            ("RAILGUNNER_SPECIAL_ALT_NAME", 7),
            ("RAILGUNNER_SPECIAL_NAME", 4),
        };

        var wrong = new System.Text.StringBuilder();
        foreach ((string name, int toCap) in cases)
        {
            string s = SkillText.Extra(name, 0f, null, Holding((RoR2Content.Items.BleedOnHit, 1)), AllLines);
            if (!s.Contains($"({toCap} to cap)", System.StringComparison.Ordinal))
            {
                int at = s.LastIndexOf('(');
                wrong.Append($"{name} wanted {toCap} got [{(at >= 0 ? s.Substring(at) : s)}]; ");
            }
        }

        if (wrong.Length > 0)
        {
            throw new AssertFail(wrong.ToString());
        }
    }

    // LookingGlass's inline notes for Caustic Leap and Plasma Bolt close a style they never open
    private static void SkillNoteTagsBalance()
    {
        var wrong = new System.Text.StringBuilder();
        foreach (string name in ProcCoefficientData.skillsAdditional.Keys)
        {
            string s = SkillText.Extra(name, 0f, null, null, AllLines);
            int opens = Count(s, "<style=");
            int closes = Count(s, "</style>");
            if (opens != closes)
            {
                wrong.Append($"{name} opens {opens} closes {closes}; ");
            }
        }

        if (wrong.Length > 0)
        {
            throw new AssertFail(wrong.ToString());
        }
    }

    private static int Count(string s, string part)
    {
        int n = 0;
        for (int i = s.IndexOf(part, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, System.StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
