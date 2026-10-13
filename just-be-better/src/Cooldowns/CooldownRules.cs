using System;

namespace JustBeBetter.Cooldowns
{
    // When an icon's countdown shows, the game's rule widened to any charge refilling
    internal static class CooldownRules
    {
        // A skill set to hide its cooldown, one that doesn't recharge, or one blocked from recharging stays as the game has it
        internal static bool SkillRecharging(int stock, int maxStock, float remaining, float rechargeRate, bool blocked, bool hidden)
        {
            // On the divided value as the game reads it, so a mod's absurd rate can't make it show 0
            return !hidden && !blocked && rechargeRate > 0f && stock < maxStock && remaining > 0f && remaining / rechargeRate > 0f;
        }

        // In the game's own units, seconds left at the current recharge rate, rounded up
        internal static int SkillSeconds(float remaining, float rechargeRate)
        {
            if (rechargeRate <= 0f || float.IsNaN(remaining) || float.IsInfinity(remaining))
            {
                return 0;
            }

            // Divided in float as the game does, so both round the same at a whole second
            float ratio = remaining / rechargeRate;
            double seconds = Math.Ceiling(ratio);
            return seconds >= int.MaxValue ? int.MaxValue : (int)Math.Max(0d, seconds);
        }

        internal static bool EquipmentRecharging(bool hasEquipment, bool disabled, int stock, int maxStock, int cooldownValue)
        {
            return hasEquipment && !disabled && stock < maxStock && cooldownValue > 0;
        }

        // The game's DisplayData.showCooldown, so ours only ever adds to it
        internal static bool EquipmentGameShows(bool hasEquipment, int stock, int cooldownValue)
        {
            return stock <= 0 && (hasEquipment || cooldownValue > 0);
        }
    }
}
