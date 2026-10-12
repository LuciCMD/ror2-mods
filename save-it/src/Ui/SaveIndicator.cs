using System.Linq;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaveIt.Ui
{
    internal enum SaveNotice
    {
        Saved,
        Partial,
        Failed,
    }

    // One line under the item bar after each save, on the local HUD. Saves happen at stage start, often
    // before the new HUD exists, so a notice waits for a HUD and its clock starts once it's on screen
    internal sealed class SaveIndicator : MonoBehaviour
    {
        private const float FadeSeconds = 1f;
        private const float GiveUpSeconds = 20f;
        private const float FontSize = 22f;

        private static readonly Color SavedColor = new Color(0.87f, 0.88f, 0.9f);
        private static readonly Color PartialColor = new Color(1f, 0.78f, 0.36f);
        private static readonly Color FailedColor = new Color(1f, 0.45f, 0.4f);
        private static readonly Vector3[] Corners = new Vector3[4];

        private static string? pendingText;
        private static SaveNotice pendingKind;

        private string? text;
        private Color color;
        private float duration;
        private float postedAt;
        private float shownAt;
        private bool started;
        private HUD? hud;
        private TextMeshProUGUI? label;

        internal static void Post(string text, SaveNotice kind)
        {
            pendingText = text;
            pendingKind = kind;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (pendingText != null)
            {
                text = pendingText;
                color = pendingKind switch
                {
                    SaveNotice.Failed => FailedColor,
                    SaveNotice.Partial => PartialColor,
                    _ => SavedColor,
                };
                duration = pendingKind switch
                {
                    SaveNotice.Failed => 12f,
                    SaveNotice.Partial => 8f,
                    _ => 4f,
                };
                pendingText = null;
                postedAt = now;
                started = false;
            }

            if (text == null || (!started && now - postedAt > GiveUpSeconds))
            {
                Clear();
                return;
            }

            if (!EnsureLabel())
            {
                return;
            }

            if (!started)
            {
                shownAt = now;
                started = true;
            }

            float left = duration - (now - shownAt);
            if (left <= 0f)
            {
                Clear();
                return;
            }

            label!.text = text;
            label.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(left / FadeSeconds));
            Place();
        }

        private bool EnsureLabel()
        {
            if (label && hud && hud!.mainContainer.activeInHierarchy)
            {
                label!.gameObject.SetActive(true);
                return true;
            }

            hud = HUD.readOnlyInstanceList.FirstOrDefault(h => h && h.itemInventoryDisplay && h.mainContainer.activeInHierarchy);
            if (!hud)
            {
                return false;
            }

            var go = new GameObject("SaveItIndicator", typeof(RectTransform));
            go.transform.SetParent(hud!.mainContainer.transform, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            label = go.AddComponent<TextMeshProUGUI>();
            TextMeshProUGUI? sample = hud.mainContainer.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t != label && t.font);
            if (sample)
            {
                label.font = sample!.font;
            }

            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Top;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(1000f, FontSize * 2f);
            return true;
        }

        // Follows the item bar's bottom edge, so it holds at any resolution and HUD scale
        private void Place()
        {
            var bar = (RectTransform)hud!.itemInventoryDisplay.transform;
            bar.GetWorldCorners(Corners);
            Vector3 bottom = (Corners[0] + Corners[3]) * 0.5f;
            float height = (Corners[1] - Corners[0]).magnitude;
            label!.rectTransform.position = bottom - (bar.up * (height * 0.2f));
        }

        private void Clear()
        {
            text = null;
            if (label)
            {
                label!.gameObject.SetActive(false);
            }
        }
    }
}
