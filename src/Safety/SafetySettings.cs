using System;
using BepInEx.Configuration;

namespace JustBeBetter.Safety
{
    internal enum SoloMode
    {
        Pause,
        Protect,
        Off,
    }

    internal sealed class SafetySettings
    {
        internal const string Section = "Item Picker Safety";
        internal const float MaxGraceSeconds = 10f;

        internal SafetySettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Keeps you safe while you pick an item, and lets Escape close the picker.");

            SoloMode = config.Bind(
                Section,
                "Solo Mode",
                Safety.SoloMode.Pause,
                "What happens while you pick an item alone. Pause freezes the world, Protect makes you "
                + "invincible while the world keeps running, Off does nothing.");

            ProtectInMultiplayer = config.Bind(
                Section,
                "Protect in Multiplayer",
                true,
                "Makes the player picking an item invincible while everyone else keeps playing. Only the "
                + "host's setting counts.");

            CloakWhilePicking = config.Bind(
                Section,
                "Cloak While Picking",
                true,
                "Also cloaks the player picking, so enemies lose track of them. Applies in multiplayer and "
                + "in solo Protect mode.");

            GraceSeconds = config.Bind(
                Section,
                "Grace Period",
                1f,
                new ConfigDescription(
                    "Seconds of invincibility after the picker closes, to get your bearings. 0 turns it off.",
                    new AcceptableValueRange<float>(0f, MaxGraceSeconds)));
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<SoloMode> SoloMode { get; }

        internal ConfigEntry<bool> ProtectInMultiplayer { get; }

        internal ConfigEntry<bool> CloakWhilePicking { get; }

        internal ConfigEntry<float> GraceSeconds { get; }
    }
}
