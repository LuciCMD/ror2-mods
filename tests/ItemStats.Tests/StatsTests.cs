using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using JustBeBetter.StatsDisplay;

// Stats Display: the template, the numbers behind the stats, and the combo meter
internal static partial class Program
{
    private static void RunStats()
    {
        Test("Stats text: known names are filled, unknown ones stay as typed and are reported", StatsTemplateFills);
        Test("Stats text: escaped, empty, spaced and unclosed brackets stay as typed", StatsTemplateLeavesOdd);
        Test("Stats text: a filled value isn't read again as a name", StatsTemplateNoRefill);
        Test("Stats numbers: decimals as set, a dot under a comma culture, odd values readable", StatsNumbers);
        Test("Stats numbers: armor reduces damage as the game counts it, negative armor adds to it", StatsArmor);
        Test("Stats numbers: Tougher Times and Safer Spaces match the game's formulas", StatsItems);
        Test("Stats numbers: portals list in order and shrink as more show", StatsPortals);
        Test("Combo: hits inside the window add up, and the combo ends a window after the last", ComboAddsAndEnds);
        Test("Combo: DPS is the window's damage over its length, older hits drop out", ComboDps);
        Test("Combo: kills count per combo, stage and run, bests per run and ever", ComboKills);
        Test("Combo: a new run clears the run's numbers and keeps the all-time bests", ComboNewRun);
        Test("Combo: bad damage values are ignored and bad saved bests start at 0", ComboBadValues);
        Test("Combo: the seconds left round up and read 0 once ended", ComboSecondsLeft);
        Test("Stats names: every documented name and every name in the default texts exists", StatsNamesExist);
        Test("Stats numbers: no stat has its own decimals, all follow Decimal Places", StatsFollowDecimals);
    }

    // Read from the source, LookingGlass fixed dps at one decimal and the author expected it to follow
    private static void StatsFollowDecimals()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        string definitions = File.ReadAllText(Path.Combine(root, "StatsDisplay", "StatDefinitions.cs"));
        foreach (Match fixedPattern in Regex.Matches(definitions, @"StatMath\.Number\([^;]*?,\s*""[^""]*""\)"))
        {
            Expect(false, "fixed decimals in " + fixedPattern.Value);
        }

        Expect(Regex.IsMatch(definitions, @"stats\[""dps""\] = c => Damage\(N\("), "dps doesn't use the set decimals");
    }

    private static string FillWith(string template, List<string>? unknown = null)
    {
        var values = new Dictionary<string, string> { ["damage"] = "12", ["crit"] = "5%", ["echo"] = "[damage]" };
        return StatTemplate.Fill(template, n => values.TryGetValue(n, out string? v) ? v : null, unknown);
    }

    private static void StatsTemplateFills()
    {
        var unknown = new List<string>();
        Equal("<size=115%>Stats</size>\nDamage: 12 Crit: 5%", FillWith("<size=115%>Stats</size>\nDamage: [damage] Crit: [crit]", unknown));
        Equal("[nope] and 12", FillWith("[nope] and [damage]", unknown));
        Expect(unknown.SequenceEqual(new[] { "nope" }), string.Join(",", unknown));
        Equal("1212", FillWith("[damage][damage]"));
        Equal("[12]", FillWith("[[damage]]"));
    }

    private static void StatsTemplateLeavesOdd()
    {
        Equal(string.Empty, FillWith(string.Empty));
        Equal(string.Empty, StatTemplate.Fill(null, _ => "x"));
        Equal("\\[damage]", FillWith("\\[damage]"));
        Equal("[] [a b] [damage", FillWith("[] [a b] [damage"));
        Equal("[", FillWith("["));
        Equal("12]", FillWith("[damage]]"));
    }

    private static void StatsTemplateNoRefill()
    {
        Equal("[damage]", FillWith("[echo]"));
    }

    private static void StatsNumbers()
    {
        Equal("0.##", StatMath.Pattern(2));
        Equal("0", StatMath.Pattern(0));
        Equal("0", StatMath.Pattern(-3));
        Equal("0.####", StatMath.Pattern(99));
        Equal("1.23", StatMath.Number(1.234f, StatMath.Pattern(2)));
        Equal("1.5", StatMath.Number(1.5f, StatMath.Pattern(2)));
        Equal("3", StatMath.Number(3f, StatMath.Pattern(2)));
        Equal("12", StatMath.Number(12.4f, StatMath.Pattern(0)));
        Equal("N/A", StatMath.Number(float.NaN, "0.##"));
        Equal("∞", StatMath.Number(float.PositiveInfinity, "0.##"));
        Equal("-∞", StatMath.Number(float.NegativeInfinity, "0.##"));

        CultureInfo was = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Equal("1.23", StatMath.Number(1.234f, "0.##"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    private static void StatsArmor()
    {
        Expect(Math.Abs(StatMath.ArmorReductionPercent(100f) - 50f) < 0.001f, "100 armor");
        Expect(Math.Abs(StatMath.ArmorReductionPercent(0f)) < 0.001f, "0 armor");
        Expect(Math.Abs(StatMath.ArmorReductionPercent(-100f) + 50f) < 0.001f, $"-100 armor {StatMath.ArmorReductionPercent(-100f)}");
        Expect(Math.Abs(StatMath.EffectiveHealth(100f, 100f) - 200f) < 0.01f, "ehp at 100 armor");
        Expect(Math.Abs(StatMath.EffectiveHealth(150f, -100f) - 100f) < 0.01f, "ehp at -100 armor");
        Expect(StatMath.DamageTaken(float.NaN) == 1f, "NaN armor");
        Expect(!float.IsInfinity(StatMath.EffectiveHealth(100f, -1000000f)), "huge negative armor stays finite");
    }

    private static void StatsItems()
    {
        Expect(Math.Abs(StatMath.BlockChancePercent(1) - 13.0435f) < 0.001f, $"one bear {StatMath.BlockChancePercent(1)}");
        Expect(StatMath.BlockChancePercent(0) == 0f, "no bear");
        Expect(StatMath.BlockChancePercent(-2) == 0f, "negative bear");
        Expect(Math.Abs(StatMath.SaferSpacesSeconds(1) - 13.5f) < 0.001f, $"one safer spaces {StatMath.SaferSpacesSeconds(1)}");
        Expect(Math.Abs(StatMath.SaferSpacesSeconds(2) - 12.15f) < 0.001f, "two safer spaces");
        Expect(float.IsNaN(StatMath.SaferSpacesSeconds(0)), "none held");
    }

    private static void StatsPortals()
    {
        Equal("<style=cStack>None</style>", StatMath.PortalText(StatMath.Portals.None));
        Equal("<size=87%><style=cIsVoid>Void </style></size>", StatMath.PortalText(StatMath.Portals.Void));
        Equal("<size=77%><style=cLunarObjective>Bazaar </style><style=cIsVoid>Void </style></size>",
            StatMath.PortalText(StatMath.Portals.Void | StatMath.Portals.Bazaar));
    }

    private static void ComboAddsAndEnds()
    {
        var meter = new ComboMeter(0f, 0);
        meter.Hit(0f, 5f, 100f, false);
        meter.Hit(4f, 5f, 50f, false);
        Expect(meter.Combo == 150f, $"combo {meter.Combo}");
        Expect(!meter.End(8.9f, 5f), "ended inside the window");
        Expect(meter.End(9f, 5f), "didn't end a window after the last hit");
        Expect(meter.Combo == 0f, "combo kept after ending");
        Expect(!meter.End(20f, 5f), "ended twice");
        Expect(meter.BestCombo == 150f && meter.BestComboThisRun == 150f, "best not kept");

        meter.Hit(30f, 5f, 10f, false);
        Expect(meter.Combo == 10f, $"a hit after the window starts a new combo, {meter.Combo}");
        Expect(meter.BestCombo == 150f, "best lowered");
    }

    private static void ComboDps()
    {
        var meter = new ComboMeter(0f, 0);
        meter.Hit(0f, 5f, 100f, false);
        meter.Hit(3f, 5f, 100f, false);
        Expect(Math.Abs(meter.Dps(4f, 5f) - 40f) < 0.001f, $"at 4 s {meter.Dps(4f, 5f)}");
        Expect(Math.Abs(meter.Dps(6f, 5f) - 20f) < 0.001f, $"at 6 s {meter.Dps(6f, 5f)}");
        Expect(meter.Dps(9f, 5f) == 0f, $"at 9 s {meter.Dps(9f, 5f)}");
        Expect(meter.Dps(9f, 0f) == 0f, "no window");
        Expect(meter.Dps(9f, float.NaN) == 0f, "NaN window");

        // Many small hits leave no crumb once they've all gone
        for (int i = 0; i < 10000; i++)
        {
            meter.Hit(100f + i * 0.001f, 5f, 0.1f + i % 7 * 1234.567f, false);
        }

        Expect(meter.Dps(200f, 5f) == 0f, $"crumb left {meter.Dps(200f, 5f)}");
    }

    private static void ComboKills()
    {
        var meter = new ComboMeter(0f, 2);
        meter.Hit(0f, 5f, 10f, true);
        meter.Hit(1f, 5f, 10f, true);
        meter.Hit(2f, 5f, 10f, false);
        Expect(meter.ComboKills == 2 && meter.KillsThisStage == 2 && meter.KillsThisRun == 2, "first combo");
        Expect(meter.BestKillCombo == 2 && meter.BestKillComboThisRun == 2, "bests after first combo");
        meter.End(10f, 5f);
        Expect(meter.ComboKills == 0, "kill combo kept after ending");

        meter.NewStage();
        meter.Hit(20f, 5f, 10f, true);
        meter.Hit(21f, 5f, 10f, true);
        meter.Hit(22f, 5f, 10f, true);
        Expect(meter.KillsThisStage == 3 && meter.KillsThisRun == 5, $"stage {meter.KillsThisStage} run {meter.KillsThisRun}");
        Expect(meter.BestKillCombo == 3 && meter.BestKillComboThisRun == 3, "bests after second combo");
    }

    private static void ComboNewRun()
    {
        var meter = new ComboMeter(500f, 9);
        meter.Hit(0f, 5f, 800f, true);
        meter.NewRun();
        Expect(meter.Combo == 0f && meter.ComboKills == 0, "combo carried into a new run");
        Expect(meter.BestComboThisRun == 0f && meter.BestKillComboThisRun == 0, "run bests carried");
        Expect(meter.KillsThisRun == 0 && meter.KillsThisStage == 0, "kills carried");
        Expect(meter.Dps(1f, 5f) == 0f, "DPS carried");
        Expect(meter.BestCombo == 800f && meter.BestKillCombo == 9, "all-time bests lost");
        Expect(!meter.End(100f, 5f), "a cleared combo ended");
    }

    private static void ComboBadValues()
    {
        var meter = new ComboMeter(float.NaN, -4);
        Expect(meter.BestCombo == 0f && meter.BestKillCombo == 0, "bad saved bests");
        meter.Hit(0f, 5f, float.NaN, false);
        meter.Hit(0f, 5f, -50f, false);
        meter.Hit(0f, 5f, float.PositiveInfinity, false);
        Expect(meter.Combo == 0f && meter.Dps(1f, 5f) == 0f, $"combo {meter.Combo}");
        Expect(new ComboMeter(float.PositiveInfinity, 0).BestCombo == 0f, "infinite saved best");
    }

    private static void ComboSecondsLeft()
    {
        var meter = new ComboMeter(0f, 0);
        Expect(meter.SecondsLeft(0f, 5f) == 0, "no combo");
        meter.Hit(0f, 5f, 10f, false);
        Expect(meter.SecondsLeft(0f, 5f) == 5, $"on the hit {meter.SecondsLeft(0f, 5f)}");
        Expect(meter.SecondsLeft(1f, 5f) == 4, $"at 1 s {meter.SecondsLeft(1f, 5f)}");
        Expect(meter.SecondsLeft(0.5f, 5f) == 5, $"at 0.5 s {meter.SecondsLeft(0.5f, 5f)}");
        Expect(meter.SecondsLeft(4.9f, 5f) == 1, $"at 4.9 s {meter.SecondsLeft(4.9f, 5f)}");
        Expect(meter.SecondsLeft(5f, 5f) == 0, "at 5 s");
    }

    // Read from the source, as the table needs a live body to run
    private static void StatsNamesExist()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        string definitions = File.ReadAllText(Path.Combine(root, "StatsDisplay", "StatDefinitions.cs"));
        var known = new HashSet<string>(Regex.Matches(definitions, @"stats\[""(\w+)""\] =").Select(m => m.Groups[1].Value));
        Expect(known.Count >= 60, $"only {known.Count} stats found");

        string settings = File.ReadAllText(Path.Combine(root, "StatsDisplay", "StatsDisplaySettings.cs"));
        Match list = Regex.Match(settings, @"Names: (.*?)\.""", RegexOptions.Singleline);
        Expect(list.Success, "the documented names weren't found");
        string documented = Regex.Replace(list.Groups[1].Value, @"""\s*\+\s*""", string.Empty);
        foreach (string name in documented.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
        {
            Expect(known.Contains(name), $"documented but missing: {name}");
        }

        foreach (string text in new[] { StatsDisplaySettingsTexts.Main, StatsDisplaySettingsTexts.Tab })
        {
            var unknown = new List<string>();
            StatTemplate.Fill(text, n => known.Contains(n) ? "x" : null, unknown);
            Expect(unknown.Count == 0, "default text uses " + string.Join(", ", unknown));
        }
    }

    // The defaults as written in the settings, read from the source as the settings class needs BepInEx
    private static class StatsDisplaySettingsTexts
    {
        internal static readonly string Main = ReadDefault("DefaultMainText");
        internal static readonly string Tab = ReadDefault("DefaultTabText");

        private static string ReadDefault(string name)
        {
            string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .First(a => a.Key == "SourceRoot").Value!;
            string settings = File.ReadAllText(Path.Combine(root, "StatsDisplay", "StatsDisplaySettings.cs"));
            Match m = Regex.Match(settings, name + @" =\s*(.*?);", RegexOptions.Singleline);
            Expect(m.Success, name + " not found");
            string joined = string.Concat(Regex.Matches(m.Groups[1].Value, @"""((?:[^""\\]|\\.)*)""").Select(s => s.Groups[1].Value));
            return Regex.Unescape(joined);
        }
    }
}
