using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using JustBeBetter.Loading;
using RoR2;
using RoR2.ConVar;
using UnityEngine.ResourceManagement.AsyncOperations;

// Intro Skip at its edges
// IntroSkip needs the game's startup to run, so the reflection tests check every member and scene key it relies on in
// the installed game, and a tick model of RoR2Application.InitializeGameRoutine, the splash, the intro and the fade
// manager runs IntroSkip's hooks as written, with FixIntroSkip and Skip_Intro_Deluxe mirrored from their decompile
// A change to IntroSkip has to be mirrored in IntroSim
internal static partial class Program
{
    private static void RunIntroSkipEdge()
    {
        Test("Intro skip edge: the fade flag, splash_skip and shouldSkip exist with the types IntroSkip expects", IntroEdgeReflected);
        Test("Intro skip edge: the scene keys in IntroSkip are the ones the game loads at each step", IntroEdgeSceneKeys);
        Test("Intro skip edge: the fade manager still keys on a scene named splash", IntroEdgeFadeKeysOnSplash);
        Test("Intro skip edge: the splash's music trigger is the StartIntroMusic call IntroSkip makes", IntroEdgeMusicTrigger);
        Test("Intro skip edge: an empty preload handle counts as done when yielded", IntroEdgeEmptyHandleDone);
        Test("Intro skip edge: every setting, cvar and timing gives one title, after loading, with fades on", IntroEdgeMatrix);
        Test("Intro skip edge: a skipped splash keeps the loading screen up until the title", IntroEdgeLoadingScreenStays);
        Test("Intro skip edge: a splash Finish landing while the deferred title loads doesn't open it twice", IntroEdgeLateSplashFinish);
        Test("Intro skip edge: a throw while starting the intro leaves the splash to play and still reaches the title", IntroEdgeThrowStartingIntro);
        Test("Intro skip edge: with the old mods installed, startup runs as it would without Just Be Better", IntroEdgeOldModsSameAsWithout);
    }

    private const BindingFlags IntroStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static void IntroEdgeReflected()
    {
        FieldInfo? fade = typeof(FadeToBlackManager).GetField("haveLoadedSplash", IntroStatic);
        Expect(fade != null, "FadeToBlackManager.haveLoadedSplash, private static");
        Expect(fade!.FieldType == typeof(bool), $"haveLoadedSplash is {fade.FieldType}, SetValue(null, true) would throw");

        FieldInfo? cvar = typeof(SplashScreenController).GetField("cvSplashSkip", IntroStatic);
        Expect(cvar != null, "SplashScreenController.cvSplashSkip, private static");
        Expect(cvar!.GetValue(null) is BoolConVar, "cvSplashSkip isn't a BoolConVar once the class is set up");

        PropertyInfo? skip = typeof(IntroCutsceneController).GetProperty("shouldSkip", BindingFlags.Static | BindingFlags.Public);
        Expect(skip?.SetMethod != null && skip.GetMethod != null, "IntroCutsceneController.shouldSkip, public static get and set");

        FieldInfo? onLoad = typeof(RoR2Application).GetField("onLoad", BindingFlags.Static | BindingFlags.Public);
        Expect(onLoad?.FieldType == typeof(Action), "RoR2Application.onLoad, a public static Action field");

        MethodInfo? load = typeof(LoadHelper).GetMethod("LoadSceneAdditivelyAsync", new[] { typeof(string) });
        Expect(load?.ReturnType == typeof(IEnumerator), "LoadHelper.LoadSceneAdditivelyAsync(string) returning IEnumerator");
        MethodInfo? preload = typeof(LoadHelper).GetMethod("StartLoadSceneAdditiveAsync", new[] { typeof(string) });
        Expect(preload?.ReturnType == typeof(AsyncOperationHandle), "LoadHelper.StartLoadSceneAdditiveAsync(string) returning AsyncOperationHandle");
    }

    private static Dictionary<string, string> IntroSkipConstants()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        string text = File.ReadAllText(System.IO.Path.Combine(root, "Loading", "IntroSkip.cs"));
        return Regex.Matches(text, @"const string (\w+) = ""([0-9a-f]+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
    }

    // Each ldstr and call in a method body, in IL order, a plain byte scan so a stray match only adds noise
    private static List<(int At, object What)> IntroIl(MethodBase method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        Module module = method.Module;
        var found = new List<(int, object)>();
        for (int i = 0; i + 4 < il.Length; i++)
        {
            int token = BitConverter.ToInt32(il, i + 1);
            int table = (int)((uint)token >> 24);
            try
            {
                if (il[i] == 0x72 && table == 0x70)
                {
                    found.Add((i, module.ResolveString(token)));
                }
                else if ((il[i] == 0x28 || il[i] == 0x6F) && (table == 0x06 || table == 0x0A || table == 0x2B))
                {
                    found.Add((i, module.ResolveMethod(token)!));
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return found;
    }

    // The scene key is loaded through the named method, within a few instructions of it
    private static int IntroKeyFeeds(List<(int At, object What)> il, string key, string methodName)
    {
        foreach ((int at, object what) in il)
        {
            if (what is string s && s == key
                && il.Any(x => x.At > at && x.At - at < 40 && x.What is MethodBase m && m.Name == methodName))
            {
                return at;
            }
        }

        return -1;
    }

    private static void IntroEdgeSceneKeys()
    {
        Dictionary<string, string> keys = IntroSkipConstants();
        Expect(keys.ContainsKey("SplashScene") && keys.ContainsKey("IntroScene") && keys.ContainsKey("TitleScene"),
            "couldn't read SplashScene, IntroScene and TitleScene from IntroSkip.cs");

        Type routine = typeof(RoR2Application).GetNestedTypes(BindingFlags.NonPublic)
            .First(t => t.Name.Contains("InitializeGameRoutine"));
        MethodInfo moveNext = routine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        List<(int, object)> startup = IntroIl(moveNext);
        int splash = IntroKeyFeeds(startup, keys["SplashScene"], "LoadSceneAdditivelyAsync");
        int intro = IntroKeyFeeds(startup, keys["IntroScene"], "StartLoadSceneAdditiveAsync");
        Expect(splash >= 0, "startup doesn't load SplashScene through LoadSceneAdditivelyAsync");
        Expect(intro >= 0, "startup doesn't preload IntroScene through StartLoadSceneAdditiveAsync");
        Expect(splash < intro, "startup preloads the intro before the splash, so introStarted is set too late");

        List<(int, object)> finish = IntroIl(typeof(SplashScreenController).GetMethod("Finish")!);
        Expect(IntroKeyFeeds(finish, keys["TitleScene"], "ServerChangeScene") >= 0, "the splash doesn't open TitleScene through ServerChangeScene");
        Expect(IntroKeyFeeds(finish, keys["IntroScene"], "LoadSceneAdditivelyAsync") >= 0, "the splash doesn't load IntroScene");
    }

    private static void IntroEdgeFadeKeysOnSplash()
    {
        MethodInfo unloaded = typeof(FadeToBlackManager).GetMethod("OnSceneUnloaded", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        Expect(IntroIl(unloaded).Any(x => x.What is string s && s == "splash"), "OnSceneUnloaded no longer compares against splash");
        Expect(IntroIl(unloaded).Any(x => x.What is MethodBase m && m.Name == "ForceFullBlack"), "OnSceneUnloaded no longer forces black");
    }

    private static void IntroEdgeMusicTrigger()
    {
        MethodInfo onEnable = typeof(TriggerIntroMusicStart).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<(int At, object What)> il = IntroIl(onEnable);
        string calls = string.Join(", ", il.Select(x => x.What is MethodBase m ? m.Name : "\"" + x.What + "\""));
        Expect(calls == "get_Instance, StartIntroMusic", $"TriggerIntroMusicStart.OnEnable now does {calls}");
    }

    // FixIntroSkip did the same, and the game yields the handle straight from its startup coroutine
    private static void IntroEdgeEmptyHandleDone()
    {
        AsyncOperationHandle handle = default;
        Expect(!handle.IsValid(), "an empty handle reads valid");
        Expect(handle.IsDone, "an empty handle reads not done, the startup coroutine would wait on it");
        IEnumerator boxed = handle;
        Expect(!boxed.MoveNext(), "an empty handle asks for another frame, Unity would keep waiting");
    }

    private static readonly int[] IntroSplashLengths = { 6, 60 };
    private static readonly int[] IntroIntroLengths = { 5, 80 };

    private static IEnumerable<IntroSim> IntroAllRuns(bool fix, bool deluxe, bool ours)
    {
        foreach (int bits in Enumerable.Range(0, 32))
        {
            foreach (int splashLength in IntroSplashLengths)
            {
                foreach (int introLength in IntroIntroLengths)
                {
                    yield return new IntroSim
                    {
                        SkipSplash = (bits & 1) != 0,
                        SkipIntro = (bits & 2) != 0,
                        SplashCvar = (bits & 4) != 0,
                        IntroCvar = (bits & 8) != 0,
                        FadeFlagFound = (bits & 16) != 0,
                        SplashLength = splashLength,
                        IntroLength = introLength,
                        Fix = fix,
                        Deluxe = deluxe,
                        Ours = ours,
                    };
                }
            }
        }
    }

    private static void IntroEdgeMatrix()
    {
        var problems = new List<string>();
        foreach (IntroSim sim in IntroAllRuns(false, false, true))
        {
            sim.Run();
            bool introSkipped = sim.SkipIntro || sim.IntroCvar;
            bool splashSkipped = sim.FadeFlagFound && (sim.SkipSplash || sim.SplashCvar);
            var wrong = new List<string>();
            if (sim.TitleRequests != 1)
            {
                wrong.Add($"title opened {sim.TitleRequests} times");
            }
            if (sim.EarlyTitle)
            {
                wrong.Add("title opened mid-load");
            }
            if (!sim.Activated.Contains("title"))
            {
                wrong.Add("never reached the title");
            }
            if (sim.Activated.Contains("splash") == splashSkipped)
            {
                wrong.Add(splashSkipped ? "splash loaded" : "splash missing");
            }
            int intros = sim.Activated.Count(s => s == "intro");
            if (intros != (introSkipped ? 0 : 1))
            {
                wrong.Add($"intro played {intros} times");
            }
            if (sim.MusicStarts != 1)
            {
                wrong.Add($"intro music started {sim.MusicStarts} times");
            }
            foreach (string scene in new[] { "title", "intro" })
            {
                if (sim.BlackOnArrival.TryGetValue(scene, out bool black) && !black)
                {
                    wrong.Add($"{scene} arrived with fades still off");
                }
            }
            if (wrong.Count > 0)
            {
                problems.Add(sim + ": " + string.Join(", ", wrong));
            }
        }

        Expect(problems.Count == 0, $"{problems.Count} runs went wrong, first: {problems.FirstOrDefault()}");
    }

    private static void IntroEdgeLoadingScreenStays()
    {
        var sim = new IntroSim { SkipSplash = true, SkipIntro = true, FadeFlagFound = true, Ours = true, SplashLength = 6, IntroLength = 5 };
        sim.Run();
        Expect(sim.Activated.SequenceEqual(new[] { "title" }), "scenes shown before the title: " + string.Join(", ", sim.Activated));
        Expect(sim.Log.Any(l => l.EndsWith("unload loadingbasic")), "loadingbasic wasn't the scene the title replaced");
    }

    // The splash's Start calls Finish under splash_skip and its timeline calls it again, the second one can land
    // between onLoad and the title arriving when the splash plays because the fade flag is missing
    private static void IntroEdgeLateSplashFinish()
    {
        var problems = new List<string>();
        foreach (bool cvar in new[] { false, true })
        {
            for (int length = 1; length < 40; length++)
            {
                var sim = new IntroSim
                {
                    SkipSplash = true,
                    SkipIntro = true,
                    SplashCvar = cvar,
                    FadeFlagFound = false,
                    Ours = true,
                    SplashLength = length,
                    IntroLength = 5,
                };
                sim.Run();
                if (sim.TitleRequests != 1 || sim.EarlyTitle)
                {
                    problems.Add($"{sim}: title opened {sim.TitleRequests} times{(sim.EarlyTitle ? ", mid-load" : "")}");
                }
            }
        }

        Expect(problems.Count == 0, $"{problems.Count} runs went wrong, first: {problems.FirstOrDefault()}",
            string.Join("\n", problems.Select(p => "     " + p)));
    }

    private static void IntroEdgeThrowStartingIntro()
    {
        var sim = new IntroSim
        {
            SkipSplash = true,
            SkipIntro = false,
            FadeFlagFound = true,
            Ours = true,
            SplashLength = 6,
            IntroLength = 5,
            ThrowStartingIntro = true,
        };
        sim.Run();
        Expect(sim.Activated.Contains("splash"), "the splash didn't play after the throw");
        Expect(sim.Activated.Count(s => s == "intro") == 1, $"intro played {sim.Activated.Count(s => s == "intro")} times");
        Expect(sim.TitleRequests == 1 && !sim.EarlyTitle, $"title opened {sim.TitleRequests} times");
        Expect(sim.MusicStarts >= 1, "no intro music");
    }

    private static void IntroEdgeOldModsSameAsWithout()
    {
        var problems = new List<string>();
        foreach ((bool fix, bool deluxe) in new[] { (true, false), (false, true), (true, true) })
        {
            List<IntroSim> with = IntroAllRuns(fix, deluxe, true).ToList();
            List<IntroSim> without = IntroAllRuns(fix, deluxe, false).ToList();
            for (int i = 0; i < with.Count; i++)
            {
                with[i].Run();
                without[i].Run();
                if (!with[i].Log.SequenceEqual(without[i].Log))
                {
                    int at = with[i].Log.Zip(without[i].Log).TakeWhile(p => p.First == p.Second).Count();
                    problems.Add($"{with[i]}: with ours [{with[i].Log.ElementAtOrDefault(at)}] without [{without[i].Log.ElementAtOrDefault(at)}]");
                }
            }
        }

        Expect(problems.Count == 0, $"{problems.Count} runs differ, first: {problems.FirstOrDefault()}",
            string.Join("\n", problems.Where(p => p.Contains("Length=6/5")).Take(8).Select(p => "     " + p)));
    }

    private static void Expect(bool condition, string what, string detail)
    {
        if (!condition)
        {
            throw new AssertFail(what, detail);
        }
    }

    // One startup in ticks, the game's side follows the 1.5.0 decompile and IntroSkip's side follows IntroSkip.cs
    private sealed class IntroSim
    {
        private const string Splash = "splash";
        private const string Intro = "intro";
        private const string Title = "title";
        private const int SceneLoadTicks = 2;
        private const int SplashLoadAt = 5;
        private const int ContentDoneAt = 30;

        internal bool SkipSplash, SkipIntro, SplashCvar, IntroCvar, FadeFlagFound, Fix, Deluxe, Ours, ThrowStartingIntro;
        internal int SplashLength, IntroLength;

        internal readonly List<string> Log = new();
        internal readonly List<string> Activated = new();
        internal readonly Dictionary<string, bool> BlackOnArrival = new();
        internal int TitleRequests, MusicStarts;
        internal bool EarlyTitle;

        // Game state
        private int now;
        private readonly List<(int At, int Seq, Action Act)> queue = new();
        private int seq;
        private bool isLoading = true, loadFinished, inOnLoad;
        private Action? onLoad;
        private bool haveLoadedSplash;
        private string active = "loadingbasic";
        private int activeId;
        private bool shouldSkipField;
        private bool introSkipRequested;
        private readonly HashSet<string> preloads = new();

        // IntroSkip state
        private bool? oldModsLoaded;
        private bool introStarted, titleQueued;

        // Hook chains, outermost last added as MonoMod does
        private Func<string, Op> loadScene = null!;
        private Func<string, Op> preload = null!;
        private Action<int> splashFinish = null!;

        private bool ShouldSkip => shouldSkipField || IntroCvar;

        private sealed class Op
        {
            internal bool Done;
        }

        public override string ToString()
            => $"SkipSplash={SkipSplash} SkipIntro={SkipIntro} splash_skip={SplashCvar} intro_skip={IntroCvar} fadeFlag={FadeFlagFound} "
               + $"Fix={Fix} Deluxe={Deluxe} Length={SplashLength}/{IntroLength}";

        internal void Run()
        {
            loadScene = VanillaLoadScene;
            preload = VanillaPreload;
            splashFinish = VanillaSplashFinish;

            // Plugin Awake in load order, FixIntroSkip and Skip_Intro_Deluxe sort before Just_Be_Better
            if (Fix)
            {
                Func<string, Op> l = loadScene;
                loadScene = g => FixLoadScene(l, g);
                Func<string, Op> p = preload;
                preload = g => FixPreload(p, g);
                Action<int> f = splashFinish;
                splashFinish = id => FixSplashFinish(f, id);
            }
            if (Deluxe)
            {
                shouldSkipField = true;
            }
            if (Ours)
            {
                Func<string, Op> l = loadScene;
                loadScene = g => OursLoadScene(l, g);
                Func<string, Op> p = preload;
                preload = g => OursPreload(p, g);
                Action<int> f = splashFinish;
                splashFinish = id => OursSplashFinish(f, id);
            }

            At(SplashLoadAt, MainSplash);
            for (int guard = 0; queue.Count > 0 && guard < 10000; guard++)
            {
                (int At, int Seq, Action Act) next = queue.OrderBy(q => q.At).ThenBy(q => q.Seq).First();
                queue.Remove(next);
                now = next.At;
                next.Act();
            }
        }

        private void At(int tick, Action act) => queue.Add((tick, seq++, act));

        private void Note(string what) => Log.Add($"{now} {what}");

        // RoR2Application.InitializeGameRoutine from the splash on
        private void MainSplash()
        {
            Op splash = loadScene(Splash);
            void WaitSplash()
            {
                if (!splash.Done)
                {
                    At(now + 1, WaitSplash);
                    return;
                }

                // PreIntro then the intro preload, then the rest of the content
                preload(Intro);
                At(Math.Max(ContentDoneAt, now + 1), OnLoadEvent);
            }
            WaitSplash();
        }

        private void OnLoadEvent()
        {
            Note("onLoad");
            Action? snapshot = onLoad;
            inOnLoad = true;
            snapshot?.Invoke();
            inOnLoad = false;
            onLoad = null;
            isLoading = false;
            loadFinished = true;
        }

        // LoadHelper.LoadSceneAdditivelyAsync, the new scene goes active and the old one unloads
        private Op VanillaLoadScene(string scene)
        {
            var op = new Op();
            bool preloaded = preloads.Remove(scene);
            Note("load " + scene + (preloaded ? " preloaded" : ""));
            At(now + (preloaded ? 0 : SceneLoadTicks), () =>
            {
                Activate(scene);
                op.Done = true;
            });
            return op;
        }

        private Op VanillaPreload(string scene)
        {
            Note("preload " + scene);
            preloads.Add(scene);
            return new Op { Done = true };
        }

        private void ServerChangeScene(string scene)
        {
            TitleRequests++;
            if (isLoading && !inOnLoad)
            {
                EarlyTitle = true;
            }
            Note("change to " + scene);
            At(now + SceneLoadTicks, () => Activate(scene));
        }

        private void Activate(string scene)
        {
            string old = active;
            active = scene;
            int id = ++activeId;
            Activated.Add(scene);
            Note("activate " + scene);

            // FadeToBlackManager.OnSceneUnloaded
            Note("unload " + old);
            haveLoadedSplash = haveLoadedSplash || old == Splash;
            BlackOnArrival[scene] = haveLoadedSplash;

            if (scene == Splash)
            {
                // TriggerIntroMusicStart.OnEnable, then SplashScreenController.Start and its timeline's end
                MusicStarts++;
                Note("music");
                At(now, () =>
                {
                    if (SplashCvar && activeId == id)
                    {
                        splashFinish(id);
                    }
                });
                At(now + SplashLength, () =>
                {
                    if (activeId == id)
                    {
                        splashFinish(id);
                    }
                });
            }
            else if (scene == Intro)
            {
                At(now + IntroLength, () =>
                {
                    if (activeId == id)
                    {
                        IntroFinish();
                    }
                });
            }
        }

        private void VanillaSplashFinish(int id)
        {
            Note("splash finish");
            if (ShouldSkip)
            {
                ServerChangeScene(Title);
            }
            else
            {
                loadScene(Intro);
            }
        }

        // IntroCutsceneController.Finish, a TransitionToTitle added during onLoad is lost as the game clears onLoad after
        private void IntroFinish()
        {
            Note("intro finish");
            if (introSkipRequested)
            {
                return;
            }
            if (loadFinished)
            {
                TransitionToTitle();
            }
            else
            {
                onLoad += TransitionToTitle;
            }
        }

        private void TransitionToTitle()
        {
            ServerChangeScene(Title);
            introSkipRequested = true;
        }

        // FixIntroSkip 1.0.0
        private Op FixLoadScene(Func<string, Op> orig, string scene)
        {
            if (scene == Splash && SplashCvar)
            {
                // A fake splash, OnEnable starts the music, then Finish now and again from Start next frame
                MusicStarts++;
                Note("fix fake splash");
                splashFinish(-1);
                At(now + 1, () => splashFinish(-1));
                return new Op { Done = true };
            }
            return orig(scene);
        }

        private Op FixPreload(Func<string, Op> orig, string scene)
            => scene == Intro && isLoading ? new Op { Done = true } : orig(scene);

        private void FixSplashFinish(Action<int> orig, int id)
        {
            if (ShouldSkip && isLoading)
            {
                onLoad += () => ServerChangeScene(Title);
            }
            else
            {
                orig(id);
            }
        }

        // IntroSkip as written
        private bool OldModsLoaded()
        {
            if (oldModsLoaded == null)
            {
                oldModsLoaded = Fix || Deluxe;
                if (!oldModsLoaded.Value && SkipIntro)
                {
                    shouldSkipField = true;
                }
            }
            return oldModsLoaded.Value;
        }

        private Op OursLoadScene(Func<string, Op> orig, string scene)
        {
            try
            {
                bool oldMods = OldModsLoaded();
                if (scene == Splash && IntroRules.SkipSplashScene(SkipSplash, SplashCvar, FadeFlagFound, oldMods))
                {
                    OursSkipSplash(orig);
                    return new Op { Done = true };
                }
            }
            catch (InvalidOperationException)
            {
                Note("skip failed, splash plays");
            }

            return orig(scene);
        }

        private void OursSkipSplash(Func<string, Op> orig)
        {
            if (ShouldSkip)
            {
                QueueTitle();
            }
            else
            {
                if (ThrowStartingIntro)
                {
                    throw new InvalidOperationException("StartCoroutine");
                }
                orig(Intro);
                introStarted = true;
                MarkSplashSeen();
            }

            MusicStarts++;
            Note("music");
        }

        private Op OursPreload(Func<string, Op> orig, string scene)
        {
            bool oldMods = OldModsLoaded();
            if (scene == Intro && IntroRules.SkipIntroPreload(isLoading, ShouldSkip, introStarted, oldMods))
            {
                Note("preload skipped");
                return new Op { Done = true };
            }
            return orig(scene);
        }

        private void OursSplashFinish(Action<int> orig, int id)
        {
            bool oldMods = OldModsLoaded();
            if (titleQueued)
            {
                Note("splash finish, title already queued");
                return;
            }
            if (IntroRules.DeferTitle(ShouldSkip, isLoading, oldMods))
            {
                Note("splash finish deferred");
                QueueTitle();
                return;
            }
            orig(id);
        }

        private void QueueTitle()
        {
            if (titleQueued)
            {
                return;
            }
            titleQueued = true;
            onLoad += GoToTitle;
        }

        private void GoToTitle()
        {
            MarkSplashSeen();
            ServerChangeScene(Title);
        }

        private void MarkSplashSeen()
        {
            if (FadeFlagFound)
            {
                haveLoadedSplash = true;
            }
        }
    }
}
