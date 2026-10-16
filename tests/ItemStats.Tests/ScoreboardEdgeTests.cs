using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using JustBeBetter.Scoreboard;
using RoR2;
using Options = JustBeBetter.Scoreboard.CounterText.Options;

// Edge cases for Scoreboard Counters
// The model tests run the game's ScoreboardStrip.UpdateItemCountText and ItemInventoryDisplay count as decompiled
// from the installed RoR2.dll, with ItemCounters' row and header passes mirrored on top, since neither runs outside Unity
// The board stands in for ScoreboardController.OnEnable and Rebuild and the header's LanguageTextMeshController
// A change to ItemCounters has to be mirrored here, a change to CounterText shows here by itself
internal static partial class Program
{
    private static void RunScoreboardEdge()
    {
        Test("Counters edge: colour and size tags nest without crossing for every setting and tally", SbEdgeNesting);
        Test("Counters edge: a cleared tally builds the same text as a new one, as rows share one", SbEdgeTallyReuse);
        Test("Counters edge: every named game tier has a group but NoTier and AssignedAtRuntime", SbEdgeEveryTier);
        Test("Counters edge: one colour per group, none the temp colour", SbEdgeColours);
        Test("Counters edge: every group and temp at int.MaxValue builds with no negatives in any culture", SbEdgeHuge);
        Test("Counters edge: the members ItemCounters reads exist in the installed game", SbEdgeReflected);
        Test("Counters edge model: a void corruption with the same total rewrites the tiers next frame", SbEdgeTierSwap);
        Test("Counters edge model: every settings change shows on the next frame", SbEdgeSettingsChange);
        Test("Counters edge model: a row moved to another player with the same total shows that player", SbEdgeMasterSwap);
        Test("Counters edge model: temp items decaying with no change event read right within a second", SbEdgeTempDecay);
        Test("Counters edge model: the game's plain total never stays after the frame it's written in", SbEdgeGameWrites);
        Test("Counters edge model: changes while the scoreboard is closed show when it opens", SbEdgeClosedChanges);
        Test("Counters edge model: a hidden inventory shows the game's count, coming back shows ours", SbEdgeDisabled);
        Test("Counters edge model: a row moved to a hidden inventory with an equal game count drops the last tiers", SbEdgeDisabledSameCount);
        Test("Counters edge model: after a failure the row shows the game's count as the log says", SbEdgeFailedLeavesOurs);
        Test("Counters edge model: rented and shared items count toward the total as the game counts them", SbEdgeRentalChanneled);
        Test("Counters edge model: hidden items add nothing, temp or not", SbEdgeHiddenItems);
        Test("Counters edge model: with Unhide Items on, hidden items add to the total the row's icons show", SbEdgeHiddenItemsUnhidden);
        Test("Counters edge: the members the header reads exist in the installed game", SbEdgeHeaderReflected);
        Test("Counters edge: the header keeps each of the game's own words once, however the label ends", SbEdgeHeaderWords);
        Test("Counters edge header: a void corruption with the same total shows in the header next frame", SbEdgeHeaderVoidSwap);
        Test("Counters edge header: with your row gone after dying, other rows keep your header current", SbEdgeHeaderLocalDead);
        Test("Counters edge header: every reopen reads the game's word once with your current counts", SbEdgeHeaderEachOpen);
        Test("Counters edge header: the frame the scoreboard opens shows ours, not the game's plain label", SbEdgeHeaderOpenFrame);
        Test("Counters edge header: a language change while open swaps the word, not stacks it", SbEdgeHeaderLanguageOpen);
        Test("Counters edge header: a language change while closed shows the new word on open, Tier Counts off or on", SbEdgeHeaderLanguageClosed);
        Test("Counters edge header: Tier Counts toggled every frame shows the right header and wrapping each frame", SbEdgeHeaderToggle);
        Test("Counters edge header: Tier Counts off while closed gives back the game's wrapping on open", SbEdgeHeaderWrapAfterOpen);
        Test("Counters edge header: Tier Counts off in the frame the game rewrote gives back wrapping", SbEdgeHeaderWrapAfterLanguage);
        Test("Counters edge header: a failure in the frame the game rewrote gives back wrapping", SbEdgeHeaderWrapAfterFailure);
        Test("Counters edge header: a row failure hands the header back, and a reopen keeps the game's", SbEdgeHeaderRowFailure);
        Test("Counters edge header: a header failure hands the header back with its wrapping", SbEdgeHeaderFailure);
        Test("Counters edge header: split-screen boards each show their own player, every frame", SbEdgeHeaderSplitScreen);
        Test("Counters edge header: the local master moving to another with the same total shows the new one", SbEdgeHeaderMasterSwap);
        Test("Counters edge header: a hidden local inventory gives the game's header, coming back gives ours", SbEdgeHeaderDisabled);
        Test("Counters edge header: temp items decaying with no change event read right within a second", SbEdgeHeaderTempDecay);
        Test("Counters edge header: no Items header found leaves rows working and warns once", SbEdgeHeaderNoLabel);
        Test("Counters edge header: nothing changing writes nothing, at most one header write a frame", SbEdgeHeaderQuiet);
        Test("Counters edge header: opening with no rows shows the game's header, not the last counts", SbEdgeHeaderNoRows);
        Test("Counters edge header: a row list rebuild in the same frame as a change shows next frame", SbEdgeHeaderRebuild);
        Test("Counters edge header: random opens, languages, settings, deaths and changes keep the header right", SbEdgeHeaderFuzz);
    }

    private static readonly Options[] SbAllOptions = Enumerable.Range(0, 16).Select(f => (Options)f).ToArray();

    // Every <color> and <size> closes in the reverse order it opened, TextMeshPro pops its stacks in order
    private static void SbEdgeNesting()
    {
        var random = new Random(4242);
        ItemTier[] tiers = (ItemTier[])Enum.GetValues(typeof(ItemTier));
        for (int round = 0; round < 300; round++)
        {
            var tally = new CounterText.Tally();
            int items = random.Next(0, 12);
            for (int i = 0; i < items; i++)
            {
                tally.Add(tiers[random.Next(tiers.Length)], random.Next(-2, 5), random.Next(-2, 5));
            }

            foreach (Options options in SbAllOptions)
            {
                string text = CounterText.Counts(tally, options);
                var open = new Stack<string>();
                foreach (Match tag in Regex.Matches(text, "<(/?)(color|size)[^>]*>"))
                {
                    string kind = tag.Groups[2].Value;
                    if (tag.Groups[1].Value.Length == 0)
                    {
                        open.Push(kind);
                        continue;
                    }

                    Expect(open.Count > 0 && open.Peek() == kind, $"options {options} close {kind} out of order: {text}");
                    open.Pop();
                }

                Expect(open.Count == 0, $"options {options} leave {open.Count} tags open: {text}");
                string bare = Regex.Replace(text, "<[^>]*>", string.Empty);
                Expect(Regex.IsMatch(bare, @"^[0-9 \[\]]+$"), $"options {options} show something but digits: [{bare}]");
            }
        }
    }

    private static void SbEdgeTallyReuse()
    {
        var shared = new CounterText.Tally();
        shared.Add(ItemTier.Tier1, 9, 9);
        shared.Add(ItemTier.Page, 3, 1);
        shared.Add(ItemTier.NoTier, 4, 4);
        shared.Clear();
        shared.Add(ItemTier.Tier2, 1, 2);
        CounterText.Tally fresh = TallyOf((ItemTier.Tier2, 1, 2));
        foreach (Options options in SbAllOptions)
        {
            Equal(CounterText.Counts(fresh, options), CounterText.Counts(shared, options));
        }

        Equal(CounterText.Counts(fresh, CounterDefaults), CounterText.Counts(fresh, CounterDefaults));
    }

    private static void SbEdgeEveryTier()
    {
        foreach (ItemTier tier in (ItemTier[])Enum.GetValues(typeof(ItemTier)))
        {
            bool untiered = tier == ItemTier.NoTier || tier == ItemTier.AssignedAtRuntime;
            Expect(CounterText.GroupOf(tier).HasValue != untiered, $"{tier} {(untiered ? "has" : "has no")} group");
        }
    }

    private static void SbEdgeColours()
    {
        const BindingFlags any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        string[] colours = (string[])typeof(CounterText).GetField("Colours", any)!.GetValue(null)!;
        string temp = (string)typeof(CounterText).GetField("TempColour", any)!.GetValue(null)!;
        Expect(colours.Length == CounterText.Groups, $"{colours.Length} colours for {CounterText.Groups} groups");
        Expect(colours.Distinct(StringComparer.OrdinalIgnoreCase).Count() == colours.Length, "two groups share a colour");
        Expect(!colours.Contains(temp, StringComparer.OrdinalIgnoreCase), "a group uses the temp colour");
    }

    private static void SbEdgeHuge()
    {
        CultureInfo before = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "ar-SA", "fa-IR", "sv-SE", "tr-TR" })
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                var tally = new CounterText.Tally();
                foreach (ItemTier tier in (ItemTier[])Enum.GetValues(typeof(ItemTier)))
                {
                    tally.Add(tier, int.MaxValue, int.MaxValue);
                    tally.Add(tier, int.MaxValue, int.MaxValue);
                }

                foreach (Options options in SbAllOptions)
                {
                    string text = CounterText.Counts(tally, options);
                    Expect(!text.Contains('-', StringComparison.Ordinal), $"{culture} {options} has a negative: {text}");
                    Expect(Regex.IsMatch(Regex.Replace(text, "<[^>]*>", string.Empty), @"^[0-9 \[\]]+$"), $"{culture} {options} odd digits: {text}");
                }

                string merged = CounterText.Counts(tally, Options.CountTempWithPermanent);
                string sum = (tally.PermanentTotal + tally.TempTotal).ToString(CultureInfo.InvariantCulture);
                Expect(merged.Contains(">" + sum + "<", StringComparison.Ordinal), $"merged total should be {sum}: {merged}");
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    private static void SbEdgeReflected()
    {
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type strip = typeof(RoR2.UI.ScoreboardStrip);
        Expect(strip.GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType == typeof(Inventory), "ScoreboardStrip.inventory");
        Expect(strip.GetField("itemCountText", BindingFlags.Instance | BindingFlags.Public) != null, "ScoreboardStrip.itemCountText");
        Expect(strip.GetMethod("SetMaster", inst, null, new[] { typeof(CharacterMaster) }, null) != null, "ScoreboardStrip.SetMaster");
        Expect(strip.GetMethod("UpdateItemCountText", inst, null, Type.EmptyTypes, null) != null, "ScoreboardStrip.UpdateItemCountText");
        Expect(typeof(Inventory).GetProperty("inventoryDisabled")?.PropertyType == typeof(bool), "Inventory.inventoryDisabled");
        Expect(typeof(Inventory).GetMethod("GetItemCountPermanent", new[] { typeof(ItemIndex) })?.ReturnType == typeof(int), "GetItemCountPermanent");
        Expect(typeof(Inventory).GetMethod("GetItemCountTemp", new[] { typeof(ItemIndex) })?.ReturnType == typeof(int), "GetItemCountTemp");
        Expect(typeof(Inventory).GetMethod("GetItemCountChanneled", new[] { typeof(ItemIndex) })?.ReturnType == typeof(int), "GetItemCountChanneled");
        Expect(typeof(Inventory).GetMethod("GetItemCountRented", new[] { typeof(ItemIndex) })?.ReturnType == typeof(int), "GetItemCountRented");
        Expect(typeof(RoR2.UI.ItemInventoryDisplay).GetMethod("GetTotalVisibleItemCount", Type.EmptyTypes)?.ReturnType == typeof(int), "GetTotalVisibleItemCount");
        Expect(typeof(Inventory).GetEvent("onInventoryChangedGlobal")?.EventHandlerType == typeof(Action<Inventory>), "onInventoryChangedGlobal");
    }

    // A tiny catalog, in ItemIndex order
    private static readonly (ItemTier Tier, bool Hidden, bool Removable)[] SbCatalog =
    {
        (ItemTier.Tier1, false, true),
        (ItemTier.Tier2, false, true),
        (ItemTier.Tier3, false, true),
        (ItemTier.VoidTier1, false, true),
        (ItemTier.Lunar, false, true),
        (ItemTier.NoTier, true, false),
        (ItemTier.NoTier, false, false),
    };

    private sealed class SbInventory
    {
        public readonly int[] Permanent = new int[SbCatalog.Length];
        public readonly int[] Temp = new int[SbCatalog.Length];
        public readonly int[] Rental = new int[SbCatalog.Length];
        public readonly int[] Channeled = new int[SbCatalog.Length];
        public readonly List<SbStrip> Displays = new List<SbStrip>();
        public bool Disabled;
        public SbMirror? Mod;

        // Inventory.UpdateEffectiveItemStacks, all four kinds added, zero while disabled for removable items
        public int Effective(int i)
        {
            long sum = (long)Permanent[i] + Channeled[i] + Temp[i] + Rental[i];
            int n = (int)Math.Clamp(sum, 0L, int.MaxValue);
            return Disabled && SbCatalog[i].Removable ? 0 : n;
        }

        // HandleInventoryChanged, the display's own handler before the global one
        public void Changed()
        {
            foreach (SbStrip strip in Displays)
            {
                strip.DisplayChanged();
            }

            Mod?.OnInventoryChanged(this);
        }
    }

    // The Items header's TMP text
    private sealed class SbLabel
    {
        public string? Text = "Items";
        public bool WordWrap = true;
        public int Writes;

        public void Write(string? text)
        {
            Text = text;
            Writes++;
        }
    }

    // A ScoreboardController with its Items header, its HUD's local user and the strips its allocator holds
    private sealed class SbBoard
    {
        public readonly List<SbStrip> Strips = new List<SbStrip>();
        public SbLabel? Label = new SbLabel();
        public bool HasHud = true;

        // HUD.localUserViewer.cachedMaster.inventory, null with no master
        public SbInventory? Local;
        public string Word = "Items";
        public bool Active;

        // LanguageTextMeshController.UpdateLabel
        public void GameWritesLabel()
        {
            Label?.Write(Word);
        }

        // OnCurrentLanguageChanged only finds active objects
        public void ChangeLanguage(string word)
        {
            Word = word;
            if (Active)
            {
                GameWritesLabel();
            }
        }

        // The controller's OnEnable rebuilds, calling the hook through SetMaster, the header's own OnEnable writes its label
        public void Open(SbMirror mod, bool labelFirst, params SbInventory[] living)
        {
            Active = true;
            foreach (SbStrip strip in Strips)
            {
                strip.Active = true;
                strip.DisplayChanged();
            }

            if (labelFirst)
            {
                GameWritesLabel();
            }

            Rebuild(mod, living);
            if (!labelFirst)
            {
                GameWritesLabel();
            }
        }

        public void Close()
        {
            Active = false;
            foreach (SbStrip strip in Strips)
            {
                strip.Active = false;
            }
        }

        // Only players with a body get a strip, the allocator keeps the first ones and destroys the rest
        public void Rebuild(SbMirror mod, params SbInventory[] living)
        {
            while (Strips.Count > living.Length)
            {
                SbStrip gone = Strips[Strips.Count - 1];
                gone.Inventory?.Displays.Remove(gone);
                Strips.RemoveAt(Strips.Count - 1);
            }

            while (Strips.Count < living.Length)
            {
                Strips.Add(new SbStrip { Board = this, Active = Active });
            }

            for (int i = 0; i < living.Length; i++)
            {
                Strips[i].SetMaster(living[i], mod);
            }
        }
    }

    private sealed class SbStrip
    {
        private readonly int[] displayStacks = new int[SbCatalog.Length];
        private int previousItemCount = int.MaxValue;
        public SbBoard? Board;
        public SbInventory? Inventory;
        public bool HasMaster;
        public bool Active = true;
        public string Text = string.Empty;
        public int TextWrites;

        public int GameTotal()
        {
            int n = 0;
            for (int i = 0; i < SbCatalog.Length; i++)
            {
                if (!SbCatalog[i].Hidden)
                {
                    n += displayStacks[i];
                }
            }

            return n;
        }

        // ItemInventoryDisplay.OnInventoryChanged, only while active
        public void DisplayChanged()
        {
            if (!Active)
            {
                return;
            }

            for (int i = 0; i < SbCatalog.Length; i++)
            {
                displayStacks[i] = Inventory?.Effective(i) ?? 0;
            }
        }

        // ScoreboardStrip.SetMaster with the hook on top
        public void SetMaster(SbInventory? inventory, SbMirror mod)
        {
            Inventory?.Displays.Remove(this);
            Inventory = inventory;
            HasMaster = inventory != null;
            if (inventory != null)
            {
                inventory.Displays.Add(this);
                DisplayChanged();
                UpdateItemCountText(mod);
            }
        }

        // ScoreboardStrip.UpdateItemCountText with the hook on top
        public void UpdateItemCountText(SbMirror mod)
        {
            int total = GameTotal();
            if (HasMaster && total != previousItemCount)
            {
                previousItemCount = total;
                Write(total.ToString(CultureInfo.InvariantCulture));
            }

            mod.Hook(this);
        }

        public void Write(string text)
        {
            Text = text;
            TextWrites++;
        }
    }

    // ItemCounters.OnInventoryChanged, OnUpdateItemCountText, UpdateRow, UpdateHeader, HeaderOf, the hand backs and Count,
    // line for line, Time.frameCount is FrameCount and Time.unscaledTime is Now
    private sealed class SbMirror
    {
        private readonly ConditionalWeakTable<SbStrip, Row> rows = new ConditionalWeakTable<SbStrip, Row>();
        private readonly ConditionalWeakTable<SbBoard, Header> headers = new ConditionalWeakTable<SbBoard, Header>();
        private readonly ConditionalWeakTable<SbInventory, Changes> changes = new ConditionalWeakTable<SbInventory, Changes>();
        private readonly CounterText.Tally tally = new CounterText.Tally();
        public Options Options = CounterDefaults;
        public bool CountHidden;
        public float Now;
        public int FrameCount;
        public bool Failed;
        public bool HandBackFailed;
        public bool ThrowNext;
        public bool ThrowInHeader;
        public int Warnings;

        public void OnInventoryChanged(SbInventory inventory)
        {
            if (changes.TryGetValue(inventory, out Changes? seen))
            {
                seen.Count++;
            }
        }

        public void Hook(SbStrip self)
        {
            if (Failed)
            {
                HandBack(self);
                return;
            }

            try
            {
                UpdateRow(self);
                UpdateHeader(self);
            }
            catch (Exception)
            {
                Failed = true;
                HandBack(self);
            }
        }

        private void UpdateRow(SbStrip strip)
        {
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new InvalidOperationException("model failure");
            }

            if (strip.Inventory is not SbInventory inventory)
            {
                return;
            }

            Row row = rows.GetOrCreateValue(strip);
            if (inventory.Disabled)
            {
                HandBackRow(strip, row);
                return;
            }

            Options options = Options;
            if (row.Stale(inventory, changes.GetOrCreateValue(inventory).Count, options, Now))
            {
                row.Text = CounterText.Row(Count(inventory), options);
            }

            if (!string.Equals(strip.Text, row.Text, StringComparison.Ordinal))
            {
                strip.Write(row.Text!);
            }
        }

        private void UpdateHeader(SbStrip strip)
        {
            Header? header = HeaderOf(strip);
            if (header == null)
            {
                return;
            }

            if (!header.Searched)
            {
                header.Searched = true;
                header.Label = header.Board!.Label;
                header.Wrap = header.Label != null && header.Label.WordWrap;
                header.HasHud = header.Board.HasHud;
                if (header.Label == null || !header.HasHud)
                {
                    Warnings++;
                }
            }

            if (header.Label == null || !header.HasHud)
            {
                return;
            }

            string? current = header.Label.Text;
            if (header.Text == null || !string.Equals(current, header.Text, StringComparison.Ordinal))
            {
                header.GameLabel = current;
                header.Text = null;
                header.Label.WordWrap = header.Wrap;
            }

            Options options = Options;
            SbInventory? inventory = header.Board!.Local;
            if (inventory == null || inventory.Disabled || (options & Options.TierCounts) == 0)
            {
                HandBackHeader(header);
                return;
            }

            if (header.Stale(inventory, changes.GetOrCreateValue(inventory).Count, options, Now))
            {
                // Stands in for anything in Count or CounterText.Header throwing
                if (ThrowInHeader)
                {
                    ThrowInHeader = false;
                    throw new InvalidOperationException("model header failure");
                }

                header.Text = CounterText.Header(header.GameLabel, Count(inventory), options);
            }

            if (header.Text != null && !string.Equals(header.Label.Text, header.Text, StringComparison.Ordinal))
            {
                header.Label.WordWrap = false;
                header.Label.Write(header.Text);
            }
        }

        private Header? HeaderOf(SbStrip strip)
        {
            Row row = rows.GetOrCreateValue(strip);
            if (row.Board == null)
            {
                row.Board = strip.Board;
                if (row.Board == null)
                {
                    return null;
                }
            }

            Header header = headers.GetOrCreateValue(row.Board);
            header.Board = row.Board;
            return header;
        }

        private void HandBack(SbStrip strip)
        {
            if (HandBackFailed || !rows.TryGetValue(strip, out Row? row))
            {
                return;
            }

            HandBackRow(strip, row);
            if (row.Board != null && headers.TryGetValue(row.Board, out Header? header))
            {
                HandBackHeader(header);
            }
        }

        private void HandBackRow(SbStrip strip, Row row)
        {
            if (HandBackFailed || row.Text == null)
            {
                return;
            }

            if (string.Equals(strip.Text, row.Text, StringComparison.Ordinal))
            {
                strip.Write(strip.GameTotal().ToString(CultureInfo.InvariantCulture));
            }

            row.Text = null;
        }

        private void HandBackHeader(Header header)
        {
            if (HandBackFailed || header.Label == null)
            {
                return;
            }

            if (header.Text != null && string.Equals(header.Label.Text, header.Text, StringComparison.Ordinal))
            {
                header.Label.Write(header.GameLabel ?? string.Empty);
            }

            header.Text = null;
            if (header.Label.WordWrap != header.Wrap)
            {
                header.Label.WordWrap = header.Wrap;
            }
        }

        public CounterText.Tally Count(SbInventory inventory)
        {
            tally.Clear();
            for (int i = 0; i < SbCatalog.Length; i++)
            {
                if (SbCatalog[i].Hidden && !CountHidden)
                {
                    continue;
                }

                long held = (long)inventory.Permanent[i] + inventory.Channeled[i] + inventory.Rental[i];
                int permanent = (int)Math.Min(int.MaxValue, held);
                int temp = inventory.Temp[i];
                if (permanent != 0 || temp != 0)
                {
                    tally.Add(SbCatalog[i].Tier, permanent, temp);
                }
            }

            return tally;
        }

        private class Shown
        {
            private SbInventory? inventory;
            private int seenChanges;
            private Options options;
            private float nextRefresh;
            public string? Text;

            public bool Stale(SbInventory current, int changeCount, Options currentOptions, float now)
            {
                if (Text != null
                    && ReferenceEquals(inventory, current)
                    && seenChanges == changeCount
                    && options == currentOptions
                    && now < nextRefresh)
                {
                    return false;
                }

                inventory = current;
                seenChanges = changeCount;
                options = currentOptions;
                nextRefresh = now + 1f;
                return true;
            }
        }

        private sealed class Row : Shown
        {
            public SbBoard? Board;
        }

        private sealed class Header : Shown
        {
            public SbBoard? Board;
            public bool Searched;
            public SbLabel? Label;
            public bool Wrap;
            public bool HasHud;
            public string? GameLabel;
        }

        private sealed class Changes
        {
            public int Count;
        }
    }

    private static CounterText.Tally SbTally(SbInventory inventory)
    {
        var tally = new CounterText.Tally();
        for (int i = 0; i < SbCatalog.Length; i++)
        {
            int held = inventory.Permanent[i] + inventory.Channeled[i] + inventory.Rental[i];
            if (!SbCatalog[i].Hidden && (held != 0 || inventory.Temp[i] != 0))
            {
                tally.Add(SbCatalog[i].Tier, held, inventory.Temp[i]);
            }
        }

        return tally;
    }

    // The text the row should hold, built from what the inventory holds now
    private static string SbFresh(SbInventory inventory, Options options)
    {
        return CounterText.Row(SbTally(inventory), options);
    }

    // What the header should read now, ours or the game's word
    private static string SbFreshHeader(SbBoard board, Options options)
    {
        if (board.Local == null || board.Local.Disabled || (options & Options.TierCounts) == 0)
        {
            return board.Word;
        }

        return CounterText.Header(board.Word, SbTally(board.Local), options)!;
    }

    private static SbInventory SbInv(SbMirror mod, int white = 0, int green = 0, int red = 0, int voidWhite = 0, int lunar = 0)
    {
        var inventory = new SbInventory { Mod = mod };
        inventory.Permanent[0] = white;
        inventory.Permanent[1] = green;
        inventory.Permanent[2] = red;
        inventory.Permanent[3] = voidWhite;
        inventory.Permanent[4] = lunar;
        return inventory;
    }

    private static void SbFrame(SbMirror mod, float seconds, params SbStrip[] strips)
    {
        mod.Now += seconds;
        mod.FrameCount++;
        foreach (SbStrip strip in strips)
        {
            strip.UpdateItemCountText(mod);
        }
    }

    private static void SbEdgeTierSwap()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 3, green: 1);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);

        a.Permanent[0]--;
        a.Permanent[3]++;
        a.Changed();
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);

        // Removed and added as two messages a frame apart
        a.Permanent[1]--;
        a.Changed();
        SbFrame(mod, 0.016f, strip);
        a.Permanent[3]++;
        a.Changed();
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);
    }

    private static void SbEdgeSettingsChange()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, red: 1, lunar: 1);
        a.Temp[1] = 2;
        a.Temp[4] = 1;
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        var random = new Random(7);
        for (int step = 0; step < 200; step++)
        {
            mod.Options = SbAllOptions[random.Next(SbAllOptions.Length)];
            SbFrame(mod, 0.001f, strip);
            Equal(SbFresh(a, mod.Options), strip.Text);
        }
    }

    private static void SbEdgeMasterSwap()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 3);
        SbInventory b = SbInv(mod, red: 3);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        strip.SetMaster(b, mod);
        Equal(SbFresh(b, mod.Options), strip.Text);
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(b, mod.Options), strip.Text);
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);

        // A's change while B was shown, then back to A within the timer
        strip.SetMaster(b, mod);
        a.Permanent[0] = 1;
        a.Permanent[2] = 2;
        a.Changed();
        strip.SetMaster(a, mod);
        Equal(SbFresh(a, mod.Options), strip.Text);
    }

    private static void SbEdgeTempDecay()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 1);
        a.Temp[0] = 3;
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);

        // As a client the stacks drop in SyncStackToDecay with no change event
        a.Temp[0] = 0;
        for (int frame = 0; frame < 70; frame++)
        {
            SbFrame(mod, 1f / 60f, strip);
        }

        Equal(SbFresh(a, mod.Options), strip.Text);
        Expect(!strip.Text.Contains('[', StringComparison.Ordinal), $"brackets stay after temp items ran out: {strip.Text}");
    }

    private static void SbEdgeGameWrites()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 1);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        for (int step = 0; step < 50; step++)
        {
            a.Permanent[step % 5] += step % 3 == 0 ? -1 : 1;
            a.Permanent[step % 5] = Math.Max(0, a.Permanent[step % 5]);
            a.Changed();
            SbFrame(mod, 0.016f, strip);
            Expect(!Regex.IsMatch(strip.Text, "^[0-9]+$"), $"step {step} left the game's plain total: {strip.Text}");
            Equal(SbFresh(a, mod.Options), strip.Text);
        }
    }

    private static void SbEdgeClosedChanges()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);

        strip.Active = false;
        a.Permanent[0] = 1;
        a.Permanent[3] = 1;
        a.Changed();
        mod.Now += 0.1f;

        // ScoreboardController.OnEnable rebuilds, the display catches up in its own OnEnable
        strip.Active = true;
        strip.DisplayChanged();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);
    }

    private static void SbEdgeDisabled()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, red: 1);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);

        a.Disabled = true;
        a.Changed();
        SbFrame(mod, 0.016f, strip);
        Equal(strip.GameTotal().ToString(CultureInfo.InvariantCulture), strip.Text);

        a.Disabled = false;
        a.Changed();
        SbFrame(mod, 0.016f, strip);
        Equal(SbFresh(a, mod.Options), strip.Text);
    }

    // The game's count stays the same, so it never writes, ours has to hand it back
    private static void SbEdgeDisabledSameCount()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);

        // Two items the game won't disable, canRemove off or tagged CannotDisable, so its count reads 2 as A's did
        SbInventory b = SbInv(mod, green: 5);
        b.Permanent[6] = 2;
        b.Disabled = true;
        strip.SetMaster(b, mod);
        SbFrame(mod, 0.016f, strip);
        Expect(strip.GameTotal() == 2, "setup, the game's count should match A's");
        Equal(strip.GameTotal().ToString(CultureInfo.InvariantCulture), strip.Text);
    }

    // The log says counts go back to the game's, so the row mustn't keep our last text until the total moves
    private static void SbEdgeFailedLeavesOurs()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);

        a.Permanent[0] = 1;
        a.Permanent[3] = 1;
        a.Changed();
        mod.ThrowNext = true;
        SbFrame(mod, 0.016f, strip);
        Expect(mod.Failed, "setup, the model should have failed");
        SbFrame(mod, 2f, strip);
        Equal(strip.GameTotal().ToString(CultureInfo.InvariantCulture), strip.Text);
    }

    // The game's count is the effective stack, which also holds ItemStorageType.Rental and Channeled
    private static void SbEdgeRentalChanneled()
    {
        string[] kinds = Enum.GetNames(typeof(ItemStorageType));
        Expect(kinds.Contains("Rental") && kinds.Contains("Channeled"), "setup, the game has no rented or shared items any more");

        var mod = new SbMirror { Options = Options.TierCounts | Options.CountTempWithPermanent };
        SbInventory a = SbInv(mod, white: 2);
        a.Rental[1] = 1;
        a.Channeled[2] = 3;
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        string game = strip.GameTotal().ToString(CultureInfo.InvariantCulture);
        Expect(
            strip.Text.Contains("<color=#FFFFFF>" + game + "</color>", StringComparison.Ordinal),
            $"the game counts {game} items with 1 rented and 3 shared, ours: {strip.Text}");
    }

    private static void SbEdgeHiddenItems()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 1);
        a.Permanent[5] = 4;
        a.Temp[5] = 4;
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Expect(!strip.Text.Contains('[', StringComparison.Ordinal), $"hidden temp items made brackets: {strip.Text}");
        Expect(strip.Text.Contains("<color=#FFFFFF>1</color>", StringComparison.Ordinal), $"hidden items in the total: {strip.Text}");
    }

    private static void SbEdgeHiddenItemsUnhidden()
    {
        var mod = new SbMirror { CountHidden = true };
        SbInventory a = SbInv(mod, white: 1);
        a.Permanent[5] = 4;
        var strip = new SbStrip();
        strip.SetMaster(a, mod);
        SbFrame(mod, 0.016f, strip);
        Expect(strip.Text.Contains("<color=#FFFFFF>5</color>", StringComparison.Ordinal), $"hidden items left out of the total: {strip.Text}");
    }

    private const float SbDt = 1f / 60f;

    // One frame, every open board's strips run Update in turn
    private static void SbTick(SbMirror mod, params SbBoard[] boards)
    {
        mod.Now += SbDt;
        mod.FrameCount++;
        foreach (SbBoard board in boards)
        {
            if (!board.Active)
            {
                continue;
            }

            foreach (SbStrip strip in board.Strips.ToArray())
            {
                strip.UpdateItemCountText(mod);
            }
        }
    }

    // HUD.Update opens the boards, then their strips run Update in that same frame
    private static void SbOpenFrame(SbMirror mod, bool labelFirst, SbInventory[] living, params SbBoard[] boards)
    {
        mod.Now += SbDt;
        mod.FrameCount++;
        foreach (SbBoard board in boards)
        {
            board.Open(mod, labelFirst, living);
        }

        foreach (SbBoard board in boards)
        {
            foreach (SbStrip strip in board.Strips.ToArray())
            {
                strip.UpdateItemCountText(mod);
            }
        }
    }

    private static SbBoard SbOpened(SbMirror mod, SbInventory? local, params SbInventory[] living)
    {
        var board = new SbBoard { Local = local };
        SbOpenFrame(mod, true, living, board);
        SbTick(mod, board);
        return board;
    }

    private static void SbHeaderIs(SbBoard board, Options options, string when)
    {
        string expected = SbFreshHeader(board, options);
        if (!string.Equals(board.Label!.Text, expected, StringComparison.Ordinal))
        {
            throw new AssertFail($"{when}: expected [{expected}] got [{board.Label.Text}]");
        }
    }

    private static void SbEdgeHeaderReflected()
    {
        Expect(typeof(RoR2.UI.HUD).GetProperty("localUserViewer")?.PropertyType == typeof(LocalUser), "HUD.localUserViewer");
        Type? cached = typeof(LocalUser).GetProperty("cachedMaster")?.PropertyType ?? typeof(LocalUser).GetField("cachedMaster")?.FieldType;
        Expect(cached == typeof(CharacterMaster), "LocalUser.cachedMaster");
        Expect(typeof(CharacterMaster).GetProperty("inventory")?.PropertyType == typeof(Inventory), "CharacterMaster.inventory");
        Expect(typeof(RoR2.UI.ScoreboardStrip).GetField("itemInventoryDisplay")?.FieldType == typeof(RoR2.UI.ItemInventoryDisplay), "ScoreboardStrip.itemInventoryDisplay");
        Type? text = Assembly.Load("Unity.TextMeshPro").GetType("TMPro.TMP_Text");
        Expect(text?.GetProperty("enableWordWrapping")?.PropertyType == typeof(bool), "TMP_Text.enableWordWrapping");
        Expect(text?.GetProperty("text")?.PropertyType == typeof(string), "TMP_Text.text");
    }

    // RoR2's SCOREBOARD_HEADER_ITEMS in every language it ships, none ends in a colon
    private static readonly string[] SbGameWords =
    {
        "Items", "Gegenstände", "Objetos", "Objets", "Oggetti", "アイテム", "아이템", "Itens", "Предметы", "Nesneler", "物品",
    };

    private static void SbEdgeHeaderWords()
    {
        CounterText.Tally tally = TallyOf((ItemTier.Tier1, 2, 1), (ItemTier.VoidTier2, 1, 0));
        string counts = CounterText.Counts(tally, CounterDefaults);
        foreach (string word in SbGameWords)
        {
            foreach (string label in new[] { word, word + "\n", word + ":", " " + word + " :\r\n", "\t" + word + "::" })
            {
                Equal(word + ": " + counts, CounterText.Header(label, tally, CounterDefaults)!);
            }
        }

        Equal(counts, CounterText.Header(":", tally, CounterDefaults)!);
        Equal(counts, CounterText.Header(" : \n", tally, CounterDefaults)!);
    }

    private static void SbEdgeHeaderVoidSwap()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 3, green: 1);
        SbBoard board = SbOpened(mod, a, a);
        SbHeaderIs(board, mod.Options, "open");

        a.Permanent[0]--;
        a.Permanent[3]++;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "the frame after the swap");
        Expect(board.Label!.Text!.Contains("<color=#ED7FCD>1</color>", StringComparison.Ordinal), $"no void count: {board.Label.Text}");
        Equal(SbFresh(a, mod.Options), board.Strips[0].Text);

        // Removed and added as two messages a frame apart
        a.Permanent[1]--;
        a.Changed();
        SbTick(mod, board);
        a.Permanent[3]++;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "after two messages");
    }

    // Rebuild skips players with no body, so a dead local player has no row of their own
    private static void SbEdgeHeaderLocalDead()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 2);
        SbInventory b = SbInv(mod, red: 1);
        SbBoard board = SbOpened(mod, a, a, b);
        board.Close();
        SbOpenFrame(mod, true, new[] { b }, board);
        SbTick(mod, board);
        Expect(board.Strips.Count == 1, "setup, only b should have a row");
        SbHeaderIs(board, mod.Options, "reopened with only b's row");

        a.Permanent[0]--;
        a.Permanent[3]++;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "a's void swap with no row of its own");

        a.Temp[1] = 2;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "a's temp items with no row of its own");
    }

    private static void SbEdgeHeaderEachOpen()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 1);
        SbBoard board = SbOpened(mod, a, a);
        for (int round = 0; round < 6; round++)
        {
            board.Close();
            a.Permanent[round % 5]++;
            a.Changed();
            mod.Now += 3f;
            SbOpenFrame(mod, true, new[] { a }, board);
            SbHeaderIs(board, mod.Options, $"open {round}");
            SbTick(mod, board);
            SbHeaderIs(board, mod.Options, $"the frame after open {round}");
            int words = Regex.Matches(board.Label!.Text!, "Items").Count;
            Expect(words == 1, $"open {round} shows the word {words} times: {board.Label.Text}");
        }
    }

    // Assumes Unity runs the controller's OnEnable on ScoreboardPanel before the header label's, parent first
    // Its Rebuild calls SetMaster, so the hook takes this frame's header pass before the label is rewritten
    private static void SbEdgeHeaderOpenFrame()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, red: 1);
        SbBoard board = SbOpened(mod, a, a);
        board.Close();
        mod.Now += 3f;
        SbOpenFrame(mod, false, new[] { a }, board);
        SbHeaderIs(board, mod.Options, "end of the frame the scoreboard opened, controller OnEnable first");
    }

    private static void SbEdgeHeaderLanguageOpen()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbBoard board = SbOpened(mod, a, a);
        board.ChangeLanguage("Objets");
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "the frame after a change to French");
        Expect(!board.Label!.Text!.Contains("Items", StringComparison.Ordinal), $"the old word stayed: {board.Label.Text}");

        board.ChangeLanguage("Items");
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "back to English");
        Expect(!board.Label.Text!.Contains("Objets", StringComparison.Ordinal), $"the old word stayed: {board.Label.Text}");
    }

    private static void SbEdgeHeaderLanguageClosed()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbBoard board = SbOpened(mod, a, a);
        board.Close();
        board.ChangeLanguage("Предметы");
        Expect(board.Label!.Text!.StartsWith("Items: ", StringComparison.Ordinal), "setup, a closed label isn't rewritten");

        mod.Options &= ~Options.TierCounts;
        SbOpenFrame(mod, false, new[] { a }, board);
        SbTick(mod, board);
        Equal("Предметы", board.Label.Text!);
        Expect(board.Label.WordWrap, "wrapping off on the game's label");

        mod.Options |= Options.TierCounts;
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "Tier Counts back on");

        board.Close();
        board.ChangeLanguage("Objets");
        SbOpenFrame(mod, true, new[] { a }, board);
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "reopened after a change to French, label first");
    }

    private static void SbEdgeHeaderToggle()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, red: 1, lunar: 1);
        a.Temp[0] = 1;
        SbBoard board = SbOpened(mod, a, a);
        var random = new Random(11);
        for (int step = 0; step < 200; step++)
        {
            mod.Options = SbAllOptions[random.Next(SbAllOptions.Length)];
            SbTick(mod, board);
            SbHeaderIs(board, mod.Options, $"step {step} with {mod.Options}");
            bool ours = (mod.Options & Options.TierCounts) != 0;
            Expect(board.Label!.WordWrap == !ours, $"step {step} with {mod.Options} has wrapping {(board.Label.WordWrap ? "on" : "off")}");
        }
    }

    // The game's label wraps, and only a label still showing ours gets its wrapping back
    private static void SbEdgeHeaderWrapAfterOpen()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        SbBoard board = SbOpened(mod, a, a);
        Expect(!board.Label!.WordWrap, "setup, ours should be on one line");
        board.Close();
        mod.Options &= ~Options.TierCounts;

        // The label's OnEnable writes before the controller's Rebuild reaches the hook
        SbOpenFrame(mod, true, new[] { a }, board);
        SbTick(mod, board);
        Equal("Items", board.Label.Text!);
        Expect(board.Label.WordWrap, "the game's label was left with word wrapping off");
    }

    private static void SbEdgeHeaderWrapAfterLanguage()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        SbBoard board = SbOpened(mod, a, a);
        board.ChangeLanguage("Objets");
        mod.Options &= ~Options.TierCounts;
        SbTick(mod, board);
        Equal("Objets", board.Label!.Text!);
        Expect(board.Label.WordWrap, "the game's label was left with word wrapping off");
    }

    private static void SbEdgeHeaderWrapAfterFailure()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2);
        SbBoard board = SbOpened(mod, a, a);
        board.ChangeLanguage("Objets");
        mod.ThrowInHeader = true;
        SbTick(mod, board);
        Expect(mod.Failed, "setup, the model should have failed");
        Equal("Objets", board.Label!.Text!);
        Expect(board.Label.WordWrap, "the game's label was left with word wrapping off after the failure");
    }

    private static void SbEdgeHeaderRowFailure()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbBoard board = SbOpened(mod, a, a);
        mod.ThrowNext = true;
        SbTick(mod, board);
        Expect(mod.Failed, "setup, the model should have failed");
        Equal("Items", board.Label!.Text!);
        Expect(board.Label.WordWrap, "wrapping off after the failure");
        Equal(board.Strips[0].GameTotal().ToString(CultureInfo.InvariantCulture), board.Strips[0].Text);

        a.Permanent[3]++;
        a.Changed();
        SbTick(mod, board);
        Equal("Items", board.Label.Text!);
        Equal(board.Strips[0].GameTotal().ToString(CultureInfo.InvariantCulture), board.Strips[0].Text);

        board.Close();
        SbOpenFrame(mod, false, new[] { a }, board);
        SbTick(mod, board);
        Equal("Items", board.Label.Text!);
    }

    private static void SbEdgeHeaderFailure()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbBoard board = SbOpened(mod, a, a);
        a.Permanent[0]++;
        a.Changed();
        mod.ThrowInHeader = true;
        SbTick(mod, board);
        Expect(mod.Failed, "setup, the model should have failed");
        Equal("Items", board.Label!.Text!);
        Expect(board.Label.WordWrap, "wrapping off after the failure");
        Equal(board.Strips[0].GameTotal().ToString(CultureInfo.InvariantCulture), board.Strips[0].Text);
    }

    // Time.frameCount is shared, the dedupe is per board
    private static void SbEdgeHeaderSplitScreen()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbInventory b = SbInv(mod, red: 2, lunar: 1);
        var one = new SbBoard { Local = a };
        var two = new SbBoard { Local = b };
        SbOpenFrame(mod, true, new[] { a, b }, one, two);
        SbTick(mod, one, two);
        SbHeaderIs(one, mod.Options, "first player's board");
        SbHeaderIs(two, mod.Options, "second player's board");
        Expect(one.Label!.Text != two.Label!.Text, "both boards show the same counts");

        a.Permanent[0]--;
        a.Permanent[3]++;
        a.Changed();
        SbTick(mod, two, one);
        SbHeaderIs(one, mod.Options, "first board after its void swap, second board's strips first");
        SbHeaderIs(two, mod.Options, "second board after the first player's swap");

        b.Temp[1] = 3;
        b.Changed();
        SbTick(mod, one, two);
        SbHeaderIs(one, mod.Options, "first board after the second player's temp items");
        SbHeaderIs(two, mod.Options, "second board after its temp items");

        int oneWrites = one.Label.Writes;
        int twoWrites = two.Label.Writes;
        for (int frame = 0; frame < 120; frame++)
        {
            SbTick(mod, one, two);
        }

        Expect(one.Label.Writes == oneWrites && two.Label.Writes == twoWrites, "headers rewritten with nothing changed");
    }

    private static void SbEdgeHeaderMasterSwap()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 3);
        SbInventory b = SbInv(mod, red: 3);
        SbBoard board = SbOpened(mod, a, a, b);
        board.Local = b;
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "moved to b");
        board.Local = a;
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "back to a");
        board.Local = null;
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "no master");
        Expect(board.Label!.WordWrap, "wrapping off with no master");
        board.Local = b;
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "b again");
    }

    private static void SbEdgeHeaderDisabled()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, red: 1);
        SbBoard board = SbOpened(mod, a, a);
        a.Disabled = true;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "hidden");
        Expect(board.Label!.WordWrap, "wrapping off while hidden");
        a.Disabled = false;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "shown again");
        Expect(!board.Label.WordWrap, "ours wraps");
    }

    private static void SbEdgeHeaderTempDecay()
    {
        var mod = new SbMirror { Options = CounterDefaults | Options.TempPerTier };
        SbInventory a = SbInv(mod, white: 1);
        a.Temp[0] = 3;
        SbBoard board = SbOpened(mod, a, a);
        Expect(board.Label!.Text!.Contains("[3]", StringComparison.Ordinal), $"setup, no temp count: {board.Label.Text}");
        a.Temp[0] = 0;
        for (int frame = 0; frame < 70; frame++)
        {
            SbTick(mod, board);
        }

        SbHeaderIs(board, mod.Options, "70 frames after the temp items ran out");
        Expect(!board.Label.Text!.Contains('[', StringComparison.Ordinal), $"brackets stay: {board.Label.Text}");
    }

    private static void SbEdgeHeaderNoLabel()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        var noLabel = new SbBoard { Local = a, Label = null };
        var noHud = new SbBoard { Local = a, HasHud = false };
        SbOpenFrame(mod, true, new[] { a }, noLabel, noHud);
        for (int frame = 0; frame < 30; frame++)
        {
            SbTick(mod, noLabel, noHud);
        }

        Expect(!mod.Failed, "a missing header counted as a failure");
        Equal(SbFresh(a, mod.Options), noLabel.Strips[0].Text);
        Equal(SbFresh(a, mod.Options), noHud.Strips[0].Text);
        Equal("Items", noHud.Label!.Text!);
        Expect(mod.Warnings == 2, $"{mod.Warnings} warnings, expected one per board");
    }

    private static void SbEdgeHeaderQuiet()
    {
        var mod = new SbMirror();
        SbInventory[] living = { SbInv(mod, white: 2), SbInv(mod, green: 1), SbInv(mod, red: 4), SbInv(mod, lunar: 1) };
        living[0].Temp[1] = 2;
        SbBoard board = SbOpened(mod, living[0], living);
        int writes = board.Label!.Writes;
        int[] stripWrites = board.Strips.Select(s => s.TextWrites).ToArray();
        for (int frame = 0; frame < 300; frame++)
        {
            SbTick(mod, board);
        }

        Expect(board.Label.Writes == writes, $"{board.Label.Writes - writes} header writes over 5 s with nothing changed");
        Expect(board.Strips.Select(s => s.TextWrites).SequenceEqual(stripWrites), "rows rewritten with nothing changed");

        living[0].Permanent[0]++;
        living[0].Changed();
        SbTick(mod, board);
        Expect(board.Label.Writes == writes + 1, $"{board.Label.Writes - writes} header writes in the frame after one change");
    }

    private static void SbEdgeHeaderNoRows()
    {
        foreach (bool labelFirst in new[] { true, false })
        {
            var mod = new SbMirror();
            SbInventory a = SbInv(mod, white: 2);
            SbBoard board = SbOpened(mod, a, a);
            board.Close();
            a.Permanent[0] += 3;
            a.Changed();
            SbOpenFrame(mod, labelFirst, Array.Empty<SbInventory>(), board);
            SbTick(mod, board);
            SbTick(mod, board);
            Equal("Items", board.Label!.Text!);
        }
    }

    private static void SbEdgeHeaderRebuild()
    {
        var mod = new SbMirror();
        SbInventory a = SbInv(mod, white: 2, green: 1);
        SbInventory b = SbInv(mod, red: 1);
        SbInventory c = SbInv(mod, lunar: 2);
        SbBoard board = SbOpened(mod, a, a, b);

        // A player joins after this frame's header pass, Rebuild's SetMaster calls land in the same frame
        SbTick(mod, board);
        a.Permanent[0]--;
        a.Permanent[3]++;
        a.Changed();
        board.Rebuild(mod, a, b, c);
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "the frame after a rebuild that grew the rows");

        // a dead and b gone, c's row is the one left
        board.Rebuild(mod, c);
        a.Permanent[2]++;
        a.Changed();
        SbTick(mod, board);
        SbHeaderIs(board, mod.Options, "the frame after a rebuild that left only c");
    }

    private static void SbEdgeHeaderFuzz()
    {
        var random = new Random(2026);
        var mod = new SbMirror();
        SbInventory[] players = { SbInv(mod, white: 2, green: 1), SbInv(mod, red: 1), SbInv(mod, white: 5, lunar: 1) };
        string[] words = { "Items", "Objets", "Предметы", "アイテム" };
        var board = new SbBoard { Local = players[0] };
        SbInventory[] living = players;
        SbOpenFrame(mod, true, living, board);
        var history = new List<string>();
        for (int step = 0; step < 4000; step++)
        {
            string did;
            SbInventory some = players[random.Next(players.Length)];
            int slot = random.Next(5);
            switch (random.Next(9))
            {
                case 0:
                    if (board.Active)
                    {
                        board.Close();
                        did = "close";
                    }
                    else
                    {
                        living = players.Where(_ => random.Next(3) > 0).ToArray();
                        bool labelFirst = random.Next(2) == 0;
                        mod.Now += SbDt;
                        mod.FrameCount++;
                        board.Open(mod, labelFirst, living);
                        did = $"open {(labelFirst ? "label" : "controller")} first with {living.Length} rows";
                    }

                    break;
                case 1:
                    board.ChangeLanguage(words[random.Next(words.Length)]);
                    did = "language " + board.Word;
                    break;
                case 2:
                    mod.Options = SbAllOptions[random.Next(SbAllOptions.Length)];
                    did = "options " + mod.Options;
                    break;
                case 3:
                    some.Permanent[slot] = Math.Max(0, some.Permanent[slot] + random.Next(-1, 2));
                    some.Changed();
                    did = "count change";
                    break;
                case 4:
                    some.Temp[slot] = random.Next(0, 3);
                    some.Changed();
                    did = "temp change";
                    break;
                case 5:
                    some.Disabled = !some.Disabled;
                    some.Changed();
                    did = "hidden " + some.Disabled;
                    break;
                case 6:
                    board.Local = random.Next(4) == 0 ? null : some;
                    did = "local master moved";
                    break;
                case 7:
                    if (board.Active)
                    {
                        living = players.Where(_ => random.Next(3) > 0).ToArray();
                        board.Rebuild(mod, living);
                    }

                    did = $"rebuild with {living.Length} rows";
                    break;
                default:
                    if (some.Permanent[0] > 0)
                    {
                        some.Permanent[0]--;
                        some.Permanent[3]++;
                        some.Changed();
                    }

                    did = "void swap";
                    break;
            }

            history.Add(did);
            if (history.Count > 8)
            {
                history.RemoveAt(0);
            }

            SbTick(mod, board);
            SbTick(mod, board);

            // With no rows nothing of ours runs, that's checked apart
            if (!board.Active || board.Strips.Count == 0)
            {
                continue;
            }

            string expected = SbFreshHeader(board, mod.Options);
            if (!string.Equals(board.Label!.Text, expected, StringComparison.Ordinal))
            {
                throw new AssertFail($"step {step} after {string.Join(", ", history)}: header expected [{expected}] got [{board.Label.Text}]");
            }

            foreach (SbStrip strip in board.Strips)
            {
                string row = strip.Inventory!.Disabled
                    ? strip.GameTotal().ToString(CultureInfo.InvariantCulture)
                    : SbFresh(strip.Inventory, mod.Options);
                if (!string.Equals(strip.Text, row, StringComparison.Ordinal))
                {
                    throw new AssertFail($"step {step} after {string.Join(", ", history)}: row expected [{row}] got [{strip.Text}]");
                }
            }
        }
    }
}
