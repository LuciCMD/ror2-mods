// The tier colours and layout ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: a tally and flags instead of fields, temp items added in leave no brackets,
// tiers in the Items header for your own items with rows showing only their total

using System;
using System.Globalization;
using System.Text;
using RoR2;

namespace JustBeBetter.Scoreboard
{
    // The scoreboard's item count text, apart from the game so it can be checked outside it
    internal static class CounterText
    {
        [Flags]
        internal enum Options
        {
            None = 0,
            TierCounts = 1,
            TempTotal = 2,
            TempPerTier = 4,
            CountTempWithPermanent = 8,
        }

        // In the order shown, the first three always, the rest only when held
        internal enum Group
        {
            White,
            Green,
            Red,
            Boss,
            Lunar,
            Void,
            Food,
            Hallowed,
            Hex,
            Page,
        }

        internal const int Groups = 10;
        private const int AlwaysShown = 3;

        // LookingGlass's temp colour, the game's own is hard to read at this size
        private const string TempColour = "#6FD8F1";
        private const string TotalColour = "#FFFFFF";

        private static readonly string[] Colours =
        {
            "#FFFFFF",
            "#77FF17",
            "#E7543A",
            "#FFEB04",
            "#307FFF",
            "#ED7FCD",
            "#FF8000",
            "#9469FC",
            "#D2FB1F",
            "#1D88FF",
        };

        // Null for tiers that only count toward the total, such as NoTier and modded tiers
        internal static Group? GroupOf(ItemTier tier)
        {
            switch (tier)
            {
                case ItemTier.Tier1: return Group.White;
                case ItemTier.Tier2: return Group.Green;
                case ItemTier.Tier3: return Group.Red;
                case ItemTier.Boss: return Group.Boss;
                case ItemTier.Lunar: return Group.Lunar;
                case ItemTier.VoidTier1:
                case ItemTier.VoidTier2:
                case ItemTier.VoidTier3:
                case ItemTier.VoidBoss: return Group.Void;
                case ItemTier.FoodTier: return Group.Food;
                case ItemTier.Cursed: return Group.Hallowed;
                case ItemTier.Hex: return Group.Hex;
                case ItemTier.Page: return Group.Page;
                default: return null;
            }
        }

        // A row's count, the total and its temp items, small enough for the game's box
        internal static string Row(Tally tally, Options options)
        {
            if (tally == null)
            {
                throw new ArgumentNullException(nameof(tally));
            }

            var text = new StringBuilder(64);
            Total(text, tally, options, "70%");
            return text.ToString();
        }

        // The Items header with your counts after it, or null to leave the game's header
        internal static string? Header(string? label, Tally tally, Options options)
        {
            if ((options & Options.TierCounts) == 0)
            {
                return null;
            }

            // The prefab's label ends in a newline, a translation may end in a colon already
            string word = (label ?? string.Empty).Trim().TrimEnd(':').TrimEnd();
            return word.Length == 0 ? Counts(tally, options) : word + ": " + Counts(tally, options);
        }

        // Sized for the 24 px header so the numbers match LookingGlass's on its 32 px row
        internal static string Counts(Tally tally, Options options)
        {
            if (tally == null)
            {
                throw new ArgumentNullException(nameof(tally));
            }

            bool merge = (options & Options.CountTempWithPermanent) != 0;
            bool tempPerTier = !merge && (options & Options.TempPerTier) != 0;
            var text = new StringBuilder(160);
            text.Append("<size=80%>");
            bool first = true;
            for (int i = 0; i < Groups; i++)
            {
                int permanent = tally.Permanent[i];
                int temp = tally.Temp[i];
                if (i >= AlwaysShown && permanent == 0 && temp == 0)
                {
                    continue;
                }

                if (!first)
                {
                    text.Append(' ');
                }

                first = false;
                Coloured(text, Colours[i], merge ? (long)permanent + temp : permanent);
                if (tempPerTier && temp > 0)
                {
                    // Closed and reopened rather than nested, as LookingGlass does
                    text.Append("</size><size=55%>");
                    Bracket(text, temp);
                    text.Append("</size><size=80%>");
                }
            }

            text.Append("</size> ");
            Total(text, tally, options, "65%");
            return text.ToString();
        }

        private static void Total(StringBuilder text, Tally tally, Options options, string tempSize)
        {
            bool merge = (options & Options.CountTempWithPermanent) != 0;
            Coloured(text, TotalColour, tally.PermanentTotal + (merge ? tally.TempTotal : 0));
            if (!merge && (options & Options.TempTotal) != 0 && tally.TempTotal > 0)
            {
                text.Append("<size=").Append(tempSize).Append('>');
                Bracket(text, tally.TempTotal);
                text.Append("</size>");
            }
        }

        private static void Coloured(StringBuilder text, string colour, long count)
        {
            text.Append("<color=").Append(colour).Append('>')
                .Append(count.ToString(CultureInfo.InvariantCulture))
                .Append("</color>");
        }

        private static void Bracket(StringBuilder text, long count)
        {
            text.Append("<color=").Append(TempColour).Append(">[")
                .Append(count.ToString(CultureInfo.InvariantCulture))
                .Append("]</color>");
        }

        // Counts per group and in total, hidden items left out by the caller
        internal sealed class Tally
        {
            internal int[] Permanent { get; } = new int[Groups];

            internal int[] Temp { get; } = new int[Groups];

            internal long PermanentTotal { get; private set; }

            internal long TempTotal { get; private set; }

            internal void Clear()
            {
                Array.Clear(Permanent, 0, Groups);
                Array.Clear(Temp, 0, Groups);
                PermanentTotal = 0;
                TempTotal = 0;
            }

            internal void Add(ItemTier tier, int permanent, int temp)
            {
                permanent = Math.Max(0, permanent);
                temp = Math.Max(0, temp);
                PermanentTotal += permanent;
                TempTotal += temp;
                if (GroupOf(tier) is Group group)
                {
                    Permanent[(int)group] = Sum(Permanent[(int)group], permanent);
                    Temp[(int)group] = Sum(Temp[(int)group], temp);
                }
            }

            private static int Sum(int a, int b)
            {
                return (int)Math.Min(int.MaxValue, (long)a + b);
            }
        }
    }
}
