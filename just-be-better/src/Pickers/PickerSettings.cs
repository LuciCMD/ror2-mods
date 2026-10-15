using System;
using BepInEx.Configuration;

namespace JustBeBetter.Pickers
{
    internal sealed class PickerSettings
    {
        internal const string Section = "Item Pickers";
        internal const int MaxItemsPerRow = 30;

        // In PickerOrder's order
        internal static readonly string[] OrderLabels = { "Game's Order", "Most Held First", "A to Z", "By Tier" };

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
                + "Command menu and the Scrapper, sorts those two and crafting menus, and lets you size, see through "
                + "and close its window.");

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

            FitBigLists = config.Bind(
                Section,
                "Fit Big Lists",
                true,
                "Grows the picker window when it has more items than fit, and makes the buttons smaller once it "
                + "fills most of the screen, so every item shows without scrolling.");

            ItemsPerRow = config.Bind(
                Section,
                "Items per Row",
                0,
                new ConfigDescription(
                    "How many items a row holds before the next row starts, as far as the screen's width allows. 0 "
                    + "keeps lists the game's window holds as they are and makes bigger ones roughly square.",
                    new AcceptableValueRange<int>(0, MaxItemsPerRow)));

            WindowBlur = config.Bind(
                Section,
                "Window Blur",
                true,
                "Blurs the game behind the picker window, as the game does. Off shows the game sharp behind it.");

            WindowOpacity = config.Bind(
                Section,
                "Window Opacity",
                100,
                new ConfigDescription(
                    "How solid the picker window's background is, as a percentage of the game's own. Lower lets "
                    + "more of the game show through.",
                    new AcceptableValueRange<int>(0, 100)));

            CloseOnMovement = config.Bind(
                Section,
                "Close on Movement",
                true,
                "Closes the picker when you move, jump or sprint, using your own key bindings. Not while typing "
                + "in the search box, and not with a controller, where the stick moves around the picker.");

            CommandSorting = config.Bind(
                Section,
                "Command Sorting",
                PickerOrder.GamesOrder,
                "How the Command menu orders its items, after your favourites. Most Held First goes by the count "
                + "on each button, and By Tier runs from common to rare, then the void tiers.");

            ScrapperSorting = config.Bind(
                Section,
                "Scrapper Sorting",
                PickerOrder.ByTier,
                "How the Scrapper orders your items, after your favourites. The game's order is the order you got "
                + "them in. By Tier sorts them like Inventory Sort does your inventory bar, or common to rare when "
                + "that part is off.");

            CraftingSorting = config.Bind(
                Section,
                "Crafting Sorting",
                PickerOrder.ByTier,
                "How crafting menus such as the Wandering Chef's order your items, after your favourites. By Tier "
                + "sorts them like Inventory Sort does your inventory bar, or common to rare when that part is off.");

            UsableFirst = config.Bind(
                Section,
                "Usable Items First",
                true,
                "Puts the items no recipe can take at the bottom of a crafting menu.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> ItemCounts { get; }

        internal ConfigEntry<bool> HideZeroCounts { get; }

        internal ConfigEntry<bool> CorruptedMark { get; }

        internal ConfigEntry<bool> HoverTooltips { get; }

        internal ConfigEntry<bool> FitBigLists { get; }

        internal ConfigEntry<int> ItemsPerRow { get; }

        internal ConfigEntry<bool> WindowBlur { get; }

        internal ConfigEntry<int> WindowOpacity { get; }

        internal ConfigEntry<bool> CloseOnMovement { get; }

        internal ConfigEntry<PickerOrder> CommandSorting { get; }

        internal ConfigEntry<PickerOrder> ScrapperSorting { get; }

        internal ConfigEntry<PickerOrder> CraftingSorting { get; }

        internal ConfigEntry<bool> UsableFirst { get; }
    }
}
