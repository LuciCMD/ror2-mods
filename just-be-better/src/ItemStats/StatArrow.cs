using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using TMPro;

namespace JustBeBetter.ItemStats
{
    // What sits between a stat's now and after values, worked out once since every tooltip and card shares a font
    internal static class StatArrow
    {
        private const char Arrow = '→';
        private const string Fallback = " to ";

        private static string? text;
        private static bool watchingLanguage;

        // The author's choice is the arrow, "to" only for a font without one
        // Tooltip and card text switch to the language's font, so that's the one checked, again after a change
        // of language since some languages use another font
        // A dynamic font only has a glyph in its atlas once it's added from the font file, so the check may add it
        internal static string Text(ManualLogSource log, TMP_FontAsset? fallbackFont)
        {
            if (!watchingLanguage)
            {
                watchingLanguage = true;
                Language.onCurrentLanguageChanged += () => text = null;
            }

            if (text != null)
            {
                return text;
            }

            TMP_FontAsset? font = HGTextMeshProUGUI.defaultLanguageFont;
            if (font == null)
            {
                font = fallbackFont;
            }

            // Not cached, the language's font may just not be loaded yet
            if (font == null)
            {
                log.LogWarning("No text font found yet, the stats use \"to\" for now");
                return Fallback;
            }

            bool inAtlas = font.HasCharacter(Arrow, true);
            bool added = inAtlas || font.HasCharacter(Arrow, true, true);
            // The fallback font draws it long, so it's shrunk and lifted back to the middle of the line
            text = added ? $" <size=80%><voffset=0.05em>{Arrow}</voffset></size> " : Fallback;
            log.LogInfo(
                $"Stat arrow in font {font.name} ({font.atlasPopulationMode}): already there {inAtlas}, added from "
                + $"the font file {added}, so the stats use {(added ? "the arrow" : "\"to\"")}"
                + (added ? string.Empty : $". Others the font could show: {Others(font)}"));
            return text;
        }

        // Which arrow-like characters the font could show instead, for choosing one if the arrow is missing
        // Only asked then, since asking adds each one found to the game's font
        private static string Others(TMP_FontAsset font)
        {
            var found = new List<string>();
            foreach (char c in "⇒➜►▶»›")
            {
                found.Add($"U+{(int)c:X4} {(font.HasCharacter(c, true, true) ? "yes" : "no")}");
            }

            return string.Join(", ", found);
        }
    }
}
