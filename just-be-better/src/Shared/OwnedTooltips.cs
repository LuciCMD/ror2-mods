using System.Runtime.CompilerServices;
using RoR2.UI;
using UnityEngine;

namespace JustBeBetter.Shared
{
    // Tooltip text we write over the game's, kept apart from text another mod put there
    // A selected icon asks for its tooltip every frame, so text written moments ago for the same thing is kept
    internal sealed class OwnedTooltips
    {
        private const float Fresh = 0.5f;

        private readonly ConditionalWeakTable<TooltipProvider, Written> written = new ConditionalWeakTable<TooltipProvider, Written>();

        // Empty, or still what we last wrote
        internal bool Ours(TooltipProvider provider)
        {
            return string.IsNullOrEmpty(provider.overrideBodyText)
                || (written.TryGetValue(provider, out Written mine) && mine.Text == provider.overrideBodyText);
        }

        // The game reuses icons for other things, so the key has to match too
        internal bool IsFresh(TooltipProvider provider, string key)
        {
            return written.TryGetValue(provider, out Written mine)
                && mine.Key == key
                && mine.Text == provider.overrideBodyText
                && Time.unscaledTime - mine.Time < Fresh;
        }

        // Still showing what we wrote for this key
        internal bool Holds(TooltipProvider provider, string key)
        {
            return written.TryGetValue(provider, out Written mine) && mine.Key == key && mine.Text == provider.overrideBodyText;
        }

        // Stops tracking a tooltip whose text was handed back some other way
        internal void Forget(TooltipProvider provider)
        {
            written.Remove(provider);
        }

        internal void Write(TooltipProvider provider, string key, string text)
        {
            provider.overrideBodyText = text;
            written.Remove(provider);
            written.Add(provider, new Written(key, text, Time.unscaledTime));
        }

        // Back to the game's text, for an icon reused for something we leave alone or a setting turned off
        internal void Release(TooltipProvider provider)
        {
            if (written.TryGetValue(provider, out Written mine))
            {
                if (provider.overrideBodyText == mine.Text)
                {
                    provider.overrideBodyText = null;
                }

                written.Remove(provider);
            }
        }

        private sealed class Written
        {
            internal Written(string key, string text, float time)
            {
                Key = key;
                Text = text;
                Time = time;
            }

            internal string Key { get; }

            internal string Text { get; }

            internal float Time { get; }
        }
    }
}
