// Ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: a table keyed by buff instead of language tokens, the style tags' stray quote
// removed, a few typos fixed, its comments dropped

using System.Collections.Generic;
using RoR2;

namespace JustBeBetter.BuffInfo
{
    // English names and descriptions for the game's buffs, the game has none of its own
    internal static class BuffDefinitions
    {
        private const string utilityString = "<style=cIsUtility>";
        private const string damageString = "<style=cIsDamage>";
        private const string healingString = "<style=cIsHealing>";
        private const string healthString = "<style=cIsHealth>";
        private const string voidString = "<style=cIsVoid>";
        private const string shrineString = "<style=cShrine>";
        private const string styleString = "</style>";

        private static Dictionary<BuffIndex, (string Name, string Description)>? table;

        // Built on first use, once the buff catalog has its indices
        internal static bool TryGet(BuffDef buff, out string name, out string description)
        {
            table ??= Build();
            if (buff != null && table.TryGetValue(buff.buffIndex, out (string Name, string Description) entry))
            {
                name = entry.Name;
                description = entry.Description;
                return true;
            }

            name = string.Empty;
            description = string.Empty;
            return false;
        }

        private static Dictionary<BuffIndex, (string Name, string Description)> Build()
        {
            var built = new Dictionary<BuffIndex, (string Name, string Description)>();

            // A buff another mod removed, or one this game version lacks, is skipped
            void Add(BuffDef buff, string name, string description)
            {
                if (buff != null && buff.buffIndex != BuffIndex.None)
                {
                    built[buff.buffIndex] = (name, description);
                }
            }

            // Base Game
            Add(RoR2Content.Buffs.AffixRed, "Blazing", $"Leave a fire trail that hurts enemies, and apply a 50% total damage {damageString}burn{styleString} on hit.");
            Add(RoR2Content.Buffs.AffixBlue, "Overloading", $"Attacks leave overloading orbs that explode for 50% TOTAL damage in a 3m radius after a short delay. 50% of {healingString}health{styleString} is replaced by {utilityString}shield{styleString}.");
            Add(RoR2Content.Buffs.AffixWhite, "Glacial", $"Leave an ice explosion on death, and apply an 80% {utilityString}slow{styleString} on hit.");
            Add(RoR2Content.Buffs.AffixPoison, "Malachite", $"Shoot occasional urchins and apply {healthString}disable healing for 8s{styleString} on hit.");
            Add(RoR2Content.Buffs.AffixHaunted, "Celestine", $"{utilityString}Cloak{styleString} nearby allies, and apply an 80% {utilityString}slow{styleString} on hit.");
            Add(RoR2Content.Buffs.AffixLunar, "Perfected", $"{utilityString}Cripple{styleString} on hit. Occasionally fire 4 bomb projectiles at enemies. Gain 30% increased {utilityString}movement speed{styleString}, and gain 25% {healthString}max health{styleString}. All health will be replaced with {utilityString}shields{styleString}.");
            Add(RoR2Content.Buffs.HealingDisabled, $"Malachite Poisoning", $"Disables all healing, including health regeneration. ");
            Add(RoR2Content.Buffs.AffixHauntedRecipient, "Celestine Cloak", $"You are {utilityString}cloaked{styleString} due to a Celestine Ally.");
            Add(RoR2Content.Buffs.BanditSkull, $"Bandit Skull", $"Desperado gains {damageString}10% damage{styleString} per stack.");
            Add(RoR2Content.Buffs.ArmorBoost, $"Strong Armor Boost", $"Gain {utilityString}+200 armor.{styleString} ");
            Add(RoR2Content.Buffs.SmallArmorBoost, $"Small Armor Boost", $"Gain {utilityString}+100 armor{styleString}. ");
            Add(RoR2Content.Buffs.CrocoRegen, $"Regenerative", $"Gain {healingString}health regeneration{styleString} equal to 5% of your {healingString}maximum health{styleString}. ");
            Add(RoR2Content.Buffs.Weak, $"Weak", $"Reduces {utilityString}armor{styleString} by 30%, reduces {utilityString}movement speed and damage{styleString} by 40%. ");
            Add(RoR2Content.Buffs.Entangle, $"Entangle", $"Disables {utilityString}movement.{styleString}");
            Add(RoR2Content.Buffs.Fruiting, $"Fruiting", $"Spawn 2-8 {healingString}healing{styleString} fruits on death. ");
            Add(RoR2Content.Buffs.MercExpose, $"Expose", $"Will take {damageString}350% damage{styleString} from next attack.");
            Add(RoR2Content.Buffs.MedkitHeal, $"Medkit Heal", $"Gain {healingString}health{styleString} after a 2-second delay. ");
            Add(RoR2Content.Buffs.Warbanner, $"Warbanner", $"Gain {utilityString}+30% attack{styleString} and {utilityString}movement speed{styleString}. ");
            Add(RoR2Content.Buffs.ElementalRingsReady, $"Elemental Bands Ready", $"Elemental bands can be activated.");
            Add(RoR2Content.Buffs.ElementalRingsCooldown, $"Elemental Bands Cooldown", $"Elemental Bands are on cooldown");
            Add(RoR2Content.Buffs.WhipBoost, $"Whip Boost", $"Gain {utilityString}+30% movement speed{styleString} for each Red Whip. ");
            Add(RoR2Content.Buffs.AttackSpeedOnCrit, $"Attack Speed On Crit", $"Gain {utilityString}+12% attack speed{styleString} per stack.");
            Add(RoR2Content.Buffs.Cloak, $"Cloak", $"{utilityString}Disappear{styleString}. Enemies cannot target invisible allies.");
            Add(RoR2Content.Buffs.CloakSpeed, $"Cloak Speed", $"Gain {utilityString}+40% movement speed{styleString} while invisible. ");
            Add(RoR2Content.Buffs.Energized, $"Energized", $"Gain {utilityString}+70% attack speed{styleString}.");
            Add(RoR2Content.Buffs.DeathMark, $"Death Mark", $"Increases {damageString}damage taken{styleString} from {healthString}all sources{styleString} by 50% for 7 (+7 per stack) seconds. ");
            Add(RoR2Content.Buffs.NoCooldowns, $"No Cooldowns", $"Ability cooldowns reduced to 0.5s. ");
            Add(RoR2Content.Buffs.LaserTurbineKillCharge, $"Resonance Disc Kill Charge", $"Gain a stack per kill that lasts for 7 seconds. At 4 stacks, the Resonance Disc fires, resetting all stacks. ");
            Add(RoR2Content.Buffs.TeslaField, $"Tesla Field", $"Shock nearby enemies for {damageString}200% damage{styleString} every 0.5s.");
            Add(RoR2Content.Buffs.Pulverized, $"Pulverized", $"Reduces {utilityString}armor{styleString} by 60.");
            Add(RoR2Content.Buffs.PulverizeBuildup, $"Pulverize Buildup", $"Upon getting 5 stacks, reset all stacks and apply the Pulverized debuff. ");
            Add(RoR2Content.Buffs.PowerBuff, $"Power Buff", $"Gain {damageString}+50% damage{styleString}. ");
            Add(RoR2Content.Buffs.LunarSecondaryRoot, $"Lunar Root", $"Disables {utilityString}movement{styleString}. ");
            Add(RoR2Content.Buffs.LunarDetonationCharge, $"Ruin", $"Consumes Ruin stacks to deal 300% {damageString}damage{styleString} plus 120% {damageString}damage{styleString} per Ruin stack ");
            Add(RoR2Content.Buffs.ElephantArmorBoost, $"Elephant Armor Boost", $"Gain {shrineString}+500 armor{styleString}, negating most attacks. ");
            Add(RoR2Content.Buffs.FullCrit, $"Full Crit", $"Gain {damageString}100% critical strike{styleString} chance. ");
            Add(RoR2Content.Buffs.LifeSteal, $"Life Steal", $"{healingString}Heal{styleString} for 20% of {damageString}damage{styleString} dealt.");
            Add(RoR2Content.Buffs.TeamWarCry, $"Team War Cry", $"Gain {utilityString}+50% movement speed{styleString} and {utilityString}+100% attack speed{styleString}. ");
            Add(RoR2Content.Buffs.WarCryBuff, $"War Cry", $"Gain {utilityString}+50% movement speed{styleString} and {utilityString}+100% attack speed{styleString}. ");
            Add(RoR2Content.Buffs.TonicBuff, $"Spinel Tonic", $"Gain the following boosts:\n{healingString}+150% max health{styleString}\n{healingString}+400% health regeneration{styleString}\n{utilityString}+170% attack speed{styleString}\n{utilityString}+130% movement speed{styleString}\n{utilityString}+20 armor{styleString}\n{damageString}+200% base damage{styleString}");
            Add(RoR2Content.Buffs.Immune, $"Immune", $"Become immune to all attacks.");
            Add(RoR2Content.Buffs.HiddenInvincibility, $"Hidden Invincibility", $"Become immune to all attacks.");
            Add(RoR2Content.Buffs.PermanentCurse, $"Permanent Curse", $"Maximum health reduced");
            Add(RoR2Content.Buffs.Bleeding, $"Bleed", $"Deals {damageString}240% damage{styleString} over time. Refreshes duration when applied.");
            Add(RoR2Content.Buffs.SuperBleed, $"Hemorrhage", $"{damageString}2000% base damage{styleString} over 15s. Refreshes duration when applied.");
            Add(RoR2Content.Buffs.Poisoned, $"Poisoned", $"Deals 1% of the victim's {damageString}maximum health{styleString} per second");
            Add(RoR2Content.Buffs.Blight, $"Blight", $"Deals {damageString}20% base damage stat{styleString} per tick");
            Add(RoR2Content.Buffs.OnFire, $"Ignited", $"Applies a percent of {damageString}damage{styleString} over time, and {healthString}disables health regeneration.{styleString} ");
            Add(DLC1Content.Buffs.StrongerBurn, "Wild Burn", $"A deadly burn with at least 4x effectiveness of regular Ignite. Health regeneration is disabled. ");
            Add(RoR2Content.Buffs.Slow50, $"50% Slow", $"Reduces {utilityString}movement speed{styleString} by 50% ");
            Add(RoR2Content.Buffs.Slow60, $"60% Slow", $"Reduces {utilityString}movement speed{styleString} by 60% ");
            Add(RoR2Content.Buffs.Slow80, $"80% Slow", $"Reduces {utilityString}movement speed{styleString} by 80% ");
            Add(RoR2Content.Buffs.BeetleJuice, $"Beetle Juice", $"Reduce {damageString}movement speed{styleString}, {healingString}character damage{styleString}, and {damageString}attack speed{styleString} by 5%. ");
            Add(RoR2Content.Buffs.Cripple, $"Cripple", $"Reduces {utilityString}armor{styleString} by 20. Reduces {utilityString}movement speed{styleString} by 50%. ");
            Add(RoR2Content.Buffs.Nullified, $"Nullified", $"Disables {utilityString}movement{styleString}.");
            Add(RoR2Content.Buffs.NullifyStack, $"Nullify Buildup", $"Upon getting 3 stacks, reset all stacks and apply the Nullified debuff. ");
            Add(RoR2Content.Buffs.Overheat, $"Overheat", $"Increases the duration of burn damage from Grandparent's sun attack. ");
            Add(RoR2Content.Buffs.ClayGoo, $"Tar", $"Reduces {utilityString}movement speed{styleString} by 50%. ");
            Add(RoR2Content.Buffs.VoidFogMild, $"Void Fog", $"Deals a small amount of {damageString}damage{styleString} multiple times per second, {healthString}increasing with each tick{styleString}. ");
            Add(RoR2Content.Buffs.VoidFogStrong, $"Void Fog", $"Deals a medium amount of {damageString}damage{styleString} multiple times per second, {healthString}increasing with each tick{styleString}.");

            // DLC1
            Add(DLC1Content.Buffs.VoidSurvivorCorruptMode, "Void Corruption", $"+100 Armor and your skills are {voidString}Corrupted.{styleString}");
            Add(DLC1Content.Buffs.OutOfCombatArmorBuff, "Opal Armor", $"Gain {shrineString}+100 armor{styleString}. Removed on hit.");
            Add(DLC1Content.Buffs.KillMoveSpeed, "Harpoon Kill", $"Gain {utilityString}+25% movement speed{styleString}.");
            Add(DLC1Content.Buffs.PrimarySkillShurikenBuff, "Shuriken", $"Your next primary attack launches a high damage Shuriken.");
            Add(DLC1Content.Buffs.PermanentDebuff, "Scorpion Armor Reduction", $"Reduces {utilityString}armor{styleString} by 2 per stack for the remainder of the stage, or until killed. ");
            Add(DLC1Content.Buffs.ImmuneToDebuffReady, "Debuff Immunity", $"Will {utilityString}prevent{styleString} the next debuff and add 10% Barrier.");
            Add(DLC1Content.Buffs.ImmuneToDebuffCooldown, "Debuff Immunity Cooldown", $"Ben's Raincoat is on cooldown");
            Add(DLC1Content.Buffs.Fracture, "Collapse", $"3 seconds after the first stack is applied, deals 400% {damageString}damage{styleString} per stack and removes all stacks. ");
            Add(DLC1Content.Buffs.MushroomVoidActive, "Weeping Fungus Regeneration", $"{healingString}Heal{styleString} while sprinting.");
            Add(DLC1Content.Buffs.BearVoidReady, "Safer Spaces Ready", $"Negates the next source of damage, then goes on cooldown.");
            Add(DLC1Content.Buffs.BearVoidCooldown, "Safer Spaces Cooldown", $"Damage negation is on cooldown.");
            Add(DLC1Content.Buffs.ElementalRingVoidReady, "Singularity Band Ready", $"Singularity Band can be activated.");
            Add(DLC1Content.Buffs.ElementalRingVoidCooldown, "Singularity Band Cooldown", $"Singularity Band is on cooldown");
            Add(DLC1Content.Buffs.EliteEarth, "Mending", $"{healingString}Heal{styleString} nearby non-mending allies. Produce a {healingString}healing core{styleString} on death, which detonates and heals all nearby allies. ");
            Add(DLC1Content.Buffs.EliteVoid, "Voidtouched", $"{damageString}Collapse{styleString} on hit and {utilityString}block one hit{styleString} every 15 seconds.");
            Add(DLC1Content.Buffs.VoidRaidCrabWardWipeFog, "Voidling Fog", $"Deals a small amount of {damageString}damage{styleString} multiple times per second, {healthString}increasing with each tick{styleString}. Until you return to Voidling. ");

            // DLC2
            Add(DLC2Content.Buffs.ChakraBuff, "Tranquility", $"Improves Seekers skills in various ways.");
            Add(DLC2Content.Buffs.RevitalizeBuff, "Saving Grace", $"Increase your stats by 7%. Seeker can revive you using Meditate.");
            Add(DLC2Content.Buffs.SeekerRevivedOnce, "Consumed Grace", $"You were revived by Seeker and cannot be revived by her again until the next stage.");
            Add(DLC2Content.Buffs.Boosted, "Yes, CHEF!", $"CHEFs next attack will be upgraded.");
            Add(DLC2Content.Buffs.Oiled, "Oily", $"You take +300% Burn damage");
            Add(DLC2Content.Buffs.Frost, "Frost", $"Slows you by stacking amounts. Freeze upon getting 6 stacks or if Oiled.");
            string cooking = $"Enemies with 2 Cooking buffs drop Food. Food heals for 2hp + 4% max hp for each unique cooking buff";
            Add(DLC2Content.Buffs.CookingChopped, "Cooking", cooking);
            Add(DLC2Content.Buffs.CookingRoasted, "Cooking", cooking);
            Add(DLC2Content.Buffs.CookingChilled, "Cooking", cooking);
            Add(DLC2Content.Buffs.CookingRolled, "Cooking", cooking);
            Add(DLC2Content.Buffs.CookingOiled, "Cooking", cooking);
            Add(DLC2Content.Buffs.EnergizedCore, "Energized Core", $"Lunar Tampering stat changes will be three times as effective.");
            Add(DLC2Content.Buffs.lunarruin, "Lunar Ruin", $"Increase damage taken by 10% per buff. Decrease healing by 20%");
            Add(DLC2Content.Buffs.DisableAllSkills, "Skills Disabled", $"Your skills and equipment are disabled.");
            Add(DLC2Content.Buffs.ExtraLifeBuff, "Extra Life", $"Revive if you die this stage.");
            Add(DLC2Content.Buffs.SoulCost, "Soul Cost", $"Reduce health by 10%");
            Add(DLC2Content.Buffs.AttackSpeedPerNearbyAllyOrEnemyBuff, "Bolstering Lantern", $"Gain an attack speed boost because an ally is nearby");
            Add(DLC2Content.Buffs.DelayedDamageBuff, "Warped Echo", $"Warped Echo is ready to split damage.");
            Add(DLC2Content.Buffs.DelayedDamageDebuff, "Damage Echo", $"Instances of Echoed split damage.");
            Add(DLC2Content.Buffs.ElusiveAntlersBuff, "Elusive Antler", $"Temporarily increase speed by 12% for 12s. Refreshes stack duration when reapplied");
            Add(DLC2Content.Buffs.IncreaseDamageBuff, "Chronic Expansion", $"Increase your damage.");
            Add(DLC2Content.Buffs.KnockBackAvailable, "Breaching Fin Ready", $"Breaching Fin is Ready");
            Add(DLC2Content.Buffs.KnockBackUnavailable, "Breaching Fin Cooldown", $"Breaching Fin is on cooldown for 15s");
            Add(DLC2Content.Buffs.ExtraStatsOnLevelUpBuff, "Prayer Beads XP", $"Stored Experience that will grant the stats of 0.2 (+0.05 per Prayer Beads) levels upon removal of a Prayer Bead");
            Add(DLC2Content.Buffs.IncreasePrimaryDamageBuff, "Luminous Charge", $"Stored Lightning for your next primary attack. Can only be used at 3 stacks or higher.");
            Add(DLC2Content.Buffs.TeleportOnLowHealthActive, "Unstable Aura", $"Unstable Transmitter has activated a dimensional Aura. Enemies that touch it and die will increase duration by 1s.");
            Add(DLC2Content.Buffs.BoostAllStatsBuff, "Growth Nectar", $"Increase all stats by 4%");
            Add(DLC2Content.Buffs.ExtraBossMissile, "War Missile", $"Stored Missiles dealing 2.5% max Hp damage, for the next boss or Scavenger encounter");
            Add(DLC2Content.Buffs.FreeUnlocks, "Free Purchase", $"Your next purchase is free");
            Add(DLC2Content.Buffs.EliteAurelionite, "Gilded", $"Fire out a Gilded Spike every 10-15s. Doing damage has a chance to produce extra Gold.");
            Add(DLC2Content.Buffs.EliteBead, "Twisted", $"Give nearby allies 300 Armor. Unleash a Twisted Spike after nearby allies are hurt 10 times. This ability has a 10s cooldown.");
            Add(DLC2Content.Buffs.BeadArmor, "Twisted Armor", $"Gain 300 Armor");

            // DLC3
            Add(DLC3Content.Buffs.StimShot, "Stim-Shot", $"Increase <style=cIsDamage>attack speed</style> and <style=cIsDamage>movement speed</style> by <style=cIsDamage>25%</style>.");
            Add(DLC3Content.Buffs.NanoBug, "Nano Bugged", $"Nano Bugged");
            Add(DLC3Content.Buffs.EliteCollective, "Collective", $"Gain a 30m dome that protects you from outside attacks. All allies within gain 25% cooldown reduction.");
            Add(DLC3Content.Buffs.CollectiveShareBuff, "Part Collective", $"Gain 25% cooldown reduction.");
            Add(DLC3Content.Buffs.InventoryDisable, "Items Disabled", $"All your items are temporarily disabled.");
            Add(DLC3Content.Buffs.Accelerant, "Greased", $"Being ignited or hit by fire-like attacks causes you to explode.");
            Add(DLC3Content.Buffs.PowerCubeBuff, "Prison Matrix", $"Increase base armor by 50%");
            Add(DLC3Content.Buffs.PowerPyramidBuff, "Sentry Key", $"Increase movement speed by 15%");
            Add(DLC3Content.Buffs.TrashToTreasureWhite, "Common Trash to Treasure", $"Increase movement speed by 6%");
            Add(DLC3Content.Buffs.TrashToTreasureGreen, "Uncommon Trash to Treasure", $"Increase base health regen speed by 3 hp/s.");
            Add(DLC3Content.Buffs.TrashToTreasureRed, "Legendary Trash to Treasure", $"Increase attack speed by 30%");
            Add(DLC3Content.Buffs.TrashToTreasureYellow, "Boss Trash to Treasure", $"Reduce skill cooldowns by 15%");
            Add(DLC3Content.Buffs.JumpDamageStrikeCharge, "Faraday Charge", $"Increase movement speed by 1.6% and jump height by 2%");
            Add(DLC3Content.Buffs.SpeedOnPickup, "Collectors Compulsion", $"Increases all stats by 3%");
            Add(DLC3Content.Buffs.SharedSuffering, "Networked Suffering", $"");
            Add(DLC3Content.Buffs.SharedSufferingStock, "Network Capacity", $"Your network of suffering is at max capacity");
            Add(DLC3Content.Buffs.SharedSufferingStockEmpty, "Network Full", $"Your network of suffering is at max capacity");
            Add(DLC3Content.Buffs.ShockDamageEnergized, "Conductor Energized", $"Increase attack and movement speed by 30%. All Electrical attacks deal critical damage.");
            Add(DLC3Content.Buffs.Brittle, "Cooled", $"Instantly remove all overheat and burn effects");
            Add(DLC3Content.Buffs.UltimateMealBoost, "Ultimate Meal", $"+2 Luck per Ultimate Meal");
            Add(DLC3Content.Buffs.CritChanceAndDamage, "Hiking", $"+1% Crit chance and damage");
            Add(DLC3Content.Buffs.Parrying, "Parrying", $"Blocks incoming damage once and turns it into a blessing.");
            Add(DLC3Content.Buffs.SureProc, "God's Blessing", $"Your next attack triggers all chance based on-hit and on-kill effects.");
            Add(DLC3Content.Buffs.Slow10Stacking, "10% Slow", $"Decrease movement speed by 10%");
            Add(DLC3Content.Buffs.HealNovaRegen, "Emergency Nova Regeneration", $"Gain {healingString}health regeneration{styleString} equal to 2.5% of your {healingString}maximum health{styleString}. ");
            Add(DLC3Content.Buffs.TransferDebuffOnHit, "Weighted", $"Netronium Weight is decreasing all your stats.");
            Add(DLC3Content.Buffs.Jailed, "Jailed", $"Jailed");

            return built;
        }
    }
}
