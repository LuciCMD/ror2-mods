using System;
using BepInEx.Configuration;

namespace MousePlease
{
    internal sealed class Settings
    {
        internal Settings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            FreeInMenus = config.Bind(
                "Cursor",
                "Free Cursor in Menus",
                true,
                "Lets the mouse leave the game window whenever the game shows its cursor: menus, the lobby, "
                + "the pause menu, the Command picker and chat. Turn off to keep it inside the window as the "
                + "game does.");

            FreeWhileLoading = config.Bind(
                "Cursor",
                "Free Cursor While Loading",
                true,
                "Leaves the mouse free during the startup loading screen and stage loads, so other windows "
                + "can be used meanwhile.");
        }

        internal ConfigEntry<bool> FreeInMenus { get; }

        internal ConfigEntry<bool> FreeWhileLoading { get; }
    }
}
