using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.ImmuneBar
{
    // IMMUNE in place of the health numbers while nothing can hurt the bar's owner, as HealthbarImmune did
    // The numbers are only rewritten by the game when health changes, so they're put back here when it ends
    internal sealed class ImmuneHealthBar : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ImmuneToken = "IMMUNE_TO_DAMAGE_HITMARKER";

        private readonly ImmuneBarSettings settings;
        private readonly ManualLogSource log;

        // Bars showing IMMUNE now, with what they had before
        private readonly ConditionalWeakTable<HealthBar, Saved> shown = new ConditionalWeakTable<HealthBar, Saved>();
        private readonly ConditionalWeakTable<HUDBossHealthBarController, SavedBoss> shownBoss =
            new ConditionalWeakTable<HUDBossHealthBarController, SavedBoss>();

        private readonly HashSet<string> loggedReasons = new HashSet<string>(StringComparer.Ordinal);

        private FieldInfo? barsField;
        private FieldInfo? trailingField;
        private FieldInfo? bossGroupField;
        private FieldInfo? oldTotalField;
        private FieldInfo? oldMaxField;
        private bool failedOnce;
        private bool failedBossOnce;
        private bool warnedNoSlash;

        internal ImmuneHealthBar(ImmuneBarSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            barsField = typeof(HealthBar).GetField("barInfoCollection", Private);
            trailingField = barsField?.FieldType.GetField("trailingOverHealthbarInfo", Any);
            if (barsField == null || trailingField == null || trailingField.FieldType != typeof(HealthBar.BarInfo))
            {
                trailingField = null;
                log.LogWarning("The health bar's internals have changed, IMMUNE shows without the colour");
            }

            bossGroupField = typeof(HUDBossHealthBarController).GetField("currentBossGroup", Private);
            if (bossGroupField == null)
            {
                log.LogWarning("The boss bar's internals have changed, it never shows IMMUNE");
            }

            oldTotalField = typeof(HUDBossHealthBarController).GetField("oldTotalHealth", Private);
            oldMaxField = typeof(HUDBossHealthBarController).GetField("oldTotalMaxHealth", Private);
            if (oldTotalField?.FieldType != typeof(int) || oldMaxField?.FieldType != typeof(int))
            {
                oldTotalField = null;
                oldMaxField = null;
                log.LogWarning("The boss bar's cached totals have changed, its numbers may lag after IMMUNE");
            }

            // Both would write the same text every frame
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.DestroyedClone.HealthbarImmune"))
            {
                log.LogWarning("HealthbarImmune is installed too, uninstall it or turn this section off");
            }

            On.RoR2.UI.HealthBar.UpdateBarInfos += OnUpdateBarInfos;
            On.RoR2.UI.HUDBossHealthBarController.LateUpdate += OnBossLateUpdate;
        }

        public void Disable()
        {
            On.RoR2.UI.HealthBar.UpdateBarInfos -= OnUpdateBarInfos;
            On.RoR2.UI.HUDBossHealthBarController.LateUpdate -= OnBossLateUpdate;
            RestoreAllBars();
            RestoreAllBosses();
        }

        // After a fault too, so no bar stays on IMMUNE once this part stops
        private void RestoreAllBars()
        {
            foreach (HealthBar bar in UnityEngine.Object.FindObjectsOfType<HealthBar>())
            {
                if (shown.TryGetValue(bar, out Saved saved))
                {
                    Restore(bar, saved);
                }
            }
        }

        private void RestoreAllBosses()
        {
            foreach (HUDBossHealthBarController boss in UnityEngine.Object.FindObjectsOfType<HUDBossHealthBarController>())
            {
                if (shownBoss.TryGetValue(boss, out SavedBoss saved))
                {
                    RestoreBoss(boss, saved);
                }
            }
        }

        private void TryRestore(Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't put every bar back, one may read IMMUNE until the next stage: {e.Message}");
            }
        }

        private void OnUpdateBarInfos(On.RoR2.UI.HealthBar.orig_UpdateBarInfos orig, HealthBar self)
        {
            orig(self);
            if (failedOnce)
            {
                return;
            }

            try
            {
                UpdateBar(self);
            }
            catch (Exception e)
            {
                // Every bar runs this every frame, so a lasting fault is reported once
                failedOnce = true;
                log.LogError($"Couldn't mark an immune health bar, bars stay as the game shows them from here: {e}");
                TryRestore(RestoreAllBars);
            }
        }

        private void UpdateBar(HealthBar bar)
        {
            if (!bar)
            {
                return;
            }

            bool wasShown = shown.TryGetValue(bar, out Saved saved);
            HealthComponent? source = bar.source;
            CharacterBody? body = source ? source.body : null;

            // A body that died holding an Immune buff shows its 0 like any other
            string? reason = body != null && body && source!.alive ? Reason(source, body) : null;

            // Only bars that show numbers, which is the HUD's
            TextMeshProUGUI? slash = reason != null ? SlashOf(bar) : null;
            if (reason == null || slash == null)
            {
                if (wasShown)
                {
                    Restore(bar, saved);
                }
            }

            if (reason == null)
            {
                return;
            }

            if (slash == null)
            {
                if (!warnedNoSlash && (bar.currentHealthText || bar.spriteAsNumberManager) && bar.GetComponentInParent<HUD>())
                {
                    warnedNoSlash = true;
                    log.LogWarning("The HUD's health bar has no Slash text any more, so it can't show IMMUNE");
                }

                return;
            }

            if (!wasShown)
            {
                saved = new Saved(slash.text, slash.gameObject.activeSelf, bar.spriteAsNumberManager && bar.spriteAsNumberManager.gameObject.activeSelf);
                shown.Add(bar, saved);
                LogFirst(reason, body!);
            }

            string word = Language.GetString(ImmuneToken);
            SetText(bar.currentHealthText, string.Empty);
            SetText(bar.fullHealthText, string.Empty);
            if (bar.spriteAsNumberManager && bar.spriteAsNumberManager.gameObject.activeSelf)
            {
                bar.spriteAsNumberManager.gameObject.SetActive(false);
            }

            if (!slash.gameObject.activeSelf)
            {
                slash.gameObject.SetActive(true);
            }

            SetText(slash, word);
            Colour(bar, settings.BarColour.Value);
        }

        // As the game writes them, so nothing jumps when it next does
        // The colour needs no putting back, the game sets it again every frame
        private void Restore(HealthBar bar, Saved saved)
        {
            shown.Remove(bar);
            TextMeshProUGUI? slash = SlashOf(bar);
            if (slash != null)
            {
                SetText(slash, saved.SlashText);
                slash.gameObject.SetActive(saved.SlashActive);
            }

            HealthComponent? source = bar.source;
            if (source)
            {
                // Floats as the game formats them, so huge health reads 1E+07 as it does there
                float current = Mathf.Ceil(source!.combinedHealth);
                float full = Mathf.Ceil(source.fullHealth);
                SetText(bar.currentHealthText, current.ToString(System.Globalization.CultureInfo.CurrentCulture));
                SetText(bar.fullHealthText, full.ToString(System.Globalization.CultureInfo.CurrentCulture));
                if (bar.spriteAsNumberManager)
                {
                    bar.spriteAsNumberManager.SetHitPointValues((int)current, (int)full);
                }
            }

            if (bar.spriteAsNumberManager)
            {
                bar.spriteAsNumberManager.gameObject.SetActive(saved.SpriteActive);
            }
        }

        private void Colour(HealthBar bar, Color colour)
        {
            if (trailingField == null)
            {
                return;
            }

            // A boxed copy changed in place, then written back
            object bars = barsField!.GetValue(bar);
            var info = (HealthBar.BarInfo)trailingField.GetValue(bars);
            info.color = colour;
            trailingField.SetValue(bars, info);
            barsField.SetValue(bar, bars);
        }

        private void OnBossLateUpdate(On.RoR2.UI.HUDBossHealthBarController.orig_LateUpdate orig, HUDBossHealthBarController self)
        {
            orig(self);
            if (failedBossOnce || bossGroupField == null)
            {
                return;
            }

            try
            {
                UpdateBoss(self);
            }
            catch (Exception e)
            {
                failedBossOnce = true;
                log.LogError($"Couldn't mark the boss bar as immune, it stays as the game shows it from here: {e}");
                TryRestore(RestoreAllBosses);
            }
        }

        private void UpdateBoss(HUDBossHealthBarController boss)
        {
            if (!boss)
            {
                return;
            }

            bool wasShown = shownBoss.TryGetValue(boss, out SavedBoss saved);
            var group = bossGroupField!.GetValue(boss) as BossGroup;
            string? reason = settings.BossBar.Value && group != null && group && boss.container && boss.container.activeSelf
                ? GroupReason(group)
                : null;
            if (reason == null)
            {
                if (wasShown)
                {
                    RestoreBoss(boss, saved);
                }

                return;
            }

            if (!wasShown)
            {
                shownBoss.Add(boss, new SavedBoss(boss.fillRectImage ? boss.fillRectImage.color : Color.white));
                LogFirst("boss " + reason, null);
            }

            SetText(boss.healthLabel, Language.GetString(ImmuneToken));
            if (boss.fillRectImage)
            {
                boss.fillRectImage.color = settings.BarColour.Value;
            }
        }

        // The game only rewrites the label when its cached totals change, so they're cleared to make it write the
        // group it shows now, which may not be the one that was immune, and that group is written here meanwhile
        private void RestoreBoss(HUDBossHealthBarController boss, SavedBoss saved)
        {
            shownBoss.Remove(boss);
            if (boss.fillRectImage)
            {
                boss.fillRectImage.color = saved.FillColour;
            }

            oldTotalField?.SetValue(boss, -1);
            oldMaxField?.SetValue(boss, -1);
            var group = bossGroupField?.GetValue(boss) as BossGroup;
            if (boss.healthLabel && group != null && group)
            {
                boss.healthLabel.SetText(
                    $"{Mathf.FloorToInt(group.totalObservedHealth)}/{Mathf.FloorToInt(group.totalMaxObservedMaxHealth)}");
            }
        }

        private static string? GroupReason(BossGroup group)
        {
            CombatSquad squad = group.combatSquad;
            if (!squad)
            {
                return null;
            }

            var reasons = new List<string?>();
            foreach (CharacterMaster master in squad.readOnlyMembersList)
            {
                CharacterBody? body = master ? master.GetBody() : null;
                if (body != null && body && body.healthComponent && body.healthComponent.alive)
                {
                    reasons.Add(Reason(body.healthComponent, body));
                }
            }

            return ImmuneRules.AllImmune(reasons.Select(r => r != null)) ? reasons[0] : null;
        }

        private static string? Reason(HealthComponent health, CharacterBody body)
        {
            return ImmuneRules.Reason(new ImmuneFlags(
                health.godMode,
                Has(body, RoR2Content.Buffs.Immune),
                Has(body, RoR2Content.Buffs.HiddenInvincibility),
                Has(body, DLC2Content.Buffs.HiddenRejectAllDamage),
                Has(body, DLC2Content.Buffs.SojournVehicle),
                Has(body, RoR2Content.Buffs.BrotherThroneShield)));
        }

        // A DLC's buffs are null when its content didn't load
        private static bool Has(CharacterBody body, BuffDef? buff)
        {
            return buff != null && buff && body.HasBuff(buff);
        }

        private static TextMeshProUGUI? SlashOf(HealthBar bar)
        {
            Transform slash = bar.transform.Find("Slash");
            return slash ? slash.GetComponent<TextMeshProUGUI>() : null;
        }

        // TMP marks itself dirty on every set, so only on a change
        private static void SetText(TextMeshProUGUI? text, string value)
        {
            if (text != null && text && text.text != value)
            {
                text.text = value;
            }
        }

        private void LogFirst(string reason, CharacterBody? body)
        {
            if (Logs.VerboseOn && loggedReasons.Add(reason))
            {
                log.Verbose($"IMMUNE shown for {reason}{(body != null ? $" on {body.name}" : string.Empty)}");
            }
        }

        private sealed class Saved
        {
            internal Saved(string slashText, bool slashActive, bool spriteActive)
            {
                SlashText = slashText;
                SlashActive = slashActive;
                SpriteActive = spriteActive;
            }

            internal string SlashText { get; }

            internal bool SlashActive { get; }

            internal bool SpriteActive { get; }
        }

        private sealed class SavedBoss
        {
            internal SavedBoss(Color fillColour)
            {
                FillColour = fillColour;
            }

            internal Color FillColour { get; }
        }
    }
}
