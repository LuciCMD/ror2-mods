using System;
using System.Linq;
using JustBeBetter.ChestTweaks;

// Which kind an interactable is from its name and parents, and when it's outlined or hidden
internal static partial class Program
{
    private static void RunChestTweaks()
    {
        Test("Chest tweaks: spawned and placed copies keep their prefab's name", ChestBaseNames);
        Test("Chest tweaks: prefab names map to their kind", ChestPrefabKinds);
        Test("Chest tweaks: altars, plates and placed chests are found by name and parent", ChestPlacedKinds);
        Test("Chest tweaks: other objects aren't a kind", ChestUnknown);
        Test("Chest tweaks: only chests, barrels and shops can hide", ChestCanHide);
        Test("Chest tweaks: hiding and the outline follow the settings and Drifter", ChestRulesFollowSettings);
    }

    private static void ChestBaseNames()
    {
        Equal("Chest1", ChestKinds.BaseName("Chest1(Clone)"));
        Equal("Chest1", ChestKinds.BaseName("Chest1 (3)"));
        Equal("Chest1", ChestKinds.BaseName("Chest1 (12)(Clone)"));
        Equal("CategoryChest2Damage Variant", ChestKinds.BaseName("CategoryChest2Damage Variant(Clone)"));
        Equal("Shrine (Old)", ChestKinds.BaseName("Shrine (Old)"));
        Equal("Thing ()", ChestKinds.BaseName("Thing ()"));
    }

    private static void ChestPrefabKinds()
    {
        (string Name, ChestKind Kind)[] cases =
        {
            ("Chest1(Clone)", ChestKind.Chest),
            ("CasinoChest(Clone)", ChestKind.Chest),
            ("Chest1StealthedVariant(Clone)", ChestKind.StealthedChest),
            ("LockboxVoid(Clone)", ChestKind.Lockbox),
            ("LunarChest(Clone)", ChestKind.LunarPod),
            ("VoidChest(Clone)", ChestKind.VoidCradle),
            ("Barrel1(Clone)", ChestKind.Barrel),
            ("EquipmentBarrel(Clone)", ChestKind.EquipmentBarrel),
            ("ShopTerminal(Clone)", ChestKind.Shop),
            ("LunarShopTerminal", ChestKind.Shop),
            ("DroneScrapper(Clone)", ChestKind.Scrapper),
            ("DuplicatorWild(Clone)", ChestKind.Printer),
            ("MegaDroneBroken(Clone)", ChestKind.Drone),
            ("LemurianEgg(Clone)", ChestKind.Drone),
            ("Turret1Broken(Clone)", ChestKind.Turret),
            ("ShrineChanceSnowy Variant(Clone)", ChestKind.Shrine),
        };
        foreach ((string name, ChestKind kind) in cases)
        {
            Equal(kind.ToString(), ChestKinds.Of(name, Array.Empty<string>())?.ToString() ?? "null");
        }
    }

    private static void ChestPlacedKinds()
    {
        Equal("NewtAltar", ChestKinds.Of("NewtStatue (2)", new[] { "HOLDER: Newt Statues" })?.ToString() ?? "null");
        Equal("PressurePlate", ChestKinds.Of("GLPressurePlate (1)", new[] { "HOLDER: Secret Stuff" })?.ToString() ?? "null");
        Equal("Chest", ChestKinds.Of("mdlChestSpecial", new[] { "Group", "HOLDER: Preplaced Objects" })?.ToString() ?? "null");
        Equal("Chest", ChestKinds.Of("Anything", new[] { "Chests" })?.ToString() ?? "null");
        Equal("Chest", ChestKinds.Of("Anything", new[] { "Inner", "HOLDER: Preplaced Goodies" })?.ToString() ?? "null");
    }

    private static void ChestUnknown()
    {
        Expect(ChestKinds.Of("Teleporter1(Clone)", new[] { "HOLDER: Preplaced Objects" }) == null, "a teleporter counted as a kind");
        Expect(ChestKinds.Of("mdlRock", new[] { "HOLDER: Rocks" }) == null, "a rock counted as a kind");
        Expect(ChestKinds.Of("", Array.Empty<string>()) == null, "an empty name counted as a kind");
        Expect(ChestKinds.Of("GenericPickup(Clone)", null!) == null, "no parents threw or counted");
    }

    private static void ChestCanHide()
    {
        ChestKind[] hideable =
        {
            ChestKind.Chest, ChestKind.StealthedChest, ChestKind.LunarPod, ChestKind.VoidCradle,
            ChestKind.Barrel, ChestKind.EquipmentBarrel, ChestKind.Shop,
        };
        foreach (ChestKind kind in ChestKinds.All)
        {
            Expect(ChestKinds.CanHide(kind) == hideable.Contains(kind), $"{kind} hide was {ChestKinds.CanHide(kind)}");
        }
    }

    private static void ChestRulesFollowSettings()
    {
        Expect(ChestRules.ShouldHide(true, true, false, false), "a used chest wasn't hidden");
        Expect(!ChestRules.ShouldHide(false, true, false, false), "a printer was hidden");
        Expect(!ChestRules.ShouldHide(true, false, false, false), "hidden with its switch off");
        Expect(!ChestRules.ShouldHide(true, true, true, true), "hidden as Drifter with Keep on");
        Expect(ChestRules.ShouldHide(true, true, true, false), "Keep as Drifter stopped hiding for another survivor");
        Expect(ChestRules.ShouldHide(true, true, false, true), "Drifter kept them with Keep off");
        Expect(ChestRules.Outlined(true, false), "an outline didn't show");
        Expect(!ChestRules.Outlined(true, true), "a faded one kept its outline");
        Expect(!ChestRules.Outlined(false, false), "an outline showed with its switch off");
    }
}
