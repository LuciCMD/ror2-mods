using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JustBeBetter.ItemStats
{
    // An item with no stat table entry, usually a modded one, read from its description instead
    // Only the plain "10% (+10% per stack)" form, which stacks in a straight line, so nothing is guessed
    // Hyperbolic stacking, caps and wording such as "more per stack" are left out, as the text can't say how they work
    internal static class StackText
    {
        internal sealed class Stat
        {
            internal Stat(string label, float first, float step, ItemStatsDef.MeasurementUnits unit, ItemStatsDef.ValueType type)
            {
                Label = label;
                First = first;
                Step = step;
                Unit = unit;
                Type = type;
            }

            internal string Label { get; }

            internal float First { get; }

            internal float Step { get; }

            internal ItemStatsDef.MeasurementUnits Unit { get; }

            internal ItemStatsDef.ValueType Type { get; }

            // A value falling from above 0 stops there, the text can't say what's past it
            internal float At(int count)
            {
                float value = First + (Step * (Math.Max(1, count) - 1));
                return Step < 0f && First >= 0f ? Math.Max(0f, value) : value;
            }
        }

        private const string Unit = @"%|\s?seconds?\b|\s?meters?\b|s\b|m\b";

        private static readonly Regex Pair = new Regex(
            @"(?:<style=(?<style>\w+)>)?(?:<[^>]+>)*(?:(?<![\w.,])(?<lead>[+-]))?(?<![\d.,])(?<!\d[-–])(?<first>\d+(?:\.\d+)?)(?<unit>" + Unit + @")?(?:\s|<[^>]+>)*"
            + @"\(\s*(?<sign>[+-])\s*(?<step>\d+(?:\.\d+)?)(?<unit2>" + Unit + @")?\s+per\s+stack\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // An unclosed tag runs to the end, so none of it reaches a label
        private static readonly Regex Tag = new Regex("<[^>]*(?:>|$)", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Minor = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "or", "but", "nor", "of", "for", "to", "in", "on", "at", "by", "per", "as", "vs",
            "via", "from", "with", "into", "over",
        };

        // Words that carry nothing at a label's edge, "of damage taken" reads as "damage taken"
        // "to" stays, so "up to" keeps its meaning
        private static readonly HashSet<string> Edge = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "or", "but", "nor", "of", "for", "in", "on", "at", "by", "per", "as", "vs", "via",
            "from", "with", "into", "over", "within", "is", "are",
        };

        // After a value these start a condition, "for 7s" or "within 20m", so the words before it name it better
        private static readonly HashSet<string> Condition = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "for", "to", "within", "in", "on", "at", "every", "each", "when", "while", "after", "until", "up", "if",
        };

        private static readonly char[] Stops = { '.', ',', ';', ':', '!', '?', '(', ')', '\n', '\r' };
        private static readonly char[] Digits = "0123456789".ToCharArray();
        private static readonly char[] Spaces = { ' ', '\t' };
        private static readonly char[] Marks = { '%', '+', '-', '\'', '"', '<', '>' };

        private const int MaxAfterWords = 5;
        private const int MaxBeforeWords = 4;

        internal static List<Stat> Read(string? description)
        {
            var stats = new List<Stat>();
            if (string.IsNullOrEmpty(description))
            {
                return stats;
            }

            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Pair.Matches(description))
            {
                ItemStatsDef.MeasurementUnits unit = UnitOf(match.Groups["unit"].Value);
                string stepUnitText = match.Groups["unit2"].Value;

                // "10% (+5 per stack)" means 5%, but "10 (+5% per stack)" can't be read
                if (stepUnitText.Length > 0 && UnitOf(stepUnitText) != unit)
                {
                    continue;
                }

                // The game's Mono throws on a number too long for a float, where newer .NET gives infinity
                if (!Number(match.Groups["first"].Value, out float first) || !Number(match.Groups["step"].Value, out float step))
                {
                    continue;
                }

                if (match.Groups["lead"].Value == "-")
                {
                    first = -first;
                }

                if (match.Groups["sign"].Value == "-")
                {
                    step = -step;
                }

                if (unit == ItemStatsDef.MeasurementUnits.Percentage)
                {
                    first /= 100f;
                    step /= 100f;
                }

                string named = Label(description!, match);
                if (named.Length == 0)
                {
                    named = "Value " + (stats.Count + 1).ToString(CultureInfo.InvariantCulture);
                }

                string label = named;
                for (int n = 2; !labels.Add(label); n++)
                {
                    label = named + " " + n.ToString(CultureInfo.InvariantCulture);
                }

                stats.Add(new Stat(label, first, step, unit, TypeOf(match.Groups["style"].Value)));
            }

            return stats;
        }

        // Shaped like the table's own entries, so it shows and compares the same way
        internal static ItemStatsDef? ToDef(List<Stat> stats)
        {
            if (stats.Count == 0)
            {
                return null;
            }

            var def = new ItemStatsDef();
            foreach (Stat stat in stats)
            {
                def.descriptions.Add(stat.Label + ": ");
                def.valueTypes.Add(stat.Type);
                def.measurementUnits.Add(stat.Unit);
            }

            def.calculateValuesFlat = count => stats.Select(stat => stat.At(count)).ToList();
            return def;
        }

        private static bool Number(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                && !float.IsInfinity(value) && !float.IsNaN(value);
        }

        private static ItemStatsDef.MeasurementUnits UnitOf(string unit)
        {
            string u = unit.Trim().ToLowerInvariant();
            if (u == "%")
            {
                return ItemStatsDef.MeasurementUnits.Percentage;
            }

            if (u == "s" || u.StartsWith("second", StringComparison.Ordinal))
            {
                return ItemStatsDef.MeasurementUnits.Seconds;
            }

            if (u == "m" || u.StartsWith("meter", StringComparison.Ordinal))
            {
                return ItemStatsDef.MeasurementUnits.Meters;
            }

            return ItemStatsDef.MeasurementUnits.Number;
        }

        // The game's text styles, so the value keeps the colour the description gave it
        private static ItemStatsDef.ValueType TypeOf(string style)
        {
            switch (style)
            {
                case "cIsDamage":
                    return ItemStatsDef.ValueType.Damage;
                case "cIsHealing":
                    return ItemStatsDef.ValueType.Healing;
                case "cIsUtility":
                    return ItemStatsDef.ValueType.Utility;
                case "cIsHealth":
                    return ItemStatsDef.ValueType.Health;
                case "cIsVoid":
                    return ItemStatsDef.ValueType.Void;
                case "cShrine":
                    return ItemStatsDef.ValueType.Shrine;
                default:
                    return ItemStatsDef.ValueType.None;
            }
        }

        // The words after the value, "chance to bleed", unless they're a condition, else the ones before it,
        // "increase attack speed by", and the bare words when trimming leaves nothing, "within"
        private static string Label(string description, Match match)
        {
            List<string> after = Clause(Tag.Replace(description.Substring(match.Index + match.Length), string.Empty), true);
            List<string> before = Clause(Tag.Replace(description.Substring(0, match.Index), string.Empty), false);
            List<string> afterLabel = Trim(after, MaxAfterWords, true);
            if (afterLabel.Count > 0 && !Condition.Contains(after[0]))
            {
                return Title(afterLabel);
            }

            List<string> beforeLabel = Trim(before, MaxBeforeWords, false);
            if (beforeLabel.Count > 0)
            {
                return Title(beforeLabel);
            }

            return Title(afterLabel.Count > 0 ? afterLabel : before.Skip(Math.Max(0, before.Count - 2)).ToList());
        }

        private static List<string> Trim(List<string> words, int max, bool forward)
        {
            List<string> kept = StripEdges(words);
            if (kept.Count > max)
            {
                kept = StripEdges(forward ? kept.Take(max).ToList() : kept.Skip(kept.Count - max).ToList());
            }

            return kept;
        }

        private static List<string> StripEdges(List<string> words)
        {
            var kept = new List<string>(words);
            while (kept.Count > 0 && Edge.Contains(kept[0]))
            {
                kept.RemoveAt(0);
            }

            while (kept.Count > 0 && Edge.Contains(kept[kept.Count - 1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }

            return kept;
        }

        // The clause's words up to the nearest stop or number on that side
        private static List<string> Clause(string text, bool forward)
        {
            string clause;
            if (forward)
            {
                int end = text.IndexOfAny(Stops);
                clause = end >= 0 ? text.Substring(0, end) : text;
                int digit = clause.IndexOfAny(Digits);
                clause = digit >= 0 ? clause.Substring(0, digit) : clause;
            }
            else
            {
                int start = text.LastIndexOfAny(Stops);
                clause = start >= 0 ? text.Substring(start + 1) : text;
                int digit = clause.LastIndexOfAny(Digits);
                clause = digit >= 0 ? clause.Substring(digit + 1) : clause;
            }

            // Signs and units left from a neighbouring value, the % of "+12% movement speed"
            return clause.Split(Spaces, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Trim(Marks))
                .Where(word => word.Any(char.IsLetter))
                .ToList();
        }

        private static string Title(List<string> words)
        {
            var text = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                bool keepLower = i > 0 && i < words.Count - 1 && Minor.Contains(word);
                text.Append(text.Length > 0 ? " " : string.Empty)
                    .Append(keepLower ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word.Substring(1));
            }

            return text.ToString();
        }
    }
}
