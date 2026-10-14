using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JustBeBetter.ItemInfo;
using JustBeBetter.ItemStats;
using JustBeBetter.Notifications;
using RoR2;
using NotificationInfo = RoR2.CharacterMasterNotificationQueue.NotificationInfo;
using TransformationType = RoR2.CharacterMasterNotificationQueue.TransformationType;

// Pickup Notifications at its edges, after FillCatalog so the void pair resolves
// PickupCards can't run outside Unity, so CardChain mirrors its hooks around PickupDetails and PingDetails as written
// PickupCards' leaf hooks skip the game's Push*Notification, so a hidden pickup never reaches PushNotification
// PickupCards.OnPushNotification is added after PickupDetails', so MonoMod runs it first and PickupDetails sees the new length
// A change to PickupCards, PickupDetails.OnPushNotification or PingDetails.Push has to be mirrored here
internal static partial class Program
{
    private static void RunNotificationEdge()
    {
        Test("Cards edge: the game's trial card length isn't 6 s, so Card Duration leaves it", CardsEdgeTrialLength);
        Test("Cards edge: the game sends every pickup card through the four leaves PickupCards hooks", CardsEdgeLeavesCoverPickups);
        Test("Cards edge: PushNotification is only called by the queue's own pushes", CardsEdgeNoOtherPushers);
        Test("Cards edge: a ping with pickup cards off still queues its card for 5 s", CardsEdgePingWhileHidden);
        Test("Cards edge: a hidden pickup queues nothing and leaves the queue as it was", CardsEdgeHiddenPickupQueuesNothing);
        Test("Cards edge: the chosen length reaches pickup and corruption cards, not trials or pings", CardsEdgeLengths);
        Test("Cards edge: Show Pickup Cards toggled between pickups applies to the next one", CardsEdgeToggleBetween);
        Test("Cards edge: pickup cards off, a void pickup's corruption card still reads 1 → 4, host order", CardsEdgeHiddenVoidHost);
        Test("Cards edge: pickup cards off, a void pickup's corruption card still reads 1 → 4, client order", CardsEdgeHiddenVoidClient);
        Test("Cards edge: pickup cards off, the corruption card's text shows the 1 → 4 step", CardsEdgeHiddenVoidText);
        Test("Cards edge: a pickup folded into a ping card keeps the ping's 5 s, as vanilla folds keep the first length", CardsEdgePickupIntoPingLength);
    }

    // PickupCards plus PickupDetails.OnPushNotification plus PingDetails.Push, around the game's queue
    private sealed class CardChain
    {
        internal readonly GameQueue Queue = new GameQueue();
        internal readonly PingCards Pings = new PingCards();
        internal readonly PickupChanges<NotificationInfo> Changes = new PickupChanges<NotificationInfo>();
        internal bool ShowPickups = true;
        internal float Wanted = CardRules.GameSeconds;

        // A pickup from GenericPickupController, through PickupCards' leaf hook
        internal void Pickup(NotificationInfo info, float now)
        {
            if (CardRules.Hide(ShowPickups, Pings.Pushing))
            {
                // SkippedCards into PickupDetails.OnSkipped, a stand-in card takes the hidden pickup's change
                if (info.data is ItemDef skipped && !skipped.hidden)
                {
                    Changes.CardQueued(new NotificationInfo(skipped, null, false, 0), (int)skipped.itemIndex, now, false);
                }

                return;
            }

            Push(info, CardRules.GameSeconds, now);
        }

        // A corruption, artifact or trial card, which PickupCards doesn't hide
        internal void Push(NotificationInfo info, float gameSeconds, float now)
        {
            // PickupCards.OnPushNotification, outermost
            float duration = CardRules.Duration(gameSeconds, Wanted, Pings.Pushing);

            // PickupDetails.OnPushNotification
            bool ping = Pings.Pushing;
            if (ping)
            {
                Pings.Add(info);
            }

            int before = Queue.Cards.Count;
            Queue.Push(info, duration, now);
            if (ping || info.data is not ItemDef item)
            {
                return;
            }

            bool merged = Queue.Cards.Count == before && before > 0;
            NotificationInfo card = merged ? Queue.Cards[before - 1].Info : info;
            if (merged)
            {
                Pings.PickedUp(card);
            }

            Changes.CardQueued(card, (int)item.itemIndex, now, merged);
        }

        // PingDetails.Push
        internal Timed? Ping(NotificationInfo info, float now)
        {
            int before = Queue.Cards.Count;
            Pings.Pushing = true;
            try
            {
                Pickup(info, now);
            }
            finally
            {
                Pings.Pushing = false;
            }

            if (Queue.Cards.Count <= before)
            {
                return null;
            }

            Timed card = Queue.Cards[Queue.Cards.Count - 1];
            card.Duration = 5f;
            PingQueue.BringForward((System.Collections.IList)Queue.Cards, card, (c, s) => ((Timed)c).Duration = s);
            return card;
        }
    }

    private static void CardsEdgeTrialLength()
    {
        Type relay = typeof(ItemDef).Assembly.GetType("RoR2.TrialVariantEventRelay")!;
        FieldInfo? field = relay?.GetField("trialNotifDuration", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Expect(field != null && field.FieldType == typeof(float), "TrialVariantEventRelay.trialNotifDuration is gone");

        // Set by the static constructor, so run it
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(relay!.TypeHandle);
        float seconds = (float)field!.GetValue(null)!;
        Expect(Math.Abs(seconds - CardRules.GameSeconds) > 0.001f, $"a trial card is {seconds} s, Card Duration would change it");
        Expect(CardRules.Duration(seconds, 20f, false) == seconds, "the trial's own length changed");
    }

    private static void CardsEdgeLeavesCoverPickups()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var hooked = new HashSet<string> { "PushItemNotification", "PushEquipmentNotification", "PushDroneNotification", "PushKeyItemNotification" };
        MethodInfo pickup = typeof(CharacterMasterNotificationQueue).GetMethods(all)
            .First(m => m.Name == "PushPickupNotification" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(UniquePickup));
        string[] calls = Callees(pickup).Where(c => c.DeclaringType == typeof(CharacterMasterNotificationQueue)).Select(c => c.Name).ToArray();
        Expect(calls.Length > 0, "PushPickupNotification calls nothing in the queue, the IL reader failed");
        foreach (string call in calls)
        {
            Expect(hooked.Contains(call), $"PushPickupNotification reaches {call}, which PickupCards doesn't hide");
        }

        foreach (string leaf in hooked)
        {
            Expect(calls.Contains(leaf), $"PushPickupNotification no longer calls {leaf}");
        }
    }

    // A pickup that reached PushNotification any other way would get past Show Pickup Cards
    private static void CardsEdgeNoOtherPushers()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var known = new HashSet<string>
        {
            "PushItemNotification", "PushEquipmentNotification", "PushDroneNotification", "PushKeyItemNotification",
            "PushArtifactNotification", "PushTrialNotification", "PushItemTransformNotification", "PushEquipmentTransformNotification",
            "PushItemNotificationCustom", "PushEquipmentNotificationCustom", "PushKeyItemNotificationCustom",
        };
        var callers = new List<string>();
        foreach (MethodInfo m in typeof(CharacterMasterNotificationQueue).GetMethods(all))
        {
            if (Callees(m).Any(c => c.DeclaringType == typeof(CharacterMasterNotificationQueue) && c.Name == "PushNotification"))
            {
                callers.Add(m.Name);
            }
        }

        Expect(callers.Count > 0, "nothing calls PushNotification, the IL reader failed");
        foreach (string caller in callers)
        {
            Expect(known.Contains(caller), $"{caller} pushes a card, check whether it's a pickup PickupCards should hide");
        }
    }

    private static void CardsEdgePingWhileHidden()
    {
        var chain = new CardChain { ShowPickups = false, Wanted = 20f };
        Timed? card = chain.Ping(Card(RoR2Content.Items.Bear), 10f);
        Expect(card != null, "the ping's card was hidden with Show Pickup Cards off");
        Expect(card!.Duration == 5f, $"the ping's card is {card.Duration} s, not its own 5 s");
        Expect(chain.Pings.Contains(card.Info), "the card isn't marked as a ping");
        Expect(!chain.Pings.Pushing, "Pushing stayed on after the ping");
    }

    private static void CardsEdgeHiddenPickupQueuesNothing()
    {
        var chain = new CardChain { ShowPickups = false };
        chain.Ping(Card(RoR2Content.Items.Syringe), 9f);
        chain.Pickup(Card(RoR2Content.Items.Bear), 10f);
        chain.Pickup(Card(RoR2Content.Items.Syringe), 10f);
        Expect(chain.Queue.Cards.Count == 1, $"{chain.Queue.Cards.Count} cards, a hidden pickup was queued");
        Expect(chain.Pings.Contains(chain.Queue.Cards[0].Info), "the hidden Syringe pickup took over the ping card");
    }

    private static void CardsEdgeLengths()
    {
        var chain = new CardChain { Wanted = 12.5f };
        chain.Pickup(Card(RoR2Content.Items.Bear), 10f);
        chain.Push(TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid), 6f, 10.5f);
        chain.Push(new NotificationInfo(new object(), null, false, 0), 12f, 11f);
        Expect(chain.Queue.Cards[0].Duration == 12.5f, $"pickup card {chain.Queue.Cards[0].Duration} s");
        Expect(chain.Queue.Cards[1].Duration == 12.5f, $"corruption card {chain.Queue.Cards[1].Duration} s");
        Expect(chain.Queue.Cards[2].Duration == 12f, $"trial card {chain.Queue.Cards[2].Duration} s");
        Timed? ping = chain.Ping(Card(RoR2Content.Items.Syringe), 11.5f);
        Expect(ping != null && ping.Duration == 5f, $"ping card {ping?.Duration} s");
    }

    private static void CardsEdgeToggleBetween()
    {
        var chain = new CardChain { ShowPickups = false };
        chain.Pickup(Card(RoR2Content.Items.Bear), 10f);
        chain.ShowPickups = true;
        chain.Pickup(Card(RoR2Content.Items.Syringe), 11f);
        chain.ShowPickups = false;
        chain.Pickup(Card(RoR2Content.Items.Hoof), 12f);
        Expect(chain.Queue.Cards.Count == 1 && chain.Queue.Cards[0].Info.data == (object)RoR2Content.Items.Syringe,
            $"{chain.Queue.Cards.Count} cards, only the Syringe picked up while on should show");
    }

    // Holding 3 Tougher Times, take a Safer Spaces with pickup cards off
    // The server gives it at 10 s and corrupts the 3 at 10.5 s, as in TransformEdgeHostOrder
    // The hidden pickup's 0 → 1 count change still waits in PickupChanges, as PickupDetails notes every local change,
    // and the corruption card, still shown, takes it instead of its own 1 → 4
    private static void CardsEdgeHiddenVoidHost()
    {
        var chain = new CardChain { ShowPickups = false };
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);

        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 10f);
        chain.Pickup(Card(DLC1Content.Items.BearVoid), 10.02f);
        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 10.5f);
        chain.Push(corrupt, 6f, 10.52f);

        Expect(chain.Queue.Cards.Count == 1, $"{chain.Queue.Cards.Count} cards, only the corruption should show");
        ExpectChangeOf(chain.Changes, corrupt, 1, 4);
    }

    private static void CardsEdgeHiddenVoidClient()
    {
        var chain = new CardChain { ShowPickups = false };
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);

        chain.Pickup(Card(DLC1Content.Items.BearVoid), 10f);
        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 10.1f);
        chain.Push(corrupt, 6f, 10.5f);
        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 10.6f);

        ExpectChangeOf(chain.Changes, corrupt, 1, 4);
    }

    private static void CardsEdgeHiddenVoidText()
    {
        Utils.CapChance = true;
        var chain = new CardChain { ShowPickups = false };
        NotificationInfo corrupt = TransformCard(DLC1Content.Items.BearVoid, RoR2Content.Items.Bear, TransformationType.ContagiousVoid);
        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 0, 1, 10f);
        chain.Pickup(Card(DLC1Content.Items.BearVoid), 10.02f);
        chain.Changes.CountChanged(Index(DLC1Content.Items.BearVoid), 1, 4, 10.5f);
        chain.Push(corrupt, 6f, 10.52f);

        string s = CardText(chain.Pings, chain.Changes, corrupt, Holding((DLC1Content.Items.BearVoid, 4)));
        Contains(Stats(DLC1Content.Items.BearVoid, 1, 4, false), s);
    }

    // Ping a Tougher Times, then pick one up within its 5 s with Card Duration at 20 s
    // The game folds the pickup into the ping card and PickupDetails makes it the pickup's card, which still
    // ends at the ping's 5 s, as vanilla folds keep the first card's length
    private static void CardsEdgePickupIntoPingLength()
    {
        var chain = new CardChain { Wanted = 20f };
        Timed? ping = chain.Ping(Card(RoR2Content.Items.Bear), 10f);
        chain.Pickup(Card(RoR2Content.Items.Bear), 11f);
        Expect(chain.Queue.Cards.Count == 1 && !chain.Pings.Contains(ping!.Info), "the pickup didn't fold into the ping card");
        Expect(ping.Duration == 5f, $"the pickup's card ends at {ping.Duration} s, not the ping's 5 s");
    }
}
