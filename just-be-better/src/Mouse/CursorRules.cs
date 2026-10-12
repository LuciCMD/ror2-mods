using System;
using System.Reflection;
using BepInEx.Logging;
using MonoMod.Cil;
using RoR2;
using RoR2.Networking;
using RoR2.UI;
using UnityEngine;

namespace JustBeBetter.Mouse
{
    // The game sets the cursor in two places every frame, RoR2Application.UpdateCursorState and
    // MPEventSystemManager.Update: Confined while it draws its own cursor, Locked otherwise, loading included
    // Both go through Apply so one rule decides
    // Letting either set Confined for even a frame would pull a mouse that has left the window back in
    internal sealed class CursorRules : IFeature
    {
        private readonly CursorSettings settings;
        private readonly ManualLogSource log;
        private Func<bool>? isLoadingScene;
        private bool visible;
        private string state = string.Empty;

        internal CursorRules(CursorSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));

            // Private static property, reads UNet's scene-change operation
            MethodInfo? getter = typeof(NetworkManagerSystem)
                .GetProperty("isLoadingScene", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetGetMethod(true);
            isLoadingScene = getter != null ? (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter) : null;
            if (isLoadingScene == null)
            {
                log.LogWarning("NetworkManagerSystem.isLoadingScene not found; the mouse stays locked during stage loads");
            }
        }

        public void Enable()
        {
            On.RoR2.RoR2Application.UpdateCursorState += OnUpdateCursorState;
            IL.RoR2.MPEventSystemManager.Update += RouteCursorCalls;
        }

        public void Disable()
        {
            On.RoR2.RoR2Application.UpdateCursorState -= OnUpdateCursorState;
            IL.RoR2.MPEventSystemManager.Update -= RouteCursorCalls;
        }

        // Replaces the game's version rather than wrapping it: the original only sets the mode when it differs
        // from what the game wants, so a load starting mid-play would never reach Apply
        private void OnUpdateCursorState(On.RoR2.RoR2Application.orig_UpdateCursorState orig)
        {
            MPEventSystem keyboardMouse = MPEventSystemManager.kbmEventSystem;
            if (Application.isBatchMode || !keyboardMouse)
            {
                orig();
                return;
            }

            MPEventSystem combined = MPEventSystemManager.combinedEventSystem;
            bool gameCursor = keyboardMouse.isCursorVisible || (combined && combined.isCursorVisible);
            Apply(gameCursor ? CursorLockMode.Confined : CursorLockMode.Locked);
        }

        // Cursor is a static class, so the generic MatchCall<T> the analyzer suggests can't take it
#pragma warning disable CA2263
        private void RouteCursorCalls(ILContext il)
        {
            var cursor = new ILCursor(il);
            int lockCalls = 0;
            while (cursor.TryGotoNext(i => i.MatchCall(typeof(Cursor), "set_lockState")))
            {
                cursor.Remove();
                cursor.EmitDelegate<Action<CursorLockMode>>(Apply);
                lockCalls++;
            }

            cursor.Index = 0;
            int visibleCalls = 0;
            while (cursor.TryGotoNext(i => i.MatchCall(typeof(Cursor), "set_visible")))
            {
                cursor.Remove();
                cursor.EmitDelegate<Action<bool>>(_ => ApplyVisible());
                visibleCalls++;
            }

            if (lockCalls != 1 || visibleCalls != 1)
            {
                log.LogWarning(
                    $"MPEventSystemManager.Update changed: routed {lockCalls} lock and {visibleCalls} visibility "
                    + "calls, expected one each; the cursor may flicker between modes");
            }
        }
#pragma warning restore CA2263

        private void Apply(CursorLockMode wanted)
        {
            bool menu = wanted == CursorLockMode.Confined && settings.FreeInMenus.Value;

            // A lock taken into one of the game's loading stalls keeps the mouse clipped to the window even
            // after tabbing out, since the frozen game can't release it, so it waits until there's play to aim
            bool waiting = wanted == CursorLockMode.Locked && settings.FreeWhileLoading.Value && !InPlay();
            CursorLockMode mode = menu || waiting ? CursorLockMode.None : wanted;

            // The game draws its own cursor only in menus, and only follows the mouse while focused
            bool unfocused = !Application.isFocused && (settings.FreeInMenus.Value || settings.FreeWhileLoading.Value);
            visible = waiting || unfocused;
            if (Cursor.lockState != mode)
            {
                Cursor.lockState = mode;
            }

            ApplyVisible();
            Report(waiting ? "free until play starts" : menu ? "free in a menu" : $"{mode}, as the game sets it", unfocused);
        }

        // A local camera following something: the only time the game needs the mouse locked
        private bool InPlay()
        {
            if (IsLoadingScene())
            {
                return false;
            }

            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig && rig.localUserViewer != null && rig.target)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyVisible()
        {
            if (Cursor.visible != visible)
            {
                Cursor.visible = visible;
            }
        }

        private bool IsLoadingScene()
        {
            if (isLoadingScene == null)
            {
                return false;
            }

            try
            {
                return isLoadingScene();
            }
            catch (Exception e)
            {
                isLoadingScene = null;
                log.LogWarning($"Couldn't tell whether a stage is loading; the mouse stays locked during stage loads: {e}");
                return false;
            }
        }

        private void Report(string newState, bool unfocused)
        {
            string full = unfocused ? $"{newState}; another window has focus, so Windows' cursor shows" : newState;
            if (full != state)
            {
                state = full;
                log.LogDebug($"Cursor: {full}");
            }
        }
    }
}
