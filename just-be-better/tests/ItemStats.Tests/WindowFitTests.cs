using System;
using JustBeBetter.Pickers;

// The picker window's fit, in the grid's own units, with a made-up grid of 80 px buttons
// 8 columns by 4 rows as the game's, and room for 1600 x 900 on the screen
internal static partial class Program
{
    private static readonly WindowFit.Grid Shape = new WindowFit.Grid(80f, 80f, 8f, 8f, 0f, 0f);
    private const float GameWidth = 696f;
    private const float GameHeight = 344f;
    private const float MaxWidth = 1600f;
    private const float MaxHeight = 900f;

    private static void RunWindowFit()
    {
        Test("Picker window: a list the game's grid holds is left alone", FitLeavesSmallList);
        Test("Picker window: a longer list keeps at least the game's 8 columns", FitGrowsDown);
        Test("Picker window: past the screen's height it widens, buttons kept full size", FitWidens);
        Test("Picker window: past the whole screen the buttons shrink, still fitting", FitShrinks);
        Test("Picker window: too many even at the smallest size widens fully and says so", FitOverflows);
        Test("Picker window: padding counts against the room", FitCountsPadding);
        Test("Picker window: no items or no columns change nothing", FitEmpty);
        Test("Picker window: the Command menu's 45 items make a 7 x 7 square", FitCommandSquare);
        Test("Picker window: Items per Row sets the row length", FitRowLength);
        Test("Picker window: Items per Row applies even to a list the game holds", FitRowLengthSmallList);
        Test("Picker window: Items per Row equal to the game's leaves it alone", FitRowLengthAsGame);
        Test("Picker window: Items per Row stops at the screen's width", FitRowLengthCapped);
        Test("Picker window: Items per Row past the item count makes one row", FitRowLengthPastCount);
        RunWindowFitEdges();
    }

    private static WindowFit.Plan FitFor(int count, WindowFit.Grid? shape = null)
    {
        return WindowFit.For(count, 8, shape ?? Shape, GameWidth, GameHeight, MaxWidth, MaxHeight);
    }

    private static void Expect(bool condition, string what)
    {
        if (!condition)
        {
            throw new AssertFail(what);
        }
    }

    private static void ExpectInside(WindowFit.Plan plan, WindowFit.Grid shape, int count)
    {
        Expect((long)plan.Columns * plan.Rows >= count, $"{plan.Columns} x {plan.Rows} holds fewer than {count}");
        Expect(shape.Width(plan.Columns, plan.Scale) <= MaxWidth, $"width {shape.Width(plan.Columns, plan.Scale)} past {MaxWidth}");
        Expect(shape.Height(plan.Rows, plan.Scale) <= MaxHeight, $"height {shape.Height(plan.Rows, plan.Scale)} past {MaxHeight}");
    }

    private static void FitLeavesSmallList()
    {
        Expect(!FitFor(32).Grows, "32 items fill the game's 8 x 4 exactly");
    }

    private static void FitGrowsDown()
    {
        WindowFit.Plan plan = FitFor(40);
        Expect(plan.Grows && plan.Columns == 8 && plan.Rows == 5 && plan.Scale == 1f, $"got {plan.Columns} x {plan.Rows} at {plan.Scale}");
        ExpectInside(plan, Shape, 40);
    }

    // 10 rows fit the height, so 150 needs 15 columns
    private static void FitWidens()
    {
        WindowFit.Plan plan = FitFor(150);
        Expect(plan.Columns == 15 && plan.Rows == 10 && plan.Scale == 1f, $"got {plan.Columns} x {plan.Rows} at {plan.Scale}");
        ExpectInside(plan, Shape, 150);
    }

    // At full size the screen holds 18 x 10, so 400 has to shrink, and only as far as needed
    private static void FitShrinks()
    {
        WindowFit.Plan plan = FitFor(400);
        Expect(plan.Grows && !plan.Overflows && plan.Scale < 1f, $"got {plan.Columns} x {plan.Rows} at {plan.Scale}");
        ExpectInside(plan, Shape, 400);
        float larger = plan.Scale + 0.05f;
        int columns = Shape.Fit(MaxWidth, larger, vertical: false);
        int rows = Shape.Fit(MaxHeight, larger, vertical: true);
        Expect((long)columns * rows < 400, $"{larger} would have held them too");
    }

    private static void FitOverflows()
    {
        WindowFit.Plan plan = FitFor(100000);
        Expect(plan.Overflows && plan.Scale == WindowFit.MinScale, $"got {plan.Scale}, overflows {plan.Overflows}");
        Expect(Shape.Width(plan.Columns, plan.Scale) <= MaxWidth, "widest grid past the screen");
        Expect(Shape.Width(plan.Columns + 1, plan.Scale) > MaxWidth, "not the widest grid");
    }

    private static void FitCountsPadding()
    {
        var padded = new WindowFit.Grid(80f, 80f, 8f, 8f, 40f, 40f);
        WindowFit.Plan plan = FitFor(150, padded);
        ExpectInside(plan, padded, 150);
        Expect(padded.Fit(MaxWidth, 1f, vertical: false) == 17, $"padding should cost a column, got {padded.Fit(MaxWidth, 1f, vertical: false)}");
    }

    private static void FitEmpty()
    {
        Expect(!FitFor(0).Grows, "no items grew the window");
        Expect(!WindowFit.For(40, 0, Shape, GameWidth, GameHeight, MaxWidth, MaxHeight).Grows, "no columns grew the window");
    }

    // The Command panel as the game builds it, from the log: 5 columns of 80 px, spacing 2, padding 8 x 10, 544 square
    private static readonly WindowFit.Grid Command = new WindowFit.Grid(80f, 80f, 2f, 2f, 8f, 10f);

    private static WindowFit.Plan CommandFor(int count, int itemsPerRow = 0)
    {
        return WindowFit.For(count, 5, Command, 544f, 544f, MaxWidth, MaxHeight, itemsPerRow);
    }

    private static void ExpectShape(WindowFit.Plan plan, int columns, int rows)
    {
        Expect(plan.Grows && plan.Columns == columns && plan.Rows == rows && plan.Scale == 1f,
            $"expected {columns} x {rows} at full size, got {plan.Columns} x {plan.Rows} at {plan.Scale}, grows {plan.Grows}");
    }

    private static void FitCommandSquare()
    {
        ExpectShape(CommandFor(45), 7, 7);
        Expect(!CommandFor(30).Grows, "30 fit the game's 5 x 6");
    }

    private static void FitRowLength()
    {
        ExpectShape(CommandFor(45, 9), 9, 5);
    }

    private static void FitRowLengthSmallList()
    {
        ExpectShape(CommandFor(20, 10), 10, 2);
    }

    private static void FitRowLengthAsGame()
    {
        Expect(!CommandFor(20, 5).Grows, "asking for the game's own 5 changed a list it holds");
    }

    // 19 columns of 82 fit 1600 with the padding
    private static void FitRowLengthCapped()
    {
        ExpectShape(CommandFor(60, 30), 19, 4);
    }

    private static void FitRowLengthPastCount()
    {
        ExpectShape(CommandFor(3, 10), 3, 1);
    }
}
