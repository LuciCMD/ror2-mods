using System;
using BepInEx.Configuration;

namespace JustBeBetter.Mouse
{
    internal sealed class CursorSettings
    {
        internal const string Section = "Cursor";

        internal CursorSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Lets the mouse leave the game window as set below. Off leaves the cursor to the game.");

            FreeInMenus = config.Bind(
                Section,
                "Free Cursor in Menus",
                true,
                "Lets the mouse leave the game window whenever the game shows its cursor: menus, the lobby, "
                + "the pause menu, the Command picker and chat. Turn off to keep it inside the window as the "
                + "game does.");

            FreeWhileLoading = config.Bind(
                Section,
                "Free Cursor While Loading",
                true,
                "Leaves the mouse free during the startup loading screen and stage loads, so other windows "
                + "can be used meanwhile.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> FreeInMenus { get; }

        internal ConfigEntry<bool> FreeWhileLoading { get; }
    }
}
