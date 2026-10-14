using System;
using BepInEx.Configuration;

namespace JustBeBetter.Notifications
{
    internal sealed class NotificationSettings
    {
        internal const string Section = "Pickup Notifications";

        internal NotificationSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Lets you switch off the card that shows what you picked up, and change how long cards stay.");

            ShowPickupCards = config.Bind(
                Section,
                "Show Pickup Cards",
                true,
                "Shows a card for each item, equipment, drone or key item you pick up. Off still shows void "
                + "corruption, artifact and trial cards, and your pings.");

            CardDuration = config.Bind(
                Section,
                "Card Duration",
                CardRules.GameSeconds,
                new ConfigDescription(
                    "How many seconds each card stays, the game's is 6. Pings and trial cards keep their own length.",
                    new AcceptableValueRange<float>(CardRules.MinSeconds, CardRules.MaxSeconds)));
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> ShowPickupCards { get; }

        internal ConfigEntry<float> CardDuration { get; }
    }
}
