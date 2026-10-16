using System;
using System.Collections.Generic;
using System.Text;

namespace JustBeBetter.StatsDisplay
{
    // A LookingGlass style text, where each [name] stands for a stat's value
    internal static class StatTemplate
    {
        // Unknown names and \[name] stay as typed, as LookingGlass leaves them
        internal static string Fill(string? template, Func<string, string?> value, ICollection<string>? unknown = null)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            var text = new StringBuilder(template!.Length + 64);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '[' && (i == 0 || template[i - 1] != '\\'))
                {
                    int end = i + 1;
                    while (end < template.Length && IsNameChar(template[end]))
                    {
                        end++;
                    }

                    if (end > i + 1 && end < template.Length && template[end] == ']')
                    {
                        string name = template.Substring(i + 1, end - i - 1);
                        string? filled = value(name);
                        if (filled != null)
                        {
                            text.Append(filled);
                            i = end + 1;
                            continue;
                        }

                        unknown?.Add(name);
                    }
                }

                text.Append(c);
                i++;
            }

            return text.ToString();
        }

        // What LookingGlass's \w matched, short of letters outside English
        private static bool IsNameChar(char c)
        {
            return c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }
    }
}
