using System;
using BepInEx.Logging;

namespace JustBeBetter.ItemInfo
{
    // The Item Info section's two halves, the pickup card and inventory hover, each starting on its own so one
    // failing leaves the other running
    internal sealed class ItemInfoFeature : IFeature
    {
        private readonly IFeature[] parts;
        private readonly ManualLogSource log;

        internal ItemInfoFeature(ItemInfoSettings settings, ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            parts = new IFeature[] { new PickupDetails(settings, log), new HoverDetails(settings, log) };
        }

        public void Enable()
        {
            foreach (IFeature part in parts)
            {
                try
                {
                    part.Enable();
                }
                catch (Exception e)
                {
                    log.LogError($"Couldn't start {part.GetType().Name}, it stays off: {e}");
                    TryDisable(part);
                }
            }
        }

        public void Disable()
        {
            foreach (IFeature part in parts)
            {
                TryDisable(part);
            }
        }

        private void TryDisable(IFeature part)
        {
            try
            {
                part.Disable();
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't fully switch {part.GetType().Name} off: {e.Message}");
            }
        }
    }
}
