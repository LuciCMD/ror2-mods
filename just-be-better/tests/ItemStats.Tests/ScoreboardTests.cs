using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using JustBeBetter.Scoreboard;
using RoR2;
using Options = JustBeBetter.Scoreboard.CounterText.Options;

// Scoreboard Counters: which tier counts where, and the header's and rows' text for each setting
internal static partial class Program
{
    private const Options CounterDefaults = Options.TierCounts | Options.TempTotal;

    private static void RunScoreboard()
    {
        Test("Counters: every tier lands in its group, voids in one, others only in the total", CountersGroups);
        Test("Counters: the default header reads Items: then tiers, the total and its temp count", CountersDefaultText);
        Test("Counters: tiers held only as temp items still show, empty optional tiers don't", CountersOptionalTiers);
        Test("Counters: Temp per Tier brackets only the tiers holding temp items", CountersTempPerTier);
        Test("Counters: temp items counted in add to tiers and totals, with no brackets", CountersMerged);
        Test("Counters: a row shows its total and temp count, never the tiers", CountersRowTotalOnly);
        Test("Counters: Tier Counts off leaves the game's header", CountersHeaderOff);
        Test("Counters: the header label keeps the game's word, trimmed, in any language", CountersHeaderLabel);
        Test("Counters: nothing held reads 0 0 0 and a 0 total, no brackets", CountersEmpty);
        Test("Counters: size tags open and close in pairs for every setting", CountersBalancedTags);
        Test("Counters: big counts have no separators under a comma culture, negatives are ignored", CountersNumbers);
    }

    private static CounterText.Tally TallyOf(params (ItemTier Tier, int Permanent, int Temp)[] items)
    {
        var tally = new CounterText.Tally();
        foreach ((ItemTier tier, int permanent, int temp) in items)
        {
            tally.Add(tier, permanent, temp);
        }

        return tally;
    }

    private static void CountersGroups()
    {
        CounterText.Tally tally = TallyOf(
            (ItemTier.Tier1, 1, 0),
            (ItemTier.Tier2, 2, 0),
            (ItemTier.Tier3, 3, 0),
            (ItemTier.Boss, 4, 0),
            (ItemTier.Lunar, 5, 0),
            (ItemTier.VoidTier1, 1, 0),
            (ItemTier.VoidTier2, 1, 0),
            (ItemTier.VoidTier3, 1, 0),
            (ItemTier.VoidBoss, 1, 0),
            (ItemTier.FoodTier, 7, 0),
            (ItemTier.Cursed, 8, 0),
            (ItemTier.Hex, 9, 0),
            (ItemTier.Page, 10, 0),
            (ItemTier.NoTier, 20, 0),
            (ItemTier.AssignedAtRuntime, 30, 0),
            ((ItemTier)99, 40, 0));
        int[] expected = { 1, 2, 3, 4, 5, 4, 7, 8, 9, 10 };
        for (int i = 0; i < CounterText.Groups; i++)
        {
            Expect(tally.Permanent[i] == expected[i], $"{(CounterText.Group)i} has {tally.Permanent[i]}, expected {expected[i]}");
        }

        Expect(tally.PermanentTotal == 143, $"total {tally.PermanentTotal}, expected 143 with untiered and modded items");
        Expect(Enum.GetValues(typeof(CounterText.Group)).Length == CounterText.Groups, "Groups doesn't match the enum");
    }

    private static void CountersDefaultText()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2), (ItemTier.Tier2, 5, 0), (ItemTier.Tier3, 1, 0), (ItemTier.Lunar, 2, 0), (ItemTier.Tier1, 0, 2));
        Equal(
            "Items: <size=80%><color=#FFFFFF>3</color> <color=#77FF17>5</color> <color=#E7543A>1</color> <color=#307FFF>2</color>"
            + "</size> <color=#FFFFFF>11</color><size=65%><color=#6FD8F1>[4]</color></size>",
            CounterText.Header("Items", tally, CounterDefaults));
    }

    private static void CountersOptionalTiers()
    {
        string text = CounterText.Counts(TallyOf((ItemTier.Boss, 0, 1)), CounterDefaults);
        Expect(text.Contains("<color=#FFEB04>0</color>", StringComparison.Ordinal), $"boss held only as temp is missing: {text}");
        Expect(!text.Contains("#307FFF", StringComparison.Ordinal), $"lunar shows with none held: {text}");
        Expect(!text.Contains("#ED7FCD", StringComparison.Ordinal), $"void shows with none held: {text}");
    }

    private static void CountersTempPerTier()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2), (ItemTier.Tier2, 5, 0), (ItemTier.Tier3, 1, 2));
        Equal(
            "<size=80%><color=#FFFFFF>3</color></size><size=55%><color=#6FD8F1>[2]</color></size><size=80%>"
            + " <color=#77FF17>5</color>"
            + " <color=#E7543A>1</color></size><size=55%><color=#6FD8F1>[2]</color></size><size=80%>"
            + "</size> <color=#FFFFFF>9</color><size=65%><color=#6FD8F1>[4]</color></size>",
            CounterText.Counts(tally, CounterDefaults | Options.TempPerTier));
    }

    private static void CountersMerged()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2), (ItemTier.Tier2, 5, 0), (ItemTier.Tier3, 1, 2), (ItemTier.Lunar, 2, 0));
        Options merged = CounterDefaults | Options.TempPerTier | Options.CountTempWithPermanent;
        Equal(
            "<size=80%><color=#FFFFFF>5</color> <color=#77FF17>5</color> <color=#E7543A>3</color> <color=#307FFF>2</color>"
            + "</size> <color=#FFFFFF>15</color>",
            CounterText.Counts(tally, merged));
        Equal("<color=#FFFFFF>15</color>", CounterText.Row(tally, merged));
    }

    private static void CountersRowTotalOnly()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2), (ItemTier.Tier2, 5, 0), (ItemTier.Lunar, 1, 0));
        Equal("<color=#FFFFFF>9</color><size=70%><color=#6FD8F1>[2]</color></size>", CounterText.Row(tally, CounterDefaults | Options.TempPerTier));
        Equal("<color=#FFFFFF>9</color>", CounterText.Row(tally, Options.TierCounts));
        Equal("<color=#FFFFFF>9</color>", CounterText.Row(TallyOf((ItemTier.Tier1, 9, 0)), CounterDefaults));
    }

    private static void CountersHeaderOff()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2));
        Expect(CounterText.Header("Items", tally, Options.TempTotal | Options.TempPerTier) == null, "the header should be left alone with Tier Counts off");
    }

    // The prefab's own text ends in a newline, and a translation or another mod may end in a colon already
    private static void CountersHeaderLabel()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 1, 0));
        string counts = CounterText.Counts(tally, CounterDefaults);
        Equal("Items: " + counts, CounterText.Header("Items\n", tally, CounterDefaults)!);
        Equal("Objets: " + counts, CounterText.Header("  Objets : ", tally, CounterDefaults)!);
        Equal("Gegenstände: " + counts, CounterText.Header("Gegenstände:", tally, CounterDefaults)!);
        Equal(counts, CounterText.Header(null, tally, CounterDefaults)!);
        Equal(counts, CounterText.Header(" \n", tally, CounterDefaults)!);
    }

    private static void CountersEmpty()
    {
        Equal(
            "<size=80%><color=#FFFFFF>0</color> <color=#77FF17>0</color> <color=#E7543A>0</color>"
            + "</size> <color=#FFFFFF>0</color>",
            CounterText.Counts(new CounterText.Tally(), CounterDefaults | Options.TempPerTier));
        Equal("<color=#FFFFFF>0</color>", CounterText.Row(new CounterText.Tally(), CounterDefaults));
    }

    private static void CountersBalancedTags()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 3, 2), (ItemTier.VoidTier2, 0, 1), (ItemTier.Page, 1, 1), (ItemTier.NoTier, 0, 4));
        for (int flags = 0; flags < 16; flags++)
        {
            foreach (string text in new[] { CounterText.Counts(tally, (Options)flags), CounterText.Row(tally, (Options)flags) })
            {
                int depth = 0;
                foreach (Match tag in Regex.Matches(text, "</?size"))
                {
                    depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                    Expect(depth >= 0, $"options {(Options)flags} close a size tag never opened: {text}");
                }

                Expect(depth == 0, $"options {(Options)flags} leave {depth} size tags open: {text}");
                Expect(Regex.Matches(text, "<color=").Count == Regex.Matches(text, "</color>").Count, $"options {(Options)flags} unbalanced colours: {text}");
            }
        }
    }

    private static void CountersNumbers()
    {
        CultureInfo before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            CounterText.Tally tally = TallyOf((ItemTier.Tier1, 12345, 0), (ItemTier.Tier2, -5, -3), (ItemTier.Tier1, int.MaxValue, 0));
            string text = CounterText.Counts(tally, CounterDefaults);
            Expect(text.Contains(">2147483647<", StringComparison.Ordinal), $"white should cap at int.MaxValue: {text}");
            Expect(text.Contains(">2147495992<", StringComparison.Ordinal), $"the total should add past int.MaxValue: {text}");
            Expect(text.Contains("<color=#77FF17>0</color>", StringComparison.Ordinal), $"negative green should read 0: {text}");
            Expect(!text.Contains('.', StringComparison.Ordinal) && !text.Contains("[", StringComparison.Ordinal), $"separator or bracket: {text}");
            Equal("<color=#FFFFFF>2147495992</color>", CounterText.Row(tally, CounterDefaults));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
