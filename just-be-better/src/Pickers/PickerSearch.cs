using System;
using System.Collections.ObjectModel;
using System.Reflection;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.UI;

namespace JustBeBetter.Pickers
{
    // Search and favourites for every item picker built on PickupPickerPanel: the Command artifact, the
    // Scrapper, Potentials and the rest
    // The panel binds each button to its option index when it creates it, so buttons are only hidden and
    // reordered, never rebound, and the choice sent to the server is unchanged
    internal sealed class PickerSearch : IFeature
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly ManualLogSource log;
        private readonly Favourites favourites;
        private FieldInfo? buttonsField;
        private FieldInfo? optionsField;
        private FieldInfo? automaticNavigationField;

        internal PickerSearch(ManualLogSource log, Favourites favourites)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.favourites = favourites ?? throw new ArgumentNullException(nameof(favourites));
        }

        public void Enable()
        {
            Type type = typeof(PickupPickerPanel);
            buttonsField = type.GetField("buttonAllocator", Private);
            optionsField = type.GetField("pickupOptions", Private);
            automaticNavigationField = type.GetField("automaticButtonNavigation", Private);
            if (buttonsField == null || optionsField == null)
            {
                log.LogWarning("The item picker's internals have changed; it stays as the game has it");
                return;
            }

            On.RoR2.UI.PickupPickerPanel.SetPickupOptions += OnSetPickupOptions;
        }

        public void Disable()
        {
            On.RoR2.UI.PickupPickerPanel.SetPickupOptions -= OnSetPickupOptions;
        }

        private void OnSetPickupOptions(On.RoR2.UI.PickupPickerPanel.orig_SetPickupOptions orig, PickupPickerPanel self, PickupPickerController.Option[] options)
        {
            orig(self, options);
            try
            {
                Attach(self);
            }
            catch (Exception e)
            {
                log.LogError($"Couldn't add search to the item picker: {e}");
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
            PickerFilter filter = PickerFilter.For(panel, favourites, automatic);
            filter.Clear();
            int count = Math.Min(elements.Count, options.Length);
            for (int i = 0; i < count; i++)
            {
                PickupDef def = PickupCatalog.GetPickupDef(options[i].pickup.pickupIndex);
                filter.Add(elements[i], def?.internalName, def != null ? PickerText.Of(def) : string.Empty);
            }

            filter.Apply();
        }
    }
}
