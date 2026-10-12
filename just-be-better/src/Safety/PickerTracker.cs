using System;
using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;
using UnityEngine.Networking;

namespace JustBeBetter.Safety
{
    // Server-side record of which player has which picker open
    internal sealed class PickerTracker
    {
        private readonly Dictionary<NetworkUIPromptController, CharacterMaster> openPickers =
            new Dictionary<NetworkUIPromptController, CharacterMaster>();

        private readonly ManualLogSource log;

        internal PickerTracker(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // Raised after the record changes, so OpenCount already includes or excludes the picker
        internal event Action<CharacterMaster>? Opened;

        internal event Action<CharacterMaster>? Closed;

        internal int OpenCount => openPickers.Count;

        internal void Enable()
        {
            On.RoR2.NetworkUIPromptController.SetParticipantMasterId += OnSetParticipantMasterId;
        }

        internal void Disable()
        {
            On.RoR2.NetworkUIPromptController.SetParticipantMasterId -= OnSetParticipantMasterId;
            openPickers.Clear();
        }

        // Every participant change goes through here: SetParticipantMaster (open, choose, cancel, walk away)
        // and OnDisable (picker destroyed)
        // ClearParticipant is never called by the game
        private void OnSetParticipantMasterId(
            On.RoR2.NetworkUIPromptController.orig_SetParticipantMasterId orig,
            NetworkUIPromptController self,
            NetworkInstanceId newMasterObjectInstanceId)
        {
            orig(self, newMasterObjectInstanceId);

            if (!NetworkServer.active || !self)
            {
                return;
            }

            try
            {
                HandleParticipantChange(self, self.currentParticipantMaster);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't track a picker change: {e}");
            }
        }

        private void HandleParticipantChange(NetworkUIPromptController prompt, CharacterMaster? participant)
        {
            bool wasOpen = openPickers.TryGetValue(prompt, out CharacterMaster previous);
            if (wasOpen && participant && previous == participant)
            {
                return;
            }

            if (wasOpen)
            {
                openPickers.Remove(prompt);
                log.LogInfo($"Picker closed: {prompt.name} by {NameOf(previous)}");
                Closed?.Invoke(previous);
            }

            if (participant && PickerTypes.IsPicker(prompt))
            {
                openPickers[prompt] = participant!;
                log.LogInfo($"Picker opened: {prompt.name} by {NameOf(participant!)}");
                Opened?.Invoke(participant!);
            }
        }

        private static string NameOf(CharacterMaster master)
        {
            return master ? Util.GetBestMasterName(master) : "a player who left";
        }
    }
}
