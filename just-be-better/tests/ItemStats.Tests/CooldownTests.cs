using JustBeBetter.Cooldowns;

// Cooldown Timers: when an icon's countdown shows, and what it reads
internal static partial class Program
{
    private static void RunCooldowns()
    {
        Test("Cooldown: a skill with charges left but refilling one shows its countdown", CooldownSkillRefilling);
        Test("Cooldown: a skill at full charges, blocked, hidden or not recharging shows nothing extra", CooldownSkillQuiet);
        Test("Cooldown: an empty skill still counts as recharging, so a stale game number can't hide it", CooldownSkillEmpty);
        Test("Cooldown: seconds round up at the recharge rate and never go negative or overflow", CooldownSeconds);
        Test("Cooldown: equipment refilling a charge shows, full, disabled or none doesn't", CooldownEquipment);
        Test("Cooldown: the game's own equipment rule is kept as it is", CooldownEquipmentGameRule);
    }

    private static void CooldownSkillRefilling()
    {
        Expect(CooldownRules.SkillRecharging(1, 2, 3.2f, 1f, false, false), "1 of 2, 3.2 s left");
        Expect(CooldownRules.SkillRecharging(4, 5, 0.1f, 1f, false, false), "4 of 5, nearly done");
    }

    private static void CooldownSkillQuiet()
    {
        Expect(!CooldownRules.SkillRecharging(2, 2, 0f, 1f, false, false), "full");
        Expect(!CooldownRules.SkillRecharging(3, 2, 1f, 1f, false, false), "above max, as with a bonus charge");
        Expect(!CooldownRules.SkillRecharging(1, 2, 3f, 1f, true, false), "blocked");
        Expect(!CooldownRules.SkillRecharging(1, 2, 3f, 1f, false, true), "skill hides its cooldown");
        Expect(!CooldownRules.SkillRecharging(1, 2, 3f, 0f, false, false), "doesn't recharge");
        Expect(!CooldownRules.SkillRecharging(1, 2, 0f, 1f, false, false), "nothing left to wait");
    }

    private static void CooldownSkillEmpty()
    {
        Expect(CooldownRules.SkillRecharging(0, 1, 4f, 1f, false, false), "0 of 1");
    }

    private static void CooldownSeconds()
    {
        Expect(CooldownRules.SkillSeconds(3.2f, 1f) == 4, "3.2 s reads 4 as the game shows it");
        Expect(CooldownRules.SkillSeconds(3f, 1f) == 3, "whole seconds stay");
        Expect(CooldownRules.SkillSeconds(6f, 2f) == 3, "a doubled recharge rate halves it");
        Expect(CooldownRules.SkillSeconds(1f, 0f) == 0, "no rate");
        Expect(CooldownRules.SkillSeconds(-1f, 1f) == 0, "negative");
        Expect(CooldownRules.SkillSeconds(float.NaN, 1f) == 0, "NaN");
        Expect(CooldownRules.SkillSeconds(float.PositiveInfinity, 1f) == 0, "infinite");
        Expect(CooldownRules.SkillSeconds(1e12f, 1e-6f) == int.MaxValue, "huge");
    }

    private static void CooldownEquipment()
    {
        Expect(CooldownRules.EquipmentRecharging(true, false, 1, 2, 30), "1 of 2 refilling");
        Expect(CooldownRules.EquipmentRecharging(true, false, 0, 1, 30), "empty");
        Expect(!CooldownRules.EquipmentRecharging(true, false, 2, 2, 30), "full");
        Expect(!CooldownRules.EquipmentRecharging(true, true, 1, 2, 30), "disabled");
        Expect(!CooldownRules.EquipmentRecharging(false, false, 0, 1, 30), "no equipment");
        Expect(!CooldownRules.EquipmentRecharging(true, false, 1, 2, 0), "nothing left to wait");
    }

    private static void CooldownEquipmentGameRule()
    {
        Expect(CooldownRules.EquipmentGameShows(true, 0, 30), "empty with equipment");
        Expect(!CooldownRules.EquipmentGameShows(true, 1, 30), "a charge left");
        Expect(CooldownRules.EquipmentGameShows(false, 0, 5), "no equipment, still counting");
        Expect(!CooldownRules.EquipmentGameShows(false, 0, 0), "no equipment, nothing to count");
    }
}
