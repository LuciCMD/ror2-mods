using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace JustBeBetter.BuffInfo
{
    // Timers and hover text on every buff bar, after the game lays it out, which it does every few frames
    // Each bar reads its own body's timers, so an ally's or a boss's bar shows theirs, not yours
    internal sealed class BuffDetails : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int PercentDisplay = 1;

        private readonly BuffInfoSettings settings;
        private readonly ManualLogSource log;

        // Icons whose stack text we took over, so it's handed back once the buff isn't timed
        private readonly ConditionalWeakTable<BuffIcon, string> written = new ConditionalWeakTable<BuffIcon, string>();

        // Tooltips we added, the only ones turned off with Buff Descriptions
        private readonly ConditionalWeakTable<BuffIcon, TooltipProvider> tooltips = new ConditionalWeakTable<BuffIcon, TooltipProvider>();

        // Logged once per name, as copies of one bar share it
        private readonly HashSet<string> hoverCanvases = new HashSet<string>(StringComparer.Ordinal);

        private FieldInfo? sourceField;
        private FieldInfo? iconsField;
        private FieldInfo? iconField;
        private FieldInfo? buffField;
        private FieldInfo? countField;
        private FieldInfo? timedField;
        private FieldInfo? timedIndexField;
        private FieldInfo? timedTimerField;
        private bool failed;
        private bool toldClient;

        internal BuffDetails(BuffInfoSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            sourceField = typeof(BuffDisplay).GetField("source", Private);
            iconsField = typeof(BuffDisplay).GetField("buffIconDisplayData", Private);
            Type? data = typeof(BuffDisplay).GetNestedType("BuffIconDisplayData", Any);
            iconField = data?.GetField("buffIconComponent", Any);
            buffField = data?.GetField("buffDef", Any);
            countField = data?.GetField("buffCount", Any);
            timedField = typeof(CharacterBody).GetField("timedBuffs", Any);
            Type? timed = typeof(CharacterBody).GetNestedType("TimedBuff", Any);
            timedIndexField = timed?.GetField("buffIndex", Any);
            timedTimerField = timed?.GetField("timer", Any);
            if (sourceField == null || iconsField == null || iconField == null || buffField == null || countField == null)
            {
                log.LogWarning("The buff bar's internals have changed, buffs stay as the game shows them");
                return;
            }

            if (timedField == null || timedIndexField == null || timedTimerField == null)
            {
                log.LogWarning("Buff timers' internals have changed, buffs show no timers");
            }

            On.RoR2.UI.BuffDisplay.UpdateLayout += OnUpdateLayout;
        }

        public void Disable()
        {
            On.RoR2.UI.BuffDisplay.UpdateLayout -= OnUpdateLayout;
        }

        private void OnUpdateLayout(On.RoR2.UI.BuffDisplay.orig_UpdateLayout orig, BuffDisplay self)
        {
            orig(self);
            if (failed)
            {
                return;
            }

            try
            {
                Update(self);
            }
            catch (Exception e)
            {
                // Once, as it runs every few frames for every buff bar
                failed = true;
                log.LogError($"Couldn't update the buff icons, buffs stay as the game shows them from here: {e}");
            }
        }

        private void Update(BuffDisplay display)
        {
            if (iconsField!.GetValue(display) is not IList icons || icons.Count == 0)
            {
                return;
            }

            Dictionary<int, float>? timers = settings.Timers.Value ? TimersOf(sourceField!.GetValue(display) as CharacterBody) : null;
            foreach (object entry in icons)
            {
                if (iconField!.GetValue(entry) is not BuffIcon icon || !icon || buffField!.GetValue(entry) is not BuffDef buff || !buff)
                {
                    continue;
                }

                float timer = 0f;
                bool timed = timers != null && timers.TryGetValue((int)buff.buffIndex, out timer);
                ShowTimer(icon, buff, (int)countField!.GetValue(entry), timed, timer);
                ShowTooltip(icon, buff);
            }
        }

        // Timers live only on the host, a client's copy of the list stays empty
        private Dictionary<int, float>? TimersOf(CharacterBody? body)
        {
            if (body == null || !body || timedField == null)
            {
                return null;
            }

            if (!NetworkServer.active)
            {
                if (!toldClient)
                {
                    toldClient = true;
                    log.LogInfo("Playing as a client, so buffs show no timers, the game only keeps them on the host");
                }

                return null;
            }

            if (timedField.GetValue(body) is not IList list || list.Count == 0)
            {
                return null;
            }

            var entries = new List<(int, float)>(list.Count);
            foreach (object timed in list)
            {
                entries.Add(((int)timedIndexField!.GetValue(timed), (float)timedTimerField!.GetValue(timed)));
            }

            return BuffText.Longest(entries);
        }

        // Written into the game's stack count, as the timer sits above it, and handed back once untimed
        private void ShowTimer(BuffIcon icon, BuffDef buff, int count, bool timed, float timer)
        {
            var stackText = icon.stackCount;
            if (!stackText)
            {
                return;
            }

            bool ours = written.TryGetValue(icon, out _);
            string stack = BuffText.Stack(buff.canStack, (int)buff.stackingDisplayMethod == PercentDisplay, count, buff.displayOffset);
            // Written every pass, as the game rewrites the label itself when an icon is pooled or the bar is
            // hidden, and the bar only lays out every few frames
            if (timed)
            {
                string text = BuffText.Timed(timer, settings.OneDecimal.Value, stack);
                if (!ours)
                {
                    written.Add(icon, text);
                }

                stackText.SetText(text);
                stackText.enabled = true;
                return;
            }

            if (ours)
            {
                written.Remove(icon);
                stackText.SetText(stack);
                stackText.enabled = buff.canStack;
            }
        }

        private void ShowTooltip(BuffIcon icon, BuffDef buff)
        {
            bool ours = tooltips.TryGetValue(icon, out TooltipProvider tooltip);
            if (!settings.Descriptions.Value)
            {
                if (ours && tooltip)
                {
                    tooltip.enabled = false;
                }

                return;
            }

            if (!ours || !tooltip)
            {
                // Pooled icons may already carry one, from the game or another mod
                if (icon.TryGetComponent(out TooltipProvider _))
                {
                    return;
                }

                if (!CanHover(icon))
                {
                    return;
                }

                tooltip = icon.gameObject.AddComponent<TooltipProvider>();
                tooltips.Remove(icon);
                tooltips.Add(icon, tooltip);
            }

            tooltip.enabled = true;
            tooltip.titleColor = buff.buffColor == Color.white ? Color.gray : buff.buffColor;
            if (BuffDefinitions.TryGet(buff, out string name, out string description))
            {
                tooltip.overrideTitleText = name;
                tooltip.overrideBodyText = BuffText.Body(name, description);
            }
            else
            {
                tooltip.overrideTitleText = BuffText.Tidy(buff.name);
                tooltip.overrideBodyText = string.Empty;
            }
        }

        // A tooltip needs pointer events on the icon's own canvas, as a nested canvas isn't reached by its
        // parent's raycaster, and the HUD's buff bar sits in LevelDisplayCluster's, which has none
        private bool CanHover(BuffIcon icon)
        {
            // Enemies' overhead bars show buffs too, a canvas each, but no one points at them
            if (icon.GetComponentInParent<CombatHealthBarViewer>())
            {
                return false;
            }

            Canvas canvas = icon.GetComponentInParent<Canvas>();
            if (!canvas)
            {
                return false;
            }

            if (!canvas.GetComponent<GraphicRaycaster>())
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                if (hoverCanvases.Add(canvas.name))
                {
                    log.LogDebug($"Added pointer events to {canvas.name} so its buff icons can show descriptions");
                }
            }

            return true;
        }
    }
}
