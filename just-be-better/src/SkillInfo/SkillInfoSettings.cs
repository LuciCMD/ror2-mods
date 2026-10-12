using System;
using BepInEx.Configuration;

namespace JustBeBetter.SkillInfo
{
    internal sealed class SkillInfoSettings
    {
        internal const string Section = "Skill Info";

        internal SkillInfoSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Adds cooldowns, proc coefficients and more to skill tooltips, on your skill icons in a run and on "
                + "the loadout screen.");

            Cooldown = config.Bind(
                Section,
                "Cooldown",
                true,
                "Shows the skill's cooldown. In a run it's yours after cooldown reduction, with the base value beside "
                + "it when items change it.");

            ProcCoefficient = config.Bind(
                Section,
                "Proc Coefficient",
                true,
                "Shows how strongly the skill triggers on-hit items, where 1 is full strength. Covers the game's own "
                + "survivors.");

            ExtraNotes = config.Bind(
                Section,
                "Extra Notes",
                false,
                "Adds notes for some skills, such as how many times a flamethrower hits or a second proc "
                + "coefficient for a pool or explosion.");

            ItemProcs = config.Bind(
                Section,
                "Your Item Procs",
                false,
                "Lists each proc item you hold with its chance for this skill, and how many stacks reach the cap. "
                + "Only in a run, and it follows Item Info's Cap Chance at 100%.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> Cooldown { get; }

        internal ConfigEntry<bool> ProcCoefficient { get; }

        internal ConfigEntry<bool> ExtraNotes { get; }

        internal ConfigEntry<bool> ItemProcs { get; }
    }
}
