using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JustBeBetter.Shared
{
    // Pins an entry with a star in a corner of its button: faint while the button is hovered, gold once pinned
    // Left-click on the star only, since unlock mods (RealerCheatUnlocks) use right-click on survivors
    internal sealed class FavouriteMark : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Func<bool>? isPinned;
        private FavouriteStar? star;
        private bool hovered;

        internal static void Attach(GameObject button, Func<bool> isPinned, Action toggle, bool leftCorner)
        {
            if (!button.TryGetComponent(out FavouriteMark mark))
            {
                mark = button.AddComponent<FavouriteMark>();
                mark.star = FavouriteStar.Create(button.transform, leftCorner);
            }

            mark.isPinned = isPinned;
            mark.star!.Toggle = toggle;
            mark.star.transform.SetAsLastSibling();
            mark.Refresh();
        }

        internal static void Refresh(GameObject button)
        {
            if (button.TryGetComponent(out FavouriteMark mark))
            {
                mark.Refresh();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
        }

        private void OnDisable()
        {
            hovered = false;
            Refresh();
        }

        private void Refresh()
        {
            if (star && isPinned != null)
            {
                star!.Show(isPinned(), hovered);
            }
        }
    }

    // Takes the press as well as the click, so the button underneath doesn't also act
    internal sealed class FavouriteStar : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
    {
        private const float HotScale = 1.15f;

        // Of the button's size, so it shrinks with the grid
        private const float Size = 0.38f;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.29f);
        private static readonly Color Faint = new Color(1f, 1f, 1f, 0.8f);

        private Image? image;
        private bool pinned;
        private bool buttonHovered;
        private bool hot;

        internal Action? Toggle { get; set; }

        internal static FavouriteStar Create(Transform parent, bool leftCorner)
        {
            var go = new GameObject("Just_Be_Better Favourite", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(leftCorner ? 0f : 1f - Size, 1f - Size);
            rect.anchorMax = new Vector2(leftCorner ? Size : 1f, 1f);
            rect.offsetMin = new Vector2(leftCorner ? 2f : 0f, 0f);
            rect.offsetMax = new Vector2(leftCorner ? 0f : -2f, -2f);
            FavouriteStar star = go.AddComponent<FavouriteStar>();
            star.image = go.AddComponent<Image>();
            star.image.preserveAspect = true;
            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(1f, -1f);
            return star;
        }

        internal void Show(bool isPinned, bool isButtonHovered)
        {
            pinned = isPinned;
            buttonHovered = isButtonHovered;
            Redraw();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hot = true;
            Redraw();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hot = false;
            Redraw();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Toggle?.Invoke();
            }
        }

        private void OnDisable()
        {
            hot = false;
        }

        private void Redraw()
        {
            if (!image)
            {
                return;
            }

            image!.enabled = pinned || buttonHovered;
            image.sprite = pinned || hot ? StarSprite.Filled : StarSprite.Hollow;
            image.color = pinned || hot ? Gold : Faint;
            transform.localScale = Vector3.one * (hot ? HotScale : 1f);
        }
    }
}
