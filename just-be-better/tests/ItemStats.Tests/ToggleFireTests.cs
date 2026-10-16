using JustBeBetter.ToggleFire;

// When a click keeps the primary firing, and what lets it go
internal static partial class Program
{
    private const float FireTap = 0.3f;

    private static void RunToggleFire()
    {
        Test("Toggle fire: a quick click keeps firing after the release", FireTapLatches);
        Test("Toggle fire: a long press fires only while held", FireHoldIsOrdinary);
        Test("Toggle fire: clicking again stops it, and that click doesn't start it again", FireSecondClickStops);
        Test("Toggle fire: a stop lets go, and a press held through it doesn't latch", FireStopLetsGo);
        Test("Toggle fire: skills that need a fresh press for each shot never latch", FireMustKeyPress);
        Test("Toggle fire: a click exactly at the tap length still latches", FireTapEdge);
        Test("Toggle fire: semi-automatic skills and Captain's shotgun never latch", FireWhichLatch);
    }

    // Ticks of (down, time), returning what the button reads as on the last one
    private static bool Ticks(FireLatch latch, bool canLatch, params (bool Down, float At)[] ticks)
    {
        bool held = false;
        foreach ((bool down, float at) in ticks)
        {
            held = latch.Step(down, at, FireTap, canLatch);
        }

        return held;
    }

    private static void FireTapLatches()
    {
        var latch = new FireLatch();
        Expect(Ticks(latch, true, (false, 0f), (true, 1f)), "pressed reads held");
        Expect(Ticks(latch, true, (false, 1.1f)), "released after a tap still reads held");
        Expect(latch.Latched, "latched after a tap");
        Expect(Ticks(latch, true, (false, 5f), (false, 9f)), "keeps firing with the button up");
    }

    private static void FireHoldIsOrdinary()
    {
        var latch = new FireLatch();
        Ticks(latch, true, (true, 1f), (true, 1.2f), (true, 1.5f));
        Expect(!Ticks(latch, true, (false, 1.6f)), "a long press lets go on release");
        Expect(!latch.Latched, "a long press doesn't latch");
    }

    private static void FireSecondClickStops()
    {
        var latch = new FireLatch();
        Ticks(latch, true, (true, 1f), (false, 1.1f));
        Expect(Ticks(latch, true, (true, 3f)), "the stopping click still fires while held");
        Expect(!latch.Latched, "the press lets go of the latch");
        Expect(!Ticks(latch, true, (false, 3.1f)), "the quick stopping click doesn't latch again");
        Expect(!latch.Latched, "still off after the stopping click");
        Ticks(latch, true, (true, 5f), (false, 5.1f));
        Expect(latch.Latched, "a later tap latches again");
    }

    private static void FireStopLetsGo()
    {
        var latch = new FireLatch();
        Ticks(latch, true, (true, 1f), (false, 1.1f));
        latch.Stop(false);
        Expect(!latch.Latched, "a stop lets go");
        Expect(!Ticks(latch, true, (false, 2f)), "reads up after a stop");

        // Pressed, then a menu opens before the release
        Ticks(latch, true, (true, 3f));
        latch.Stop(true);
        Expect(!Ticks(latch, true, (false, 3.1f)), "a press held through a stop doesn't latch on release");
        Expect(!latch.Latched, "still off");
    }

    private static void FireMustKeyPress()
    {
        var latch = new FireLatch();
        Ticks(latch, false, (true, 1f));
        Expect(!Ticks(latch, false, (false, 1.05f)), "a semi-automatic skill doesn't latch");
        Expect(!latch.Latched, "not latched");
    }

    private static void FireWhichLatch()
    {
        Expect(FireLatch.CanLatch(false, "EntityStates.Commando.CommandoWeapon.FirePistol2"), "an automatic primary latches");
        Expect(FireLatch.CanLatch(false, null), "a primary with no state type still latches");
        Expect(!FireLatch.CanLatch(true, "EntityStates.Commando.CommandoWeapon.FirePistol2"), "a semi-automatic primary doesn't");
        Expect(!FireLatch.CanLatch(false, "EntityStates.Captain.Weapon.ChargeCaptainShotgun"), "Captain's shotgun doesn't");
    }

    private static void FireTapEdge()
    {
        var latch = new FireLatch();
        Ticks(latch, true, (true, 1f), (false, 1f + FireTap));
        Expect(latch.Latched, "a click of exactly the tap length latches");

        var longer = new FireLatch();
        Ticks(longer, true, (true, 1f), (false, 1.31f));
        Expect(!longer.Latched, "just past the tap length doesn't latch");
    }
}
