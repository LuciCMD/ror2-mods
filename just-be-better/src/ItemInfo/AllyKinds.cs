using System;
using System.Collections.Generic;

namespace JustBeBetter.ItemInfo
{
    // Where an ally card's text comes from when the ally isn't a drone
    internal enum AllySource
    {
        None,
        BeetleGland,
        Squid,
        RoboBallBuddy,
        MinorConstructOnKill,
        VoidMegaCrabItem,
        PhysicsProjectile,
        VengefulSpirit,
        TitanGoldDuringTP,
        GhostOnKill,
        SpawnAltar,
        GummyClone,
        EngiTurret,
        EngiWalkerTurret,
        Devotion,
    }

    // Nothing on a summon records the item or skill behind it, so it's told by its body, which every player sees
    // Inventories and deployable slots won't do, ghosts and turrets copy a whole inventory and clients get no slots
    internal static class AllyKinds
    {
        private static readonly Dictionary<string, AllySource> ByBody = new Dictionary<string, AllySource>(StringComparer.Ordinal)
        {
            ["BeetleGuardAllyBody"] = AllySource.BeetleGland,
            ["SquidTurretBody"] = AllySource.Squid,
            ["RoboBallRedBuddyBody"] = AllySource.RoboBallBuddy,
            ["RoboBallGreenBuddyBody"] = AllySource.RoboBallBuddy,
            ["MinorConstructOnKillBody"] = AllySource.MinorConstructOnKill,
            ["VoidMegaCrabAllyBody"] = AllySource.VoidMegaCrabItem,
            ["VoidJailerAllyBody"] = AllySource.VoidMegaCrabItem,
            ["NullifierAllyBody"] = AllySource.VoidMegaCrabItem,
            ["FriendUnitBody"] = AllySource.PhysicsProjectile,
            ["VengefulSpiritFollowerBody"] = AllySource.VengefulSpirit,
            ["TitanGoldBody"] = AllySource.TitanGoldDuringTP,
            ["EngiTurretBody"] = AllySource.EngiTurret,
            ["EngiWalkerTurretBody"] = AllySource.EngiWalkerTurret,
            ["DevotedLemurianBody"] = AllySource.Devotion,
            ["DevotedLemurianBruiserBody"] = AllySource.Devotion,
        };

        internal static IEnumerable<string> Bodies => ByBody.Keys;

        // A ghost or clone wears someone else's body, so its subtitle, such as a boss's, says nothing about it
        internal static bool UseSubtitle(bool ghost, bool gooboClone) => !ghost && !gooboClone;

        // A ghost wears its victim's body and a Goobo clone its owner's, so their markers come before the body
        // The Happiest Mask and DLC4's altar both raise ghosts with the same markers, and placing the altar uses it up,
        // so a ghost is the Mask's when the owner holds one and the altar's otherwise
        // The only other source is a junk equipment the game never hands out
        internal static AllySource Of(string? bodyName, bool ghost, bool ownerHasMask, bool gooboClone)
        {
            if (ghost)
            {
                return ownerHasMask ? AllySource.GhostOnKill : AllySource.SpawnAltar;
            }

            if (gooboClone)
            {
                return AllySource.GummyClone;
            }

            return bodyName != null && ByBody.TryGetValue(bodyName, out AllySource source) ? source : AllySource.None;
        }
    }
}
