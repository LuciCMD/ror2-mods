using UnityEngine;

namespace EquipmentBeQuiet
{
    // Wwise sets volume per emitter, not per sound, so quieter sounds are posted on a child of their emitter
    // whose output volume is turned down. Same position, so distance and panning match; it goes with its
    // parent, which also stops anything still playing on it. Used at every volume, 0 and 100 included, so a
    // loop and the event that stops it always meet here
    internal static class QuietEmitter
    {
        internal static GameObject Get(GameObject source, string emitterName, int percent)
        {
            Transform found = source.transform.Find(emitterName);
            GameObject emitter;
            if (found)
            {
                emitter = found.gameObject;
            }
            else
            {
                emitter = new GameObject(emitterName);
                emitter.transform.SetParent(source.transform, false);

                // Registers with Wwise on enable and follows the parent's position every frame
                emitter.AddComponent<AkGameObj>();
            }

            // Linear gain: 1 is the normal level
            AkSoundEngine.SetGameObjectOutputBusVolume(
                AkSoundEngine.GetAkGameObjectID(emitter),
                AkSoundEngine.AK_INVALID_GAME_OBJECT,
                Mathf.Clamp(percent, 0, 100) / 100f);
            return emitter;
        }
    }
}
