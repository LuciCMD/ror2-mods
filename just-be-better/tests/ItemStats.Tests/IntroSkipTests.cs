using JustBeBetter.Loading;

// When the intro skip steps in at each step of startup
internal static partial class Program
{
    private static void RunIntroSkip()
    {
        Test("Intro skip: the splash is skipped by the setting or the game's splash_skip", IntroSplashSkipped);
        Test("Intro skip: the splash plays when the fade flag is missing", IntroSplashNeedsFadeFlag);
        Test("Intro skip: the title waits for loading only while the intro is skipped", IntroTitleDeferred);
        Test("Intro skip: the intro preload is skipped only while loading and unused", IntroPreloadSkipped);
        Test("Intro skip: every step leaves the game alone while the old mods are installed", IntroOldModsWin);
    }

    private static void IntroSplashSkipped()
    {
        Expect(IntroRules.SkipSplashScene(true, false, true, false), "Skip Splash on didn't skip");
        Expect(IntroRules.SkipSplashScene(false, true, true, false), "splash_skip on didn't skip");
        Expect(!IntroRules.SkipSplashScene(false, false, true, false), "skipped with both off");
    }

    private static void IntroSplashNeedsFadeFlag()
    {
        Expect(!IntroRules.SkipSplashScene(true, true, false, false), "skipped without the fade flag, so fades would stay off");
    }

    private static void IntroTitleDeferred()
    {
        Expect(IntroRules.DeferTitle(true, true, false), "the title wasn't held while loading");
        Expect(!IntroRules.DeferTitle(true, false, false), "the title was held after loading");
        Expect(!IntroRules.DeferTitle(false, true, false), "the splash's jump to the intro was held");
    }

    private static void IntroPreloadSkipped()
    {
        Expect(IntroRules.SkipIntroPreload(true, true, false, false), "a skipped intro was preloaded");
        Expect(IntroRules.SkipIntroPreload(true, false, true, false), "an intro already loading was preloaded again");
        Expect(!IntroRules.SkipIntroPreload(true, false, false, false), "the preload for an intro that plays after the splash was dropped");
        Expect(!IntroRules.SkipIntroPreload(false, true, true, false), "a preload after loading was dropped");
    }

    private static void IntroOldModsWin()
    {
        Expect(!IntroRules.SkipSplashScene(true, true, true, true), "the splash was skipped with the old mods installed");
        Expect(!IntroRules.DeferTitle(true, true, true), "the title was held with the old mods installed");
        Expect(!IntroRules.SkipIntroPreload(true, true, true, true), "the preload was dropped with the old mods installed");
    }
}
