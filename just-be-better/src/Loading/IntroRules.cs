namespace JustBeBetter.Loading
{
    // When the intro skip steps in, kept apart from the game so it can be tested
    // Each step leaves the game alone while FixIntroSkip or Skip_Intro_Deluxe is still installed
    internal static class IntroRules
    {
        // The game only turns its fade to black on once a scene named splash unloads, so without that flag the
        // splash plays rather than leave every later fade off
        internal static bool SkipSplashScene(bool skipSplash, bool splashCvar, bool fadeFlagFound, bool oldModsLoaded)
            => !oldModsLoaded && fadeFlagFound && (skipSplash || splashCvar);

        // The game jumps to the title as soon as the splash ends, even mid-load
        internal static bool DeferTitle(bool introSkipped, bool isLoading, bool oldModsLoaded)
            => !oldModsLoaded && introSkipped && isLoading;

        // Nothing would use the preloaded intro, and the intro started in place of the splash is already loading
        internal static bool SkipIntroPreload(bool isLoading, bool introSkipped, bool introStarted, bool oldModsLoaded)
            => !oldModsLoaded && isLoading && (introSkipped || introStarted);
    }
}
