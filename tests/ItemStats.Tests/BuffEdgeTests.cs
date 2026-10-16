using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using JustBeBetter.BuffInfo;
using RoR2;

// Edge cases for Buff Info
// The frame tests run a model, the game's BuffDisplay.UpdateLayout, its icon pool and BuffIcon.UpdateIcon as read
// from the IL, with BuffDetails' postfix mirrored on top, since neither runs outside Unity
// The text comes from the linked BuffText, so a text change shows here, a postfix change has to be mirrored
internal static partial class Program
{
    private static void RunBuffEdge()
    {
        Test("Buff edge: our stack text matches the game's own AppendInt for every count and offset", BuffEdgeStackMatchesGame);
        Test("Buff edge: a falling timer never reads higher a moment later, in either mode", BuffEdgeSecondsMonotonic);
        Test("Buff edge: decimal timers never read more than is left, whole ones never less", BuffEdgeSecondsBounds);
        Test("Buff edge: odd timers read short and in invariant digits under a comma culture", BuffEdgeOddTimers);
        Test("Buff edge: Longest keeps infinite and negative entries, drops only NaN", BuffEdgeLongestOdd);
        Test("Buff edge: Tidy on odd names never leaves double spaces, edges or an empty title", BuffEdgeTidyOdd);
        Test("Buff edge: the reflected fields exist in the installed game with the types BuffDetails casts to", BuffEdgeReflectedFields);
        Test("Buff edge model: a count change while timed shows the new count under the timer", BuffEdgeCountChange);
        Test("Buff edge model: turning timers off mid-buff hands back the game's text, on again brings the timer", BuffEdgeToggleTimers);
        Test("Buff edge model: a timer ending under permanent stacks hands the text back", BuffEdgeTimerEndsStacksStay);
        Test("Buff edge model: as a client the icons read as the game has them", BuffEdgeClient);
        Test("Buff edge model: random play without icon reuse always shows the timer or the game's text", BuffEdgeFuzzNoReuse);
        Test("Buff edge model: a pooled icon reused for a buff with the same timer text still shows the timer", BuffEdgePoolReuseSameText);
        Test("Buff edge model: hiding and showing the buff bar within one timer step keeps the timer", BuffEdgeHideShowSameText);
        Test("Buff edge model: random play with the shared icon pool always shows the timer or the game's text", BuffEdgeFuzzWithPool);
        Test("Buff edge model: random play with the bar hidden and shown always shows the timer or the game's text", BuffEdgeFuzzHideShow);
        Test("Buff edge: the table builds with missing content, skips it, and finds every buff that's there", BuffEdgeDefinitionsMissingContent);
        Test("Buff edge: a ported description that's empty or only repeats its name shows the title alone", BuffEdgeDescriptionsNotEmpty);
    }

    // The game's own stack text, through StringBuilderExtensions.AppendInt as BuffIcon.UpdateIcon calls it
    private static string GameAppendInt(int value)
    {
        MethodInfo append = typeof(BuffDef).Assembly.GetType("StringBuilderExtensions")!
            .GetMethod("AppendInt", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var sb = new StringBuilder();
        append.Invoke(null, new object[] { sb, value, 1u, uint.MaxValue });
        return sb.ToString();
    }

    private static void BuffEdgeStackMatchesGame()
    {
        int[] counts = { 0, 1, 2, 9, 10, 99, 100, 1000, 65535, int.MaxValue };
        int[] offsets = { 0, 1, -1, -5, 10 };
        foreach (int count in counts)
        {
            foreach (int offset in offsets)
            {
                int shown = unchecked(count + offset);
                string game = GameAppendInt(shown);
                Equal("x" + game, BuffText.Stack(true, false, count, offset));
                Equal(game + "%", BuffText.Stack(true, true, count, offset));
            }
        }
    }

    private static void BuffEdgeSecondsMonotonic()
    {
        foreach (bool oneDecimal in new[] { true, false })
        {
            float t = 12f;
            double lastShown = double.MaxValue;
            float lastT = t;
            while (t > -0.1f)
            {
                double shown = double.Parse(BuffText.Seconds(t, oneDecimal), CultureInfo.InvariantCulture);
                Expect(shown <= lastShown, $"{(oneDecimal ? "decimal" : "whole")}: {lastT} read {lastShown}, then {t} read {shown}");
                lastShown = shown;
                lastT = t;
                t -= 1f / 60f;
            }
        }
    }

    private static void BuffEdgeSecondsBounds()
    {
        for (float t = 0.001f; t < 30f; t += 0.0137f)
        {
            double tenth = double.Parse(BuffText.Seconds(t, true), CultureInfo.InvariantCulture);
            double whole = double.Parse(BuffText.Seconds(t, false), CultureInfo.InvariantCulture);
            Expect(tenth <= t + 1e-6, $"{t} left reads {tenth}");
            Expect(t - tenth < 0.1 + 1e-5, $"{t} left reads {tenth}, a tenth or more short");
            Expect(whole >= t - 1e-6, $"{t} left reads {whole}");
            Expect(whole - t < 1 + 1e-5, $"{t} left reads {whole}, a second or more over");
        }
    }

    private static void BuffEdgeOddTimers()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            float[] odd = { float.NaN, float.NegativeInfinity, -0f, float.Epsilon, 1e-30f, 0.05f, 0.1f, 9.95f, 99.99f, 1e9f, float.MaxValue, float.PositiveInfinity };
            foreach (float t in odd)
            {
                foreach (bool oneDecimal in new[] { true, false })
                {
                    string s = BuffText.Seconds(t, oneDecimal);
                    Expect(!s.Contains(','), $"{t} reads [{s}] with a comma");
                    Expect(s.Length <= 7, $"{t} reads [{s}], too long for an icon");
                    Expect(!s.StartsWith("-", StringComparison.Ordinal), $"{t} reads [{s}]");
                    Expect(double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _), $"{t} reads [{s}], not a number");
                }
            }

            Equal("<size=80%>2.5</size>\nx3", BuffText.Timed(2.55f, true, "x3"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static void BuffEdgeLongestOdd()
    {
        var longest = BuffText.Longest(new[] { (1, float.PositiveInfinity), (1, 3f), (2, -1f), (2, -5f), (3, float.NaN), (3, float.NaN), (4, float.NaN), (4, 2f), (-1, 1f) });
        Expect(float.IsPositiveInfinity(longest[1]), $"buff 1 {longest[1]}");
        Expect(longest[2] == -1f, $"buff 2 {longest[2]}");
        Expect(!longest.ContainsKey(3), "only NaN entries count as untimed");
        Expect(longest[4] == 2f, $"buff 4 {longest[4]}");
        Expect(longest[-1] == 1f, "BuffIndex.None is kept as is");
    }

    private static void BuffEdgeTidyOdd()
    {
        var cases = new Dictionary<string, string>
        {
            ["bdHP"] = "HP",
            ["bdA"] = "A",
            ["bd_x"] = "bd x",
            ["BD"] = "BD",
            ["ABCDef"] = "ABC Def",
            ["My Buff"] = "My Buff",
            ["_Buff_"] = "Buff",
            ["MY_BUFF"] = "MY BUFF",
            ["ÄrgerBuff"] = "Ärger Buff",
            ["a__b"] = "a b",
            ["Buff _Name"] = "Buff Name",
            ["Some_Buff"] = "Some Buff",
        };
        var wrong = new List<string>();
        foreach (KeyValuePair<string, string> c in cases)
        {
            string got = BuffText.Tidy(c.Key);
            if (got != c.Value)
            {
                wrong.Add($"[{c.Key}] read [{got}], expected [{c.Value}]");
            }
        }

        foreach (string blank in new[] { " ", "_", "___", "\t" })
        {
            string got = BuffText.Tidy(blank);
            if (got.Length == 0)
            {
                wrong.Add($"[{blank.Replace("\t", "\\t")}] reads empty, so the tooltip has no title");
            }
        }

        Expect(wrong.Count == 0, string.Join("; ", wrong));
    }

    private static void BuffEdgeReflectedFields()
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type display = typeof(RoR2.UI.BuffDisplay);
        Expect(display.GetField("source", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType == typeof(CharacterBody), "BuffDisplay.source");
        FieldInfo? icons = display.GetField("buffIconDisplayData", BindingFlags.Instance | BindingFlags.NonPublic);
        Expect(icons != null && typeof(IList).IsAssignableFrom(icons.FieldType), "BuffDisplay.buffIconDisplayData is a list");
        Type data = display.GetNestedType("BuffIconDisplayData", any)!;
        Expect(data.GetField("buffIconComponent", any)?.FieldType == typeof(RoR2.UI.BuffIcon), "buffIconComponent");
        Expect(data.GetField("buffDef", any)?.FieldType == typeof(BuffDef), "buffDef");
        FieldInfo count = data.GetField("buffCount", any)!;
        object entry = Activator.CreateInstance(data, any, null, new object[] { (BuffIndex)7 }, null)!;
        count.SetValue(entry, 3);
        Expect((int)count.GetValue(entry)! == 3, "buffCount unboxes as int");

        FieldInfo? timedList = typeof(CharacterBody).GetField("timedBuffs", any);
        Expect(timedList != null && typeof(IList).IsAssignableFrom(timedList.FieldType), "CharacterBody.timedBuffs is a list");
        Type timed = typeof(CharacterBody).GetNestedType("TimedBuff", any)!;
        object t = Activator.CreateInstance(timed, true)!;
        FieldInfo index = timed.GetField("buffIndex", any)!;
        FieldInfo timer = timed.GetField("timer", any)!;
        index.SetValue(t, (BuffIndex)42);
        timer.SetValue(t, 2.5f);

        // As TimersOf casts them
        Expect((int)index.GetValue(t)! == 42, "a boxed BuffIndex unboxes as int");
        Expect((float)timer.GetValue(t)! == 2.5f, "timer unboxes as float");
        Expect((int)BuffDef.StackingDisplayMethod.Percentage == 1, "Percentage is 1, as PercentDisplay assumes");
        Expect(typeof(RoR2.UI.BuffIcon).GetField("stackCount")?.FieldType.Name == "TextMeshProUGUI", "BuffIcon.stackCount");
    }

    // ---- Model, read from the IL of BuffIcon, BuffDisplay and CharacterBody.UpdateBuffs ----

    private sealed class MBuff
    {
        public MBuff(int index, bool canStack, bool percent = false, int offset = 0)
        {
            Index = index;
            CanStack = canStack;
            Percent = percent;
            Offset = offset;
        }

        public int Index { get; }
        public bool CanStack { get; }
        public bool Percent { get; }
        public int Offset { get; }
        public override string ToString() => $"buff{Index}{(CanStack ? "" : " (no stack)")}";
    }

    private sealed class MText
    {
        public string Text = string.Empty;
        public bool Enabled;
    }

    private sealed class MIcon
    {
        private static int nextId;
        public readonly int Id = nextId++;
        public MBuff? BuffDef;
        public MBuff? LastBuffDef;
        public int BuffCount;
        public int LastBuffCount;
        public readonly MText Stack = new MText();

        public void ResetBuffIcon()
        {
            BuffDef = null;
            LastBuffCount = -1;
        }

        public void UpdateIcon()
        {
            if (BuffDef == LastBuffDef && BuffCount == LastBuffCount)
            {
                return;
            }

            LastBuffDef = BuffDef;
            LastBuffCount = BuffCount;
            if (BuffDef == null || !BuffDef.CanStack)
            {
                Stack.Enabled = false;
                return;
            }

            Stack.Enabled = true;
            Stack.Text = GameStack(BuffDef, BuffCount);
        }

        // GameObject.SetActive, OnDisable resets, OnEnable updates
        public void OnDisable() => ResetBuffIcon();

        public void OnEnable() => UpdateIcon();
    }

    private static string GameStack(MBuff b, int count)
    {
        string n = (count + b.Offset).ToString(CultureInfo.InvariantCulture);
        return b.Percent ? n + "%" : "x" + n;
    }

    private sealed class MTimed
    {
        public MTimed(MBuff buff, float timer)
        {
            Buff = buff;
            Timer = timer;
        }

        public MBuff Buff { get; }
        public float Timer;
    }

    private sealed class MBody
    {
        public readonly Dictionary<MBuff, int> Permanent = new Dictionary<MBuff, int>();
        public readonly List<MTimed> Timed = new List<MTimed>();

        public int Count(MBuff b) => (Permanent.TryGetValue(b, out int p) ? p : 0) + Timed.Count(t => t.Buff == b);

        // CharacterBody.UpdateBuffs, NaN stays as timer <= 0 is false
        public void Tick(float dt)
        {
            for (int i = Timed.Count - 1; i >= 0; i--)
            {
                Timed[i].Timer -= dt;
                if (Timed[i].Timer <= 0f)
                {
                    Timed.RemoveAt(i);
                }
            }
        }
    }

    private sealed class MEntry
    {
        public MEntry(MBuff buff, int count)
        {
            Buff = buff;
            Count = count;
        }

        public MBuff Buff { get; }
        public int Count;
        public MIcon? Icon;
    }

    private sealed class MPool
    {
        private readonly Stack<MIcon> free = new Stack<MIcon>();
        public bool Reuse = true;

        public MIcon Get()
        {
            MIcon icon = Reuse && free.Count > 0 ? free.Pop() : new MIcon();
            icon.ResetBuffIcon();
            icon.OnEnable();
            return icon;
        }

        public void Return(MIcon icon)
        {
            icon.OnDisable();
            free.Push(icon);
        }
    }

    private sealed class MDisplay
    {
        private readonly MPool pool;
        private bool reallocated;

        public MDisplay(MBody body, MPool pool)
        {
            Body = body;
            this.pool = pool;
        }

        public MBody Body { get; }
        public readonly List<MEntry> Entries = new List<MEntry>();

        // AllocateIcons, CullExpiredBuffIcons, then the layout loop only when something changed
        public void UpdateLayout(MirrorBuffDetails? mod, IList<MBuff> catalog)
        {
            foreach (MBuff b in catalog)
            {
                int count = Body.Count(b);
                MEntry? e = Entries.FirstOrDefault(x => x.Buff == b);
                if (count > 0 && e == null)
                {
                    Entries.Add(new MEntry(b, count));
                    reallocated = true;
                }
                else if (count > 0 && e!.Count != count)
                {
                    e.Count = count;
                    reallocated = true;
                }
                else if (count == 0 && e != null)
                {
                    if (e.Icon != null)
                    {
                        pool.Return(e.Icon);
                    }

                    Entries.Remove(e);
                    reallocated = true;
                }
            }

            Entries.Sort((a, c) => a.Buff.Index.CompareTo(c.Buff.Index));
            if (reallocated)
            {
                foreach (MEntry e in Entries)
                {
                    e.Icon ??= pool.Get();
                    e.Icon.BuffDef = e.Buff;
                    e.Icon.BuffCount = e.Count;
                    e.Icon.UpdateIcon();
                }

                reallocated = false;
            }

            mod?.Postfix(this);
        }

        // The bar's parent switched off and on, BuffDisplay.OnEnable lays out again before its icons' OnEnable
        public void HideShow(MirrorBuffDetails? mod, IList<MBuff> catalog)
        {
            foreach (MEntry e in Entries)
            {
                e.Icon?.OnDisable();
            }

            reallocated = true;
            UpdateLayout(mod, catalog);
            foreach (MEntry e in Entries)
            {
                e.Icon?.OnEnable();
            }
        }
    }

    // BuffDetails.Update and ShowTimer, line for line
    private sealed class MirrorBuffDetails
    {
        private readonly ConditionalWeakTable<MIcon, string> written = new ConditionalWeakTable<MIcon, string>();
        public bool Timers = true;
        public bool OneDecimal = true;
        public bool Host = true;

        public void Postfix(MDisplay display)
        {
            if (display.Entries.Count == 0)
            {
                return;
            }

            Dictionary<int, float>? timers = Timers ? TimersOf(display.Body) : null;
            foreach (MEntry entry in display.Entries)
            {
                if (entry.Icon == null)
                {
                    continue;
                }

                float timer = 0f;
                bool timed = timers != null && timers.TryGetValue(entry.Buff.Index, out timer);
                ShowTimer(entry.Icon, entry.Buff, entry.Count, timed, timer);
            }
        }

        private Dictionary<int, float>? TimersOf(MBody body)
        {
            if (!Host || body.Timed.Count == 0)
            {
                return null;
            }

            return BuffText.Longest(body.Timed.Select(t => (t.Buff.Index, t.Timer)).ToList());
        }

        private void ShowTimer(MIcon icon, MBuff buff, int count, bool timed, float timer)
        {
            MText stackText = icon.Stack;
            bool ours = written.TryGetValue(icon, out _);
            string stack = BuffText.Stack(buff.CanStack, buff.Percent, count, buff.Offset);
            if (timed)
            {
                string text = BuffText.Timed(timer, OneDecimal, stack);
                if (!ours)
                {
                    written.Add(icon, text);
                }

                stackText.Text = text;
                stackText.Enabled = true;
                return;
            }

            if (ours)
            {
                written.Remove(icon);
                stackText.Text = stack;
                stackText.Enabled = buff.CanStack;
            }
        }
    }

    // What the icon should show, the timer over the game's count when timed, else exactly the game's
    private static string? CheckShown(MDisplay display, MirrorBuffDetails mod)
    {
        foreach (MEntry e in display.Entries)
        {
            if (e.Icon == null)
            {
                continue;
            }

            float[] timers = display.Body.Timed.Where(t => t.Buff == e.Buff && !float.IsNaN(t.Timer)).Select(t => t.Timer).ToArray();
            bool timed = mod.Timers && mod.Host && timers.Length > 0;
            MText shown = e.Icon.Stack;
            if (timed)
            {
                string want = BuffText.Timed(timers.Max(), mod.OneDecimal, e.Buff.CanStack ? GameStack(e.Buff, e.Count) : string.Empty);
                if (!shown.Enabled || shown.Text != want)
                {
                    return $"{e.Buff} x{e.Count} on icon {e.Icon.Id}: shows [{Esc(shown.Text)}] enabled {shown.Enabled}, should show [{Esc(want)}]";
                }
            }
            else if (e.Buff.CanStack)
            {
                string want = GameStack(e.Buff, e.Count);
                if (!shown.Enabled || shown.Text != want)
                {
                    return $"{e.Buff} x{e.Count} untimed on icon {e.Icon.Id}: shows [{Esc(shown.Text)}] enabled {shown.Enabled}, the game shows [{want}]";
                }
            }
            else if (shown.Enabled)
            {
                return $"{e.Buff} untimed and unstackable on icon {e.Icon.Id}: shows [{Esc(shown.Text)}], the game hides it";
            }
        }

        return null;
    }

    private static string Esc(string s) => s.Replace("\n", "\\n");

    private static void ExpectShown(MDisplay display, MirrorBuffDetails mod, string where)
    {
        string? wrong = CheckShown(display, mod);
        Expect(wrong == null, $"{where}: {wrong}");
    }

    private static void BuffEdgeCountChange()
    {
        var a = new MBuff(1, true);
        var catalog = new[] { a };
        var body = new MBody();
        var mod = new MirrorBuffDetails();
        var d = new MDisplay(body, new MPool());
        body.Timed.Add(new MTimed(a, 5f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "one stack");
        body.Timed.Add(new MTimed(a, 3f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "two stacks");
        body.Tick(3.5f);
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "back to one");
    }

    private static void BuffEdgeToggleTimers()
    {
        foreach (bool canStack in new[] { true, false })
        {
            var a = new MBuff(1, canStack);
            var catalog = new[] { a };
            var body = new MBody();
            var mod = new MirrorBuffDetails();
            var d = new MDisplay(body, new MPool());
            body.Timed.Add(new MTimed(a, 5f));
            d.UpdateLayout(mod, catalog);
            mod.Timers = false;
            d.UpdateLayout(mod, catalog);
            ExpectShown(d, mod, $"off, stack {canStack}");
            mod.Timers = true;
            d.UpdateLayout(mod, catalog);
            ExpectShown(d, mod, $"on again, stack {canStack}");
            mod.OneDecimal = false;
            d.UpdateLayout(mod, catalog);
            ExpectShown(d, mod, $"whole seconds, stack {canStack}");
        }
    }

    private static void BuffEdgeTimerEndsStacksStay()
    {
        foreach (bool canStack in new[] { true, false })
        {
            var a = new MBuff(1, canStack);
            var catalog = new[] { a };
            var body = new MBody();
            var mod = new MirrorBuffDetails();
            var d = new MDisplay(body, new MPool());
            body.Permanent[a] = 1;
            body.Timed.Add(new MTimed(a, 1f));
            d.UpdateLayout(mod, catalog);
            ExpectShown(d, mod, "timed and permanent");
            body.Tick(2f);
            d.UpdateLayout(mod, catalog);
            ExpectShown(d, mod, $"timer gone, permanent stays, stack {canStack}");
        }
    }

    private static void BuffEdgeClient()
    {
        var a = new MBuff(1, true);
        var b = new MBuff(2, false);
        var catalog = new[] { a, b };
        var body = new MBody();
        var mod = new MirrorBuffDetails { Host = false };
        var d = new MDisplay(body, new MPool());
        body.Timed.Add(new MTimed(a, 5f));
        body.Timed.Add(new MTimed(b, 5f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "client");
    }

    // Two displays share one pool, as the HUD's bar, ally cards and the boss bar share BuffDisplay.pool
    private static void BuffEdgeFuzz(bool reuse, bool hideShow, int seed)
    {
        var rng = new Random(seed);
        var catalog = new[]
        {
            new MBuff(0, true), new MBuff(1, false), new MBuff(2, true, true), new MBuff(3, true, false, 1), new MBuff(4, true),
        };
        var pool = new MPool { Reuse = reuse };
        var mod = new MirrorBuffDetails();
        var displays = new[] { new MDisplay(new MBody(), pool), new MDisplay(new MBody(), pool) };
        var log = new List<string>();
        for (int step = 0; step < 4000; step++)
        {
            MDisplay d = displays[rng.Next(displays.Length)];
            MBuff b = catalog[rng.Next(catalog.Length)];
            int op = rng.Next(hideShow ? 11 : 10);
            switch (op)
            {
                case 0:
                case 1:
                    // Whole and half seconds make equal timer text likely, as on buffs applied together
                    float duration = rng.Next(2) == 0 ? rng.Next(1, 6) : rng.Next(1, 12) * 0.5f;
                    d.Body.Timed.Add(new MTimed(b, duration));
                    log.Add($"add {b} {duration}s");
                    break;
                case 2:
                    d.Body.Timed.RemoveAll(t => t.Buff == b);
                    log.Add($"clear {b}");
                    break;
                case 3:
                    d.Body.Permanent[b] = rng.Next(3);
                    log.Add($"permanent {b} {d.Body.Permanent[b]}");
                    break;
                case 4:
                case 5:
                case 6:
                    float dt = rng.Next(4) * (1f / 60f);
                    d.Body.Tick(dt);
                    log.Add($"tick {dt:0.000}");
                    break;
                case 7:
                    mod.Timers = rng.Next(5) != 0;
                    log.Add($"timers {mod.Timers}");
                    break;
                case 8:
                    mod.OneDecimal = rng.Next(2) == 0;
                    log.Add($"decimal {mod.OneDecimal}");
                    break;
                case 9:
                    break;
                case 10:
                    d.HideShow(mod, catalog);
                    log.Add("hide and show");
                    break;
            }

            foreach (MDisplay each in displays)
            {
                each.UpdateLayout(mod, catalog);
            }

            foreach (MDisplay each in displays)
            {
                string? wrong = CheckShown(each, mod);
                if (wrong != null)
                {
                    throw new AssertFail($"seed {seed} step {step}: {wrong}", "     last steps: " + string.Join(", ", log.Skip(Math.Max(0, log.Count - 8))));
                }
            }
        }
    }

    private static void BuffEdgeFuzzNoReuse()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            BuffEdgeFuzz(false, false, seed);
        }
    }

    private static void BuffEdgeFuzzWithPool()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            BuffEdgeFuzz(true, false, seed);
        }
    }

    private static void BuffEdgeFuzzHideShow()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            BuffEdgeFuzz(true, true, seed);
        }
    }

    // A stackable buff cleared early, its icon pooled, then another at the same count and timer text takes it
    private static void BuffEdgePoolReuseSameText()
    {
        var a = new MBuff(1, true);
        var b = new MBuff(2, true);
        var catalog = new[] { a, b };
        var body = new MBody();
        var mod = new MirrorBuffDetails { OneDecimal = false };
        var d = new MDisplay(body, new MPool());
        body.Timed.Add(new MTimed(a, 4.5f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "first buff");
        body.Timed.Clear();
        d.UpdateLayout(mod, catalog);
        body.Timed.Add(new MTimed(b, 4.2f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "second buff on the reused icon");
    }

    // BuffIcon.OnDisable resets the icon, so the game writes x2 again on the next layout
    private static void BuffEdgeHideShowSameText()
    {
        var a = new MBuff(1, true);
        var catalog = new[] { a };
        var body = new MBody();
        var mod = new MirrorBuffDetails { OneDecimal = false };
        var d = new MDisplay(body, new MPool());
        body.Timed.Add(new MTimed(a, 4.5f));
        body.Timed.Add(new MTimed(a, 4.4f));
        d.UpdateLayout(mod, catalog);
        ExpectShown(d, mod, "before hiding");
        d.HideShow(mod, catalog);
        ExpectShown(d, mod, "shown again");
    }

    // ---- The ported table, linked from the mod ----

    private static readonly Regex AddLine = new Regex(@"^\s*Add\((?<owner>\w+Content)\.Buffs\.(?<field>\w+),\s*\$?""(?<name>[^""]*)"",\s*(?<desc>.*)\);\s*$", RegexOptions.Compiled);

    private static List<(string Owner, string Field, string Name, string Desc)> TableLines()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "SourceRoot").Value!;
        var rows = new List<(string, string, string, string)>();
        foreach (string line in File.ReadAllLines(System.IO.Path.Combine(root, "BuffInfo", "BuffDefinitions.cs")))
        {
            Match m = AddLine.Match(line);
            if (m.Success)
            {
                rows.Add((m.Groups["owner"].Value, m.Groups["field"].Value, m.Groups["name"].Value, m.Groups["desc"].Value));
            }
        }

        return rows;
    }

    private static FieldInfo BuffField(string owner, string field)
    {
        Type buffs = typeof(BuffDef).Assembly.GetType("RoR2." + owner)!.GetNestedType("Buffs", BindingFlags.Public | BindingFlags.NonPublic)!;
        return buffs.GetField(field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
    }

    // FillContent's defs have no native object, so Unity reads them as destroyed, give them a fake one
    private static void MakeAlive(BuffDef def)
    {
        FieldInfo cached = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic)!;
        cached.SetValue(def, new IntPtr(1));
    }

    private static void BuffEdgeDefinitionsMissingContent()
    {
        var rows = TableLines();
        Expect(rows.Count >= 130, $"only {rows.Count} table rows parsed");
        var restore = new List<(FieldInfo, object?)>();
        try
        {
            var alive = new HashSet<BuffIndex>();
            BuffDef? missing = null;
            BuffDef? unregistered = null;
            foreach (var row in rows)
            {
                FieldInfo f = BuffField(row.Owner, row.Field);
                var def = (BuffDef?)f.GetValue(null);
                Expect(def is not null, $"{row.Owner}.{row.Field} wasn't filled");
                MakeAlive(def!);
                restore.Add((f, def));

                // A DLC that isn't there, a field another mod cleared, and one never given an index
                if (row.Owner == "DLC3Content")
                {
                    f.SetValue(null, null);
                }
                else if (row.Field == "Warbanner")
                {
                    missing = def;
                    f.SetValue(null, null);
                }
                else if (row.Field == "TonicBuff")
                {
                    unregistered = (BuffDef)RuntimeHelpers.GetUninitializedObject(typeof(BuffDef));
                    typeof(BuffDef).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .First(x => x.FieldType == typeof(BuffIndex)).SetValue(unregistered, BuffIndex.None);
                    MakeAlive(unregistered);
                    f.SetValue(null, unregistered);
                }
                else
                {
                    alive.Add(def!.buffIndex);
                }
            }

            Expect(!BuffDefinitions.TryGet(null!, out _, out _), "a null buff finds nothing");
            var table = (IDictionary)typeof(BuffDefinitions).GetField("table", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            int count = table.Count;
            Expect(count == alive.Count, $"table has {count} buffs, {alive.Count} were there");
            Expect(!BuffDefinitions.TryGet(missing!, out _, out _), "a cleared field's buff isn't described");
            Expect(!BuffDefinitions.TryGet(unregistered!, out _, out _), "a buff with no index finds nothing");
            var warCry = (BuffDef)BuffField("RoR2Content", "WarCryBuff").GetValue(null)!;
            Expect(BuffDefinitions.TryGet(warCry, out string name, out _) && name == "War Cry", $"War Cry reads [{name}]");
        }
        finally
        {
            foreach ((FieldInfo f, object? v) in restore)
            {
                f.SetValue(null, v);
            }
        }
    }

    private static void BuffEdgeDescriptionsNotEmpty()
    {
        var wrong = new List<string>();
        foreach (var row in TableLines())
        {
            string desc = row.Desc.Trim();
            if (desc.StartsWith("$", StringComparison.Ordinal))
            {
                desc = desc.Substring(1);
            }

            // A shared variable, such as the cooking text
            if (!desc.StartsWith("\"", StringComparison.Ordinal))
            {
                continue;
            }

            // Those that say nothing show the title alone, the rest show their text
            string plain = desc.Trim('"').Trim();
            bool empty = plain.Length == 0 || string.Equals(plain, row.Name, StringComparison.OrdinalIgnoreCase);
            string shown = BuffText.Body(row.Name, plain);
            if (empty ? shown.Length != 0 : shown != plain)
            {
                wrong.Add($"{row.Owner}.{row.Field} \"{row.Name}\" with [{plain}] shows [{shown}]");
            }
        }

        Expect(wrong.Count == 0, string.Join("; ", wrong));
    }
}
