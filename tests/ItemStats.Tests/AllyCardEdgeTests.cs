using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;

// Ally Card Info at its edges
// AllySim mirrors AllyCardDetails.OnUpdateInfo, Update and OnSettingChanged as written, over the real CardList
// A change to those has to be mirrored in AllySim
internal static partial class Program
{
    private static void RunAllyCardEdge()
    {
        Test("Ally card edge: short text the current language lacks still shows its English fallback", AllyEdgeFallbackShort);
        Test("Ally card edge: full text the current language lacks still shows its English fallback", AllyEdgeFallbackFull);
        Test("Ally card edge: a token registered with no text falls through to the next", AllyEdgeEmptyRegistered);
        Test("Ally card edge: a ping with short text keeps the game's text", AllyEdgePingShort);
        Test("Ally card edge: a ping with full text swaps the skill text in for the one-liner", AllyEdgePingFull);
        Test("Ally card edge: a ping of a drone with no known text keeps the game's text, no raw key", AllyEdgePingNothingKnown);
        Test("Ally card edge: a ping of a drone with only its one-liner keeps the game's text", AllyEdgePingOnlyShort);
        Test("Ally card edge: a ping of a drone with no short token doesn't throw", AllyEdgePingNullShortToken);
        Test("Ally card edge: a card seen during a refresh joins the list without breaking it", AllyEdgeAddDuringRefresh);
        Test("Ally card edge: destroyed cards that compare equal stay told apart", AllyEdgeDestroyedEqual);
        Test("Ally card edge: a card first seen with the setting off gets its tooltip once it's on", AllyEdgeOffThenOn);
        Test("Ally card edge: a card reused for a player then another drone follows each", AllyEdgeReuse);
        Test("Ally card edge: someone else's tooltip is never touched", AllyEdgeForeignTooltip);
        Test("Ally card edge: fifty quick toggles end right with one tooltip per card", AllyEdgeRapidToggle);
        Test("Ally card edge: Full Descriptions toggled while Ally Card Info is off shows nothing", AllyEdgeFullWhileOff);
        Test("Ally card edge: the bag overlay's card never gets a tooltip", AllyEdgeBagCard);
        Test("Ally card edge: a card from a destroyed HUD drops out of the refresh", AllyEdgeOldHud);
        Test("Ally card edge: one card failing its refresh doesn't stop the rest", AllyEdgeOneCardThrows);
        Test("Ally card edge: our tooltip destroyed by someone else is replaced on the next refresh", AllyEdgeOursDestroyed);
    }

    // Swaps in a language with the given strings that falls back to an English one, as every game language does
    private static void WithFallbackLanguage(Dictionary<string, string> current, Dictionary<string, string> english, Action body)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo strings = typeof(Language).GetField("stringsByToken", flags)!;
        var fallback = (Language)RuntimeHelpers.GetUninitializedObject(typeof(Language));
        strings.SetValue(fallback, english);
        var language = (Language)RuntimeHelpers.GetUninitializedObject(typeof(Language));
        strings.SetValue(language, current);
        typeof(Language).GetField("fallbackLanguage", flags)!.SetValue(language, fallback);
        FieldInfo field = typeof(Language).GetField("<currentLanguage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? old = field.GetValue(null);
        field.SetValue(null, language);
        try
        {
            body();
        }
        finally
        {
            field.SetValue(null, old);
        }
    }

    private static DroneDef TestDrone(string? skill, string? full, string? brief)
    {
        var drone = (DroneDef)RuntimeHelpers.GetUninitializedObject(typeof(DroneDef));
        drone.nameToken = "DRONE_TEST_BODY_NAME";
        drone.skillDescriptionToken = skill!;
        drone.descriptionToken = full!;
        drone.pickupToken = brief!;
        return drone;
    }

    private const string TestSkill = "Fires a 4-round burst for 140% damage.";
    private const string TestDesc = "A companion that follows you and shoots.";
    private const string TestShort = "Shoots nearby enemies.";

    private static DroneDef StandardDrone() => TestDrone("DRONE_TEST_SKILL_DESCRIPTION", "DRONE_TEST_DESCRIPTION", "DRONE_TEST_FUNCTION");

    private static Dictionary<string, string> AllDroneText() => new()
    {
        ["DRONE_TEST_SKILL_DESCRIPTION"] = TestSkill,
        ["DRONE_TEST_DESCRIPTION"] = TestDesc,
        ["DRONE_TEST_FUNCTION"] = TestShort,
    };

    // A mod that ships its drone's text in en only, played in another language
    // Language.GetString finds it through the fallback but IsTokenInvalid only asks the current language
    private static void AllyEdgeFallbackShort()
    {
        WithFallbackLanguage(new(), AllDroneText(), () =>
        {
            Equal(TestShort, Language.GetString("DRONE_TEST_FUNCTION"));
            Equal(TestShort, ItemStatText.DroneText(StandardDrone(), false));
        });
    }

    private static void AllyEdgeFallbackFull()
    {
        WithFallbackLanguage(new(), AllDroneText(), () => Equal(TestSkill, ItemStatText.DroneText(StandardDrone(), true)));
    }

    private static void AllyEdgeEmptyRegistered()
    {
        var strings = new Dictionary<string, string> { ["DRONE_TEST_FUNCTION"] = string.Empty, ["DRONE_TEST_DESCRIPTION"] = TestDesc };
        WithLanguage(strings, () => Equal(TestDesc, ItemStatText.DroneText(StandardDrone(), false)));
    }

    private static void AllyEdgePingShort()
    {
        WithLanguage(AllDroneText(), () =>
        {
            string game = TestShort + "\nCosts 40 gold.";
            Equal(game, ItemStatText.DescribePickup(StandardDrone(), null, game, false, true, " → ", null)!);
            Equal(game, ItemStatText.DescribePing(StandardDrone(), null, game, false, true, " → ")!);
        });
    }

    private static void AllyEdgePingFull()
    {
        WithLanguage(AllDroneText(), () =>
        {
            string game = TestShort + "\nCosts 40 gold.";
            Equal(TestSkill + "\nCosts 40 gold.", ItemStatText.DescribePing(StandardDrone(), null, game, true, true, " → ")!);
        });
    }

    private static void AllyEdgePingNothingKnown()
    {
        WithLanguage(new(), () =>
        {
            string game = "DRONE_TEST_FUNCTION";
            Equal(game, ItemStatText.DescribePing(StandardDrone(), null, game, true, true, " → ")!);
            Equal("Some other card text", ItemStatText.DescribePing(StandardDrone(), null, "Some other card text", true, true, " → ")!);
        });
    }

    private static void AllyEdgePingOnlyShort()
    {
        WithLanguage(new() { ["DRONE_TEST_FUNCTION"] = TestShort }, () =>
        {
            string game = TestShort + "\nCosts 40 gold.";
            Equal(game, ItemStatText.DescribePing(StandardDrone(), null, game, true, true, " → ")!);
        });
    }

    // DroneToken skips a null token on purpose, but SwapIn then asks Language for the null one
    private static void AllyEdgePingNullShortToken()
    {
        WithLanguage(new() { ["DRONE_TEST_SKILL_DESCRIPTION"] = TestSkill }, () =>
        {
            DroneDef drone = TestDrone("DRONE_TEST_SKILL_DESCRIPTION", null, null);
            Equal(TestSkill, ItemStatText.DescribePing(drone, null, "Game text", true, true, " → ")!);
        });
    }

    private static void AllyEdgeAddDuringRefresh()
    {
        var list = new CardList<FakeCard>(card => !card.Destroyed);
        var first = new FakeCard();
        var second = new FakeCard();
        list.Add(first);
        list.Add(second);
        var late = new FakeCard();
        int visited = 0;
        foreach (FakeCard card in list.Live())
        {
            visited++;
            list.Add(late);
            second.Destroyed = true;
        }

        Expect(visited == 2, "the refresh walks the cards it started with");
        List<FakeCard> after = list.Live();
        Expect(after.Count == 2 && after.Contains(first) && after.Contains(late), "the late card is kept and the destroyed one dropped");
    }

    // == follows UnityEngine.Object, so two destroyed cards are equal and a destroyed one equals null
    private sealed class UnityCard
    {
        internal bool Destroyed;

        private static bool Dead(UnityCard? c) => c is null || c.Destroyed;

        public static bool operator ==(UnityCard? a, UnityCard? b) => Dead(a) && Dead(b) || !Dead(a) && !Dead(b) && ReferenceEquals(a, b);

        public static bool operator !=(UnityCard? a, UnityCard? b) => !(a == b);

        public override bool Equals(object? obj) => obj is UnityCard c && this == c;

        public override int GetHashCode() => 0;
    }

    private static void AllyEdgeDestroyedEqual()
    {
        var list = new CardList<UnityCard>(card => card != null);
        var gone = new UnityCard();
        var other = new UnityCard();
        list.Add(gone);
        gone.Destroyed = true;
        other.Destroyed = true;
        Expect(gone == other, "the fake compares the way Unity does");
        Expect(!list.Contains(other), "a different destroyed card isn't taken for the kept one");
        var live = new UnityCard();
        Expect(list.Add(live), "a live card is kept");
        Expect(list.Live().Count == 1, "the destroyed one is pruned");
    }

    private sealed class SimTooltip
    {
        internal bool Enabled = true;
        internal bool Destroyed;
        internal string Title = string.Empty;
        internal string Body = string.Empty;
    }

    private sealed class SimAllyCard
    {
        internal bool Destroyed;
        internal bool UnderManager = true;
        internal bool HasCanvas = true;
        internal bool OrigThrows;
        internal string? Drone;
        internal string Name = string.Empty;

        // AllyText.Describe throwing for this ally, say a modded body with no name token
        internal bool DescribeThrows;

        // A body with no text, reported back the way Describe's unknownBody is
        internal string? UnknownBody;
        internal readonly List<SimTooltip> Tooltips = new();

        internal void Ally(string? drone, string name)
        {
            Drone = drone;
            Name = name;
        }
    }

    // AllyCardDetails over fake cards, a drone's text is its name plus which length was asked for
    private sealed class AllySim
    {
        private readonly CardList<SimAllyCard> cards = new(card => !card.Destroyed);
        private readonly Dictionary<SimAllyCard, SimTooltip> tooltips = new(ReferenceEqualityComparer.Instance);
        private bool cardInfo = true;
        private bool full = true;

        private readonly HashSet<string> unknownBodies = new(StringComparer.Ordinal);

        internal int Failures;

        internal readonly List<string> Log = new();

        internal bool CardInfo
        {
            set
            {
                if (cardInfo != value)
                {
                    cardInfo = value;
                    SettingChanged();
                }
            }
        }

        internal bool Full
        {
            set
            {
                if (full != value)
                {
                    full = value;
                    SettingChanged();
                }
            }
        }

        internal static string Text(string drone, bool fullText) => drone + (fullText ? " full" : " short");

        // Stands in for AllyText.Describe, empty for a player or an ally with no text
        private string Describe(SimAllyCard card, out string? unknownBody)
        {
            unknownBody = null;
            if (card.DescribeThrows)
            {
                throw new ArgumentNullException("token");
            }

            if (card.Drone == null)
            {
                unknownBody = card.UnknownBody;
                return string.Empty;
            }

            return Text(card.Drone, full);
        }

        internal SimTooltip? Ours(SimAllyCard card) => tooltips.TryGetValue(card, out SimTooltip? t) && !t.Destroyed ? t : null;

        // OnUpdateInfo, orig runs outside the try
        internal void UpdateInfo(SimAllyCard card)
        {
            if (card.OrigThrows)
            {
                throw new InvalidOperationException("orig failed");
            }

            try
            {
                Update(card);
            }
            catch (Exception)
            {
                if (!card.Destroyed && tooltips.TryGetValue(card, out SimTooltip? tooltip) && !tooltip.Destroyed)
                {
                    tooltip.Enabled = false;
                }

                Failures++;
            }
        }

        private void Update(SimAllyCard card)
        {
            if (card.Destroyed)
            {
                return;
            }

            if (!cards.Contains(card))
            {
                if (!card.UnderManager)
                {
                    return;
                }

                cards.Add(card);
            }

            // Describe runs before the tooltip is touched, as in AllyCardDetails.Update
            string? unknownBody = null;
            string body = cardInfo ? Describe(card, out unknownBody) : string.Empty;
            if (!string.IsNullOrEmpty(unknownBody) && unknownBodies.Add(unknownBody!))
            {
                Log.Add("No text for the ally " + unknownBody);
            }

            SimTooltip? tooltip = Ours(card);
            bool ours = tooltip != null;
            if (body.Length == 0)
            {
                if (ours)
                {
                    tooltip!.Enabled = false;
                }

                return;
            }

            if (!ours)
            {
                if (card.Tooltips.Exists(t => !t.Destroyed))
                {
                    return;
                }

                if (!card.HasCanvas)
                {
                    return;
                }

                tooltip = new SimTooltip();
                card.Tooltips.Add(tooltip);
                tooltips[card] = tooltip;
            }

            tooltip!.Enabled = true;
            tooltip.Title = card.Name;
            tooltip.Body = body;
        }

        private void SettingChanged()
        {
            foreach (SimAllyCard card in cards.Live())
            {
                try
                {
                    UpdateInfo(card);
                }
                catch (Exception)
                {
                    Failures++;
                }
            }
        }
    }

    private static SimAllyCard SeenCard(AllySim sim, string? drone, string name)
    {
        var card = new SimAllyCard();
        card.Ally(drone, name);
        sim.UpdateInfo(card);
        return card;
    }

    private static void AllyEdgeOffThenOn()
    {
        var sim = new AllySim { CardInfo = false };
        SimAllyCard card = SeenCard(sim, "Gunner", "Gunner Drone");
        Expect(card.Tooltips.Count == 0, "no tooltip while off");
        sim.CardInfo = true;
        SimTooltip? t = sim.Ours(card);
        Expect(t != null && t.Enabled && t.Body == AllySim.Text("Gunner", true) && t.Title == "Gunner Drone", "the tooltip arrives on the toggle");
    }

    private static void AllyEdgeReuse()
    {
        var sim = new AllySim();
        SimAllyCard card = SeenCard(sim, "Gunner", "Gunner Drone");
        card.Ally(null, "Player Two");
        sim.UpdateInfo(card);
        Expect(!sim.Ours(card)!.Enabled, "a player's card shows nothing");
        card.Ally("Healing", "Healing Drone");
        sim.UpdateInfo(card);
        SimTooltip t = sim.Ours(card)!;
        Expect(t.Enabled && t.Body == AllySim.Text("Healing", true) && t.Title == "Healing Drone", "the next drone's text, not the first one's");
        Expect(card.Tooltips.Count == 1, "the tooltip is reused, not stacked");
    }

    private static void AllyEdgeForeignTooltip()
    {
        var sim = new AllySim();
        var card = new SimAllyCard();
        card.Ally("Gunner", "Gunner Drone");
        var foreign = new SimTooltip { Body = "theirs", Enabled = true };
        card.Tooltips.Add(foreign);
        sim.UpdateInfo(card);
        sim.CardInfo = false;
        sim.CardInfo = true;
        sim.Full = false;
        Expect(foreign.Enabled && foreign.Body == "theirs" && card.Tooltips.Count == 1, "left exactly as it was");
    }

    private static void AllyEdgeRapidToggle()
    {
        var sim = new AllySim();
        SimAllyCard a = SeenCard(sim, "Gunner", "Gunner Drone");
        SimAllyCard b = SeenCard(sim, null, "Player Two");
        for (int i = 0; i < 50; i++)
        {
            sim.CardInfo = i % 2 == 0 ? false : true;
            sim.Full = i % 3 == 0;
        }

        sim.CardInfo = true;
        sim.Full = false;
        Expect(a.Tooltips.Count == 1 && sim.Ours(a)!.Enabled && sim.Ours(a)!.Body == AllySim.Text("Gunner", false), "the drone ends on short text");
        Expect(b.Tooltips.Count == 0, "the player's card never got one");
        Expect(sim.Failures == 0, "no failures");
    }

    private static void AllyEdgeFullWhileOff()
    {
        var sim = new AllySim();
        SimAllyCard card = SeenCard(sim, "Gunner", "Gunner Drone");
        sim.CardInfo = false;
        sim.Full = false;
        sim.Full = true;
        Expect(!sim.Ours(card)!.Enabled, "stays off");
    }

    private static void AllyEdgeBagCard()
    {
        var sim = new AllySim();
        var bag = new SimAllyCard { UnderManager = false };
        bag.Ally("Gunner", "Gunner Drone");
        sim.UpdateInfo(bag);
        sim.CardInfo = false;
        sim.CardInfo = true;
        Expect(bag.Tooltips.Count == 0, "never touched");
    }

    private static void AllyEdgeOldHud()
    {
        var sim = new AllySim();
        SimAllyCard old = SeenCard(sim, "Gunner", "Gunner Drone");
        old.Destroyed = true;
        SimAllyCard next = SeenCard(sim, "Gunner", "Gunner Drone");
        old.Drone = "Healing";
        sim.CardInfo = false;
        Expect(!sim.Ours(next)!.Enabled, "the new HUD's card follows the setting");
        Expect(sim.Ours(old)!.Enabled && sim.Failures == 0, "the destroyed card wasn't refreshed");
    }

    // Another mod's UpdateInfo hook below ours, or the game's own code, throwing on one card
    private static void AllyEdgeOneCardThrows()
    {
        var sim = new AllySim();
        SimAllyCard first = SeenCard(sim, "Gunner", "Gunner Drone");
        SimAllyCard broken = SeenCard(sim, "Healing", "Healing Drone");
        SimAllyCard last = SeenCard(sim, "Missile", "Missile Drone");
        broken.OrigThrows = true;
        sim.CardInfo = false;
        Expect(!sim.Ours(first)!.Enabled, "the first card turned off");
        Expect(!sim.Ours(last)!.Enabled, "the card after the failing one turned off too");
    }

    private static void AllyEdgeOursDestroyed()
    {
        var sim = new AllySim();
        SimAllyCard card = SeenCard(sim, "Gunner", "Gunner Drone");
        card.Tooltips[0].Destroyed = true;
        sim.Full = false;
        SimTooltip? t = sim.Ours(card);
        Expect(t != null && t.Enabled && t.Body == AllySim.Text("Gunner", false), "a fresh tooltip with the new text");
    }
}
