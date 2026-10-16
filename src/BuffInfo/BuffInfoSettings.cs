using System;
using BepInEx.Configuration;

namespace JustBeBetter.BuffInfo
{
    internal sealed class BuffInfoSettings
    {
        internal const string Section = "Buff Info";

        internal BuffInfoSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Shows how long each timed buff has left on its icon, and what a buff does when you hover it.");

            Timers = config.Bind(
                Section,
                "Buff Timers",
                true,
                "Shows the seconds left on timed buffs and debuffs. Only for the host or in solo, as the game "
                + "doesn't send buff timers to other players.");

            OneDecimal = config.Bind(
                Section,
                "One Decimal",
                true,
                "Shows timers to a tenth of a second, such as 1.4. Off rounds up to whole seconds.");

            Descriptions = config.Bind(
                Section,
                "Buff Descriptions",
                true,
                "Shows a buff's name and what it does when you hover its icon. Covers the game's own buffs, in "
                + "English, others show their name.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> Timers { get; }

        internal ConfigEntry<bool> OneDecimal { get; }

        internal ConfigEntry<bool> Descriptions { get; }
    }
}
