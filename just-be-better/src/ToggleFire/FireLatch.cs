namespace JustBeBetter.ToggleFire
{
    // One player's primary button, ticked after the game reads it, a short tap latches it held and the next
    // press lets go, a long press stays an ordinary hold
    internal sealed class FireLatch
    {
        private bool wasDown;
        private float pressedAt;

        // A press that ended a latch, or was held through a stop, mustn't start a new one on release
        private bool pressSpent;

        internal bool Latched { get; private set; }

        // A skill that needs a fresh press per shot would fire every tick held this way, and Captain's shotgun
        // only fires on release, so a latched tap would charge it forever
        internal static bool CanLatch(bool mustKeyPress, string? activationState)
        {
            return !mustKeyPress && activationState != "EntityStates.Captain.Weapon.ChargeCaptainShotgun";
        }

        // Whether the button should read as held this tick, down is what the game read itself
        internal bool Step(bool down, float now, float tapSeconds, bool canLatch)
        {
            if (down && !wasDown)
            {
                pressedAt = now;
                pressSpent = Latched;
                Latched = false;
            }
            else if (!down && wasDown && !pressSpent && canLatch && now - pressedAt <= tapSeconds)
            {
                Latched = true;
            }

            wasDown = down;
            return down || Latched;
        }

        internal void Stop(bool down)
        {
            Latched = false;
            pressSpent |= down;
            wasDown = down;
        }
    }
}
