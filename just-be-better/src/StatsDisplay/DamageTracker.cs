using System;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using RoR2;
using UnityEngine;

namespace JustBeBetter.StatsDisplay
{
    // Feeds the combo meter from the damage numbers the game shows every player, so it works on a client too
    // Only your own body's hits count, not your drones' or minions', as in LookingGlass
    internal sealed class DamageTracker
    {
        private static readonly object Counted = new object();

        private readonly StatsDisplaySettings settings;
        private readonly ManualLogSource log;

        // Victims already counted as kills, as a dead body can still take hits
        private ConditionalWeakTable<HealthComponent, object> killed = new ConditionalWeakTable<HealthComponent, object>();
        private bool failed;

        internal DamageTracker(StatsDisplaySettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            Meter = new ComboMeter(settings.BestCombo.Value, settings.BestKillCombo.Value);
        }

        internal ComboMeter Meter { get; }

        // Scaled time, so a paused game doesn't end a combo
        internal static float Now => Time.time;

        internal float Window => Mathf.Clamp(settings.DpsWindow.Value, StatsDisplaySettings.MinDpsWindow, StatsDisplaySettings.MaxDpsWindow);

        internal void Enable()
        {
            GlobalEventManager.onClientDamageNotified += OnDamage;
            Run.onRunStartGlobal += OnRunStart;
            On.RoR2.Stage.PreStartClient += OnStageStart;
        }

        internal void Disable()
        {
            GlobalEventManager.onClientDamageNotified -= OnDamage;
            Run.onRunStartGlobal -= OnRunStart;
            On.RoR2.Stage.PreStartClient -= OnStageStart;
            Keep();
        }

        // Every frame, so a combo ends on time and its records are kept even with the panel hidden
        internal void Tick()
        {
            if (Meter.End(Now, Window))
            {
                Keep();
            }
        }

        private void OnDamage(DamageDealtMessage message)
        {
            if (failed || message == null)
            {
                return;
            }

            try
            {
                GameObject? own = LocalUserManager.GetFirstLocalUser()?.cachedBodyObject;
                // Your own health costs name you as the attacker too
                if (!own || message.attacker != own || message.victim == own)
                {
                    return;
                }

                bool kill = false;
                if (message.victim && message.victim.TryGetComponent(out HealthComponent victim) && !victim.alive && !killed.TryGetValue(victim, out _))
                {
                    killed.Add(victim, Counted);
                    kill = true;
                }

                Meter.Hit(Now, Window, message.damage, kill);
            }
            catch (Exception e)
            {
                // Once, as it runs for every hit anyone makes
                failed = true;
                log.LogError($"Couldn't count a hit, DPS, combos and kills stop counting from here: {e}");
            }
        }

        private void OnRunStart(Run run)
        {
            Keep();
            Meter.NewRun();
            killed = new ConditionalWeakTable<HealthComponent, object>();
        }

        private void OnStageStart(On.RoR2.Stage.orig_PreStartClient orig, Stage self)
        {
            orig(self);
            Meter.NewStage();
        }

        // Written only when beaten, as each write saves the config file
        private void Keep()
        {
            if (Meter.BestCombo > settings.BestCombo.Value)
            {
                settings.BestCombo.Value = Meter.BestCombo;
            }

            if (Meter.BestKillCombo > settings.BestKillCombo.Value)
            {
                settings.BestKillCombo.Value = Meter.BestKillCombo;
            }
        }
    }
}
