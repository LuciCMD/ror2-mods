using System;
using System.Collections.Generic;

namespace JustBeBetter.StatsDisplay
{
    // DPS, combos and kills from your own hits, timed by the caller so it can be checked outside the game
    // A combo is every hit with less than the window between them, and ends a window after the last one
    internal sealed class ComboMeter
    {
        private readonly Queue<(float Time, float Damage)> recent = new Queue<(float, float)>();
        // A double, so a huge hit leaving the window doesn't take the small hits' rounding with it
        private double recentDamage;
        private float lastHit = float.NegativeInfinity;

        internal ComboMeter(float bestCombo, int bestKillCombo)
        {
            BestCombo = Valid(bestCombo) && bestCombo > 0f ? bestCombo : 0f;
            BestKillCombo = Math.Max(0, bestKillCombo);
        }

        internal float Combo { get; private set; }

        internal int ComboKills { get; private set; }

        internal float BestComboThisRun { get; private set; }

        internal int BestKillComboThisRun { get; private set; }

        internal float BestCombo { get; private set; }

        internal int BestKillCombo { get; private set; }

        internal int KillsThisStage { get; private set; }

        internal int KillsThisRun { get; private set; }

        internal void Hit(float now, float window, float damage, bool killed)
        {
            End(now, window);
            Prune(now, window);
            if (Valid(damage) && damage > 0f)
            {
                recent.Enqueue((now, damage));
                recentDamage += damage;
                Combo += damage;
                BestComboThisRun = Math.Max(BestComboThisRun, Combo);
                BestCombo = Math.Max(BestCombo, Combo);
            }

            lastHit = now;
            if (killed)
            {
                ComboKills++;
                KillsThisStage++;
                KillsThisRun++;
                BestKillComboThisRun = Math.Max(BestKillComboThisRun, ComboKills);
                BestKillCombo = Math.Max(BestKillCombo, ComboKills);
            }
        }

        // True when a combo just ended, so its records can be kept
        internal bool End(float now, float window)
        {
            if (Combo <= 0f && ComboKills == 0)
            {
                return false;
            }

            if (now - lastHit < window)
            {
                return false;
            }

            Combo = 0f;
            ComboKills = 0;
            return true;
        }

        internal float Dps(float now, float window)
        {
            if (!(window > 0f))
            {
                return 0f;
            }

            Prune(now, window);
            return (float)(recentDamage / window);
        }

        // On every hit too, so hits don't pile up when nothing reads DPS
        private void Prune(float now, float window)
        {
            while (recent.Count > 0 && !(now - recent.Peek().Time < window))
            {
                recentDamage -= recent.Dequeue().Damage;
            }

            // Drift would leave a crumb once every hit has gone
            if (recent.Count == 0 || recentDamage < 0d)
            {
                recentDamage = 0d;
                foreach ((float _, float damage) in recent)
                {
                    recentDamage += damage;
                }
            }
        }

        // Whole seconds rounded up, 0 once the combo has ended, LookingGlass's cast and add read one too many on a hit
        internal int SecondsLeft(float now, float window)
        {
            if (Combo <= 0f && ComboKills == 0)
            {
                return 0;
            }

            float left = window - (now - lastHit);
            return left > 0f ? (int)Math.Ceiling(left) : 0;
        }

        internal void NewStage()
        {
            KillsThisStage = 0;
        }

        internal void NewRun()
        {
            recent.Clear();
            recentDamage = 0d;
            lastHit = float.NegativeInfinity;
            Combo = 0f;
            ComboKills = 0;
            BestComboThisRun = 0f;
            BestKillComboThisRun = 0;
            KillsThisStage = 0;
            KillsThisRun = 0;
        }

        private static bool Valid(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
