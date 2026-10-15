using System;
using System.Collections.Generic;

namespace JustBeBetter.Pickers
{
    // The counts a picker opened with, for a menu the game refreshes while it's open (a crafting menu redoes its
    // options on every inventory change), so a decaying temporary stack or a pickup can't move its buttons
    internal sealed class OpenCounts
    {
        private readonly Dictionary<string, int> first = new Dictionary<string, int>(StringComparer.Ordinal);

        internal int Keep(string? key, int count)
        {
            if (key == null)
            {
                return count;
            }

            if (first.TryGetValue(key, out int kept))
            {
                return kept;
            }

            first[key] = count;
            return count;
        }
    }
}
