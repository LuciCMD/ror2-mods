using System;
using System.Collections.Generic;
using System.Reflection;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;

// Ally Card Info for allies that aren't drones, at its edges
internal static partial class Program
{
    private static void RunAllyOthersEdge()
    {
        Test("Ally others edge: Zoea's Void Jailer reads as Newly Hatched Zoea", AllyOthersZoeaJailer);
        Test("Ally others edge: Zoea's Void Reaver reads as Newly Hatched Zoea", AllyOthersZoeaNullifier);
        Test("Ally others edge: an altar's ghost is still the altar's once placing it used the equipment up", AllyOthersAltarConsumed);
        Test("Ally others edge: a ghost with both markers is the ghost, whatever body", AllyOthersGhostAndGoobo);
        Test("Ally others edge: markers work with no or an empty body name", AllyOthersNoBodyName);
        Test("Ally others edge: body names match exactly, no case folding or padding", AllyOthersExactNames);
        Test("Ally others edge: every table body leads somewhere and every item source has a body", AllyOthersTableComplete);
        Test("Ally others edge: token text with empty, null and self-named tokens", AllyOthersTokenTextEdges);
        Test("Ally others edge: token text before any language loads is empty, no throw", AllyOthersTokenTextNoLanguage);
        Test("Ally others edge: an unknown body is logged once however often its card refreshes", AllyOthersUnknownOnce);
        Test("Ally others edge: a card whose new ally can't be described stops showing the last ally's text", AllyOthersDescribeThrows);
    }

    private static HashSet<string> GameBodies()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        Type[] lists =
        {
            typeof(RoR2Content.BodyPrefabs), typeof(DLC1Content.BodyPrefabs), typeof(DLC2Content.BodyPrefabs),
            typeof(DLC3Content.BodyPrefabs), typeof(DLC4Content.BodyPrefabs), typeof(CU8Content.BodyPrefabs),
        };
        foreach (Type type in lists)
        {
            foreach (FieldInfo field in type.GetFields())
            {
                known.Add(field.Name);
            }
        }

        return known;
    }

    // VoidMegaCrabItemBehavior picks cscVoidJailerAlly 15, cscNullifierAlly 15 and cscVoidMegaCrabAlly 1,
    // so the Devastator is only 1 Zoea ally in 31
    private static void AllyOthersZoeaJailer()
    {
        Expect(GameBodies().Contains("VoidJailerAllyBody"), "the Jailer ally is one of the game's bodies");
        Expect(AllyKinds.Of("VoidJailerAllyBody", false, false, false) == AllySource.VoidMegaCrabItem,
            "got " + AllyKinds.Of("VoidJailerAllyBody", false, false, false));
    }

    private static void AllyOthersZoeaNullifier()
    {
        Expect(GameBodies().Contains("NullifierAllyBody"), "the Reaver ally is one of the game's bodies");
        Expect(AllyKinds.Of("NullifierAllyBody", false, false, false) == AllySource.VoidMegaCrabItem,
            "got " + AllyKinds.Of("NullifierAllyBody", false, false, false));
    }

    // EquipmentSlot.FireSpawnAltarPlacement transforms the equipment into EquipmentIndex.None, or an Enigma roll,
    // and SpawnAltarZoneController only raises its ghosts when the altar ends, so by then the owner holds no altar
    private static void AllyOthersAltarConsumed()
    {
        AllySource source = AllyKinds.Of("BeetleBody", true, false, false);
        Expect(source == AllySource.SpawnAltar, "an altar ghost, owner without the Mask or the altar, got " + source);
    }

    private static void AllyOthersGhostAndGoobo()
    {
        // A Mask ghost of a Goobo clone copies the clone's inventory, marker and all
        Expect(AllyKinds.Of("EngiBody", true, true, true) == AllySource.GhostOnKill, "ghost before Goobo");
        Expect(AllyKinds.Of("TitanGoldBody", true, true, false) == AllySource.GhostOnKill, "ghost before the table");
        Expect(AllyKinds.Of("DevotedLemurianBody", false, false, true) == AllySource.GummyClone, "Goobo before the table");
    }

    private static void AllyOthersNoBodyName()
    {
        Expect(AllyKinds.Of(null, true, true, false) == AllySource.GhostOnKill, "a ghost with no body name");
        Expect(AllyKinds.Of(null, false, false, true) == AllySource.GummyClone, "a clone with no body name");
        Expect(AllyKinds.Of(string.Empty, false, true, false) == AllySource.None, "empty name, no markers");
    }

    private static void AllyOthersExactNames()
    {
        Expect(AllyKinds.Of("beetleguardallybody", false, false, false) == AllySource.None, "lower case");
        Expect(AllyKinds.Of("BeetleGuardAllyBody ", false, false, false) == AllySource.None, "trailing space");
        Expect(AllyKinds.Of("BeetleGuardBody", false, false, false) == AllySource.None, "the enemy Beetle Guard");
    }

    private static void AllyOthersTableComplete()
    {
        var reached = new HashSet<AllySource>();
        foreach (string body in AllyKinds.Bodies)
        {
            AllySource source = AllyKinds.Of(body, false, false, false);
            Expect(source != AllySource.None, body + " leads to a source");
            reached.Add(source);
        }

        foreach (AllySource source in Enum.GetValues(typeof(AllySource)))
        {
            bool marker = source == AllySource.None || source == AllySource.GhostOnKill || source == AllySource.SpawnAltar
                || source == AllySource.GummyClone;
            Expect(marker || reached.Contains(source), source + " is reached by some body");
        }
    }

    private static void AllyOthersTokenTextEdges()
    {
        WithLanguage(
            new Dictionary<string, string>
            {
                ["ITEM_X_PICKUP"] = "Short",
                ["ITEM_SELF"] = "ITEM_SELF",
                ["ITEM_EMPTY_DESC"] = string.Empty,
            },
            () =>
            {
                Equal("Short", ItemStatText.TokenText(null, "ITEM_X_PICKUP", true));
                Equal("Short", ItemStatText.TokenText(string.Empty, "ITEM_X_PICKUP", true));
                Equal("Short", ItemStatText.TokenText("ITEM_EMPTY_DESC", "ITEM_X_PICKUP", true));
                Equal(string.Empty, ItemStatText.TokenText("ITEM_SELF", null, true));
                Equal(string.Empty, ItemStatText.TokenText(string.Empty, string.Empty, false));
                Equal(string.Empty, ItemStatText.TokenText(null, null, false));
            });
    }

    private static void AllyOthersTokenTextNoLanguage()
    {
        FieldInfo field = typeof(Language).GetField("<currentLanguage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? old = field.GetValue(null);
        field.SetValue(null, null);
        try
        {
            Equal(string.Empty, ItemStatText.TokenText("ITEM_X_DESC", "ITEM_X_PICKUP", true));
        }
        finally
        {
            field.SetValue(null, old);
        }
    }

    private static void AllyOthersUnknownOnce()
    {
        var sim = new AllySim();
        var card = new SimAllyCard { UnknownBody = "ModdedThingBody" };
        card.Ally(null, "Modded Thing");
        sim.UpdateInfo(card);
        sim.UpdateInfo(card);
        sim.CardInfo = false;
        sim.CardInfo = true;
        sim.Full = false;
        var other = new SimAllyCard { UnknownBody = "ModdedThingBody" };
        other.Ally(null, "Modded Thing");
        sim.UpdateInfo(other);
        Expect(sim.Log.Count == 1, "logged " + sim.Log.Count + " times");
        Expect(card.Tooltips.Count == 0 && other.Tooltips.Count == 0, "no tooltip for it");
    }

    // AllyCardManager reuses a card for whoever is next in the list, so a card that showed a Gunner Drone
    // can be handed an ally whose Describe throws, a modded body with a null name token for one
    private static void AllyOthersDescribeThrows()
    {
        var sim = new AllySim();
        SimAllyCard card = SeenCard(sim, "Gunner", "Gunner Drone");
        card.Ally("Modded", "Modded Thing");
        card.DescribeThrows = true;
        sim.UpdateInfo(card);
        Expect(sim.Failures == 1, "the failure was caught");
        SimTooltip t = sim.Ours(card)!;
        Expect(!t.Enabled || t.Body != AllySim.Text("Gunner", true),
            "hovering the modded ally's card shows [" + t.Title + "] [" + t.Body + "]");
    }
}
