using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JustBeBetter.Loading
{
    // Times each scene change from the request to the player being in control, using only the game's events and
    // a check each frame, so play itself isn't slowed
    internal sealed class StageWatcher : MonoBehaviour
    {
        private const double PlayTimeoutSeconds = 60;

        // Losing control this soon before a scene change counts as the start of the load (the teleporter's exit)
        private const double LostControlWindowSeconds = 30;
        private const int MaxMarks = 32;
        private const int TopFreezes = 6;

        private readonly List<Span> marks = new List<Span>();
        private ManualLogSource? log;
        private Func<bool>? isLoadingScene;
        private bool wasLoading;
        private bool wasInPlay;
        private long lostControl;
        private long loadStart;
        private long loadDone;
        private bool expectPlay;
        private string scene = "an unknown scene";
        private string stage = string.Empty;

        internal void Init(ManualLogSource log)
        {
            this.log = log;

            // Private static property, true while UNet's scene change is running
            MethodInfo? getter = typeof(NetworkManagerSystem)
                .GetProperty("isLoadingScene", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetGetMethod(true);
            isLoadingScene = getter != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter) : null;
            if (isLoadingScene == null)
            {
                log.LogWarning("NetworkManagerSystem.isLoadingScene not found; stage loads aren't timed");
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Stage.onStageStartGlobal += OnStageStart;
            SceneDirector.onPrePopulateSceneServer += OnPrePopulate;
            SceneDirector.onPostPopulateSceneServer += OnPostPopulate;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Stage.onStageStartGlobal -= OnStageStart;
            SceneDirector.onPrePopulateSceneServer -= OnPrePopulate;
            SceneDirector.onPostPopulateSceneServer -= OnPostPopulate;
        }

        private void Update()
        {
            if (isLoadingScene == null || log == null)
            {
                return;
            }

            bool loading = isLoadingScene();
            if (loadStart == 0)
            {
                WatchControl();
            }

            if (loading && !wasLoading)
            {
                if (loadStart != 0)
                {
                    Finish("another scene change started before it reached play");
                }

                Begin();
            }
            else if (!loading && wasLoading && loadStart != 0)
            {
                Mark("scene change finished");
                loadDone = Stopwatch.GetTimestamp();
                if (!expectPlay)
                {
                    Finish(null);
                }
            }
            else if (loadDone != 0)
            {
                WaitForPlay();
            }

            wasLoading = loading;
        }

        private void WatchControl()
        {
            bool inPlay = InPlay();
            if (wasInPlay && !inPlay)
            {
                lostControl = Stopwatch.GetTimestamp();
            }

            wasInPlay = inPlay;
        }

        private void WaitForPlay()
        {
            if (InPlay())
            {
                Mark("in control");
                Finish(null);
            }
            else if (Timeline.Seconds(Stopwatch.GetTimestamp() - loadDone) > PlayTimeoutSeconds)
            {
                Finish($"stopped timing; it hadn't reached play {PlayTimeoutSeconds:0} s after the scene change finished");
            }
        }

        private void Begin()
        {
            long now = Stopwatch.GetTimestamp();
            bool fromPlay = lostControl != 0 && Timeline.Seconds(now - lostControl) < LostControlWindowSeconds;
            loadStart = fromPlay ? lostControl : now;
            lostControl = 0;
            wasInPlay = false;
            loadDone = 0;
            expectPlay = false;
            scene = "an unknown scene";
            stage = string.Empty;
            marks.Clear();
            Timeline.Freezes.RemoveAll(f => f.End < loadStart);
            if (fromPlay)
            {
                marks.Add(new Span("lost control", loadStart, loadStart));
            }

            Mark("scene change");
        }

        private void OnSceneLoaded(Scene loaded, LoadSceneMode mode)
        {
            if (loadStart == 0 || mode != LoadSceneMode.Single)
            {
                return;
            }

            SceneDef? def = SceneCatalog.GetSceneDefFromSceneName(loaded.name);
            string title = def != null && !string.IsNullOrEmpty(def.nameToken) ? Language.GetString(def.nameToken) : string.Empty;
            scene = title.Length > 0 && title != def!.nameToken ? $"{title} ({loaded.name})" : loaded.name;
            expectPlay = def != null && def.sceneType is SceneType.Stage or SceneType.Intermission
                or SceneType.TimedIntermission or SceneType.UntimedStage;
            Mark("scene loaded");
        }

        private void OnStageStart(Stage started)
        {
            if (Run.instance)
            {
                stage = $", stage {Run.instance.stageClearCount + 1}";
            }

            Mark("stage started");
        }

        private void OnPrePopulate(SceneDirector director)
        {
            Mark("director starts spawning");
        }

        private void OnPostPopulate(SceneDirector director)
        {
            Mark("director done spawning");
        }

        private void Mark(string label)
        {
            if (loadStart == 0 || marks.Count >= MaxMarks)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            marks.Add(new Span(label, now, now));
            Timeline.Step(label);
        }

        private void Finish(string? problem)
        {
            long end = marks.Count > 0 ? marks.Max(m => m.Start) : Stopwatch.GetTimestamp();
            if (Logs.VerboseOn)
            {
                Report(end, problem);
            }

            Timeline.Freezes.RemoveAll(f => f.End <= end);
            Timeline.Step("playing");
            loadStart = 0;
            loadDone = 0;
            marks.Clear();
        }

        private void Report(long end, string? problem)
        {
            ManualLogSource log = this.log!;
            string outcome = problem ?? (expectPlay ? "to being in control" : "to the scene being ready");
            string from = marks.Count > 0 && marks[0].Label == "lost control" ? "losing control" : "the scene change";
            log.Verbose($"[Stage] {scene}{stage}: {Timeline.Seconds(end - loadStart):0.0} s from {from} {outcome}");
            if (problem != null)
            {
                log.Verbose($"[Stage]   {problem}");
            }

            var ordered = marks.OrderBy(m => m.Start).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                log.Verbose($"[Stage]   {Timeline.Seconds(ordered[i].Start - ordered[i - 1].Start),6:0.00} s  {ordered[i - 1].Label} → {ordered[i].Label}");
            }

            var freezes = Timeline.Freezes.Where(f => f.End >= loadStart && f.Start <= end).ToList();
            log.Verbose($"[Stage]   Freezes: {freezes.Count}, {freezes.Sum(f => f.Seconds):0.0} s in total. Longest:");
            foreach (Span freeze in freezes.OrderByDescending(f => f.Seconds).Take(TopFreezes))
            {
                log.Verbose($"[Stage]     {freeze.Seconds,6:0.00} s  {freeze.Label}");
            }
        }

        // In control once a local player's camera follows a body, by which time the old scene's rigs are gone
        private static bool InPlay()
        {
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig && rig.localUserViewer != null && rig.target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
