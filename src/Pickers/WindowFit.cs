using System;

namespace JustBeBetter.Pickers
{
    // How many columns and rows a picker grid needs for its items, and how far its buttons shrink
    // Roughly square unless a row length is asked for, wider once the screen's height runs out, and only a full
    // screen shrinks the buttons
    // Plain C# in grid units so the tests can drive it
    internal static class WindowFit
    {
        internal const float MinScale = 0.4f;
        private const float ScaleStep = 0.05f;
        private const float Slack = 0.5f;

        // wantedColumns 0 leaves a list the game's grid holds as it is
        internal static Plan For(
            int count, int gameColumns, Grid grid, float gameWidth, float gameHeight, float maxWidth, float maxHeight, int wantedColumns = 0)
        {
            if (count <= 0 || gameColumns <= 0)
            {
                return Plan.Unchanged;
            }

            // The game's grid already holds them, counting only the columns that fit its width
            bool gameHolds = gameColumns <= grid.Fit(gameWidth, 1f, vertical: false)
                && Rows(count, gameColumns) <= grid.Fit(gameHeight, 1f, vertical: true);
            if (gameHolds && (wantedColumns <= 0 || wantedColumns == gameColumns))
            {
                return Plan.Unchanged;
            }

            int target = wantedColumns > 0 ? wantedColumns : Math.Max(gameColumns, (int)Math.Ceiling(Math.Sqrt(count)));
            target = Math.Min(target, count);

            // A room that can't be measured gives no more than the game's
            maxWidth = float.IsNaN(maxWidth) ? gameWidth : maxWidth;
            maxHeight = float.IsNaN(maxHeight) ? gameHeight : maxHeight;

            int steps = (int)Math.Round((1f - MinScale) / ScaleStep);
            for (int step = 0; step <= steps; step++)
            {
                // Float steps land a hair under the last one
                float scale = Math.Max(MinScale, 1f - step * ScaleStep);
                int columns = grid.Fit(maxWidth, scale, vertical: false);
                int rows = grid.Fit(maxHeight, scale, vertical: true);
                if (columns >= 1 && rows >= 1 && (long)columns * rows >= count)
                {
                    return Choose(count, target, columns, rows, scale);
                }
            }

            // Too many for the screen even at the smallest buttons, so the widest grid, running past the bottom
            int lastColumns = Math.Max(1, grid.Fit(maxWidth, MinScale, vertical: false));
            return new Plan(true, lastColumns, Rows(count, lastColumns), MinScale, true);
        }

        // The target row length while the screen is tall enough for it, else just wide enough for the rows that fit
        private static Plan Choose(int count, int target, int maxColumns, int maxRows, float scale)
        {
            int columns = Math.Min(target, maxColumns);
            if (Rows(count, columns) > maxRows)
            {
                columns = Math.Min(maxColumns, Rows(count, maxRows));
            }

            return new Plan(true, columns, Rows(count, columns), scale, false);
        }

        private static int Rows(int count, int columns)
        {
            return (count + columns - 1) / columns;
        }

        internal readonly struct Grid
        {
            internal Grid(float cellWidth, float cellHeight, float spacingX, float spacingY, float paddingX, float paddingY)
            {
                CellWidth = cellWidth;
                CellHeight = cellHeight;
                SpacingX = spacingX;
                SpacingY = spacingY;
                PaddingX = paddingX;
                PaddingY = paddingY;
            }

            internal float CellWidth { get; }

            internal float CellHeight { get; }

            internal float SpacingX { get; }

            internal float SpacingY { get; }

            internal float PaddingX { get; }

            internal float PaddingY { get; }

            // Cells and spacing shrink with the scale, padding stays
            internal float Width(int columns, float scale)
            {
                return PaddingX + (columns * CellWidth + Math.Max(0, columns - 1) * SpacingX) * scale;
            }

            internal float Height(int rows, float scale)
            {
                return PaddingY + (rows * CellHeight + Math.Max(0, rows - 1) * SpacingY) * scale;
            }

            internal int Fit(float size, float scale, bool vertical)
            {
                float cell = (vertical ? CellHeight : CellWidth) * scale;
                float spacing = (vertical ? SpacingY : SpacingX) * scale;
                float padding = vertical ? PaddingY : PaddingX;
                if (cell <= 0f)
                {
                    return 0;
                }

                // Mono turns an infinite or huge float into int.MinValue, so the cast is kept in range first
                double fit = Math.Floor((size - padding + spacing + Slack) / (cell + spacing));
                return double.IsNaN(fit) || fit < 0 ? 0 : fit >= int.MaxValue ? int.MaxValue : (int)fit;
            }
        }

        internal readonly struct Plan
        {
            internal static readonly Plan Unchanged = new Plan(false, 0, 0, 1f, false);

            internal Plan(bool grows, int columns, int rows, float scale, bool overflows)
            {
                Grows = grows;
                Columns = columns;
                Rows = rows;
                Scale = scale;
                Overflows = overflows;
            }

            internal bool Grows { get; }

            internal int Columns { get; }

            internal int Rows { get; }

            internal float Scale { get; }

            internal bool Overflows { get; }
        }
    }
}
