using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace JustBeBetter.ChestTweaks
{
    // Defaults are the author's QoLChests settings, all live apart from Enabled
    internal sealed class ChestTweaksSettings
    {
        internal const string Section = "Chest Tweaks";
        internal const float MaxHideDelay = 10f;

        // QoLChests' yellow
        private static readonly Color Yellow = new Color32(255, 255, 1, 255);

        // Lists so Risk Of Options shows them in this order
        private readonly List<(ChestKind Kind, ConfigEntry<bool> Entry)> outline = new List<(ChestKind, ConfigEntry<bool>)>();
        private readonly List<(ChestKind Kind, ConfigEntry<bool> Entry)> hide = new List<(ChestKind, ConfigEntry<bool>)>();

        internal ChestTweaksSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Outlines chests, shops, drones and other interactables so they stand out, and fades or hides "
                + "chests, barrels and shops once used.");
            OutlineColour = config.Bind(
                Section,
                "Outline Colour",
                Yellow,
                "The colour of every outline, the one on what you look at included. Ones you can't afford still show red.");

            BindOutline(config, ChestKind.Chest, "Outline Chests", true, "Small, large and legendary chests, category chests and adaptive chests.");
            BindOutline(config, ChestKind.StealthedChest, "Outline Stealthed Chests", true, "The cloaked chests that only show up close.");
            BindOutline(config, ChestKind.Lockbox, "Outline Lockboxes", true, "Rusted and encrusted lockboxes and their trial and objective versions.");
            BindOutline(config, ChestKind.LunarPod, "Outline Lunar Pods", true, "Lunar pods, which cost a lunar coin.");
            BindOutline(config, ChestKind.VoidCradle, "Outline Void Cradles", true, "Void cradles, which cost health and give a void item.");
            BindOutline(config, ChestKind.Barrel, "Outline Barrels", true, "Barrels with money and experience.");
            BindOutline(config, ChestKind.EquipmentBarrel, "Outline Equipment Barrels", true, "Barrels with an equipment.");
            BindOutline(config, ChestKind.Shop, "Outline Shops", true, "Multishop terminals, the Bazaar's lunar shop, drone shops and other terminals.");
            BindOutline(config, ChestKind.Scrapper, "Outline Scrappers", true, "Scrappers for items and for drones.");
            BindOutline(config, ChestKind.Printer, "Outline Printers", true, "3D printers of every tier.");
            BindOutline(config, ChestKind.Drone, "Outline Drones", true, "Broken drones, drone stations and Lemurian eggs.");
            BindOutline(config, ChestKind.Turret, "Outline Turrets", true, "Broken gunner turrets.");
            BindOutline(config, ChestKind.NewtAltar, "Outline Newt Altars", true, "The altars that open the way to the Bazaar.");
            BindOutline(config, ChestKind.PressurePlate, "Outline Pressure Plates", true, "The pressure plates on Abandoned Aqueduct.");
            BindOutline(config, ChestKind.Shrine, "Outline Shrines", false, "Every shrine.");

            BindHide(config, ChestKind.Chest, "Hide Used Chests", "Chests once opened.");
            BindHide(config, ChestKind.StealthedChest, "Hide Used Stealthed Chests", "Stealthed chests once opened.");
            BindHide(config, ChestKind.LunarPod, "Hide Used Lunar Pods", "Lunar pods once opened.");
            BindHide(config, ChestKind.VoidCradle, "Hide Used Void Cradles", "Void cradles once opened.");
            BindHide(config, ChestKind.Barrel, "Hide Used Barrels", "Barrels once opened.");
            BindHide(config, ChestKind.EquipmentBarrel, "Hide Used Equipment Barrels", "Equipment barrels once opened.");
            BindHide(config, ChestKind.Shop, "Hide Used Shops", "Shop terminals once bought from or closed.");

            Fade = config.Bind(
                Section,
                "Fade Instead of Hiding",
                true,
                "Leaves used ones faint, so you can still see where they were. Off hides them completely.");
            HideDelay = config.Bind(
                Section,
                "Hide Delay",
                1f,
                new ConfigDescription(
                    "Seconds before a used one fades or hides.",
                    new AcceptableValueRange<float>(0f, MaxHideDelay)));
            KeepAsDrifter = config.Bind(
                Section,
                "Keep Used Ones as Drifter",
                false,
                "Leaves used ones in view while you play Drifter, who can pick them up.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<Color> OutlineColour { get; }

        internal ConfigEntry<bool> Fade { get; }

        internal ConfigEntry<float> HideDelay { get; }

        internal ConfigEntry<bool> KeepAsDrifter { get; }

        internal IEnumerable<ConfigEntry<bool>> OutlineEntries => outline.Select(o => o.Entry);

        internal IEnumerable<ConfigEntry<bool>> HideEntries => hide.Select(h => h.Entry);

        internal bool OutlineOn(ChestKind kind) => outline.Any(o => o.Kind == kind && o.Entry.Value);

        internal bool HideOn(ChestKind kind) => hide.Any(h => h.Kind == kind && h.Entry.Value);

        private void BindOutline(ConfigFile config, ChestKind kind, string label, bool on, string description)
            => outline.Add((kind, config.Bind(Section, label, on, description)));

        private void BindHide(ConfigFile config, ChestKind kind, string label, string description)
            => hide.Add((kind, config.Bind(Section, label, true, description)));
    }
}
