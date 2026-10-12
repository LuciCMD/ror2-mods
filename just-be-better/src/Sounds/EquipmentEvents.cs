using System;
using System.Collections.Generic;

namespace JustBeBetter.Sounds
{
    // Vanilla equipment sound events, from the sound banks' .txt listings (game 1.5.0)
    // Stop events are listed too, so a loop and the event that ends it meet on the same emitter
    internal static class EquipmentEvents
    {
        internal const string Chime = "Play_UI_equipment_activate";

        // Also played by REX's BurrowDash on its own body
        // The equipment's comes from an effect emitter
        internal const string Fruit = "Play_item_use_fruit";

        private static readonly string[] Names =
        {
            "Play_item_use_BFG_charge", "Play_item_use_BFG_explode", "Play_item_use_BFG_fire", "Play_item_use_BFG_zaps",
            "Stop_item_use_BFG_loop",
            "Play_item_use_EMPScanner_activate", "Play_item_use_EMPScanner_findChest", "Play_item_use_EMPScanner_loop_end",
            "Stop_item_use_EMPScanner_loop",
            "Play_item_use_blackhole_end", "Play_item_use_blackhole_start", "Play_item_use_blackhole_suckin",
            "Stop_item_use_blackhole_loop",
            "Play_item_use_bossHunter",
            "Play_item_use_bugWingClose", "Play_item_use_bugWingFlapLoop", "Play_item_use_bugWingOpen",
            "Stop_item_use_bugWingFlapLoop",
            "Play_item_use_cleanse",
            "Play_item_use_crit_end", "Play_item_use_crit_start", "Stop_item_use_crit_loop",
            "Play_item_use_fireballDash_explode", "Play_item_use_fireballDash_start", "Stop_item_use_fireballDash_loop",
            Fruit,
            "Play_item_use_gainArmor",
            "Play_item_use_gateway_activeLoop", "Play_item_use_gateway_create", "Play_item_use_gateway_rideEnd",
            "Play_item_use_gateway_rideStart", "Play_item_use_gateway_vanish",
            "Stop_item_use_gateway_activeLoop", "Stop_item_use_gateway_rideLoop",
            "Play_item_use_groundEnemies", "Play_item_use_groundEnemies_hit",
            "Play_item_use_gummyClone_expire", "Play_item_use_gummyClone_spawn",
            "Play_item_use_healAndRevive_activate", "Play_item_use_healAndRevive_healOrb_gather",
            "Play_item_use_healAndRevive_healPlantGrow", "Play_item_use_healAndRevive_reviveTreeGrow",
            "Play_item_use_hellfire_end", "Play_item_use_hellfire_start", "Stop_item_use_hellfire_loop",
            "Play_item_use_lighningArm",
            "Play_item_use_meteor_impact", "Play_item_use_meteor_storm_end", "Play_item_use_meteor_storm_start",
            "Play_item_use_meteor_tell",
            "Play_item_use_molotov_fire_loop", "Play_item_use_molotov_impact_big", "Play_item_use_molotov_impact_small",
            "Play_item_use_molotov_throw", "Stop_item_use_molotov_fire_loop",
            "Play_item_use_passive_healing",
            "Play_item_use_radio",
            "Play_item_use_recycler",
            "Play_item_use_sawmerang_catch", "Play_item_use_sawmerang_chargeup", "Play_item_use_sawmerang_fly_loop",
            "Play_item_use_sawmerang_throw", "Stop_item_use_sawmerang_chargeup", "Stop_item_use_sawmerang_fly_loop",
            "Play_item_use_tonic", "Play_item_use_tonic_debuff",
            "Play_item_goldgat_fire", "Play_item_goldgat_winddown", "Play_item_goldgat_windup",
            "Play_deathProjectile_activate", "Play_deathProjectile_activeLoop", "Play_deathProjectile_exit",
            "Play_deathProjectile_pulse", "Play_deathProjectile_throw", "Stop_deathProjectile_activeLoop",
            "Play_lifestealOnHit_activate",
            "Play_teamWarCry_activate",
            "Play_DLC4_Equip_SpawnAltar_Activate", "Play_DLC4_Equip_SpawnAltar_Charge", "Play_DLC4_Equip_SpawnAltar_Loop",
            "Stop_DLC4_Equip_SpawnAltar_Loop",
            "Play_GG_CHAR_Skill_G_Force_Activate", "Play_GG_CHAR_Skill_G_Force_Loop", "Stop_GG_CHAR_Skill_G_Force_Loop",
        };

        private static readonly HashSet<uint> Ids = BuildIds();

        internal static uint ChimeId { get; } = Id(Chime);

        internal static uint FruitId { get; } = Id(Fruit);

        internal static bool Contains(uint id)
        {
            return Ids.Contains(id);
        }

        // Wwise's ID for a name: 32-bit FNV-1 of the lowercase name (checked against the banks' listings)
        internal static uint Id(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash = unchecked(hash * 16777619) ^ char.ToLowerInvariant(c);
            }

            return hash;
        }

        private static HashSet<uint> BuildIds()
        {
            var ids = new HashSet<uint>();
            foreach (string name in Names)
            {
                if (!ids.Add(Id(name)))
                {
                    throw new InvalidOperationException($"Equipment event listed twice: {name}");
                }
            }

            return ids;
        }
    }
}
