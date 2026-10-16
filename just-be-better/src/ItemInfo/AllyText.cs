using System;
using System.Collections.Generic;
using EntityStates.Engi.EngiWeapon;
using JustBeBetter.ItemStats;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace JustBeBetter.ItemInfo
{
    internal sealed class AllyTip
    {
        internal AllyTip(string name, Color colour, string body, string what)
        {
            Name = name;
            Colour = colour;
            Body = body;
            What = what;
        }

        // Shown when the card has no name label
        internal string Name { get; }

        internal Color Colour { get; }

        internal string Body { get; }

        // For the log
        internal string What { get; }
    }

    // What an ally does, a drone's own text or else what summoned it
    internal static class AllyText
    {
        // Engineer's two turret skills, found once by the state they place, as their names aren't in the game's code
        private static readonly Dictionary<Type, SkillDef?> TurretSkills = new Dictionary<Type, SkillDef?>();

        internal static AllyTip? Describe(CharacterMaster master, bool fullDescription, out string? noText)
        {
            noText = null;
            CharacterBody? body = master.GetBody();
            if (body == null || !body || master.playerCharacterMasterController)
            {
                return null;
            }

            DroneDef? drone = DroneOf(master, body);
            if (drone != null)
            {
                return Tip(Language.GetString(drone.nameToken), ColorCatalog.GetColor(drone.color), ItemStatText.DroneText(drone, fullDescription), drone.name);
            }

            Inventory? inventory = master.inventory ? master.inventory : null;
            CharacterMaster? owner = master.minionOwnership ? master.minionOwnership.ownerMaster : null;
            Inventory? ownerInventory = owner != null && owner && owner.inventory ? owner.inventory : null;
            EquipmentDef? altar = DLC4Content.Equipment.SpawnAltar;
            string bodyName = BodyCatalog.GetBodyName(body.bodyIndex) ?? string.Empty;
            bool ghost = Has(inventory, RoR2Content.Items.Ghost);
            bool gooboClone = Has(inventory, DLC1Content.Items.GummyCloneIdentifier);
            AllySource source = AllyKinds.Of(
                bodyName,
                ghost,
                Has(ownerInventory, RoR2Content.Items.GhostOnKill),
                gooboClone);

            if (source != AllySource.None)
            {
                AllyTip? tip = source switch
                {
                    AllySource.BeetleGland => Item(body, RoR2Content.Items.BeetleGland, fullDescription),
                    AllySource.Squid => Item(body, RoR2Content.Items.Squid, fullDescription),
                    AllySource.RoboBallBuddy => Item(body, RoR2Content.Items.RoboBallBuddy, fullDescription),
                    AllySource.MinorConstructOnKill => Item(body, DLC1Content.Items.MinorConstructOnKill, fullDescription),
                    AllySource.VoidMegaCrabItem => Item(body, DLC1Content.Items.VoidMegaCrabItem, fullDescription),
                    AllySource.PhysicsProjectile => Item(body, DLC3Content.Items.PhysicsProjectile, fullDescription),
                    AllySource.VengefulSpirit => Item(body, DLC4Content.Items.VengefulSpirit, fullDescription),
                    AllySource.TitanGoldDuringTP => Item(body, RoR2Content.Items.TitanGoldDuringTP, fullDescription),
                    AllySource.GhostOnKill => Item(body, RoR2Content.Items.GhostOnKill, fullDescription),
                    AllySource.SpawnAltar => Equipment(body, altar, fullDescription),
                    AllySource.GummyClone => Equipment(body, DLC1Content.Equipment.GummyClone, fullDescription),
                    AllySource.EngiTurret => Turret(body, typeof(PlaceTurret)),
                    AllySource.EngiWalkerTurret => Turret(body, typeof(PlaceWalkerTurret)),
                    AllySource.Devotion => Artifact(body, CU8Content.Artifacts.Devotion),
                    _ => null,
                };

                // Known but blank, such as a turret skill not found, which would otherwise go unseen
                if (tip == null)
                {
                    noText = $"{bodyName} ({source})";
                }

                return tip;
            }

            // A modded summon may say what it is in its subtitle, as bosses do
            string subtitle = body.subtitleNameToken;
            string text = string.IsNullOrEmpty(subtitle) || !AllyKinds.UseSubtitle(ghost, gooboClone)
                ? string.Empty
                : ItemStatText.TokenText(subtitle, null, true);
            if (text.Length == 0)
            {
                noText = bodyName;
                return null;
            }

            return Tip(body.GetDisplayName(), Color.clear, text, bodyName);
        }

        // Operator's drones lie broken in a body DroneCatalog doesn't list, while the master still names the real one
        private static DroneDef? DroneOf(CharacterMaster master, CharacterBody body)
        {
            DroneIndex index = DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex);
            DroneDef? drone = index != DroneIndex.None ? DroneCatalog.GetDroneDef(index) : DroneCatalog.FindDroneDefFromBody(master.bodyPrefab);
            return drone != null && drone ? drone : null;
        }

        // Total, not effective, as a disabled inventory or item says nothing about what an ally is
        private static bool Has(Inventory? inventory, ItemDef? item)
        {
            return inventory != null && item != null && inventory.GetItemCountTotal(item.itemIndex) > 0;
        }

        private static AllyTip? Item(CharacterBody body, ItemDef? item, bool fullDescription)
        {
            if (item == null || !item)
            {
                return null;
            }

            return Tip(body.GetDisplayName(), PickupColour(PickupCatalog.FindPickupIndex(item.itemIndex)),
                ItemStatText.TokenText(item.descriptionToken, item.pickupToken, fullDescription), item.name);
        }

        private static AllyTip? Equipment(CharacterBody body, EquipmentDef? equipment, bool fullDescription)
        {
            if (equipment == null || !equipment)
            {
                return null;
            }

            return Tip(body.GetDisplayName(), PickupColour(PickupCatalog.FindPickupIndex(equipment.equipmentIndex)),
                ItemStatText.TokenText(equipment.descriptionToken, equipment.pickupToken, fullDescription), equipment.name);
        }

        // Artifacts have one text, the same either way
        private static AllyTip? Artifact(CharacterBody body, ArtifactDef? artifact)
        {
            if (artifact == null || !artifact)
            {
                return null;
            }

            return Tip(body.GetDisplayName(), PickupColour(PickupCatalog.FindPickupIndex(artifact.artifactIndex)),
                ItemStatText.TokenText(artifact.descriptionToken, null, true), artifact.cachedName);
        }

        // Skills have one text too, in Engineer's colour
        private static AllyTip? Turret(CharacterBody body, Type placeState)
        {
            if (!TurretSkills.TryGetValue(placeState, out SkillDef? skill))
            {
                skill = FindTurretSkill(placeState);
                TurretSkills[placeState] = skill;
            }

            if (skill == null || !skill)
            {
                return null;
            }

            GameObject engineer = BodyCatalog.FindBodyPrefab("EngiBody");
            Color colour = engineer && engineer.TryGetComponent(out CharacterBody engineerBody) ? engineerBody.bodyColor : Color.clear;
            return Tip(body.GetDisplayName(), colour, ItemStatText.TokenText(skill.skillDescriptionToken, null, true), skill.skillName);
        }

        private static SkillDef? FindTurretSkill(Type placeState)
        {
            GameObject engineer = BodyCatalog.FindBodyPrefab("EngiBody");
            SkillLocator? skills = engineer ? engineer.GetComponent<SkillLocator>() : null;
            SkillFamily? family = skills != null && skills && skills.special ? skills.special.skillFamily : null;
            if (family == null || !family)
            {
                return null;
            }

            foreach (SkillFamily.Variant variant in family.variants)
            {
                if (variant.skillDef && variant.skillDef.activationState.stateType == placeState)
                {
                    return variant.skillDef;
                }
            }

            return null;
        }

        private static Color PickupColour(PickupIndex index)
        {
            PickupDef? pickup = PickupCatalog.GetPickupDef(index);
            return pickup != null ? pickup.baseColor : Color.clear;
        }

        // A clear title colour is how TooltipProvider says there's no tooltip, so a missing colour falls back to white items'
        private static AllyTip? Tip(string name, Color colour, string body, string what)
        {
            if (colour.a <= 0f)
            {
                colour = ColorCatalog.GetColor(ColorCatalog.ColorIndex.Tier1Item);
            }

            return body.Length > 0 ? new AllyTip(name, colour, body, what) : null;
        }
    }
}
