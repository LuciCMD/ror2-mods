using System;
using BepInEx.Configuration;

namespace JustBeBetter.ToggleFire
{
    internal sealed class ToggleFireSettings
    {
        internal const string Section = "Toggle Fire";
        internal const float MinTapSeconds = 0.1f;
        internal const float MaxTapSeconds = 1f;

        internal ToggleFireSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            // Off unless asked for, as it changes how your primary skill's button works
            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Lets a quick click keep your primary skill firing until you click again, so you don't have to hold "
                + "the button down. Holding still fires as usual. Stops when a menu or the scoreboard opens, you use "
                + "another skill, die or move to the next stage.",
                false);

            TapSeconds = config.Bind(
                Section,
                "Tap Length",
                0.3f,
                new ConfigDescription(
                    "How short a click must be, in seconds, to keep firing. A longer press fires only while held.",
                    new AcceptableValueRange<float>(MinTapSeconds, MaxTapSeconds)));

            ShowMark = config.Bind(
                Section,
                "Show Mark",
                true,
                "Shows AUTO above your primary skill's icon while it keeps firing.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<float> TapSeconds { get; }

        internal ConfigEntry<bool> ShowMark { get; }
    }
}
