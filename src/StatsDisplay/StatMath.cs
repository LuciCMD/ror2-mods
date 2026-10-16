// The portal list ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: flags instead of fields, the rest of the file written for Just_Be_Better

using System;
using System.Globalization;
using System.Text;

namespace JustBeBetter.StatsDisplay
{
    // The numbers and text behind the stats, apart from the game so they can be checked outside it
    internal static class StatMath
    {
        internal const string NotAvailable = "N/A";
        internal const int MaxDecimals = 4;

        // Portal flags, in the order LookingGlass lists them
        [Flags]
        internal enum Portals
        {
            None = 0,
            Bazaar = 1,
            Gold = 2,
            Node = 4,
            Encrypted = 8,
            Storm = 16,
            Void = 32,
            Celestial = 64,
            Artifact = 128,
        }

        private static readonly (Portals Portal, string Text)[] PortalNames =
        {
            (Portals.Bazaar, "<style=cLunarObjective>Bazaar </style>"),
            (Portals.Gold, "<style=cIsDamage>Gold </style>"),
            (Portals.Node, "<style=cIsHealth>Node </style>"),
            (Portals.Encrypted, "<style=cIsUtility>Encrypted </style>"),
            (Portals.Storm, "<style=cIsHealing>Storm </style>"),
            (Portals.Void, "<style=cIsVoid>Void </style>"),
            (Portals.Celestial, "<style=cIsUtility>Celestial </style>"),
            (Portals.Artifact, "<style=cIsDeath>Artifact </style>"),
        };

        // As LookingGlass's "0.##", but a dot whatever the system's culture
        internal static string Pattern(int decimals)
        {
            int places = Math.Max(0, Math.Min(decimals, MaxDecimals));
            return places == 0 ? "0" : "0." + new string('#', places);
        }

        internal static string Number(float value, string pattern)
        {
            if (float.IsNaN(value))
            {
                return NotAvailable;
            }

            if (float.IsInfinity(value))
            {
                return value > 0 ? "∞" : "-∞";
            }

            string text = value.ToString(pattern, CultureInfo.InvariantCulture);

            // .NET Core keeps the sign of a value that rounds to zero
            return text.Length > 1 && text[0] == '-' && text.Substring(1).Trim('0', '.').Length == 0 ? text.Substring(1) : text;
        }

        // What a hit's damage is multiplied by, negative armor counts as the game does
        internal static float DamageTaken(float armor)
        {
            if (float.IsNaN(armor))
            {
                return 1f;
            }

            return armor >= 0f ? 100f / (100f + armor) : 2f - 100f / (100f - armor);
        }

        // Negative when armor below 0 adds damage
        internal static float ArmorReductionPercent(float armor)
        {
            return (1f - DamageTaken(armor)) * 100f;
        }

        internal static float EffectiveHealth(float health, float armor)
        {
            return health / DamageTaken(armor);
        }

        // Tougher Times, hyperbolic
        internal static float BlockChancePercent(int stacks)
        {
            if (stacks <= 0)
            {
                return 0f;
            }

            float block = 0.15f * stacks;
            return block / (block + 1f) * 100f;
        }

        // Safer Spaces, 15 s times 0.9 per stack as HealthComponent.TakeDamageProcess has it, so 13.5 s for one
        internal static float SaferSpacesSeconds(int stacks)
        {
            return stacks <= 0 ? float.NaN : 15f * (float)Math.Pow(0.9, stacks);
        }

        // Shrunk to fit as more show, as LookingGlass does
        internal static string PortalText(Portals portals)
        {
            var text = new StringBuilder();
            int count = 0;
            foreach ((Portals portal, string name) in PortalNames)
            {
                if ((portals & portal) != 0)
                {
                    text.Append(name);
                    count++;
                }
            }

            if (count == 0)
            {
                return "<style=cStack>None</style>";
            }

            return $"<size={(int)(7f / (7f + count) * 100f)}%>{text}</size>";
        }
    }
}
