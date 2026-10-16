using System.Collections.Generic;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;

// Which text a drone's ally card shows, and the cards a setting change refreshes
internal static partial class Program
{
    private static void RunAllyCards()
    {
        Test("Ally cards: full text prefers the skill text, then the description", AllyFullOrder);
        Test("Ally cards: short text falls back to the description when the drone has none, like the Bomber", AllyBomberFallback);
        Test("Ally cards: a drone with no known text shows nothing instead of a raw key", AllyNoText);
        Test("Ally cards: a setting change refreshes cards it has seen, inactive ones too", AllyCardsRefresh);
        Test("Ally cards: destroyed cards drop out, and a card is only kept once", AllyCardsPrune);
        Test("Ally cards: each summon's body leads to what summoned it", AllyKindsByBody);
        Test("Ally cards: a ghost is told by its marker and its owner's Mask or altar, whatever body it wears", AllyKindsGhost);
        Test("Ally cards: a Goobo clone wears its owner's body but reads as Goobo", AllyKindsGoobo);
        Test("Ally cards: a summoner's text falls back between its tokens and the English text", AllyTokenText);
        Test("Ally cards: every body the table names is one of the game's", AllyBodiesExist);
    }

    // The content packs fill their BodyPrefabs fields by asset name, so a field is a body that exists
    private static void AllyBodiesExist()
    {
        var known = new HashSet<string>();
        System.Type[] lists =
        {
            typeof(RoR2.RoR2Content.BodyPrefabs), typeof(RoR2.DLC1Content.BodyPrefabs), typeof(RoR2.DLC3Content.BodyPrefabs),
            typeof(RoR2.DLC4Content.BodyPrefabs), typeof(RoR2.CU8Content.BodyPrefabs),
        };
        foreach (System.Type type in lists)
        {
            foreach (System.Reflection.FieldInfo field in type.GetFields())
            {
                known.Add(field.Name);
            }
        }

        Expect(known.Count > 100, "found the game's body lists");
        foreach (string body in AllyKinds.Bodies)
        {
            Expect(known.Contains(body), body + " is a body in the game");
        }
    }

    private static void AllyKindsByBody()
    {
        var expected = new Dictionary<string, AllySource>
        {
            ["BeetleGuardAllyBody"] = AllySource.BeetleGland,
            ["SquidTurretBody"] = AllySource.Squid,
            ["RoboBallRedBuddyBody"] = AllySource.RoboBallBuddy,
            ["RoboBallGreenBuddyBody"] = AllySource.RoboBallBuddy,
            ["MinorConstructOnKillBody"] = AllySource.MinorConstructOnKill,
            ["VoidMegaCrabAllyBody"] = AllySource.VoidMegaCrabItem,
            ["FriendUnitBody"] = AllySource.PhysicsProjectile,
            ["VengefulSpiritFollowerBody"] = AllySource.VengefulSpirit,
            ["TitanGoldBody"] = AllySource.TitanGoldDuringTP,
            ["EngiTurretBody"] = AllySource.EngiTurret,
            ["EngiWalkerTurretBody"] = AllySource.EngiWalkerTurret,
            ["DevotedLemurianBody"] = AllySource.Devotion,
            ["DevotedLemurianBruiserBody"] = AllySource.Devotion,
        };
        foreach (KeyValuePair<string, AllySource> pair in expected)
        {
            Expect(AllyKinds.Of(pair.Key, false, false, false) == pair.Value, pair.Key);
        }

        Expect(AllyKinds.Of("MinorConstructBody", false, false, false) == AllySource.None, "the enemy construct isn't Defense Nucleus's");
        Expect(AllyKinds.Of("LemurianBody", false, false, false) == AllySource.None, "a plain lemurian isn't Devotion's");
        Expect(AllyKinds.Of("SomeModdedBody", false, true, false) == AllySource.None, "an unknown body has no source");
        Expect(AllyKinds.Of(null, false, false, false) == AllySource.None, "no body name");
    }

    private static void AllyKindsGhost()
    {
        Expect(AllyKinds.Of("BeetleGuardAllyBody", true, true, false) == AllySource.GhostOnKill, "a ghost of a Beetle Guard is the Mask's");
        Expect(AllyKinds.Of("LemurianBody", true, false, false) == AllySource.SpawnAltar, "a ghost without the owner's Mask is the altar's");
        Expect(AllyKinds.Of("LemurianBody", false, true, false) == AllySource.None, "the owner's Mask alone doesn't make a ghost");
        Expect(!AllyKinds.UseSubtitle(true, false), "a ghost of a boss doesn't show the boss's subtitle");
        Expect(!AllyKinds.UseSubtitle(false, true), "a clone doesn't show its owner's subtitle");
        Expect(AllyKinds.UseSubtitle(false, false), "a modded summon may use its own");
    }

    private static void AllyKindsGoobo()
    {
        Expect(AllyKinds.Of("EngiBody", false, false, true) == AllySource.GummyClone, "a clone of Engineer");
        Expect(AllyKinds.Of("EngiTurretBody", false, false, true) == AllySource.GummyClone, "the marker wins over the body");
    }

    private static void AllyTokenText()
    {
        WithFallbackLanguage(
            new Dictionary<string, string> { ["ITEM_X_PICKUP"] = "Short" },
            new Dictionary<string, string> { ["ITEM_X_PICKUP"] = "Short", ["ITEM_X_DESC"] = "Full" },
            () =>
            {
                Equal("Full", ItemStatText.TokenText("ITEM_X_DESC", "ITEM_X_PICKUP", true));
                Equal("Short", ItemStatText.TokenText("ITEM_X_DESC", "ITEM_X_PICKUP", false));
                Equal("Full", ItemStatText.TokenText("ITEM_X_DESC", null, false));
                Equal("", ItemStatText.TokenText("ITEM_Y_DESC", "ITEM_Y_PICKUP", true));
                Equal("", ItemStatText.TokenText(null, null, true));
            });
    }

    private static System.Func<string, bool> Known(params string[] tokens)
    {
        var known = new HashSet<string>(tokens);
        return known.Contains;
    }

    private static void AllyFullOrder()
    {
        Expect(ItemStatText.DroneToken("SKILL", "DESC", "SHORT", true, Known("SKILL", "DESC", "SHORT")) == "SKILL", "skill text first");
        Expect(ItemStatText.DroneToken("SKILL", "DESC", "SHORT", true, Known("DESC", "SHORT")) == "DESC", "then the description");
        Expect(ItemStatText.DroneToken("", "DESC", "SHORT", true, Known("", "DESC")) == "DESC", "an empty token is skipped");
        Expect(ItemStatText.DroneToken("SKILL", "DESC", "SHORT", false, Known("SKILL", "DESC", "SHORT")) == "SHORT", "short text keeps the one-liner");
    }

    private static void AllyBomberFallback()
    {
        System.Func<string, bool> bomber = Known("DRONE_BOMBER_SKILL_DESCRIPTION", "DRONE_BOMBER_DESCRIPTION");
        Expect(ItemStatText.DroneToken("DRONE_BOMBER_SKILL_DESCRIPTION", "DRONE_BOMBER_DESCRIPTION", "DRONE_BOMBER_FUNCTION", false, bomber)
            == "DRONE_BOMBER_DESCRIPTION", "short text falls back to the description");
        Expect(ItemStatText.DroneToken(null, null, "DRONE_X_FUNCTION", false, Known()) == null, "nothing known gives nothing");
    }

    private static void AllyNoText()
    {
        Expect(ItemStatText.DroneToken("A", "B", "C", true, Known()) == null, "full text with nothing known");
        Expect(ItemStatText.DroneToken(null, null, null, false, Known()) == null, "all missing");
    }

    private sealed class FakeCard
    {
        internal bool Destroyed;
        internal bool Active = true;
    }

    private static void AllyCardsRefresh()
    {
        var list = new CardList<FakeCard>(card => !card.Destroyed);
        var shown = new FakeCard();
        var hidden = new FakeCard();
        list.Add(shown);
        list.Add(hidden);
        hidden.Active = false;
        List<FakeCard> live = list.Live();
        Expect(live.Count == 2, "both cards refresh");
        Expect(live.Contains(hidden), "the inactive card refreshes");
    }

    private static void AllyCardsPrune()
    {
        var list = new CardList<FakeCard>(card => !card.Destroyed);
        var first = new FakeCard();
        Expect(list.Add(first), "a new card is kept");
        Expect(!list.Add(first), "the same card isn't kept twice");
        first.Destroyed = true;
        var next = new FakeCard();
        list.Add(next);
        Expect(!list.Contains(first), "adding prunes a destroyed card");
        next.Destroyed = true;
        Expect(list.Live().Count == 0, "refreshing prunes a destroyed card");
        Expect(!list.Add(null!), "a missing card isn't kept");
    }
}
