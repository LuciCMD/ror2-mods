using System;
using BepInEx.Configuration;
using JustBeBetter.BuffInfo;
using JustBeBetter.ChestTweaks;
using JustBeBetter.Cooldowns;
using JustBeBetter.Hidden;
using JustBeBetter.ImmuneBar;
using JustBeBetter.InventorySort;
using JustBeBetter.ItemInfo;
using JustBeBetter.Loading;
using JustBeBetter.Mouse;
using JustBeBetter.Notifications;
using JustBeBetter.Pickers;
using JustBeBetter.Safety;
using JustBeBetter.Scoreboard;
using JustBeBetter.SkillInfo;
using JustBeBetter.StatsDisplay;
using JustBeBetter.Sounds;
using JustBeBetter.ToggleFire;

namespace JustBeBetter
{
    // One section per part of the mod, each with an Enabled read once at launch: a part that's off isn't hooked
    // at all, so it can't clash with another mod doing the same job
    internal sealed class Settings
    {
        internal const string LobbySection = "Lobby";
        internal const string SavesSection = "Saves";
        internal const string GeneralSection = "General";

        internal Settings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            // The patcher reads this too before any plugin loads, so its name and section must match JustBeBetterPatcher.ReadSettings
            VerboseLogging = config.Bind(
                GeneralSection,
                "Verbose Logging",
                false,
                "Writes what each part does to the log, such as stage load times, first uses and where item stats come from. Turn on for testing or a bug report, it makes the log much longer.");
            Safety = new SafetySettings(config);
            Pickers = new PickerSettings(config);
            ItemInfo = new ItemInfoSettings(config);
            Notifications = new NotificationSettings(config);
            Hidden = new HiddenSettings(config);
            InventorySort = new InventorySortSettings(config);
            SkillInfo = new SkillInfoSettings(config);
            Cooldowns = new CooldownSettings(config);
            Buffs = new BuffInfoSettings(config);
            Stats = new StatsDisplaySettings(config);
            Scoreboard = new ScoreboardSettings(config);
            ImmuneBar = new ImmuneBarSettings(config);
            Chests = new ChestTweaksSettings(config);
            LobbyEnabled = BindEnabled(
                config,
                LobbySection,
                "Fits big rosters in the lobby with a survivor grid that has search and favourites, wraps the "
                + "loadout and rule rows, and keeps artifact votes across restarts.");
            SavesEnabled = BindEnabled(
                config,
                SavesSection,
                "Lets the host save a run and continue it later from the lobby.");
            Cursor = new CursorSettings(config);
            ToggleFire = new ToggleFireSettings(config);
            Loading = new LoadingSettings(config);
            Sounds = new SoundSettings(config);

            // Debug only, so it stays out of Risk Of Options
            RepeatSurvivors = config.Bind(
                "Debug",
                "Repeat Survivors",
                0,
                new ConfigDescription(
                    "Shows every survivor this many extra times in the lobby grid, to try out paging and small icons without a big modded roster. Leave at 0 to play.",
                    new AcceptableValueRange<int>(0, 10)));
        }

        internal ConfigEntry<bool> VerboseLogging { get; }

        internal SafetySettings Safety { get; }

        internal PickerSettings Pickers { get; }

        internal ItemInfoSettings ItemInfo { get; }

        internal NotificationSettings Notifications { get; }

        internal HiddenSettings Hidden { get; }

        internal InventorySortSettings InventorySort { get; }

        internal SkillInfoSettings SkillInfo { get; }

        internal CooldownSettings Cooldowns { get; }

        internal BuffInfoSettings Buffs { get; }

        internal StatsDisplaySettings Stats { get; }

        internal ScoreboardSettings Scoreboard { get; }

        internal ImmuneBarSettings ImmuneBar { get; }

        internal ChestTweaksSettings Chests { get; }

        internal ConfigEntry<bool> LobbyEnabled { get; }

        internal ConfigEntry<bool> SavesEnabled { get; }

        internal CursorSettings Cursor { get; }

        internal ToggleFireSettings ToggleFire { get; }

        internal LoadingSettings Loading { get; }

        internal SoundSettings Sounds { get; }

        internal ConfigEntry<int> RepeatSurvivors { get; }

        internal static ConfigEntry<bool> BindEnabled(ConfigFile config, string section, string description, bool on = true)
        {
            return config.Bind(section, "Enabled", on, description + " Applies from the next launch.");
        }
    }
}
