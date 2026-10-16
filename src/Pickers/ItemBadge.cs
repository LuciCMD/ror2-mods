using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Pickers
{
    // Lives on a picker button: its item count in the bottom-right corner and, when the item would be
    // corrupted, the void item's icon in the top-right, clear of the favourite star top-left
    internal sealed class ItemBadge : MonoBehaviour
    {
        // Of the button's size, so both shrink with the grid
        private const float CountHeight = 0.36f;
        private const float IconSize = 0.38f;
        private const float Inset = 2f;

        private TextMeshProUGUI? count;
        private Color countColor = Color.white;
        private Image? icon;
        private AsyncImage? asyncIcon;
        private PickupIndex shownCorruptor = PickupIndex.none;

        internal static ItemBadge For(MPButton button, TextMeshProUGUI? style)
        {
            if (!button.TryGetComponent(out ItemBadge badge))
            {
                badge = button.gameObject.AddComponent<ItemBadge>();
                badge.count = CreateCount(button.transform, style);
                badge.countColor = badge.count.color;
                badge.CreateIcon(button);
            }

            return badge;
        }

        internal void Hide()
        {
            Show(null, false, null);
        }

        // A null count or corruptor leaves that part off
        // A void count is the void item's, tinted so it doesn't read as this item's own
        internal void Show(int? itemCount, bool voidCount, PickupDef? corruptor)
        {
            if (count)
            {
                count!.enabled = itemCount.HasValue;
                if (itemCount.HasValue)
                {
                    count.text = "x" + itemCount.Value;
                    count.color = voidCount ? (Color)ColorCatalog.GetColor(ColorCatalog.ColorIndex.VoidItem) : countColor;
                }
            }

            if (icon)
            {
                ShowCorruptor(corruptor);
            }
        }

        private void ShowCorruptor(PickupDef? corruptor)
        {
            bool show = corruptor != null;
            icon!.gameObject.SetActive(show);

            // AsyncImage stops listening for its sprite while hidden, so a mark shown again assigns it afresh
            if (!show)
            {
                shownCorruptor = PickupIndex.none;
                return;
            }

            if (corruptor!.pickupIndex == shownCorruptor)
            {
                return;
            }

            shownCorruptor = corruptor.pickupIndex;
            if (asyncIcon)
            {
                asyncIcon!.SetAssetOrDirectReference(corruptor.iconSpriteReference, corruptor.iconSprite);
            }
            else
            {
                icon.sprite = corruptor.iconSprite;
            }
        }

        private static TextMeshProUGUI CreateCount(Transform button, TextMeshProUGUI? style)
        {
            var go = new GameObject("Just_Be_Better Item Count", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(button, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, CountHeight);
            rect.offsetMin = new Vector2(Inset, Inset);
            rect.offsetMax = new Vector2(-Inset - 1f, 0f);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            if (style != null)
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.color = style.color;
                text.fontStyle = style.fontStyle;
            }

            text.alignment = TextAlignmentOptions.BottomRight;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 6f;
            text.fontSizeMax = 72f;
            text.raycastTarget = false;
            text.enabled = false;
            return text;
        }

        // Loads through the game's AsyncImage as the button's own icon does, so a sprite that isn't in memory
        // yet still turns up
        private void CreateIcon(MPButton button)
        {
            var go = new GameObject("Just_Be_Better Corrupted By", typeof(RectTransform));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(button.transform, false);
            rect.anchorMin = new Vector2(1f - IconSize, 1f - IconSize);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(-Inset, -Inset);

            icon = go.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            asyncIcon = go.AddComponent<AsyncImage>();

            // Set here since it's otherwise only found on enable, and a mark first shown on a button the search
            // hides would drop its sprite
            asyncIcon.renderer = icon;

            // Hidden until the sprite arrives rather than a white square
            asyncIcon.disableWhileLoading = true;
        }
    }
}
