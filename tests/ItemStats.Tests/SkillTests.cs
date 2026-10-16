using JustBeBetter.ItemStats;
using JustBeBetter.SkillInfo;
using RoR2;

// The lines added to skill tooltips, after FillCatalog so held items resolve
internal static partial class Program
{
    private static readonly SkillText.Lines AllLines = new SkillText.Lines(true, true, true, true);
    private static readonly SkillText.Lines NoProcLine = new SkillText.Lines(true, false, true, false);

    private static void RunSkills()
    {
        Test("Skill: a primary shows its proc coefficient and no cooldown line", SkillPrimary);
        Test("Skill: a dash marked -1 shows its cooldown and nothing about procs", SkillDash);
        Test("Skill: a reduced cooldown shows the base and the reduction", SkillReducedCooldown);
        Test("Skill: a note that carries on the proc line needs that line shown", SkillNoteOnProcLine);
        Test("Skill: a note on its own line shows without the proc line", SkillNoteOwnLine);
        Test("Skill: an unknown skill gets no proc line or item procs", SkillUnknown);
        Test("Skill: held Tri-Tip x2 reads 20% with 10 to cap", SkillItemProcs);
        Test("Skill: item procs need a holder, so the loadout screen shows none", SkillNoHolder);
        Test("Skill: every line off adds nothing", SkillNothing);
    }

    private static void SkillPrimary()
    {
        Equal("\n\nProc Coefficient: <style=cIsDamage>1.0</style>", SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, null, AllLines));
    }

    private static void SkillDash()
    {
        Equal("\n\nCooldown: <style=cIsUtility>4 s</style>", SkillText.Extra("COMMANDO_UTILITY_NAME", 4f, null, Holding((RoR2Content.Items.BleedOnHit, 2)), AllLines));
    }

    private static void SkillReducedCooldown()
    {
        Equal(
            "\n\nCooldown: <style=cIsUtility>6 s</style> <style=cStack>(base 8 s, 25% reduction)</style>",
            SkillText.Extra("COMMANDO_UTILITY_NAME", 8f, 6f, null, AllLines));
    }

    // Acrid's Caustic Leap adds its pool's coefficient after the leap's
    private static void SkillNoteOnProcLine()
    {
        Contains("Proc Coefficient: <style=cIsDamage>1.0</style> + <style=cIsDamage>0.1</style> Pool", SkillText.Extra("CROCO_UTILITY_NAME", 6f, null, null, AllLines));
        Lacks("Pool", SkillText.Extra("CROCO_UTILITY_NAME", 6f, null, null, NoProcLine));
    }

    private static void SkillNoteOwnLine()
    {
        Contains("\nTicks: <style=cIsDamage>19</style>", SkillText.Extra("HUNTRESS_SPECIAL_NAME", 12f, null, null, NoProcLine));
    }

    private static void SkillUnknown()
    {
        Equal("\n\nCooldown: <style=cIsUtility>5 s</style>", SkillText.Extra("MODDED_SKILL_NAME", 5f, null, Holding((RoR2Content.Items.BleedOnHit, 2)), AllLines));
    }

    private static void SkillItemProcs()
    {
        Utils.CapChance = true;
        string s = SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, Holding((RoR2Content.Items.BleedOnHit, 2)), AllLines);
        Contains("Your item procs:", s);
        Contains(Language.GetString(RoR2Content.Items.BleedOnHit.nameToken) + ": <style=cIsDamage>20%</style> <style=cStack>(10 to cap)</style>", s);
    }

    private static void SkillNoHolder()
    {
        Lacks("Your item procs", SkillText.Extra("COMMANDO_PRIMARY_NAME", 0f, null, null, AllLines));
    }

    private static void SkillNothing()
    {
        Equal(string.Empty, SkillText.Extra("HUNTRESS_SPECIAL_NAME", 12f, 9f, Holding((RoR2Content.Items.BleedOnHit, 2)), new SkillText.Lines(false, false, false, false)));
    }
}
