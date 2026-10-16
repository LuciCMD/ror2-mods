using System.Collections.Generic;

namespace JustBeBetter.ImmuneBar
{
    // What the game treats as taking no damage at all, in HealthComponent.TakeDamageProcess
    // Blocks that can still fail, such as Tougher Times or Safer Spaces, don't count
    internal readonly struct ImmuneFlags
    {
        internal ImmuneFlags(bool godMode, bool immuneBuff, bool hiddenInvincibility, bool rejectAll, bool sojourn, bool throneShield)
        {
            GodMode = godMode;
            ImmuneBuff = immuneBuff;
            HiddenInvincibility = hiddenInvincibility;
            RejectAll = rejectAll;
            Sojourn = sojourn;
            ThroneShield = throneShield;
        }

        internal bool GodMode { get; }

        internal bool ImmuneBuff { get; }

        internal bool HiddenInvincibility { get; }

        internal bool RejectAll { get; }

        internal bool Sojourn { get; }

        internal bool ThroneShield { get; }
    }

    internal static class ImmuneRules
    {
        // Null when nothing makes it immune, else the first reason, for the log
        internal static string? Reason(ImmuneFlags flags)
        {
            if (flags.GodMode)
            {
                return "god mode";
            }

            if (flags.RejectAll)
            {
                return "reject all damage";
            }

            if (flags.ImmuneBuff)
            {
                return "Immune buff";
            }

            if (flags.HiddenInvincibility)
            {
                return "hidden invincibility";
            }

            if (flags.Sojourn)
            {
                return "Sojourn vehicle";
            }

            return flags.ThroneShield ? "throne shield" : null;
        }

        // A boss bar sums its group, so it reads immune only while every living member is
        internal static bool AllImmune(IEnumerable<bool> livingMembers)
        {
            bool any = false;
            foreach (bool immune in livingMembers)
            {
                if (!immune)
                {
                    return false;
                }

                any = true;
            }

            return any;
        }
    }
}
