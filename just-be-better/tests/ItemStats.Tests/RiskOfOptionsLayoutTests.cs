using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// The Risk Of Options layout, read from the source as RiskOfOptionsSupport needs Risk Of Options, BepInEx and the game
// Identifiers follow ModSettingsManager.AddOption in Risk Of Options 2.8.6, guid.category.name.type with spaces as
// underscores and upper case, and Category.AddOption throws on a repeat, which would stop every part in Awake
internal static partial class Program
{
    private static readonly string[] RooTabs = { "General", "Item Pickers", "Items", "HUD", "Chests" };

    private static void RunRiskOfOptionsLayout()
    {
        Test("Risk Of Options: no two options in one tab share an identifier", RooNoIdentifierCollision);
        Test("Risk Of Options: every config setting outside Debug is registered exactly once", RooNothingDropped);
        Test("Risk Of Options: tabs come in the planned order", RooTabOrder);
        Test("Risk Of Options: every option sits after a part switch, in its own part", RooOptionsUnderOwnPart);
        Test("Risk Of Options: heading and choice labels are written after AddOption registers its own text", RooTextAfterAdd);
        Test("Risk Of Options: Cap Chance isn't greyed by Item Info, Skill Info's item procs read it with Item Info off", RooCapChanceNotGreyedByItemInfo);
    }

    private sealed class RooBinding
    {
        internal RooBinding(string cls, string prop, string section, string key)
        {
            Class = cls;
            Prop = prop;
            Section = section;
            Key = key;
        }

        internal string Class { get; }

        internal string Prop { get; }

        internal string Section { get; }

        internal string Key { get; }
    }

    private sealed class RooOption
    {
        internal RooOption(string tab, string method, RooBinding binding, string? nameOverride, RooBinding? owner, string call)
        {
            Tab = tab;
            Method = method;
            Binding = binding;
            NameOverride = nameOverride;
            Owner = owner;
            Call = call;
        }

        internal string Tab { get; }

        internal string Method { get; }

        internal RooBinding Binding { get; }

        internal string? NameOverride { get; }

        // The part switch the option greys with, null for a part switch itself
        internal RooBinding? Owner { get; }

        internal string Call { get; }

        internal string Name => NameOverride ?? (Method == "Part" ? Binding.Section : Binding.Key);

        internal string Type => Method switch
        {
            "Part" or "CheckBox" => "checkbox",
            "Choice" => "choice",
            "IntSlider" or "Percent" => "int_slider",
            "Seconds" => "step_slider",
            "Colour" => "color",
            "Text" => "string_input_field",
            _ => throw new InvalidOperationException("unknown page method " + Method),
        };

        internal string Identifier => ("mephit.Just_Be_Better." + Tab + "." + Name + "." + Type).Replace(" ", "_").ToUpper();
    }

    private static string RooSourceRoot()
    {
        return typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "SourceRoot").Value!;
    }

    // Class name to property to binding, plus Settings' own properties and the chest kind groups
    private static (Dictionary<string, Dictionary<string, RooBinding>> Classes, Dictionary<string, string> SettingsProps, List<RooBinding> All) RooBindings()
    {
        string root = RooSourceRoot();
        var classes = new Dictionary<string, Dictionary<string, RooBinding>>();
        var all = new List<RooBinding>();
        var settingsProps = new Dictionary<string, string>();
        var bindEnabled = new Regex(@"(\w+)\s*=\s*(?:Settings\.)?BindEnabled\(\s*config,\s*(\w+)", RegexOptions.Compiled);
        var bind = new Regex(@"(\w+)\s*=\s*config\.Bind(?:<[^>]+>)?\(\s*(\w+|""[^""]*""),\s*""([^""]*)""", RegexOptions.Compiled);
        var grouped = new Regex(@"Bind(Outline|Hide)\(\s*config,\s*[\w.]+,\s*""([^""]*)""", RegexOptions.Compiled);
        var constant = new Regex(@"const string (\w+) = ""([^""]*)""", RegexOptions.Compiled);
        var cls = new Regex(@"class (\w+Settings|Settings)\b", RegexOptions.Compiled);
        foreach (string file in Directory.EnumerateFiles(root, "*Settings.cs", SearchOption.AllDirectories).Append(Path.Combine(root, "Settings.cs")).Distinct())
        {
            string text = File.ReadAllText(file);
            Match c = cls.Match(text);
            if (!c.Success)
            {
                continue;
            }

            string name = c.Groups[1].Value;
            var consts = constant.Matches(text).Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            string Resolve(string s) => s.StartsWith("\"") ? s.Trim('"') : consts[s];
            var props = new Dictionary<string, RooBinding>();
            foreach (Match m in bindEnabled.Matches(text))
            {
                props[m.Groups[1].Value] = new RooBinding(name, m.Groups[1].Value, Resolve(m.Groups[2].Value), "Enabled");
            }

            foreach (Match m in bind.Matches(text))
            {
                props[m.Groups[1].Value] = new RooBinding(name, m.Groups[1].Value, Resolve(m.Groups[2].Value), m.Groups[3].Value);
            }

            all.AddRange(props.Values);
            foreach (Match m in grouped.Matches(text))
            {
                string group = m.Groups[1].Value + "Entries";
                var b = new RooBinding(name, group, consts["Section"], m.Groups[2].Value);
                all.Add(b);
                props[group + "#" + b.Key] = b;
            }

            classes[name] = props;
            if (name == "Settings")
            {
                foreach (Match m in Regex.Matches(text, @"internal (\w+Settings) (\w+) \{ get; \}"))
                {
                    settingsProps[m.Groups[2].Value] = m.Groups[1].Value;
                }
            }
        }

        return (classes, settingsProps, all);
    }

    private static List<string> RooSplitArgs(string args)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        bool quoted = false;
        foreach (char ch in args)
        {
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (ch == '(' || ch == '[' || ch == '{'))
            {
                depth++;
            }
            else if (!quoted && (ch == ')' || ch == ']' || ch == '}'))
            {
                depth--;
            }

            if (ch == ',' && depth == 0 && !quoted)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString().Trim());
        }

        return parts;
    }

    // Walks Register in order, as Risk Of Options keeps tabs and options in the order they're added
    private static List<RooOption> RooRegistrations()
    {
        var (classes, settingsProps, _) = RooBindings();
        string text = File.ReadAllText(Path.Combine(RooSourceRoot(), "RiskOfOptionsSupport.cs"));
        int start = text.IndexOf("internal static void Register(", StringComparison.Ordinal);
        int end = text.IndexOf("private static MethodInfo? FindSetText", StringComparison.Ordinal);
        Expect(start >= 0 && end > start, "Register or FindSetText not found, the test no longer matches RiskOfOptionsSupport");
        string body = text.Substring(start, end - start);

        var pages = new Dictionary<string, string>();
        var aliases = new Dictionary<string, string>();
        var owners = new Dictionary<string, RooBinding?>();
        var options = new List<RooOption>();
        string? loopGroup = null;
        string? loopAlias = null;
        var page = new Regex(@"var (\w+) = new Page\(""([^""]+)""");
        var alias = new Regex(@"^\s*[\w.]*Settings (\w+) = settings\.(\w+);");
        var loop = new Regex(@"foreach \(ConfigEntry<bool> (\w+) in (\w+)\.(OutlineEntries|HideEntries)\)");
        var call = new Regex(@"^\s*(\w+)\.(Part|CheckBox|Choice|IntSlider|Percent|Seconds|Colour|Text)\((.*)\);\s*$");

        RooBinding Lookup(string expr)
        {
            string[] bits = expr.Split('.');
            if (bits[0] == "settings" && bits.Length == 2)
            {
                return classes["Settings"][bits[1]];
            }

            string cls = bits[0] == "settings" ? settingsProps[bits[1]] : settingsProps[aliases[bits[0]]];
            return classes[cls][bits[^1]];
        }

        foreach (string line in body.Split('\n'))
        {
            Match m;
            if ((m = page.Match(line)).Success)
            {
                pages[m.Groups[1].Value] = m.Groups[2].Value;
                owners[m.Groups[1].Value] = null;
                continue;
            }

            if ((m = alias.Match(line)).Success)
            {
                aliases[m.Groups[1].Value] = m.Groups[2].Value;
                continue;
            }

            if ((m = loop.Match(line)).Success)
            {
                loopAlias = m.Groups[1].Value;
                loopGroup = m.Groups[2].Value + "." + m.Groups[3].Value;
                continue;
            }

            if (!(m = call.Match(line)).Success || !pages.ContainsKey(m.Groups[1].Value))
            {
                continue;
            }

            string pageVar = m.Groups[1].Value;
            string method = m.Groups[2].Value;
            List<string> args = RooSplitArgs(m.Groups[3].Value);
            string? nameOverride = null;
            foreach (string a in args.Skip(1))
            {
                Match named = Regex.Match(a, @"^name:\s*""([^""]*)""$");
                if (named.Success)
                {
                    nameOverride = named.Groups[1].Value;
                }
                else if (Regex.IsMatch(a, @"^""[^""]*""$"))
                {
                    nameOverride = a.Trim('"');
                }
            }

            var targets = new List<RooBinding>();
            if (args[0] == loopAlias && loopGroup != null)
            {
                string[] g = loopGroup.Split('.');
                string cls = settingsProps[aliases[g[0]]];
                targets.AddRange(classes[cls].Where(p => p.Key.StartsWith(g[1] + "#", StringComparison.Ordinal)).Select(p => p.Value));
            }
            else
            {
                targets.Add(Lookup(args[0]));
            }

            foreach (RooBinding b in targets)
            {
                options.Add(new RooOption(pages[pageVar], method, b, nameOverride, method == "Part" ? null : owners[pageVar], line.Trim()));
            }

            if (method == "Part")
            {
                owners[pageVar] = targets[0];
            }
        }

        Expect(options.Count >= 60, $"only {options.Count} registrations read, the test no longer matches RiskOfOptionsSupport");
        return options;
    }

    private static void RooNoIdentifierCollision()
    {
        var clashes = RooRegistrations()
            .GroupBy(o => o.Identifier)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + " from " + string.Join(" and ", g.Select(o => o.Binding.Section + "/" + o.Binding.Key)))
            .ToList();
        Expect(clashes.Count == 0, "identifiers repeat, Category.AddOption would throw in Awake", string.Join("\n     ", clashes));

        // Name tokens are built the same way, a clash there would show one option's text on another
        var tokens = RooRegistrations()
            .Where(o => o.Method == "Choice")
            .GroupBy(o => (o.Tab + "." + o.Name).Replace(" ", "_").ToUpper())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Expect(tokens.Count == 0, "choice label tokens repeat", string.Join("\n     ", tokens));
    }

    private static void RooNothingDropped()
    {
        // Best Combo and Best Kill Combo are records the stats panel keeps, not settings
        List<RooBinding> all = RooBindings().All
            .Where(b => b.Section != "Debug" && b.Key != "Best Combo" && b.Key != "Best Kill Combo")
            .ToList();
        List<RooOption> registered = RooRegistrations();
        var missing = all.Where(b => !registered.Any(o => o.Binding.Section == b.Section && o.Binding.Key == b.Key))
            .Select(b => b.Section + "/" + b.Key)
            .ToList();
        var twice = registered.GroupBy(o => o.Binding.Section + "/" + o.Binding.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Expect(all.Count >= 80, $"only {all.Count} config settings read, the test no longer matches the settings classes");
        Expect(missing.Count == 0, $"{missing.Count} settings missing from Risk Of Options", string.Join("\n     ", missing));
        Expect(twice.Count == 0, "settings registered twice", string.Join("\n     ", twice));
        Expect(!registered.Any(o => o.Binding.Section == "Debug"), "a Debug setting reached Risk Of Options", string.Empty);
    }

    private static void RooTabOrder()
    {
        string[] order = RooRegistrations().Select(o => o.Tab).Distinct().ToArray();
        Expect(order.SequenceEqual(RooTabs), "tabs out of order", "     got " + string.Join(", ", order));
    }

    private static void RooOptionsUnderOwnPart()
    {
        var wrong = RooRegistrations()
            .Where(o => o.Method != "Part" && (o.Owner == null || o.Owner.Section != o.Binding.Section))
            .Select(o => $"{o.Binding.Section}/{o.Binding.Key} greys with {(o.Owner == null ? "nothing" : o.Owner.Section)}")
            .ToList();
        Expect(wrong.Count == 0, "options greyed by another part's switch", string.Join("\n     ", wrong));
    }

    // LanguageApi.Add overwrites, so text written before AddOption's RegisterTokens would be lost
    private static void RooTextAfterAdd()
    {
        string text = File.ReadAllText(Path.Combine(RooSourceRoot(), "RiskOfOptionsSupport.cs"));
        string Body(string signature)
        {
            int at = text.IndexOf(signature, StringComparison.Ordinal);
            Expect(at >= 0, signature + " not found");
            int next = text.IndexOf("internal void ", at + signature.Length, StringComparison.Ordinal);
            return next < 0 ? text.Substring(at) : text.Substring(at, next - at);
        }

        string part = Body("internal void Part(");
        Expect(part.IndexOf("Add(option)", StringComparison.Ordinal) >= 0
            && part.IndexOf("Add(option)", StringComparison.Ordinal) < part.IndexOf("SetText(", StringComparison.Ordinal),
            "Part writes its heading before AddOption, which would put back the plain name");
        string choice = Body("internal void Choice(");
        Expect(choice.IndexOf("Add(option)", StringComparison.Ordinal) >= 0
            && choice.IndexOf("Add(option)", StringComparison.Ordinal) < choice.IndexOf("Label(", StringComparison.Ordinal),
            "Choice labels before AddOption, the tokens don't exist yet");
    }

    // SkillDetails sets Utils.CapChance from Item Info's entry whenever Your Item Procs is on, without asking whether
    // Item Info is running, so the switch still matters with Item Info off
    private static void RooCapChanceNotGreyedByItemInfo()
    {
        string skill = File.ReadAllText(Path.Combine(RooSourceRoot(), "SkillInfo", "SkillDetails.cs"));
        Expect(skill.Contains("itemInfo.CapChance.Value") && !skill.Contains("EnabledAtLaunch"),
            "precondition changed, SkillDetails no longer reads Cap Chance regardless of Item Info");
        RooOption cap = RooRegistrations().Single(o => o.Binding.Key == "Cap Chance at 100%");
        string support = File.ReadAllText(Path.Combine(RooSourceRoot(), "RiskOfOptionsSupport.cs"));
        Match used = Regex.Match(support, @"Func<bool> (\w+) = \(\) => ([^;]+);\s*items\.CheckBox\(info\.CapChance, \1, ownSwitch: false\)");
        Expect(used.Success,
            "Cap Chance at 100% greys out with Item Info's switch, yet still caps Skill Info's item procs",
            "     " + cap.Call);
        Expect(used.Success && used.Groups[2].Value.Contains("info.Enabled.Value") && used.Groups[2].Value.Contains("skills.ItemProcs.Value"),
            "Cap Chance at 100% doesn't stay live while either Item Info or Skill Info's item procs read it");
    }
}
