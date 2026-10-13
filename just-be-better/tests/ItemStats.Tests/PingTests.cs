using System.Collections;
using System.Collections.Generic;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;

// Ping Info: the card's text and how its card jumps the queue
internal static partial class Program
{
    private static void RunPing()
    {
        Test("Ping card: a pinged Tougher Times with 6 held reads 47.37% → 51.22%", PingNextStack);
        Test("Ping card: a pinged item you don't hold shows its first stack", PingFirstStack);
        Test("Ping card: a pinged item your void item corrupts shows the void item it adds to", PingCorrupted);
        Test("Ping card: equipment keeps the game's card text with its cooldown", PingEquipment);
        Test("Corruption card: the void item's card gets its stats, before → after", CorruptionCardStats);
        Test("Ping queue: a card alone in the queue is left showing", PingQueueAlone);
        Test("Ping queue: behind one card, the front card is cut short", PingQueueBehindOne);
        Test("Ping queue: at the back of a long queue, it moves to second and the front is cut short", PingQueueLong);
        Test("Ping queue: a card that isn't queued changes nothing", PingQueueMissing);
    }

    // DescribePickup alone takes the latest stack, 5 to 6 here, which is wrong for something not picked up
    private static void PingNextStack()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((RoR2Content.Items.Bear, 6));
        string s = ItemStatText.DescribePing(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ")!;
        Contains("<style=cIsUtility>47.37%</style> → <style=cIsUtility>51.22%</style>", s);
    }

    private static void PingFirstStack()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((RoR2Content.Items.Syringe, 1));
        string s = ItemStatText.DescribePing(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ")!;
        Contains("<style=cIsUtility>13.04%</style>", s);
    }

    // Failed while ping cards went through DescribePickup, which gives a corrupted item its text alone
    private static void PingCorrupted()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 2));
        string s = ItemStatText.DescribePing(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ")!;
        Contains("Corrupted by", s);
        Contains("→", s);
    }

    private static void PingEquipment()
    {
        string s = ItemStatText.DescribePing(Launcher(), null, "CommandMissile_PICKUP", false, true, " → ")!;
        Contains("Cooldown: <style=cIsUtility>45 s</style>", s);
    }

    // The game's corruption card has the void item as its data, PickupDetails used to skip it
    private static void CorruptionCardStats()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 2));
        string s = ItemStatText.DescribePickup(DLC1Content.Items.BearVoid, m, "BearVoid_PICKUP", false, true, " → ", null)!;
        Contains("→", s);
    }

    private static List<float> durations = new List<float>();

    private static IList Cards(params string[] names)
    {
        durations = new List<float>();
        foreach (string _ in names)
        {
            durations.Add(5f);
        }

        return new ArrayList(names);
    }

    // Durations are kept by position as the game keeps them on each card
    private static void SetDuration(IList queue, object card, float seconds)
    {
        durations[queue.IndexOf(card)] = seconds;
    }

    private static void PingQueueAlone()
    {
        IList q = Cards("ping");
        Expect(PingQueue.BringForward(q, "ping", (c, s) => SetDuration(q, c, s)), "brought forward");
        Equal("ping", string.Join(",", q.Names()));
        Expect(durations[0] == 5f, "the ping card keeps its time");
    }

    private static void PingQueueBehindOne()
    {
        IList q = Cards("pickup", "ping");
        Expect(PingQueue.BringForward(q, "ping", (c, s) => SetDuration(q, c, s)), "brought forward");
        Equal("pickup,ping", string.Join(",", q.Names()));
        Expect(durations[0] == PingQueue.CutShort, $"front card cut to {durations[0]}");
    }

    private static void PingQueueLong()
    {
        IList q = Cards("pickup", "a", "b", "ping");
        Expect(PingQueue.BringForward(q, "ping", (c, s) => SetDuration(q, c, s)), "brought forward");
        Equal("pickup,ping,a,b", string.Join(",", q.Names()));
        Expect(durations[0] == PingQueue.CutShort, $"front card cut to {durations[0]}");
    }

    private static void PingQueueMissing()
    {
        IList q = Cards("pickup", "a");
        Expect(!PingQueue.BringForward(q, "ping", (c, s) => SetDuration(q, c, s)), "not brought forward");
        Equal("pickup,a", string.Join(",", q.Names()));
        Expect(durations[0] == 5f, "front card untouched");
        Expect(!PingQueue.BringForward(null!, "ping", (c, s) => { }), "no queue");
    }

    private static IEnumerable<string> Names(this IList list)
    {
        foreach (object o in list)
        {
            yield return (string)o;
        }
    }
}
