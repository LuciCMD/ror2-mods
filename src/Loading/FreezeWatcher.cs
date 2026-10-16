using System.Diagnostics;
using UnityEngine;

namespace JustBeBetter.Loading
{
    // Any frame that took longer than the threshold is a freeze: the window stops responding for that long
    internal sealed class FreezeWatcher : MonoBehaviour
    {
        private const double ThresholdSeconds = 0.25;

        // Hitches during play pile up between loads, so the oldest go first
        private const int MaxFreezes = 256;

        private long lastFrame;
        private string stepAtLastFrame = string.Empty;

        private void Update()
        {
            long now = Stopwatch.GetTimestamp();

            // The first frame's gap is the rest of the plugins loading, which the patcher already times
            if (lastFrame != 0 && Timeline.Seconds(now - lastFrame) > ThresholdSeconds)
            {
                string step = Timeline.CurrentStep == stepAtLastFrame
                    ? stepAtLastFrame
                    : $"{stepAtLastFrame} → {Timeline.CurrentStep}";
                if (Timeline.Freezes.Count >= MaxFreezes)
                {
                    Timeline.Freezes.RemoveAt(0);
                }

                Timeline.Freezes.Add(new Span(step, lastFrame, now));
            }

            lastFrame = now;
            stepAtLastFrame = Timeline.CurrentStep;
        }
    }
}
