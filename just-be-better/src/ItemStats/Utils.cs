// Ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: internal, namespace, the cap read from our setting, unused helpers dropped,
// PrivateStatic added, nullable checks off

#nullable disable

using System;
using System.Reflection;
using UnityEngine;

namespace JustBeBetter.ItemStats
{
    internal static class Utils
    {
        internal static bool CapChance { get; set; } = true;

        public static float CalculateChanceWithLuck(float baseChance, float luckIn)
        {
            if (CapChance)
            {
                baseChance = Mathf.Min(baseChance, 1);
            }
            float chanceFloored = Mathf.Floor(baseChance);
            float chanceMod = baseChance % 1f;
            int luck = Mathf.CeilToInt(luckIn);
            if (luck > 0)
                return chanceFloored + (1f - Mathf.Pow(1f - chanceMod, luck + 1));
            if (luck < 0)
                return chanceFloored + Mathf.Pow(chanceMod, Mathf.Abs(luck) + 1);

            return baseChance;
        }

        public static float GetExponentialRechargeTime(float baseCooldown, float extraPercent, int count)
        {
            return baseCooldown * Mathf.Pow(1 - extraPercent, count - 1);
        }

        public static float GetExponentialStacking(float basePercent, float extraPercent, int count)
        {
            return 1 - (1 - basePercent) * Mathf.Pow(1 - extraPercent, count - 1);
        }

        public static float GetHyperbolicStacking(float basePercent, float extraPercent, int count)
        {
            return 1f - 1f / (1f + basePercent + extraPercent * (count - 1));
        }

        public static float GetBandolierStacking(int count)
        {
            return 1f - 1f / Mathf.Pow(1f + count, 0.33f);
        }

        // LookingGlass reads these directly from a publicized game, a missing one throws and that item's tooltip
        // shows its description without stats
        internal static T PrivateStatic<T>(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(type.Name, name);
            return (T)field.GetValue(null);
        }
    }
}
