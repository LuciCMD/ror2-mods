using System;
using BepInEx.Logging;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Lobby
{
    // The difficulty row (and any other rule shown as a strip) slides one line of choices to centre on the
    // voted one, so the rest can only be reached at its edges
    // Here the choices wrap into a grid like the Artifacts below it, all visible at once, and the sliding is
    // switched off
    internal sealed class RuleStripWrap
    {
        // As the Artifacts grid: 56 px choices, 4 to a line in the game's 263 px strip
        private const float Cell = 56f;
        private const float Gap = 6f;
        private const int Pad = 6;
        private const float FallbackWidth = 263f;

        private readonly ManualLogSource log;

        internal RuleStripWrap(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.UI.RuleBookViewerStrip.SetData += OnSetData;
            On.RoR2.UI.RuleBookViewerStrip.Update += OnUpdate;
        }

        internal void Disable()
        {
            On.RoR2.UI.RuleBookViewerStrip.SetData -= OnSetData;
            On.RoR2.UI.RuleBookViewerStrip.Update -= OnUpdate;
        }

        private void OnSetData(On.RoR2.UI.RuleBookViewerStrip.orig_SetData orig, RuleBookViewerStrip self, System.Collections.Generic.List<RoR2.RuleChoiceDef> newChoices, int choiceIndex)
        {
            orig(self, newChoices, choiceIndex);
            try
            {
                Wrap(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't wrap a rule strip: {e}");
            }
        }

        // A wrapped strip stays put, the game's version would slide it every frame
        private static void OnUpdate(On.RoR2.UI.RuleBookViewerStrip.orig_Update orig, RuleBookViewerStrip self)
        {
            if (!self.GetComponent<WrappedStrip>())
            {
                orig(self);
            }
        }

        // Sets the strip up once, and its line count follows its width from then on, since the game fills it
        // before the panel has been laid out and the strip's width is still the prefab's
        private static void Wrap(RuleBookViewerStrip strip)
        {
            RectTransform container = strip.choiceContainer;
            if (!container)
            {
                return;
            }

            if (container.TryGetComponent(out HorizontalOrVerticalLayoutGroup line))
            {
                UnityEngine.Object.DestroyImmediate(line);
            }

            GridLayoutGroup grid = Ensure<GridLayoutGroup>(container.gameObject);
            grid.padding = new RectOffset(Pad, Pad, Pad, Pad);
            grid.cellSize = new Vector2(Cell, Cell);
            grid.spacing = new Vector2(Gap, Gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;

            // The grid fills the strip's width from the top, and the strip itself asks its parent for the height
            Ensure<LayoutElement>(container.gameObject).ignoreLayout = true;
            container.anchorMin = new Vector2(0f, 1f);
            container.anchorMax = Vector2.one;
            container.pivot = new Vector2(0.5f, 1f);
            container.anchoredPosition = Vector2.zero;
            container.localPosition = new Vector3(container.localPosition.x, container.localPosition.y, 0f);

            WrappedStrip wrapped = Ensure<WrappedStrip>(strip.gameObject);
            wrapped.Init(strip, grid, Ensure<LayoutElement>(strip.gameObject));
            wrapped.Fit();
        }

        private static T Ensure<T>(GameObject target)
            where T : Component
        {
            return target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
        }

        // Marks a strip as wrapped, and refits its grid whenever its width or choice count changes
        private sealed class WrappedStrip : MonoBehaviour
        {
            private RuleBookViewerStrip? strip;
            private GridLayoutGroup? grid;
            private LayoutElement? element;
            private float fittedWidth = -1f;
            private int fittedCount = -1;

            internal void Init(RuleBookViewerStrip strip, GridLayoutGroup grid, LayoutElement element)
            {
                this.strip = strip;
                this.grid = grid;
                this.element = element;
                fittedWidth = -1f;
            }

            internal void Fit()
            {
                if (!strip || !grid || !element)
                {
                    return;
                }

                float width = ((RectTransform)transform).rect.width;
                int count = strip!.choiceControllers.Count;
                if (Mathf.Abs(width - fittedWidth) < 0.5f && count == fittedCount)
                {
                    return;
                }

                fittedWidth = width;
                fittedCount = count;
                int perLine = Mathf.Max(1, (int)(((width > 1f ? width : FallbackWidth) - (Pad * 2) + Gap) / (Cell + Gap)));
                int lines = Mathf.Max(1, (count + perLine - 1) / perLine);
                float height = (Pad * 2) + (lines * Cell) + ((lines - 1) * Gap);
                grid!.constraintCount = perLine;
                strip.choiceContainer.sizeDelta = new Vector2(0f, height);
                element!.minHeight = height;
                element.preferredHeight = height;
                LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
            }

            private void LateUpdate()
            {
                Fit();
            }
        }
    }
}
