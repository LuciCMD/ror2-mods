using System;
using System.Collections.Generic;
using JustBeBetter.Pickers;

// Items per Row and the automatic square shape, swept over odd grids, screens and counts
internal static partial class Program
{
    private static void RunWindowFitRows()
    {
        Test("Picker window rows: every Items per Row keeps its promises", FitRowSweep);
        Test("Picker window rows: automatic is the squarest shape the room allows", FitAutoSweep);
        Test("Picker window rows: a negative Items per Row acts as automatic", FitRowNegative);
        Test("Picker window rows: one per row widens only as far as the height needs", FitRowOneTall);
        Test("Picker window rows: fewer per row than the game's still regrids a list it holds", FitRowFewerThanGame);
        Test("Picker window rows: the Command menu on the author's 1080p room", FitCommandOn1080);
        Test("Picker window rows: a huge Items per Row is capped, not overflowed", FitRowHuge);
    }

    private static readonly (string Name, WindowFit.Grid Grid)[] RowGrids =
    {
        ("command", new WindowFit.Grid(80f, 80f, 2f, 2f, 8f, 10f)),
        ("square 80", new WindowFit.Grid(80f, 80f, 8f, 8f, 0f, 0f)),
        ("odd 64x96 padded", new WindowFit.Grid(64f, 96f, 4f, 12f, 10f, 20f)),
        ("wide 100x30", new WindowFit.Grid(100f, 30f, 0f, 0f, 0f, 0f)),
        ("fractional", new WindowFit.Grid(73.3f, 61.7f, 6.15f, 3.35f, 12.5f, 7.25f)),
        ("big 500", new WindowFit.Grid(500f, 500f, 20f, 20f, 40f, 40f)),
    };

    private static readonly (float W, float H)[] RowScreens = { (1600f, 790f), (1600f, 900f), (300f, 200f), (3440f, 1200f), (1152f, 648f) };

    private static List<int> RowCounts()
    {
        var counts = new List<int>();
        for (int c = 1; c <= 260; c++)
        {
            counts.Add(c);
        }

        counts.AddRange(new[] { 999, 1000, 5000 });
        return counts;
    }

    private static int RowsFor(int count, int columns)
    {
        return (count + columns - 1) / columns;
    }

    // What the plan must hold to whatever row length it aimed at
    private static void CheckShape(List<string> problems, string where, WindowFit.Grid grid, float maxW, float maxH, int count, int aim, WindowFit.Plan plan)
    {
        string got = $"{plan.Columns} x {plan.Rows} at {plan.Scale:R}{(plan.Overflows ? " overflowing" : string.Empty)}";
        if (plan.Columns < 1 || plan.Rows < 1 || (long)plan.Columns * plan.Rows < count || plan.Rows != RowsFor(count, plan.Columns))
        {
            problems.Add($"{where}: {got} doesn't hold {count} exactly");
            return;
        }

        if (plan.Overflows)
        {
            return;
        }

        int maxC = grid.Fit(maxW, plan.Scale, vertical: false);
        int maxR = grid.Fit(maxH, plan.Scale, vertical: true);
        if (plan.Columns > maxC || plan.Rows > maxR)
        {
            problems.Add($"{where}: {got} is past the room's {maxC} x {maxR}");
            return;
        }

        int want = Math.Min(aim, count);
        if (want <= maxC && RowsFor(count, want) <= maxR && plan.Columns != want)
        {
            problems.Add($"{where}: {got}, though {want} per row fits the room's {maxC} x {maxR}");
        }
        else if (plan.Columns > want && RowsFor(count, plan.Columns - 1) <= maxR)
        {
            problems.Add($"{where}: {got} is wider than {want} per row needs, {plan.Columns - 1} columns fit the height");
        }
        else if (plan.Columns < want && plan.Columns != maxC)
        {
            problems.Add($"{where}: {got} is narrower than {want} per row, yet the room holds {maxC}");
        }
    }

    private static void Report(List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new AssertFail($"{problems.Count} bad plans, first: {problems[0]}", "     " + string.Join("\n     ", problems.GetRange(0, Math.Min(8, problems.Count))));
        }
    }

    private static void FitRowSweep()
    {
        var problems = new List<string>();
        int[] wanted = { 1, 2, 3, 5, 7, 8, 10, 19, 30 };
        foreach ((string name, WindowFit.Grid grid) in RowGrids)
        {
            foreach ((float maxW, float maxH) in RowScreens)
            {
                foreach (int gameColumns in new[] { 1, 5, 8 })
                {
                    float gameW = Math.Min(maxW, grid.Width(gameColumns, 1f));
                    float gameH = Math.Min(maxH, grid.Height(4, 1f));
                    foreach (int perRow in wanted)
                    {
                        foreach (int count in RowCounts())
                        {
                            string where = $"{name} on {maxW}x{maxH}, game {gameColumns} cols, {perRow} per row, {count} items";
                            WindowFit.Plan plan;
                            try
                            {
                                plan = WindowFit.For(count, gameColumns, grid, gameW, gameH, maxW, maxH, perRow);
                            }
                            catch (Exception e)
                            {
                                problems.Add($"{where}: threw {e.GetType().Name}");
                                continue;
                            }

                            if (!plan.Grows)
                            {
                                // Only the game's own row length on a list it holds may be left alone
                                if (perRow != gameColumns)
                                {
                                    problems.Add($"{where}: left unchanged though {perRow} per row isn't the game's {gameColumns}");
                                }

                                continue;
                            }

                            CheckShape(problems, where, grid, maxW, maxH, count, perRow, plan);
                        }
                    }
                }
            }
        }

        Report(problems);
    }

    private static void FitAutoSweep()
    {
        var problems = new List<string>();
        foreach ((string name, WindowFit.Grid grid) in RowGrids)
        {
            foreach ((float maxW, float maxH) in RowScreens)
            {
                foreach (int gameColumns in new[] { 1, 5, 8 })
                {
                    float gameW = Math.Min(maxW, grid.Width(gameColumns, 1f));
                    float gameH = Math.Min(maxH, grid.Height(4, 1f));
                    foreach (int count in RowCounts())
                    {
                        string where = $"{name} on {maxW}x{maxH}, game {gameColumns} cols, {count} items";
                        WindowFit.Plan plan = WindowFit.For(count, gameColumns, grid, gameW, gameH, maxW, maxH);
                        if (!plan.Grows)
                        {
                            continue;
                        }

                        int square = Math.Max(gameColumns, (int)Math.Ceiling(Math.Sqrt(count)));
                        CheckShape(problems, where, grid, maxW, maxH, count, square, plan);
                    }
                }
            }
        }

        Report(problems);
    }

    private static void FitRowNegative()
    {
        WindowFit.Plan auto = CommandFor(45);
        WindowFit.Plan negative = CommandFor(45, -3);
        Expect(negative.Grows == auto.Grows && negative.Columns == auto.Columns && negative.Rows == auto.Rows && negative.Scale == auto.Scale,
            $"-3 gave {negative.Columns} x {negative.Rows}, automatic gives {auto.Columns} x {auto.Rows}");
        Expect(!CommandFor(20, -1).Grows, "-1 changed a list the game holds");
    }

    // 10 rows of 82 fit 900, so 45 at one per row needs 5 columns, no more
    private static void FitRowOneTall()
    {
        ExpectShape(CommandFor(45, 1), 5, 9);
        ExpectShape(CommandFor(10, 1), 1, 10);
        ExpectShape(CommandFor(11, 1), 2, 6);
    }

    private static void FitRowFewerThanGame()
    {
        ExpectShape(CommandFor(20, 2), 2, 10);
        ExpectShape(CommandFor(4, 3), 3, 2);
    }

    // The log's 672 window and 544 grid on 1920 x 1080 at 0.9 x 0.85 leave a 1600 x 790 grid
    private static void FitCommandOn1080()
    {
        WindowFit.Plan plan = WindowFit.For(45, 5, Command, 544f, 544f, 1600f, 790f);
        ExpectShape(plan, 7, 7);
        float window = 672f + Math.Max(0f, Command.Height(plan.Rows, plan.Scale) - 544f);
        Expect(window <= 918f, $"window {window} tall, past the 918 the screen allows");
    }

    // 200 don't fit 19 x 9 at full size, 0.95 holds 20 x 10
    private static void FitRowHuge()
    {
        WindowFit.Plan plan = WindowFit.For(200, 5, Command, 544f, 544f, 1600f, 790f, int.MaxValue);
        Expect(plan.Grows && !plan.Overflows && plan.Columns == 20 && plan.Rows == 10 && plan.Scale < 1f,
            $"got {plan.Columns} x {plan.Rows} at {plan.Scale}, overflows {plan.Overflows}");
        Expect(Command.Width(plan.Columns, plan.Scale) <= 1600.5f, $"width {Command.Width(plan.Columns, plan.Scale)} past 1600");
    }
}
