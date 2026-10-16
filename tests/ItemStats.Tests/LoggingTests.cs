using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using JustBeBetter.Shared;

// The Verbose Logging switch, through Logs, the one place Info lines are written
internal static partial class Program
{
    private static readonly Regex RawInfo = new Regex(
        @"\bLog(Info|Debug|Message)\s*\(|\.Log\s*\(\s*(BepInEx\.Logging\.)?LogLevel",
        RegexOptions.Compiled);

    private static void RunLogging()
    {
        Test("Logging: no part writes Info or Debug around Logs, so the switch reaches every line", LogNothingAround);
        Test("Logging: with Verbose Logging off only Info lines are written", LogVerboseOff);
        Test("Logging: with Verbose Logging on both are written as Info", LogVerboseOn);
        Test("Logging: the switch takes effect on the next line, no restart", LogSwitchLive);
        Test("Logging: a verbose message is still built with the switch off, so a save area's capture runs", LogMessageBuiltWhenOff);
        Test("Logging: the patcher reads Verbose Logging from the section and name the plugin binds", LogPatcherSameSetting);
        Test("Logging: a once-per-kind set guarding a Verbose line marks only while Verbose Logging is on", LogOnceSetsFollowSwitch);
    }

    private static readonly Regex OnceGuard = new Regex(@"^(\s*)(?:else\s+)?if \((.*\b\w+\.Add\(.*)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex MethodStart = new Regex(@"^        (?:private|internal|public|protected)\b.*\(", RegexOptions.Compiled);

    // A kind marked while off would never log once the switch is turned on mid-session
    // The switch may be checked in the guard itself or earlier in the same method
    private static void LogOnceSetsFollowSwitch()
    {
        string root = LogSourceRoot();
        var found = new List<string>();
        int guards = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = OnceGuard.Match(lines[i]);
                if (!m.Success)
                {
                    continue;
                }

                string indent = m.Groups[1].Value;
                string guard = m.Groups[2].Value;
                bool earlyOut = guard.Contains("!", StringComparison.Ordinal) && i + 2 < lines.Length && lines[i + 2].Trim() == "return;";
                // An early out guards the rest of the method, a block only itself
                string end = earlyOut ? indent.Substring(Math.Min(4, indent.Length)) + "}" : indent + "}";
                int stop = Array.FindIndex(lines, i + 2, l => l == end);
                string region = string.Join("\n", lines.Skip(i + 1).Take((stop < 0 ? lines.Length : stop) - i));
                if (!region.Contains(".Verbose(", StringComparison.Ordinal))
                {
                    continue;
                }

                guards++;
                int start = Array.FindLastIndex(lines, i, l => MethodStart.IsMatch(l));
                string before = string.Join("\n", lines.Skip(start).Take(i - start));
                if (!guard.Contains("Logs.VerboseOn", StringComparison.Ordinal) && !before.Contains("Logs.VerboseOn", StringComparison.Ordinal))
                {
                    found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Expect(guards >= 8, $"only {guards} once-per-kind guards found, the scan no longer reads the source");
        Expect(found.Count == 0, "marked with the switch off:\n     " + string.Join("\n     ", found));
    }

    private static string LogSourceRoot()
    {
        return typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
    }

    private static void LogNothingAround()
    {
        string root = LogSourceRoot();
        string helper = Path.GetFullPath(Path.Combine(root, "Shared", "Logs.cs"));
        var found = new List<string>();
        int verbose = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            verbose += lines.Count(l => l.Contains(".Verbose(", StringComparison.Ordinal));
            if (Path.GetFullPath(file) == helper)
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) && RawInfo.IsMatch(lines[i]))
                {
                    found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Expect(verbose >= 60, $"only {verbose} Verbose calls found, the scan no longer reads the source");
        Expect(found.Count == 0, "written around Logs:\n     " + string.Join("\n     ", found));
    }

    private static void WithVerbose(Func<bool> on, Action body)
    {
        Logs.Follow(on);
        try
        {
            body();
        }
        finally
        {
            Logs.Follow(() => false);
        }
    }

    private static void LogVerboseOff()
    {
        var log = new ManualLogSource();
        WithVerbose(() => false, () =>
        {
            log.Verbose("first use");
            log.Info("saves are here");
        });
        Expect(log.Lines.SequenceEqual(new[] { "INFO saves are here" }), string.Join(" | ", log.Lines));
    }

    private static void LogVerboseOn()
    {
        var log = new ManualLogSource();
        WithVerbose(() => true, () =>
        {
            log.Verbose("first use");
            log.Info("saves are here");
        });
        Expect(log.Lines.SequenceEqual(new[] { "INFO first use", "INFO saves are here" }), string.Join(" | ", log.Lines));
    }

    private static void LogSwitchLive()
    {
        var log = new ManualLogSource();
        bool on = false;
        WithVerbose(() => on, () =>
        {
            log.Verbose("a");
            on = true;
            log.Verbose("b");
            on = false;
            log.Verbose("c");
        });
        Expect(log.Lines.SequenceEqual(new[] { "INFO b" }), string.Join(" | ", log.Lines));
    }

    private static void LogMessageBuiltWhenOff()
    {
        var log = new ManualLogSource();
        int captured = 0;
        WithVerbose(() => false, () => log.Verbose($"[Save] Run: {++captured}"));
        Expect(captured == 1, $"the capture ran {captured} times");
        Expect(log.Lines.Count == 0, string.Join(" | ", log.Lines));
    }

    private static void LogPatcherSameSetting()
    {
        string root = LogSourceRoot();
        string settings = File.ReadAllText(Path.Combine(root, "Settings.cs"));
        string patcher = File.ReadAllText(Path.Combine(root, "..", "patcher", "JustBeBetterPatcher.cs"));
        Expect(settings.Contains("GeneralSection = \"General\"", StringComparison.Ordinal), "Settings no longer names its General section \"General\"");
        Expect(Regex.IsMatch(settings, @"Bind\(\s*GeneralSection,\s*""Verbose Logging"""), "Settings no longer binds Verbose Logging in GeneralSection");
        Expect(patcher.Contains("GeneralSection = \"General\"", StringComparison.Ordinal), "the patcher no longer reads the \"General\" section");
        Expect(patcher.Contains("Bind(GeneralSection, \"Verbose Logging\", false", StringComparison.Ordinal), "the patcher no longer reads Verbose Logging");
    }
}
