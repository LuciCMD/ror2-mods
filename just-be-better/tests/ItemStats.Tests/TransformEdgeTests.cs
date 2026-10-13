using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using RoR2;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;
using TransformationInfo = RoR2.CharacterMasterNotificationQueue.TransformationInfo;
using TransformationType = RoR2.CharacterMasterNotificationQueue.TransformationType;

// Transformation cards and ping cards at their edges, after FillCatalog so Bear to BearVoid resolves
// The flows follow the game's IL, ContagiousItemManager turns every held original into the void item
// 0.5 s after the void item is held, in one inventory change, then sends one transform card
internal static partial class Program
{
    private static void RunTransformEdge()
    {
        Test("Transform edge: void pickup then its corruption, host order, pickup 0 → 1 and corruption card 1 → 4", TransformEdgeHostOrder);
        Test("Transform edge: void pickup then its corruption, client order, cards before their changes", TransformEdgeClientOrder);
        Test("Transform edge: the corruption card's text shows the 1 → 4 step", TransformEdgeCardText);
        Test("Transform edge: a corruption card never folds into the void item's pickup card", TransformEdgePickupAndTransformApart);
        Test("Transform edge: two equal Egocentrism cards in a row fold and span both steps", TransformEdgeMergedSpansBoth);
        Test("Transform edge: a corruption card behind a ping card of the void item stays apart and the ping stays a ping", TransformEdgeBehindPing);
        Test("Transform edge: ping the void item, pick it up, then the corruption, each card reads its own step", TransformEdgePingPickupCorrupt);
        Test("Transform edge: two upgrades into one item from different originals keep their own steps", TransformEdgeTwoOriginalsOneItem);
        Test("Transform edge: a broken watch card shows the consumed stacks", TransformEdgeConsumedWatch);
        Test("Transform edge: an upgrade into an item already corrupted shows text only", TransformEdgeUpgradeIntoCorrupted);
        Test("Transform edge: full text on a corruption card keeps the Corrupts line, via the description", TransformEdgeFullTextKeepsCorrupts);
        Test("Ping edge: a pinged Hex keeps the game's Hallowed Shrine hint, stats on", PingHexKeepsSuffixStats);
        Test("Ping edge: a pinged Hex keeps the game's Hallowed Shrine hint, full text on", PingHexKeepsSuffixFullText);
        Test("Ping edge: a ping card with no player shows the one-liner only", PingNoMaster);
        Test("Ping edge: a pinged void item held alongside an unconverted original counts both", PingVoidHeldAndOriginal);
    }

    private static NotificationInfo TransformCard(ItemDef to, ItemDef from, TransformationType type)
    {
        return new NotificationInfo(to, new TransformationInfo(type, from), false, 0);
    }

    private static int Index(ItemDef item) => (int)item.itemIndex;

    // Holding 3 Tougher Times, take a Safer Spaces, the server gives it at 10 s and corrupts the 3 at 10.5 s
    // On a host the inventory changes first and the cards come through the local connection a frame later
    private static void TransformEdgeHostOrder()
    {
        var queue = new GameQueue();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo pickup = Card(DLC1Content.Items.BearVoid);
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);

        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 10f);
        PickupPush(queue, changes, pickup, 10.02f);
        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 10.5f);
        PickupPush(queue, changes, corrupt, 10.52f);

        Expect(queue.Cards.Count == 2, $"{queue.Cards.Count} cards");
        ExpectChangeOf(changes, pickup, 0, 1);
        ExpectChangeOf(changes, corrupt, 1, 4);
    }

    private static void TransformEdgeClientOrder()
    {
        var queue = new GameQueue();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo pickup = Card(DLC1Content.Items.BearVoid);
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);

        PickupPush(queue, changes, pickup, 10f);
        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 10.1f);
        PickupPush(queue, changes, corrupt, 10.5f);
        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 10.6f);

        ExpectChangeOf(changes, pickup, 0, 1);
        ExpectChangeOf(changes, corrupt, 1, 4);
    }

    private static void TransformEdgeCardText()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 4));
        string s = ItemStatText.DescribePickup(DLC1Content.Items.BearVoid, m, "BearVoid_PICKUP", false, true, " → ", (1, 4))!;
        Contains(Stats(DLC1Content.Items.BearVoid, 1, 4, false), s);
    }

    private static void TransformEdgePickupAndTransformApart()
    {
        NotificationInfo pickup = Card(DLC1Content.Items.BearVoid);
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);
        Expect(pickup != corrupt, "the game's == tells them apart by the transformation");
    }

    // Egocentrism takes one item per tick, twice the same Syringe gives two equal cards that the game folds
    private static void TransformEdgeMergedSpansBoth()
    {
        var queue = new GameQueue();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo first = TransformCard(DLC1Content.Items.LunarSun, RoR2Content.Items.Syringe, TransformationType.LunarSun);
        NotificationInfo second = TransformCard(DLC1Content.Items.LunarSun, RoR2Content.Items.Syringe, TransformationType.LunarSun);
        changes.CountChanged(Index(DLC1Content.Items.LunarSun), 1, 2, 10f);
        PickupPush(queue, changes, first, 10.02f);
        changes.CountChanged(Index(DLC1Content.Items.LunarSun), 2, 3, 10.5f);
        PickupPush(queue, changes, second, 10.52f);
        Expect(queue.Cards.Count == 1, $"{queue.Cards.Count} cards, the second folded in");
        ExpectChangeOf(changes, first, 1, 3);
    }

    private static void TransformEdgeBehindPing()
    {
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo ping = Card(DLC1Content.Items.BearVoid);
        PingPush(queue, pings, ping, 10f);
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);
        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 2, 3, 10.5f);
        PickupPush(queue, changes, corrupt, 10.52f, pings);
        Expect(queue.Cards.Count == 2, $"{queue.Cards.Count} cards");
        Expect(pings.Contains(ping), "the ping card is still a ping card");
        Expect(!pings.Contains(corrupt), "the corruption card isn't one");
        ExpectChangeOf(changes, corrupt, 2, 3);
        if (changes.TryGet(ping, out int b, out int a))
        {
            throw new AssertFail($"the ping card took the corruption's change {b} to {a}");
        }
    }

    // Holding 3 Tougher Times, ping a Safer Spaces then take it within the card's 5 s
    private static void TransformEdgePingPickupCorrupt()
    {
        Utils.CapChance = true;
        var queue = new GameQueue();
        var pings = new PingCards();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo ping = Card(DLC1Content.Items.BearVoid);
        PingPush(queue, pings, ping, 10f);
        Contains("corrupts your 3", CardText(pings, changes, ping, Holding((RoR2Content.Items.Bear, 3))));

        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 11f);
        PickupPush(queue, changes, Card(DLC1Content.Items.BearVoid), 11.02f, pings);
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);
        changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 11.5f);
        PickupPush(queue, changes, corrupt, 11.52f, pings);

        Expect(queue.Cards.Count == 2, $"{queue.Cards.Count} cards");
        Expect(!pings.Contains(ping), "the ping card became the pickup's card");
        ExpectChangeOf(changes, ping, 0, 1);
        ExpectChangeOf(changes, corrupt, 1, 4);
    }

    // Benthic Bloom upgrades each picked item in its own transform, so two originals landing on one item
    // give two cards that differ by previousData and two changes, on a host change then card each time
    private static void TransformEdgeTwoOriginalsOneItem()
    {
        var queue = new GameQueue();
        var changes = new PickupChanges<NotificationInfo>();
        NotificationInfo fromSyringe = TransformCard(RoR2Content.Items.Bear, RoR2Content.Items.Syringe, TransformationType.CloverVoid);
        NotificationInfo fromHoof = TransformCard(RoR2Content.Items.Bear, RoR2Content.Items.Hoof, TransformationType.CloverVoid);
        changes.CountChanged(Index(RoR2Content.Items.Bear), 0, 2, 10f);
        changes.CountChanged(Index(RoR2Content.Items.Bear), 2, 3, 10f);
        PickupPush(queue, changes, fromSyringe, 10.02f);
        PickupPush(queue, changes, fromHoof, 10.02f);
        Expect(queue.Cards.Count == 2, $"{queue.Cards.Count} cards");
        ExpectChangeOf(changes, fromSyringe, 0, 2);
        ExpectChangeOf(changes, fromHoof, 2, 3);
    }

    // HealthComponent turns every Delicate Watch into its broken version at once, a ConsumedItem card
    private static void TransformEdgeConsumedWatch()
    {
        CharacterMaster m = Holding((DLC1Content.Items.FragileDamageBonusConsumed, 2));
        string s = ItemStatText.DescribePickup(
            DLC1Content.Items.FragileDamageBonusConsumed, m, "FragileDamageBonusConsumed_PICKUP", false, true, " → ", (0, 2))!;
        Contains("40%", s);
        Lacks("→", s);
    }

    // Benthic Bloom upgrades into a Tougher Times while a Safer Spaces is held, it's corrupted half a second later
    private static void TransformEdgeUpgradeIntoCorrupted()
    {
        CharacterMaster m = Holding((RoR2Content.Items.Bear, 1), (DLC1Content.Items.BearVoid, 2));
        Equal("Bear_PICKUP", ItemStatText.DescribePickup(RoR2Content.Items.Bear, m, "Bear_PICKUP", false, true, " → ", (0, 1))!);
    }

    // Outside the game no language is loaded and every token reads as invalid, so full text needs one
    // Restored after, as other tests rely on tokens resolving to themselves
    private static void WithLanguage(System.Collections.Generic.Dictionary<string, string> strings, System.Action body)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var language = (Language)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Language));
        typeof(Language).GetField("stringsByToken", flags)!.SetValue(language, strings);
        System.Reflection.FieldInfo current = typeof(Language).GetField(
            "<currentLanguage>k__BackingField", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        object? old = current.GetValue(null);
        current.SetValue(null, language);
        try
        {
            body();
        }
        finally
        {
            current.SetValue(null, old);
        }
    }

    private const string BearVoidPickup = "Block the next source of damage. <style=cIsVoid>Corrupts all Tougher Times</style>.";
    private const string BearVoidDesc = "<style=cIsHealing>Blocks</style> incoming damage once. Recharges after <style=cIsUtility>15 seconds</style> <style=cStack>(-10% per stack)</style>. <style=cIsVoid>Corrupts all Tougher Times</style>.";

    private static void TransformEdgeFullTextKeepsCorrupts()
    {
        WithLanguage(new() { ["BearVoid_PICKUP"] = BearVoidPickup, ["BearVoid_DESC"] = BearVoidDesc }, () =>
        {
            CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 4));
            string s = ItemStatText.DescribePickup(DLC1Content.Items.BearVoid, m, BearVoidPickup, true, false, " → ", (1, 4))!;
            Equal(BearVoidDesc, s);
        });
    }

    // GrantHexes drops Hex pickups that can be pinged, and GenericNotification.SetItem wraps their one-liner
    // in HEXTIER_PICKUP_SUFFIX, which in English is the one-liner then the Hallowed Shrine line
    private const string HexHint = "\n<style=cIsHealing>Cleanse</style> this <style=cDeath>Hex</color> in a <style=cHumanObjective>Hallowed Shrine</style>.";

    private static void PingHexKeepsSuffixStats()
    {
        ItemDef hex = DLC4Content.Items.HexCurse;
        string game = Language.GetString(hex.pickupToken) + HexHint;
        string s = ItemStatText.DescribePing(hex, Holding((RoR2Content.Items.Syringe, 1)), game, false, true, " → ")!;
        Contains("Hallowed Shrine", s);
    }

    private static void PingHexKeepsSuffixFullText()
    {
        var strings = new System.Collections.Generic.Dictionary<string, string>
        {
            ["HexCurse_PICKUP"] = "Reduce your maximum health.",
            ["HexCurse_DESC"] = "Reduce your <style=cIsHealth>maximum health</style> by <style=cDeath>30%</style>.",
        };
        WithLanguage(strings, () =>
        {
            ItemDef hex = DLC4Content.Items.HexCurse;
            string game = Language.GetString(hex.pickupToken) + HexHint;
            CharacterMaster m = Holding((RoR2Content.Items.Syringe, 1));
            Contains("Hallowed Shrine", ItemStatText.DescribePickup(hex, m, game, true, false, " → ", null)!);
            Contains("Hallowed Shrine", ItemStatText.DescribePing(hex, m, game, true, false, " → ")!);
        });
    }

    private static void PingNoMaster()
    {
        Equal("Bear_PICKUP", ItemStatText.DescribePing(RoR2Content.Items.Bear, null, "Bear_PICKUP", false, true, " → ")!);
    }

    // In the half second before the corruption runs, a Safer Spaces and a Tougher Times are both held
    private static void PingVoidHeldAndOriginal()
    {
        Utils.CapChance = true;
        CharacterMaster m = Holding((DLC1Content.Items.BearVoid, 2), (RoR2Content.Items.Bear, 1));
        string s = ItemStatText.DescribePing(DLC1Content.Items.BearVoid, m, "BearVoid_PICKUP", false, true, " → ")!;
        Contains(Stats(DLC1Content.Items.BearVoid, 2, 4, true), s);
    }

    private static void ExpectChangeOf(PickupChanges<NotificationInfo> changes, NotificationInfo card, int before, int after)
    {
        if (!changes.TryGet(card, out int b, out int a))
        {
            throw new AssertFail($"no change matched, expected {before} to {after}");
        }

        Equal($"{before} to {after}", $"{b} to {a}");
    }
}
