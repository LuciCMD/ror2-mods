using System;
using BepInEx.Configuration;

namespace JustLoad
{
    // Both are read once at launch. Bundle Cache belongs to the patcher half, which reads it from this same file
    // before any plugin loads; it's bound here too so Risk Of Options can show it. Keep its text matching the
    // patcher's, since whichever side saves last writes the description
    internal sealed class Settings
    {
        internal Settings(ConfigFile config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            FasterEffectScan = config.Bind(
                "Speed-Ups",
                "Faster Effect Scan",
                true,
                "Looks for screen effects only in mods that can have them, instead of every loaded file. Turn off to compare startup times.");
            BundleCache = config.Bind(
                "Speed-Ups",
                "Bundle Cache",
                true,
                "Keeps a quick-loading copy of mods' slow-to-open asset bundles in BepInEx\\cache\\Just_Load and opens that instead. Takes a few hundred MB of disk.");
        }

        internal ConfigEntry<bool> FasterEffectScan { get; }

        internal ConfigEntry<bool> BundleCache { get; }
    }
}
