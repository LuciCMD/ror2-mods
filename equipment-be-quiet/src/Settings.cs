using System;
using BepInEx.Configuration;

namespace EquipmentBeQuiet
{
    internal sealed class Settings
    {
        private static readonly AcceptableValueRange<int> Percent = new AcceptableValueRange<int>(0, 100);

        internal Settings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ChimeVolume = config.Bind(
                "Volume",
                "Activation Chime Volume",
                0,
                new ConfigDescription(
                    "How loud the chime every equipment plays when used is, as a percentage of its normal volume. "
                    + "0 mutes it.",
                    Percent));

            SoundVolume = config.Bind(
                "Volume",
                "Equipment Sound Volume",
                0,
                new ConfigDescription(
                    "How loud each equipment's own sounds are, as a percentage of their normal volume. 0 mutes "
                    + "them.",
                    Percent));
        }

        internal ConfigEntry<int> ChimeVolume { get; }

        internal ConfigEntry<int> SoundVolume { get; }
    }
}
