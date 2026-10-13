using System;
using BepInEx.Configuration;

namespace JustBeBetter.Cooldowns
{
    internal sealed class CooldownSettings
    {
        internal const string Section = "Cooldown Timers";

        internal CooldownSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Shows the countdown on your skill and equipment icons whenever a charge is refilling, not only when "
                + "none are left.");

            Skills = config.Bind(
                Section,
                "Skill Cooldowns",
                true,
                "Shows a skill's countdown to its next charge while it recharges, even with charges left, such as "
                + "your secondary with Backup Magazines.");

            Equipment = config.Bind(
                Section,
                "Equipment Cooldown",
                true,
                "Shows your equipment's countdown to its next charge while it recharges, even with charges left, "
                + "such as with Fuel Cells.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> Skills { get; }

        internal ConfigEntry<bool> Equipment { get; }
    }
}
