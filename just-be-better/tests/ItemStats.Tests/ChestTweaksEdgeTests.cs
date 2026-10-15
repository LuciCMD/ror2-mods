using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using JustBeBetter.ChestTweaks;
using RoR2;
using UnityEngine;

// Chest Tweaks at its edges
// ChestTweaksFeature and UsedHider need Unity and the hooks to run, so the reflection tests check every game member
// they reach by name, and CtSim mirrors their state logic as written against the 1.5.0 decompile of PurchaseInteraction,
// MultiShopController, DroneVendorMultiShopController and the barrel states
// A change to ChestTweaksFeature or UsedHider has to be mirrored in CtSim
internal static partial class Program
{
    private static void RunChestTweaksEdge()
    {
        Test("Chest tweaks edge: the game members the feature reaches by name exist where it expects", ChestEdgeReflected);
        Test("Chest tweaks edge: every prefab name keeps its kind as a spawned copy, a placed copy and under a chest holder", ChestEdgeEveryPrefabVariant);
        Test("Chest tweaks edge: odd names strip to the prefab name without throwing", ChestEdgeOddNames);
        Test("Chest tweaks edge: every kind has an outline switch and every hideable kind a hide switch", ChestEdgeSettingsCoverKinds);
        Test("Chest tweaks edge: OnSyncAvailable called again for the same value adds no second hider or stand", ChestEdgeSyncTwice);
        Test("Chest tweaks edge: a multishop stand goes only after its last terminal, in any order, and comes back with one", ChestEdgeTerminalOrders);
        Test("Chest tweaks edge: an adaptive chest's availability flips during its spin don't hide it, Opened does", ChestEdgeRouletteSpin);
        Test("Chest tweaks edge: a Delusion reset brings a hidden chest and its outline back, and it hides again once reopened", ChestEdgeDelusion);
        Test("Chest tweaks edge: outline off puts back the Highlight's own isOn and colour, on again reapplies", ChestEdgeOutlineRoundTrip);
        Test("Chest tweaks edge: a colour change while gone shows once the chest comes back", ChestEdgeColourWhileGone);
        Test("Chest tweaks edge: a Highlight with no targets and no models is left alone", ChestEdgeNoTargets);
        Test("Chest tweaks edge: hide off then on during the delay shows it in between and restarts the delay", ChestEdgeHideOffOnMidDelay);
        Test("Chest tweaks edge: Fade toggled during the delay or after ends in the new mode for a used chest", ChestEdgeFadeToggleTracked);
        Test("Chest tweaks edge: Disable puts everything back and nothing fires after", ChestEdgeDisable);
        Test("Chest tweaks edge: destroyed objects in the tables don't throw and get pruned", ChestEdgeDestroyedKeys);
        Test("Chest tweaks edge: Hide Used Shops off then on hides a bought-out multishop's stand again", ChestEdgeStandHideOffOn);
        Test("Chest tweaks edge: Keep Used Ones as Drifter turned off hides a stand bought out as Drifter", ChestEdgeStandKeepDrifterOff);
        Test("Chest tweaks edge: Fade toggled during a stand's delay leaves the stand in the new mode", ChestEdgeStandFadeMidDelay);
        Test("Chest tweaks edge: a drone shop stand comes back when Executive Card reopens its terminals", ChestEdgeDroneStandExecutiveCard);
        Test("Chest tweaks edge: hiding turned off leaves a multishop terminal unfaded when its stand has no ModelLocator", ChestEdgeStandRestoreOverTerminal);
        Test("Chest tweaks edge: Drifter is recognised once BodyCatalog is ready even if checked before", ChestEdgeDrifterBeforeCatalog);
        Test("Chest tweaks edge: the outline on what you look at differs from the always-on outline", ChestEdgeLookAtColour);
    }

    private const BindingFlags ChestAny = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static void ChestEdgeReflected()
    {
        Type? opened = typeof(RouletteChestController).GetNestedType("Opened", BindingFlags.NonPublic);
        Expect(opened != null, "RouletteChestController.Opened, a private nested class");
        MethodInfo? enter = opened!.GetMethod(nameof(EntityStates.EntityState.OnEnter), BindingFlags.Instance | BindingFlags.Public);

        // Declared on Opened itself, or the hook would land on a base state and fire for every state
        Expect(enter != null && enter.DeclaringType == opened, $"Opened.OnEnter declared on {enter?.DeclaringType}");
        Expect(typeof(EntityStates.EntityState).IsAssignableFrom(opened), "Opened isn't an EntityState, the Hook delegate wouldn't bind");
        Expect(typeof(EntityStates.Barrel.Opened).GetMethod("OnEnter")?.DeclaringType == typeof(EntityStates.Barrel.Opened), "Barrel.Opened.OnEnter");
        Expect(typeof(EntityStates.Barrel.Closing).GetMethod("OnEnter")?.DeclaringType == typeof(EntityStates.Barrel.Closing), "Barrel.Closing.OnEnter");
        Expect(typeof(PurchaseInteraction).GetMethod("OnSyncAvailable", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null) != null,
            "PurchaseInteraction.OnSyncAvailable(bool)");
        Expect(typeof(PurchaseInteraction).GetField("available")?.FieldType == typeof(bool), "PurchaseInteraction.available");
        Expect(typeof(MultiShopController).GetProperty("terminalGameObjectsList") != null, "MultiShopController.terminalGameObjectsList");
        Expect(typeof(Highlight).GetProperty("highlightTargetValid") != null, "Highlight.highlightTargetValid");
        Expect(typeof(Highlight).GetMethod("SetTargetRendererList") != null, "Highlight.SetTargetRendererList");
        Expect(typeof(Highlight).GetField("CustomColor")?.FieldType == typeof(Color), "Highlight.CustomColor");
        Expect(typeof(CommonShaderProperties).GetField("_Fade", BindingFlags.Static | BindingFlags.Public) != null, "CommonShaderProperties._Fade");
        Expect(typeof(BodyCatalog).GetMethod("FindBodyIndex", new[] { typeof(string) }) != null, "BodyCatalog.FindBodyIndex(string)");
    }

    private static void ChestEdgeEveryPrefabVariant()
    {
        var prefabs = (Dictionary<string, ChestKind>)typeof(ChestKinds).GetField("Prefabs", ChestAny)!.GetValue(null)!;
        Expect(prefabs.Count >= 90, $"only {prefabs.Count} prefab names");
        var wrong = new List<string>();
        foreach ((string name, ChestKind kind) in prefabs)
        {
            foreach ((string variant, string[] parents) in new[]
            {
                (name + "(Clone)", Array.Empty<string>()),
                (name + " (2)", Array.Empty<string>()),
                (name + " (11)(Clone)", Array.Empty<string>()),
                (name + "(Clone)", new[] { "Chests" }),
                (name + " (1)", new[] { "HOLDER: Preplaced Objects" }),
            })
            {
                ChestKind? got = ChestKinds.Of(variant, parents);
                if (got != kind)
                {
                    wrong.Add($"{variant} under [{string.Join("/", parents)}] gave {got?.ToString() ?? "null"}, wanted {kind}");
                }
            }
        }

        Expect(wrong.Count == 0, $"{wrong.Count} wrong, first: {wrong.FirstOrDefault()}");
    }

    private static void ChestEdgeOddNames()
    {
        Equal("Chest1", ChestKinds.BaseName("Chest1(Clone)(Clone)"));
        Equal("Chest1", ChestKinds.BaseName("Chest1 (Clone)"));
        Equal("Chest1", ChestKinds.BaseName("  Chest1(Clone)  "));
        Equal("Chest1", ChestKinds.BaseName("Chest1(Clone) (4)"));
        Equal("Chest1", ChestKinds.BaseName("Chest1  (4)"));
        Equal("", ChestKinds.BaseName("(Clone)"));
        Equal("(1)", ChestKinds.BaseName(" (1)"));
        Equal("A (", ChestKinds.BaseName("A ("));
        Equal("Chest1 (2)x", ChestKinds.BaseName("Chest1 (2)x"));
        Equal("Chest1 (-2)", ChestKinds.BaseName("Chest1 (-2)"));
        Expect(ChestKinds.Of("   ", Array.Empty<string>()) == null, "whitespace counted as a kind");
        Expect(ChestKinds.Of("(Clone)", Array.Empty<string>()) == null, "a bare (Clone) counted as a kind");
        Expect(ChestKinds.Of("Chest1(clone)", Array.Empty<string>()) == null, "lowercase clone suffix matched, Unity never writes it");
    }

    private static void ChestEdgeSettingsCoverKinds()
    {
        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "SourceRoot").Value!;
        string text = File.ReadAllText(System.IO.Path.Combine(root, "ChestTweaks", "ChestTweaksSettings.cs"));
        var outline = Regex.Matches(text, @"BindOutline\(config, ChestKind\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        var hide = Regex.Matches(text, @"BindHide\(config, ChestKind\.(\w+)").Select(m => m.Groups[1].Value).ToList();
        foreach (ChestKind kind in ChestKinds.All)
        {
            Expect(outline.Count(k => k == kind.ToString()) == 1, $"{kind} has {outline.Count(k => k == kind.ToString())} outline switches");
            int hides = hide.Count(k => k == kind.ToString());
            Expect(hides == (ChestKinds.CanHide(kind) ? 1 : 0), $"{kind} has {hides} hide switches");
        }
    }

    // Host order from MultiShopController.OnPurchase, the bought one flips twice, then a client's deserialize repeats each
    private static void ChestEdgeSyncTwice()
    {
        var sim = new CtSim();
        CtShop shop = sim.AddMultiShop(3);
        sim.OnSyncAvailable(shop.Terminals[0], false);
        foreach (CtObj terminal in shop.Terminals)
        {
            sim.OnSyncAvailable(terminal, false);
        }

        foreach (CtObj terminal in shop.Terminals)
        {
            sim.OnSyncAvailable(terminal, false);
        }

        foreach (CtObj terminal in shop.Terminals)
        {
            Expect(sim.LiveHiders(terminal) == 1, $"{terminal.Name} has {sim.LiveHiders(terminal)} hiders");
        }

        Expect(sim.LiveHiders(shop.Stand) == 1 && sim.Stands.Count == 1, $"stand has {sim.LiveHiders(shop.Stand)} hiders");
        sim.Tick(1.1f);
        Expect(shop.Terminals.All(t => !CtSim.Shown(t)) && !CtSim.Shown(shop.Stand), "not all gone after the delay");
    }

    private static void ChestEdgeTerminalOrders()
    {
        foreach (int[] order in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
        {
            var sim = new CtSim();
            CtShop shop = sim.AddMultiShop(3);
            for (int i = 0; i < order.Length; i++)
            {
                sim.OnSyncAvailable(shop.Terminals[order[i]], false);
                bool last = i == order.Length - 1;
                Expect(sim.Stands.ContainsKey(shop.Stand) == last, $"order {string.Join("", order)}: stand queued after {i + 1} flips");
            }

            sim.Tick(1.1f);
            Expect(!CtSim.Shown(shop.Stand), $"order {string.Join("", order)}: stand still shown");

            CtObj back = shop.Terminals[order[1]];
            sim.OnSyncAvailable(back, true);
            Expect(CtSim.Shown(back) && CtSim.Shown(shop.Stand), $"order {string.Join("", order)}: a terminal back didn't bring the stand back");
            sim.Tick(1.1f);
            Expect(CtSim.Shown(back) && CtSim.Shown(shop.Stand), $"order {string.Join("", order)}: came back then went again");

            sim.OnSyncAvailable(back, false);
            sim.Tick(1.1f);
            Expect(!CtSim.Shown(back) && !CtSim.Shown(shop.Stand), $"order {string.Join("", order)}: bought again but stand stayed");
        }
    }

    // RouletteChestController's states set available true, false, true, false, false before Opened
    private static void ChestEdgeRouletteSpin()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("CasinoChest(Clone)");
        chest.Available = true;
        foreach (bool available in new[] { true, false, true, false, false })
        {
            sim.OnSyncAvailable(chest, available);
            sim.Tick(0.6f);
        }

        Expect(CtSim.Shown(chest) && sim.LiveHiders(chest) == 0, "an availability flip hid the adaptive chest");
        sim.MarkUsed(chest);
        sim.Tick(1.1f);
        Expect(!CtSim.Shown(chest), "Opened didn't hide the adaptive chest");
        Expect(!sim.IsOutlined(chest), "the gone adaptive chest kept its outline");
    }

    private static void ChestEdgeDelusion()
    {
        foreach (bool fade in new[] { true, false })
        {
            var sim = new CtSim { Fade = fade };
            CtObj chest = sim.AddTracked("Chest1(Clone)");
            sim.MarkUsed(chest);
            sim.Tick(1.1f);
            Expect(!CtSim.Shown(chest) && !sim.IsOutlined(chest), $"fade {fade}: not gone");

            sim.MarkAvailable(chest);
            Expect(CtSim.Shown(chest) && sim.IsOutlined(chest), $"fade {fade}: the Delusion reset didn't bring it back with its outline");
            sim.Tick(2f);
            Expect(CtSim.Shown(chest), $"fade {fade}: went again on its own after the reset");

            sim.MarkUsed(chest);
            sim.Tick(1.1f);
            Expect(!CtSim.Shown(chest) && !sim.IsOutlined(chest), $"fade {fade}: reopened but stayed");
        }
    }

    private static void ChestEdgeOutlineRoundTrip()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("Chest1(Clone)", hl =>
        {
            hl.IsOn = true;
            hl.Mode = 2;
            hl.Custom = 9;
        });
        Expect(sim.IsOutlined(chest), "not outlined");

        sim.OutlineOn.Remove(ChestKind.Chest);
        sim.RefreshAll();
        CtHl h = chest.Highlight!;
        Expect(h.IsOn && h.Mode == 2 && h.Custom == 9, $"put back as isOn {h.IsOn} mode {h.Mode} custom {h.Custom}");

        sim.OutlineOn.Add(ChestKind.Chest);
        sim.Colour = 4;
        sim.RefreshAll();
        Expect(sim.IsOutlined(chest), "outline on again didn't reapply with the new colour");
    }

    private static void ChestEdgeColourWhileGone()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("Chest1(Clone)");
        sim.MarkUsed(chest);
        sim.Tick(1.1f);
        sim.Colour = 7;
        sim.RefreshAll();
        Expect(!sim.IsOutlined(chest), "a colour change put the outline back on a gone chest");
        sim.MarkAvailable(chest);
        Expect(sim.IsOutlined(chest) && chest.Highlight!.Custom == 7, "came back without the new colour");
    }

    private static void ChestEdgeNoTargets()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("Chest1(Clone)", hl =>
        {
            hl.TargetValid = false;
            hl.HasModels = false;
        });
        CtHl h = chest.Highlight!;
        Expect(!h.IsOn && h.Mode == 0, "a Highlight with nothing to outline was switched on");
        sim.OutlineOn.Remove(ChestKind.Chest);
        sim.RefreshAll();
        Expect(!h.IsOn && h.Mode == 0, "outline off changed a Highlight never outlined");
    }

    private static void ChestEdgeHideOffOnMidDelay()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("Chest1(Clone)");
        sim.MarkUsed(chest);
        sim.Tick(0.5f);
        sim.HideOn.Remove(ChestKind.Chest);
        sim.RefreshAll();
        sim.Tick(0.6f);
        Expect(CtSim.Shown(chest) && sim.IsOutlined(chest), "went with hiding off");
        sim.HideOn.Add(ChestKind.Chest);
        sim.RefreshAll();
        sim.Tick(0.6f);
        Expect(CtSim.Shown(chest), "the delay didn't restart");
        sim.Tick(0.5f);
        Expect(!CtSim.Shown(chest), "never went once hiding was back");
    }

    private static void ChestEdgeFadeToggleTracked()
    {
        foreach (float toggleAt in new[] { 0.5f, 1.5f })
        {
            var sim = new CtSim { Fade = true };
            CtObj chest = sim.AddTracked("Chest1(Clone)");
            sim.MarkUsed(chest);
            sim.Tick(toggleAt);
            sim.Fade = false;
            sim.RefreshAll();
            sim.Tick(1.1f);
            sim.Tick(0.02f);
            Expect(CtSim.Hidden(chest), $"toggled at {toggleAt} s: not hidden, renderer {chest.Renderers[0].Enabled} fade {chest.Renderers[0].Fade}");
            sim.Fade = true;
            sim.RefreshAll();
            sim.Tick(0.02f);
            Expect(CtSim.Faded(chest), $"toggled back at {toggleAt} s: not faded");
            Expect(!sim.IsOutlined(chest), $"toggled at {toggleAt} s: outline back on a gone chest");
        }
    }

    private static void ChestEdgeDisable()
    {
        var sim = new CtSim();
        CtObj gone = sim.AddTracked("Chest1(Clone)", hl => hl.IsOn = false);
        CtObj waiting = sim.AddTracked("Barrel1(Clone)");
        CtShop shop = sim.AddMultiShop(2);
        sim.MarkUsed(gone);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        sim.MarkUsed(waiting);
        sim.Tick(0.3f);
        sim.Disable();
        foreach (CtObj o in new[] { gone, waiting, shop.Stand }.Concat(shop.Terminals))
        {
            Expect(CtSim.Shown(o), $"{o.Name} not put back");
            Expect(o.Highlight == null || !o.Highlight.IsOn, $"{o.Name} kept its outline");
        }

        sim.Tick(5f);
        foreach (CtObj o in new[] { gone, waiting, shop.Stand }.Concat(shop.Terminals))
        {
            Expect(CtSim.Shown(o), $"{o.Name} went after Disable");
        }
    }

    private static void ChestEdgeDestroyedKeys()
    {
        var sim = new CtSim();
        CtObj chest = sim.AddTracked("Chest1(Clone)");
        CtShop shop = sim.AddMultiShop(2);
        sim.MarkUsed(chest);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        sim.DestroyObject(chest);
        sim.DestroyObject(shop.Stand);
        sim.Fade = false;
        sim.RefreshAll();
        sim.Colour = 3;
        sim.RefreshAll();
        sim.MarkAvailable(chest);
        sim.ReportStage();
        Expect(!sim.Tracked.Keys.Any(h => !h.Alive), "a destroyed Highlight stayed tracked");
        Expect(!sim.Stands.Keys.Any(s => !s.Alive), "a destroyed stand stayed");
    }

    private static void ChestEdgeStandHideOffOn()
    {
        var sim = new CtSim();
        CtShop shop = sim.AddMultiShop(3);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        Expect(!CtSim.Shown(shop.Stand), "stand not gone to start with");
        sim.HideOn.Remove(ChestKind.Shop);
        sim.RefreshAll();
        Expect(CtSim.Shown(shop.Stand) && shop.Terminals.All(CtSim.Shown), "hide off didn't bring the shop back");
        sim.HideOn.Add(ChestKind.Shop);
        sim.RefreshAll();
        sim.Tick(1.1f);
        Expect(shop.Terminals.All(t => !CtSim.Shown(t)), "terminals didn't go again");
        Expect(!CtSim.Shown(shop.Stand), "terminals went again but the stand stayed in view");
    }

    private static void ChestEdgeStandKeepDrifterOff()
    {
        var sim = new CtSim { KeepAsDrifter = true, LocalIsDrifter = true };
        CtShop shop = sim.AddMultiShop(3);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        Expect(CtSim.Shown(shop.Stand) && shop.Terminals.All(CtSim.Shown), "went as Drifter with Keep on");
        sim.KeepAsDrifter = false;
        sim.RefreshAll();
        sim.Tick(1.1f);
        Expect(shop.Terminals.All(t => !CtSim.Shown(t)), "terminals didn't go with Keep off");
        Expect(!CtSim.Shown(shop.Stand), "terminals went with Keep off but the stand stayed in view");
    }

    private static void ChestEdgeStandFadeMidDelay()
    {
        var sim = new CtSim { Fade = true };
        CtShop shop = sim.AddMultiShop(3);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(0.5f);
        sim.Fade = false;
        sim.RefreshAll();
        sim.Tick(0.6f);
        sim.Tick(0.02f);
        Expect(shop.Terminals.All(CtSim.Hidden), "terminals not hidden after Fade went off");
        CtRend r = shop.Stand.Renderers[0];
        Expect(CtSim.Hidden(shop.Stand), $"terminals hidden but the stand is faded instead, renderer on {r.Enabled} fade {r.Fade}");
    }

    // DroneVendorMultiShopController.OnPurchase closes every terminal, ResetDroneVendor reopens the unbought ones
    // when MultiShopCardUtils set doCloseOnTerminalPurchase false for the bought one
    private static void ChestEdgeDroneStandExecutiveCard()
    {
        Expect(!typeof(DroneVendorMultiShopController).IsSubclassOf(typeof(MultiShopController)),
            "DroneVendorMultiShopController is a MultiShopController, so ShowShopStands would see it");
        Expect(typeof(DroneVendorMultiShopController).GetMethod("ResetDroneVendor") != null, "DroneVendorMultiShopController.ResetDroneVendor");
        Expect(typeof(RoR2.Items.MultiShopCardUtils) != null, "MultiShopCardUtils");

        var sim = new CtSim();
        CtShop vendor = sim.AddDroneVendor(3);
        foreach (CtObj t in vendor.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        Expect(!CtSim.Shown(vendor.Stand), "drone stand didn't go when bought");

        sim.Tick(2f);
        foreach (CtObj t in vendor.Terminals.Skip(1))
        {
            sim.OnSyncAvailable(t, true);
        }

        sim.Tick(0.02f);
        Expect(vendor.Terminals.Skip(1).All(CtSim.Shown), "reopened terminals stayed hidden");
        Expect(CtSim.Shown(vendor.Stand), "two drone terminals are buyable again but their stand stays hidden");
    }

    // MultiShopController.CreateTerminals parents each terminal under a terminalPositions transform of the stand,
    // so with no ModelLocator on the stand its fade covers the terminals' renderers too
    private static void ChestEdgeStandRestoreOverTerminal()
    {
        var sim = new CtSim { Fade = true };
        CtShop shop = sim.AddMultiShop(3, standHasModelLocator: false);
        foreach (CtObj t in shop.Terminals)
        {
            sim.OnSyncAvailable(t, false);
        }

        sim.Tick(1.1f);
        sim.HideOn.Remove(ChestKind.Shop);
        sim.RefreshAll();
        var wrong = shop.Terminals.Where(t => !CtSim.Shown(t)).Select(t => $"{t.Name} fade {t.Renderers[0].Fade}").ToList();
        Expect(wrong.Count == 0, $"hiding off but still faded: {string.Join(", ", wrong)}");
    }

    // IsDrifter caches a missing DrifterBody for good, BodyCatalog.FindBodyIndex is a plain dictionary lookup that
    // returns None until the catalog is built
    private static void ChestEdgeDrifterBeforeCatalog()
    {
        var sim = new CtSim { CatalogReady = false };
        sim.AddTracked("Chest1(Clone)");
        sim.RefreshAll();

        sim.CatalogReady = true;
        sim.KeepAsDrifter = true;
        sim.LocalIsDrifter = true;
        CtObj chest = sim.AddTracked("Chest1 (2)");
        sim.MarkUsed(chest);
        sim.Tick(1.1f);
        Expect(CtSim.Shown(chest), "Drifter with Keep on, but the chest went because Drifter was looked up before the catalog");
    }

    // InteractionDriver.OnPreRenderOutlineHighlight draws what you look at in Highlight.GetColor(), which is the custom
    // colour this feature sets, so the looked-at outline is drawn the same as every other one and the setting says so
    private static void ChestEdgeLookAtColour()
    {
        MethodInfo driver = typeof(InteractionDriver).GetMethod("OnPreRenderOutlineHighlight", ChestAny)!;
        MethodInfo getColor = typeof(Highlight).GetMethod("GetColor")!;
        byte[] il = driver.GetMethodBody()!.GetILAsByteArray()!;
        bool callsGetColor = Enumerable.Range(0, il.Length - 4)
            .Any(i => (il[i] == 0x28 || il[i] == 0x6F) && BitConverter.ToInt32(il, i + 1) == getColor.MetadataToken);
        Expect(callsGetColor, "InteractionDriver no longer reads Highlight.GetColor, recheck this test");

        var h = (Highlight)RuntimeHelpers.GetUninitializedObject(typeof(Highlight));
        Color outline = new Color32(255, 255, 1, 255);
        h.highlightColor = Highlight.HighlightColor.custom;
        h.CustomColor = outline;
        Color lookedAt = h.GetColor();
        Expect(lookedAt == outline, "the looked-at outline no longer takes the Outline Colour, recheck its description");

        string root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "SourceRoot").Value!;
        string source = File.ReadAllText(System.IO.Path.Combine(root, "ChestTweaks", "ChestTweaksSettings.cs"));
        Expect(source.Contains("the one on what you look at included", StringComparison.Ordinal),
            "the Outline Colour description no longer says what you look at takes the colour too");
        Expect(!source.Contains("still shows on what you look at", StringComparison.Ordinal),
            "the Outline Colour description still claims the game's own outline shows on what you look at");
    }

    private sealed class CtRend
    {
        public bool Enabled = true;
        public float Fade = 1f;
        public bool Alive = true;
    }

    private sealed class CtCol
    {
        public bool Enabled = true;
        public bool Alive = true;
    }

    private sealed class CtHl
    {
        public CtHl(CtObj go) => Go = go;

        public CtObj Go { get; }
        public bool IsOn;
        public int Mode;
        public int Custom = 99;
        public bool TargetValid = true;
        public bool HasModels = true;
        public bool Alive = true;
    }

    private sealed class CtObj
    {
        public CtObj(string name, CtObj? parent)
        {
            Name = name;
            Parent = parent;
            parent?.Children.Add(this);
        }

        public string Name { get; }
        public CtObj? Parent { get; }
        public List<CtObj> Children { get; } = new();
        public List<CtRend> Renderers { get; } = new() { new CtRend() };
        public List<CtCol> Colliders { get; } = new() { new CtCol() };
        public bool ModelLocator = true;
        public bool Alive = true;
        public bool? Available;
        public CtHl? Highlight;

        public IEnumerable<CtObj> Subtree() => new[] { this }.Concat(Children.SelectMany(c => c.Subtree()));
    }

    private sealed class CtShop
    {
        public CtShop(CtObj stand, bool multi) => (Stand, Multi) = (stand, multi);

        public CtObj Stand { get; }
        public bool Multi { get; }
        public List<CtObj> Terminals { get; } = new();
    }

    private sealed class CtTracked
    {
        public CtTracked(ChestKind kind, CtHl hl) => (Kind, WasOn, WasMode, WasCustom) = (kind, hl.IsOn, hl.Mode, hl.Custom);

        public ChestKind Kind { get; }
        public bool WasOn { get; }
        public int WasMode { get; }
        public int WasCustom { get; }
        public bool Used;
        public bool Outlined;
        public CtHider? Hider;
    }

    // UsedHider as written
    private sealed class CtHider
    {
        private readonly CtSim sim;
        private float goneAt;
        private Action? restore;
        private Action? onGone;

        public CtHider(CtSim sim, CtObj go) => (this.sim, Go) = (sim, go);

        public CtObj Go { get; }
        public bool Enabled = true;
        public bool Destroyed;
        public bool Fading { get; private set; }
        public bool Gone => restore != null;

        public void Begin(float delay, bool fade, Action? whenGone)
        {
            Restore();
            goneAt = sim.Time + Math.Max(0f, delay);
            Fading = fade;
            onGone = whenGone;
            Enabled = true;
        }

        public void Restore()
        {
            Action? undo = restore;
            restore = null;
            undo?.Invoke();
        }

        public void Skip(CtObj child)
        {
            if (!skip.Contains(child))
            {
                skip.Add(child);
            }
        }

        public void Switch(bool fade)
        {
            if (Fading == fade)
            {
                return;
            }

            Fading = fade;
            if (Gone)
            {
                Restore();
                restore = Apply();
            }
        }

        public void Update()
        {
            if (sim.Time < goneAt)
            {
                return;
            }

            Enabled = false;
            restore = Apply();
            onGone?.Invoke();
        }

        public void OnDestroy() => Restore();

        private readonly List<CtObj> skip = new();

        // Own<T> leaves out anything under a skipped child
        private IEnumerable<CtObj> Own(CtObj root) => new[] { root }.Concat(root.Children.Where(c => !skip.Contains(c)).SelectMany(Own));

        private IEnumerable<CtRend> Renderers(CtObj target) => target.ModelLocator ? target.Renderers : Own(target).SelectMany(o => o.Renderers);

        private Action Apply()
        {
            var undo = new List<Action>();
            foreach (CtRend r in Renderers(Go).Where(r => r.Alive && r.Enabled))
            {
                if (!Fading)
                {
                    r.Enabled = false;
                    undo.Add(() => { if (r.Alive) r.Enabled = true; });
                    continue;
                }

                float original = r.Fade;
                r.Fade = 0.25f;
                undo.Add(() => { if (r.Alive) r.Fade = original; });
            }

            foreach (CtCol c in Own(Go).SelectMany(o => o.Colliders).Where(c => c.Alive && c.Enabled))
            {
                c.Enabled = false;
                undo.Add(() => { if (c.Alive) c.Enabled = true; });
            }

            return () => Undo(undo);
        }

        private static void Undo(List<Action> undo)
        {
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                undo[i]();
            }
        }
    }

    // ChestTweaksFeature's state logic as written, the game side is a list of objects and a frame tick
    // Hiders update in the order they were added, the order Unity uses isn't specified
    private sealed class CtSim
    {
        public readonly HashSet<ChestKind> OutlineOn = new(ChestKinds.All.Where(k => k != ChestKind.Shrine));
        public readonly HashSet<ChestKind> HideOn = new(ChestKinds.All.Where(ChestKinds.CanHide));
        public readonly Dictionary<CtHl, CtTracked> Tracked = new();
        public readonly Dictionary<CtObj, (CtHider Hider, List<CtObj> Terminals)> Stands = new();
        public bool Fade = true;
        public float Delay = 1f;
        public bool KeepAsDrifter;
        public bool LocalIsDrifter;
        public bool CatalogReady = true;
        public int Colour = 1;
        public float Time;

        private readonly List<CtHider> hiders = new();
        private readonly List<CtHider> pendingDestroy = new();
        private readonly List<CtShop> shops = new();
        private int drifter = -1;
        private bool drifterMissing;

        public static bool Shown(CtObj o) => o.Renderers.All(r => r.Enabled && r.Fade == 1f) && o.Colliders.All(c => c.Enabled);

        public static bool Hidden(CtObj o) => o.Renderers.All(r => !r.Enabled) && o.Colliders.All(c => !c.Enabled);

        public static bool Faded(CtObj o) => o.Renderers.All(r => r.Enabled && r.Fade == 0.25f) && o.Colliders.All(c => !c.Enabled);

        public bool IsOutlined(CtObj o) => o.Highlight != null && o.Highlight.IsOn && o.Highlight.Mode == 3 && o.Highlight.Custom == Colour;

        public int LiveHiders(CtObj o) => hiders.Count(h => h.Go == o && !h.Destroyed && !pendingDestroy.Contains(h));

        public CtObj AddTracked(string name, Action<CtHl>? setUp = null, CtObj? parent = null)
        {
            var o = new CtObj(name, parent);
            o.Highlight = new CtHl(o);
            setUp?.Invoke(o.Highlight);
            Track(o.Highlight);
            return o;
        }

        public CtShop AddMultiShop(int count, bool standHasModelLocator = true)
        {
            var stand = new CtObj("TripleShop(Clone)", null) { ModelLocator = standHasModelLocator };
            var shop = new CtShop(stand, true);
            for (int i = 0; i < count; i++)
            {
                var position = new CtObj($"Terminal{i + 1}", stand);
                position.Renderers.Clear();
                position.Colliders.Clear();
                CtObj terminal = AddTracked("MultiShopTerminal(Clone)", null, position);
                terminal.Available = true;
                shop.Terminals.Add(terminal);
            }

            shops.Add(shop);
            return shop;
        }

        public CtShop AddDroneVendor(int count)
        {
            var shop = new CtShop(new CtObj("DroneVendorMultiShop(Clone)", null), false);
            for (int i = 0; i < count; i++)
            {
                CtObj terminal = AddTracked("TripleDroneShopTerminal(Clone)");
                terminal.Available = true;
                shop.Terminals.Add(terminal);
            }

            shops.Add(shop);
            return shop;
        }

        public void Tick(float seconds)
        {
            float step = 0.02f;
            for (float t = 0; t < seconds - 1e-4f; t += step)
            {
                Time += step;
                foreach (CtHider h in hiders.ToList())
                {
                    if (h.Enabled && !h.Destroyed)
                    {
                        h.Update();
                    }
                }

                EndOfFrame();
            }
        }

        public void DestroyObject(CtObj o)
        {
            foreach (CtObj part in o.Subtree())
            {
                part.Alive = false;
                part.Renderers.ForEach(r => r.Alive = false);
                part.Colliders.ForEach(c => c.Alive = false);
                if (part.Highlight != null)
                {
                    part.Highlight.Alive = false;
                }

                foreach (CtHider h in hiders.Where(h => h.Go == part && !h.Destroyed))
                {
                    h.Destroyed = true;
                    h.OnDestroy();
                }
            }
        }

        // PurchaseInteraction.OnSyncAvailable with the feature's hook after orig
        public void OnSyncAvailable(CtObj self, bool newAvailable)
        {
            self.Available = newAvailable;
            CtHl? hl = self.Highlight;
            if (hl == null || !hl.Alive || !Tracked.TryGetValue(hl, out CtTracked? t) || t.Kind != ChestKind.Shop)
            {
                return;
            }

            if (newAvailable)
            {
                MarkAvailable(self);
            }
            else
            {
                MarkUsed(self);
            }
        }

        public void MarkUsed(CtObj target)
        {
            if (!target.Alive || !Find(target, out CtHl hl, out CtTracked t) || t.Used)
            {
                return;
            }

            t.Used = true;
            Refresh(hl, t);
            if (t.Kind == ChestKind.Shop)
            {
                HideShopStand(target);
            }
        }

        public void MarkAvailable(CtObj target)
        {
            if (!target.Alive || !Find(target, out CtHl hl, out CtTracked t) || !t.Used)
            {
                return;
            }

            t.Used = false;
            Refresh(hl, t);
            if (t.Kind == ChestKind.Shop)
            {
                ShowShopStands(target);
            }
        }

        public void RefreshAll()
        {
            foreach (KeyValuePair<CtHl, CtTracked> pair in Tracked.ToList())
            {
                Refresh(pair.Key, pair.Value);
            }

            bool hideShops = HideUsed(ChestKind.Shop);
            foreach (KeyValuePair<CtObj, (CtHider Hider, List<CtObj> Terminals)> stand in Stands.ToList())
            {
                if (!hideShops || !stand.Key.Alive)
                {
                    Remove(stand.Value.Hider);
                    Stands.Remove(stand.Key);
                }
                else
                {
                    stand.Value.Hider.Switch(Fade);
                }
            }

            foreach (KeyValuePair<CtHl, CtTracked> pair in Tracked.ToList())
            {
                if (pair.Key.Alive && pair.Value.Used && pair.Value.Kind == ChestKind.Shop)
                {
                    HideShopStand(pair.Key.Go);
                }
            }
        }

        public void Disable()
        {
            foreach (KeyValuePair<CtHl, CtTracked> pair in Tracked)
            {
                RemoveHider(pair.Value);
                if (pair.Key.Alive)
                {
                    SetOutline(pair.Key, pair.Value, false);
                }
            }

            foreach ((CtHider Hider, List<CtObj> Terminals) stand in Stands.Values)
            {
                Remove(stand.Hider);
            }

            Tracked.Clear();
            Stands.Clear();
        }

        public void ReportStage()
        {
            foreach (CtHl gone in Tracked.Keys.Where(h => !h.Alive).ToList())
            {
                Tracked.Remove(gone);
            }

            foreach (CtObj gone in Stands.Keys.Where(s => !s.Alive).ToList())
            {
                Stands.Remove(gone);
            }
        }

        private void Track(CtHl hl)
        {
            if (!hl.Alive)
            {
                return;
            }

            if (!Tracked.TryGetValue(hl, out CtTracked? t))
            {
                var ancestors = new List<string>();
                for (CtObj? p = hl.Go.Parent; p != null; p = p.Parent)
                {
                    ancestors.Add(p.Name);
                }

                ChestKind? kind = ChestKinds.Of(hl.Go.Name, ancestors);
                if (kind == null)
                {
                    return;
                }

                t = new CtTracked(kind.Value, hl);
                Tracked[hl] = t;
            }

            Refresh(hl, t);
        }

        private bool Find(CtObj target, out CtHl hl, out CtTracked t)
        {
            hl = target.Highlight!;
            if (hl != null && hl.Alive && Tracked.TryGetValue(hl, out t!))
            {
                return true;
            }

            t = null!;
            return false;
        }

        private bool HideUsed(ChestKind kind) => ChestRules.ShouldHide(ChestKinds.CanHide(kind), HideOn.Contains(kind), KeepAsDrifter, IsDrifter());

        private void Refresh(CtHl hl, CtTracked t)
        {
            if (!hl.Alive)
            {
                return;
            }

            if (t.Used && HideUsed(t.Kind))
            {
                if (t.Hider == null)
                {
                    t.Hider = AddHider(hl.Go);
                    t.Hider.Begin(Delay, Fade, () => Refresh(hl, t));
                }
                else
                {
                    t.Hider.Switch(Fade);
                }
            }
            else
            {
                RemoveHider(t);
            }

            bool gone = t.Hider != null && t.Hider.Gone;
            SetOutline(hl, t, ChestRules.Outlined(OutlineOn.Contains(t.Kind), gone));
        }

        private void SetOutline(CtHl hl, CtTracked t, bool on)
        {
            if (on)
            {
                if (!hl.TargetValid)
                {
                    if (!hl.HasModels)
                    {
                        return;
                    }

                    hl.TargetValid = true;
                }

                hl.IsOn = true;
                hl.Mode = 3;
                hl.Custom = Colour;
                t.Outlined = true;
            }
            else if (t.Outlined)
            {
                hl.IsOn = t.WasOn;
                hl.Mode = t.WasMode;
                hl.Custom = t.WasCustom;
                t.Outlined = false;
            }
        }

        private CtHider AddHider(CtObj go)
        {
            var h = new CtHider(this, go);
            hiders.Add(h);
            return h;
        }

        private void RemoveHider(CtTracked t)
        {
            if (t.Hider != null)
            {
                Remove(t.Hider);
                t.Hider = null;
            }
        }

        private void Remove(CtHider hider)
        {
            if (hider.Destroyed)
            {
                return;
            }

            hider.Restore();
            hider.Enabled = false;
            pendingDestroy.Add(hider);
        }

        private void EndOfFrame()
        {
            foreach (CtHider h in pendingDestroy.Where(h => !h.Destroyed))
            {
                h.Destroyed = true;
                h.OnDestroy();
                hiders.Remove(h);
            }

            pendingDestroy.Clear();
        }

        private void HideShopStand(CtObj terminal)
        {
            if (!HideUsed(ChestKind.Shop))
            {
                return;
            }

            CtObj? stand = null;
            List<CtObj> terminals = new() { terminal };
            foreach (CtShop shop in shops.Where(s => s.Multi && s.Stand.Alive))
            {
                if (shop.Terminals.Contains(terminal))
                {
                    if (shop.Terminals.All(Unavailable))
                    {
                        stand = shop.Stand;
                        terminals = shop.Terminals.ToList();
                    }

                    break;
                }
            }

            // Stands in for the nearest DroneVendorMultiShopController with a ModelLocator within 4.3 m
            if (stand == null && ChestKinds.BaseName(terminal.Name) == "TripleDroneShopTerminal")
            {
                stand = shops.FirstOrDefault(s => !s.Multi && s.Stand.Alive && s.Stand.ModelLocator && s.Terminals.Contains(terminal))?.Stand;
            }

            if (stand == null)
            {
                return;
            }

            if (!Stands.TryGetValue(stand, out (CtHider Hider, List<CtObj> Terminals) known))
            {
                known = (AddHider(stand), new List<CtObj>());
                known.Hider.Begin(Delay, Fade, null);
                Stands[stand] = known;
            }

            foreach (CtObj t in terminals.Where(t => t.Alive && !known.Terminals.Contains(t)))
            {
                known.Terminals.Add(t);
                known.Hider.Skip(t);
            }
        }

        private static bool Unavailable(CtObj terminal) => !terminal.Alive || terminal.Available != true;

        private void ShowShopStands(CtObj terminal)
        {
            foreach (KeyValuePair<CtObj, (CtHider Hider, List<CtObj> Terminals)> stand in Stands.ToList())
            {
                if (!stand.Key.Alive || stand.Value.Terminals.Contains(terminal))
                {
                    Remove(stand.Value.Hider);
                    Stands.Remove(stand.Key);
                }
            }
        }

        private bool IsDrifter()
        {
            if (drifter == -1 && !drifterMissing)
            {
                drifter = CatalogReady ? 42 : -1;
                if (drifter == -1 && CatalogReady)
                {
                    drifterMissing = true;
                }
            }

            return drifter != -1 && LocalIsDrifter;
        }
    }
}
