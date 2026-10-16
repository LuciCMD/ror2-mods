using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoR2;

namespace JustBeBetter.ItemStats
{
    // Stats for items our table doesn't have: what a mod registered with LookingGlass when it's installed, as the
    // mod's own author wrote those, else what the item's description says per stack
    internal static class ModdedStats
    {
        private const string LookingGlassTable = "LookingGlass.ItemStatsNameSpace.ItemDefinitions, LookingGlass";

        // Read again from the description only when its text changes, as with a language switch
        private static readonly Dictionary<int, (string Text, ItemStatsDef? Def)> FromText = new Dictionary<int, (string, ItemStatsDef?)>();

        // A mod can register with LookingGlass at any time, so only what was found is kept
        private static readonly Dictionary<int, ItemStatsDef> FromLookingGlass = new Dictionary<int, ItemStatsDef>();

        private static FieldInfo? lookingGlassField;
        private static bool searched;

        // Mods we have entries for, written from their code, ahead of LookingGlass and the description
        private static readonly (string Mod, ModBinding Binding)[] Bindings =
        {
            ("Aetherium", new ModBinding("Aetherium", AetheriumEntries.All)),
        };

        // What didn't bind, each reported once
        internal static IEnumerable<string> TakeProblems()
        {
            var problems = new List<string>();
            foreach ((string mod, ModBinding binding) in Bindings)
            {
                problems.AddRange(binding.Problems.Select(problem => $"{mod}: {problem}"));
                binding.Problems.Clear();
            }

            return problems;
        }

        // Where the stats came from, for the log, empty when none were found
        internal static ItemStatsDef? For(ItemDef item, out string source)
        {
            source = string.Empty;
            foreach ((string mod, ModBinding binding) in Bindings)
            {
                ItemStatsDef? entry = binding.For(item);
                if (entry != null)
                {
                    source = $"our entry for {mod}, with its config values";
                    return entry;
                }
            }

            int key = (int)item.itemIndex;
            ItemStatsDef? def = LookingGlassDef(key);
            if (def != null)
            {
                source = "LookingGlass's table";
                return def;
            }

            // A mod can leave the token unset, and GetString throws on null
            if (string.IsNullOrEmpty(item.descriptionToken))
            {
                return null;
            }

            string text = Language.GetString(item.descriptionToken);
            if (!FromText.TryGetValue(key, out (string Text, ItemStatsDef? Def) cached) || cached.Text != text)
            {
                cached = (text, StackText.ToDef(StackText.Read(text)));
                FromText[key] = cached;
            }

            if (cached.Def != null)
            {
                source = "its description";
            }

            return cached.Def;
        }

        private static ItemStatsDef? LookingGlassDef(int key)
        {
            if (FromLookingGlass.TryGetValue(key, out ItemStatsDef found))
            {
                return found;
            }

            if (!searched)
            {
                searched = true;
                lookingGlassField = Type.GetType(LookingGlassTable, false)?.GetField("allItemDefinitions", BindingFlags.Public | BindingFlags.Static);
            }

            if (lookingGlassField == null || !(lookingGlassField.GetValue(null) is IDictionary table) || !table.Contains(key))
            {
                return null;
            }

            ItemStatsDef? def = Convert(table[key]);
            if (def != null)
            {
                FromLookingGlass[key] = def;
            }

            return def;
        }

        // LookingGlass's def has the same fields as ours, its enums matched by name
        private static ItemStatsDef? Convert(object? theirs)
        {
            if (theirs == null)
            {
                return null;
            }

            Type type = theirs.GetType();
            object? Field(string name) => type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(theirs);

            var def = new ItemStatsDef();
            foreach (object? description in Field("descriptions") as IEnumerable ?? Array.Empty<object>())
            {
                def.descriptions.Add(description?.ToString() ?? string.Empty);
            }

            foreach (object? value in Field("valueTypes") as IEnumerable ?? Array.Empty<object>())
            {
                def.valueTypes.Add(Enum.TryParse(value?.ToString(), out ItemStatsDef.ValueType parsed) ? parsed : ItemStatsDef.ValueType.None);
            }

            foreach (object? unit in Field("measurementUnits") as IEnumerable ?? Array.Empty<object>())
            {
                def.measurementUnits.Add(Enum.TryParse(unit?.ToString(), out ItemStatsDef.MeasurementUnits parsed) ? parsed : ItemStatsDef.MeasurementUnits.Number);
            }

            def.calculateValuesBody = Field("calculateValuesBody") as Func<CharacterBody, int, List<float>>;
            def.calculateValues = Field("calculateValues") as Func<CharacterMaster, int, List<float>>;
            def.calculateValuesFlat = Field("calculateValuesFlat") as Func<int, List<float>>;
            def.calculateValuesNew = Field("calculateValuesNew") as Func<float, int, float, List<float>>;
            def.isScrap = Field("isScrap") is bool scrap && scrap;
            def.hasChance = Field("hasChance") is bool chance && chance;
            def.chanceScaling = Enum.TryParse(Field("chanceScaling")?.ToString(), out ItemStatsDef.ChanceScaling scaling) ? scaling : ItemStatsDef.ChanceScaling.Linear;

            bool computes = def.calculateValuesBody != null || def.calculateValues != null || def.calculateValuesFlat != null || def.calculateValuesNew != null;
            return computes && def.descriptions.Count > 0 ? def : null;
        }
    }
}
