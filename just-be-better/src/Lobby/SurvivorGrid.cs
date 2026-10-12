using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Lobby
{
    // Builds the lobby's survivor grid in place of the game's CharacterSelectBarController.Build: the same icons,
    // fillers and navigation, but with favourites first, filtered by the search, more and smaller icons when the
    // roster outgrows the grid, and pages once even the smallest icons don't fit
    internal sealed class SurvivorGrid
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // The game's grid is 8 columns of about 70 px in 3 rows
        // Icons shrink to MinCell before pages start
        private const int BaseColumns = 8;
        private const int MaxColumns = 16;
        private const float MinCell = 44f;
        private const float FallbackWidth = 589f;
        private const float FallbackHeight = 222f;

        private readonly ManualLogSource log;
        private readonly Favourites favourites;
        private readonly ConfigEntry<int> repeat;
        private FieldInfo? iconsField;
        private FieldInfo? fillersField;
        private PropertyInfo? pickedProperty;
        private MethodInfo? shouldDisplay;
        private MethodInfo? buildMethod;
        private MethodInfo? preference;

        private CharacterSelectBarController? bar;
        private SurvivorSearchBar? searchBar;
        private string query = string.Empty;
        private int page;
        private bool followPick = true;
        private SurvivorDef? lastPicked;
        private bool pickHidden;

        internal SurvivorGrid(ManualLogSource log, Favourites favourites, ConfigEntry<int> repeat)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.favourites = favourites ?? throw new ArgumentNullException(nameof(favourites));
            this.repeat = repeat ?? throw new ArgumentNullException(nameof(repeat));
        }

        internal void Enable()
        {
            Type type = typeof(CharacterSelectBarController);
            iconsField = type.GetField("survivorIconControllers", Any);
            fillersField = type.GetField("fillerIcons", Any);
            pickedProperty = type.GetProperty("pickedIcon", Any);
            shouldDisplay = type.GetMethod("ShouldDisplaySurvivor", Any);
            buildMethod = type.GetMethod("Build", Any);
            preference = type.GetMethod("GetLocalUserExistingSurvivorPreference", Any);
            if (iconsField == null || fillersField == null || pickedProperty == null || shouldDisplay == null || buildMethod == null)
            {
                log.LogWarning("The survivor grid's internals have changed; the grid stays as the game has it");
                return;
            }

            On.RoR2.CharacterSelectBarController.Awake += OnAwake;
            On.RoR2.CharacterSelectBarController.Build += OnBuild;
            On.RoR2.CharacterSelectBarController.EnforceValidChoice += OnEnforceValidChoice;
        }

        internal void Disable()
        {
            On.RoR2.CharacterSelectBarController.Awake -= OnAwake;
            On.RoR2.CharacterSelectBarController.Build -= OnBuild;
            On.RoR2.CharacterSelectBarController.EnforceValidChoice -= OnEnforceValidChoice;
        }

        internal bool IsFavourite(SurvivorDef? survivor)
        {
            return favourites.Contains(survivor?.cachedName);
        }

        internal void SetQuery(string text)
        {
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed == query)
            {
                return;
            }

            query = trimmed;
            page = 0;
            Rebuild();
        }

        internal void Turn(int delta)
        {
            page += delta;
            Rebuild();
        }

        internal void ToggleFavourite(SurvivorDef? survivor)
        {
            favourites.Toggle(survivor?.cachedName);
            Rebuild();
        }

        private void Rebuild()
        {
            if (bar && buildMethod != null)
            {
                buildMethod.Invoke(bar, null);
            }
        }

        private void OnAwake(On.RoR2.CharacterSelectBarController.orig_Awake orig, CharacterSelectBarController self)
        {
            orig(self);
            bar = self;
            query = string.Empty;
            page = 0;
            followPick = true;
            lastPicked = null;
            pickHidden = false;
            try
            {
                searchBar = SurvivorSearchBar.Attach(self, this);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't add the survivor search: {e}");
            }
        }

        private void OnBuild(On.RoR2.CharacterSelectBarController.orig_Build orig, CharacterSelectBarController self)
        {
            try
            {
                Build(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't build the survivor grid; using the game's: {e}");
                pickHidden = false;
                orig(self);
            }
        }

        // With the picked survivor filtered out or on another page, the game would pick the first icon instead
        private void OnEnforceValidChoice(On.RoR2.CharacterSelectBarController.orig_EnforceValidChoice orig, CharacterSelectBarController self)
        {
            if (!(self == bar && pickHidden))
            {
                orig(self);
            }
        }

        private void Build(CharacterSelectBarController self)
        {
            var picked = (SurvivorIconController?)pickedProperty!.GetValue(self);
            if (picked && picked!.survivorDef)
            {
                lastPicked = picked.survivorDef;
            }
            else if (lastPicked == null && preference != null)
            {
                lastPicked = preference.Invoke(self, null) as SurvivorDef;
            }

            List<SurvivorDef> all = Roster(self);
            GridLayoutGroup grid = self.iconContainerGrid;
            Fit fit = FitGrid(grid, all.Count);
            int pages = Math.Max(1, (all.Count + fit.PageSize - 1) / fit.PageSize);
            if (followPick && lastPicked != null && all.IndexOf(lastPicked) >= 0)
            {
                page = all.IndexOf(lastPicked) / fit.PageSize;
            }

            followPick = false;
            page = Mathf.Clamp(page, 0, pages - 1);
            List<SurvivorDef> shown = all.Skip(page * fit.PageSize).Take(fit.PageSize).ToList();
            grid.constraintCount = fit.Columns;

            // The game's AdjustGridLayoutCellSize only fits the width, so rows would stay 70 px tall
            grid.cellSize = new Vector2(fit.Cell, fit.Cell);

            ReadOnlyCollection<SurvivorIconController> icons = Allocate(self, shown.Count, fit.Columns);
            for (int i = 0; i < shown.Count; i++)
            {
                SetUp(icons, i, shown[i], fit.Columns);
            }

            SurvivorIconController? match = icons.FirstOrDefault(icon => icon.survivorDef == lastPicked);
            pickedProperty.SetValue(self, match);
            pickHidden = lastPicked != null && !match;
            searchBar?.Show(page, pages);
        }

        // Displayable survivors matching the search, favourites first, each order kept from the catalog
        private List<SurvivorDef> Roster(CharacterSelectBarController self)
        {
            var visible = new List<SurvivorDef>();
            foreach (SurvivorDef survivor in SurvivorCatalog.orderedSurvivorDefs)
            {
                if ((bool)shouldDisplay!.Invoke(self, new object[] { survivor }) && Matches(survivor))
                {
                    visible.Add(survivor);
                }
            }

            List<SurvivorDef> ordered = visible.Where(IsFavourite).Concat(visible.Where(s => !IsFavourite(s))).ToList();

            // Debug: the roster repeated, to see paging and small icons without a big modded roster
            int copies = Mathf.Clamp(repeat.Value, 0, 10);
            var result = new List<SurvivorDef>(ordered);
            for (int i = 0; i < copies; i++)
            {
                result.AddRange(ordered);
            }

            return result;
        }

        private bool Matches(SurvivorDef survivor)
        {
            if (query.Length == 0)
            {
                return true;
            }

            string name = Language.GetString(survivor.displayNameToken) ?? string.Empty;
            return name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (survivor.cachedName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        // The most columns that keep the icons at least MinCell and all rows in the game's grid height
        // If none do, the smallest icons with as many full pages as needed
        private static Fit FitGrid(GridLayoutGroup grid, int count)
        {
            var rect = (RectTransform)grid.transform;
            float width = rect.rect.width > 1f ? rect.rect.width - grid.padding.horizontal : FallbackWidth;
            float height = grid.TryGetComponent(out LayoutElement element) && element.minHeight > 1f
                ? element.minHeight - grid.padding.vertical
                : FallbackHeight;
            float gap = grid.spacing.x;

            int smallest = BaseColumns;
            for (int columns = BaseColumns; columns <= MaxColumns; columns++)
            {
                float cell = (width - (gap * (columns - 1))) / columns;
                if (cell < MinCell)
                {
                    break;
                }

                smallest = columns;
                int rows = Math.Max(1, (count + columns - 1) / columns);
                if ((rows * cell) + ((rows - 1) * gap) <= height + 0.5f)
                {
                    return new Fit(columns, cell, Math.Max(1, count));
                }
            }

            float smallCell = (width - (gap * (smallest - 1))) / smallest;
            int pageRows = Math.Max(1, (int)((height + gap) / (smallCell + gap)));
            return new Fit(smallest, smallCell, smallest * pageRows);
        }

        // The game's allocation: icons, then fillers to complete the last row
        private ReadOnlyCollection<SurvivorIconController> Allocate(CharacterSelectBarController self, int count, int columns)
        {
            var icons = (UIElementAllocator<SurvivorIconController>)iconsField!.GetValue(self);
            object fillers = fillersField!.GetValue(self);
            int cells = (count + columns - 1) / columns * columns;
            icons.AllocateElements(count);
            Type fillerType = fillers.GetType();
            fillerType.GetMethod("AllocateElements")!.Invoke(fillers, new object[] { Math.Max(cells - count, 0) });
            fillerType.GetMethod("MoveElementsToContainerEnd")!.Invoke(fillers, null);
            return icons.elements;
        }

        // As the game: the first icon is the fallback, left and right wrap around, up and down move a row
        private void SetUp(ReadOnlyCollection<SurvivorIconController> icons, int i, SurvivorDef survivor, int columns)
        {
            SurvivorIconController icon = icons[i];
            icon.survivorDef = survivor;
            icon.hgButton.defaultFallbackButton = i == 0;
            FavouriteMark.Attach(icon.gameObject, () => IsFavourite(icon.survivorDef), () => ToggleFavourite(icon.survivorDef), false);
            int count = icons.Count;
            if (count <= 1)
            {
                return;
            }

            Navigation nav = icon.hgButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnLeft = icons[i == 0 ? count - 1 : i - 1].hgButton;
            nav.selectOnRight = icons[i == count - 1 ? 0 : i + 1].hgButton;
            nav.selectOnUp = i - columns >= 0 ? icons[i - columns].hgButton : null;
            nav.selectOnDown = i + columns < count ? icons[i + columns].hgButton : null;
            icon.hgButton.navigation = nav;
        }

        private readonly struct Fit
        {
            internal Fit(int columns, float cell, int pageSize)
            {
                Columns = columns;
                Cell = cell;
                PageSize = pageSize;
            }

            internal int Columns { get; }

            internal float Cell { get; }

            internal int PageSize { get; }
        }
    }
}
