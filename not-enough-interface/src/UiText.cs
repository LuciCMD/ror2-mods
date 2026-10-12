using RoR2;
using RoR2.UI;

namespace NotEnoughInterface
{
    // The game's dialogs and labels take language tokens; this registers plain text under a fresh token
    internal static class UiText
    {
        private static int counter;

        internal static string Token(string text)
        {
            Language? language = Language.currentLanguage;
            if (language == null)
            {
                return text;
            }

            string token = "NOT_ENOUGH_INTERFACE_TEXT_" + counter++;
            language.SetStringByToken(token, text);
            return token;
        }

        internal static SimpleDialogBox.TokenParamsPair Pair(string text)
        {
            return new SimpleDialogBox.TokenParamsPair(Token(text));
        }

        internal static void SetLabel(MPButton button, string text)
        {
            LanguageTextMeshController label = button.GetComponentInChildren<LanguageTextMeshController>(true);
            if (label)
            {
                label.token = Token(text);
            }
        }
    }
}
