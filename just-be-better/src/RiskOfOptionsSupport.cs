using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace JustBeBetter
{
    // Only touched after checking Risk Of Options is loaded, any other use would fail to resolve its types
    internal static class RiskOfOptionsSupport
    {
        internal const string PluginGuid = "com.rune580.riskofoptions";

        private const string IconResource = "JustBeBetter.icon.png";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings)
        {
            ModSettingsManager.SetModDescription(
                "Small fixes and additions for item pickers, the lobby, saving runs, the mouse cursor, loading "
                + "and equipment sounds. Turning a section on or off applies from the next launch.",
                JustBeBetterPlugin.PluginGuid,
                JustBeBetterPlugin.PluginName);

            Sprite? icon = LoadIcon();
            if (icon != null)
            {
                ModSettingsManager.SetModIcon(icon, JustBeBetterPlugin.PluginGuid, JustBeBetterPlugin.PluginName);
            }

            Enabled(settings.Safety.Enabled);
            Add(new ChoiceOption(settings.Safety.SoloMode));
            Add(new CheckBoxOption(settings.Safety.ProtectInMultiplayer));
            Add(new CheckBoxOption(settings.Safety.CloakWhilePicking));
            Add(new StepSliderOption(
                settings.Safety.GraceSeconds,
                new StepSliderConfig
                {
                    min = 0f,
                    max = Safety.SafetySettings.MaxGraceSeconds,
                    increment = 0.5f,
                    FormatString = "{0:0.0} s",
                }));

            Enabled(settings.Pickers.Enabled);
            Add(new CheckBoxOption(settings.Pickers.ItemCounts));
            Add(new CheckBoxOption(settings.Pickers.HideZeroCounts));
            Add(new CheckBoxOption(settings.Pickers.CorruptedMark));
            Add(new CheckBoxOption(settings.Pickers.HoverTooltips));

            Enabled(settings.ItemInfo.Enabled);
            Add(new CheckBoxOption(settings.ItemInfo.FullDescriptions));
            Add(new CheckBoxOption(settings.ItemInfo.ItemStats));
            Add(new CheckBoxOption(settings.ItemInfo.CapChance));
            Add(new CheckBoxOption(settings.ItemInfo.PickupDetails));
            Enabled(settings.LobbyEnabled);
            Enabled(settings.SavesEnabled);

            Enabled(settings.Cursor.Enabled);
            Add(new CheckBoxOption(settings.Cursor.FreeInMenus));
            Add(new CheckBoxOption(settings.Cursor.FreeWhileLoading));

            // Both act during startup, so a change only shows on the next launch
            Enabled(settings.Loading.Enabled);
            Add(new CheckBoxOption(settings.Loading.FasterEffectScan, new CheckBoxConfig { restartRequired = true }));
            Add(new CheckBoxOption(settings.Loading.BundleCache, new CheckBoxConfig { restartRequired = true }));

            Enabled(settings.Sounds.Enabled);
            Add(new IntSliderOption(settings.Sounds.ChimeVolume, Percent()));
            Add(new IntSliderOption(settings.Sounds.SoundVolume, Percent()));
        }

        private static void Enabled(ConfigEntry<bool> entry)
        {
            Add(new CheckBoxOption(entry, new CheckBoxConfig { restartRequired = true }));
        }

        private static IntSliderConfig Percent()
        {
            return new IntSliderConfig { min = 0, max = 100, formatString = "{0}%" };
        }

        private static void Add(BaseOption option)
        {
            ModSettingsManager.AddOption(option, JustBeBetterPlugin.PluginGuid, JustBeBetterPlugin.PluginName);
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
