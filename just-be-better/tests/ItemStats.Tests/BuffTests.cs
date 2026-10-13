using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using JustBeBetter.BuffInfo;

// Buff Info: the text on a buff icon, and the ported descriptions' tags
internal static partial class Program
{
    private static void RunBuffs()
    {
        Test("Buff text: the stack count reads as the game's, x3, 30% or nothing", BuffStackText);
        Test("Buff text: a timer reads 1.4 to a tenth, floored, or 2 rounded up in whole seconds", BuffSeconds);
        Test("Buff text: a timer sits above the stack count, alone it keeps a blank line under it", BuffTimedText);
        Test("Buff text: odd timers read 0, and huge ones stay short", BuffOddSeconds);
        Test("Buff text: of several entries for one buff the longest time left wins", BuffLongest);
        Test("Buff text: an undescribed buff's internal name reads as words", BuffTidy);
        Test("Buff text: every ported description opens and closes its style tags in pairs", BuffTagsBalanced);
    }

    private static void BuffStackText()
    {
        Equal("x3", BuffText.Stack(true, false, 3, 0));
        Equal("30%", BuffText.Stack(true, true, 30, 0));
        Equal("x4", BuffText.Stack(true, false, 3, 1));
        Equal(string.Empty, BuffText.Stack(false, false, 3, 0));
    }

    private static void BuffSeconds()
    {
        Equal("1.4", BuffText.Seconds(1.47f, true));
        Equal("2", BuffText.Seconds(1.47f, false));
        Equal("0.0", BuffText.Seconds(0.04f, true));
        Equal("1", BuffText.Seconds(0.04f, false));
        Equal("3", BuffText.Seconds(3f, false));
        Equal("0.7", BuffText.Seconds(0.7f, true));
        Equal("2.3", BuffText.Seconds(2.3f, true));
    }

    private static void BuffTimedText()
    {
        Equal("<size=80%>1.4</size>\nx3", BuffText.Timed(1.47f, true, "x3"));
        Equal("<size=80%>2</size>\n ", BuffText.Timed(1.47f, false, string.Empty));
    }

    private static void BuffOddSeconds()
    {
        Equal("0.0", BuffText.Seconds(float.NaN, true));
        Equal("0", BuffText.Seconds(-3f, false));
        Expect(BuffText.Seconds(float.PositiveInfinity, true).Length <= 7, "infinite stays short");
        Expect(BuffText.Seconds(1e30f, false).Length <= 5, "huge stays short");
    }

    private static void BuffLongest()
    {
        var longest = BuffText.Longest(new[] { (5, 1f), (5, 4.5f), (5, 2f), (7, 0.5f), (7, float.NaN) });
        Expect(longest[5] == 4.5f, $"buff 5 {longest[5]}");
        Expect(longest[7] == 0.5f, $"buff 7 {longest[7]}");
        Expect(BuffText.Longest(new (int, float)[0]).Count == 0, "none");
    }

    private static void BuffTidy()
    {
        Equal("Armor Boost", BuffText.Tidy("bdArmorBoost"));
        Equal("Elite Haunted", BuffText.Tidy("EliteHaunted"));
        Equal("HP Regen Boost", BuffText.Tidy("HPRegenBoost"));
        Equal("my buff", BuffText.Tidy("my_buff"));
        Equal("bd", BuffText.Tidy("bd"));
        Equal("Unknown Buff", BuffText.Tidy(null));
    }

    // Read from the source, as the table only fills once the game's buffs exist
    private static void BuffTagsBalanced()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        string[] lines = File.ReadAllLines(Path.Combine(root, "BuffInfo", "BuffDefinitions.cs"));
        var opens = new Regex(@"\{(utility|damage|healing|health|void|shrine)String\}|<style=c\w+>", RegexOptions.Compiled);
        var closes = new Regex(@"\{styleString\}|</style>", RegexOptions.Compiled);
        int entries = 0;
        foreach (string line in lines.Where(l => l.TrimStart().StartsWith("Add(", System.StringComparison.Ordinal)))
        {
            entries++;
            int open = opens.Matches(line).Count;
            int close = closes.Matches(line).Count;
            Expect(open == close, $"{open} opened, {close} closed in {line.Trim()}");
            Expect(!line.Contains("<style=\\\"", System.StringComparison.Ordinal), $"stray quote in {line.Trim()}");
        }

        Expect(entries >= 130, $"only {entries} entries found");
    }
}
