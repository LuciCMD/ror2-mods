using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Pickers
{
    // Lives on an item picker panel: grows its window to fit the items, and sets the background's blur and opacity
    // The game's sizes are read once, so a picker that resets its options is fitted from them again
    internal sealed class PickerWindow : MonoBehaviour
    {
        private const string WindowPath = "MainPanel";
        private const string BackgroundPath = "MainPanel/Juice/BG";
        private const string BlurType = "TranslucentImage";

        // Room left around the window, as a share of the screen
        private const float ScreenWidthShare = 0.9f;
        private const float ScreenHeightShare = 0.85f;

        // Each kind of picker is described in the log once, which is what a fit gone wrong needs
        private static readonly HashSet<string> Described = new HashSet<string>();
        private static readonly HashSet<string> Checked = new HashSet<string>();
        private static readonly Vector3[] Corners = new Vector3[4];
        private const float CheckDelay = 1f;

        private readonly List<Background> backgrounds = new List<Background>();
        private PickupPickerPanel? panel;
        private PickerSettings? settings;
        private ManualLogSource? log;
        private GridLayoutGroup? grid;
        private RectTransform? window;
        private Game? game;
        private bool unfit;
        private Vector2 setSize;
        private float checkAt = -1f;

        internal static PickerWindow For(PickupPickerPanel panel, PickerSettings settings, ManualLogSource log)
        {
            if (!panel.TryGetComponent(out PickerWindow picker))
            {
                picker = panel.gameObject.AddComponent<PickerWindow>();
                picker.panel = panel;
                picker.settings = settings;
                picker.log = log;
                picker.FindBackgrounds();
            }

            return picker;
        }

        // Before the panel builds its buttons, since it lays out their keyboard order by its column count
        internal void Fit(int count)
        {
            if (settings == null || panel == null || unfit)
            {
                return;
            }

            if (game == null && !Measure())
            {
                unfit = true;
                return;
            }

            Restore();
            checkAt = -1f;
            if (!settings.FitBigLists.Value)
            {
                return;
            }

            Game g = game!;
            WindowFit.Plan plan = WindowFit.For(
                count, g.Columns, g.Grid, g.GridSize.x, g.GridSize.y, g.MaxGridSize.x, g.MaxGridSize.y, settings.ItemsPerRow.Value);
            if (!plan.Grows)
            {
                return;
            }

            grid!.cellSize = g.CellSize * plan.Scale;
            grid.spacing = g.Spacing * plan.Scale;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = plan.Columns;
            panel.maxColumnCount = plan.Columns;

            // The grid stretches with the window, so the window grows by what the grid lacks
            var needed = new Vector2(g.Grid.Width(plan.Columns, plan.Scale), g.Grid.Height(plan.Rows, plan.Scale));
            window!.sizeDelta = g.WindowSize + Vector2.Max(Vector2.zero, needed - g.GridSize);
            KeepOnScreen();
            setSize = window.sizeDelta;
            checkAt = Time.unscaledTime + CheckDelay;

            string line =$"{name}: {count} items in {plan.Columns} x {plan.Rows} at {plan.Scale:P0} button size";
            if (plan.Overflows)
            {
                log!.LogWarning($"{line}, still too many for the screen so the grid runs past the window");
            }
            else
            {
                log!.Verbose(line);
            }
        }

        // The Command panel's PickerPanelSizeAdjuster adds height for the rows at the end of SetPickupOptions, on
        // top of whatever size the window has, so a fitted window gets its own size back afterwards
        internal void AfterSetup()
        {
            if (window == null || checkAt < 0f)
            {
                return;
            }

            if ((window.sizeDelta - setSize).sqrMagnitude > 1f)
            {
                log?.Verbose($"{name}: the game resized the fitted window to {window.sizeDelta}, put back to {setSize}");
                window.sizeDelta = setSize;
                KeepOnScreen();
            }
        }

        // The open animation and layout run after us, so whether the size held is only known a moment later
        // Unscaled time, the world may be paused while picking
        private void LateUpdate()
        {
            KeepBlur();
            if (checkAt < 0f || Time.unscaledTime < checkAt)
            {
                return;
            }

            checkAt = -1f;
            try
            {
                CheckSize();
            }
            catch (Exception e)
            {
                log?.LogError($"Couldn't check the item picker window's size: {e}");
            }
        }

        private void CheckSize()
        {
            if (window == null)
            {
                return;
            }

            if ((window.sizeDelta - setSize).sqrMagnitude > 1f)
            {
                log!.LogWarning(
                    $"{name}: something changed the window after it was fitted, set to {setSize}, now {window.sizeDelta} "
                    + $"({window.rect.width:0} x {window.rect.height:0}). Layout: {Layout()}");
            }
            else if (Logs.VerboseOn && Checked.Add(name))
            {
                log!.Verbose($"{name}: the window kept its fitted size, {window.rect.width:0} x {window.rect.height:0}. Layout: {Layout()}");
            }
        }

        // The window, what it sits in and what it holds, with anything that can size a rect
        private string Layout()
        {
            var lines = new List<string>();
            for (Transform? at = window!.parent; at != null && at != transform.parent; at = at.parent)
            {
                lines.Add("up " + DescribeRect(at));
            }

            lines.Add("window " + DescribeRect(window));
            foreach (Transform child in window)
            {
                lines.Add(DescribeRect(child));
                foreach (Transform inner in child)
                {
                    lines.Add("  " + DescribeRect(inner));
                }
            }

            return string.Join(" | ", lines);
        }

        private static string DescribeRect(Transform t)
        {
            if (t is not RectTransform r)
            {
                return t.name;
            }

            var drivers = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                switch (c)
                {
                    case UIJuice juice:
                        drivers.Add($"UIJuice pans {(juice.panningRect != null ? juice.panningRect.name : "nothing")}");
                        break;
                    case AspectRatioFitter aspect:
                        drivers.Add($"AspectRatioFitter {aspect.aspectMode} {aspect.aspectRatio:0.##}");
                        break;
                    case ContentSizeFitter fitter:
                        drivers.Add($"ContentSizeFitter {fitter.horizontalFit}/{fitter.verticalFit}");
                        break;
                    case LayoutGroup group:
                        drivers.Add(group.GetType().Name);
                        break;
                    case LayoutElement element:
                        drivers.Add($"LayoutElement ignore {element.ignoreLayout}");
                        break;
                }
            }

            return $"{t.name} anchors {r.anchorMin}-{r.anchorMax} size {r.sizeDelta} rect {r.rect.width:0}x{r.rect.height:0}"
                + (drivers.Count > 0 ? $" [{string.Join(", ", drivers)}]" : string.Empty);
        }

        // Blur and opacity, read each time a picker opens
        internal void ApplyLook()
        {
            if (settings == null)
            {
                return;
            }

            // The blur is a full-screen layer behind the window, so opacity is only the window's own background
            float opacity = Mathf.Clamp(settings.WindowOpacity.Value, 0, 100) / 100f;
            foreach (Background background in backgrounds)
            {
                if (background.Image && !background.Blurs)
                {
                    Color color = background.Image.color;
                    color.a = background.Alpha * opacity;
                    background.Image.color = color;
                }
            }

            KeepBlur();
        }

        // LookingGlass sets the blur from its own option once the picker has opened, after us, so it's held every frame
        private void KeepBlur()
        {
            if (settings == null)
            {
                return;
            }

            bool blur = settings.WindowBlur.Value;
            foreach (Background background in backgrounds)
            {
                if (background.Blurs && background.Image && background.Image.enabled != blur)
                {
                    background.Image.enabled = blur;
                }
            }
        }

        private void FindBackgrounds()
        {
            var seen = new HashSet<Image>();
            // Only ones the game shows, a blur it left off stays off
            foreach (Image image in GetComponentsInChildren<Image>(true).Where(i => i.GetType().Name == BlurType && i.enabled))
            {
                seen.Add(image);
                backgrounds.Add(new Background(image, image.color.a, true));
            }

            Transform? bg = transform.Find(BackgroundPath);
            if (bg != null && bg.TryGetComponent(out Image plain) && plain.enabled && seen.Add(plain))
            {
                backgrounds.Add(new Background(plain, plain.color.a, false));
            }

            if (backgrounds.Count == 0)
            {
                log?.LogWarning($"{name} has no background found, Window Blur and Window Opacity leave it as it is");
            }
        }

        // The game's grid and window, and how far the window may grow on this screen
        private bool Measure()
        {
            grid = panel!.gridlayoutGroup != null ? panel.gridlayoutGroup : GetComponentInChildren<GridLayoutGroup>(true);
            window = transform.Find(WindowPath) as RectTransform;
            var gridRect = grid != null ? grid.transform as RectTransform : null;
            Canvas? canvas = GetComponentInParent<Canvas>();
            var screen = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (grid == null || gridRect == null || window == null || screen == null)
            {
                log!.LogWarning($"{name}'s window or grid wasn't found, it keeps the game's size");
                return false;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(window);
            Vector2 windowSize = window.rect.size;
            Vector2 gridSize = gridRect.rect.size;
            bool stretches = Stretches(gridRect);
            int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? grid.constraintCount : panel.maxColumnCount;
            Describe(gridRect, windowSize, gridSize, stretches, columns);
            if (!stretches)
            {
                log!.LogWarning($"{name}'s grid doesn't grow with its window, it keeps the game's size");
                return false;
            }

            // The screen in the window's own units, in case anything between them is scaled
            float scale = ScreenScale(screen);
            Vector2 maxWindow = Vector2.Scale(screen.rect.size * scale, new Vector2(ScreenWidthShare, ScreenHeightShare));
            Vector2 maxGrid = Vector2.Max(gridSize, gridSize + maxWindow - windowSize);

            var shape = new WindowFit.Grid(
                grid.cellSize.x, grid.cellSize.y, grid.spacing.x, grid.spacing.y, grid.padding.horizontal, grid.padding.vertical);
            game = new Game(
                shape,
                grid.cellSize,
                grid.spacing,
                grid.constraint,
                grid.constraintCount,
                panel.maxColumnCount,
                Math.Max(1, columns),
                window.sizeDelta,
                window.anchoredPosition,
                gridSize,
                maxGrid);
            return true;
        }

        // Grown by a little and put back, the grid has to follow for growing the window to help
        // One side at a time, so anything tying width to height shows up
        private bool Stretches(RectTransform gridRect)
        {
            return Follows(gridRect, new Vector2(10f, 0f)) && Follows(gridRect, new Vector2(0f, 10f));
        }

        private bool Follows(RectTransform gridRect, Vector2 step)
        {
            Vector2 size = window!.sizeDelta;
            Vector2 before = gridRect.rect.size;
            window.sizeDelta = size + step;
            LayoutRebuilder.ForceRebuildLayoutImmediate(window);
            Vector2 grown = gridRect.rect.size - before;
            window.sizeDelta = size;
            LayoutRebuilder.ForceRebuildLayoutImmediate(window);
            return (grown - step).sqrMagnitude < 1f;
        }

        private void Restore()
        {
            Game g = game!;
            grid!.cellSize = g.CellSize;
            grid.spacing = g.Spacing;
            grid.constraint = g.Constraint;
            grid.constraintCount = g.ConstraintCount;
            panel!.maxColumnCount = g.MaxColumnCount;
            window!.sizeDelta = g.WindowSize;
            window.anchoredPosition = g.Position;
        }

        // A window grown off-centre could reach past the screen's edge, so it's moved back in
        private void KeepOnScreen()
        {
            Canvas? canvas = GetComponentInParent<Canvas>();
            var screen = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (screen == null || window!.parent == null)
            {
                return;
            }

            window.GetWorldCorners(Corners);
            Vector3 min = screen.InverseTransformPoint(Corners[0]);
            Vector3 max = screen.InverseTransformPoint(Corners[2]);
            Rect bounds = screen.rect;
            // Left and top edges first, so a window too big for the screen runs off the bottom with its search bar in view
            float dx = min.x < bounds.xMin ? bounds.xMin - min.x : max.x > bounds.xMax ? bounds.xMax - max.x : 0f;
            float dy = max.y > bounds.yMax ? bounds.yMax - max.y : min.y < bounds.yMin ? bounds.yMin - min.y : 0f;
            if (dx != 0f || dy != 0f)
            {
                window.anchoredPosition += new Vector2(dx, dy) * ScreenScale(screen);
            }
        }

        // A zero scale anywhere would give infinity or NaN, so that counts as unscaled
        private float ScreenScale(RectTransform screen)
        {
            float scale = window!.parent != null ? screen.lossyScale.x / window.parent.lossyScale.x : 1f;
            return float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f ? 1f : scale;
        }

        private void Describe(RectTransform gridRect, Vector2 windowSize, Vector2 gridSize, bool stretches, int columns)
        {
            if (!Logs.VerboseOn || !Described.Add(name))
            {
                return;
            }

            string looks = string.Join(", ", backgrounds.Select(b => $"{Path(b.Image.transform)} ({(b.Blurs ? "blur" : "plain")}, alpha {b.Alpha:0.00})"));
            log!.Verbose(
                $"{name}: window {windowSize.x:0} x {windowSize.y:0}, grid {gridSize.x:0} x {gridSize.y:0} at "
                + $"{Path(gridRect)} ({(stretches ? "grows with the window" : "fixed")}), {columns} columns of "
                + $"{grid!.cellSize.x:0} x {grid.cellSize.y:0}, spacing {grid.spacing.x:0}, padding "
                + $"{grid.padding.horizontal} x {grid.padding.vertical}, backgrounds: {(looks.Length > 0 ? looks : "none")}");
        }

        private string Path(Transform t)
        {
            var parts = new List<string>();
            for (Transform? at = t; at != null && at != transform; at = at.parent)
            {
                parts.Insert(0, at.name);
            }

            return string.Join("/", parts);
        }

        private readonly struct Background
        {
            internal Background(Image image, float alpha, bool blurs)
            {
                Image = image;
                Alpha = alpha;
                Blurs = blurs;
            }

            internal Image Image { get; }

            internal float Alpha { get; }

            internal bool Blurs { get; }
        }

        private sealed class Game
        {
            internal Game(
                WindowFit.Grid grid,
                Vector2 cellSize,
                Vector2 spacing,
                GridLayoutGroup.Constraint constraint,
                int constraintCount,
                int maxColumnCount,
                int columns,
                Vector2 windowSize,
                Vector2 position,
                Vector2 gridSize,
                Vector2 maxGridSize)
            {
                Grid = grid;
                CellSize = cellSize;
                Spacing = spacing;
                Constraint = constraint;
                ConstraintCount = constraintCount;
                MaxColumnCount = maxColumnCount;
                Columns = columns;
                WindowSize = windowSize;
                Position = position;
                GridSize = gridSize;
                MaxGridSize = maxGridSize;
            }

            internal WindowFit.Grid Grid { get; }

            internal Vector2 CellSize { get; }

            internal Vector2 Spacing { get; }

            internal GridLayoutGroup.Constraint Constraint { get; }

            internal int ConstraintCount { get; }

            internal int MaxColumnCount { get; }

            internal int Columns { get; }

            internal Vector2 WindowSize { get; }

            internal Vector2 Position { get; }

            internal Vector2 GridSize { get; }

            internal Vector2 MaxGridSize { get; }
        }
    }
}
