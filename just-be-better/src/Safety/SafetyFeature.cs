using System;
using BepInEx.Logging;

namespace JustBeBetter.Safety
{
    // Pauses or protects whoever has an item picker open, keeps the run timer visible over the picker and lets
    // Escape close it
    // The protection is the host's, the timer and Escape are local UI
    internal sealed class SafetyFeature : IFeature
    {
        private readonly PickerTracker pickerTracker;
        private readonly SoloPause soloPause;
        private readonly Protection protection;
        private readonly RunTimerKeeper runTimerKeeper;
        private readonly EscapeClosesPicker escapeClosesPicker;

        internal SafetyFeature(SafetySettings settings, ManualLogSource log)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            pickerTracker = new PickerTracker(log);
            soloPause = new SoloPause(settings, pickerTracker, log);
            protection = new Protection(settings, pickerTracker, log);
            runTimerKeeper = new RunTimerKeeper(log);
            escapeClosesPicker = new EscapeClosesPicker(log);
        }

        public void Enable()
        {
            soloPause.Enable();
            protection.Enable();
            pickerTracker.Enable();
            runTimerKeeper.Enable();
            escapeClosesPicker.Enable();
        }

        public void Disable()
        {
            escapeClosesPicker.Disable();
            runTimerKeeper.Disable();
            protection.Disable();
            soloPause.Disable();
            pickerTracker.Disable();
        }
    }
}
