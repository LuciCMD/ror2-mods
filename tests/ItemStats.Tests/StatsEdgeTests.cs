using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using JustBeBetter.StatsDisplay;
using RoR2;
using Console = System.Console;

// Edge cases for Stats Display, the template, the number text and the combo meter
// The game side can't run here, so the last tests only check what it reads by reflection and note call order
internal static partial class Program
{
    private static void RunStatsEdge()
    {
        Test("Stats edge text: escapes, nesting, odd names and tags stay as typed", StatsEdgeTemplateOdd);
        Test("Stats edge text: each name is looked up once per use, never for escaped or broken ones", StatsEdgeTemplateLookups);
        Test("Stats edge text: a huge text and a run of open brackets fill in linear time", StatsEdgeTemplateHuge);
        Test("Stats edge numbers: a value that rounds to zero never reads -0", StatsEdgeNegativeZero);
        Test("Stats edge numbers: huge and tiny values read without exponents in every culture", StatsEdgeNumberRange);
        Test("Stats edge numbers: armor is continuous at 0, monotonic, and bounded at the extremes", StatsEdgeArmor);
        Test("Stats edge numbers: block chance and Safer Spaces stay in range for any stack count", StatsEdgeItemsRange);
        Test("Stats edge numbers: unknown portal bits read as none, all eight shrink to 46%", StatsEdgePortals);
        Test("Combo edge: a hit exactly a window later ends the old combo and starts a new one", StatsEdgeBoundaryHit);
        Test("Combo edge: many hits in one frame add up and leave together", StatsEdgeSameFrame);
        Test("Combo edge: shrinking the window ends a combo, growing it keeps one going", StatsEdgeWindowChange);
        Test("Combo edge: kills alone make a combo that ends on time", StatsEdgeKillOnly);
        Test("Combo edge: NewStage and NewRun in either order leave clean counts, NewStage keeps the combo", StatsEdgeStageRunOrder);
        Test("Combo edge: a negative saved best combo starts at 0 like the kill combo", StatsEdgeNegativeSavedBest);
        Test("Combo edge: seconds left never exceed the window, never rise, and read the window on a hit", StatsEdgeSecondsLeft);
        Test("Combo edge: random play keeps every record and count invariant", StatsEdgeFuzzInvariants);
        Test("Combo edge: DPS stays within 0.1% of the true sum over ten minutes of steady hits", StatsEdgeDpsDrift);
        Test("Combo edge: DPS reads small hits right once a huge hit has left the window", StatsEdgeDpsAfterHugeHit);
        Test("Combo edge: hits older than the window are let go even when DPS is never read", StatsEdgeQueueBounded);
        Test("Stats edge game: the members read by reflection exist with the types cast to", StatsEdgeReflected);
    }

    private static string EdgeFill(string template, List<string>? unknown = null, List<string>? asked = null)
    {
        var values = new Dictionary<string, string> { ["damage"] = "12", ["a_1"] = "A", ["empty"] = string.Empty, ["back"] = "\\" };
        return StatTemplate.Fill(
            template,
            n =>
            {
                asked?.Add(n);
                return values.TryGetValue(n, out string? v) ? v : null;
            },
            unknown);
    }

    private static void StatsEdgeTemplateOdd()
    {
        Equal("\\\\[damage]", EdgeFill("\\\\[damage]"));
        Equal("12\\", EdgeFill("[damage]\\"));
        Equal("[[[12]]]", EdgeFill("[[[[damage]]]]"));
        Equal("[foo12]", EdgeFill("[foo[damage]]"));
        Equal("<style=cIsDamage>12</style>", EdgeFill("<style=cIsDamage>[damage]</style>"));
        Equal("[dam\nage] [ damage ] [damage ] [dam-age] [dégâts] [伤害]", EdgeFill("[dam\nage] [ damage ] [damage ] [dam-age] [dégâts] [伤害]"));
        Equal("A", EdgeFill("[a_1]"));
        Equal("x  y", EdgeFill("x [empty] y"));
        Equal("\\12", EdgeFill("[back][damage]"));
        Equal("12\r\n12", EdgeFill("[damage]\r\n[damage]"));
        Equal("😀12😀", EdgeFill("😀[damage]😀"));

        var unknown = new List<string>();
        Equal("[Damage] [DAMAGE]", EdgeFill("[Damage] [DAMAGE]", unknown));
        Expect(unknown.SequenceEqual(new[] { "Damage", "DAMAGE" }), "case differences not reported: " + string.Join(",", unknown));
    }

    private static void StatsEdgeTemplateLookups()
    {
        var asked = new List<string>();
        EdgeFill("[damage] \\[damage] [damage [] [a b] [damage][nope]", null, asked);
        Expect(asked.SequenceEqual(new[] { "damage", "damage", "nope" }), "looked up " + string.Join(",", asked));
    }

    private static void StatsEdgeTemplateHuge()
    {
        var big = new StringBuilder();
        for (int i = 0; i < 100000; i++)
        {
            big.Append("Damage: [damage] [nope] \\[damage] ");
        }

        string opens = new string('[', 200000) + "damage" + new string(']', 1);
        string longName = "[" + new string('a', 500000) + "]";
        var watch = Stopwatch.StartNew();
        string filled = EdgeFill(big.ToString());
        string filledOpens = EdgeFill(opens);
        var unknown = new List<string>();
        string filledLong = EdgeFill(longName, unknown);
        watch.Stop();
        Expect(watch.ElapsedMilliseconds < 3000, $"took {watch.ElapsedMilliseconds} ms");
        Expect(filled.Length == big.Length - 100000 * ("[damage]".Length - 2), $"length {filled.Length}");
        Equal(new string('[', 199999) + "12", filledOpens);
        Equal(longName, filledLong);
        Expect(unknown.Count == 1 && unknown[0].Length == 500000, "long name not reported once");
    }

    private static void StatsEdgeNegativeZero()
    {
        var wrong = new List<string>();
        foreach (float value in new[] { -0f, -0.001f, -0.004f, -1e-30f })
        {
            for (int decimals = 0; decimals <= StatMath.MaxDecimals; decimals++)
            {
                string text = StatMath.Number(value, StatMath.Pattern(decimals));
                if (text.TrimStart('-').Trim('0', '.').Length == 0 && text != "0")
                {
                    wrong.Add($"{value.ToString("R", CultureInfo.InvariantCulture)} at {decimals} decimals reads [{text}]");
                }
            }
        }

        // Where a player could see it, armor a hair below 0 shows as a -0% reduction
        string armor = StatMath.Number(StatMath.ArmorReductionPercent(-0.001f), StatMath.Pattern(2)) + "%";
        if (armor != "0%")
        {
            wrong.Add($"armor -0.001 reduction reads [{armor}]");
        }

        Expect(wrong.Count == 0, string.Join("; ", wrong));
    }

    private static void StatsEdgeNumberRange()
    {
        CultureInfo was = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "", "de-DE", "fr-FR", "tr-TR", "sv-SE", "ar-SA", "fa-IR", "hi-IN" })
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                foreach (float value in new[] { 1e20f, float.MaxValue, 1e-10f, 1234567.891f, -1234.5f, 0.005f })
                {
                    string text = StatMath.Number(value, StatMath.Pattern(2));
                    Expect(!text.Contains('E') && !text.Contains(',') && text.All(c => c < 128),
                        $"{culture}: {value.ToString("R", CultureInfo.InvariantCulture)} reads [{text}]");
                }

                Equal("-1234.5", StatMath.Number(-1234.5f, StatMath.Pattern(4)));
            }
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    private static void StatsEdgeArmor()
    {
        Expect(Math.Abs(StatMath.DamageTaken(-1e-6f) - StatMath.DamageTaken(1e-6f)) < 1e-6f, "jump at 0 armor");
        float last = float.PositiveInfinity;
        for (float armor = -100000f; armor <= 100000f; armor += 7.3f)
        {
            float taken = StatMath.DamageTaken(armor);
            Expect(taken <= last + 1e-6f, $"damage taken rises at armor {armor}: {taken} after {last}");
            Expect(taken > 0f && taken <= 2f, $"damage taken {taken} at armor {armor}");
            last = taken;
        }

        foreach (float armor in new[] { float.MaxValue, -float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, -100f, -99.99f })
        {
            float percent = StatMath.ArmorReductionPercent(armor);
            Expect(!float.IsNaN(percent) && percent <= 100f && percent >= -100f, $"reduction {percent} at armor {armor}");
        }

        // Game's HealthComponent multiplier, 2 - 100 / (100 - armor) under 0
        Expect(Math.Abs(StatMath.DamageTaken(-50f) - (2f - 100f / 150f)) < 1e-6f, "-50 armor");
        Expect(Math.Abs(StatMath.EffectiveHealth(1000f, 300f) - 4000f) < 0.1f, "ehp at 300 armor");
    }

    private static void StatsEdgeItemsRange()
    {
        float lastBlock = 0f;
        foreach (int stacks in new[] { 1, 2, 5, 50, 1000, 100000, int.MaxValue })
        {
            float block = StatMath.BlockChancePercent(stacks);
            Expect(block > lastBlock - 1e-4f && block <= 100f, $"block {block} at {stacks}");
            lastBlock = block;
        }

        float lastSafer = float.PositiveInfinity;
        foreach (int stacks in new[] { 1, 2, 10, 100, 1000, int.MaxValue })
        {
            float safer = StatMath.SaferSpacesSeconds(stacks);
            Expect(!float.IsNaN(safer) && safer >= 0f && safer <= lastSafer, $"safer spaces {safer} at {stacks}");
            lastSafer = safer;
        }

        Expect(float.IsNaN(StatMath.SaferSpacesSeconds(int.MinValue)), "negative stacks");
    }

    private static void StatsEdgePortals()
    {
        Equal("<style=cStack>None</style>", StatMath.PortalText((StatMath.Portals)256));
        Equal("<style=cStack>None</style>", StatMath.PortalText((StatMath.Portals)(-512)));
        string all = StatMath.PortalText((StatMath.Portals)255);
        Expect(all.StartsWith("<size=46%>", StringComparison.Ordinal) && all.EndsWith("</size>", StringComparison.Ordinal), all);
        Expect(StatMath.PortalText((StatMath.Portals)(-1)) == all, "extra bits change the text");
    }

    private static void StatsEdgeBoundaryHit()
    {
        var meter = new ComboMeter(0f, 0);
        meter.Hit(1000.1f, 5f, 100f, true);
        meter.Hit(1005.1f, 5f, 10f, false);
        bool ended = 1005.1f - 1000.1f >= 5f;
        if (ended)
        {
            Expect(meter.Combo == 10f && meter.ComboKills == 0, $"old combo carried, combo {meter.Combo} kills {meter.ComboKills}");
            Expect(Math.Abs(meter.Dps(1005.1f, 5f) - 2f) < 1e-5f, $"DPS kept the old hit {meter.Dps(1005.1f, 5f)}");
        }
        else
        {
            Expect(meter.Combo == 110f && meter.ComboKills == 1, $"combo split early, combo {meter.Combo}");
            Expect(Math.Abs(meter.Dps(1005.1f, 5f) - 22f) < 1e-5f, $"DPS dropped the old hit early {meter.Dps(1005.1f, 5f)}");
        }

        Expect(meter.BestCombo == 110f || meter.BestCombo == 100f, $"best {meter.BestCombo}");
        Expect(meter.BestKillCombo == 1 && meter.BestKillComboThisRun == 1, "kill best lost when the combo ended inside a hit");
    }

    private static void StatsEdgeSameFrame()
    {
        var meter = new ComboMeter(0f, 0);
        for (int i = 0; i < 1000; i++)
        {
            meter.Hit(7f, 5f, 3f, i % 10 == 0);
        }

        Expect(meter.Combo == 3000f && meter.ComboKills == 100, $"combo {meter.Combo} kills {meter.ComboKills}");
        Expect(Math.Abs(meter.Dps(7f, 5f) - 600f) < 0.01f, $"DPS {meter.Dps(7f, 5f)}");
        Expect(meter.SecondsLeft(7f, 5f) == 5, $"left {meter.SecondsLeft(7f, 5f)}");
        Expect(meter.Dps(12f, 5f) == 0f, "hits stayed past the window");
        Expect(meter.End(12f, 5f) && meter.Combo == 0f, "combo didn't end");
    }

    private static void StatsEdgeWindowChange()
    {
        var meter = new ComboMeter(0f, 0);
        meter.Hit(0f, 30f, 100f, false);
        Expect(!meter.End(10f, 30f), "ended inside a 30 s window");
        Expect(meter.SecondsLeft(10f, 30f) == 20, $"left {meter.SecondsLeft(10f, 30f)}");
        Expect(meter.SecondsLeft(10f, 5f) == 0, "left after shrinking");
        Expect(meter.End(10f, 5f), "shrinking the window didn't end the combo");
        Expect(meter.BestCombo == 100f, "best lost");

        var grow = new ComboMeter(0f, 0);
        grow.Hit(0f, 5f, 100f, false);
        Expect(!grow.End(8f, 30f) && grow.Combo == 100f, "growing the window ended the combo");
        grow.Hit(9f, 30f, 50f, false);
        Expect(grow.Combo == 150f, $"combo {grow.Combo}");
    }

    private static void StatsEdgeKillOnly()
    {
        var meter = new ComboMeter(0f, 0);
        meter.Hit(0f, 5f, 0f, true);
        meter.Hit(1f, 5f, float.NaN, true);
        Expect(meter.Combo == 0f && meter.ComboKills == 2, $"combo {meter.Combo} kills {meter.ComboKills}");
        Expect(meter.SecondsLeft(1f, 5f) == 5, "no timer on a kill-only combo");
        Expect(!meter.End(5.9f, 5f), "ended early");
        Expect(meter.End(6f, 5f) && meter.ComboKills == 0, "kill-only combo didn't end");
        Expect(meter.BestKillCombo == 2, "kill best lost");
    }

    private static void StatsEdgeStageRunOrder()
    {
        var a = new ComboMeter(0f, 0);
        a.Hit(0f, 5f, 10f, true);
        a.NewStage();
        Expect(a.KillsThisStage == 0 && a.KillsThisRun == 1 && a.Combo == 10f && a.ComboKills == 1, "NewStage cleared the run or combo");
        a.NewRun();
        a.NewStage();
        Expect(a.KillsThisStage == 0 && a.KillsThisRun == 0 && a.Combo == 0f, "run then stage");

        var b = new ComboMeter(0f, 0);
        b.Hit(0f, 5f, 10f, true);
        b.NewStage();
        b.NewRun();
        Expect(b.KillsThisStage == 0 && b.KillsThisRun == 0 && b.ComboKills == 0, "stage then run");
        Expect(b.SecondsLeft(0f, 5f) == 0 && b.Dps(0f, 5f) == 0f, "timer or DPS carried into a new run");
        b.Hit(1f, 5f, 5f, true);
        Expect(b.KillsThisStage == 1 && b.KillsThisRun == 1 && b.Combo == 5f, "first hit of the new run");
    }

    private static void StatsEdgeNegativeSavedBest()
    {
        var meter = new ComboMeter(-50f, -3);
        Expect(meter.BestKillCombo == 0, "negative kill best kept");
        Expect(meter.BestCombo == 0f, $"a hand-edited Best Combo of -50 shows as maxCombo [{meter.BestCombo}] until the first hit");
    }

    private static void StatsEdgeSecondsLeft()
    {
        foreach (float window in new[] { 1f, 2.5f, 5f, 30f })
        {
            foreach (float start in new[] { 0f, 1000.1f, 36000.37f })
            {
                var meter = new ComboMeter(0f, 0);
                meter.Hit(start, window, 1f, false);
                int cap = (int)Math.Ceiling(window);
                Expect(meter.SecondsLeft(start, window) == cap, $"window {window} from {start}: on the hit reads {meter.SecondsLeft(start, window)}");
                int last = int.MaxValue;
                for (int step = 0; step <= 4000; step++)
                {
                    float now = start + window * step / 3000f;
                    int left = meter.SecondsLeft(now, window);
                    Expect(left <= cap && left >= 0 && left <= last, $"window {window} from {start} at {now}: {left} after {last}");
                    last = left;
                }

                Expect(last == 0, $"window {window} from {start}: still {last} after the window");
            }
        }
    }

    private static void StatsEdgeFuzzInvariants()
    {
        var rng = new Random(1234);
        var meter = new ComboMeter(250f, 3);
        float now = 0f;
        float bestEver = 250f;
        int bestKillsEver = 3;
        for (int i = 0; i < 200000; i++)
        {
            float window = 1f + (float)rng.NextDouble() * 29f;
            now += (float)(rng.NextDouble() * rng.NextDouble() * 8.0);
            int op = rng.Next(100);
            if (op < 80)
            {
                float damage = op < 2 ? float.NaN : op < 4 ? -5f : (float)(rng.NextDouble() * 1000.0);
                meter.Hit(now, window, damage, rng.Next(6) == 0);
            }
            else if (op < 92)
            {
                meter.End(now, window);
            }
            else if (op < 98)
            {
                float dps = meter.Dps(now, window);
                Expect(dps >= 0f && !float.IsNaN(dps) && !float.IsInfinity(dps), $"step {i}: DPS {dps}");
            }
            else if (op < 99)
            {
                meter.NewStage();
            }
            else
            {
                meter.NewRun();
            }

            Expect(meter.BestCombo >= bestEver && meter.BestKillCombo >= bestKillsEver, $"step {i}: an all-time best went down");
            bestEver = meter.BestCombo;
            bestKillsEver = meter.BestKillCombo;
            Expect(meter.Combo >= 0f && meter.Combo <= meter.BestComboThisRun && meter.BestComboThisRun <= meter.BestCombo,
                $"step {i}: combo {meter.Combo} run best {meter.BestComboThisRun} best {meter.BestCombo}");
            Expect(meter.ComboKills >= 0 && meter.ComboKills <= meter.BestKillComboThisRun && meter.BestKillComboThisRun <= meter.BestKillCombo,
                $"step {i}: kills {meter.ComboKills} run best {meter.BestKillComboThisRun} best {meter.BestKillCombo}");
            Expect(meter.KillsThisStage <= meter.KillsThisRun, $"step {i}: stage kills over run kills");
            int left = meter.SecondsLeft(now, window);
            Expect(left >= 0 && left <= (int)Math.Ceiling(window), $"step {i}: {left} s left of a {window} s window");
        }
    }

    // Steady play, normal hits with crits and the odd big proc, read 4 times a second as the panel does
    private static void StatsEdgeDpsDrift()
    {
        var rng = new Random(99);
        var meter = new ComboMeter(0f, 0);
        var hits = new List<(float Time, float Damage)>();
        const float window = 5f;
        float worst = 0f;
        string worstAt = string.Empty;
        float nextRead = 0f;
        for (float now = 0f; now < 600f; now += 0.05f)
        {
            float damage = (float)(50 + rng.NextDouble() * 4950);
            damage *= rng.Next(4) == 0 ? 2f : 1f;
            damage *= rng.Next(200) == 0 ? 100f : 1f;
            meter.Hit(now, window, damage, false);
            hits.Add((now, damage));
            if (now >= nextRead)
            {
                nextRead = now + 0.25f;
                double exact = hits.Where(h => now - h.Time < window).Sum(h => (double)h.Damage) / window;
                float dps = meter.Dps(now, window);
                float error = (float)(Math.Abs(dps - exact) / exact);
                if (error > worst)
                {
                    worst = error;
                    worstAt = $"at {now:0.00} s DPS {dps} true {exact:0.0}";
                }
            }
        }

        Expect(worst < 0.001f, $"worst error {worst:P3} {worstAt}");
    }

    private static void StatsEdgeDpsAfterHugeHit()
    {
        var meter = new ComboMeter(0f, 0);
        var hits = new List<(float Time, float Damage)>();
        const float window = 5f;
        meter.Hit(0f, window, 5e7f, true);
        hits.Add((0f, 5e7f));
        var wrong = new List<string>();
        for (int step = 1; step < 200; step++)
        {
            float now = step * 0.05f;
            meter.Hit(now, window, 3f, false);
            hits.Add((now, 3f));
            if (step % 5 == 0)
            {
                double exact = hits.Where(h => now - h.Time < window).Sum(h => (double)h.Damage) / window;
                float dps = meter.Dps(now, window);
                if (Math.Abs(dps - exact) > exact * 0.01)
                {
                    wrong.Add($"{now:0.00} s reads {dps:0.##}, true {exact:0.##}");
                }
            }
        }

        Expect(wrong.Count == 0, "after a 50M hit then 3 damage ticks every 0.05 s: " + string.Join("; ", wrong.Take(4)) + $" ({wrong.Count} reads off)");
    }

    private static void StatsEdgeQueueBounded()
    {
        var meter = new ComboMeter(0f, 0);
        for (int i = 0; i < 100000; i++)
        {
            meter.Hit(i * 0.1f, 5f, 10f, false);
        }

        FieldInfo field = typeof(ComboMeter).GetField("recent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int held = ((ICollection)field.GetValue(meter)!).Count;
        Expect(held <= 60, $"{held} hits held after 10000 s of play with [dps] and [percentDps] not in the text");
    }

    private static void StatsEdgeReflected()
    {
        FieldInfo? preview = typeof(PortalSpawner).GetField("previewChildName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Expect(preview != null && preview.FieldType == typeof(string), "PortalSpawner.previewChildName missing or not a string");
        MethodInfo? decay = typeof(HealthComponent).GetMethod("GetBarrierDecayRate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        Expect(decay != null && decay.ReturnType == typeof(float), "HealthComponent.GetBarrierDecayRate missing or not a float");

        // Notes on call order, for the in-game checklist
        Type[] types;
        try
        {
            types = typeof(Stage).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray()!;
        }

        foreach ((Type owner, string callee) in new[]
        {
            (typeof(Stage), "PreStartClient"),
            (typeof(PortalSpawner), "set_WillSpawn"),
            (typeof(PortalSpawner), "set_NetworkwillSpawn"),
            (typeof(PortalSpawner), "OnWillSpawnUpdated"),
            (typeof(HealthComponent), "SendDamageDealt"),
            (typeof(GlobalEventManager), "ClientDamageNotified"),
        })
        {
            Console.WriteLine($"     note, callers of {owner.Name}.{callee}: " + string.Join(", ", CallersOf(types, owner, callee)));
        }

        foreach ((Type owner, string name) in new[] { (typeof(GlobalEventManager), "ClientDamageNotified"), (typeof(HealthComponent), "HandleDamageDealt"), (typeof(Stage), "PreStartClient") })
        {
            foreach (MethodInfo m in owner.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == name))
            {
                Console.WriteLine($"     note, {owner.Name}.{name} ({RawIl(m)?.Length ?? -1} IL bytes) calls: "
                    + string.Join(", ", Callees(m).Select(c => c.DeclaringType?.Name + "." + c.Name).Distinct()));
            }
        }

        // Whether the victim's health is already 0 when the damage message goes out, in call order
        MethodInfo? take = typeof(HealthComponent).GetMethod("TakeDamageProcess", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (take != null)
        {
            Console.WriteLine("     note, TakeDamageProcess health and message order: " + string.Join(" > ", Callees(take)
                .Select(c => c.Name)
                .Where(n => n.Contains("health", StringComparison.OrdinalIgnoreCase) || n.Contains("Send", StringComparison.Ordinal) || n == "Die" || n.Contains("Kill", StringComparison.Ordinal))));
        }
    }

    private static IEnumerable<string> CallersOf(Type[] types, Type owner, string callee)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var callers = new List<string>();
        foreach (Type type in types)
        {
            MethodBase[] methods;
            try
            {
                methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).ToArray();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (MethodBase method in methods)
            {
                if (Callees(method).Any(m => m.Name == callee && m.DeclaringType == owner))
                {
                    callers.Add(type.Name + "." + method.Name);
                }
            }
        }

        return callers;
    }

    private static readonly Dictionary<string, System.Reflection.PortableExecutable.PEReader> PeReaders = new();

    // Straight from the file, as GetMethodBody loads every local's type and some only load in the game
    private static byte[]? RawIl(MethodBase method)
    {
        try
        {
            string path = method.Module.Assembly.Location;
            if (!PeReaders.TryGetValue(path, out var pe))
            {
                pe = new System.Reflection.PortableExecutable.PEReader(System.IO.File.OpenRead(path));
                PeReaders[path] = pe;
            }

            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var handle = (System.Reflection.Metadata.MethodDefinitionHandle)System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(method.MetadataToken);
            int rva = reader.GetMethodDefinition(handle).RelativeVirtualAddress;
            return rva == 0 ? null : System.Reflection.Metadata.PEReaderExtensions.GetMethodBody(pe, rva).GetILBytes();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    private static IEnumerable<MethodBase> Callees(MethodBase method)
    {
        if (OneByte[0].Size == 0)
        {
            foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var op = (OpCode)f.GetValue(null)!;
                ushort v = (ushort)op.Value;
                if (v < 0x100)
                {
                    OneByte[v] = op;
                }
                else if ((v & 0xff00) == 0xfe00)
                {
                    TwoByte[v & 0xff] = op;
                }
            }
        }

        byte[]? il = RawIl(method);
        var found = new List<MethodBase>();
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
            if (op.OperandType == OperandType.InlineMethod)
            {
                try
                {
                    MethodBase? m = method.Module.ResolveMethod(BitConverter.ToInt32(il, pos),
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                    if (m != null)
                    {
                        found.Add(m);
                    }
                }
                catch (Exception)
                {
                    // Generic or missing reference, not what's being looked for
                }
            }

            pos += size;
        }

        return found;
    }
}
