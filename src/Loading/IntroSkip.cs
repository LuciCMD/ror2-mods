using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.ConVar;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace JustBeBetter.Loading
{
    // Skips the logo splash and the intro cutscene, as Skip_Intro_Deluxe with FixIntroSkip did
    // Since Seekers of the Storm both play during loading, so the jump to the title waits for loading to finish
    internal sealed class IntroSkip
    {
        // Addressable scene keys from RoR2Application.LoadGameContentAsync and SplashScreenController.Finish
        private const string SplashScene = "fe13e00695117284fb0d1b25422a0529";
        private const string IntroScene = "330d792ae1727574e969e68ce8e966d2";
        private const string TitleScene = "dafb193bd5aedc74abd11b22f8dce1c2";
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly string[] OldMods = { "groovesalad.FixIntroSkip", "Hibiscus.SkipIntroDeluxe" };

        private readonly LoadingSettings settings;
        private readonly ManualLogSource log;
        private FieldInfo? fadeFlag;
        private BoolConVar? splashCvar;
        private bool? oldModsLoaded;
        private bool introStarted;
        private bool titleQueued;

        internal IntroSkip(LoadingSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            fadeFlag = typeof(FadeToBlackManager).GetField("haveLoadedSplash", PrivateStatic);
            if (fadeFlag == null && settings.SkipSplash.Value)
            {
                log.LogWarning("Couldn't find the game's fade flag, so the splash plays");
            }

            splashCvar = typeof(SplashScreenController).GetField("cvSplashSkip", PrivateStatic)?.GetValue(null) as BoolConVar;
            if (splashCvar == null)
            {
                log.LogWarning("Couldn't read the game's splash_skip, so only Skip Splash counts");
            }

            On.LoadHelper.LoadSceneAdditivelyAsync += OnLoadScene;
            On.LoadHelper.StartLoadSceneAdditiveAsync += OnPreloadScene;
            On.RoR2.SplashScreenController.Finish += OnSplashFinish;
        }

        internal void Disable()
        {
            On.LoadHelper.LoadSceneAdditivelyAsync -= OnLoadScene;
            On.LoadHelper.StartLoadSceneAdditiveAsync -= OnPreloadScene;
            On.RoR2.SplashScreenController.Finish -= OnSplashFinish;
        }

        // Checked once the splash is due, when every plugin has loaded, so each hook calls this before reading
        // shouldSkip
        private bool OldModsLoaded()
        {
            if (oldModsLoaded == null)
            {
                oldModsLoaded = OldMods.Any(Chainloader.PluginInfos.ContainsKey);
                if (oldModsLoaded.Value)
                {
                    log.LogWarning("FixIntroSkip or Skip_Intro_Deluxe is installed, so the splash and intro are left to them. Uninstall both to use Skip Splash and Skip Intro");
                }
                else if (settings.SkipIntro.Value)
                {
                    // Read by the game's own splash, intro and title code too, as intro_skip is
                    IntroCutsceneController.shouldSkip = true;
                }
            }

            return oldModsLoaded.Value;
        }

        private IEnumerator OnLoadScene(On.LoadHelper.orig_LoadSceneAdditivelyAsync orig, string sceneGuid)
        {
            try
            {
                bool oldMods = OldModsLoaded();
                if (sceneGuid == SplashScene
                    && IntroRules.SkipSplashScene(settings.SkipSplash.Value, splashCvar?.value == true, fadeFlag != null, oldMods))
                {
                    SkipSplash(orig);
                    return Array.Empty<object>().GetEnumerator();
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't skip the splash, so it plays: {e}");
            }

            return orig(sceneGuid);
        }

        // What the splash would do once it ends, without loading it
        private void SkipSplash(On.LoadHelper.orig_LoadSceneAdditivelyAsync orig)
        {
            if (IntroCutsceneController.shouldSkip)
            {
                QueueTitle();
                log.Verbose("Skipped the splash and intro, the title shows once loading finishes");
            }
            else
            {
                // The intro's own controller takes it to the title once it ends
                RoR2Application.instance.StartCoroutine(orig(IntroScene));
                introStarted = true;
                MarkSplashSeen();
                log.Verbose("Skipped the splash, the intro plays");
            }

            StartMusic();
        }

        // What the splash scene's TriggerIntroMusicStart does, and the only thing that turns the music on
        // Last and on its own, so a throw here can't bring the splash back on top of a skip already made
        private void StartMusic()
        {
            try
            {
                if (MusicController.Instance)
                {
                    MusicController.Instance.StartIntroMusic();
                }
                else
                {
                    log.LogWarning("No music controller yet, so music stays off this session");
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't start the music, so it stays off this session: {e}");
            }
        }

        private AsyncOperationHandle OnPreloadScene(On.LoadHelper.orig_StartLoadSceneAdditiveAsync orig, string sceneGuid)
        {
            bool oldMods = OldModsLoaded();
            if (sceneGuid == IntroScene
                && IntroRules.SkipIntroPreload(RoR2Application.isLoading, IntroCutsceneController.shouldSkip, introStarted, oldMods))
            {
                log.Verbose("Intro preload skipped");

                // The game only yields it, and an empty handle counts as done
                return default;
            }

            return orig(sceneGuid);
        }

        private void OnSplashFinish(On.RoR2.SplashScreenController.orig_Finish orig, SplashScreenController self)
        {
            bool oldMods = OldModsLoaded();

            // The splash's own end can land after loading, while the queued title is already on its way
            if (titleQueued)
            {
                log.Verbose("Splash done, the title is already queued");
                return;
            }

            if (IntroRules.DeferTitle(IntroCutsceneController.shouldSkip, RoR2Application.isLoading, oldMods))
            {
                log.Verbose("Splash done before loading, the title shows once loading finishes");
                QueueTitle();
                return;
            }

            orig(self);
        }

        private void QueueTitle()
        {
            if (titleQueued)
            {
                return;
            }

            titleQueued = true;
            RoR2Application.onLoad += GoToTitle;
        }

        // Runs inside the game's load, where a throw would stop loading for good
        private void GoToTitle()
        {
            try
            {
                MarkSplashSeen();
                PlatformSystems.networkManager.ServerChangeScene(TitleScene);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't open the title, trying the console: {e}");
                try
                {
                    RoR2.Console.instance.SubmitCmd(null, "set_scene title");
                }
                catch (Exception e2)
                {
                    log.LogError($"Couldn't open the title from the console either: {e2}");
                }
            }
        }

        // The splash scene's unload sets this, and the next unload fades in from black as it would after the splash
        private void MarkSplashSeen()
        {
            fadeFlag?.SetValue(null, true);
        }
    }
}
