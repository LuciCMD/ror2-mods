using System.IO;
using System.Runtime.CompilerServices;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace JustLoad
{
    // Only touched after checking Risk Of Options is loaded; any other use would fail to resolve its types
    internal static class RiskOfOptionsSupport
    {
        internal const string PluginGuid = "com.rune580.riskofoptions";

        private const string IconResource = "JustLoad.icon.png";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings)
        {
            ModSettingsManager.SetModDescription(
                "Makes the game start faster. Both speed-ups apply from the next launch.",
                JustLoadPlugin.PluginGuid,
                JustLoadPlugin.PluginName);

            Sprite? icon = LoadIcon();
            if (icon != null)
            {
                ModSettingsManager.SetModIcon(icon, JustLoadPlugin.PluginGuid, JustLoadPlugin.PluginName);
            }

            // Both act during startup, so a change only shows on the next launch
            Add(new CheckBoxOption(settings.FasterEffectScan, new CheckBoxConfig { restartRequired = true }));
            Add(new CheckBoxOption(settings.BundleCache, new CheckBoxConfig { restartRequired = true }));
        }

        private static void Add(BaseOption option)
        {
            ModSettingsManager.AddOption(option, JustLoadPlugin.PluginGuid, JustLoadPlugin.PluginName);
        }

        // Rendered from thunderstore/icon.svg
        private static Sprite? LoadIcon()
        {
            using Stream? stream = typeof(RiskOfOptionsSupport).Assembly.GetManifestResourceStream(IconResource);
            if (stream == null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            // Size is replaced by the PNG's own on load
            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(buffer.ToArray()))
            {
                return null;
            }

            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
