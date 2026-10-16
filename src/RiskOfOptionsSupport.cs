using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.Safety;
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

        // The loading bar's blue
        private const string HeadingColour = "#7FB8FF";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register(Settings settings, ManualLogSource log)
        {
            ModSettingsManager.SetModDescription(
                "Quality of life for item pickers, item, skill and buff info, pickup cards, inventory sorting, the "
                + "scoreboard, a stats panel, chests, the lobby, saved runs, the cursor, toggle fire, loading and "
                + "equipment sounds. "
                + "Turning a part on or off applies from the next launch.",
                JustBeBetterPlugin.PluginGuid,
                JustBeBetterPlugin.PluginName);

            Sprite? icon = LoadIcon();
            if (icon != null)
            {
                ModSettingsManager.SetModIcon(icon, JustBeBetterPlugin.PluginGuid, JustBeBetterPlugin.PluginName);
            }

            // Risk Of Options makes a tab per config section and shows four to a page, so parts share five tabs
            // here while the config file keeps a section per part, tabs in the order first added
            var general = new Page("General", log);
            // First, before any part's switch it could grey out under
            general.CheckBox(settings.VerboseLogging);
            general.Part(settings.LobbyEnabled);
            general.Part(settings.SavesEnabled);

            general.Part(settings.Cursor.Enabled);
            general.CheckBox(settings.Cursor.FreeInMenus);
            general.CheckBox(settings.Cursor.FreeWhileLoading);

            general.Part(settings.ToggleFire.Enabled);
            general.Seconds(settings.ToggleFire.TapSeconds, ToggleFire.ToggleFireSettings.MinTapSeconds, ToggleFire.ToggleFireSettings.MaxTapSeconds, 0.05f, format: "{0:0.00} s");
            general.CheckBox(settings.ToggleFire.ShowMark);

            // All act during startup, so a change only shows on the next launch
            general.Part(settings.Loading.Enabled);
            general.CheckBox(settings.Loading.FasterEffectScan, restart: true);
            general.CheckBox(settings.Loading.BundleCache, restart: true);
            general.CheckBox(settings.Loading.SkipSplash, restart: true);
            general.CheckBox(settings.Loading.SkipIntro, restart: true);

            general.Part(settings.Sounds.Enabled);
            general.Percent(settings.Sounds.ChimeVolume);
            general.Percent(settings.Sounds.SoundVolume);
            general.Percent(settings.Sounds.WarHornVolume);

            var pickers = new Page("Item Pickers", log);
            Pickers.PickerSettings picker = settings.Pickers;
            pickers.Part(picker.Enabled);
            pickers.CheckBox(picker.ItemCounts);
            pickers.CheckBox(picker.HideZeroCounts, () => picker.ItemCounts.Value);
            pickers.CheckBox(picker.CorruptedMark);
            pickers.CheckBox(picker.HoverTooltips);
            pickers.CheckBox(picker.FitBigLists);
            pickers.IntSlider(picker.ItemsPerRow, Pickers.PickerSettings.MaxItemsPerRow, () => picker.FitBigLists.Value);
            pickers.CheckBox(picker.WindowBlur);
            pickers.Percent(picker.WindowOpacity);
            pickers.CheckBox(picker.CloseOnMovement);
            pickers.Choice(picker.CommandSorting, Pickers.PickerSettings.OrderLabels);
            pickers.Choice(picker.ScrapperSorting, Pickers.PickerSettings.OrderLabels);
            pickers.Choice(picker.CraftingSorting, Pickers.PickerSettings.OrderLabels);
            pickers.CheckBox(picker.UsableFirst);

            // Grace follows any protection, the solo pause included, as in Protection
            SafetySettings safety = settings.Safety;
            pickers.Part(safety.Enabled);
            pickers.Choice(safety.SoloMode);
            pickers.CheckBox(safety.ProtectInMultiplayer);
            pickers.CheckBox(safety.CloakWhilePicking, () => safety.SoloMode.Value == SoloMode.Protect || safety.ProtectInMultiplayer.Value);
            pickers.Seconds(safety.GraceSeconds, 0f, SafetySettings.MaxGraceSeconds, 0.5f, () => safety.SoloMode.Value != SoloMode.Off || safety.ProtectInMultiplayer.Value);

            var items = new Page("Items", log);
            ItemInfo.ItemInfoSettings info = settings.ItemInfo;
            items.Part(info.Enabled);
            items.CheckBox(info.FullDescriptions);
            items.CheckBox(info.ItemStats);
            // Not tied to Item Stats, the pickers and skill tooltips cap with it too, so it only greys once neither
            // Item Info nor Skill Info's item procs can use it
            SkillInfo.SkillInfoSettings skills = settings.SkillInfo;
            Func<bool> capUsed = () => info.Enabled.Value || (skills.Enabled.Value && skills.ItemProcs.Value);
            items.CheckBox(info.CapChance, capUsed, ownSwitch: false);
            items.CheckBox(info.PickupDetails);
            items.CheckBox(info.PingInfo);
            items.CheckBox(info.OtherPlayersPings, () => info.PingInfo.Value, "Pings from Other Players");
            items.CheckBox(info.ShowHiddenItems, () => info.PingInfo.Value);
            items.CheckBox(info.AllyCardInfo);

            // Not tied to Show Pickup Cards, void and ping cards still show with it off
            items.Part(settings.Notifications.Enabled);
            items.CheckBox(settings.Notifications.ShowPickupCards);
            items.Seconds(settings.Notifications.CardDuration, Notifications.CardRules.MinSeconds, Notifications.CardRules.MaxSeconds, 0.5f);

            items.Part(settings.Hidden.Enabled);
            items.CheckBox(settings.Hidden.UnhideItems);
            items.CheckBox(settings.Hidden.UnhideBuffs);

            InventorySort.InventorySortSettings sort = settings.InventorySort;
            items.Part(sort.Enabled);
            items.Choice(sort.ScrapPosition, InventorySort.InventorySortSettings.ScrapLabels);
            items.Choice(sort.TierOrder, InventorySort.InventorySortSettings.TierLabels);
            items.CheckBox(sort.MixVoids, () => sort.TierOrder.Value != InventorySort.TierOrder.Off);
            items.Choice(sort.StackSize, InventorySort.InventorySortSettings.StackLabels);
            items.CheckBox(sort.SortRunReports);

            var hud = new Page("HUD", log);
            hud.Part(settings.SkillInfo.Enabled);
            hud.CheckBox(settings.SkillInfo.Cooldown, name: "Cooldown in Tooltip");
            hud.CheckBox(settings.SkillInfo.ProcCoefficient);
            hud.CheckBox(settings.SkillInfo.ExtraNotes);
            hud.CheckBox(settings.SkillInfo.ItemProcs);

            hud.Part(settings.Cooldowns.Enabled);
            hud.CheckBox(settings.Cooldowns.Skills);
            hud.CheckBox(settings.Cooldowns.Equipment);

            BuffInfo.BuffInfoSettings buffs = settings.Buffs;
            hud.Part(buffs.Enabled);
            hud.CheckBox(buffs.Timers);
            hud.CheckBox(buffs.OneDecimal, () => buffs.Timers.Value);
            hud.CheckBox(buffs.Descriptions);

            StatsDisplay.StatsDisplaySettings stats = settings.Stats;
            hud.Part(stats.Enabled);
            hud.Text(stats.MainText);
            hud.Text(stats.TabText);
            hud.CheckBox(stats.StatColours);
            hud.IntSlider(stats.DecimalPlaces, StatsDisplay.StatsDisplaySettings.MaxDecimals);
            hud.IntSlider(stats.FontSize, StatsDisplay.StatsDisplaySettings.MaxFontSize, name: "Stats Font Size");
            hud.Seconds(stats.DpsWindow, StatsDisplay.StatsDisplaySettings.MinDpsWindow, StatsDisplay.StatsDisplaySettings.MaxDpsWindow, 1f, format: "{0:0} s");

            // Greyed out while they'd have no effect, as LookingGlass did
            Scoreboard.ScoreboardSettings scoreboard = settings.Scoreboard;
            hud.Part(scoreboard.Enabled);
            hud.CheckBox(scoreboard.TierCounts);
            hud.CheckBox(scoreboard.TempTotal, () => !scoreboard.CountTempWithPermanent.Value);
            hud.CheckBox(scoreboard.TempPerTier, () => scoreboard.TierCounts.Value && !scoreboard.CountTempWithPermanent.Value);
            hud.CheckBox(scoreboard.CountTempWithPermanent);

            hud.Part(settings.ImmuneBar.Enabled);
            hud.Colour(settings.ImmuneBar.BarColour);
            hud.CheckBox(settings.ImmuneBar.BossBar);

            // How used ones go sits above the long list of kinds, so it isn't lost below it
            ChestTweaks.ChestTweaksSettings chests = settings.Chests;
            var chestsPage = new Page("Chests", log);
            Func<bool> anyOutline = () => chests.OutlineEntries.Any(e => e.Value);
            Func<bool> anyHide = () => chests.HideEntries.Any(e => e.Value);
            chestsPage.Part(chests.Enabled);
            chestsPage.Colour(chests.OutlineColour, anyOutline);
            foreach (ConfigEntry<bool> entry in chests.OutlineEntries)
            {
                chestsPage.CheckBox(entry);
            }

            chestsPage.CheckBox(chests.Fade, anyHide);
            chestsPage.Seconds(chests.HideDelay, 0f, ChestTweaks.ChestTweaksSettings.MaxHideDelay, 0.5f, anyHide);
            chestsPage.CheckBox(chests.KeepAsDrifter, anyHide);
            foreach (ConfigEntry<bool> entry in chests.HideEntries)
            {
                chestsPage.CheckBox(entry);
            }
        }

        // Risk Of Options keeps its text in an internal table read before the game's own, reached by reflection,
        // null once it has changed
        private static MethodInfo? FindSetText()
        {
            return typeof(ModSettingsManager).Assembly.GetType("RiskOfOptions.Lib.LanguageApi")
                ?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string) }, null);
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

        // One tab, filled in order, each part's switch heading the options after it, which grey out while it's off
        private sealed class Page
        {
            private readonly string tab;
            private readonly ManualLogSource log;
            private ConfigEntry<bool>? part;

            internal Page(string tab, ManualLogSource log)
            {
                this.tab = tab;
                this.log = log;
            }

            // Named after its section, since every part's switch is called Enabled in the config
            internal void Part(ConfigEntry<bool> enabled)
            {
                part = enabled;
                string name = enabled.Definition.Section;
                var option = new CheckBoxOption(enabled, new CheckBoxConfig { category = tab, name = name, restartRequired = true });
                Add(option);
                SetText(option.GetNameToken(), $"<b><color={HeadingColour}>{name.ToUpperInvariant()}</color></b>", name);
            }

            // ownSwitch false for an option another part reads too, its needs then decide alone
            internal void CheckBox(ConfigEntry<bool> entry, Func<bool>? needs = null, string? name = null, bool restart = false, bool ownSwitch = true)
            {
                Add(new CheckBoxOption(entry, Prepare(new CheckBoxConfig { restartRequired = restart }, needs, name, ownSwitch)));
            }

            internal void Choice(ConfigEntryBase entry, string[]? labels = null)
            {
                var option = new ChoiceOption(entry, Prepare(new ChoiceConfig(), null, null));
                Add(option);
                if (labels != null)
                {
                    Label(option, labels);
                }
            }

            internal void IntSlider(ConfigEntry<int> entry, int max, Func<bool>? needs = null, string? name = null)
            {
                Add(new IntSliderOption(entry, Prepare(new IntSliderConfig { min = 0, max = max }, needs, name)));
            }

            internal void Percent(ConfigEntry<int> entry)
            {
                Add(new IntSliderOption(entry, Prepare(new IntSliderConfig { min = 0, max = 100, formatString = "{0}%" }, null, null)));
            }

            internal void Seconds(ConfigEntry<float> entry, float min, float max, float step, Func<bool>? needs = null, string format = "{0:0.0} s")
            {
                var config = new StepSliderConfig { min = min, max = max, increment = step, FormatString = format };
                Add(new StepSliderOption(entry, Prepare(config, needs, null)));
            }

            internal void Colour(ConfigEntry<Color> entry, Func<bool>? needs = null)
            {
                Add(new ColorOption(entry, Prepare(new ColorOptionConfig(), needs, null)));
            }

            // Several lines, with the tags shown as typed rather than drawn, saved when the field is left
            internal void Text(ConfigEntry<string> entry)
            {
                var config = new InputFieldConfig
                {
                    lineType = TMP_InputField.LineType.MultiLineNewline,
                    submitOn = InputFieldConfig.SubmitEnum.OnExitOrSubmit,
                    richText = false,
                };
                Add(new StringInputFieldOption(entry, Prepare(config, null, null)));
            }

            private static void Add(BaseOption option)
            {
                ModSettingsManager.AddOption(option, JustBeBetterPlugin.PluginGuid, JustBeBetterPlugin.PluginName);
            }

            private T Prepare<T>(T config, Func<bool>? needs, string? name, bool ownSwitch = true)
                where T : BaseOptionConfig
            {
                ConfigEntry<bool>? owner = ownSwitch ? part : null;
                config.category = tab;
                if (name != null)
                {
                    config.name = name;
                }

                config.checkIfDisabled = () => (owner != null && !owner.Value) || (needs != null && !needs());
                return config;
            }

            // Risk Of Options shows a choice by its enum name, from tokens it registers in AddOption
            private void Label(ChoiceOption option, string[] labels)
            {
                try
                {
                    MethodInfo? getTokens = typeof(ChoiceOption).GetMethod("GetNameTokens", AnyInstance);
                    MethodInfo? setText = FindSetText();
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

            // Falls back to the plain name Risk Of Options already registered
            private void SetText(string token, string text, string name)
            {
                try
                {
                    MethodInfo? setText = FindSetText();
                    if (setText == null)
                    {
                        log.LogWarning($"Risk Of Options has changed, {name} shows as a plain switch rather than a heading");
                        return;
                    }

                    setText.Invoke(null, new object[] { token, text });
                }
                // Anything, as a throw here would stop every part from starting over a heading
                catch (Exception e)
                {
                    log.LogWarning($"Couldn't make {name} a heading in Risk Of Options, it shows as a plain switch: {e}");
                }
            }
        }
    }
}
