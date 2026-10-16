using System;
using System.Collections.Generic;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace JustBeBetter.Safety
{
    // The picker panel is spawned on top of the HUD and covers it
    // This draws the run timer's block above it so a frozen timer shows the game is paused
    // Local UI only, so it runs on every machine
    internal sealed class RunTimerKeeper
    {
        private const int MaxHierarchyDepth = 32;

        private readonly Dictionary<NetworkUIPromptController, Canvas> raisedBlocks =
            new Dictionary<NetworkUIPromptController, Canvas>();

        private readonly ManualLogSource log;

        internal RunTimerKeeper(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.NetworkUIPromptController.OnControlBegin += OnControlBegin;
            On.RoR2.NetworkUIPromptController.OnControlEnd += OnControlEnd;
        }

        internal void Disable()
        {
            On.RoR2.NetworkUIPromptController.OnControlBegin -= OnControlBegin;
            On.RoR2.NetworkUIPromptController.OnControlEnd -= OnControlEnd;
            foreach (Canvas canvas in raisedBlocks.Values)
            {
                if (canvas)
                {
                    UnityEngine.Object.Destroy(canvas);
                }
            }

            raisedBlocks.Clear();
        }

        // The panel is created inside orig, so it already exists when the timer is raised
        private void OnControlBegin(
            On.RoR2.NetworkUIPromptController.orig_OnControlBegin orig,
            NetworkUIPromptController self)
        {
            orig(self);

            try
            {
                if (self && self.isDisplaying && PickerTypes.IsPicker(self))
                {
                    RaiseTimer(self);
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't keep the run timer visible: {e}");
            }
        }

        private void OnControlEnd(
            On.RoR2.NetworkUIPromptController.orig_OnControlEnd orig,
            NetworkUIPromptController self)
        {
            try
            {
                LowerTimer(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't put the run timer back: {e}");
            }

            orig(self);
        }

        private void RaiseTimer(NetworkUIPromptController prompt)
        {
            if (raisedBlocks.ContainsKey(prompt))
            {
                return;
            }

            HUD? hud = LocalHudOf(prompt.currentParticipantMaster);
            if (!hud)
            {
                return;
            }

            RunTimerUIController timer = hud!.GetComponentInChildren<RunTimerUIController>(true);
            if (!timer)
            {
                log.LogWarning("No run timer found in the HUD");
                return;
            }

            Transform container = hud.mainContainer.transform;
            GameObject block = FindTimerBlock(timer.transform, container).gameObject;
            if (block.GetComponent<Canvas>())
            {
                log.LogWarning($"Run timer block {block.name} already has its own canvas; left as is");
                return;
            }

            int sortingOrder = HighestSortingOrder(container) + 1;
            Canvas canvas = block.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            raisedBlocks[prompt] = canvas;

            log.Verbose(
                $"Run timer kept visible: raised {PathBelow(block.transform, container)} to sorting order "
                + $"{sortingOrder}; HUD panel active: {hud.mainUIPanel.activeInHierarchy}");
        }

        private void LowerTimer(NetworkUIPromptController prompt)
        {
            if (!raisedBlocks.TryGetValue(prompt, out Canvas canvas))
            {
                return;
            }

            raisedBlocks.Remove(prompt);
            if (canvas)
            {
                UnityEngine.Object.Destroy(canvas);
            }
        }

        // Same route the game takes in NetworkUIPromptController.OnParticipantDiscovered
        private static HUD? LocalHudOf(CharacterMaster master)
        {
            if (!master || !master.playerCharacterMasterController)
            {
                return null;
            }

            NetworkUser networkUser = master.playerCharacterMasterController.networkUser;
            if (!networkUser || networkUser.localUser == null)
            {
                return null;
            }

            CameraRigController cameraRig = networkUser.localUser.cameraRigController;
            return cameraRig ? cameraRig.hud : null;
        }

        // Walks up to the largest ancestor that isn't a full-screen layer, the top-right block holding stage,
        // timer and difficulty
        private static Transform FindTimerBlock(Transform timer, Transform container)
        {
            Transform block = timer;
            for (int depth = 0; depth < MaxHierarchyDepth; depth++)
            {
                Transform parent = block.parent;
                if (!parent || parent == container || IsFullScreen(parent))
                {
                    break;
                }

                block = parent;
            }

            return block;
        }

        private static bool IsFullScreen(Transform transform)
        {
            return transform is RectTransform rect
                && rect.anchorMin == Vector2.zero
                && rect.anchorMax == Vector2.one;
        }

        private static int HighestSortingOrder(Transform container)
        {
            int highest = 0;
            foreach (Canvas canvas in container.GetComponentsInChildren<Canvas>(true))
            {
                highest = Math.Max(highest, canvas.sortingOrder);
            }

            return highest;
        }

        private static string PathBelow(Transform target, Transform root)
        {
            string path = target.name;
            Transform current = target.parent;
            for (int depth = 0; depth < MaxHierarchyDepth && current && current != root; depth++)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
