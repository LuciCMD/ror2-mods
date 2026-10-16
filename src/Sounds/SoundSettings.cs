using System;
using BepInEx.Configuration;

namespace JustBeBetter.Sounds
{
    internal sealed class SoundSettings
    {
        internal const string Section = "Equipment Sounds";

        // Author's choice (2026-10-12)
        private const int DefaultVolume = 75;

        // Author's choice (2026-10-16)
        private const int DefaultWarHornVolume = 25;

        private static readonly AcceptableValueRange<int> Percent = new AcceptableValueRange<int>(0, 100);

        internal SoundSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Sets equipment volume as below. Off leaves equipment sounds to the game.");

            ChimeVolume = config.Bind(
                Section,
                "Activation Chime Volume",
                DefaultVolume,
                new ConfigDescription(
                    "How loud the chime every equipment plays when used is, as a percentage of its normal volume. "
                    + "0 mutes it.",
                    Percent));

            SoundVolume = config.Bind(
                Section,
                "Equipment Sound Volume",
                DefaultVolume,
                new ConfigDescription(
                    "How loud each equipment's own sounds are, as a percentage of their normal volume. 0 mutes "
                    + "them.",
                    Percent));

            WarHornVolume = config.Bind(
                Section,
                "War Horn Volume",
                DefaultWarHornVolume,
                new ConfigDescription(
                    "How loud War Horn's horn is, which sounds every time you use equipment, as a percentage of its "
                    + "normal volume. 0 mutes it.",
                    Percent));
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<int> WarHornVolume { get; }

        internal ConfigEntry<int> ChimeVolume { get; }

        internal ConfigEntry<int> SoundVolume { get; }
    }
}
