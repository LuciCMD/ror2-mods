using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JustBeBetter.BuffInfo
{
    // The text on a buff icon, kept apart from Unity so it can be tested
    internal static class BuffText
    {
        // Matches the game's stack count, scaled down so both lines fit the icon
        private const string TimerSize = "<size=80%>";
        private const string UnknownName = "Unknown Buff";

        // The game's own stack text, "x3" or "30%", empty for a buff that doesn't stack
        internal static string Stack(bool canStack, bool asPercent, int count, int displayOffset)
        {
            if (!canStack)
            {
                return string.Empty;
            }

            int shown = count + displayOffset;
            return asPercent ? shown.ToString(CultureInfo.InvariantCulture) + "%" : "x" + shown.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Seconds(float seconds, bool oneDecimal)
        {
            if (float.IsNaN(seconds) || seconds <= 0f)
            {
                return oneDecimal ? "0.0" : "0";
            }

            if (oneDecimal)
            {
                // Floored to the tenth, so 0.04 left doesn't read as 0.1, multiplied in float so 0.7 stays 0.7
                float scaled = Math.Min(seconds, 99999f) * 10f;
                double tenths = Math.Floor(scaled) / 10d;
                return tenths.ToString("0.0", CultureInfo.InvariantCulture);
            }

            return Math.Ceiling(Math.Min(seconds, 99999f)).ToString("0", CultureInfo.InvariantCulture);
        }

        // Timer above the stack count, a blank second line keeps a lone timer at the same height
        internal static string Timed(float seconds, bool oneDecimal, string stack)
        {
            return TimerSize + Seconds(seconds, oneDecimal) + "</size>\n" + (stack.Length > 0 ? stack : " ");
        }

        // The longest time left among a buff's entries, as that's when its icon goes away
        internal static Dictionary<int, float> Longest(IEnumerable<(int Buff, float Timer)> timers)
        {
            var longest = new Dictionary<int, float>();
            foreach ((int buff, float timer) in timers)
            {
                if (float.IsNaN(timer))
                {
                    continue;
                }

                if (!longest.TryGetValue(buff, out float best) || timer > best)
                {
                    longest[buff] = timer;
                }
            }

            return longest;
        }

        // A few ported entries are empty or only repeat their name, so those show the title alone
        internal static string Body(string name, string? description)
        {
            string text = description?.Trim() ?? string.Empty;
            return string.Equals(text, name?.Trim(), StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
        }

        // A buff without a description shows its internal name made readable, bdArmorBoost as Armor Boost
        // Never empty, as a tooltip with no title doesn't show
        internal static string Tidy(string? internalName)
        {
            if (string.IsNullOrWhiteSpace(internalName))
            {
                return UnknownName;
            }

            string name = internalName!;
            if (name.Length > 2 && name.StartsWith("bd", StringComparison.Ordinal) && char.IsUpper(name[2]))
            {
                name = name.Substring(2);
            }

            var text = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || char.IsWhiteSpace(c))
                {
                    AppendSpace(text);
                    continue;
                }

                bool wordStart = i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
                if (wordStart)
                {
                    AppendSpace(text);
                }

                text.Append(c);
            }

            string tidy = text.ToString().Trim();
            return tidy.Length > 0 ? tidy : UnknownName;
        }

        // One space between words, none at the start
        private static void AppendSpace(StringBuilder text)
        {
            if (text.Length > 0 && text[text.Length - 1] != ' ')
            {
                text.Append(' ');
            }
        }
    }
}
