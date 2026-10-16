using System;
using System.Collections.Generic;
using System.Linq;

namespace JustBeBetter.ChestTweaks
{
    internal enum ChestKind
    {
        Chest,
        StealthedChest,
        Lockbox,
        LunarPod,
        VoidCradle,
        Barrel,
        EquipmentBarrel,
        Shop,
        Scrapper,
        Printer,
        Drone,
        Turret,
        NewtAltar,
        PressurePlate,
        Shrine,
    }

    // Which kind an interactable is, from its object's name, kept apart from the game so it can be tested
    // The prefab names are QoLChests 1.3.6's list, found by name here rather than by loading every prefab at startup
    internal static class ChestKinds
    {
        // Parents in a stage that hold placed chests, altars and plates, from QoLChests
        private static readonly string[] Holders = { "HOLDER: Preplaced", "HOLDER: Newt", "HOLDER: Secret" };

        // Every interactable with an outline in these is a chest
        private static readonly string[] ChestHolders = { "Chests", "HOLDER: Preplaced Goodies" };

        private static readonly Dictionary<string, ChestKind> Prefabs = Build();

        internal static IEnumerable<ChestKind> All => Enum.GetValues(typeof(ChestKind)).Cast<ChestKind>();

        // Only chests, barrels and shops get used up
        internal static bool CanHide(ChestKind kind) => kind switch
        {
            ChestKind.Chest or ChestKind.StealthedChest or ChestKind.LunarPod or ChestKind.VoidCradle
                or ChestKind.Barrel or ChestKind.EquipmentBarrel or ChestKind.Shop => true,
            _ => false,
        };

        // Ancestors run from the parent up to the scene root
        internal static ChestKind? Of(string objectName, IEnumerable<string> ancestors)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return null;
            }

            string name = BaseName(objectName);
            if (Prefabs.TryGetValue(name, out ChestKind kind))
            {
                return kind;
            }

            if (name.Contains("NewtStatue", StringComparison.OrdinalIgnoreCase))
            {
                return ChestKind.NewtAltar;
            }

            if (name.Contains("GLPressure", StringComparison.OrdinalIgnoreCase))
            {
                return ChestKind.PressurePlate;
            }

            foreach (string ancestor in ancestors ?? Enumerable.Empty<string>())
            {
                if (ChestHolders.Any(h => string.Equals(ancestor, h, StringComparison.OrdinalIgnoreCase)))
                {
                    return ChestKind.Chest;
                }

                if (Holders.Any(h => ancestor.Contains(h, StringComparison.OrdinalIgnoreCase))
                    && name.Contains("Chest", StringComparison.OrdinalIgnoreCase))
                {
                    return ChestKind.Chest;
                }
            }

            return null;
        }

        // A spawned copy is "Name(Clone)" and a placed duplicate is "Name (2)"
        internal static string BaseName(string objectName)
        {
            string name = objectName.Trim();
            while (true)
            {
                if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
                    continue;
                }

                int open = name.LastIndexOf(" (", StringComparison.Ordinal);
                int digits = name.Length - open - 3;
                if (open > 0 && digits > 0 && name.EndsWith(')')
                    && name.Substring(open + 2, digits).All(char.IsDigit))
                {
                    name = name.Substring(0, open).TrimEnd();
                    continue;
                }

                return name;
            }
        }

        private static Dictionary<string, ChestKind> Build()
        {
            var map = new Dictionary<string, ChestKind>(StringComparer.Ordinal);
            void Add(ChestKind kind, params string[] names)
            {
                foreach (string name in names)
                {
                    map[name] = kind;
                }
            }

            Add(
                ChestKind.Chest,
                "CasinoChest",
                "CategoryChestDamage",
                "CategoryChestHealing",
                "CategoryChestUtility",
                "Chest1",
                "Chest2",
                "GoldChest",
                "CategoryChest2Damage Variant",
                "CategoryChest2Healing Variant",
                "CategoryChest2Utility Variant",
                "HollowChestFree",
                "Chest1SadObjective",
                "TrialCategoryChestDamage",
                "TrialCategoryChest2Damage",
                "GoldChestSad",
                "NkuhanaChest");
            Add(ChestKind.LunarPod, "LunarChest");
            Add(ChestKind.VoidCradle, "VoidChest");
            Add(ChestKind.StealthedChest, "Chest1StealthedVariant");
            Add(ChestKind.Lockbox, "Lockbox", "LockboxVoid", "LockboxSoulObjective", "LockboxTrial");
            Add(ChestKind.Barrel, "Barrel1");
            Add(ChestKind.EquipmentBarrel, "EquipmentBarrel");
            Add(
                ChestKind.Shop,
                "LunarShopTerminal",
                "MultiShopEquipmentTerminal",
                "MultiShopLargeTerminal",
                "MultiShopTerminal",
                "ShopTerminal",
                "FreeChestTerminalShippingDrone",
                "TripleDroneShopTerminal",
                "TemporaryItemsShopTerminal",
                "TrialMultiShopTerminal",
                "TrialTripleShop",
                "TrialTripleShopLarge",
                "HauntedVendingMachine");
            Add(ChestKind.Scrapper, "Scrapper", "DroneScrapper");
            Add(ChestKind.Printer, "Duplicator", "DuplicatorLarge", "DuplicatorMilitary", "DuplicatorWild");
            Add(
                ChestKind.Drone,
                "Drone1Broken",
                "Drone2Broken",
                "EmergencyDroneBroken",
                "EquipmentDroneBroken",
                "FlameDroneBroken",
                "MegaDroneBroken",
                "MissileDroneBroken",
                "BombardmentDroneBroken",
                "CleanupDroneBroken",
                "CopycatDroneBroken",
                "HaulerDroneBroken",
                "JailerDroneBroken",
                "JunkDroneBroken",
                "RechargeDroneBroken",
                "DroneCombinerStation",
                "DroneAssemblyStation",
                "LemurianEgg");
            Add(ChestKind.Turret, "Turret1Broken");
            Add(
                ChestKind.Shrine,
                "ShrineBlood",
                "ShrineBloodSandy Variant",
                "ShrineBloodSnowy Variant",
                "ShrineBoss",
                "ShrineBossSandy Variant",
                "ShrineBossSnowy Variant",
                "ShrineChance",
                "ShrineChanceSandy Variant",
                "ShrineChanceSnowy Variant",
                "ShrineCleanse",
                "ShrineCleanseSandy Variant",
                "ShrineCleanseSnowy Variant",
                "ShrineCombat",
                "ShrineCombatSandy Variant",
                "ShrineCombatSnowy Variant",
                "ShrineGoldshoresAccess",
                "ShrineHealing",
                "ShrineRestack",
                "ShrineRestackSandy Variant",
                "ShrineRestackSnowy Variant",
                "ShrineHalcyonite",
                "ShrineColossusAccess",
                "ShrineRebirth",
                "ShrineCombatCollective",
                "ShrineCatastrophe",
                "ShrineCombatPoison",
                "BetterHallowingShrineBase",
                "HallowingShrineBase",
                "ShrineCompulsion",
                "ShrineDeath",
                "ShrineDread");
            return map;
        }
    }

    // When an interactable shows its outline and when a used one goes
    internal static class ChestRules
    {
        // A used one keeps its outline until it fades or hides, and never goes while that's turned off
        internal static bool ShouldHide(bool canHide, bool hideOn, bool keepAsDrifter, bool isDrifter)
            => canHide && hideOn && !(keepAsDrifter && isDrifter);

        internal static bool Outlined(bool outlineOn, bool gone) => outlineOn && !gone;
    }
}
