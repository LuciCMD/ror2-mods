using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace NotEnoughInterface
{
    // Marks a tab's scroll viewport. The game lifts selection and hover outlines onto override-sorting canvases
    // (RefreshCanvasDrawOrder.OnEnable), which RectMask2D doesn't clip; inside a viewport they're put back so the
    // mask cuts them off at the edge. Their overhang is smaller than the gaps between buttons, so nothing covers them
    internal sealed class TabViewport : MonoBehaviour
    {
        internal static void Enable()
        {
            On.RoR2.RefreshCanvasDrawOrder.OnEnable += OnOutlineEnable;
        }

        internal static void Disable()
        {
            On.RoR2.RefreshCanvasDrawOrder.OnEnable -= OnOutlineEnable;
        }

        internal static void Unlift(RefreshCanvasDrawOrder outline)
        {
            Canvas canvas = outline.canvas;
            if (!canvas || !canvas.overrideSorting || !outline.GetComponentInParent<TabViewport>(true))
            {
                return;
            }

            canvas.overrideSorting = false;
            foreach (MaskableGraphic graphic in outline.GetComponentsInChildren<MaskableGraphic>(true))
            {
                graphic.RecalculateClipping();
            }
        }

        private static void OnOutlineEnable(On.RoR2.RefreshCanvasDrawOrder.orig_OnEnable orig, RefreshCanvasDrawOrder self)
        {
            orig(self);
            Unlift(self);
        }
    }
}
