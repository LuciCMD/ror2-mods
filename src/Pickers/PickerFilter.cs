using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using JustBeBetter.InventorySort;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Pickers
{
    // Lives on an item picker panel: a search box just above its grid, buttons hidden when they don't match,
    // favourites moved to the front, the rest sorted as set for the Command menu, Scrapper and crafting menus
    // The order is checked each frame and restored, so a setting change shows at once
    // LookingGlass sorts the options before the panel gets them, so its order is the game's order here
    internal sealed class PickerFilter : MonoBehaviour
    {
        private const float BarHeight = 36f;
        private const float BarGap = 6f;
        private const string LookingGlassCloser = "ClosePickupPicker";

        private static readonly char[] Spaces = { ' ', '\t' };
        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Behaviour> pausedClosers = new List<Behaviour>();
        private PickupPickerPanel? panel;
        private Favourites? favourites;
        private ConfigEntry<PickerOrder>? sorting;
        private Func<SortRules?>? inventoryRules;
        private List<Entry>? arranged;
        private PickerOrder arrangedFor;
        private SortRules? arrangedRules;
        private ConfigEntry<bool>? usableFirst;
        private bool arrangedUsable;
        private OpenCounts? openCounts;
        private RectTransform? bar;
        private TMP_InputField? searchField;
        private bool automaticNavigation;
        private string[] words = Array.Empty<string>();

        // No sorting setting keeps the game's order after the favourites, inventory rules make By Tier follow Inventory Sort
        // and usableFirst drops the entries added as unusable to the bottom, keepOpenCounts sorts by the counts it opened with
        internal static PickerFilter For(
            PickupPickerPanel panel,
            Favourites favourites,
            bool automaticNavigation,
            ConfigEntry<PickerOrder>? sorting,
            Func<SortRules?>? inventoryRules = null,
            ConfigEntry<bool>? usableFirst = null,
            bool keepOpenCounts = false)
        {
            if (!panel.TryGetComponent(out PickerFilter filter))
            {
                filter = panel.gameObject.AddComponent<PickerFilter>();
                filter.panel = panel;
                filter.favourites = favourites;
                filter.automaticNavigation = automaticNavigation;
                filter.BuildBar();
            }

            filter.sorting = sorting;
            filter.inventoryRules = inventoryRules;
            filter.usableFirst = usableFirst;
            if (keepOpenCounts && filter.openCounts == null)
            {
                filter.openCounts = new OpenCounts();
            }

            return filter;
        }

        internal bool IsTyping => searchField != null && searchField.isFocused;

        internal void Clear()
        {
            entries.Clear();
            arranged = null;
        }

        // In the game's order, with the count as it is now so buttons don't move under the cursor
        internal void Add(MPButton button, string? key, string text, string name, int tierRank, int count, ItemTier? tier, bool scrap, bool unusable)
        {
            int sortCount = openCounts != null ? openCounts.Keep(key, count) : count;
            entries.Add(new Entry(button, key, text, name, tierRank, sortCount, tier, scrap, unusable));
            arranged = null;
            FavouriteMark.Attach(button.gameObject, () => favourites!.Contains(key), () => Toggle(key), true);
        }

        internal void Apply()
        {
            foreach (Entry entry in entries)
            {
                if (entry.Button)
                {
                    bool show = words.Length == 0 || PickerText.Matches(entry.Text, words);
                    if (entry.Button.gameObject.activeSelf != show)
                    {
                        entry.Button.gameObject.SetActive(show);
                    }
                }
            }

            Order();
        }

        private void Toggle(string? key)
        {
            favourites?.Toggle(key);
            arranged = null;
            foreach (Entry entry in entries)
            {
                if (entry.Button)
                {
                    FavouriteMark.Refresh(entry.Button.gameObject);
                }
            }

            Order();
        }

        private void SetQuery(string text)
        {
            words = (text ?? string.Empty).ToLowerInvariant().Split(Spaces, StringSplitOptions.RemoveEmptyEntries);
            Apply();
        }

        private void LateUpdate()
        {
            PlaceBar();
            if (!InOrder())
            {
                Order();
            }
        }

        private void OnDisable()
        {
            ResumeClosers();
        }

        private bool IsFavourite(Entry entry)
        {
            return favourites != null && favourites.Contains(entry.Key);
        }

        private bool InOrder()
        {
            return Wanted().Select(e => e.Button).SequenceEqual(Sorted().Select(e => e.Button));
        }

        // Unsorted pickers keep whatever order the grid has, as before sorting existed
        // A sorted order is kept until its inputs change, as name comparisons every frame are slow on Mono
        private List<Entry> Wanted()
        {
            if (sorting == null)
            {
                return Arranged(Sorted(), PickerOrder.GamesOrder);
            }

            SortRules? rules = inventoryRules?.Invoke();
            bool usable = usableFirst != null && usableFirst.Value;
            if (arranged == null || arrangedFor != sorting.Value || !Nullable.Equals(arrangedRules, rules) || arrangedUsable != usable)
            {
                arrangedFor = sorting.Value;
                arrangedRules = rules;
                arrangedUsable = usable;
                arranged = Arranged(entries.Where(e => e.Button).ToList(), arrangedFor, rules, usable);
            }

            return arranged.Where(e => e.Button).ToList();
        }

        private List<Entry> Arranged(List<Entry> live, PickerOrder order, SortRules? rules = null, bool usableFirst = false)
        {
            List<SortKey> keys = live
                .Select(e => new SortKey(e.Name, e.TierRank, e.Count, IsFavourite(e), e.Tier, e.Scrap, usableFirst && e.Unusable))
                .ToList();
            return PickerSort.Arrange(keys, order, rules).Select(i => live[i]).ToList();
        }

        private List<Entry> Sorted()
        {
            return entries.Where(e => e.Button).OrderBy(e => e.Button.transform.GetSiblingIndex()).ToList();
        }

        private void Order()
        {
            List<Entry> current = Sorted();
            List<int> slots = current.Select(e => e.Button.transform.GetSiblingIndex()).ToList();
            List<Entry> wanted = Wanted();
            for (int i = 0; i < wanted.Count; i++)
            {
                wanted[i].Button.transform.SetSiblingIndex(slots[i]);
            }

            Navigate(wanted.Where(e => e.Button.gameObject.activeSelf).ToList());
        }

        // As the game: left and right along the grid, up and down a row
        // Only when the panel uses explicit navigation
        private void Navigate(List<Entry> visible)
        {
            if (automaticNavigation || panel == null || visible.Count == 0)
            {
                return;
            }

            int columns = Math.Max(1, panel.maxColumnCount);
            for (int i = 0; i < visible.Count; i++)
            {
                Navigation nav = visible[i].Button.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = i % columns > 0 ? visible[i - 1].Button : null;
                nav.selectOnRight = i % columns < columns - 1 && i + 1 < visible.Count ? visible[i + 1].Button : null;
                nav.selectOnUp = i - columns >= 0 ? visible[i - columns].Button : null;
                nav.selectOnDown = i + columns < visible.Count ? visible[i + columns].Button : null;
                visible[i].Button.navigation = nav;
            }
        }

        private void BuildBar()
        {
            RectTransform? container = panel != null ? panel.buttonContainer : null;
            if (container == null || container.parent == null)
            {
                return;
            }

            var go = new GameObject("Just_Be_Better Picker Search", typeof(RectTransform));
            bar = (RectTransform)go.transform;
            bar.SetParent(container.parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            TMP_FontAsset? font = panel!.GetComponentsInChildren<TextMeshProUGUI>(true).Select(t => t.font).FirstOrDefault(f => f);
            Image? style = panel.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite && i.type == Image.Type.Sliced);
            searchField = SearchField.Create(bar, style, font, "Search Items", SetQuery);
            searchField.onSelect.AddListener(_ => PauseClosers());
            searchField.onDeselect.AddListener(_ => ResumeClosers());
            PlaceBar();
        }

        // Just above the grid, as wide as it, wherever the panel's layout put the grid
        private void PlaceBar()
        {
            RectTransform? container = panel != null ? panel.buttonContainer : null;
            if (bar == null || container == null)
            {
                return;
            }

            var parent = (RectTransform)bar.parent;
            container.GetWorldCorners(Corners);
            Vector3 topLeft = parent.InverseTransformPoint(Corners[1]);
            Vector3 topRight = parent.InverseTransformPoint(Corners[2]);
            // Anchored at the parent's pivot, where its local coordinates start
            bar.anchorMin = bar.anchorMax = parent.pivot;
            bar.pivot = Vector2.zero;
            bar.anchoredPosition = new Vector2(topLeft.x, topLeft.y + BarGap);
            bar.sizeDelta = new Vector2(topRight.x - topLeft.x, BarHeight);
        }

        // LookingGlass closes the picker on any key, but not while typing a search
        private void PauseClosers()
        {
            ResumeClosers();
            foreach (MonoBehaviour behaviour in GetComponentsInParent<MonoBehaviour>(true).Concat(GetComponentsInChildren<MonoBehaviour>(true)))
            {
                if (behaviour && behaviour.enabled && behaviour.GetType().Name == LookingGlassCloser)
                {
                    behaviour.enabled = false;
                    pausedClosers.Add(behaviour);
                }
            }
        }

        private void ResumeClosers()
        {
            foreach (Behaviour behaviour in pausedClosers)
            {
                if (behaviour)
                {
                    behaviour.enabled = true;
                }
            }

            pausedClosers.Clear();
        }

        private readonly struct Entry
        {
            internal Entry(MPButton button, string? key, string text, string name, int tierRank, int count, ItemTier? tier, bool scrap, bool unusable)
            {
                Button = button;
                Key = key;
                Text = text;
                Name = name;
                TierRank = tierRank;
                Count = count;
                Tier = tier;
                Scrap = scrap;
                Unusable = unusable;
            }

            internal MPButton Button { get; }

            internal string? Key { get; }

            internal string Text { get; }

            internal string Name { get; }

            internal int TierRank { get; }

            internal int Count { get; }

            internal ItemTier? Tier { get; }

            internal bool Scrap { get; }

            internal bool Unusable { get; }
        }
    }
}
