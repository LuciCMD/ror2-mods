using System.IO;
using System.Runtime.CompilerServices;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace EquipmentBeQuiet
{
    // Only touched after checking Risk Of Options is loaded; any other use would fail to resolve its types
    internal static class RiskOfOptionsSupport
    {
        internal const string PluginGuid = "com.rune580.riskofoptions";

        private const string IconResource = "EquipmentBeQuiet.icon.png";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings)
        {
            ModSettingsManager.SetModDescription(
                "Turns down or mutes the sounds equipment makes when used, for runs where Gesture of the Drowned "
                + "fires it constantly.",
                EquipmentBeQuietPlugin.PluginGuid,
                EquipmentBeQuietPlugin.PluginName);

            Sprite? icon = LoadIcon();
            if (icon != null)
            {
                ModSettingsManager.SetModIcon(icon, EquipmentBeQuietPlugin.PluginGuid, EquipmentBeQuietPlugin.PluginName);
            }

            Add(new IntSliderOption(settings.ChimeVolume, Slider()));
            Add(new IntSliderOption(settings.SoundVolume, Slider()));
        }

        private static IntSliderConfig Slider()
        {
            return new IntSliderConfig { min = 0, max = 100, formatString = "{0}%" };
        }

        private static void Add(BaseOption option)
        {
            ModSettingsManager.AddOption(option, EquipmentBeQuietPlugin.PluginGuid, EquipmentBeQuietPlugin.PluginName);
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
