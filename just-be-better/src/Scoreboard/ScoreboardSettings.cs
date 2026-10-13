using System;
using BepInEx.Configuration;

namespace JustBeBetter.Scoreboard
{
    internal sealed class ScoreboardSettings
    {
        internal const string Section = "Scoreboard Counters";

        internal ScoreboardSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Counts your items by tier beside the scoreboard's Items header, and shows temporary items apart "
                + "in each player's total.");

            TierCounts = config.Bind(
                Section,
                "Tier Counts",
                true,
                "Shows your count per item tier in its colour after the Items header. Only yours, other players "
                + "show their total.");

            TempTotal = config.Bind(
                Section,
                "Temp Total",
                true,
                "Shows how many temporary items are held in blue brackets after each total.");

            TempPerTier = config.Bind(
                Section,
                "Temp per Tier",
                false,
                "Shows each tier's temporary items in small blue brackets after its count in the header.");

            CountTempWithPermanent = config.Bind(
                Section,
                "Count Temp with Permanent",
                false,
                "Adds temporary items into the tier counts and the total instead of showing them apart.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> TierCounts { get; }

        internal ConfigEntry<bool> TempTotal { get; }

        internal ConfigEntry<bool> TempPerTier { get; }

        internal ConfigEntry<bool> CountTempWithPermanent { get; }

        internal CounterText.Options Current()
        {
            CounterText.Options options = CounterText.Options.None;
            if (TierCounts.Value)
            {
                options |= CounterText.Options.TierCounts;
            }

            if (TempTotal.Value)
            {
                options |= CounterText.Options.TempTotal;
            }

            if (TempPerTier.Value)
            {
                options |= CounterText.Options.TempPerTier;
            }

            if (CountTempWithPermanent.Value)
            {
                options |= CounterText.Options.CountTempWithPermanent;
            }

            return options;
        }
    }
}
