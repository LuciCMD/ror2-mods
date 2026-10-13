// Ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: read on the game's thread rather than a job, numbers with a dot in every culture,
// negative armor counted as the game does, hasOSP added as its documented name, dps, maxHealth and the like
// rounded to the set decimals, unknown values shown as N/A rather than thrown, its comments dropped

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using RoR2;
using RoR2.Networking;
using UnityEngine.Networking;

namespace JustBeBetter.StatsDisplay
{
    // What one fill of the text reads from
    internal sealed class StatContext
    {
        internal StatContext(CharacterBody body, ComboMeter meter, PortalTracker portals, float now, float window)
        {
            Body = body;
            Meter = meter;
            Portals = portals;
            Now = now;
            Window = window;
        }

        internal CharacterBody Body { get; }

        internal ComboMeter Meter { get; }

        internal PortalTracker Portals { get; }

        internal float Now { get; }

        internal float Window { get; }
    }

    internal static class StatDefinitions
    {
        // Private in the game
        private static readonly MethodInfo? BarrierDecay = typeof(HealthComponent).GetMethod(
            "GetBarrierDecayRate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        internal static Dictionary<string, Func<StatContext, string>> Build(bool colours, int decimals)
        {
            string p = StatMath.Pattern(decimals);
            string Utility(string value) => colours ? $"<style=cIsUtility>{value}</style>" : value;
            string Damage(string value) => colours ? $"<style=cIsDamage>{value}</style>" : value;
            string Healing(string value) => colours ? $"<style=cIsHealing>{value}</style>" : value;
            string Health(string value) => colours ? $"<style=cIsHealth>{value}</style>" : value;
            string Void(string value) => colours ? $"<style=cIsVoid>{value}</style>" : value;
            string Gray(string value) => colours ? $"<style=cStack>{value}</style>" : value;
            string N(float value) => StatMath.Number(value, p);
            string Percent(float value) => N(value) + "%";
            string W(long value) => value.ToString(CultureInfo.InvariantCulture);

            var stats = new Dictionary<string, Func<StatContext, string>>(StringComparer.Ordinal);

            // Damage
            stats["lvl1_damage"] = c => Damage(N(c.Body.baseDamage));
            stats["damage"] = c => Damage(N(c.Body.damage));
            stats["baseDamage"] = stats["damage"];
            stats["damagePercent"] = c => Damage(Percent(c.Body.damage / LevelScaledDamage(c.Body) * 100f));
            stats["damagePercentWithWatch"] = c => Damage(Percent(
                c.Body.damage / LevelScaledDamage(c.Body)
                * (1f + 0.2f * Count(c.Body, DLC1Content.Items.FragileDamageBonus)) * 100f));
            stats["attackSpeed"] = c => Damage(N(c.Body.attackSpeed));
            stats["attackSpeedPercent"] = c => Damage(Percent(c.Body.attackSpeed / c.Body.baseAttackSpeed * 100f));
            stats["crit"] = c => Damage(Percent(c.Body.crit));
            stats["critWithLuck"] = c => Damage(Percent(WithLuck(c.Body.crit, c.Body)));
            stats["critMultiplier"] = c => Damage("×" + N(c.Body.critMultiplier));
            stats["bleedChance"] = c => Damage(Percent(c.Body.bleedChance));
            stats["bleedChanceWithLuck"] = c => Damage(Percent(WithLuck(c.Body.bleedChance, c.Body)));
            stats["bleedChanceWithSpleen"] = c => Damage(Percent(
                WithLuck(c.Body.crit, c.Body) * WithLuck(c.Body.bleedChance, c.Body) / 100f));

            // Health
            stats["armor"] = c => Healing(N(c.Body.armor));
            stats["armorDamageReduction"] = c => Healing(Percent(StatMath.ArmorReductionPercent(c.Body.armor)));
            stats["lvl1_regen"] = c => Healing(N(c.Body.baseRegen));
            stats["regenRaw"] = c => Healing(N(c.Body.regen));
            stats["regen"] = c => Healing(N(Regen(c.Body)));
            stats["regenHp"] = c => Healing(N(Regen(c.Body)) + " hp/s");
            stats["lvl1_maxHealth"] = c => Healing(N(c.Body.baseMaxHealth));
            stats["maxHealth"] = c => Healing(N(c.Body.maxHealth));
            stats["maxShield"] = c => Healing(N(c.Body.maxShield));
            stats["maxBarrier"] = c => Healing(N(c.Body.maxBarrier));
            stats["shieldFraction"] = c => HealthOf(c, h => Healing(Percent(c.Body.maxShield / h.fullCombinedHealth * 100f)));
            stats["barrierDecayRate"] = c => HealthOf(c, h => BarrierDecay == null ? StatMath.NotAvailable : Healing(N((float)BarrierDecay.Invoke(h, null))));
            stats["maxHpPercent"] = c => HealthOf(c, h => Healing(Percent(h.fullCombinedHealth / LevelScaledHealth(c.Body) * 100f)));
            stats["maxHealthPercent"] = c => Healing(Percent(c.Body.maxHealth / LevelScaledHealth(c.Body) * 100f));
            stats["maxShieldPercent"] = c => Healing(Percent(c.Body.maxShield / LevelScaledHealth(c.Body) * 100f));
            stats["effectiveHealth"] = c => HealthOf(c, h => Healing(N(StatMath.EffectiveHealth(h.combinedHealth, c.Body.armor))));
            stats["effectiveMaxHealth"] = c => HealthOf(c, h => Healing(N(StatMath.EffectiveHealth(h.fullCombinedHealth, c.Body.armor))));
            stats["healthPercentage"] = c => HealthOf(c, h => Healing(N(h.combinedHealthFraction * 100f)));
            stats["critHeal"] = c => Healing(N(c.Body.critHeal));
            stats["hasOneShotProtection"] = c => HealthOf(c, h => Healing(
                (c.Body.oneShotProtectionFraction * h.fullCombinedHealth - h.missingCombinedHealth > 0f).ToString()));
            stats["hasOSP"] = stats["hasOneShotProtection"];
            stats["cursePenalty"] = c => Utility(N(c.Body.cursePenalty));
            stats["curseHealthReduction"] = c => Health(Percent((1f - 1f / c.Body.cursePenalty) * 100f));

            // Movement
            stats["speed"] = c => Utility(N(c.Body.moveSpeed));
            stats["speedPercent"] = c => Utility(Percent(
                c.Body.moveSpeed / c.Body.baseMoveSpeed / (c.Body.isSprinting ? c.Body.sprintingSpeedMultiplier : 1f) * 100f));
            stats["speedPercentSprintAffected"] = c => Utility(Percent(c.Body.moveSpeed / c.Body.baseMoveSpeed * 100f));
            stats["acceleration"] = c => Utility(N(c.Body.acceleration));
            stats["velocity"] = c => Utility(
                c.Body.characterMotor ? N(c.Body.characterMotor.velocity.magnitude)
                : c.Body.rigidbody ? N(c.Body.rigidbody.velocity.magnitude)
                : StatMath.NotAvailable);
            stats["jumpPower"] = c => Utility(N(c.Body.jumpPower));
            stats["maxJumpHeight"] = c => Utility(N(c.Body.maxJumpHeight));
            stats["maxJumps"] = c => Utility(W(c.Body.maxJumpCount));
            stats["availableJumps"] = c => Utility(
                c.Body.characterMotor ? W(c.Body.maxJumpCount - c.Body.characterMotor.jumpCount) : StatMath.NotAvailable);

            // Utility
            stats["luck"] = c => Utility(N(Luck(c.Body)));
            stats["teddyBearBlockChance"] = c => Utility(Percent(StatMath.BlockChancePercent(Count(c.Body, RoR2Content.Items.Bear))));
            stats["saferSpacesCD"] = c =>
            {
                int stacks = Count(c.Body, DLC1Content.Items.BearVoid);
                return Utility(stacks > 0 ? N(StatMath.SaferSpacesSeconds(stacks)) + "s" : StatMath.NotAvailable);
            };
            stats["instaKillChance"] = c => Damage(Percent(
                ItemStats.Utils.CalculateChanceWithLuck(0.005f * Count(c.Body, DLC1Content.Items.CritGlassesVoid), Luck(c.Body)) * 100f));

            // Teleporter and portals
            stats["mountainShrines"] = c => Utility(TeleporterInteraction.instance ? W(TeleporterInteraction.instance.shrineBonusStacks) : StatMath.NotAvailable);
            stats["shopPortal"] = c => Utility(Portal(c, StatMath.Portals.Bazaar));
            stats["goldPortal"] = c => Damage(Portal(c, StatMath.Portals.Gold));
            stats["msPortal"] = c => Utility(Portal(c, StatMath.Portals.Celestial));
            stats["voidPortal"] = c => Void(Portal(c, StatMath.Portals.Void));
            stats["greenPortal"] = c => Healing(Portal(c, StatMath.Portals.Storm));
            stats["portals"] = c => StatMath.PortalText(c.Portals.Current() ?? StatMath.Portals.None);

            // DPS, combos and kills
            stats["killCount"] = c => Health(W(c.Meter.KillsThisStage));
            stats["killCountRun"] = c => Health(W(c.Meter.KillsThisRun));
            stats["dps"] = c => Damage(N(c.Meter.Dps(c.Now, c.Window)));
            stats["percentDps"] = c => Damage(PercentDps(c));
            stats["combo"] = c => Damage(W((long)c.Meter.Combo));
            stats["currentCombatDamage"] = stats["combo"];
            stats["maxCombo"] = c => Damage(W((long)c.Meter.BestCombo));
            stats["maxComboThisRun"] = c => Damage(W((long)c.Meter.BestComboThisRun));
            stats["killCombo"] = c => Damage(W(c.Meter.ComboKills));
            stats["currentCombatKills"] = stats["killCombo"];
            stats["maxKillCombo"] = c => Damage(W(c.Meter.BestKillCombo));
            stats["maxKillComboThisRun"] = c => Damage(W(c.Meter.BestKillComboThisRun));
            stats["remainingComboDuration"] = c => Utility(W(c.Meter.SecondsLeft(c.Now, c.Window)));

            // Run
            stats["experience"] = c => Utility(N(c.Body.experience));
            stats["level"] = c => Utility(N(c.Body.level));
            stats["difficultyCoefficient"] = c => Damage(Run.instance ? N(Run.instance.difficultyCoefficient) : StatMath.NotAvailable);
            stats["stage"] = c => Utility(Stage.instance && Stage.instance.sceneDef ? Language.GetString(Stage.instance.sceneDef.nameToken) : StatMath.NotAvailable);
            stats["ping"] = c => Gray(Ping() + "ms");

            return stats;
        }

        private static float LevelScaledDamage(CharacterBody body)
        {
            return body.baseDamage * (body.level * 0.2f + 0.8f);
        }

        private static float LevelScaledHealth(CharacterBody body)
        {
            return body.baseMaxHealth * (body.level * 0.3f + 0.7f);
        }

        private static int Count(CharacterBody body, ItemDef item)
        {
            return body.inventory && item ? body.inventory.GetItemCountEffective(item) : 0;
        }

        private static float Luck(CharacterBody body)
        {
            return body.master ? body.master.luck : 0f;
        }

        private static float WithLuck(float percent, CharacterBody body)
        {
            return ItemStats.Utils.CalculateChanceWithLuck(percent / 100f, Luck(body)) * 100f;
        }

        // Eclipse 5 halves healing, Rejuvenation Rack doubles it per stack
        private static float Regen(CharacterBody body)
        {
            float regen = Run.instance && Run.instance.selectedDifficulty >= DifficultyIndex.Eclipse5 ? body.regen / 2f : body.regen;
            return regen * (1 + Count(body, RoR2Content.Items.IncreaseHealing));
        }

        private static string HealthOf(StatContext c, Func<HealthComponent, string> value)
        {
            return c.Body.healthComponent ? value(c.Body.healthComponent) : StatMath.NotAvailable;
        }

        private static string Portal(StatContext c, StatMath.Portals portal)
        {
            StatMath.Portals? current = c.Portals.Current();
            return current == null ? StatMath.NotAvailable : ((current.Value & portal) != 0).ToString();
        }

        // RoundToInt, as a plain cast loses a percent to float error
        private static string PercentDps(StatContext c)
        {
            float damage = c.Body.damage;
            if (!(damage > 0f) || !(c.Window > 0f))
            {
                return "0%";
            }

            return ((long)Math.Round((double)c.Meter.Dps(c.Now, c.Window) * 100d / damage)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Ping()
        {
            if (NetworkServer.active)
            {
                return "0";
            }

            NetworkConnection? connection = NetworkManagerSystem.singleton ? NetworkManagerSystem.singleton.client?.connection : null;
            return connection == null ? StatMath.NotAvailable : RttManager.GetConnectionRTTInMilliseconds(connection).ToString(CultureInfo.InvariantCulture);
        }
    }
}
