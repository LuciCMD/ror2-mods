using System.Collections.Generic;
using System.Linq;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NotEnoughInterface
{
    // Loadout rows (skills and skins) lay their choices out in a grid that wraps onto more lines, instead of one
    // line that squeezes or overflows. Rows get their real height so the tab scrolls when they don't all fit,
    // and controller navigation is rewired to follow the grid
    internal static class LoadoutWrap
    {
        // Matches the game's row: a 128 px label column, then the choices
        private const int LabelWidth = 128;
        private const int Pad = 6;
        private const float Cell = 60f;
        private const float Gap = 8f;

        internal static void Apply(LoadoutPanelController panel)
        {
            // The game's rowContainer is the panel itself
            var container = (RectTransform)panel.transform;
            var rows = new List<List<Selectable>>();
            int perLine = PerLine(container.rect.width);
            for (int i = 0; i < container.childCount; i++)
            {
                Transform row = container.GetChild(i);
                Transform? buttons = row.gameObject.activeSelf ? row.Find("ButtonContainer") : null;
                if (buttons != null)
                {
                    rows.Add(WrapRow(row, buttons, perLine));
                }
            }

            Navigate(rows, perLine);
        }

        // Before layout has run the panel can report no width; the game's own panel is 585 wide
        private static int PerLine(float width)
        {
            float usable = (width > 1f ? width : 577f) - LabelWidth - Pad;
            return Mathf.Max(1, (int)((usable + Gap) / (Cell + Gap)));
        }

        private static List<Selectable> WrapRow(Transform row, Transform buttons, int perLine)
        {
            if (buttons.TryGetComponent(out HorizontalLayoutGroup line))
            {
                Object.DestroyImmediate(line);
            }

            if (!buttons.TryGetComponent(out GridLayoutGroup grid))
            {
                grid = buttons.gameObject.AddComponent<GridLayoutGroup>();
            }

            grid.padding = new RectOffset(LabelWidth, Pad, Pad, Pad);
            grid.cellSize = new Vector2(Cell, Cell);
            grid.spacing = new Vector2(Gap, Gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = perLine;

            // The spacer only pushed the old single line apart; in a grid it would take a cell
            Transform spacer = buttons.Find("Spacer");
            if (spacer != null)
            {
                spacer.gameObject.SetActive(false);
            }

            var choices = new List<Selectable>();
            for (int i = 0; i < buttons.childCount; i++)
            {
                Transform child = buttons.GetChild(i);
                if (child.gameObject.activeSelf && child.TryGetComponent(out Selectable choice))
                {
                    choices.Add(choice);

                    // The grid sets the size now; the fitter would fight it
                    if (child.TryGetComponent(out AspectRatioFitter fitter))
                    {
                        fitter.enabled = false;
                    }
                }
            }

            int lines = Mathf.Max(1, (choices.Count + perLine - 1) / perLine);
            float height = (Pad * 2) + (lines * Cell) + ((lines - 1) * Gap);
            if (row.TryGetComponent(out LayoutElement element))
            {
                element.minHeight = height;
                element.preferredHeight = height;
            }

            return choices;
        }

        // Left and right step through the row in order; up and down move a line within the row, or to the
        // nearest choice in the row above or below. The first and last rows keep the game's links out
        private static void Navigate(List<List<Selectable>> rows, int perLine)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                List<Selectable> row = rows[r];
                for (int i = 0; i < row.Count; i++)
                {
                    Navigation nav = row[i].navigation;
                    nav.mode = Navigation.Mode.Explicit;
                    nav.selectOnLeft = i > 0 ? row[i - 1] : nav.selectOnLeft;
                    nav.selectOnRight = i + 1 < row.Count ? row[i + 1] : nav.selectOnRight;
                    nav.selectOnUp = Above(rows, r, i, perLine) ?? nav.selectOnUp;
                    nav.selectOnDown = Below(rows, r, i, perLine) ?? nav.selectOnDown;
                    row[i].navigation = nav;
                }
            }
        }

        private static Selectable? Above(List<List<Selectable>> rows, int r, int i, int perLine)
        {
            if (i >= perLine)
            {
                return rows[r][i - perLine];
            }

            List<Selectable>? previous = rows.Take(r).LastOrDefault(x => x.Count > 0);
            if (previous == null)
            {
                return null;
            }

            int lastLineStart = (previous.Count - 1) / perLine * perLine;
            return previous[Mathf.Min(lastLineStart + i, previous.Count - 1)];
        }

        private static Selectable? Below(List<List<Selectable>> rows, int r, int i, int perLine)
        {
            List<Selectable> row = rows[r];
            int column = i % perLine;
            int nextLine = ((i / perLine) + 1) * perLine;
            if (nextLine < row.Count)
            {
                return row[Mathf.Min(nextLine + column, row.Count - 1)];
            }

            List<Selectable>? next = rows.Skip(r + 1).FirstOrDefault(x => x.Count > 0);
            return next?[Mathf.Min(column, next.Count - 1)];
        }
    }
}
