using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace EquipmentBeQuiet
{
    // Every sound the game posts on a GameObject passes through here; the chime, the listed equipment events,
    // and anything posted on the user's own body while its equipment is being performed are posted on a quiet
    // child emitter instead, the rest untouched. Posts by raw Wwise object ID (no GameObject) can't be moved
    // and are left alone
    internal sealed class EquipmentSounds
    {
        private const string ChimeEmitter = "Equipment_Be_Quiet Chime";
        private const string SoundEmitter = "Equipment_Be_Quiet Sounds";

        private static Settings? settings;

        // Set while EquipmentSlot.PerformEquipmentAction runs (server side): modded equipment plays whatever
        // sound it likes there, often borrowed (Aetherium's Jar of Reshaping plays Huntress's Ballista shot),
        // so the event can't be listed, but on the user's body at that moment it can only be the equipment's
        private static GameObject? performer;

        private readonly ManualLogSource log;
        private readonly List<Hook> hooks = new List<Hook>();

        internal EquipmentSounds(Settings settings, ManualLogSource log)
        {
            EquipmentSounds.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            On.RoR2.EquipmentSlot.PerformEquipmentAction += OnPerform;
            Type engine = typeof(AkSoundEngine);
            Type callback = typeof(AkCallbackManager.EventCallback);
            Add(engine.GetMethod(nameof(AkSoundEngine.PostEvent), new[] { typeof(string), typeof(GameObject) }), nameof(PostName));
            Add(engine.GetMethod(nameof(AkSoundEngine.PostEvent), new[] { typeof(uint), typeof(GameObject) }), nameof(PostId));
            Add(
                engine.GetMethod(nameof(AkSoundEngine.PostEvent), new[] { typeof(string), typeof(GameObject), typeof(uint), callback, typeof(object) }),
                nameof(PostNameCallback));
            Add(
                engine.GetMethod(nameof(AkSoundEngine.PostEvent), new[] { typeof(uint), typeof(GameObject), typeof(uint), callback, typeof(object) }),
                nameof(PostIdCallback));
            log.LogInfo($"Equipment sounds watched through {hooks.Count} of 4 sound calls");
        }

        internal void Disable()
        {
            On.RoR2.EquipmentSlot.PerformEquipmentAction -= OnPerform;
            foreach (Hook hook in hooks)
            {
                hook.Dispose();
            }

            hooks.Clear();
        }

        private void Add(MethodInfo? target, string handler)
        {
            if (target == null)
            {
                log.LogWarning($"Sound call for {handler} not found; those sounds keep their volume");
                return;
            }

            hooks.Add(new Hook(target, typeof(EquipmentSounds).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static bool OnPerform(On.RoR2.EquipmentSlot.orig_PerformEquipmentAction orig, EquipmentSlot self, EquipmentDef def)
        {
            GameObject? outer = performer;
            performer = self ? self.gameObject : null;
            try
            {
                return orig(self, def);
            }
            finally
            {
                performer = outer;
            }
        }

        // Where a sound should play: its own emitter, or the quiet child for the chime or equipment sounds
        private static GameObject Route(uint id, GameObject go)
        {
            if (settings == null || !go)
            {
                return go;
            }

            if (id == EquipmentEvents.ChimeId)
            {
                return QuietEmitter.Get(go, ChimeEmitter, settings.ChimeVolume.Value);
            }

            bool listed = EquipmentEvents.Contains(id) && !(id == EquipmentEvents.FruitId && go.GetComponent<CharacterBody>());
            if (!listed && !(performer && go == performer))
            {
                return go;
            }

            return QuietEmitter.Get(go, SoundEmitter, settings.SoundVolume.Value);
        }

        private static uint PostName(Func<string, GameObject, uint> orig, string name, GameObject go)
        {
            return orig(name, string.IsNullOrEmpty(name) ? go : Route(EquipmentEvents.Id(name), go));
        }

        private static uint PostId(Func<uint, GameObject, uint> orig, uint id, GameObject go)
        {
            return orig(id, Route(id, go));
        }

        private static uint PostNameCallback(
            Func<string, GameObject, uint, AkCallbackManager.EventCallback, object, uint> orig,
            string name,
            GameObject go,
            uint flags,
            AkCallbackManager.EventCallback callback,
            object cookie)
        {
            return orig(name, string.IsNullOrEmpty(name) ? go : Route(EquipmentEvents.Id(name), go), flags, callback, cookie);
        }

        private static uint PostIdCallback(
            Func<uint, GameObject, uint, AkCallbackManager.EventCallback, object, uint> orig,
            uint id,
            GameObject go,
            uint flags,
            AkCallbackManager.EventCallback callback,
            object cookie)
        {
            return orig(id, Route(id, go), flags, callback, cookie);
        }
    }
}
