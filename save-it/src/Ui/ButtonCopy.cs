using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SaveIt.Ui
{
    // A copy of one of the game's buttons with its behaviour stripped, so only the look carries over
    internal static class ButtonCopy
    {
        internal static MPButton Create(MPButton template, string name, string label)
        {
            GameObject copy = Object.Instantiate(template.gameObject, template.transform.parent);
            copy.name = name;
            copy.SetActive(true);

            // Gamepad bindings would fire the template's own action (Ready, Resume…) and draw its button glyph
            foreach (HGGamepadInputEvent gamepadEvent in copy.GetComponentsInChildren<HGGamepadInputEvent>(true))
            {
                Object.DestroyImmediate(gamepadEvent);
            }

            foreach (InputBindingDisplayController glyph in copy.GetComponentsInChildren<InputBindingDisplayController>(true))
            {
                bool holdsLabel = glyph.GetComponentInChildren<LanguageTextMeshController>(true);
                if (glyph.gameObject == copy || holdsLabel)
                {
                    Object.DestroyImmediate(glyph);
                }
                else
                {
                    Object.DestroyImmediate(glyph.gameObject);
                }
            }

            MPButton button = copy.GetComponent<MPButton>();

            // Persistent click listeners from the template survive RemoveAllListeners, so the event is replaced
            button.onClick = new Button.ButtonClickedEvent();
            button.interactable = true;
            UiText.SetLabel(button, label);
            return button;
        }
    }
}
