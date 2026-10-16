using System;
using BepInEx.Configuration;
using UnityEngine;

namespace JustBeBetter.ImmuneBar
{
    internal sealed class ImmuneBarSettings
    {
        internal const string Section = "Immune Health Bar";

        internal ImmuneBarSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Shows IMMUNE in place of your health numbers and colours the bar while nothing can hurt you, as in "
                + "Risk of Rain 1.");

            BarColour = config.Bind(
                Section,
                "Immune Colour",
                Color.yellow,
                "The colour the health bar turns while immune.");

            BossBar = config.Bind(
                Section,
                "Boss Bar",
                true,
                "Also shows it on the boss health bar at the top, such as while Mithrix can't be hurt.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<Color> BarColour { get; }

        internal ConfigEntry<bool> BossBar { get; }
    }
}
