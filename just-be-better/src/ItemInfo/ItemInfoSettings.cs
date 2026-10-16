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
                + "tooltips, when you hover or click an inventory's items, such as your HUD's or the scoreboard's, and "
                + "when you ping an item. Off leaves them to the game.");

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
                + "Times. Covers the game's own items, Aetherium's, and other mods' where their stats can be read.");

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

            PingInfo = config.Bind(
                Section,
                "Ping Info",
                true,
                "Shows the pickup card for an item, shop item or drone when you ping it, with what picking it up "
                + "would give you.");

            OtherPlayersPings = config.Bind(
                Section,
                "Pings From Other Players",
                false,
                "Also shows the card when another player pings something.");

            ShowHiddenItems = config.Bind(
                Section,
                "Show Hidden Shop Items",
                false,
                "Pinging a multishop's \"?\" item shows what it really is. This reveals what the game hides on "
                + "purpose.");

            AllyCardInfo = config.Bind(
                Section,
                "Ally Card Info",
                true,
                "Shows what an ally does when you point at its card in the ally list, a drone's own text or the "
                + "item, skill or artifact behind a summon. The cursor is free while the scoreboard is open.");

            enabledAtLaunch = Enabled.Value;
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> FullDescriptions { get; }

        internal ConfigEntry<bool> ItemStats { get; }

        internal ConfigEntry<bool> CapChance { get; }

        internal ConfigEntry<bool> PickupDetails { get; }

        internal ConfigEntry<bool> PingInfo { get; }

        internal ConfigEntry<bool> OtherPlayersPings { get; }

        internal ConfigEntry<bool> ShowHiddenItems { get; }

        internal ConfigEntry<bool> AllyCardInfo { get; }

        // Enabled counts as it was at launch like every section's, and off means the game's text in the pickers too
        internal bool FullText => enabledAtLaunch && FullDescriptions.Value;

        internal bool Stats => enabledAtLaunch && ItemStats.Value;

        internal bool EnabledAtLaunch => enabledAtLaunch;

        private readonly bool enabledAtLaunch;
    }
}
