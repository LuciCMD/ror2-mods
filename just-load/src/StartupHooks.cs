using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HG.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace JustLoad
{
    // Times the game's own startup: its milestone markers, each system initializer, each startup behaviour and
    // the attribute scan; asset bundles are timed by the patcher. All removed once the main menu shows
    internal sealed class StartupHooks
    {
        private readonly ManualLogSource log;
        private readonly List<IDisposable> hooks = new List<IDisposable>();
        private readonly Action onMainMenu;
        private bool initializersHooked;

        internal StartupHooks(ManualLogSource log, Action onMainMenu)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.onMainMenu = onMainMenu ?? throw new ArgumentNullException(nameof(onMainMenu));
        }

        internal void Enable()
        {
            On.RoR2.RoR2Application.PrintSW += OnPrintSW;
            On.RoR2.UI.MainMenu.BaseMainMenuScreen.OnEnter += OnMainMenuEnter;
            Try("attribute scan", () => hooks.Add(new Hook(
                typeof(SearchableAttribute).GetMethod(nameof(SearchableAttribute.TryInitialize), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                new Action<Action>(TimedAttributeScan))));
            Try("game print markers", HookPrint);
            Try("startup behaviours", () => hooks.Add(new ILHook(
                typeof(RoR2Application).GetMethod("EnableBehaviours", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                RouteEnable)));
        }

        internal void Disable()
        {
            On.RoR2.RoR2Application.PrintSW -= OnPrintSW;
            On.RoR2.UI.MainMenu.BaseMainMenuScreen.OnEnter -= OnMainMenuEnter;
            foreach (IDisposable hook in hooks)
            {
                hook.Dispose();
            }

            hooks.Clear();
        }

        private void Try(string what, Action install)
        {
            try
            {
                install();
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't time {what}; the report leaves it out: {e}");
            }
        }

        // The shipped PrintSW is empty, but every call to it is still in place with the developers' labels
        private void OnPrintSW(On.RoR2.RoR2Application.orig_PrintSW orig, string message)
        {
            Timeline.Milestone(message);

            // The first marker comes after every plugin has loaded, so runners swapped in by mods are there by now
            if (!initializersHooked)
            {
                initializersHooked = true;
                Try("system initializers", HookInitializers);
            }

            orig(message);
        }

        private void OnMainMenuEnter(
            On.RoR2.UI.MainMenu.BaseMainMenuScreen.orig_OnEnter orig,
            RoR2.UI.MainMenu.BaseMainMenuScreen self,
            RoR2.UI.MainMenu.MainMenuController mainMenuController)
        {
            orig(self, mainMenuController);
            Timeline.Milestone("Main menu");
            onMainMenu();
        }

        // RoR2Application.Print is empty like PrintSW, but its calls carry timings such as the content load's
        private void HookPrint()
        {
            hooks.Add(new Hook(
                typeof(RoR2Application).GetMethod(nameof(RoR2Application.Print), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(object[]) }, null),
                new Action<Action<string, object[]>, string, object[]>((orig, format, arguments) =>
                {
                    Timeline.Milestone(SafeFormat(format, arguments));
                    orig(format, arguments);
                })));
        }

        private static string SafeFormat(string format, object[] arguments)
        {
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, arguments);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private static void TimedAttributeScan(Action orig)
        {
            long start = Stopwatch.GetTimestamp();
            orig();
            Timeline.Systems.Add(new Span("Attribute scan (SearchableAttribute)", start, Stopwatch.GetTimestamp()));
        }

        // Every runner calls each initializer through MethodBase.Invoke; timing that one call times them all.
        // RoR2BepInExPack replaces the game's coroutine runner with its own copy, so that one is timed too
        private void HookInitializers()
        {
            Type runner = typeof(SystemInitializerAttribute);
            Type? packFix = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "RoR2BepInExPack")?
                .GetType("RoR2BepInExPack.VanillaFixes.FixSystemInitializer");
            var methods = new List<MethodBase?>
            {
                runner.GetMethod("Execute", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                StateMachine(runner, "<ExecuteCoroutine>"),
                packFix != null ? StateMachine(packFix, "<LogBadCycle>") : null,
            };
            foreach (MethodBase? method in methods)
            {
                if (method != null)
                {
                    hooks.Add(new ILHook(method, RouteInvoke));
                }
            }
        }

        private static MethodInfo? StateMachine(Type owner, string prefix)
        {
            return owner.GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(t => t.Name.StartsWith(prefix, StringComparison.Ordinal))?
                .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        // The prefab's startup behaviours are switched on one by one; the 1.5.0 launch stalls inside this loop
        private void RouteEnable(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(i => i.MatchCallvirt<Behaviour>("set_enabled")))
            {
                log.LogWarning("RoR2Application.EnableBehaviours has changed; startup behaviours aren't timed");
                return;
            }

            cursor.Remove();
            cursor.EmitDelegate<Action<Behaviour, bool>>(TimedEnable);
        }

        private static void TimedEnable(Behaviour behaviour, bool enabled)
        {
            long start = Stopwatch.GetTimestamp();
            behaviour.enabled = enabled;
            string label = behaviour ? $"Enabling {behaviour.GetType().FullName} on {behaviour.gameObject.name}" : "Enabling a destroyed behaviour";
            lock (Timeline.Systems)
            {
                Timeline.Systems.Add(new Span(label, start, Stopwatch.GetTimestamp()));
            }
        }

        private void RouteInvoke(ILContext il)
        {
            var cursor = new ILCursor(il);
            int routed = 0;
            while (cursor.TryGotoNext(i => i.MatchCallvirt<MethodBase>(nameof(MethodBase.Invoke))))
            {
                cursor.Remove();
                cursor.EmitDelegate<Func<MethodBase, object, object[], object>>(TimedInvoke);
                routed++;
            }

            if (routed == 0)
            {
                log.LogWarning($"No initializer call found in {il.Method.FullName}; systems there aren't timed");
            }
        }

        private static object TimedInvoke(MethodBase method, object target, object[] arguments)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                return method.Invoke(target, arguments);
            }
            finally
            {
                string label = method.DeclaringType != null ? $"{method.DeclaringType.FullName}.{method.Name}" : method.Name;

                // The runner keeps a thread of its own, so this may not be the main thread
                lock (Timeline.Systems)
                {
                    Timeline.Systems.Add(new Span(label, start, Stopwatch.GetTimestamp()));
                }
            }
        }
    }
}
