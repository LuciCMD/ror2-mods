using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// BepInEx throws on these characters in a section or setting name, and a throw while binding stops every part
// Read from the source, as the settings classes need BepInEx and the game to construct
internal static partial class Program
{
    private static readonly char[] ConfigForbidden = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

    private static void RunConfigNames()
    {
        Test("Config: every section and setting name is one BepInEx accepts", ConfigNamesValid);
    }

    private static void ConfigNamesValid()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        var names = new List<(string File, string Name)>();
        var bind = new Regex(@"\.Bind(?:<[^>]+>)?\(\s*[\w.]+,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        var section = new Regex(@"const string Section = ""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in bind.Matches(text).Cast<Match>().Concat(section.Matches(text).Cast<Match>()))
            {
                names.Add((Path.GetFileName(file), Regex.Unescape(m.Groups[1].Value)));
            }
        }

        Expect(names.Count >= 30, $"only {names.Count} names found, the pattern no longer matches the settings code");
        foreach ((string file, string name) in names)
        {
            Expect(name.IndexOfAny(ConfigForbidden) < 0, $"{file}: \"{name}\" has a character BepInEx rejects");
        }
    }
}
