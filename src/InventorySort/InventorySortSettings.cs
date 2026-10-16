using System;
using BepInEx.Configuration;

namespace JustBeBetter.InventorySort
{
    internal sealed class InventorySortSettings
    {
        internal const string Section = "Inventory Sort";

        // In each enum's order
        internal static readonly string[] ScrapLabels = { "First", "Last", "With Its Tier" };
        internal static readonly string[] TierLabels = { "Rarest First", "Commonest First", "Off" };
        internal static readonly string[] StackLabels = { "Most First", "Fewest First", "Off" };

        internal InventorySortSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Sorts the items on your inventory bar, the scoreboard and the enemy item panel. Only the display "
                + "changes, never the inventory itself.");

            ScrapPosition = config.Bind(
                Section,
                "Scrap Position",
                InventorySort.ScrapPosition.First,
                "Where scrap goes. With Its Tier sorts it among the items of its colour.");

            TierOrder = config.Bind(
                Section,
                "Tier Order",
                InventorySort.TierOrder.RarestFirst,
                "Rarest First runs from lunar and boss items down to white ones, Commonest First the other way "
                + "round, with void items in their own block after the normal tiers. Off keeps the order you picked "
                + "them up in.");

            MixVoids = config.Bind(
                Section,
                "Mix Voids with Their Tier",
                false,
                "Sorts void items in with their normal tier instead of in a block after the normal tiers.");

            StackSize = config.Bind(
                Section,
                "Stack Size",
                StackOrder.MostFirst,
                "Orders the items within each tier by how many you hold. Off keeps the order you picked them up in.");

            SortRunReports = config.Bind(
                Section,
                "Sort Run Reports",
                false,
                "Sorts the game over screen and run reports too. Off shows them in the order the items were picked up.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<ScrapPosition> ScrapPosition { get; }

        internal ConfigEntry<TierOrder> TierOrder { get; }

        internal ConfigEntry<bool> MixVoids { get; }

        internal ConfigEntry<StackOrder> StackSize { get; }

        internal ConfigEntry<bool> SortRunReports { get; }

        internal SortRules Rules => new SortRules(TierOrder.Value, MixVoids.Value, StackSize.Value, ScrapPosition.Value);
    }
}
