using System;
using BepInEx.Configuration;

namespace JustBeBetter.Pickers
{
    internal sealed class PickerSettings
    {
        internal const string Section = "Item Pickers";

        internal PickerSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Adds a search box, favourite stars, item counts and tooltips to every item picker, such as the "
                + "Command menu and the Scrapper.");

            ItemCounts = config.Bind(
                Section,
                "Item Counts",
                true,
                "Shows how many of each item you have on its button, counted the way your HUD counts them. An item "
                + "your void item corrupts shows the void item's count in purple.");

            HideZeroCounts = config.Bind(
                Section,
                "Hide Zero Counts",
                false,
                "Leaves the count off items you have none of, so only the ones you hold stand out.");

            CorruptedMark = config.Bind(
                Section,
                "Corrupted Mark",
                true,
                "Shows the void item's icon on an item it would corrupt, since picking that item gives you more of "
                + "the void item instead.");

            HoverTooltips = config.Bind(
                Section,
                "Hover Tooltips",
                true,
                "Shows each item's tooltip on hover even with the game's Inspect option off. Off leaves it to "
                + "that option. What the tooltip says is set under Item Info, its stats show what picking the item "
                + "would give, and the Scrapper shows them as they are now.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> ItemCounts { get; }

        internal ConfigEntry<bool> HideZeroCounts { get; }

        internal ConfigEntry<bool> CorruptedMark { get; }

        internal ConfigEntry<bool> HoverTooltips { get; }
    }
}
