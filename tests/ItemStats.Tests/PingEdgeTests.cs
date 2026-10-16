using System.Collections;
using System.Collections.Generic;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;

// Ping Info at its edges, after FillCatalog so the void pair resolves
// The game's queue can't run outside Unity, so GameQueue copies CharacterMasterNotificationQueue's IL for
// PushNotification and FixedUpdate, and merges with the game's own NotificationInfo ==
internal static partial class Program
{
    private static void RunPingEdge()
    {
        Test("Ping edge: the game counts a ping card and a pickup card of one item as equal", PingEdgeGameEquality);
        Test("Ping edge: marking a card doesn't mark an equal one", PingEdgeMarkByReference);
        Test("Ping edge: marking null or twice is harmless", PingEdgeMarkNullTwice);
        Test("Ping edge: picking up the pinged item while its card is up reads what the pickup did", PingEdgeThenPickup);
        Test("Ping edge: picking up the pinged item while its card is up still gets a pickup card", PingEdgeThenPickupOwnCard);
        Test("Ping edge: pinging what was just picked up leaves the pickup card as it is", PingEdgeAfterPickup);
        Test("Ping edge: a second ping replaces the first on the next tick", PingEdgeSecondPing);
        Test("Ping edge: pickups queued behind a ping keep their order", PingEdgeQueuedPickupsKeepOrder);
        Test("Ping edge: a ping card alone isn't cut by its own push", PingEdgeAloneNotCut);
        Test("Ping edge: a cut front card goes on the next tick, not the same one", PingEdgeCutNextTick);
        Test("Ping edge: pinging a void item when holding its original counts the stacks it would take", PingEdgeVoidTakesOriginals);
        Test("Ping edge: pinging an item held as its void item shows the void item it adds to", PingEdgeCorruptedAddsToVoid);
        Test("Ping edge: a ping card for a holder with no inventory has no stats", PingEdgeNoInventory);
    }

    // CharacterMasterNotificationQueue as its IL has it, with Run.fixedTime passed in
    private sealed class GameQueue
    {
        internal readonly List<Timed> Cards = new List<Timed>();

        internal int Changes;

        internal void Push(NotificationInfo info, float duration, float now)
        {
            if (Cards.Count == 0 || Cards[Cards.Count - 1].Info != info)
            {
                Cards.Add(new Timed(info, now, duration));
                if (Cards.Count == 1)
                {
                    Changes++;
                }
            }
        }

        internal void FixedUpdate(float now)
        {
            if (Cards.Count > 0 && (now - Cards[0].Start) / Cards[0].Duration > 1f)
            {
                Cards.RemoveAt(0);
                if (Cards.Count > 0)
                {
                    Cards[0].Start = now;
                }

                Changes++;
            }
        }

        internal NotificationInfo? Current => Cards.Count > 0 ? Cards[0].Info : null;
    }

    private sealed class Timed
    {
        internal Timed(NotificationInfo info, float start, float duration)
        {
            Info = info;
            Start = start;
            Duration = duration;
        }

        internal NotificationInfo Info { get; }

        internal float Start { get; set; }

        internal float Duration { get; set; }
    }

    // PingDetails.Push and PickupDetails.OnPushNotification's ping branch, as written
    private static Timed? PingPush(GameQueue queue, PingCards pings, NotificationInfo info, float now)
    {
        int before = queue.Cards.Count;
        pings.Pushing = true;
        pings.Add(info);
        queue.Push(info, 6f, now);
        pings.Pushing = false;
        if (queue.Cards.Count <= before)
        {
            return null;
        }

        Timed card = queue.Cards[queue.Cards.Count - 1];
        card.Duration = 5f;
        PingQueue.BringForward((IList)queue.Cards, card, (c, s) => ((Timed)c).Duration = s);
        return card;
    }

    // PickupDetails.OnPushNotification's pickup branch then NoteQueued, as written
    private static void PickupPush(GameQueue queue, PickupChanges<NotificationInfo> changes, NotificationInfo info, float now, PingCards? pings = null)
    {
        int before = queue.Cards.Count;
        queue.Push(info, 6f, now);
        bool merged = queue.Cards.Count == before && before > 0;
        NotificationInfo card = merged ? queue.Cards[before - 1].Info : info;
        if (merged)
        {
            pings?.PickedUp(card);
        }

        changes.CardQueued(card, (int)((ItemDef)info.data).itemIndex, now, merged);
    }

    // PickupDetails.TextFor, as written
    private static string CardText(PingCards pings, PickupChanges<NotificationInfo> changes, NotificationInfo info, CharacterMaster master)
    {
        if (pings.Contains(info))
        {
            return ItemStatText.DescribePing(info.data, master, "Bear_PICKUP", false, true, " → ")!;
        }

        (int, int)? change = changes.TryGet(info, out int before, out int after) ? (before, after) : null;
        return ItemStatText.DescribePickup(info.data, master, "Bear_PICKUP", false, true, " → ", change)!;
    }

    private static NotificationInfo Card(object data)
    {
        return new NotificationInfo(data, null, false, 0);
    }

    private static void PingEdgeGameEquality()
    {
        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        NotificationInfo pickup = new NotificationInfo(RoR2Content.Items.Bear, null, null, false, true, 3);
        Expect(ping == pickup, "same data, other fields ignored, so PushNotification folds one into the other");
        Expect(!(ping != pickup), "PickupDetails.OnMatched's info != shownInfo is false for them too");
        Expect(ping != Card(RoR2Content.Items.Syringe), "other items differ");
    }

    private static void PingEdgeMarkByReference()
    {
        var pings = new PingCards();
        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        NotificationInfo pickup = Card(RoR2Content.Items.Bear);
        pings.Add(ping);
        Expect(pings.Contains(ping), "marked");
        Expect(!pings.Contains(pickup), "an equal pickup card isn't a ping card");
    }

    private static void PingEdgeMarkNullTwice()
    {
        var pings = new PingCards();
        pings.Add(null!);
        Expect(!pings.Contains(null), "null");
        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        pings.Add(ping);
        pings.Add(ping);
        Expect(pings.Contains(ping), "marked twice");
    }

    // Ping a Tougher Times with 6 held, walk over and take it within the card's 5 s
    // The game folds the pickup into the ping card and NoteQueued pairs 6 to 7 with it
    // Failed while the card stayed a ping card, so it read one more than held and jumped to 7 to 8
    private static void PingEdgeThenPickup()
    {
        Utils.CapChance = true;
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo? redrawn = null;
        changes.Matched += c => redrawn = c;

        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        Expect(PingPush(queue, pings, ping, 10f) != null, "ping card queued");
        NotificationInfo shown = queue.Current!;

        changes.CountChanged((int)RoR2Content.Items.Bear.itemIndex, 6, 7, 11f);
        PickupPush(queue, changes, Card(RoR2Content.Items.Bear), 11f, pings);
        Expect(queue.Cards.Count == 1, $"{queue.Cards.Count} cards, the pickup folded into the ping card");
        Expect(redrawn != null && !(redrawn != shown), "the shown card is redrawn");

        string s = CardText(pings, changes, shown, Holding((RoR2Content.Items.Bear, 7)));
        Contains("<style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>", s);
    }

    private static void PingEdgeThenPickupOwnCard()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        PingPush(queue, pings, Card(RoR2Content.Items.Bear), 10f);
        int before = queue.Cards.Count;
        PickupPush(queue, changes, Card(RoR2Content.Items.Bear), 11f, pings);
        bool pickupShows = queue.Cards.Count > before || !pings.Contains(queue.Cards[queue.Cards.Count - 1].Info);
        Expect(pickupShows, "the pickup has no card of its own, only the ping card it folded into");
    }

    // The reverse is the designed case, a ping adds nothing and the pickup card keeps its pairing
    private static void PingEdgeAfterPickup()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo pickup = Card(RoR2Content.Items.Bear);
        changes.CountChanged((int)RoR2Content.Items.Bear.itemIndex, 6, 7, 10f);
        PickupPush(queue, changes, pickup, 10f);
        Expect(PingPush(queue, pings, Card(RoR2Content.Items.Bear), 11f) == null, "nothing added");
        Expect(queue.Cards.Count == 1 && queue.Cards[0].Duration == 6f, "pickup card untouched");
        Expect(!pings.Contains(queue.Current), "the shown card isn't a ping card");
        Contains(
            "<style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>",
            CardText(pings, changes, queue.Current!, Holding((RoR2Content.Items.Bear, 7))));
    }

    private static void PingEdgeSecondPing()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        NotificationInfo a = Card(RoR2Content.Items.Bear);
        NotificationInfo b = Card(RoR2Content.Items.Syringe);
        PingPush(queue, pings, a, 10f);
        queue.FixedUpdate(11f);
        PingPush(queue, pings, b, 11f);
        queue.FixedUpdate(11.02f);
        Expect(ReferenceEquals(queue.Current, b), "b showing");
        Expect(queue.Cards.Count == 1, $"{queue.Cards.Count} cards");
        queue.FixedUpdate(11.02f + 4.9f);
        Expect(ReferenceEquals(queue.Current, b), "b lasts its 5 s");
        queue.FixedUpdate(11.02f + 5.1f);
        Expect(queue.Current == null, "then goes");
    }

    private static void PingEdgeQueuedPickupsKeepOrder()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo x = Card(RoR2Content.Items.Syringe);
        NotificationInfo y = Card(RoR2Content.Items.Hoof);
        PickupPush(queue, changes, x, 10f);
        PickupPush(queue, changes, y, 10f);
        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        PingPush(queue, pings, ping, 10.5f);
        queue.FixedUpdate(10.52f);
        Expect(ReferenceEquals(queue.Current, ping), "ping showing");
        queue.FixedUpdate(10.52f + 5.1f);
        Expect(ReferenceEquals(queue.Current, y), "then the pickup behind it");
    }

    private static void PingEdgeAloneNotCut()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        Timed card = PingPush(queue, pings, Card(RoR2Content.Items.Bear), 10f)!;
        Expect(card.Duration == 5f, $"duration {card.Duration}");
        Expect(queue.Changes == 1, "built once, inside the push");
    }

    private static void PingEdgeCutNextTick()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        PickupPush(queue, changes, Card(RoR2Content.Items.Syringe), 10f);
        NotificationInfo ping = Card(RoR2Content.Items.Bear);
        PingPush(queue, pings, ping, 10f);
        queue.FixedUpdate(10f);
        Expect(queue.Cards.Count == 2, "a card pushed this tick isn't dropped on it");
        queue.FixedUpdate(10f + 1f / 60f);
        Expect(ReferenceEquals(queue.Current, ping), "the ping card shows a tick later");
    }

    // Taking a Safer Spaces turns every Tougher Times into one, so with 3 held it goes to 4, not 1
    private static void PingEdgeVoidTakesOriginals()
    {
        CharacterMaster m = Holding((RoR2Content.Items.Bear, 3));
        string s = ItemStatText.DescribePing(DLC1Content.Items.BearVoid, m, "BearVoid_PICKUP", false, true, " → ")!;
        Contains("corrupts your 3", s);
    }

    private static void PingEdgeCorruptedAddsToVoid()
    {
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 2));
        string s = ItemStatText.DescribePing(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ")!;
        Contains("Corrupted by", s);
    }

    private static void PingEdgeNoInventory()
    {
        CharacterMaster m = Master(0);
        AliveField.SetValue(m, (System.IntPtr)1);
        string s = ItemStatText.DescribePing(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ")!;
        Lacks("→", s);
        Lacks("%", s);
    }
}
