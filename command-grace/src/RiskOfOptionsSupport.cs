using System.IO;
using System.Runtime.CompilerServices;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace CommandGrace
{
    // Only touched after checking Risk Of Options is loaded; any other use would fail to resolve its types
    internal static class RiskOfOptionsSupport
    {
        internal const string PluginGuid = "com.rune580.riskofoptions";

        private const string IconResource = "CommandGrace.icon.png";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings)
        {
            ModSettingsManager.SetModDescription(
                "Keeps you safe while you pick an item: pauses the world in solo and protects the picker in "
                + "multiplayer.",
                CommandGracePlugin.PluginGuid,
                CommandGracePlugin.PluginName);

            Sprite? icon = LoadIcon();
            if (icon != null)
            {
                ModSettingsManager.SetModIcon(icon, CommandGracePlugin.PluginGuid, CommandGracePlugin.PluginName);
            }

            Add(new ChoiceOption(settings.SoloMode));
            Add(new CheckBoxOption(settings.ProtectInMultiplayer));
            Add(new CheckBoxOption(settings.CloakWhilePicking));
            Add(new StepSliderOption(
                settings.GraceSeconds,
                new StepSliderConfig
                {
                    min = 0f,
                    max = Settings.MaxGraceSeconds,
                    increment = 0.5f,
                    FormatString = "{0:0.0} s",
                }));
        }

        private static void Add(BaseOption option)
        {
            ModSettingsManager.AddOption(option, CommandGracePlugin.PluginGuid, CommandGracePlugin.PluginName);
        }

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

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
        }
    }
}
