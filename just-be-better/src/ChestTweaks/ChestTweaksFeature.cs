using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EntityStates;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;

namespace JustBeBetter.ChestTweaks
{
    // Outlines interactables in one colour and fades or hides used chests, barrels and shops, as QoLChests did
    // Every signal used here also reaches clients, so it works the same when someone else hosts
    internal sealed class ChestTweaksFeature : IFeature
    {
        // QoLChests' reach from a drone shop terminal to its stand
        private const float DroneVendorReach = 4.3f;

        private readonly ChestTweaksSettings settings;
        private readonly ManualLogSource log;
        private readonly Dictionary<Highlight, Tracked> tracked = new Dictionary<Highlight, Tracked>();
        private readonly Dictionary<GameObject, Stand> shopStands = new Dictionary<GameObject, Stand>();
        private readonly HashSet<string> unknown = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reportedUnknown = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Hook> hooks = new List<Hook>();
        private BodyIndex drifter = BodyIndex.None;
        private bool drifterMissing;
        private bool oldModChecked;

        internal ChestTweaksFeature(ChestTweaksSettings settings, ManualLogSource log)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Enable()
        {
            On.RoR2.Highlight.OnEnable += OnHighlightEnable;
            On.EntityStates.Barrel.Opened.OnEnter += OnOpened;
            On.EntityStates.Barrel.Closing.OnEnter += OnClosing;
            On.RoR2.PurchaseInteraction.OnSyncAvailable += OnSyncAvailable;
            On.RoR2.SceneDirector.Start += OnSceneDirectorStart;
            HookAdaptiveChest();
            Listen(true);

            foreach (Highlight highlight in Highlight.readonlyHighlightList.ToList())
            {
                Track(highlight);
            }
        }

        public void Disable()
        {
            On.RoR2.Highlight.OnEnable -= OnHighlightEnable;
            On.EntityStates.Barrel.Opened.OnEnter -= OnOpened;
            On.EntityStates.Barrel.Closing.OnEnter -= OnClosing;
            On.RoR2.PurchaseInteraction.OnSyncAvailable -= OnSyncAvailable;
            On.RoR2.SceneDirector.Start -= OnSceneDirectorStart;
            foreach (Hook hook in hooks)
            {
                hook.Dispose();
            }

            hooks.Clear();
            Listen(false);

            foreach (KeyValuePair<Highlight, Tracked> pair in tracked)
            {
                RemoveHider(pair.Value);
                if (pair.Key)
                {
                    SetOutline(pair.Key, pair.Value, false);
                }
            }

            foreach (Stand stand in shopStands.Values)
            {
                Remove(stand.Hider);
            }

            tracked.Clear();
            shopStands.Clear();
        }

        // Hide Delay only counts for the next one used, so it isn't here
        private void Listen(bool on)
        {
            foreach (ConfigEntry<bool> entry in settings.OutlineEntries.Concat(settings.HideEntries)
                .Concat(new[] { settings.Fade, settings.KeepAsDrifter }))
            {
                if (on)
                {
                    entry.SettingChanged += OnSettingChanged;
                }
                else
                {
                    entry.SettingChanged -= OnSettingChanged;
                }
            }

            if (on)
            {
                settings.OutlineColour.SettingChanged += OnSettingChanged;
            }
            else
            {
                settings.OutlineColour.SettingChanged -= OnSettingChanged;
            }
        }

        // The adaptive chest's opened state is a private class, so it's hooked by name
        private void HookAdaptiveChest()
        {
            MethodInfo? onEnter = typeof(RouletteChestController).GetNestedType("Opened", BindingFlags.NonPublic)
                ?.GetMethod(nameof(EntityState.OnEnter), BindingFlags.Instance | BindingFlags.Public);
            if (onEnter == null)
            {
                log.LogWarning("Couldn't find the adaptive chest's opened state, so used adaptive chests stay in view");
                return;
            }

            try
            {
                hooks.Add(new Hook(onEnter, new Action<Action<EntityState>, EntityState>(OnAdaptiveOpened)));
            }
            catch (Exception e)
            {
                log.LogWarning($"Couldn't hook the adaptive chest, so used ones stay in view: {e}");
            }
        }

        private void OnHighlightEnable(On.RoR2.Highlight.orig_OnEnable orig, Highlight self)
        {
            orig(self);
            Safely("tracking an interactable", () => Track(self));
        }

        private void OnOpened(On.EntityStates.Barrel.Opened.orig_OnEnter orig, EntityStates.Barrel.Opened self)
        {
            orig(self);
            Safely("hiding a used chest", () => MarkUsed(self.outer ? self.outer.gameObject : null));
        }

        private void OnAdaptiveOpened(Action<EntityState> orig, EntityState self)
        {
            orig(self);
            Safely("hiding a used adaptive chest", () => MarkUsed(self.outer ? self.outer.gameObject : null));
        }

        // Only a Delusion reset closes a chest again
        private void OnClosing(On.EntityStates.Barrel.Closing.orig_OnEnter orig, EntityStates.Barrel.Closing self)
        {
            orig(self);
            Safely("showing a reset chest", () => MarkAvailable(self.outer ? self.outer.gameObject : null));
        }

        // The game's sync hook, so it runs for the host and for every client
        // Bazaar shops and rerolled terminals come back when available again
        private void OnSyncAvailable(On.RoR2.PurchaseInteraction.orig_OnSyncAvailable orig, PurchaseInteraction self, bool newAvailable)
        {
            orig(self, newAvailable);
            Safely("hiding a used shop", () =>
            {
                Highlight highlight = self ? self.GetComponent<Highlight>() : null!;
                if (!highlight || !tracked.TryGetValue(highlight, out Tracked t) || t.Kind != ChestKind.Shop)
                {
                    return;
                }

                if (newAvailable)
                {
                    MarkAvailable(self.gameObject);
                }
                else
                {
                    MarkUsed(self.gameObject);
                }
            });
        }

        private void OnSceneDirectorStart(On.RoR2.SceneDirector.orig_Start orig, SceneDirector self)
        {
            orig(self);
            Safely("counting outlines", ReportStage);
        }

        private void OnSettingChanged(object sender, EventArgs e) => Safely("applying a setting", RefreshAll);

        private void Track(Highlight highlight)
        {
            if (!highlight)
            {
                return;
            }

            if (!tracked.TryGetValue(highlight, out Tracked t))
            {
                ChestKind? kind = ChestKinds.Of(highlight.gameObject.name, Ancestors(highlight.transform));
                if (kind == null)
                {
                    unknown.Add(ChestKinds.BaseName(highlight.gameObject.name));
                    return;
                }

                t = new Tracked(kind.Value, highlight);
                tracked[highlight] = t;
            }

            Refresh(highlight, t);
        }

        private static IEnumerable<string> Ancestors(Transform transform)
        {
            for (Transform parent = transform.parent; parent; parent = parent.parent)
            {
                yield return parent.name;
            }
        }

        private void MarkUsed(GameObject? target)
        {
            if (!target || !Find(target!, out Highlight highlight, out Tracked t) || t.Used)
            {
                return;
            }

            t.Used = true;
            Refresh(highlight, t);
            if (t.Kind == ChestKind.Shop)
            {
                HideShopStand(target!);
            }
        }

        private void MarkAvailable(GameObject? target)
        {
            if (!target || !Find(target!, out Highlight highlight, out Tracked t) || !t.Used)
            {
                return;
            }

            t.Used = false;
            Refresh(highlight, t);
            if (t.Kind == ChestKind.Shop)
            {
                ShowShopStands(target!);
            }
        }

        private bool Find(GameObject target, out Highlight highlight, out Tracked t)
        {
            highlight = target.GetComponent<Highlight>();
            if (highlight && tracked.TryGetValue(highlight, out t))
            {
                return true;
            }

            t = null!;
            return false;
        }

        private bool HideUsed(ChestKind kind) => ChestRules.ShouldHide(
            ChestKinds.CanHide(kind),
            settings.HideOn(kind),
            settings.KeepAsDrifter.Value,
            IsDrifter());

        private void Refresh(Highlight highlight, Tracked t)
        {
            if (!highlight)
            {
                return;
            }

            if (t.Used && HideUsed(t.Kind))
            {
                if (t.Hider == null)
                {
                    t.Hider = highlight.gameObject.AddComponent<UsedHider>();
                    t.Hider.Begin(settings.HideDelay.Value, settings.Fade.Value, () => Refresh(highlight, t));
                }
                else
                {
                    t.Hider.Switch(settings.Fade.Value);
                }
            }
            else
            {
                RemoveHider(t);
            }

            bool gone = t.Hider != null && t.Hider.Gone;
            SetOutline(highlight, t, ChestRules.Outlined(settings.OutlineOn(t.Kind), gone));
        }

        private void RefreshAll()
        {
            foreach (KeyValuePair<Highlight, Tracked> pair in tracked.ToList())
            {
                Refresh(pair.Key, pair.Value);
            }

            bool hideShops = HideUsed(ChestKind.Shop);
            foreach (KeyValuePair<GameObject, Stand> stand in shopStands.ToList())
            {
                if (!hideShops || !stand.Key)
                {
                    Remove(stand.Value.Hider);
                    shopStands.Remove(stand.Key);
                }
                else
                {
                    stand.Value.Hider.Switch(settings.Fade.Value);
                }
            }

            // Shops bought out while hiding was off or kept for Drifter
            foreach (KeyValuePair<Highlight, Tracked> pair in tracked.ToList())
            {
                if (pair.Key && pair.Value.Used && pair.Value.Kind == ChestKind.Shop)
                {
                    HideShopStand(pair.Key.gameObject);
                }
            }
        }

        private void SetOutline(Highlight highlight, Tracked t, bool on)
        {
            if (on)
            {
                if (!highlight.highlightTargetValid)
                {
                    List<Renderer> models = highlight.GetComponentsInChildren<Renderer>()
                        .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                        .ToList();
                    if (models.Count == 0)
                    {
                        return;
                    }

                    highlight.SetTargetRendererList(models);
                }

                highlight.isOn = true;
                highlight.highlightColor = Highlight.HighlightColor.custom;
                highlight.CustomColor = settings.OutlineColour.Value;
                t.Outlined = true;
            }
            else if (t.Outlined)
            {
                highlight.isOn = t.WasOn;
                highlight.highlightColor = t.WasColour;
                highlight.CustomColor = t.WasCustom;
                t.Outlined = false;
            }
        }

        private static void RemoveHider(Tracked t)
        {
            if (t.Hider != null)
            {
                Remove(t.Hider);
                t.Hider = null;
            }
        }

        // Put back now, as Destroy only lands at the end of the frame
        private static void Remove(UsedHider hider)
        {
            if (!hider)
            {
                return;
            }

            hider.Restore();
            hider.enabled = false;
            UnityEngine.Object.Destroy(hider);
        }

        // A multishop's stand goes once every terminal is used, and a drone shop's once one is bought
        private void HideShopStand(GameObject terminal)
        {
            if (!HideUsed(ChestKind.Shop))
            {
                return;
            }

            GameObject? stand = null;
            List<GameObject> terminals = new List<GameObject> { terminal };
            foreach (MultiShopController shop in UnityEngine.Object.FindObjectsOfType<MultiShopController>())
            {
                List<GameObject> all = shop.terminalGameObjectsList.ToList();
                if (all.Contains(terminal))
                {
                    if (all.All(Unavailable))
                    {
                        stand = shop.gameObject;
                        terminals = all;
                    }

                    break;
                }
            }

            if (stand == null && ChestKinds.BaseName(terminal.name) == "TripleDroneShopTerminal")
            {
                stand = UnityEngine.Object.FindObjectsOfType<DroneVendorMultiShopController>()
                    .Where(v => v.GetComponent<ModelLocator>())
                    .Select(v => (Vendor: v, Distance: Vector3.Distance(v.transform.position, terminal.transform.position)))
                    .Where(v => v.Distance < DroneVendorReach)
                    .OrderBy(v => v.Distance)
                    .Select(v => v.Vendor.gameObject)
                    .FirstOrDefault();
            }

            if (stand == null)
            {
                return;
            }

            if (!shopStands.TryGetValue(stand, out Stand known))
            {
                known = new Stand(stand.AddComponent<UsedHider>());
                known.Hider.Begin(settings.HideDelay.Value, settings.Fade.Value, null);
                shopStands[stand] = known;
            }

            foreach (GameObject t in terminals.Where(t => t && !known.Terminals.Contains(t)))
            {
                known.Terminals.Add(t);
                known.Hider.Skip(t);
            }
        }

        private static bool Unavailable(GameObject terminal)
        {
            PurchaseInteraction purchase = terminal ? terminal.GetComponent<PurchaseInteraction>() : null!;
            return !purchase || !purchase.available;
        }

        // A terminal back in stock, such as with the Executive Card, brings its stand back too
        private void ShowShopStands(GameObject terminal)
        {
            foreach (KeyValuePair<GameObject, Stand> stand in shopStands.ToList())
            {
                if (!stand.Key || stand.Value.Terminals.Contains(terminal))
                {
                    Remove(stand.Value.Hider);
                    shopStands.Remove(stand.Key);
                }
            }
        }

        private bool IsDrifter()
        {
            if (drifter == BodyIndex.None && !drifterMissing)
            {
                drifter = BodyCatalog.FindBodyIndex("DrifterBody");

                // Before the catalog loads every lookup misses, so that's no answer yet
                if (drifter == BodyIndex.None && BodyCatalog.bodyCount > 0)
                {
                    drifterMissing = true;
                    log.LogWarning("Couldn't find Drifter, so Keep Used Ones as Drifter does nothing");
                }
            }

            CharacterBody? body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            return drifter != BodyIndex.None && body && body!.bodyIndex == drifter;
        }

        private void ReportStage()
        {
            foreach (Highlight gone in tracked.Keys.Where(h => !h).ToList())
            {
                tracked.Remove(gone);
            }

            foreach (GameObject gone in shopStands.Keys.Where(s => !s).ToList())
            {
                shopStands.Remove(gone);
            }

            if (!oldModChecked)
            {
                oldModChecked = true;
                if (Chainloader.PluginInfos.ContainsKey("Faust.QoLChests"))
                {
                    log.LogWarning("QoLChests is installed too, uninstall it or turn this section off");
                }
            }

            string counts = string.Join(", ", tracked.Values
                .GroupBy(t => t.Kind)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key} {g.Count(t => t.Outlined)}/{g.Count()}"));
            log.LogInfo($"Outlined on this stage: {(counts.Length == 0 ? "nothing found" : counts)}");

            List<string> fresh = unknown.Where(reportedUnknown.Add).OrderBy(n => n, StringComparer.Ordinal).ToList();
            unknown.Clear();
            if (fresh.Count > 0)
            {
                log.LogDebug($"Not a kind this outlines: {string.Join(", ", fresh)}");
            }
        }

        // One error per kind of failure, so a broken hook doesn't flood the log
        private void Safely(string what, Action act)
        {
            try
            {
                act();
            }
            catch (Exception e)
            {
                if (failed.Add(what))
                {
                    log.LogError($"Failed {what}: {e}");
                }
            }
        }

        // Its terminals sit under it but fade and come back on their own
        private sealed class Stand
        {
            internal Stand(UsedHider hider) => Hider = hider;

            internal UsedHider Hider { get; }

            internal List<GameObject> Terminals { get; } = new List<GameObject>();
        }

        private sealed class Tracked
        {
            internal Tracked(ChestKind kind, Highlight highlight)
            {
                Kind = kind;
                WasOn = highlight.isOn;
                WasColour = highlight.highlightColor;
                WasCustom = highlight.CustomColor;
            }

            internal ChestKind Kind { get; }

            internal bool WasOn { get; }

            internal Highlight.HighlightColor WasColour { get; }

            internal Color WasCustom { get; }

            internal bool Used { get; set; }

            internal bool Outlined { get; set; }

            internal UsedHider? Hider { get; set; }
        }
    }
}
