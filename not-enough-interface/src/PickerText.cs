using System.Text.RegularExpressions;
using RoR2;

namespace NotEnoughInterface
{
    // What a picker entry can be found by: its name, short pickup text and full description in the current
    // language, rich text tags stripped, lowercase
    internal static class PickerText
    {
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);

        internal static string Of(PickupDef def)
        {
            string description = string.Empty;
            if (def.itemIndex != ItemIndex.None)
            {
                ItemDef item = ItemCatalog.GetItemDef(def.itemIndex);
                description = item ? item.descriptionToken : string.Empty;
            }
            else if (def.equipmentIndex != EquipmentIndex.None)
            {
                EquipmentDef equipment = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                description = equipment ? equipment.descriptionToken : string.Empty;
            }

            string text = string.Join("\n", Get(def.nameToken), Get(def.pickupToken), Get(description));
            return Tags.Replace(text, string.Empty).ToLowerInvariant();
        }

        // Every word of the query, anywhere in the text
        internal static bool Matches(string text, string[] words)
        {
            foreach (string word in words)
            {
                if (!text.Contains(word, System.StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Get(string? token)
        {
            return string.IsNullOrEmpty(token) ? string.Empty : Language.GetString(token);
        }
    }
}
