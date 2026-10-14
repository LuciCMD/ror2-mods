using JustBeBetter.Notifications;

// Pickup Notifications: which cards are left out and which take the chosen length
internal static partial class Program
{
    private static void RunNotifications()
    {
        Test("Cards: pickups hide only with the setting off, a ping's card always shows", CardsHide);
        Test("Cards: only the game's 6 s cards take the chosen length", CardsDuration);
        Test("Cards: a ping keeps its own length whatever the setting", CardsPingDuration);
        Test("Cards: a length outside the range is held to it, a broken one leaves the game's", CardsDurationRange);
    }

    private static void CardsHide()
    {
        Expect(!CardRules.Hide(true, false), "a pickup was hidden with Show Pickup Cards on");
        Expect(CardRules.Hide(false, false), "a pickup showed with Show Pickup Cards off");
        Expect(!CardRules.Hide(false, true), "a ping's card was hidden with Show Pickup Cards off");
        Expect(!CardRules.Hide(true, true), "a ping's card was hidden with Show Pickup Cards on");
    }

    private static void CardsDuration()
    {
        Expect(CardRules.Duration(6f, 10f, false) == 10f, "a 6 s card didn't take the chosen 10 s");
        Expect(CardRules.Duration(6f, 2.5f, false) == 2.5f, "a 6 s card didn't take the chosen 2.5 s");
        Expect(CardRules.Duration(12f, 3f, false) == 12f, "a trial card's own 12 s was changed");
        Expect(CardRules.Duration(5f, 3f, false) == 5f, "a 5 s card was changed");
    }

    private static void CardsPingDuration()
    {
        Expect(CardRules.Duration(6f, 20f, true) == 6f, "a ping's card took the chosen length");
    }

    private static void CardsDurationRange()
    {
        Expect(CardRules.Duration(6f, 0f, false) == CardRules.MinSeconds, "a 0 s length wasn't raised to the minimum");
        Expect(CardRules.Duration(6f, -4f, false) == CardRules.MinSeconds, "a negative length wasn't raised to the minimum");
        Expect(CardRules.Duration(6f, 999f, false) == CardRules.MaxSeconds, "a huge length wasn't held to the maximum");
        Expect(CardRules.Duration(6f, float.NaN, false) == 6f, "a broken length didn't leave the game's 6 s");
        Expect(
            CardRules.Duration(6f, float.PositiveInfinity, false) == CardRules.MaxSeconds,
            "an endless length wasn't held to the maximum");
    }
}
