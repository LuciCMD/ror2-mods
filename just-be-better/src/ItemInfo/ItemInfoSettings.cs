using System;
using BepInEx.Configuration;

namespace JustBeBetter.ItemInfo
{
    // Steers the item text everywhere it shows: the pickup card here, the item pickers' tooltips in theirs
    internal sealed class ItemInfoSettings
    {
        internal const string Section = "Item Info";

        internal ItemInfoSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Shows items' full descriptions and worked-out stats on the pickup card, in the item pickers' "
                + "tooltips and when you hover an inventory, such as your HUD's or the scoreboard's. Off leaves them "
                + "to the game.");

            FullDescriptions = config.Bind(
                Section,
                "Full Descriptions",
                true,
                "Uses the full description with its per-stack numbers. Off shows the game's one-line summary.");

            ItemStats = config.Bind(
                Section,
                "Item Stats",
                true,
                "Adds the item's worked-out stats under the description, such as your block chance from Tougher "
                + "Times. Covers the game's own items.");

            CapChance = config.Bind(
                Section,
                "Cap Chance at 100%",
                true,
                "Stops chance stats going past 100%, as the game does. Off shows the raw value, which can read oddly "
                + "with luck.");

            PickupDetails = config.Bind(
                Section,
                "Pickup Details",
                true,
                "Shows the full description and what the pickup changed, such as your block chance going from "
                + "47.37% to 51.22%, on the card that appears when you pick something up.");

            enabledAtLaunch = Enabled.Value;
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> FullDescriptions { get; }

        internal ConfigEntry<bool> ItemStats { get; }

        internal ConfigEntry<bool> CapChance { get; }

        internal ConfigEntry<bool> PickupDetails { get; }

        // Enabled counts as it was at launch like every section's, and off means the game's text in the pickers too
        internal bool FullText => enabledAtLaunch && FullDescriptions.Value;

        internal bool Stats => enabledAtLaunch && ItemStats.Value;

        internal bool EnabledAtLaunch => enabledAtLaunch;

        private readonly bool enabledAtLaunch;
    }
}
