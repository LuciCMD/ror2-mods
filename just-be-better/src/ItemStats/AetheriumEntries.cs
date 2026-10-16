using System;
using U = JustBeBetter.ItemStats.ItemStatsDef.MeasurementUnits;
using V = JustBeBetter.ItemStats.ItemStatsDef.ValueType;

namespace JustBeBetter.ItemStats
{
    // Aetherium 0.9.0's items, written from its code rather than its descriptions, which differ in places
    // Shark Teeth's text says +25% per stack but it stacks hyperbolically, and Inspiring Drone's explosion
    // ignores its own config, so these show what the game actually does
    internal static class AetheriumEntries
    {
        private const string Items = "Aetherium.Items.";

        internal static ModEntry[] All { get; } =
        {
            new ModEntry(
                Items + "Tier1.FeatheredPlume",
                Line("Speed per Buff", U.Percentage, V.Utility, (c, n) => c("MoveSpeedPercentageBonusPerBuffStack")),
                Line("Max Buffs", U.Number, V.Utility, (c, n) => c("BuffStacksPerFeatheredPlume") * n),
                Line("Max Speed Bonus", U.Percentage, V.Utility, (c, n) => c("MoveSpeedPercentageBonusPerBuffStack") * c("BuffStacksPerFeatheredPlume") * n),
                Line("Buff Duration", U.Seconds, V.Utility, (c, n) => c("BaseDurationOfBuffInSeconds") + (c("AdditionalDurationOfBuffInSeconds") * (n - 1)))),

            // Every nail's damage grows by the bonus, "+50% more" meaning 1.5 times at two stacks
            new ModEntry(
                Items + "Tier1.NailBomb",
                Line("Triggers at Damage", U.Percentage, V.Damage, (c, n) => c("PercentDamageThresholdRequiredToActivate")),
                Line("Nails", U.Number, V.Damage, (c, n) => c("AmountOfNailsPerNailBomb")),
                Line("Damage per Nail", U.Percentage, V.Damage, (c, n) => c("PercentDamagePerNailInNailBomb") * (1f + (c("PercentDamageBonusOfAdditionalStacks") * (n - 1)))),
                Line("Enemy Immunity", U.Seconds, V.Utility, (c, n) => c("NailBombAbsurdityLimiterCooldown"))),

            new ModEntry(
                Items + "Tier2.BloodthirstyShield",
                Line("Shield Restored per Kill", U.Percentage, V.Utility, (c, n) => Hyperbolic(
                    c("ShieldPercentageRestoredPerKill"), c("AdditionalShieldPercentageRestoredPerKillDiminishing"), c("MaximumPercentageShieldRestoredPerKill"), n)),
                Line("Shield from Max Health", U.Percentage, V.Utility, (c, n) => c("BaseGrantShieldMultiplier"))),

            new ModEntry(
                Items + "Tier2.EngineersToolbelt",
                Line("Duplication Chance", U.Percentage, V.Utility, (c, n) => Hyperbolic(c("BaseDuplicationPercentChance"), c("AdditionalDuplicationPercentChance"), 1f, n), chance: true),
                Line("Revival Chance", U.Percentage, V.Utility, (c, n) => Hyperbolic(c("BaseRevivalPercentChance"), c("AdditionalRevivalPercentChance"), 1f, n), chance: true)),

            // Not Aetherium's usual hyperbolic helper, and it can pass its own maximum before the hard 90%
            new ModEntry(
                Items + "Tier2.SharkTeeth",
                Line("Damage Spread Out", U.Percentage, V.Damage, (c, n) => Clamp(
                    c("BaseDamageSpreadPercentage") + (c("MaxDamageSpreadPercentage") - (c("MaxDamageSpreadPercentage") / (1f + (c("AdditionalDamageSpreadPercentage") * (n - 1))))), 0f, 0.9f)),
                Line("Spread Over", U.Seconds, V.Utility, (c, n) => c("DurationOfDamageSpread")),
                Line("Bleed Cleansed per Hit", U.Percentage, V.Healing, (c, n) => c("EnableCleanseOnHit") > 0f ? c("CleansePercentage") : 0f)),

            new ModEntry(
                Items + "Tier2.ShieldingCore",
                Line("Armor While Shielded", U.Number, V.Healing, (c, n) => c("BaseShieldingCoreArmorGrant") + (c("AdditionalShieldingCoreArmorGrant") * (n - 1))),
                Line("Shield from Max Health", U.Percentage, V.Utility, (c, n) => c("BaseGrantShieldMultiplier"))),

            new ModEntry(
                Items + "Tier3.BlasterSword",
                Line("Sword Damage", U.Percentage, V.Damage, (c, n) => c("BaseSwordDamageMultiplier") + (c("AdditionalSwordDamageMultiplier") * (n - 1)))),

            // The explosion's damage and radius are fixed in its code, its config for them is never read
            new ModEntry(
                Items + "Tier3.InspiringDrone",
                Line("Your Stats Shared", U.Percentage, V.Utility, (c, n) => c("AllStatValueGrantedPercentage") * n),
                Line("Death Explosion Damage", U.Percentage, V.Damage, (c, n) => c("EnableDeathExplosion") > 0f ? 3f * n : 0f),
                Line("Death Explosion Radius", U.Meters, V.Damage, (c, n) => c("EnableDeathExplosion") > 0f ? 12f + (2f * n) : 0f)),

            new ModEntry(
                Items + "Tier3.WitchesRing",
                Line("Triggers at Damage", U.Percentage, V.Damage, (c, n) => c("WitchesRingTriggerThreshold")),
                Line("Cooldown", U.Seconds, V.Utility, (c, n) => c("BaseCooldownDuration") / (1f + (c("AdditionalCooldownReduction") * (n - 1))))),

            new ModEntry(
                Items + "TierLunar.AccursedPotion",
                Line("Sip Cooldown", U.Seconds, V.Utility, (c, n) => c("BaseSipCooldownDuration") * (float)Math.Pow(c("AdditionalStackSipCooldownReductionPercentage"), n - 1)),
                Line("Shared Within", U.Meters, V.Utility, (c, n) => c("BaseRadiusGranted") + (c("AdditionalRadiusGranted") * (n - 1))),
                Line("Max Effects", U.Number, V.Utility, (c, n) => c("MaxEffectsAccrued"))),

            // The default pull is a fixed speed, its "new" force config is worked out and never used
            new ModEntry(
                Items + "TierLunar.AlienMagnet",
                Line("Pull Strength", U.Number, V.Utility, (c, n) => c("UseOldFunctionality") > 0f
                    ? Clamp(c("OldStartingForceMultiplier") + (c("OldAdditionalForceMultiplier") * (n - 1)), c("OldMinimumForceMultiplier"), c("OldMaximumForceMultiplier"))
                    : 5f * n)),

            // Its damage items are base + extra * stacks - 1, a slip in its code, shown as the game gives them
            new ModEntry(
                Items + "TierLunar.UnstableDesign",
                Line("Chimera Damage Bonus", U.Percentage, V.Damage, (c, n) => 0.1f * (c("LunarChimeraBaseDamageBoost") + (c("LunarChimeraAdditionalDamageBoost") * n) - 1f)),
                Line("Chimera Health Bonus", U.Percentage, V.Health, (c, n) => 0.1f * c("LunarChimeraBaseHPBoost") * n),
                Line("Chimera Attack Speed Bonus", U.Percentage, V.Damage, (c, n) => 0.1f * c("LunarChimeraBaseAttackSpeedBoost")),
                Line("Chimera Move Speed Bonus", U.Percentage, V.Utility, (c, n) => 0.14f * c("LunarChimeraBaseMovementSpeedBoost") * n),
                Line("Resummon Delay", U.Seconds, V.Utility, (c, n) => c("LunarChimeraResummonCooldownDuration"))),

            new ModEntry(
                Items + "TierLunar.Voidheart",
                Line("Implosion Damage", U.Percentage, V.Damage, (c, n) => c("VoidImplosionDamageMultiplier")),
                Line("Implosion Radius", U.Meters, V.Damage, (c, n) => c("VoidImplosionBaseRadius") + (c("VoidImplosionAdditionalRadius") * (n - 1))),
                Line("Healing Hurts Below", U.Percentage, V.Health, (c, n) => Clamp(
                    c("VoidHeartBaseTickingTimeBombHealthThreshold") + (c("VoidHeartAdditionalTickingTimeBombHealthThreshold") * (n - 1)),
                    c("VoidHeartBaseTickingTimeBombHealthThreshold"),
                    c("VoidHeartMaxTickingTimeBombHealthThreshold"))),
                Line("Revive Cooldown", U.Seconds, V.Utility, (c, n) => c("VoidHeartCooldownDebuffDuration"))),

            // Each penalty stops at its cap, which by default is the first stack's
            new ModEntry(
                Items + "TierLunar.WeightedAnklet",
                Line("Attack Speed Penalty", U.Percentage, V.Health, (c, n) => Math.Min(n * c("BaseAttackSpeedReductionPercentage"), c("AttackSpeedReductionPercentageCap"))),
                Line("Move Speed Penalty", U.Percentage, V.Health, (c, n) => Math.Min(n * c("BaseMovementSpeedReductionPercentage"), c("MovementSpeedReductionPercentageCap"))),
                Line("Knockback Reduction", U.Percentage, V.Utility, (c, n) => Math.Min(n * c("BaseKnockbackReductionPercentage"), 1f))),

            // Attack speed is multiplied, so 50% at no stacks halves it and 300% at most triples it
            new ModEntry(
                Items + "TierLunar.ZenithAccelerator",
                Line("Attack Speed at No Stacks", U.Percentage, V.Health, (c, n) => (float)Math.Pow(c("ExponentialBaseMultiplier"), n)),
                Line("Max Attack Speed", U.Percentage, V.Damage, (c, n) => c("BaseMaximumPercentageCap") + ((n - 1) * c("AdditionalMaximumPercentageCap"))),
                Line("Attack Speed per Hit", U.Percentage, V.Damage, (c, n) => c("PercentageIncreasePerHit")),
                Line("Stacks to Max", U.Number, V.Utility, (c, n) => (float)Math.Ceiling(
                    (c("BaseMaximumPercentageCap") + ((n - 1) * c("AdditionalMaximumPercentageCap")) - Math.Pow(c("ExponentialBaseMultiplier"), n)) / c("PercentageIncreasePerHit")))),
        };

        private static ModLine Line(string label, U unit, V type, Func<Func<string, float>, int, float> value, bool chance = false)
        {
            return new ModLine(label, unit, type, value, chance);
        }

        // Aetherium's MathHelpers.InverseHyperbolicScaling
        internal static float Hyperbolic(float first, float step, float max, int count)
        {
            return first + ((max - first) * (1f - (1f / (1f + (step * (count - 1))))));
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
