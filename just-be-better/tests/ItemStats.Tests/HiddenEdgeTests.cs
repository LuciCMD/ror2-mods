using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using JustBeBetter.Scoreboard;
using RoR2;
using RoR2.UI;

// Hidden Items and Buffs at its edges
// Unhider can't run outside Unity, so the IL tests check the game still does what its hooks lean on, and the
// models mirror Unhider's AllocateIcons swap and placeholder bookkeeping as written
// A change to Unhider has to be mirrored here
internal static partial class Program
{
    private static void RunHiddenEdge()
    {
        Test("Hidden edge: BuffDisplay.AllocateIcons walks BuffCatalog.nonHiddenBuffIndices, a field Unhider can swap", HiddenEdgeAllocateReadsList);
        Test("Hidden edge: CullExpiredBuffIcons drops a buff by its iconSprite, which the placeholder fills", HiddenEdgeCullReadsIcon);
        Test("Hidden edge: the bar's icon comes from iconSprite alone, so the placeholder is what shows", HiddenEdgeSpriteFromField);
        Test("Hidden edge: ItemIsVisible is called by UpdateDisplay and the scoreboard total, and too big for Mono to inline", HiddenEdgeItemIsVisible);
        Test("Hidden edge: the buff list is put back when the bar throws", HiddenEdgeSwapRestoredOnThrow);
        Test("Hidden edge: a bar laid out inside another bar's layout still gets the game's list back", HiddenEdgeSwapNested);
        Test("Hidden edge: Unhide Buffs off, on, off, on fills each buff once and leaves none behind", HiddenEdgePlaceholderCycles);
        Test("Hidden edge: a buff given its own icon while shown keeps it when Unhide Buffs goes off", HiddenEdgeOwnIconKept);
        Test("Hidden edge: a buff another mod gave an icon first is never touched", HiddenEdgeOtherModFirst);
        Test("Hidden edge: Disable with Unhide Buffs on leaves no placeholder", HiddenEdgeDisableClears);
        Test("Hidden edge model: with Unhide Items on, the scoreboard total counts the hidden items the row shows", HiddenEdgeScoreboardTotal);
    }

    private const BindingFlags HiddenAll = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // Every field a method loads, by name
    private static List<string> FieldsRead(MethodBase method)
    {
        Callees(method);
        var found = new List<string>();
        byte[]? il = RawIl(method);
        if (il == null)
        {
            return found;
        }

        int pos = 0;
        while (pos < il.Length)
        {
            OpCode op = il[pos] == 0xfe ? TwoByte[il[++pos]] : OneByte[il[pos]];
            pos++;
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, pos),
                _ => 4,
            };
            if (op.OperandType == OperandType.InlineField && (op == OpCodes.Ldfld || op == OpCodes.Ldsfld))
            {
                try
                {
                    FieldInfo? f = method.Module.ResolveField(BitConverter.ToInt32(il, pos));
                    if (f != null)
                    {
                        found.Add(f.DeclaringType!.Name + "." + f.Name);
                    }
                }
                catch (Exception)
                {
                    // A generic or missing reference, not what's being looked for
                }
            }

            pos += size;
        }

        return found;
    }

    private static void HiddenEdgeAllocateReadsList()
    {
        FieldInfo? list = typeof(BuffCatalog).GetField("nonHiddenBuffIndices", BindingFlags.Public | BindingFlags.Static);
        Expect(list != null && list.FieldType == typeof(BuffIndex[]), "BuffCatalog.nonHiddenBuffIndices is gone or changed type");
        Expect(!list!.IsInitOnly, "BuffCatalog.nonHiddenBuffIndices is readonly, the swap can't write it");
        MethodInfo? allocate = typeof(BuffDisplay).GetMethod("AllocateIcons", HiddenAll);
        Expect(allocate != null, "BuffDisplay.AllocateIcons is gone");
        List<string> read = FieldsRead(allocate!);
        Expect(read.Contains("BuffCatalog.nonHiddenBuffIndices"), $"AllocateIcons no longer reads the list, it reads {string.Join(", ", read)}");
    }

    private static void HiddenEdgeCullReadsIcon()
    {
        MethodInfo? cull = typeof(BuffDisplay).GetMethod("CullExpiredBuffIcons", HiddenAll);
        Expect(cull != null, "BuffDisplay.CullExpiredBuffIcons is gone");
        List<string> read = FieldsRead(cull!);
        Expect(read.Contains("BuffDef.iconSprite"), $"the cull no longer reads iconSprite, it reads {string.Join(", ", read)}");
        FieldInfo? icon = typeof(BuffDef).GetField("iconSprite");
        Expect(icon != null && !icon.IsInitOnly, "BuffDef.iconSprite isn't a writable field");
    }

    private static void HiddenEdgeSpriteFromField()
    {
        MethodInfo? sprite = typeof(BuffDef).GetMethod("GetSpriteAndColor", HiddenAll);
        Expect(sprite != null, "BuffDef.GetSpriteAndColor is gone");
        Expect(FieldsRead(sprite!).Contains("BuffDef.iconSprite"), "GetSpriteAndColor no longer reads iconSprite");
    }

    private static void HiddenEdgeItemIsVisible()
    {
        MethodInfo? visible = typeof(ItemInventoryDisplay).GetMethod("ItemIsVisible", HiddenAll);
        Expect(visible != null && visible.IsStatic && visible.ReturnType == typeof(bool), "ItemInventoryDisplay.ItemIsVisible is gone or changed");
        foreach (string caller in new[] { "UpdateDisplay", "GetTotalVisibleItemCount" })
        {
            MethodInfo? m = typeof(ItemInventoryDisplay).GetMethod(caller, HiddenAll);
            Expect(m != null && Callees(m).Contains(visible!), $"{caller} no longer calls ItemIsVisible");
        }

        // Mono inlines below 20 IL bytes, a caller inlined before the hook goes in would skip it
        int size = RawIl(visible!)?.Length ?? 0;
        Expect(size >= 20, $"ItemIsVisible is {size} IL bytes, small enough for Mono to inline");
    }

    // Unhider.OnAllocateIcons, as written, against the real static field
    private static void SwapMirror(BuffIndex[]? allBuffs, bool unhide, Action orig)
    {
        if (allBuffs == null || !unhide)
        {
            orig();
            return;
        }

        BuffIndex[] shown = BuffCatalog.nonHiddenBuffIndices;
        BuffCatalog.nonHiddenBuffIndices = allBuffs;
        try
        {
            orig();
        }
        finally
        {
            BuffCatalog.nonHiddenBuffIndices = shown;
        }
    }

    private static void HiddenEdgeSwapRestoredOnThrow()
    {
        BuffIndex[] before = BuffCatalog.nonHiddenBuffIndices;
        BuffIndex[] game = { (BuffIndex)1, (BuffIndex)3 };
        BuffIndex[] all = { (BuffIndex)0, (BuffIndex)1, (BuffIndex)2, (BuffIndex)3 };
        BuffCatalog.nonHiddenBuffIndices = game;
        try
        {
            BuffIndex[]? seen = null;
            try
            {
                SwapMirror(all, true, () =>
                {
                    seen = BuffCatalog.nonHiddenBuffIndices;
                    throw new InvalidOperationException("bar broke");
                });
            }
            catch (InvalidOperationException)
            {
                // The game's own throw, passed on as the hook does
            }

            Expect(ReferenceEquals(seen, all), "the bar didn't get every buff");
            Expect(ReferenceEquals(BuffCatalog.nonHiddenBuffIndices, game), "the game's list wasn't put back after a throw");
        }
        finally
        {
            BuffCatalog.nonHiddenBuffIndices = before;
        }
    }

    private static void HiddenEdgeSwapNested()
    {
        BuffIndex[] before = BuffCatalog.nonHiddenBuffIndices;
        BuffIndex[] game = { (BuffIndex)1 };
        BuffIndex[] all = { (BuffIndex)0, (BuffIndex)1 };
        BuffCatalog.nonHiddenBuffIndices = game;
        try
        {
            SwapMirror(all, true, () => SwapMirror(all, true, () => { }));
            Expect(ReferenceEquals(BuffCatalog.nonHiddenBuffIndices, game), "nested layouts left the all-buffs list in place");
            SwapMirror(all, false, () => Expect(ReferenceEquals(BuffCatalog.nonHiddenBuffIndices, game), "the list was swapped with Unhide Buffs off"));
        }
        finally
        {
            BuffCatalog.nonHiddenBuffIndices = before;
        }
    }

    // Unhider's ApplyIcons and ClearIcons, with a string standing in for Unity's Sprite
    private sealed class FakeBuff
    {
        internal bool Hidden;
        internal string? Icon;
    }

    private sealed class IconMirror
    {
        internal const string Placeholder = "geode";
        internal readonly List<FakeBuff> Buffs = new List<FakeBuff>();
        internal readonly List<FakeBuff> Filled = new List<FakeBuff>();
        internal bool Unhide;

        internal void Apply()
        {
            if (!Unhide)
            {
                Clear();
                return;
            }

            foreach (FakeBuff def in Buffs)
            {
                if (def.Hidden && def.Icon == null)
                {
                    def.Icon = Placeholder;
                    Filled.Add(def);
                }
            }
        }

        internal void Clear()
        {
            foreach (FakeBuff def in Filled)
            {
                if (def.Icon == Placeholder)
                {
                    def.Icon = null;
                }
            }

            Filled.Clear();
        }
    }

    private static IconMirror IconsOf(params (bool Hidden, string? Icon)[] buffs)
    {
        var mirror = new IconMirror();
        mirror.Buffs.AddRange(buffs.Select(b => new FakeBuff { Hidden = b.Hidden, Icon = b.Icon }));
        return mirror;
    }

    private static void HiddenEdgePlaceholderCycles()
    {
        IconMirror m = IconsOf((false, "a"), (true, null), (true, "own"), (true, null));
        for (int i = 0; i < 5; i++)
        {
            m.Unhide = true;
            m.Apply();
            Expect(m.Filled.Count == 2, $"round {i}: {m.Filled.Count} buffs filled, 2 have no icon");
            Expect(m.Buffs.All(b => b.Icon != null), $"round {i}: a hidden buff has no icon while shown");
            m.Unhide = false;
            m.Apply();
            Expect(m.Filled.Count == 0, $"round {i}: the filled list kept {m.Filled.Count}");
            Expect(m.Buffs[1].Icon == null && m.Buffs[3].Icon == null, $"round {i}: a placeholder stayed after off");
            Expect(m.Buffs[0].Icon == "a" && m.Buffs[2].Icon == "own", $"round {i}: a buff's own icon was changed");
        }
    }

    private static void HiddenEdgeOwnIconKept()
    {
        IconMirror m = IconsOf((true, null));
        m.Unhide = true;
        m.Apply();
        m.Buffs[0].Icon = "theirs";
        m.Unhide = false;
        m.Apply();
        Expect(m.Buffs[0].Icon == "theirs", "an icon given after ours was cleared");
    }

    private static void HiddenEdgeOtherModFirst()
    {
        IconMirror m = IconsOf((true, IconMirror.Placeholder));
        m.Unhide = true;
        m.Apply();
        m.Unhide = false;
        m.Apply();
        Expect(m.Buffs[0].Icon == IconMirror.Placeholder, "another mod's icon, the same sprite as ours, was cleared");
    }

    private static void HiddenEdgeDisableClears()
    {
        IconMirror m = IconsOf((true, null), (true, null));
        m.Unhide = true;
        m.Apply();
        m.Clear();
        Expect(m.Buffs.All(b => b.Icon == null), "Disable left a placeholder");
    }

    // ItemInventoryDisplay.GetTotalVisibleItemCount with Unhider's ItemIsVisible, against ItemCounters.Count, as written
    // The row shows a Monsoon helper with Unhide Items on, the game's own count says 3, ours leaves it out
    private static void HiddenEdgeScoreboardTotal()
    {
        var held = new (ItemTier Tier, bool Hidden, int Count)[]
        {
            (ItemTier.Tier1, false, 2),
            (ItemTier.NoTier, true, 1),
        };
        bool unhideItems = true;

        int game = held.Where(h => !h.Hidden || unhideItems).Sum(h => h.Count);
        int iconsShown = held.Count(h => !h.Hidden || unhideItems);

        var tally = new CounterText.Tally();
        // ItemCounters.Count, which counts hidden items while Unhide Items is on
        foreach ((ItemTier tier, bool hidden, int count) in held)
        {
            if (hidden && !unhideItems)
            {
                continue;
            }

            tally.Add(tier, count, 0);
        }

        string ours = CounterText.Row(tally, CounterText.Options.TierCounts);
        Expect(iconsShown == 2, "setup, the hidden item's icon should show");
        Expect(
            ours.Contains("<color=#FFFFFF>" + game + "</color>", StringComparison.Ordinal),
            $"the game counts {game} items with the hidden one shown, Scoreboard Counters writes {ours}");
    }
}
