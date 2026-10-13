using System;
using System.Collections.Generic;
using System.Globalization;
using JustBeBetter.Cooldowns;

// Edge cases for Cooldown Timers
// The frame tests run a model, the game's SkillIcon.LateUpdate and EquipmentIcon.SetDisplayData cooldown text rules
// as read from the IL, with CooldownTimers' postfixes mirrored on top, since neither runs outside Unity
// The rules they call are the linked CooldownRules, so a rule change shows here, a postfix change has to be mirrored
internal static partial class Program
{
    private static void RunCooldownEdge()
    {
        Test("Cooldown edge: whenever the game and we both show a skill, we write the game's own number", CooldownEdgeSameNumberAsGame);
        Test("Cooldown edge: we never take over a skill the game zeroes, hidden, blocked or not recharging", CooldownEdgeNeverOverridesZeroed);
        Test("Cooldown edge: a skill we call recharging never reads 0", CooldownEdgeNeverZero);
        Test("Cooldown edge: equipment above its max, as on the scoreboard with Fuel Cells, adds nothing", CooldownEdgeEquipmentAboveMax);
        Test("Cooldown edge: the equipment rule matches the game's showCooldown for every real cooldown value", CooldownEdgeEquipmentMatchesGame);
        Test("Cooldown edge model: a skill dropping 1 to 0 charges on the same second stays shown and right", CooldownEdgeSkillSameSecond);
        Test("Cooldown edge model: turning skills off mid-refill hides the number the next frame", CooldownEdgeSkillToggleOff);
        Test("Cooldown edge model: a swapped skill on the same icon never shows the old skill's number", CooldownEdgeSkillSwap);
        Test("Cooldown edge model: random play never shows a stale or wrong skill number", CooldownEdgeSkillFuzz);
        Test("Cooldown edge model: a skill refilling with a charge left isn't switched off and on every frame", CooldownEdgeSkillNoChurn);
        Test("Cooldown edge model: random play keeps equipment as the game has it plus only our refills", CooldownEdgeEquipmentFuzz);
        Test("Cooldown edge model: turning equipment off mid-refill hands the text back before it reads 0", CooldownEdgeEquipmentHandBack);
    }

    // Mathf.CeilToInt, as the game writes it
    private static int GameCeil(float value)
    {
        return (int)Math.Ceiling(value);
    }

    private static readonly float[] Remainings = { 1e-7f, 0.001f, 0.5f, 0.99999994f, 1f, 1.0000001f, 2.5f, 3f, 29.9f, 120f };
    private static readonly float[] Rates = { 0.25f, 0.5f, 1f, 1.5f, 2f, 3f };

    private static void CooldownEdgeSameNumberAsGame()
    {
        foreach (float remaining in Remainings)
        {
            foreach (float rate in Rates)
            {
                if (!CooldownRules.SkillRecharging(0, 2, remaining, rate, false, false))
                {
                    throw new AssertFail($"0 of 2, {remaining} s at rate {rate} isn't recharging");
                }

                int game = GameCeil(remaining / rate);
                int ours = CooldownRules.SkillSeconds(remaining, rate);
                Expect(game == ours, $"{remaining} s at rate {rate}: game {game}, ours {ours}");
            }
        }
    }

    private static void CooldownEdgeNeverOverridesZeroed()
    {
        foreach (float remaining in Remainings)
        {
            Expect(!CooldownRules.SkillRecharging(1, 2, remaining, 0f, false, false), "rate 0");
            Expect(!CooldownRules.SkillRecharging(1, 2, remaining, -1f, false, false), "negative rate");
            Expect(!CooldownRules.SkillRecharging(1, 2, remaining, float.NaN, false, false), "NaN rate");
            Expect(!CooldownRules.SkillRecharging(1, 2, remaining, 1f, true, false), "blocked");
            Expect(!CooldownRules.SkillRecharging(1, 2, remaining, 1f, false, true), "hidden");
            Expect(!CooldownRules.SkillRecharging(0, 0, remaining, 1f, false, false), "max stock 0");
        }

        Expect(!CooldownRules.SkillRecharging(1, 2, float.NaN, 1f, false, false), "NaN remaining");
        Expect(!CooldownRules.SkillRecharging(1, 2, -0.5f, 1f, false, false), "negative remaining");
    }

    // The game hides its text when remaining / rate is exactly 0, so a 0 from us is a number the game never shows
    private static void CooldownEdgeNeverZero()
    {
        float[] remainings = { 1e-45f, 1e-30f, 1e-7f, 1f };
        float[] rates = { 1f, 2f, 1e30f, float.PositiveInfinity };
        var bad = new List<string>();
        foreach (float remaining in remainings)
        {
            foreach (float rate in rates)
            {
                if (CooldownRules.SkillRecharging(1, 2, remaining, rate, false, false) && CooldownRules.SkillSeconds(remaining, rate) < 1)
                {
                    bad.Add($"{remaining.ToString("R", CultureInfo.InvariantCulture)} s at rate {rate.ToString("R", CultureInfo.InvariantCulture)}");
                }
            }
        }

        Expect(bad.Count == 0, "recharging but reads 0: " + string.Join(", ", bad));
    }

    // With no EquipmentSlot the game reports a max of 1, and Fuel Cells hold more than that
    private static void CooldownEdgeEquipmentAboveMax()
    {
        Expect(!CooldownRules.EquipmentRecharging(true, false, 3, 1, 20), "3 held, max 1");
        Expect(!CooldownRules.EquipmentRecharging(true, false, 1, 1, 20), "1 of 1 with a stale timer");
        Expect(!CooldownRules.EquipmentRecharging(false, false, 0, 0, 0), "no inventory, all zero");
    }

    // DisplayData.showCooldown is !(stock > 0) && (equipmentDef != null || (uint)cooldownValue > 0)
    // cooldownValue is CeilToInt of a clamped time so never negative, the unsigned compare only differs below 0
    private static void CooldownEdgeEquipmentMatchesGame()
    {
        foreach (bool has in new[] { false, true })
        {
            foreach (int stock in new[] { -1, 0, 1, 2, 5 })
            {
                foreach (int cooldown in new[] { 0, 1, 30, int.MaxValue })
                {
                    bool game = !(stock > 0) && (has || (uint)cooldown > 0u);
                    Expect(game == CooldownRules.EquipmentGameShows(has, stock, cooldown), $"has {has}, stock {stock}, cooldown {cooldown}");
                }
            }
        }
    }

    private sealed class FakeText
    {
        public bool Active;
        public string Content = "0";
        public int Toggles;
        public int Writes;

        public void SetActive(bool value)
        {
            if (Active != value)
            {
                Active = value;
                Toggles++;
            }
        }

        public void Write(int value)
        {
            Content = value.ToString(CultureInfo.InvariantCulture);
            Writes++;
        }
    }

    private sealed class FakeSkill
    {
        public int Stock;
        public int MaxStock;
        public int RechargeStock = 1;
        public float Interval;
        public float Stopwatch;
        public float Rate = 1f;
        public bool Blocked;
        public bool Hidden;

        // GenericSkill.cooldownRemaining
        public float Remaining => Stock != MaxStock && RechargeStock != 0 ? Interval - Stopwatch : 0f;

        public bool IsReady => Stock >= 1;

        public void Tick(float dt)
        {
            if (Stock >= MaxStock || Blocked)
            {
                return;
            }

            Stopwatch += dt * Rate;
            if (Stopwatch >= Interval)
            {
                Stock = Math.Min(MaxStock, Stock + RechargeStock);
                Stopwatch = Stock >= MaxStock ? 0f : Stopwatch - Interval;
            }
        }

        public void Use()
        {
            if (Stock > 0)
            {
                Stock--;
            }
        }
    }

    private sealed class FakeSkillIcon
    {
        public FakeText Text = new FakeText();
        public int PreviousCoolDownInt;
        public FakeSkill? Skill;
    }

    // SkillIcon.LateUpdate's cooldown text part, with CooldownTimers.KeepSkillCountdown's patch on its hide
    private static void GameSkillFrame(FakeSkillIcon icon, bool on)
    {
        FakeSkill s = icon.Skill!;
        float rate = s.Rate;
        float cd = s.Remaining / rate;
        bool v6 = s.Stock > 0 || cd == 0f;
        bool ready = s.IsReady;
        if (s.Hidden || rate == 0f)
        {
            cd = 0f;
            v6 = true;
            ready = true;
        }

        if (v6 || ready || s.Blocked)
        {
            icon.Text.SetActive(on && CooldownRules.SkillRecharging(s.Stock, s.MaxStock, s.Remaining, rate, s.Blocked, s.Hidden));
            return;
        }

        int c = GameCeil(cd);
        if (icon.PreviousCoolDownInt != c)
        {
            icon.PreviousCoolDownInt = c;
            icon.Text.Write(c);
            icon.Text.SetActive(true);
        }
    }

    // CooldownTimers.ShowSkill
    private static void ModSkillFrame(FakeSkillIcon icon, bool on, Dictionary<FakeText, int> written)
    {
        if (!on)
        {
            written.Remove(icon.Text);
            return;
        }

        FakeSkill s = icon.Skill!;
        float remaining = s.Remaining;
        float rate = s.Rate;
        if (!CooldownRules.SkillRecharging(s.Stock, s.MaxStock, remaining, rate, s.Blocked, s.Hidden))
        {
            written.Remove(icon.Text);
            return;
        }

        int seconds = CooldownRules.SkillSeconds(remaining, rate);
        if (!written.TryGetValue(icon.Text, out int last))
        {
            last = -1;
        }

        if (last != seconds || !icon.Text.Active)
        {
            written[icon.Text] = seconds;
            icon.Text.Write(seconds);
        }

        icon.Text.SetActive(true);
    }

    private static void SkillFrame(FakeSkillIcon icon, bool on, Dictionary<FakeText, int> written, float dt)
    {
        icon.Skill!.Tick(dt);
        GameSkillFrame(icon, on);
        ModSkillFrame(icon, on, written);
    }

    // What should be on screen, null when nothing should
    private static string? SkillTruth(FakeSkill s)
    {
        if (s.Hidden || s.Rate <= 0f || s.Blocked)
        {
            return null;
        }

        float cd = s.Remaining / s.Rate;
        return cd > 0f ? GameCeil(cd).ToString(CultureInfo.InvariantCulture) : null;
    }

    private static void ExpectSkillShownRight(FakeSkillIcon icon, bool on, string where)
    {
        FakeSkill s = icon.Skill!;
        string? truth = SkillTruth(s);
        if (icon.Text.Active)
        {
            Expect(truth != null, $"{where}: shows [{icon.Text.Content}] with nothing to count (stock {s.Stock}/{s.MaxStock})");
            Expect(truth == icon.Text.Content, $"{where}: shows [{icon.Text.Content}], should read [{truth}] (stock {s.Stock}/{s.MaxStock}, {s.Remaining} s)");
        }

        if (on && CooldownRules.SkillRecharging(s.Stock, s.MaxStock, s.Remaining, s.Rate, s.Blocked, s.Hidden))
        {
            Expect(icon.Text.Active, $"{where}: refilling but hidden (stock {s.Stock}/{s.MaxStock}, {s.Remaining} s)");
        }
    }

    // Backup Magazine style, the game last showed 1 when the first charge came back, then the second is used at 0.5 s
    private static void CooldownEdgeSkillSameSecond()
    {
        var icon = new FakeSkillIcon { Skill = new FakeSkill { Stock = 2, MaxStock = 2, Interval = 4f } };
        var written = new Dictionary<FakeText, int>();
        const float dt = 0.05f;
        icon.Skill.Use();
        icon.Skill.Use();
        for (int i = 0; i < 200 && icon.Skill.Stock == 0; i++)
        {
            SkillFrame(icon, true, written, dt);
            ExpectSkillShownRight(icon, true, $"empty frame {i}");
        }

        // Now 1 of 2 with a fresh 4 s, run down to the last second
        Expect(icon.Skill.Stock == 1, "one charge back");
        while (icon.Skill.Remaining > 0.5f)
        {
            SkillFrame(icon, true, written, dt);
            ExpectSkillShownRight(icon, true, "1 of 2");
        }

        icon.Skill.Use();
        for (int i = 0; i < 5; i++)
        {
            SkillFrame(icon, true, written, dt);
            ExpectSkillShownRight(icon, true, $"used at the same second, frame {i}");
        }
    }

    private static void CooldownEdgeSkillToggleOff()
    {
        var icon = new FakeSkillIcon { Skill = new FakeSkill { Stock = 1, MaxStock = 2, Interval = 5f } };
        var written = new Dictionary<FakeText, int>();
        for (int i = 0; i < 10; i++)
        {
            SkillFrame(icon, true, written, 0.05f);
        }

        Expect(icon.Text.Active, "shown while on");
        SkillFrame(icon, false, written, 0.05f);
        Expect(!icon.Text.Active, "hidden the frame after it's turned off");

        // Back on with the same second still counting, it has to show again
        SkillFrame(icon, true, written, 0.05f);
        ExpectSkillShownRight(icon, true, "back on");
    }

    // Heretic's replacements or a different GenericSkill pushed onto the same icon, same text object
    private static void CooldownEdgeSkillSwap()
    {
        var first = new FakeSkill { Stock = 1, MaxStock = 2, Interval = 5f };
        var second = new FakeSkill { Stock = 0, MaxStock = 1, Interval = 3f, Stopwatch = 0f };
        var hidden = new FakeSkill { Stock = 1, MaxStock = 2, Interval = 5f, Hidden = true };
        var icon = new FakeSkillIcon { Skill = first };
        var written = new Dictionary<FakeText, int>();
        for (int i = 0; i < 50; i++)
        {
            SkillFrame(icon, true, written, 0.05f);
        }

        // first reads 3 now, second also reads 3
        icon.Skill = second;
        for (int i = 0; i < 30; i++)
        {
            SkillFrame(icon, true, written, 0.05f);
            ExpectSkillShownRight(icon, true, $"after the swap, frame {i}");
        }

        icon.Skill = hidden;
        SkillFrame(icon, true, written, 0.05f);
        Expect(!icon.Text.Active, "a skill that hides its cooldown shows nothing after a swap");
        icon.Skill = first;
        SkillFrame(icon, true, written, 0.05f);
        ExpectSkillShownRight(icon, true, "swapped back");
    }

    private static void CooldownEdgeSkillFuzz()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            FakeSkill[] skills =
            {
                new FakeSkill { Stock = 2, MaxStock = 2, Interval = 5f },
                new FakeSkill { Stock = 1, MaxStock = 1, Interval = 3f },
                new FakeSkill { Stock = 3, MaxStock = 3, Interval = 4f, Rate = 2f },
                new FakeSkill { Stock = 1, MaxStock = 1, Interval = 6f, RechargeStock = 0 },
                new FakeSkill { Stock = 2, MaxStock = 2, Interval = 2f, Hidden = true },
                new FakeSkill { Stock = 12, MaxStock = 12, Interval = 2f, RechargeStock = 12 },
            };
            var icon = new FakeSkillIcon { Skill = skills[0] };
            var written = new Dictionary<FakeText, int>();
            bool on = true;
            for (int frame = 0; frame < 3000; frame++)
            {
                double roll = random.NextDouble();
                string op = "tick";
                if (roll < 0.05)
                {
                    icon.Skill.Use();
                    op = "use";
                }
                else if (roll < 0.06)
                {
                    on = !on;
                    op = on ? "on" : "off";
                }
                else if (roll < 0.065)
                {
                    icon.Skill = skills[random.Next(skills.Length)];
                    op = "swap";
                }
                else if (roll < 0.07)
                {
                    icon.Skill.Blocked = !icon.Skill.Blocked;
                    op = "block";
                }
                else if (roll < 0.075)
                {
                    icon.Skill.Rate = icon.Skill.Rate == 1f ? 0.5f : icon.Skill.Rate == 0.5f ? 0f : 1f;
                    op = "rate";
                }

                SkillFrame(icon, on, written, 1f / 60f);
                ExpectSkillShownRight(icon, on, $"seed {seed} frame {frame} after {op}");
            }
        }
    }

    // In Unity each off and on runs the text's OnDisable and OnEnable, which dirties its mesh and layout
    private static void CooldownEdgeSkillNoChurn()
    {
        var icon = new FakeSkillIcon { Skill = new FakeSkill { Stock = 2, MaxStock = 2, Interval = 5f } };
        var written = new Dictionary<FakeText, int>();
        icon.Skill.Use();
        SkillFrame(icon, true, written, 1f / 60f);
        int toggles = icon.Text.Toggles;
        int writes = icon.Text.Writes;
        for (int i = 0; i < 60; i++)
        {
            SkillFrame(icon, true, written, 1f / 60f);
        }

        int toggled = icon.Text.Toggles - toggles;
        int wrote = icon.Text.Writes - writes;
        Expect(toggled <= 2 && wrote <= 3, $"over 60 frames of one second, the text was switched {toggled} times and rewritten {wrote} times");
    }

    private struct FakeEquipmentData
    {
        public bool HasDef;
        public int Cooldown;
        public int Stock;
        public int MaxStock;
        public bool Disabled;

        public bool Show => !(Stock > 0) && (HasDef || (uint)Cooldown > 0u);
    }

    private sealed class FakeEquipmentIcon
    {
        public FakeText Text = new FakeText();
        public FakeEquipmentData Current;
    }

    // EquipmentIcon.SetDisplayData's cooldown text part
    private static void GameEquipmentFrame(FakeEquipmentIcon icon, FakeEquipmentData next)
    {
        FakeEquipmentData current = icon.Current;
        if (next.Disabled)
        {
            next.Cooldown = current.Cooldown;
            if (!current.Disabled)
            {
                icon.Text.SetActive(false);
            }
        }
        else
        {
            if (current.Show != next.Show)
            {
                icon.Text.SetActive(next.Show);
            }
            else if (current.Disabled)
            {
                icon.Text.SetActive(next.Show);
            }

            if (next.Cooldown != current.Cooldown)
            {
                icon.Text.Write(next.Cooldown);
            }
        }

        icon.Current = next;
    }

    // CooldownTimers.ShowEquipment
    private static void ModEquipmentFrame(FakeEquipmentIcon icon, bool on, HashSet<FakeEquipmentIcon> forced)
    {
        bool wasForced = forced.Contains(icon);
        if (!on && !wasForced)
        {
            return;
        }

        FakeEquipmentData data = icon.Current;
        if (data.Disabled)
        {
            return;
        }

        bool gameShows = CooldownRules.EquipmentGameShows(data.HasDef, data.Stock, data.Cooldown);
        bool ours = on && CooldownRules.EquipmentRecharging(data.HasDef, false, data.Stock, data.MaxStock, data.Cooldown);
        icon.Text.SetActive(gameShows || ours);
        if (ours && !wasForced)
        {
            forced.Add(icon);
        }
        else if (!ours && wasForced)
        {
            forced.Remove(icon);
        }
    }

    private sealed class FakeEquipmentState
    {
        public bool HasDef = true;
        public int Stock = 1;
        public float Timer;
        public float Interval = 3f;

        public void Tick(float dt, int max)
        {
            if (Stock >= max)
            {
                Timer = 0f;
                return;
            }

            Timer -= dt;
            if (Timer <= 0f)
            {
                Stock++;
                Timer = Stock < max ? Timer + Interval : 0f;
            }
        }

        public void Use(int max)
        {
            if (!HasDef || Stock <= 0)
            {
                return;
            }

            if (Stock >= max)
            {
                Timer = Interval;
            }

            Stock--;
        }

        public FakeEquipmentData Data(int max, bool disabled)
        {
            return new FakeEquipmentData
            {
                HasDef = HasDef,
                Stock = Stock,
                MaxStock = max,
                Cooldown = Stock < max ? GameCeil(Math.Max(0f, Timer)) : 0,
                Disabled = disabled,
            };
        }
    }

    private static void ExpectEquipmentFrame(FakeEquipmentIcon mod, FakeEquipmentIcon vanilla, bool on, string where)
    {
        FakeEquipmentData d = mod.Current;
        bool ours = on && !d.Disabled && CooldownRules.EquipmentRecharging(d.HasDef, false, d.Stock, d.MaxStock, d.Cooldown);
        Expect(mod.Text.Content == vanilla.Text.Content, $"{where}: reads [{mod.Text.Content}], the game alone reads [{vanilla.Text.Content}]");
        if (ours)
        {
            Expect(mod.Text.Active, $"{where}: refilling {d.Stock}/{d.MaxStock} at {d.Cooldown} but hidden");
        }
        else
        {
            Expect(mod.Text.Active == vanilla.Text.Active, $"{where}: shown {mod.Text.Active}, the game alone {vanilla.Text.Active} ({d.Stock}/{d.MaxStock} at {d.Cooldown}, on {on}, disabled {d.Disabled})");
        }
    }

    private static void CooldownEdgeEquipmentFuzz()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed);
            var states = new[] { new FakeEquipmentState(), new FakeEquipmentState { Interval = 7f }, new FakeEquipmentState { HasDef = false, Stock = 0 } };
            int slot = 0;
            int max = 2;
            bool disabled = false;
            bool on = true;
            var mod = new FakeEquipmentIcon();
            var vanilla = new FakeEquipmentIcon();
            var forced = new HashSet<FakeEquipmentIcon>();
            for (int frame = 0; frame < 3000; frame++)
            {
                double roll = random.NextDouble();
                string op = "tick";
                if (roll < 0.03)
                {
                    states[slot].Use(max);
                    op = "use";
                }
                else if (roll < 0.04)
                {
                    on = !on;
                    op = on ? "on" : "off";
                }
                else if (roll < 0.045)
                {
                    disabled = !disabled;
                    op = disabled ? "disable" : "enable";
                }
                else if (roll < 0.05)
                {
                    slot = random.Next(states.Length);
                    op = "swap";
                }
                else if (roll < 0.052)
                {
                    max = max >= 4 ? 1 : max + 1;
                    op = "max " + max;
                }

                foreach (FakeEquipmentState state in states)
                {
                    state.Tick(1f / 60f, max);
                }

                FakeEquipmentData data = states[slot].Data(max, disabled);
                GameEquipmentFrame(mod, data);
                ModEquipmentFrame(mod, on, forced);
                GameEquipmentFrame(vanilla, data);
                ExpectEquipmentFrame(mod, vanilla, on, $"seed {seed} frame {frame} after {op}");
            }
        }
    }

    // Fuel Cells, 1 of 2 refilling, turned off, then the charge comes back
    private static void CooldownEdgeEquipmentHandBack()
    {
        var state = new FakeEquipmentState { Stock = 2, Interval = 3f };
        var mod = new FakeEquipmentIcon();
        var forced = new HashSet<FakeEquipmentIcon>();
        state.Use(2);
        for (int i = 0; i < 30; i++)
        {
            state.Tick(1f / 60f, 2);
            GameEquipmentFrame(mod, state.Data(2, false));
            ModEquipmentFrame(mod, true, forced);
        }

        Expect(mod.Text.Active, "shown while refilling");

        // Off while disabled, then enabled again after the refill
        GameEquipmentFrame(mod, state.Data(2, true));
        ModEquipmentFrame(mod, false, forced);
        for (int i = 0; i < 300; i++)
        {
            state.Tick(1f / 60f, 2);
            GameEquipmentFrame(mod, state.Data(2, i < 10));
            ModEquipmentFrame(mod, false, forced);
        }

        Expect(state.Stock == 2, "refilled");
        Expect(!mod.Text.Active, $"a full equipment with the setting off still shows [{mod.Text.Content}]");
        Expect(forced.Count == 0, "still marked as ours");
    }
}
