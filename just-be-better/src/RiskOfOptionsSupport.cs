using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using TMPro;
using UnityEngine;

namespace JustBeBetter
{
    // Only touched after checking Risk Of Options is loaded, any other use would fail to resolve its types
    internal static class RiskOfOptionsSupport
    {
        internal const string PluginGuid = "com.rune580.riskofoptions";

        private const string IconResource = "JustBeBetter.icon.png";
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings, ManualLogSource log)
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
            Add(new CheckBoxOption(settings.Pickers.FitBigLists));
            Add(new IntSliderOption(settings.Pickers.ItemsPerRow, new IntSliderConfig { min = 0, max = Pickers.PickerSettings.MaxItemsPerRow }));
            Add(new CheckBoxOption(settings.Pickers.WindowBlur));
            Add(new IntSliderOption(settings.Pickers.WindowOpacity, Percent()));
            Add(new CheckBoxOption(settings.Pickers.CloseOnMovement));
            AddLabelled(new ChoiceOption(settings.Pickers.CommandSorting), Pickers.PickerSettings.OrderLabels, log);
            AddLabelled(new ChoiceOption(settings.Pickers.ScrapperSorting), Pickers.PickerSettings.OrderLabels, log);

            Enabled(settings.ItemInfo.Enabled);
            Add(new CheckBoxOption(settings.ItemInfo.FullDescriptions));
            Add(new CheckBoxOption(settings.ItemInfo.ItemStats));
            Add(new CheckBoxOption(settings.ItemInfo.CapChance));
            Add(new CheckBoxOption(settings.ItemInfo.PickupDetails));
            Add(new CheckBoxOption(settings.ItemInfo.PingInfo));
            Add(new CheckBoxOption(settings.ItemInfo.OtherPlayersPings));
            Add(new CheckBoxOption(settings.ItemInfo.ShowHiddenItems));

            Enabled(settings.Notifications.Enabled);
            Add(new CheckBoxOption(settings.Notifications.ShowPickupCards));
            Add(new StepSliderOption(
                settings.Notifications.CardDuration,
                new StepSliderConfig
                {
                    min = Notifications.CardRules.MinSeconds,
                    max = Notifications.CardRules.MaxSeconds,
                    increment = 0.5f,
                    FormatString = "{0:0.0} s",
                }));

            Enabled(settings.Hidden.Enabled);
            Add(new CheckBoxOption(settings.Hidden.UnhideItems));
            Add(new CheckBoxOption(settings.Hidden.UnhideBuffs));

            Enabled(settings.InventorySort.Enabled);
            AddLabelled(new ChoiceOption(settings.InventorySort.ScrapPosition), InventorySort.InventorySortSettings.ScrapLabels, log);
            AddLabelled(new ChoiceOption(settings.InventorySort.TierOrder), InventorySort.InventorySortSettings.TierLabels, log);
            Add(new CheckBoxOption(settings.InventorySort.MixVoids));
            AddLabelled(new ChoiceOption(settings.InventorySort.StackSize), InventorySort.InventorySortSettings.StackLabels, log);
            Add(new CheckBoxOption(settings.InventorySort.SortRunReports));

            Enabled(settings.SkillInfo.Enabled);
            Add(new CheckBoxOption(settings.SkillInfo.Cooldown));
            Add(new CheckBoxOption(settings.SkillInfo.ProcCoefficient));
            Add(new CheckBoxOption(settings.SkillInfo.ExtraNotes));
            Add(new CheckBoxOption(settings.SkillInfo.ItemProcs));

            Enabled(settings.Cooldowns.Enabled);
            Add(new CheckBoxOption(settings.Cooldowns.Skills));
            Add(new CheckBoxOption(settings.Cooldowns.Equipment));

            Enabled(settings.Buffs.Enabled);
            Add(new CheckBoxOption(settings.Buffs.Timers));
            Add(new CheckBoxOption(settings.Buffs.OneDecimal));
            Add(new CheckBoxOption(settings.Buffs.Descriptions));

            Enabled(settings.Stats.Enabled);
            Add(new StringInputFieldOption(settings.Stats.MainText, TemplateField()));
            Add(new StringInputFieldOption(settings.Stats.TabText, TemplateField()));
            Add(new CheckBoxOption(settings.Stats.StatColours));
            Add(new IntSliderOption(settings.Stats.DecimalPlaces, new IntSliderConfig { min = 0, max = StatsDisplay.StatsDisplaySettings.MaxDecimals }));
            Add(new IntSliderOption(settings.Stats.FontSize, new IntSliderConfig { min = 0, max = StatsDisplay.StatsDisplaySettings.MaxFontSize }));
            Add(new StepSliderOption(
                settings.Stats.DpsWindow,
                new StepSliderConfig
                {
                    min = StatsDisplay.StatsDisplaySettings.MinDpsWindow,
                    max = StatsDisplay.StatsDisplaySettings.MaxDpsWindow,
                    increment = 1f,
                    FormatString = "{0:0} s",
                }));

            // Greyed out while they'd have no effect, as LookingGlass did
            Scoreboard.ScoreboardSettings scoreboard = settings.Scoreboard;
            Enabled(scoreboard.Enabled);
            Add(new CheckBoxOption(scoreboard.TierCounts));
            Add(new CheckBoxOption(scoreboard.TempTotal, new CheckBoxConfig { checkIfDisabled = () => scoreboard.CountTempWithPermanent.Value }));
            Add(new CheckBoxOption(
                scoreboard.TempPerTier,
                new CheckBoxConfig { checkIfDisabled = () => !scoreboard.TierCounts.Value || scoreboard.CountTempWithPermanent.Value }));
            Add(new CheckBoxOption(scoreboard.CountTempWithPermanent));

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

        // Several lines, with the tags shown as typed rather than drawn, saved when the field is left
        private static InputFieldConfig TemplateField()
        {
            return new InputFieldConfig
            {
                lineType = TMP_InputField.LineType.MultiLineNewline,
                submitOn = InputFieldConfig.SubmitEnum.OnExitOrSubmit,
                richText = false,
            };
        }

        private static IntSliderConfig Percent()
        {
            return new IntSliderConfig { min = 0, max = 100, formatString = "{0}%" };
        }

        private static void Add(BaseOption option)
        {
            ModSettingsManager.AddOption(option, JustBeBetterPlugin.PluginGuid, JustBeBetterPlugin.PluginName);
        }

        // Risk Of Options shows a choice by its enum name, from tokens it registers in AddOption and reads
        // before the game's own text, both internal so they're reached by reflection
        private static void AddLabelled(ChoiceOption option, string[] labels, ManualLogSource log)
        {
            Add(option);
            try
            {
                MethodInfo? getTokens = typeof(ChoiceOption).GetMethod("GetNameTokens", AnyInstance);
                MethodInfo? setText = typeof(ModSettingsManager).Assembly.GetType("RiskOfOptions.Lib.LanguageApi")
                    ?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string) }, null);
                if (getTokens?.Invoke(option, null) is not string[] tokens || setText == null)
                {
                    log.LogWarning($"Risk Of Options has changed, {option.Name}'s choices show by their config names");
                    return;
                }

                for (int i = 0; i < tokens.Length && i < labels.Length; i++)
                {
                    setText.Invoke(null, new object[] { tokens[i], labels[i] });
                }
            }
            // Anything, as a throw here would stop every part from starting over a label
            catch (Exception e)
            {
                log.LogWarning($"Couldn't label {option.Name}'s choices in Risk Of Options, they show by their config names: {e}");
            }
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
