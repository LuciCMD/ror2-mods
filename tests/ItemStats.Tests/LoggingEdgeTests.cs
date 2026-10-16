using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using JustBeBetter.ItemStats;
using JustBeBetter.Shared;
using RoR2;
using Path = System.IO.Path;

// Ways the Verbose Logging switch could lose work or a line, the parts needing the game read from the source
internal static partial class Program
{
    private static void RunLoggingEdge()
    {
        Test("Logging edge: a null switch is refused and the one before stays", LogEdgeFollowNull);
        Test("Logging edge: a modded item's stats still show with Verbose Logging off, and it isn't marked as logged", LogEdgeModdedOffNotMarked);
        Test("Logging edge: the plugin binds Verbose Logging off by default", LogEdgePluginDefaultOff);
        Test("Logging edge: Verbose Logging in Risk Of Options is live and never greyed", LogEdgeRooLive);
        Test("Logging edge: the plugin follows the switch before its first line and before any part starts", LogEdgeFollowFirst);
        Test("Logging edge: a once-per-kind set guarding only verbose lines is marked only while Verbose Logging is on", LogEdgeOnceSetsGated);
        Test("Logging edge: a stage load's state resets whether or not its report is written", LogEdgeStageResetsOutsideReport);
        Test("Logging edge: the startup time line is written before the breakdown is cut off", LogEdgeStartupLineFirst);
        Test("Logging edge: save and load areas run their work outside any switch or null check", LogEdgeAreasAlwaysRun);
    }

    private static void LogEdgeFollowNull()
    {
        var log = new ManualLogSource();
        WithVerbose(() => true, () =>
        {
            bool threw = false;
            try
            {
                Logs.Follow(null!);
            }
            catch (ArgumentNullException)
            {
                threw = true;
            }

            Expect(threw, "Follow(null) didn't throw");
            log.Verbose("still on");
        });
        Expect(log.Lines.SequenceEqual(new[] { "INFO still on" }), string.Join(" | ", log.Lines));
    }

    private static readonly FieldInfo ItemStatLogF = typeof(ItemStatText).GetField("log", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ModdedLoggedF = typeof(ItemStatText).GetField("moddedLogged", BindingFlags.Static | BindingFlags.NonPublic)!;

    // Off, nothing is written and the item isn't marked, so a later switch on still reports it
    // The on half needs ItemDef.name, an engine call, so it's left to the game
    private static void LogEdgeModdedOffNotMarked()
    {
        const int Key = 90077;
        object? before = ItemStatLogF.GetValue(null);
        var marked = (HashSet<int>)ModdedLoggedF.GetValue(null)!;
        var log = new ManualLogSource();
        ItemStatLogF.SetValue(null, log);
        try
        {
            ItemDef item = FakeItem(Key, "Gain 10% (+10% per stack) attack speed.");
            string s = string.Empty;
            WithVerbose(() => false, () =>
            {
                var sb = new StringBuilder();
                AppendStatsM.Invoke(null, new object[] { sb, item, Master(0), 1, 1, " → ", false });
                s = sb.ToString();
            });
            Contains("Attack Speed: </color>10%", s);
            Expect(!marked.Contains(Key), "the item was marked as logged with Verbose Logging off");
            Expect(!BrokenStats().Contains(Key), "the item was marked broken");
            Expect(log.Lines.Count == 0, string.Join(" | ", log.Lines));
        }
        finally
        {
            marked.Remove(Key);
            BrokenStats().Remove(Key);
            ItemStatLogF.SetValue(null, before);
        }
    }

    private static string LogEdgeRead(params string[] parts)
    {
        return File.ReadAllText(Path.Combine(new[] { LogSourceRoot() }.Concat(parts).ToArray()));
    }

    private static void LogEdgePluginDefaultOff()
    {
        string settings = LogEdgeRead("Settings.cs");
        Expect(Regex.IsMatch(settings, @"Bind\(\s*GeneralSection,\s*""Verbose Logging"",\s*false\s*,"), "Settings doesn't bind Verbose Logging with false as its default");
    }

    private static void LogEdgeRooLive()
    {
        string roo = LogEdgeRead("RiskOfOptionsSupport.cs");
        Match call = Regex.Match(roo, @"\.CheckBox\(\s*settings\.VerboseLogging\s*(?<rest>[^;]*)\);");
        Expect(call.Success, "no CheckBox for settings.VerboseLogging");
        string rest = call.Groups["rest"].Value.Trim();
        Expect(rest.Length == 0, $"the Verbose Logging checkbox takes more than the entry, restart or greying: [{rest}]");

        // Part sets the owner every CheckBox after it greys with, so Verbose Logging has to come before any Part on its page
        int verbose = call.Index;
        int page = roo.LastIndexOf("new Page(", verbose, StringComparison.Ordinal);
        int part = roo.IndexOf(".Part(", page, StringComparison.Ordinal);
        Expect(page >= 0 && (part < 0 || part > verbose), "a Part switch comes before Verbose Logging on its page, so it greys with that part");
    }

    private static void LogEdgeFollowFirst()
    {
        string plugin = LogEdgeRead("JustBeBetterPlugin.cs");
        int awake = plugin.IndexOf("void Awake()", StringComparison.Ordinal);
        int follow = plugin.IndexOf("Logs.Follow(", awake, StringComparison.Ordinal);
        int firstLine = Regex.Match(plugin.Substring(awake), @"\.(Verbose|Info|LogWarning|LogError)\(").Index + awake;
        int firstPart = plugin.IndexOf("StartPart(", awake, StringComparison.Ordinal);
        int roo = plugin.IndexOf("RiskOfOptionsSupport.Register(", awake, StringComparison.Ordinal);
        Expect(follow > awake, "Awake never calls Logs.Follow");
        Expect(follow < firstLine, "a line is written in Awake before Logs.Follow");
        Expect(follow < firstPart, "a part starts before Logs.Follow, its first lines ignore the switch");
        Expect(roo < 0 || follow < roo, "Risk Of Options registers before Logs.Follow");
    }

    private static readonly Regex EdgeMethodStart = new Regex(@"^\s*(private|internal|public|protected)\b[^=;]*\(", RegexOptions.Compiled);
    private static readonly Regex SetAddInIf = new Regex(@"^\s*(else\s+)?if\s*\(.*\b\w+\.Add\(", RegexOptions.Compiled);
    private static readonly Regex FlagInIf = new Regex(@"^\s*(else\s+)?if\s*\(.*!(?<flag>\w+)\b(?!\.)", RegexOptions.Compiled);
    private static readonly Regex AnyLog = new Regex(@"\.(Verbose|Info|LogWarning|LogError)\(", RegexOptions.Compiled);

    // A set or flag that only stops a verbose line repeating, marked with the switch off, keeps that line out once it's on
    private static void LogEdgeOnceSetsGated()
    {
        string root = LogSourceRoot();
        var wrong = new List<string>();
        int checkedGuards = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                bool setGuard = SetAddInIf.IsMatch(line);
                Match flag = FlagInIf.Match(line);
                bool flagGuard = !setGuard && flag.Success && i + 2 < lines.Length
                    && Regex.IsMatch(lines[i + 2], @"^\s*" + Regex.Escape(flag.Groups["flag"].Value) + @"\s*=\s*true;");
                if (!setGuard && !flagGuard)
                {
                    continue;
                }

                List<string> guarded = Guarded(lines, i);
                List<string> logs = guarded.SelectMany(l => AnyLog.Matches(l).Select(m => m.Groups[1].Value)).ToList();
                if (logs.Count == 0 || logs.Any(l => l != "Verbose"))
                {
                    continue;
                }

                checkedGuards++;
                if (line.Contains("Logs.VerboseOn", StringComparison.Ordinal) || EarlierOffReturn(lines, i))
                {
                    continue;
                }

                wrong.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Expect(checkedGuards >= 12,$"only {checkedGuards} once-guards found, the scan no longer reads the source");
        Expect(wrong.Count == 0, "marked with Verbose Logging off:\n     " + string.Join("\n     ", wrong));
    }

    // The block an if guards, or for a guard that returns, the rest of its method
    private static List<string> Guarded(string[] lines, int at)
    {
        int indent = lines[at].Length - lines[at].TrimStart().Length;
        // A condition can run over several lines, the block starts at the next lone brace
        int end = at;
        while (end + 1 < lines.Length && end < at + 4 && lines[end + 1].Trim() != "{")
        {
            end++;
        }

        var block = new List<string>();
        int i = end + 1;
        if (i < lines.Length && lines[i].Trim() == "{")
        {
            for (i++; i < lines.Length; i++)
            {
                int ind = lines[i].Length - lines[i].TrimStart().Length;
                if (lines[i].Trim() == "}" && ind == indent)
                {
                    break;
                }

                block.Add(lines[i]);
            }
        }

        if (block.Count > 0 && block.All(l => l.Trim().Length == 0 || l.Trim() == "return;"))
        {
            block.Clear();
            for (i++; i < lines.Length && !EdgeMethodStart.IsMatch(lines[i]); i++)
            {
                block.Add(lines[i]);
            }
        }

        return block;
    }

    private static bool EarlierOffReturn(string[] lines, int at)
    {
        for (int i = at - 1; i >= 0; i--)
        {
            if (lines[i].Contains("!Logs.VerboseOn", StringComparison.Ordinal))
            {
                return true;
            }

            if (EdgeMethodStart.IsMatch(lines[i]))
            {
                return false;
            }
        }

        return false;
    }

    private static string MethodBody(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        Expect(start >= 0, $"no {signature}");
        int open = text.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] == '{' ? 1 : text[i] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return text.Substring(open, i - open + 1);
            }
        }

        return text.Substring(open);
    }

    // Removes every if (Logs.VerboseOn) block, what's left runs with the switch off
    private static string WithoutVerboseBlocks(string body)
    {
        while (true)
        {
            Match m = Regex.Match(body, @"if\s*\(\s*Logs\.VerboseOn\s*\)\s*\{");
            if (!m.Success)
            {
                return body;
            }

            int depth = 0;
            int i = m.Index + m.Length - 1;
            for (; i < body.Length; i++)
            {
                depth += body[i] == '{' ? 1 : body[i] == '}' ? -1 : 0;
                if (depth == 0)
                {
                    break;
                }
            }

            body = body.Remove(m.Index, i - m.Index + 1);
        }
    }

    private static void LogEdgeStageResetsOutsideReport()
    {
        string finish = MethodBody(LogEdgeRead("Loading", "StageWatcher.cs"), "private void Finish(");
        string off = WithoutVerboseBlocks(finish);
        foreach (string reset in new[] { "marks.Clear()", "loadStart = 0", "loadDone = 0", "Timeline.Freezes.RemoveAll(", "Timeline.Step(\"playing\")" })
        {
            Expect(off.Contains(reset, StringComparison.Ordinal), $"{reset} only runs with Verbose Logging on");
        }

        Expect(!Regex.IsMatch(off, @"\breturn\b"), "Finish can return before resetting");
    }

    private static void LogEdgeStartupLineFirst()
    {
        string write = MethodBody(LogEdgeRead("Loading", "StartupReport.cs"), "internal static void Write(");
        Match line = Regex.Match(write, @"log\.Info\(\$""\[Report\] Launch to main menu");
        int cut = write.IndexOf("!Logs.VerboseOn", StringComparison.Ordinal);
        Expect(line.Success, "the launch to main menu line isn't written with Info");
        Expect(cut < 0 || line.Index < cut, "the launch to main menu line comes after the Verbose Logging return");
    }

    private static void LogEdgeAreasAlwaysRun()
    {
        foreach ((string file, string work) in new[] { ("Capture.cs", "capture()"), ("Restore.cs", "apply()") })
        {
            string area = MethodBody(LogEdgeRead("Saves", file), "private void Area(");
            Expect(area.Contains(work, StringComparison.Ordinal), $"{file}: Area never calls {work}");
            Expect(!area.Contains("VerboseOn", StringComparison.Ordinal), $"{file}: Area reads the switch, so {work} can be skipped");
            Expect(!area.Contains("?.Verbose(", StringComparison.Ordinal), $"{file}: Area writes with ?.Verbose, a null log would skip {work}");
        }
    }
}
