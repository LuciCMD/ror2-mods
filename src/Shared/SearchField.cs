using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Shared
{
    // A single-line search box in the game's look: the background copied from a nearby panel's Image, the
    // game's font, an italic placeholder
    internal static class SearchField
    {
        internal const float FontSize = 20f;

        private const int MaxLength = 40;

        private static readonly Color Placeholder = new Color(1f, 1f, 1f, 0.45f);

        internal static TMP_InputField Create(Transform parent, Image? style, TMP_FontAsset? font, string placeholder, Action<string> onChanged)
        {
            var go = new GameObject("Field", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Image background = go.AddComponent<Image>();
            if (style)
            {
                background.sprite = style!.sprite;
                background.type = style.type;
                background.color = style.color;
                background.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
            }
            else
            {
                background.color = new Color(0f, 0f, 0f, 0.5f);
            }

            RectTransform area = Stretch(new GameObject("Text Area", typeof(RectTransform)), go.transform, 12f, 4f);
            area.gameObject.AddComponent<RectMask2D>();
            TextMeshProUGUI hint = Text(area, "Placeholder", font, placeholder);
            hint.fontStyle = FontStyles.Italic;
            hint.color = Placeholder;
            TextMeshProUGUI typed = Text(area, "Text", font, string.Empty);

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = typed;
            field.placeholder = hint;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = MaxLength;
            if (font)
            {
                field.fontAsset = font;
            }

            field.pointSize = FontSize;
            field.onValueChanged.AddListener(text => onChanged(text));
            go.SetActive(true);
            return field;
        }

        internal static RectTransform Stretch(GameObject go, Transform parent, float sides, float ends)
        {
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(sides, ends);
            rect.offsetMax = new Vector2(-sides, -ends);
            return rect;
        }

        internal static TextMeshProUGUI Text(RectTransform parent, string name, TMP_FontAsset? font, string text)
        {
            RectTransform rect = Stretch(new GameObject(name, typeof(RectTransform)), parent, 0f, 0f);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font)
            {
                label.font = font;
            }

            label.fontSize = FontSize;
            label.enableWordWrapping = false;
            label.alignment = TextAlignmentOptions.Left;
            label.verticalAlignment = VerticalAlignmentOptions.Middle;
            label.text = text;
            label.raycastTarget = false;
            return label;
        }
    }
}
