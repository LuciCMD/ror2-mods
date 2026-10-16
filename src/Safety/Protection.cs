using System;
using System.Collections.Generic;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using UnityEngine.Networking;

namespace JustBeBetter.Safety
{
    // Server-side buffs for the picking player: invincible (and cloaked) while picking, then a timed grace
    internal sealed class Protection
    {
        private readonly SafetySettings settings;
        private readonly PickerTracker pickers;
        private readonly ManualLogSource log;

        private readonly Dictionary<CharacterMaster, Granted> protectedMasters =
            new Dictionary<CharacterMaster, Granted>();

        internal Protection(SafetySettings settings, PickerTracker pickers, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.pickers = pickers ?? throw new ArgumentNullException(nameof(pickers));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            pickers.Opened += OnPickerOpened;
            pickers.Closed += OnPickerClosed;
            GlobalEventManager.onCharacterDeathServer += OnCharacterDeath;
            Stage.onServerStageBegin += OnStageBegin;
            Run.onRunDestroyGlobal += OnRunDestroy;
        }

        internal void Disable()
        {
            pickers.Opened -= OnPickerOpened;
            pickers.Closed -= OnPickerClosed;
            GlobalEventManager.onCharacterDeathServer -= OnCharacterDeath;
            Stage.onServerStageBegin -= OnStageBegin;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            foreach (Granted granted in protectedMasters.Values)
            {
                RemoveGranted(granted);
            }

            protectedMasters.Clear();
        }

        private static bool IsSolo()
        {
            return PlayerCharacterMasterController.instances.Count == 1;
        }

        private bool ProtectsWhilePicking()
        {
            return IsSolo()
                ? settings.SoloMode.Value == SoloMode.Protect
                : settings.ProtectInMultiplayer.Value;
        }

        // Grace follows any protection, including the solo pause
        private bool GivesGrace()
        {
            return IsSolo()
                ? settings.SoloMode.Value != SoloMode.Off
                : settings.ProtectInMultiplayer.Value;
        }

        private void OnPickerOpened(CharacterMaster master)
        {
            if (!NetworkServer.active || !ProtectsWhilePicking() || protectedMasters.ContainsKey(master))
            {
                return;
            }

            CharacterBody body = master.GetBody();
            if (!body)
            {
                return;
            }

            // Buffs are counted stacks even when they don't stack in effect, so adding and later removing one
            // of our own never strips a stack that something else applied
            bool cloak = settings.CloakWhilePicking.Value;
            body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
            if (cloak)
            {
                body.AddBuff(RoR2Content.Buffs.Cloak);
            }

            var granted = new Granted(body, cloak);
            protectedMasters[master] = granted;
            log.Verbose($"Protecting {body.GetUserName()} while picking (cloak: {granted.Cloak})");
        }

        private void OnPickerClosed(CharacterMaster master)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (protectedMasters.TryGetValue(master, out Granted granted))
            {
                protectedMasters.Remove(master);
                RemoveGranted(granted);
            }

            float graceSeconds = settings.GraceSeconds.Value;
            CharacterBody? body = master ? master.GetBody() : null;
            if (graceSeconds > 0f && body && GivesGrace())
            {
                body!.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, graceSeconds);
                log.Verbose($"Grace for {body.GetUserName()}: {graceSeconds:0.0} s");
            }
        }

        private void OnCharacterDeath(DamageReport report)
        {
            CharacterMaster? master = report?.victimMaster;
            if (master && protectedMasters.Remove(master!))
            {
                log.Verbose($"Dropped protection for {Util.GetBestMasterName(master)} on death");
            }
        }

        private void OnStageBegin(Stage stage)
        {
            protectedMasters.Clear();
        }

        private void OnRunDestroy(Run run)
        {
            protectedMasters.Clear();
        }

        private static void RemoveGranted(Granted granted)
        {
            if (!granted.Body)
            {
                return;
            }

            RemoveOneStack(granted.Body, RoR2Content.Buffs.HiddenInvincibility);
            if (granted.Cloak)
            {
                RemoveOneStack(granted.Body, RoR2Content.Buffs.Cloak);
            }
        }

        // HasBuff guards against something having cleared every stack already
        private static void RemoveOneStack(CharacterBody body, BuffDef buff)
        {
            if (body.HasBuff(buff))
            {
                body.RemoveBuff(buff);
            }
        }

        // Body kept rather than looked up on close, so a respawn in between can't strip the wrong body
        private readonly struct Granted
        {
            internal Granted(CharacterBody body, bool cloak)
            {
                Body = body;
                Cloak = cloak;
            }

            internal CharacterBody Body { get; }

            internal bool Cloak { get; }
        }
    }
}
