using System;
using JustBeBetter.Shared;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Saves.Ui
{
    // The game's dialog box with a text field added under its description
    internal static class NamePrompt
    {
        private const int MaxNameLength = 48;
        private const float FieldHeight = 48f;

        internal static void Show(MPEventSystem eventSystem, string header, string description, string initial, string confirmLabel, Action<string> onConfirm)
        {
            SimpleDialogBox dialog = SimpleDialogBox.Create(eventSystem);
            dialog.headerToken = UiText.Pair(header);
            dialog.descriptionToken = UiText.Pair(description);
            TMP_InputField field = AddField(dialog, initial);

            void Confirm()
            {
                string name = field.text.Trim();
                onConfirm(name.Length > 0 ? name : initial);
            }

            dialog.AddActionButton(Confirm, UiText.Token(confirmLabel), true);
            dialog.AddCancelButton(UiText.Token("Cancel"));
            field.onSubmit.AddListener(_ =>
            {
                Confirm();
                if (dialog)
                {
                    UnityEngine.Object.Destroy(dialog.rootObject);
                }
            });
            field.Select();
            field.ActivateInputField();
        }

        private static TMP_InputField AddField(SimpleDialogBox dialog, string initial)
        {
            TextMeshProUGUI reference = dialog.descriptionLabel;
            Transform parent = reference.transform.parent;

            var fieldObject = new GameObject("Just_Be_Better Name Field", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            fieldObject.transform.SetParent(parent, false);
            fieldObject.transform.SetSiblingIndex(reference.transform.GetSiblingIndex() + 1);
            fieldObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            LayoutElement layout = fieldObject.GetComponent<LayoutElement>();
            layout.minHeight = FieldHeight;
            layout.preferredHeight = FieldHeight;
            layout.flexibleWidth = 1f;

            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(fieldObject.transform, false);
            Stretch(area.GetComponent<RectTransform>(), 12f);

            TextMeshProUGUI text = AddText(area.transform, "Text", reference, Color.white);
            TextMeshProUGUI placeholder = AddText(area.transform, "Placeholder", reference, new Color(1f, 1f, 1f, 0.4f));
            placeholder.text = "Run Name";
            placeholder.fontStyle = FontStyles.Italic;

            // Added after the children so OnEnable finds the text component already set
            fieldObject.SetActive(false);
            TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
            field.textViewport = area.GetComponent<RectTransform>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.characterLimit = MaxNameLength;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = initial;
            fieldObject.SetActive(true);
            return field;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, TextMeshProUGUI reference, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            Stretch(textObject.GetComponent<RectTransform>(), 0f);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = reference.font;
            text.fontSize = reference.fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            return text;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, 0f);
            rect.offsetMax = new Vector2(-padding, 0f);
        }
    }
}
