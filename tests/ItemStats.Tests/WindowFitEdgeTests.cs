using System;
using System.Collections.Generic;
using JustBeBetter.Pickers;

// Odd grids, odd screens and the float edges of the fit, beyond the made-up 80 px grid
internal static partial class Program
{
    // What Grid.Fit allows past the room for float error
    private const float FitSlack = 0.5f;

    private static void RunWindowFitEdges()
    {
        Test("Picker window edge: smallest buttons are never below MinScale", FitScaleNotBelowMin);
        Test("Picker window edge: every grid, screen and count keeps the plan's promises", FitSweepInvariants);
        Test("Picker window edge: game columns wider than the game's grid still grow", FitGameColumnsPastWidth);
        Test("Picker window edge: spacing that cancels the cell gives a usable plan", FitSpacingCancelsCell);
        Test("Picker window edge: an infinite room gives a usable plan", FitInfiniteRoom);
        Test("Picker window edge: a NaN room doesn't fall to one long column", FitNaNRoom);
        Test("Picker window edge: a screen smaller than one button overflows with one column", FitTinyScreen);
        Test("Picker window edge: one item more than the game's grid grows by one row", FitOneMore);
        Test("Picker window edge: a single game column goes roughly square", FitSingleColumn);
        RunWindowFitRows();
    }

    // At 80 px buttons on 1600 x 900, 0.45 holds 40 x 22 = 880 and 0.4 holds 45 x 25 = 1125
    // so 1000 items only fit on the last step, whose scale is 1 - 12 * 0.05 in float
    private static void FitScaleNotBelowMin()
    {
        WindowFit.Plan plan = FitFor(1000);
        Expect(plan.Grows && !plan.Overflows, $"expected a fit on the last step, got {plan.Columns} x {plan.Rows} overflows {plan.Overflows}");
        Expect(plan.Scale >= WindowFit.MinScale, $"scale {plan.Scale:R} is below MinScale {WindowFit.MinScale:R}");
    }

    private static void FitSweepInvariants()
    {
        var grids = new (string Name, WindowFit.Grid Grid)[]
        {
            ("square 80", new WindowFit.Grid(80f, 80f, 8f, 8f, 0f, 0f)),
            ("odd 64x96 padded", new WindowFit.Grid(64f, 96f, 4f, 12f, 10f, 20f)),
            ("wide 100x30 no spacing", new WindowFit.Grid(100f, 30f, 0f, 0f, 0f, 0f)),
            ("negative spacing", new WindowFit.Grid(50f, 50f, -5f, -5f, 0f, 0f)),
            ("tiny 1 px", new WindowFit.Grid(1f, 1f, 0f, 0f, 0f, 0f)),
            ("big 500 padded", new WindowFit.Grid(500f, 500f, 20f, 20f, 40f, 40f)),
            ("fractional", new WindowFit.Grid(73.3f, 61.7f, 6.15f, 3.35f, 12.5f, 7.25f)),
        };
        var screens = new (float W, float H)[] { (1600f, 900f), (300f, 200f), (10000f, 10000f), (3440f, 1200f), (1152f, 648f) };
        int[] columnsChoices = { 1, 5, 8, 30 };
        var counts = new List<int>();
        for (int c = 1; c <= 400; c++)
        {
            counts.Add(c);
        }

        counts.AddRange(new[] { 999, 1000, 1001, 5000, 20000 });
        var problems = new List<string>();
        foreach ((string name, WindowFit.Grid grid) in grids)
        {
            foreach ((float maxW, float maxH) in screens)
            {
                foreach (int gameColumns in columnsChoices)
                {
                    // The game's grid at its columns and four rows, never bigger than the room, as PickerWindow promises
                    float gameW = Math.Min(maxW, grid.Width(gameColumns, 1f));
                    float gameH = Math.Min(maxH, grid.Height(4, 1f));
                    foreach (int count in counts)
                    {
                        string where = $"{name} on {maxW}x{maxH}, game {gameColumns} cols, {count} items";
                        WindowFit.Plan plan;
                        try
                        {
                            plan = WindowFit.For(count, gameColumns, grid, gameW, gameH, maxW, maxH);
                        }
                        catch (Exception e)
                        {
                            problems.Add($"{where}: threw {e.GetType().Name}");
                            continue;
                        }

                        if (!plan.Grows)
                        {
                            continue;
                        }

                        string got = $"{plan.Columns} x {plan.Rows} at {plan.Scale:R}{(plan.Overflows ? " overflowing" : string.Empty)}";
                        if (plan.Columns < 1 || plan.Rows < 1)
                        {
                            problems.Add($"{where}: empty grid {got}");
                            continue;
                        }

                        if ((long)plan.Columns * plan.Rows < count)
                        {
                            problems.Add($"{where}: {got} holds fewer than {count}");
                        }

                        if (plan.Rows != (count + plan.Columns - 1) / plan.Columns)
                        {
                            problems.Add($"{where}: {got} has a spare row");
                        }

                        if (plan.Scale < WindowFit.MinScale - 1e-6f || plan.Scale > 1f)
                        {
                            problems.Add($"{where}: {got} scale out of range");
                        }

                        if (plan.Overflows)
                        {
                            continue;
                        }

                        if (grid.Width(plan.Columns, plan.Scale) > maxW + FitSlack || grid.Height(plan.Rows, plan.Scale) > maxH + FitSlack)
                        {
                            problems.Add($"{where}: {got} is {grid.Width(plan.Columns, plan.Scale)} x {grid.Height(plan.Rows, plan.Scale)}, past the room");
                        }

                        // Only as small as needed, the step above must not have held them
                        float larger = plan.Scale + 0.05f;
                        if (larger <= 1.0001f)
                        {
                            long cap = (long)grid.Fit(maxW, larger, vertical: false) * grid.Fit(maxH, larger, vertical: true);
                            if (cap >= count)
                            {
                                problems.Add($"{where}: {got} shrank though {larger} holds {cap}");
                            }
                        }
                    }
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} bad plans, first: {problems[0]}", "     " + string.Join("\n     ", problems.GetRange(0, Math.Min(8, problems.Count))));
        }
    }

    // The game says 20 columns but its grid is only 696 wide, room for 8, so 40 items take 5 rows of the 4 it has
    private static void FitGameColumnsPastWidth()
    {
        WindowFit.Plan plan = WindowFit.For(40, 20, Shape, GameWidth, GameHeight, MaxWidth, MaxHeight);
        Expect(plan.Grows, "left unchanged, though only 8 of the 20 columns fit the game's 696 px grid and 40 items need 5 rows of its 4");
    }

    private static void FitSpacingCancelsCell()
    {
        var degenerate = new WindowFit.Grid(80f, 80f, -80f, 8f, 0f, 0f);
        WindowFit.Plan plan = WindowFit.For(200, 8, degenerate, GameWidth, GameHeight, MaxWidth, MaxHeight);
        Expect(!plan.Grows || (plan.Columns >= 1 && plan.Rows >= 1 && (long)plan.Columns * plan.Rows >= 200),
            $"got {plan.Columns} x {plan.Rows} at {plan.Scale}");
    }

    // A zero lossyScale on the window's parent makes PickerWindow's room infinite
    private static void FitInfiniteRoom()
    {
        WindowFit.Plan plan = WindowFit.For(40, 8, Shape, GameWidth, GameHeight, float.PositiveInfinity, float.PositiveInfinity);
        Expect(plan.Grows && plan.Columns == 8 && plan.Rows == 5 && plan.Scale == 1f && !plan.Overflows,
            $"got {plan.Columns} x {plan.Rows} at {plan.Scale}, overflows {plan.Overflows}");
    }

    // 0 / 0 lossyScale gives NaN
    private static void FitNaNRoom()
    {
        WindowFit.Plan plan = WindowFit.For(40, 8, Shape, GameWidth, GameHeight, float.NaN, float.NaN);
        Expect(!plan.Grows || plan.Columns > 1, $"got {plan.Columns} x {plan.Rows} at {plan.Scale}, overflows {plan.Overflows}");
    }

    private static void FitTinyScreen()
    {
        WindowFit.Plan plan = WindowFit.For(40, 8, Shape, 20f, 20f, 20f, 20f);
        Expect(plan.Grows && plan.Overflows && plan.Columns == 1 && plan.Rows == 40, $"got {plan.Columns} x {plan.Rows}, overflows {plan.Overflows}");
    }

    private static void FitOneMore()
    {
        WindowFit.Plan plan = FitFor(33);
        Expect(plan.Grows && plan.Columns == 8 && plan.Rows == 5 && plan.Scale == 1f, $"got {plan.Columns} x {plan.Rows} at {plan.Scale}");
    }

    // One column of 80 px, so a longer list goes roughly square, 3 x 3 for 8 and 5 x 5 for 25
    private static void FitSingleColumn()
    {
        WindowFit.Plan down = WindowFit.For(8, 1, Shape, 80f, GameHeight, MaxWidth, MaxHeight);
        Expect(down.Grows && down.Columns == 3 && down.Rows == 3, $"8 items got {down.Columns} x {down.Rows}");
        WindowFit.Plan wide = WindowFit.For(25, 1, Shape, 80f, GameHeight, MaxWidth, MaxHeight);
        Expect(wide.Grows && wide.Columns == 5 && wide.Rows == 5 && wide.Scale == 1f, $"25 items got {wide.Columns} x {wide.Rows} at {wide.Scale}");
    }
}
