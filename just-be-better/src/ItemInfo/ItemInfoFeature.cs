using System;
using BepInEx.Logging;

namespace JustBeBetter.ItemInfo
{
    // The Item Info section's parts, the pickup card, inventory hover, the inspect panel and ping cards, each
    // starting on its own so one failing leaves the others running
    internal sealed class ItemInfoFeature : IFeature
    {
        private readonly IFeature[] parts;
        private readonly ManualLogSource log;

        // Shared with Pickup Notifications, which lets a ping's card through and says which pickups it left out
        internal ItemInfoFeature(ItemInfoSettings settings, PingCards pings, SkippedCards skipped, ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            parts = new IFeature[]
            {
                new PickupDetails(settings, log, pings, skipped),
                new HoverDetails(settings, log),
                new InspectDetails(settings, log),
                new PingDetails(settings, log, pings),
                new AllyCardDetails(settings, log),
            };
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
