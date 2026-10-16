using System;

namespace JustBeBetter.Notifications
{
    // Which pickup cards show and for how long, apart from the game so it can be tested
    internal static class CardRules
    {
        // The game's own length, passed as a literal by every card but trials
        internal const float GameSeconds = 6f;
        internal const float MinSeconds = 1f;
        internal const float MaxSeconds = 30f;

        // A ping's card is ours, so it shows whatever the setting
        internal static bool Hide(bool showPickups, bool ping)
        {
            return !showPickups && !ping;
        }

        // Only cards at the game's 6 s change, a trial's own length and a ping's 5 s stay
        internal static float Duration(float gameSeconds, float wanted, bool ping)
        {
            if (ping || Math.Abs(gameSeconds - GameSeconds) > 0.001f || float.IsNaN(wanted))
            {
                return gameSeconds;
            }

            return Math.Clamp(wanted, MinSeconds, MaxSeconds);
        }
    }
}
