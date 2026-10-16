using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using RoR2;
using RoR2.UI;
using UnityEngine;

// Immune Health Bar at its edges
// ImmuneHealthBar can't run outside Unity, so the reflection tests check every member it reaches by name in the
// installed game, and its bar and boss bar steps are mirrored as written against the game's own text caching
// A change to ImmuneHealthBar has to be mirrored here
internal static partial class Program
{
    private static void RunImmuneBarEdge()
    {
        Test("Immune bar edge: the members ImmuneHealthBar reaches by name exist with the types it expects", ImmuneEdgeReflected);
        Test("Immune bar edge: a colour written through the boxed bar collection reaches the bar and nothing else", ImmuneEdgeBoxedColour);
        Test("Immune bar edge: HUD numbers come back right after a flash while health moved", ImmuneEdgeHudFlashHealthMoved);
        Test("Immune bar edge: HUD numbers come back right when the bar moves to a hittable body", ImmuneEdgeHudSourceChange);
        Test("Immune bar edge: HUD numbers come back right after many short flashes", ImmuneEdgeHudRapidFlashes);
        Test("Immune bar edge: boss numbers come back when the same group stops being immune", ImmuneEdgeBossSameGroup);
        Test("Immune bar edge: boss numbers and colour come back when Boss Bar is turned off", ImmuneEdgeBossSettingOff);
        Test("Immune bar edge: switching from an immune group to a hittable one shows the new group's numbers", ImmuneEdgeBossSwitchGroup);
        Test("Immune bar edge: an immune group destroyed then one with equal totals doesn't keep IMMUNE", ImmuneEdgeBossDestroyedEqualTotals);
    }

    private const BindingFlags ImmunePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags ImmuneAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void ImmuneEdgeReflected()
    {
        FieldInfo? bars = typeof(HealthBar).GetField("barInfoCollection", ImmunePrivate);
        Expect(bars != null && bars.FieldType.IsValueType, "HealthBar.barInfoCollection, a struct");
        FieldInfo? trailing = bars!.FieldType.GetField("trailingOverHealthbarInfo", ImmuneAny);
        Expect(trailing != null && trailing.FieldType == typeof(HealthBar.BarInfo), "barInfoCollection.trailingOverHealthbarInfo");
        Expect(typeof(HealthBar.BarInfo).GetField("color")?.FieldType == typeof(Color), "BarInfo.color");
        FieldInfo? group = typeof(HUDBossHealthBarController).GetField("currentBossGroup", ImmunePrivate);
        Expect(group != null && group.FieldType == typeof(BossGroup), "HUDBossHealthBarController.currentBossGroup");
        foreach (string cache in new[] { "oldTotalHealth", "oldTotalMaxHealth" })
        {
            Expect(typeof(HUDBossHealthBarController).GetField(cache, ImmunePrivate)?.FieldType == typeof(int), "HUDBossHealthBarController." + cache);
        }
        Expect(typeof(HealthBar).GetMethod("UpdateBarInfos", ImmunePrivate, null, Type.EmptyTypes, null) != null, "HealthBar.UpdateBarInfos()");
        Expect(typeof(HUDBossHealthBarController).GetMethod("LateUpdate", ImmunePrivate, null, Type.EmptyTypes, null) != null,
            "HUDBossHealthBarController.LateUpdate()");
    }

    // ImmuneHealthBar.Colour, as written, on a bar with no native object
    private static void ImmuneEdgeBoxedColour()
    {
        FieldInfo barsField = typeof(HealthBar).GetField("barInfoCollection", ImmunePrivate)!;
        FieldInfo trailingField = barsField.FieldType.GetField("trailingOverHealthbarInfo", ImmuneAny)!;
        FieldInfo regainField = barsField.FieldType.GetField("regainableHealthBarInfo", ImmuneAny)!;
        var bar = (HealthBar)RuntimeHelpers.GetUninitializedObject(typeof(HealthBar));

        // Something set by the game beforehand, so a lost write-back would show
        object before = barsField.GetValue(bar)!;
        var green = (HealthBar.BarInfo)trailingField.GetValue(before)!;
        green.color = Color.green;
        green.normalizedXMax = 0.5f;
        trailingField.SetValue(before, green);
        var regain = (HealthBar.BarInfo)regainField.GetValue(before)!;
        regain.color = Color.blue;
        regainField.SetValue(before, regain);
        barsField.SetValue(bar, before);

        object boxed = barsField.GetValue(bar)!;
        var info = (HealthBar.BarInfo)trailingField.GetValue(boxed)!;
        info.color = Color.yellow;
        trailingField.SetValue(boxed, info);
        barsField.SetValue(bar, boxed);

        object after = barsField.GetValue(bar)!;
        var written = (HealthBar.BarInfo)trailingField.GetValue(after)!;
        Expect(written.color == Color.yellow, $"health colour reads {written.color}");
        Expect(written.normalizedXMax == 0.5f, "the bar's length was lost in the write-back");
        Expect(((HealthBar.BarInfo)regainField.GetValue(after)!).color == Color.blue, "another bar's colour changed");
    }

    // ---- HUD bar ----

    // HealthBar.UpdateHealthbar's number writing, both paths, then UpdateBarInfos with the mod's hook after it
    private sealed class MHudBar
    {
        public MHealth? Source;
        public bool UsesSprites;
        public string Current = "", Full = "", Slash = "/";
        public bool SlashActive, SpritesActive = true;
        public (int Current, int Full) Sprites;
        public float DisplayCurrent, DisplayFull, OldHealth, OldFull;
        public MSaved? Shown;

        public void GameUpdate()
        {
            if (Source != null)
            {
                float current = Mathf.Ceil(Source.Combined);
                float full = Mathf.Ceil(Source.FullHealth);
                if (UsesSprites)
                {
                    if (current != DisplayCurrent || full != DisplayFull)
                    {
                        DisplayFull = full;
                        DisplayCurrent = current;
                        Sprites = ((int)DisplayCurrent, (int)DisplayFull);
                    }
                }
                else
                {
                    if (Source.Combined != OldHealth)
                    {
                        OldHealth = Source.Combined;
                        if (current != DisplayCurrent)
                        {
                            DisplayCurrent = current;
                            Current = current.ToString(CultureInfo.CurrentCulture);
                        }
                    }

                    if (Source.FullHealth != OldFull)
                    {
                        OldFull = Source.FullHealth;
                        if (full != DisplayFull)
                        {
                            DisplayFull = full;
                            Full = full.ToString(CultureInfo.CurrentCulture);
                        }
                    }
                }
            }

            ModUpdateBar();
        }

        // ImmuneHealthBar.UpdateBar
        private void ModUpdateBar()
        {
            bool wasShown = Shown != null;
            bool immune = Source != null && Source.Immune;
            if (!immune)
            {
                if (wasShown)
                {
                    ModRestore();
                }

                return;
            }

            if (!wasShown)
            {
                Shown = new MSaved(Slash, SlashActive, UsesSprites && SpritesActive);
            }

            Current = "";
            Full = "";
            if (UsesSprites && SpritesActive)
            {
                SpritesActive = false;
            }

            SlashActive = true;
            Slash = "IMMUNE";
        }

        // ImmuneHealthBar.Restore
        private void ModRestore()
        {
            MSaved saved = Shown!;
            Shown = null;
            Slash = saved.SlashText;
            SlashActive = saved.SlashActive;
            if (Source != null)
            {
                float current = Mathf.Ceil(Source.Combined);
                float full = Mathf.Ceil(Source.FullHealth);
                Current = current.ToString(CultureInfo.CurrentCulture);
                Full = full.ToString(CultureInfo.CurrentCulture);
                if (UsesSprites)
                {
                    Sprites = ((int)current, (int)full);
                }
            }

            if (UsesSprites)
            {
                SpritesActive = saved.SpriteActive;
            }
        }

        // What the player reads
        public string Shows()
        {
            if (SlashActive && Slash == "IMMUNE")
            {
                return "IMMUNE";
            }

            return UsesSprites ? (SpritesActive ? $"{Sprites.Current}/{Sprites.Full}" : "") : $"{Current}/{Full}";
        }
    }

    private sealed class MHealth
    {
        public float Combined = 100f, FullHealth = 100f;
        public bool Immune;
    }

    private sealed record MSaved(string SlashText, bool SlashActive, bool SpriteActive);

    private static void ImmuneEdgeHudFlashHealthMoved()
    {
        foreach (bool sprites in new[] { true, false })
        {
            var body = new MHealth { Combined = 80.4f };
            var bar = new MHudBar { Source = body, UsesSprites = sprites };
            bar.GameUpdate();
            Equal("81/100", bar.Shows());
            body.Immune = true;
            bar.GameUpdate();
            Equal("IMMUNE", bar.Shows());
            body.Combined = 92.2f;
            body.FullHealth = 120f;
            bar.GameUpdate();
            Equal("IMMUNE", bar.Shows());
            body.Immune = false;
            bar.GameUpdate();
            Equal("93/120", bar.Shows());
            body.Combined = 50f;
            bar.GameUpdate();
            Equal("50/120", bar.Shows());
        }
    }

    private static void ImmuneEdgeHudSourceChange()
    {
        foreach (bool sprites in new[] { true, false })
        {
            var a = new MHealth { Combined = 100f, Immune = true };
            var b = new MHealth { Combined = 37f, FullHealth = 200f };
            var bar = new MHudBar { Source = a, UsesSprites = sprites };
            bar.GameUpdate();
            Equal("IMMUNE", bar.Shows());
            bar.Source = b;
            bar.GameUpdate();
            Equal("37/200", bar.Shows());
        }
    }

    private static void ImmuneEdgeHudRapidFlashes()
    {
        foreach (bool sprites in new[] { true, false })
        {
            var body = new MHealth();
            var bar = new MHudBar { Source = body, UsesSprites = sprites };
            for (int frame = 0; frame < 200; frame++)
            {
                body.Immune = frame % 3 != 0;
                body.Combined = 100f - frame % 7;
                bar.GameUpdate();
            }

            body.Immune = false;
            bar.GameUpdate();
            Equal($"{Mathf.Ceil(body.Combined)}/100", bar.Shows());
            Expect(bar.Slash == "/" && !bar.SlashActive, $"slash left as [{bar.Slash}] active {bar.SlashActive}");
        }
    }

    // ---- Boss bar ----

    private sealed class MBossGroup
    {
        public float Total = 1000f, Max = 1000f;
        public bool Immune, Destroyed;
    }

    private sealed class MSavedBoss
    {
        public MSavedBoss(Color fill)
        {
            Fill = fill;
        }

        public Color Fill { get; }
    }

    // HUDBossHealthBarController.LateUpdate's container and label, then the mod's hook after it
    private sealed class MBossBar
    {
        public MBossGroup? Current;
        public bool ContainerActive, BossBarSetting = true;
        public string Label = "";
        public Color Fill = Color.red;
        public int OldTotal, OldMax;
        public MSavedBoss? Shown;

        private bool Alive(MBossGroup? g) => g != null && !g.Destroyed;

        public void LateUpdate()
        {
            bool flag = Alive(Current) && Current!.Total > 0f;
            ContainerActive = flag;
            if (flag)
            {
                int total = Mathf.FloorToInt(Current!.Total);
                int max = Mathf.FloorToInt(Current.Max);
                if (total != OldTotal || max != OldMax)
                {
                    OldTotal = total;
                    OldMax = max;
                    Label = $"{total}/{max}";
                }
            }

            ModUpdateBoss();
        }

        // ImmuneHealthBar.UpdateBoss
        private void ModUpdateBoss()
        {
            bool wasShown = Shown != null;
            MBossGroup? group = Current;
            bool immune = BossBarSetting && Alive(group) && ContainerActive && group!.Immune;
            if (!immune)
            {
                if (wasShown)
                {
                    ModRestoreBoss();
                }

                return;
            }

            if (!wasShown)
            {
                Shown = new MSavedBoss(Fill);
            }

            Label = "IMMUNE";
            Fill = Color.yellow;
        }

        // ImmuneHealthBar.RestoreBoss
        private void ModRestoreBoss()
        {
            MSavedBoss saved = Shown!;
            Shown = null;
            Fill = saved.Fill;
            OldTotal = -1;
            OldMax = -1;
            if (Alive(Current))
            {
                Label = $"{Mathf.FloorToInt(Current!.Total)}/{Mathf.FloorToInt(Current.Max)}";
            }
        }
    }

    private static void ImmuneEdgeBossSameGroup()
    {
        var a = new MBossGroup { Total = 900.7f };
        var bar = new MBossBar { Current = a };
        bar.LateUpdate();
        Equal("900/1000", bar.Label);
        a.Immune = true;
        bar.LateUpdate();
        Equal("IMMUNE", bar.Label);
        Expect(bar.Fill == Color.yellow, "fill not coloured");
        a.Total = 950f;
        bar.LateUpdate();
        a.Immune = false;
        bar.LateUpdate();
        Equal("950/1000", bar.Label);
        Expect(bar.Fill == Color.red, "fill colour not put back");
    }

    private static void ImmuneEdgeBossSettingOff()
    {
        var a = new MBossGroup { Immune = true, Total = 400f };
        var bar = new MBossBar { Current = a };
        bar.LateUpdate();
        Equal("IMMUNE", bar.Label);
        bar.BossBarSetting = false;
        bar.LateUpdate();
        Equal("400/1000", bar.Label);
        Expect(bar.Fill == Color.red, "fill colour not put back");
    }

    // Two boss groups up at once, the game moves the bar to the one last hit, or off one that stops showing
    private static void ImmuneEdgeBossSwitchGroup()
    {
        var a = new MBossGroup { Immune = true, Total = 5000f, Max = 5000f };
        var b = new MBossGroup { Total = 1200f, Max = 3000f };
        var bar = new MBossBar { Current = a };
        bar.LateUpdate();
        Equal("IMMUNE", bar.Label);
        bar.Current = b;
        bar.LateUpdate();
        Equal("1200/3000", bar.Label);

        // B's totals don't move, so the game never writes again
        bar.LateUpdate();
        Equal("1200/3000", bar.Label);
    }

    private static void ImmuneEdgeBossDestroyedEqualTotals()
    {
        var a = new MBossGroup { Immune = true, Total = 2000f, Max = 2000f };
        var bar = new MBossBar { Current = a };
        bar.LateUpdate();
        Equal("IMMUNE", bar.Label);
        a.Destroyed = true;
        bar.Current = null;
        bar.LateUpdate();
        Expect(!bar.ContainerActive, "bar still up with no group");
        var c = new MBossGroup { Total = 2000f, Max = 2000f };
        bar.Current = c;
        bar.LateUpdate();
        Equal("2000/2000", bar.Label);
    }
}
