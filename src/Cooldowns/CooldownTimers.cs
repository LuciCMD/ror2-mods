using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using JustBeBetter.Shared;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;

namespace JustBeBetter.Cooldowns
{
    // Skill and equipment icons show their countdown whenever a charge is refilling, set after the game's own
    // update each frame so the game's rules stay as they are and ours only add to them
    internal sealed class CooldownTimers : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly CooldownSettings settings;
        private readonly ManualLogSource log;

        // The number each skill icon's text was last given, as the game only rewrites it when its own changes
        private readonly ConditionalWeakTable<TextMeshProUGUI, StrongBox<int>> written = new ConditionalWeakTable<TextMeshProUGUI, StrongBox<int>>();

        // Equipment icons whose text we showed, so turning the setting off hands them back
        private readonly ConditionalWeakTable<EquipmentIcon, object> forced = new ConditionalWeakTable<EquipmentIcon, object>();

        private FieldInfo? displayField;
        private FieldInfo? equipmentDefField;
        private FieldInfo? cooldownField;
        private FieldInfo? stockField;
        private FieldInfo? maxStockField;
        private FieldInfo? disabledField;
        private bool skillFailed;
        private bool equipmentFailed;

        internal CooldownTimers(CooldownSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            IL.RoR2.UI.SkillIcon.LateUpdate += KeepSkillCountdown;
            On.RoR2.UI.SkillIcon.LateUpdate += OnSkillLateUpdate;

            displayField = typeof(EquipmentIcon).GetField("currentDisplayData", Private);
            Type? data = displayField?.FieldType;
            equipmentDefField = data?.GetField("equipmentDef", Any);
            cooldownField = data?.GetField("cooldownValue", Any);
            stockField = data?.GetField("stock", Any);
            maxStockField = data?.GetField("maxStock", Any);
            disabledField = data?.GetField("equipmentDisabled", Any);
            if (equipmentDefField == null || cooldownField == null || stockField == null || maxStockField == null || disabledField == null)
            {
                log.LogWarning("The equipment icon's internals have changed, its countdown stays as the game shows it");
                return;
            }

            On.RoR2.UI.EquipmentIcon.LateUpdate += OnEquipmentLateUpdate;
        }

        public void Disable()
        {
            IL.RoR2.UI.SkillIcon.LateUpdate -= KeepSkillCountdown;
            On.RoR2.UI.SkillIcon.LateUpdate -= OnSkillLateUpdate;
            On.RoR2.UI.EquipmentIcon.LateUpdate -= OnEquipmentLateUpdate;
        }

        // The game hides a skill's countdown every frame it has a charge left, so that hide becomes a keep here
        // Undoing it afterwards instead would switch the text off and on every frame, rebuilding it each time
        private void KeepSkillCountdown(ILContext il)
        {
            var cursor = new ILCursor(il);
            int patched = 0;
            while (cursor.TryGotoNext(
                MoveType.After,
                i => i.MatchLdfld<SkillIcon>("cooldownText"),
                i => i.MatchCallvirt<Component>("get_gameObject"),
                i => i.MatchLdcI4(0)))
            {
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Func<bool, SkillIcon, bool>>((active, icon) => active || KeepShown(icon));
                patched++;
            }

            if (patched == 0)
            {
                log.LogWarning("SkillIcon.LateUpdate has changed, a refilling skill's countdown is shown again after the game hides it");
                return;
            }

            log.Verbose($"Skill countdown kept shown at {patched} places in SkillIcon.LateUpdate");
        }

        // Runs inside the game's own update, so nothing may escape it
        private bool KeepShown(SkillIcon icon)
        {
            if (skillFailed || !settings.Skills.Value)
            {
                return false;
            }

            try
            {
                GenericSkill skill = icon.targetSkill;
                return skill && skill.skillDef && CooldownRules.SkillRecharging(
                    skill.stock, skill.maxStock, skill.cooldownRemaining, skill.rechargeRate, skill.isCooldownBlocked, skill.skillDef.hideCooldown);
            }
            catch (Exception e)
            {
                skillFailed = true;
                log.LogError($"Couldn't check a skill's countdown, skill countdowns stay as the game shows them: {e}");
                return false;
            }
        }

        private void OnSkillLateUpdate(On.RoR2.UI.SkillIcon.orig_LateUpdate orig, SkillIcon self)
        {
            orig(self);
            if (skillFailed || !settings.Skills.Value)
            {
                Forget(self.cooldownText);
                return;
            }

            try
            {
                ShowSkill(self);
            }
            catch (Exception e)
            {
                // Once, as it runs every frame for every icon
                skillFailed = true;
                log.LogError($"Couldn't show a skill's countdown, skill countdowns stay as the game shows them: {e}");
            }
        }

        private void ShowSkill(SkillIcon icon)
        {
            GenericSkill skill = icon.targetSkill;
            TextMeshProUGUI text = icon.cooldownText;
            if (!skill || !skill.skillDef || !text)
            {
                return;
            }

            float remaining = skill.cooldownRemaining;
            float rate = skill.rechargeRate;
            if (!CooldownRules.SkillRecharging(skill.stock, skill.maxStock, remaining, rate, skill.isCooldownBlocked, skill.skillDef.hideCooldown))
            {
                Forget(text);
                return;
            }

            int seconds = CooldownRules.SkillSeconds(remaining, rate);
            StrongBox<int> last = written.GetValue(text, _ => new StrongBox<int>(-1));
            if (last.Value != seconds || !text.gameObject.activeSelf)
            {
                last.Value = seconds;
                text.SetText("{0}", seconds);
            }

            text.gameObject.SetActive(true);
        }

        // While it isn't ours the game may write the text, so the next number we show is always written
        private void Forget(TextMeshProUGUI? text)
        {
            if (text != null && written.TryGetValue(text, out StrongBox<int> last))
            {
                last.Value = -1;
            }
        }

        private void OnEquipmentLateUpdate(On.RoR2.UI.EquipmentIcon.orig_LateUpdate orig, EquipmentIcon self)
        {
            orig(self);
            if (equipmentFailed)
            {
                return;
            }

            try
            {
                ShowEquipment(self);
            }
            catch (Exception e)
            {
                equipmentFailed = true;
                log.LogError($"Couldn't show the equipment's countdown, it stays as the game shows it: {e}");
            }
        }

        // The game sets the number itself whenever it changes, so only whether it shows is ours
        private void ShowEquipment(EquipmentIcon icon)
        {
            TextMeshProUGUI text = icon.cooldownText;
            bool on = settings.Equipment.Value;
            bool wasForced = forced.TryGetValue(icon, out _);
            if (!text || (!on && !wasForced))
            {
                return;
            }

            object data = displayField!.GetValue(icon);
            if ((bool)disabledField!.GetValue(data))
            {
                return;
            }

            bool hasEquipment = equipmentDefField!.GetValue(data) is EquipmentDef def && def;
            int stock = (int)stockField!.GetValue(data);
            int cooldown = (int)cooldownField!.GetValue(data);
            bool gameShows = CooldownRules.EquipmentGameShows(hasEquipment, stock, cooldown);
            bool ours = on && CooldownRules.EquipmentRecharging(hasEquipment, false, stock, (int)maxStockField!.GetValue(data), cooldown);
            text.gameObject.SetActive(gameShows || ours);
            if (ours && !wasForced)
            {
                forced.Add(icon, icon);
            }
            else if (!ours && wasForced)
            {
                forced.Remove(icon);
            }
        }
    }
}
