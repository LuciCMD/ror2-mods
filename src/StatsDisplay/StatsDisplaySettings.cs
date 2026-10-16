using System;
using BepInEx.Configuration;

namespace JustBeBetter.StatsDisplay
{
    internal sealed class StatsDisplaySettings
    {
        internal const string Section = "Stats Display";
        internal const int MaxDecimals = StatMath.MaxDecimals;
        internal const int MaxFontSize = 40;
        internal const float MinDpsWindow = 1f;
        internal const float MaxDpsWindow = 30f;

        // LookingGlass's own defaults, so its texts and these read the same
        internal const string DefaultMainText =
            "<margin-left=0.6em><size=115%>Stats</size>\nDamage: [damage]\nAttack Speed: [attackSpeed]\n"
            + "Crit Chance: [critWithLuck]\nRegen: [regen]\nArmor: [armor] | [armorDamageReduction]\n"
            + "Speed: [speed]\nJumps: [availableJumps] / [maxJumps]\nKills: [killCount]\nDPS: [dps]\n"
            + "Combo: [combo]\n</margin>";

        internal const string DefaultTabText =
            "<margin-left=0.6em><size=115%>Stats</size>\nDamage: [damage]\nAttack Speed: [attackSpeed]\n"
            + "Crit Chance: [critWithLuck]\nBleed Chance: [bleedChanceWithLuck]\nRegen: [regen]\n"
            + "Armor: [armor] | [armorDamageReduction]\nSpeed: [speed]\nJumps: [availableJumps] / [maxJumps]\n"
            + "Total Kills: [killCountRun]\nMax Combo: [maxComboThisRun]\nMountain Shrines: [mountainShrines]\n"
            + "Portals: [portals] \n</margin>";

        private const string Names =
            " Each [name] is replaced by its value, and the game's rich text tags work too. Names: damage, "
            + "attackSpeed, attackSpeedPercent, crit, critWithLuck, critMultiplier, bleedChance, bleedChanceWithLuck, "
            + "maxHealth, maxShield, maxBarrier, effectiveHealth, effectiveMaxHealth, shieldFraction, "
            + "barrierDecayRate, healthPercentage, regen, regenHp, regenRaw, armor, armorDamageReduction, "
            + "curseHealthReduction, hasOSP, damagePercent, damagePercentWithWatch, maxHpPercent, speed, "
            + "speedPercent, speedPercentSprintAffected, velocity, acceleration, availableJumps, maxJumps, jumpPower, "
            + "maxJumpHeight, level, experience, luck, mountainShrines, portals, shopPortal, goldPortal, msPortal, "
            + "voidPortal, greenPortal, difficultyCoefficient, stage, ping, killCount, killCountRun, dps, "
            + "percentDps, combo, maxComboThisRun, maxCombo, killCombo, maxKillComboThisRun, maxKillCombo, "
            + "remainingComboDuration, teddyBearBlockChance, saferSpacesCD, instaKillChance, lvl1_damage, "
            + "lvl1_maxHealth.";

        internal StatsDisplaySettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Shows your stats, damage per second and combos as text under the objectives.");

            MainText = config.Bind(
                Section,
                "Main Text",
                DefaultMainText,
                "What the panel shows." + Names);

            TabText = config.Bind(
                Section,
                "Tab Text",
                DefaultTabText,
                "What the panel shows while the scoreboard is open. Leave it empty to show the main text.");

            StatColours = config.Bind(
                Section,
                "Stat Colours",
                true,
                "Shows each value in the game's colour for its kind, such as damage, healing or utility.");

            DecimalPlaces = config.Bind(
                Section,
                "Decimal Places",
                2,
                new ConfigDescription(
                    "How many decimals stats with fractions show, at most.",
                    new AcceptableValueRange<int>(0, MaxDecimals)));

            FontSize = config.Bind(
                Section,
                "Font Size",
                0,
                new ConfigDescription(
                    "The text's size. 0 matches the objectives' text.",
                    new AcceptableValueRange<int>(0, MaxFontSize)));

            DpsWindow = config.Bind(
                Section,
                "DPS Window",
                5f,
                new ConfigDescription(
                    "How many seconds of your damage DPS adds up, and how long a combo lasts without a hit.",
                    new AcceptableValueRange<float>(MinDpsWindow, MaxDpsWindow)));

            BestCombo = config.Bind(
                Section,
                "Best Combo",
                0f,
                "The most damage you've dealt in one combo, kept across runs for maxCombo.");

            BestKillCombo = config.Bind(
                Section,
                "Best Kill Combo",
                0,
                "The most kills you've made in one combo, kept across runs for maxKillCombo.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<string> MainText { get; }

        internal ConfigEntry<string> TabText { get; }

        internal ConfigEntry<bool> StatColours { get; }

        internal ConfigEntry<int> DecimalPlaces { get; }

        internal ConfigEntry<int> FontSize { get; }

        internal ConfigEntry<float> DpsWindow { get; }

        internal ConfigEntry<float> BestCombo { get; }

        internal ConfigEntry<int> BestKillCombo { get; }
    }
}
