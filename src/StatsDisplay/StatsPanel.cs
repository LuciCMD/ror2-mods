using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace JustBeBetter.StatsDisplay
{
    // The text panel under the objectives, a copy of the objective panel so it matches the HUD around it
    // Built and filled after the HUD's own update, for the first local player, as LookingGlass did
    internal sealed class StatsPanel : IFeature
    {
        private const string PanelName = "JustBeBetterStats";
        private const float FillInterval = 0.25f;
        private const float RetryInterval = 0.5f;
        private const float GiveUpAfter = 15f;
        private const float DefaultFontSize = 12f;
        private const int SettleFrames = 2;

        // Other mods add panels in the first moments of a stage, so ours moves to the bottom once after them
        private const float PlaceLastAfter = 1f;

        private readonly StatsDisplaySettings settings;
        private readonly ManualLogSource log;
        private readonly DamageTracker damage;
        private readonly PortalTracker portals;
        private readonly ConditionalWeakTable<HUD, Panel> panels = new ConditionalWeakTable<HUD, Panel>();
        private readonly ConditionalWeakTable<HUD, Attempts> attempts = new ConditionalWeakTable<HUD, Attempts>();
        private readonly List<Panel> built = new List<Panel>();
        private readonly List<string> unknown = new List<string>();
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, Func<StatContext, string>>? stats;
        private bool failed;

        internal StatsPanel(StatsDisplaySettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            damage = new DamageTracker(settings, log);
            portals = new PortalTracker(log);
        }

        public void Enable()
        {
            damage.Enable();
            portals.Enable();
            settings.StatColours.SettingChanged += OnStyleChanged;
            settings.DecimalPlaces.SettingChanged += OnStyleChanged;
            On.RoR2.UI.HUD.Update += OnHudUpdate;
        }

        public void Disable()
        {
            On.RoR2.UI.HUD.Update -= OnHudUpdate;
            settings.StatColours.SettingChanged -= OnStyleChanged;
            settings.DecimalPlaces.SettingChanged -= OnStyleChanged;
            portals.Disable();
            damage.Disable();
            foreach (Panel panel in built)
            {
                if (panel.Root)
                {
                    Object.Destroy(panel.Root);
                }
            }

            built.Clear();
        }

        private void OnStyleChanged(object sender, EventArgs e)
        {
            stats = null;
        }

        private void OnHudUpdate(On.RoR2.UI.HUD.orig_Update orig, HUD self)
        {
            orig(self);
            if (failed)
            {
                return;
            }

            try
            {
                damage.Tick();
                Update(self);
            }
            catch (Exception e)
            {
                // Once, as it runs every frame
                failed = true;
                log.LogError($"Couldn't update the stats panel, it stops updating from here: {e}");
            }
        }

        private void Update(HUD hud)
        {
            LocalUser? user = hud.localUserViewer;
            if (user == null || user != LocalUserManager.GetFirstLocalUser())
            {
                return;
            }

            Panel? panel = PanelOf(hud);
            if (panel == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!panel.PlacedLast && now - panel.Built >= PlaceLastAfter)
            {
                panel.PlacedLast = true;
                panel.Root.transform.SetAsLastSibling();
            }

            bool tab = hud.scoreboardPanel && hud.scoreboardPanel.activeSelf;
            if (tab == panel.Tab && now < panel.NextFill)
            {
                FitHeight(panel);
                return;
            }

            // A dead player's panel keeps its last text until they're back
            CharacterBody body = user.cachedBody;
            if (!body)
            {
                return;
            }

            panel.Tab = tab;
            panel.NextFill = now + FillInterval;
            string template = tab && !string.IsNullOrEmpty(settings.TabText.Value) ? settings.TabText.Value : settings.MainText.Value;
            Show(panel, Fill(template, body));
        }

        private string Fill(string template, CharacterBody body)
        {
            stats ??= StatDefinitions.Build(settings.StatColours.Value, settings.DecimalPlaces.Value);
            var context = new StatContext(body, damage.Meter, portals, DamageTracker.Now, damage.Window);
            unknown.Clear();
            string text = StatTemplate.Fill(template, name => Value(name, context), unknown);
            foreach (string name in unknown)
            {
                if (reported.Add(name))
                {
                    log.LogWarning($"There's no stat called [{name}], so it shows as typed");
                }
            }

            return text;
        }

        // One stat failing shows N/A rather than stopping the panel
        private string? Value(string name, StatContext context)
        {
            if (!stats!.TryGetValue(name, out Func<StatContext, string> value))
            {
                return null;
            }

            try
            {
                return value(context);
            }
            catch (Exception e)
            {
                if (reported.Add(name))
                {
                    log.LogWarning($"Couldn't read [{name}], so it shows {StatMath.NotAvailable}: {e}");
                }

                return StatMath.NotAvailable;
            }
        }

        private void Show(Panel panel, string text)
        {
            bool visible = text.Trim().Length > 0;
            if (panel.Root.activeSelf != visible)
            {
                panel.Root.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            // The objective panel hides its label when there's nothing to show, and the copy can follow it
            if (!panel.Text.gameObject.activeSelf)
            {
                panel.Text.gameObject.SetActive(true);
            }

            float size = settings.FontSize.Value > 0 ? settings.FontSize.Value : panel.BaseFontSize;
            if (panel.Text.fontSize != size)
            {
                panel.Text.fontSize = size;
                panel.Unsettled = SettleFrames;
            }

            if (!string.Equals(panel.Written, text, StringComparison.Ordinal))
            {
                panel.Written = text;
                panel.Text.text = text;
                panel.Unsettled = SettleFrames;
            }

            FitHeight(panel);
        }

        // For a few frames after a change, as the height is only right once laid out, and measuring lays it out again
        private static void FitHeight(Panel panel)
        {
            if (panel.Unsettled <= 0 || !panel.Root.activeSelf)
            {
                return;
            }

            panel.Unsettled--;

            float height = panel.Text.preferredHeight;
            if (Mathf.Abs(panel.Layout.preferredHeight - height) > 0.5f)
            {
                panel.Layout.preferredHeight = height;
            }
        }

        private Panel? PanelOf(HUD hud)
        {
            if (panels.TryGetValue(hud, out Panel panel))
            {
                if (panel.Root)
                {
                    return panel;
                }

                panels.Remove(hud);
                built.Remove(panel);
            }

            Attempts tries = attempts.GetOrCreateValue(hud);
            float now = Time.unscaledTime;
            if (tries.GaveUp || now < tries.Next)
            {
                return null;
            }

            tries.First = tries.First > 0f ? tries.First : now;
            tries.Next = now + RetryInterval;
            Panel? fresh = Build(hud);
            if (fresh == null)
            {
                if (now - tries.First >= GiveUpAfter)
                {
                    tries.GaveUp = true;
                    log.LogWarning("Couldn't find the HUD's objective panel, so there's no stats panel on this HUD");
                }

                return null;
            }

            panels.Add(hud, fresh);
            built.RemoveAll(p => !p.Root);
            built.Add(fresh);
            log.Verbose("Stats panel added under the objectives");
            return fresh;
        }

        private Panel? Build(HUD hud)
        {
            Transform? objectives = FindObjectivePanel(hud.transform);
            if (objectives == null)
            {
                return null;
            }

            // Copied with its header showing, as the header is the text we write into
            Transform header = objectives.Find("Label");
            bool headerShown = header && header.gameObject.activeSelf;
            if (header)
            {
                header.gameObject.SetActive(true);
            }

            GameObject copy;
            try
            {
                copy = Object.Instantiate(objectives.gameObject, objectives.parent);
            }
            finally
            {
                if (header)
                {
                    header.gameObject.SetActive(headerShown);
                }
            }

            copy.name = PanelName;
            DestroyChild(copy.transform, "StripContainer");
            DestroyChild(copy.transform, "Minimap");
            DestroyAll<HudObjectiveTargetSetter>(copy);
            DestroyAll<ObjectivePanelController>(copy);
            DestroyAll<LanguageTextMeshController>(copy);

            TextMeshProUGUI text = copy.GetComponentInChildren<TextMeshProUGUI>(true);
            LayoutElement layout = copy.GetComponentInChildren<LayoutElement>(true);
            VerticalLayoutGroup group = copy.GetComponent<VerticalLayoutGroup>();
            if (!text || !layout || !group)
            {
                Object.Destroy(copy);
                log.LogWarning("The objective panel has changed, so there's no stats panel");
                return null;
            }

            Transform transform = copy.transform;
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;
            transform.localScale = Vector3.one;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = Color.white;
            text.text = string.Empty;

            // The objectives' own lines, smaller than their header
            float baseSize = DefaultFontSize;
            Transform strip = objectives.Find("StripContainer/ObjectiveStrip/Label");
            if (strip && strip.TryGetComponent(out TextMeshProUGUI stripText))
            {
                baseSize = stripText.fontSize;
            }

            // Toggled so the layout picks up the copy at once
            group.enabled = false;
            group.enabled = true;
            transform.SetAsLastSibling();
            return new Panel(copy, text, layout, baseSize, Time.unscaledTime);
        }

        private static Transform? FindObjectivePanel(Transform hud)
        {
            foreach (VerticalLayoutGroup group in hud.GetComponentsInChildren<VerticalLayoutGroup>())
            {
                if (group.gameObject.name == "RightInfoBar")
                {
                    Transform objectives = group.transform.Find("ObjectivePanel");
                    if (objectives)
                    {
                        return objectives;
                    }
                }
            }

            return null;
        }

        // At once, so the text lookups below don't find what's being removed
        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void DestroyAll<T>(GameObject root)
            where T : Component
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
            {
                Object.DestroyImmediate(component);
            }
        }

        private sealed class Attempts
        {
            internal float First { get; set; }

            internal float Next { get; set; }

            internal bool GaveUp { get; set; }
        }

        private sealed class Panel
        {
            internal Panel(GameObject root, TextMeshProUGUI text, LayoutElement layout, float baseFontSize, float built)
            {
                Root = root;
                Text = text;
                Layout = layout;
                BaseFontSize = baseFontSize;
                Built = built;
                NextFill = built;
            }

            internal GameObject Root { get; }

            internal TextMeshProUGUI Text { get; }

            internal LayoutElement Layout { get; }

            internal float BaseFontSize { get; }

            internal float Built { get; }

            internal float NextFill { get; set; }

            internal bool Tab { get; set; }

            internal bool PlacedLast { get; set; }

            internal string Written { get; set; } = string.Empty;

            internal int Unsettled { get; set; }
        }
    }
}
