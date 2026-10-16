using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.InventorySort;
using JustBeBetter.ItemInfo;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;

namespace JustBeBetter.Pickers
{
    // Search, favourites, item counts and tooltips for every item picker built on PickupPickerPanel: the
    // Command artifact, the Scrapper, Potentials and the rest
    // The panel binds each button to its option index when it creates it, so buttons are only hidden and
    // reordered, never rebound, and the choice sent to the server is unchanged
    internal sealed class PickerSearch : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly HashSet<string> LoggedUsable = new HashSet<string>();

        private readonly PickerSettings settings;
        private readonly ItemInfoSettings info;
        private readonly Func<SortRules?> inventoryRules;
        private readonly ManualLogSource log;
        private readonly Favourites favourites;
        private FieldInfo? buttonsField;
        private FieldInfo? optionsField;
        private FieldInfo? automaticNavigationField;
        private FieldInfo? inspectField;
        private HashSet<PickupIndex>? usableIngredients;
        private Run? usableFor;
        private bool recipesFailed;

        internal PickerSearch(PickerSettings settings, ItemInfoSettings info, Func<SortRules?> inventoryRules, ManualLogSource log, Favourites favourites)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.info = info ?? throw new ArgumentNullException(nameof(info));
            this.inventoryRules = inventoryRules ?? throw new ArgumentNullException(nameof(inventoryRules));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.favourites = favourites ?? throw new ArgumentNullException(nameof(favourites));
        }

        public void Enable()
        {
            Type type = typeof(PickupPickerPanel);
            buttonsField = type.GetField("buttonAllocator", Private);
            optionsField = type.GetField("pickupOptions", Private);
            automaticNavigationField = type.GetField("automaticButtonNavigation", Private);
            inspectField = type.GetField("_inspectFeatureIsEnabled", Private);
            if (buttonsField == null || optionsField == null)
            {
                log.LogWarning("The item picker's internals have changed; it stays as the game has it");
                return;
            }

            On.RoR2.UI.PickupPickerPanel.SetPickupOptions += OnSetPickupOptions;
            On.RoR2.UI.PickupPickerPanel.RemovePickupButtonAvailability += OnRemovePickupButtonAvailability;
        }

        public void Disable()
        {
            On.RoR2.UI.PickupPickerPanel.SetPickupOptions -= OnSetPickupOptions;
            On.RoR2.UI.PickupPickerPanel.RemovePickupButtonAvailability -= OnRemovePickupButtonAvailability;
        }

        // A multi-choice picker marks a taken option unavailable and turns its tooltip off, so the details
        // are redone for it
        private void OnRemovePickupButtonAvailability(On.RoR2.UI.PickupPickerPanel.orig_RemovePickupButtonAvailability orig, PickupPickerPanel self, int pickupOption)
        {
            orig(self, pickupOption);
            try
            {
                if (self.TryGetComponent(out PickerDetails details))
                {
                    details.Refresh();
                }
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't update the item picker after an option was taken: {e}");
            }
        }

        private void OnSetPickupOptions(On.RoR2.UI.PickupPickerPanel.orig_SetPickupOptions orig, PickupPickerPanel self, PickupPickerController.Option[] options)
        {
            try
            {
                PickerWindow.For(self, settings, log).Fit(options?.Length ?? 0);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't fit the item picker's window to its items: {e}");
            }

            orig(self, options);
            try
            {
                PickerWindow.For(self, settings, log).AfterSetup();
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't keep the item picker's window at its fitted size: {e}");
            }

            try
            {
                Attach(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't add search to the item picker: {e}");
            }

            try
            {
                AttachDetails(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't add item counts and tooltips to the item picker: {e}");
            }

            try
            {
                PickerWindow.For(self, settings, log).ApplyLook();
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't set the item picker window's blur and opacity: {e}");
            }

            try
            {
                PickerCloser.Attach(self, settings, log);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't let movement close the item picker: {e}");
            }
        }

        private void Attach(PickupPickerPanel panel)
        {
            var allocator = buttonsField!.GetValue(panel) as UIElementAllocator<MPButton>;
            var options = optionsField!.GetValue(panel) as PickupPickerController.Option[];
            if (allocator == null || options == null)
            {
                return;
            }

            ReadOnlyCollection<MPButton> elements = allocator.elements;
            bool automatic = automaticNavigationField?.GetValue(panel) is bool b && b;
            ConfigEntry<PickerOrder>? sorting = SortingFor(panel);
            bool crafting = sorting == settings.CraftingSorting;
            Func<SortRules?>? follow = sorting == settings.ScrapperSorting || crafting ? inventoryRules : null;
            HashSet<PickupIndex>? usable = crafting ? UsableIngredients() : null;
            PickerFilter filter = PickerFilter.For(panel, favourites, automatic, sorting, follow, crafting ? settings.UsableFirst : null, crafting);
            filter.Clear();
            CharacterMaster? master = PickerDetails.MasterOf(panel.GetComponentInParent<HUD>());
            Inventory? inventory = master != null ? master.inventory : null;
            int count = Math.Min(elements.Count, options.Length);
            for (int i = 0; i < count; i++)
            {
                PickupDef def = PickupCatalog.GetPickupDef(options[i].pickup.pickupIndex);
                ItemDef? item = ItemOf(def);
                filter.Add(
                    elements[i],
                    def?.internalName,
                    def != null ? PickerText.Of(def) : string.Empty,
                    def != null ? PickerText.NameOf(def) : string.Empty,
                    item ? PickerSort.TierRank(item!.tier) : PickerSort.NotAnItem,
                    PickerDetails.HeldCount(def, inventory),
                    item ? item!.tier : (ItemTier?)null,
                    item && InventorySorter.IsScrap(item!),
                    usable != null && !usable.Contains(options[i].pickup.pickupIndex));
            }

            filter.Apply();
            LogSorting(panel.name, sorting, count, inventory != null, follow?.Invoke() != null);
            if (usable != null && Logs.VerboseOn && LoggedUsable.Add(panel.name))
            {
                int unusable = options.Take(count).Count(o => !usable.Contains(o.pickup.pickupIndex));
                log.Verbose($"{panel.name}: {usable.Count} pickups some recipe can take, {unusable} of the {count} offered can't");
            }
        }

        // Every pickup a recipe of this run can take, the same list the menu starts from with its slots empty
        // Recipes for a DLC the run doesn't have are left out, as the game does
        private HashSet<PickupIndex>? UsableIngredients()
        {
            if (recipesFailed)
            {
                return null;
            }

            if (usableIngredients != null && usableFor == Run.instance)
            {
                return usableIngredients;
            }

            var found = new HashSet<PickupIndex>();
            try
            {
                CraftableCatalog.RecipeEntry[] recipes = CraftableCatalog.GetAllRecipes();
                if (Run.instance)
                {
                    CraftableCatalog.FilterByEntitlement(ref recipes);
                }

                foreach (CraftableCatalog.RecipeEntry recipe in recipes)
                {
                    List<PickupIndex>? pickups = recipe?.GetAllPickups();
                    if (pickups != null)
                    {
                        found.UnionWith(pickups);
                    }
                }
            }
            catch (Exception e)
            {
                if (!recipesFailed)
                {
                    recipesFailed = true;
                    log.LogWarning($"Couldn't read the recipes, so unusable items stay in their sorted place: {e}");
                }

                return null;
            }

            usableIngredients = found;
            usableFor = Run.instance;
            return found;
        }

        // By the panel, as the drone scrapper's DroneScrapperPickerPanel isn't the Scrapper
        private ConfigEntry<PickerOrder>? SortingFor(PickupPickerPanel panel)
        {
            if (panel.GetComponent<CraftingPanel>())
            {
                return settings.CraftingSorting;
            }

            string name = panel.gameObject.name;
            if (name.StartsWith("CommandPickerPanel", StringComparison.Ordinal))
            {
                return settings.CommandSorting;
            }

            return name.StartsWith("ScrapperPickerPanel", StringComparison.Ordinal) ? settings.ScrapperSorting : null;
        }

        private static ItemDef? ItemOf(PickupDef? def)
        {
            return def != null && def.itemIndex != ItemIndex.None ? ItemCatalog.GetItemDef(def.itemIndex) : null;
        }

        // Once per picker and sort, so a picker left unsorted or sorted without counts says why
        private void LogSorting(string name, ConfigEntry<PickerOrder>? sorting, int count, bool counted, bool followsInventory)
        {
            string order = sorting != null ? sorting.Value.ToString() : "unsorted";
            if (followsInventory && sorting?.Value == PickerOrder.ByTier)
            {
                order += " following Inventory Sort";
            }

            // The warning always counts, the others only once Verbose Logging can show them
            bool warns = sorting != null && !counted && sorting.Value != PickerOrder.GamesOrder;
            if ((!warns && !Logs.VerboseOn) || !Logged.Add($"{name} {order}"))
            {
                return;
            }

            if (sorting == null)
            {
                log.Verbose($"{name}: {count} items, not the Command menu, Scrapper or a crafting menu so only favourites move");
            }
            else if (warns)
            {
                log.LogWarning($"{name}: {count} items sorted {order}, but no inventory was found so every count is 0");
            }
            else
            {
                log.Verbose($"{name}: {count} items sorted {order}");
            }
        }

        private void AttachDetails(PickupPickerPanel panel)
        {
            var allocator = buttonsField!.GetValue(panel) as UIElementAllocator<MPButton>;
            var options = optionsField!.GetValue(panel) as PickupPickerController.Option[];
            if (allocator == null || options == null)
            {
                return;
            }

            ReadOnlyCollection<MPButton> elements = allocator.elements;
            PickerDetails details = PickerDetails.For(panel, settings, info, log);
            details.Clear(options, inspectField?.GetValue(panel) is bool inspect && inspect);
            int count = Math.Min(elements.Count, options.Length);
            for (int i = 0; i < count; i++)
            {
                details.Add(elements[i], i, PickupCatalog.GetPickupDef(options[i].pickup.pickupIndex));
            }

            details.Refresh();
        }
    }
}
