using System;
using System.Reflection;
using JustBeBetter.ToggleFire;
using RoR2;

// Toggle Fire at its edges
// FireSim mirrors ToggleFireFeature.Tick as written, with the game's own InputBankTest.ButtonState and
// PlayerCharacterMasterController.ButtonBuffer, a body compares the way a UnityEngine.Object does, destroyed reads as null
// A change to Tick has to be mirrored in FireSim
internal static partial class Program
{
    private const float FireStep = 1f / 60f;

    private static void RunToggleFireEdge()
    {
        Test("Toggle fire edge: a latch reads as one hold to the game, no extra press or release", FireEdgeOneHold);
        Test("Toggle fire edge: the stopping click is a release to the game once it lets go", FireEdgeStopClickReleases);
        Test("Toggle fire edge: a tap shorter than one fixed step still latches", FireEdgeSubStepTap);
        Test("Toggle fire edge: a long stopping press fires while held and doesn't latch again", FireEdgeLongStopPress);
        Test("Toggle fire edge: a pause between press and release makes it a hold", FireEdgePauseIsHold);
        Test("Toggle fire edge: a body swapped while the old one lives stops it", FireEdgeLiveBodySwap);
        Test("Toggle fire edge: a body destroyed before the next one spawns stops it (stage change, Heretic)", FireEdgeDestroyedBodySwap);
        Test("Toggle fire edge: a press held through a body gap doesn't latch on release after the spawn", FireEdgeHeldThroughGap);
        Test("Toggle fire edge: a primary turned semi-automatic mid-latch fires once, as a held button would", FireEdgeMustKeyPressMidLatch);
        Test("Toggle fire edge: using another skill stops it the same tick", FireEdgeOtherSkillStops);
        Test("Toggle fire edge: two local players latch on their own", FireEdgeSplitScreen);
    }

    // Stands in for CharacterBody plus its InputBankTest, == follows UnityEngine.Object so a destroyed body equals null
    private sealed class SimBody
    {
        internal InputBankTest.ButtonState Skill1;
        internal InputBankTest.ButtonState Skill2;
        internal InputBankTest.ButtonState Skill3;
        internal InputBankTest.ButtonState Skill4;
        internal bool Destroyed;
        internal bool Alive = true;
        internal bool PrimaryMustKeyPress;
        internal int Shots;

        private static bool Dead(SimBody? b) => b is null || b.Destroyed;

        public static bool operator ==(SimBody? a, SimBody? b) => Dead(a) && Dead(b) || !Dead(a) && !Dead(b) && ReferenceEquals(a, b);

        public static bool operator !=(SimBody? a, SimBody? b) => !(a == b);

        public override bool Equals(object? obj) => obj is SimBody b && this == b;

        public override int GetHashCode() => 0;

        // GenericCharacterMain.HandleSkill for the primary, ready every tick
        internal void RunPrimary()
        {
            if (Skill1.down && (!PrimaryMustKeyPress || !Skill1.hasPressBeenClaimed))
            {
                Shots++;
                Skill1.hasPressBeenClaimed = true;
            }
        }
    }

    // One local player, PlayerCharacterMasterController plus the feature's Player entry
    private sealed class FireSim
    {
        private readonly FireLatch latch = new FireLatch();
        private SimBody? playerBody;
        private bool hasPlayer;

        internal SimBody? Body;
        internal bool MenuOpen;
        internal float Now;

        internal bool Latched => latch.Latched;

        // One FixedUpdate, the game's PollButtonInput then the hook, then the body's skill state
        internal void Fixed(bool real, bool skill2 = false)
        {
            Now += FireStep;
            if (Body == null)
            {
                if (hasPlayer)
                {
                    latch.Stop(false);
                }

                return;
            }

            bool canSend = !MenuOpen;
            Body.Skill1.PushState(canSend && real);
            Body.Skill2.PushState(canSend && skill2);
            Body.Skill3.PushState(false);
            Body.Skill4.PushState(false);
            Tick(Body);
            Body.RunPrimary();
        }

        // Update while FixedUpdate is stopped, as in a pause
        internal void FrameOnly(float seconds)
        {
            Now += seconds;
            if (hasPlayer && latch.Latched && MenuOpen)
            {
                latch.Stop(false);
            }
        }

        // ToggleFireFeature.Tick
        private void Tick(SimBody body)
        {
            hasPlayer = true;
            bool down = body.Skill1.down;
            if (!ReferenceEquals(playerBody, body))
            {
                if (!ReferenceEquals(playerBody, null))
                {
                    latch.Stop(down);
                }

                playerBody = body;
            }

            bool stop = !body.Alive
                || body.Skill2.justPressed || body.Skill3.justPressed || body.Skill4.justPressed
                || MenuOpen;
            if (stop)
            {
                latch.Stop(down);
                return;
            }

            bool canLatch = FireLatch.CanLatch(body.PrimaryMustKeyPress, null);
            if (!canLatch && latch.Latched)
            {
                latch.Stop(down);
            }

            bool held = latch.Step(down, Now, FireTap, canLatch);
            if (held && !down)
            {
                body.Skill1.down = true;
            }
        }

        internal void Ticks(bool real, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Fixed(real);
            }
        }

        internal void Tap()
        {
            Fixed(true);
            Fixed(false);
        }
    }

    private static void FireEdgeOneHold()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Fixed(true);
        Expect(sim.Body.Skill1.justPressed, "the tap's press reads as a press");
        sim.Fixed(false);
        Expect(sim.Latched, "latched after the tap");
        Expect(sim.Body.Skill1.down, "down on the latch's first tick");
        Expect(!sim.Body.Skill1.justReleased, "the tap's release isn't seen as a release on the tick the latch starts");
        Expect(!sim.Body.Skill1.justPressed, "nor as a second press");
        for (int i = 0; i < 30; i++)
        {
            sim.Fixed(false);
            Expect(sim.Body.Skill1.down && !sim.Body.Skill1.justPressed && !sim.Body.Skill1.justReleased, $"steady hold on latched tick {i}");
        }
    }

    private static void FireEdgeStopClickReleases()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        sim.Ticks(false, 10);
        sim.Fixed(true);
        Expect(!sim.Latched, "the stopping press lets go of the latch");
        Expect(sim.Body!.Skill1.down && !sim.Body.Skill1.justPressed, "the stopping press continues the hold");
        sim.Fixed(false);
        Expect(sim.Body.Skill1.justReleased, "the stopping click's release reads as a release, so charge skills fire");
        Expect(!sim.Latched, "and doesn't latch again");
        sim.Fixed(false);
        Expect(!sim.Body.Skill1.down, "up after");
    }

    // The game's ButtonBuffer, private, driven by reflection on a boxed copy
    private static void FireEdgeSubStepTap()
    {
        Type? bufferType = typeof(PlayerCharacterMasterController).GetNestedType("ButtonBuffer", BindingFlags.NonPublic);
        Expect(bufferType != null, "PlayerCharacterMasterController.ButtonBuffer exists");
        object buffer = Activator.CreateInstance(bufferType!)!;
        MethodInfo push = bufferType!.GetMethod("PushValue")!;
        MethodInfo poll = bufferType.GetMethod("Poll")!;
        FieldInfo value = poll.ReturnType.GetField("value")!;

        var sim = new FireSim { Body = new SimBody() };
        bool Poll() => (bool)value.GetValue(poll.Invoke(buffer, null))!;

        // Pressed and released in two frames between one fixed step and the next
        push.Invoke(buffer, new object[] { true });
        push.Invoke(buffer, new object[] { false });
        bool first = Poll();
        sim.Fixed(first);
        Expect(first, "the game keeps a sub-step tap as one tick down");
        push.Invoke(buffer, new object[] { false });
        sim.Fixed(Poll());
        Expect(sim.Latched, "and the latch takes it as a tap");
    }

    private static void FireEdgeLongStopPress()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        sim.Ticks(false, 5);
        sim.Ticks(true, 120);
        Expect(sim.Body!.Skill1.down && !sim.Latched, "the long press is an ordinary hold");
        sim.Fixed(false);
        Expect(!sim.Latched && !sim.Body.Skill1.down, "its release lets go");
    }

    private static void FireEdgePauseIsHold()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Fixed(true);
        sim.MenuOpen = true;
        sim.FrameOnly(4f);
        sim.MenuOpen = false;
        sim.Fixed(false);
        Expect(!sim.Latched, "a press then a pause then a release doesn't latch");

        // Latched, then paused, the frame hook stops it with no fixed step to clear the forced down
        sim.Tap();
        Expect(sim.Latched, "latched again");
        sim.MenuOpen = true;
        sim.FrameOnly(0.5f);
        Expect(!sim.Latched, "the pause stops it from Update");
        sim.MenuOpen = false;
        sim.Fixed(false);
        Expect(sim.Body!.Skill1.justReleased && !sim.Latched, "after the pause the game sees a release and it stays off");
    }

    private static void FireEdgeLiveBodySwap()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        sim.Body = new SimBody();
        sim.Fixed(false);
        Expect(!sim.Latched, "a new body while the old one still exists stops it");
        Expect(!sim.Body.Skill1.down, "the new body isn't firing");
    }

    // Stage change and CharacterMaster.TransformBody destroy the old body before the new one's first fixed step
    private static void FireEdgeDestroyedBodySwap()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        sim.Ticks(false, 10);
        Expect(sim.Latched, "latched on the old body");
        sim.Body.Destroyed = true;
        sim.Body = null;
        sim.Ticks(false, 30);
        sim.Body = new SimBody();
        sim.Fixed(false);
        Expect(
            !sim.Latched && !sim.Body.Skill1.down,
            $"the new body fires on its own, latched {sim.Latched}, skill1.down {sim.Body.Skill1.down}, shots {sim.Body.Shots}");
    }

    private static void FireEdgeHeldThroughGap()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Ticks(false, 3);
        SimBody old = sim.Body;
        old.Destroyed = true;
        sim.Body = null;

        // Pressed during the load and held for two seconds, let go just after the body arrives
        sim.Ticks(true, 120);
        sim.Body = new SimBody();
        sim.Ticks(true, 3);
        sim.Fixed(false);
        Expect(!sim.Latched, "a two second hold that ends just after a spawn latched as a tap");
    }

    // A real hold claims the press once, the latch's PushState(false) clears the claim every tick
    private static void FireEdgeMustKeyPressMidLatch()
    {
        var held = new FireSim { Body = new SimBody { PrimaryMustKeyPress = true } };
        held.Ticks(true, 60);
        Expect(held.Body!.Shots == 1, $"baseline, a held semi-automatic skill fires once, got {held.Body.Shots}");

        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        Expect(sim.Latched, "latched on an automatic primary");
        sim.Body!.PrimaryMustKeyPress = true;
        sim.Body.Shots = 0;
        sim.Ticks(false, 60);
        Expect(
            sim.Body.Shots <= 1,
            $"the semi-automatic override fired {sim.Body.Shots} times in a second with the button up, latched {sim.Latched}");
    }

    private static void FireEdgeOtherSkillStops()
    {
        var sim = new FireSim { Body = new SimBody() };
        sim.Tap();
        sim.Ticks(false, 3);
        sim.Fixed(false, skill2: true);
        Expect(!sim.Latched, "pressing the secondary stops it");
        Expect(sim.Body!.Skill1.justReleased, "the primary reads released that tick");
        sim.Fixed(false, skill2: true);
        sim.Fixed(false);
        Expect(!sim.Latched && !sim.Body.Skill1.down, "and stays off");
    }

    private static void FireEdgeSplitScreen()
    {
        var one = new FireSim { Body = new SimBody() };
        var two = new FireSim { Body = new SimBody() };
        one.Tap();
        two.Fixed(true);
        two.Ticks(true, 40);
        two.Fixed(false);
        Expect(one.Latched && !two.Latched, "only the player who tapped is latched");
    }
}
