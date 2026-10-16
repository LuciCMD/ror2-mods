using System;
using BepInEx.Configuration;

namespace JustBeBetter.Loading
{
    // All read once at launch
    // The patcher also reads Enabled and Bundle Cache from this file before any plugin loads, so their names
    // and section must match JustBeBetterPatcher.ReadSettings
    internal sealed class LoadingSettings
    {
        internal const string Section = "Loading";

        internal LoadingSettings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Enabled = Settings.BindEnabled(
                config,
                Section,
                "Speeds up startup as set below and writes how long startup took to the log, with a breakdown and each stage's load time when Verbose Logging is on.");
            FasterEffectScan = config.Bind(
                Section,
                "Faster Effect Scan",
                true,
                "Looks for screen effects only in mods that can have them, instead of every loaded file. Turn off to compare startup times.");
            BundleCache = config.Bind(
                Section,
                "Bundle Cache",
                true,
                "Keeps a quick-loading copy of mods' slow-to-open asset bundles in BepInEx\\cache\\Just_Be_Better and opens that instead. Takes a few hundred MB of disk.");
            SkipSplash = config.Bind(
                Section,
                "Skip Splash",
                true,
                "Leaves out the logo screens at startup, so the loading screen stays up until the intro or title.");
            SkipIntro = config.Bind(
                Section,
                "Skip Intro",
                true,
                "Leaves out the opening cutscene and goes to the title once loading finishes.");
        }

        internal ConfigEntry<bool> Enabled { get; }

        internal ConfigEntry<bool> FasterEffectScan { get; }

        internal ConfigEntry<bool> BundleCache { get; }

        internal ConfigEntry<bool> SkipSplash { get; }

        internal ConfigEntry<bool> SkipIntro { get; }
    }
}
