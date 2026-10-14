using System;
using BepInEx.Configuration;

namespace JustBeBetter.Hidden
{
    internal sealed class HiddenSettings
    {
        internal const string Section = "Hidden Items and Buffs";

        internal HiddenSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Can show the items and buffs the game keeps out of sight, such as the difficulty helpers.");

            UnhideItems = config.Bind(
                Section,
                "Unhide Items",
                false,
                "Shows hidden items in every inventory, enemies' too. They often have no proper name or description.");

            UnhideBuffs = config.Bind(
                Section,
                "Unhide Buffs",
                false,
                "Shows hidden buffs on every buff bar, enemies' too. Ones without an icon get a placeholder.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> UnhideItems { get; }

        internal ConfigEntry<bool> UnhideBuffs { get; }
    }
}
