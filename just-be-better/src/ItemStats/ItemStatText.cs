using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using JustBeBetter.Shared;
using RoR2;
using RoR2.Items;

namespace JustBeBetter.ItemStats
{
    // A pickup's tooltip body: its description, then the stat lines from LookingGlass's table
    // In a picker the lines show what picking it gives, now → after, or after alone under a heading for a
    // first stack, since the table's values at zero stacks aren't always meaningful
    internal static class ItemStatText
    {
        private const string Faded = "#808080";

        private static readonly HashSet<int> brokenStats = new HashSet<int>();
        private static readonly MethodInfo? cooldownScaleMethod =
            typeof(Inventory).GetMethod("CalculateEquipmentCooldownScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool cooldownScaleBroken;
        private static ManualLogSource? log;
        private static bool loaded;
        private static bool failed;

        internal static void EnsureLoaded(ManualLogSource source)
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            log = source;
            try
            {
                ItemDefinitions.RegisterAll();
                source.LogInfo(
                    $"Item stats ready for {ItemDefinitions.allItemDefinitions.Count} items and "
                    + $"{ItemDefinitions.allEquipmentDefinitions.Count} equipment");
            }
            catch (Exception e)
            {
                // A table that stopped partway can't be trusted, so it's not used at all
                failed = true;
                source.LogError($"Couldn't build the item stats, tooltips show descriptions only: {e}");
            }
        }

        // Null when the pickup is neither an item, equipment nor a drone
        // Gains is whether picking it gives you one, false for the Scrapper or a locked option, which show the
        // values you have now
        internal static string? Describe(PickupDef def, CharacterMaster? master, bool fullDescription, bool stats, bool gains, string arrow)
        {
            if (def.itemIndex != ItemIndex.None)
            {
                ItemDef item = ItemCatalog.GetItemDef(def.itemIndex);
                return item != null ? DescribeItem(item, master, fullDescription, stats, gains, arrow) : null;
            }

            if (def.equipmentIndex != EquipmentIndex.None)
            {
                EquipmentDef equipment = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                return equipment != null ? DescribeEquipment(equipment, master, fullDescription, stats) : null;
            }

            if (def.droneIndex != DroneIndex.None)
            {
                DroneDef drone = DroneCatalog.GetDroneDef(def.droneIndex);
                return drone ? DroneText(drone, fullDescription) : null;
            }

            return null;
        }

        // An item as someone holds it, for their inventory, its stats at the count they have
        // Given the game's text, only its one-liner is swapped, so a Hex item keeps its Hallowed Shrine hint
        internal static string DescribeHeld(ItemDef item, CharacterMaster? master, bool fullDescription, bool stats, string? gameText = null)
        {
            if (gameText == null)
            {
                return DescribeItem(item, master, fullDescription, stats, false, string.Empty);
            }

            var text = new StringBuilder(SwapIn(gameText, item.pickupToken, item.descriptionToken, fullDescription));
            Inventory? inventory = master != null && master.inventory ? master.inventory : null;
            if (stats && inventory != null)
            {
                int have = inventory.GetItemCountEffective(item.itemIndex);
                AppendStats(text, item, master!, have, have, string.Empty, true);
            }

            return text.ToString();
        }

        internal static string DescribeHeld(EquipmentDef equipment, CharacterMaster? master, bool fullDescription, bool stats)
        {
            return DescribeEquipment(equipment, master, fullDescription, stats);
        }

        // For the card shown after a pickup, so the lines are what it just changed, before → after
        // Change is the count change matched to this card, else it's taken as the latest one stack
        // The game's own card text is kept and only its one-liner swapped for the full description, so
        // anything it adds around it stays, such as a Hex item's cleanse line
        // An item about to be corrupted gets only its text
        internal static string? DescribePickup(
            object data, CharacterMaster? master, string gameText, bool fullDescription, bool stats, string arrow, (int Before, int After)? change)
        {
            switch (data)
            {
                case ItemDef item:
                {
                    var text = new StringBuilder(SwapIn(gameText, item.pickupToken, item.descriptionToken, fullDescription));
                    Inventory? inventory = master != null && master.inventory ? master.inventory : null;
                    if (stats && inventory != null && !IsCorrupted(item.itemIndex, inventory))
                    {
                        int held = inventory.GetItemCountEffective(item.itemIndex);
                        (int before, int after) = change ?? (held - 1, held);
                        AppendStats(text, item, master!, before, after, arrow, false);
                    }

                    return text.ToString();
                }

                case EquipmentDef equipment:
                {
                    var text = new StringBuilder(SwapIn(gameText, equipment.pickupToken, equipment.descriptionToken, fullDescription));
                    if (stats)
                    {
                        AppendEquipmentDetails(text, equipment, master);
                    }

                    return text.ToString();
                }

                case DroneDef drone:
                    return fullDescription ? SwapIn(gameText, drone.pickupToken, DroneText(drone, true)) : gameText;
                default:
                    return null;
            }
        }

        private static string SwapIn(string gameText, string shortToken, string fullToken, bool fullDescription)
        {
            return fullDescription && !Language.IsTokenInvalid(fullToken)
                ? SwapIn(gameText, shortToken, Language.GetString(fullToken))
                : gameText;
        }

        private static string SwapIn(string gameText, string shortToken, string full)
        {
            string shortText = Language.GetString(shortToken);
            return !string.IsNullOrEmpty(shortText) && gameText.Contains(shortText) ? gameText.Replace(shortText, full) : full;
        }

        // Drones fill their tokens unevenly, some only the skill text, some only the description
        private static string DroneText(DroneDef drone, bool fullDescription)
        {
            if (fullDescription && !Language.IsTokenInvalid(drone.skillDescriptionToken))
            {
                return Language.GetString(drone.skillDescriptionToken);
            }

            return Description(drone.descriptionToken, drone.pickupToken, fullDescription);
        }

        private static bool IsCorrupted(ItemIndex item, Inventory inventory)
        {
            ItemIndex voidItem = ContagiousItemManager.GetTransformedItemIndex(item);
            return voidItem != ItemIndex.None && inventory.GetItemCountEffective(voidItem) > 0;
        }

        private static string DescribeItem(ItemDef item, CharacterMaster? master, bool fullDescription, bool stats, bool gains, string arrow)
        {
            string description = Description(item.descriptionToken, item.pickupToken, fullDescription);
            Inventory? inventory = master != null && master.inventory ? master.inventory : null;
            if (inventory == null)
            {
                return description;
            }

            var text = new StringBuilder();
            if (!gains)
            {
                int have = inventory.GetItemCountEffective(item.itemIndex);
                text.Append(description);
                if (stats)
                {
                    AppendStats(text, item, master!, have, have, arrow, true);
                }

                return text.ToString();
            }

            // Held as its void item, so picking it adds to that instead
            ItemIndex voidIndex = ContagiousItemManager.GetTransformedItemIndex(item.itemIndex);
            ItemDef? voidItem = voidIndex != ItemIndex.None && inventory.GetItemCountEffective(voidIndex) > 0
                ? ItemCatalog.GetItemDef(voidIndex)
                : null;
            if (voidItem != null)
            {
                int voidHeld = inventory.GetItemCountEffective(voidIndex);
                text.Append("<color=").Append(Faded).Append('>').Append(description).Append("</color>");
                text.Append("\n\nCorrupted by <style=cIsVoid>").Append(Language.GetString(voidItem.nameToken))
                    .Append("</style>, so picking it adds to that\n\n");
                text.Append(Description(voidItem.descriptionToken, voidItem.pickupToken, fullDescription));
                if (stats)
                {
                    AppendStats(text, voidItem, master!, voidHeld, voidHeld + 1, arrow, true);
                }

                return text.ToString();
            }

            // A void item takes in the items it corrupts once picked
            int held = inventory.GetItemCountEffective(item.itemIndex);
            text.Append(description);
            string corrupts = Corrupts(item.itemIndex, inventory, out int absorbed);
            if (absorbed > 0)
            {
                text.Append("\n\n<style=cIsVoid>Picking it corrupts your ").Append(corrupts).Append("</style>");
            }

            if (stats)
            {
                AppendStats(text, item, master!, held, held + absorbed + 1, arrow, true);
            }

            return text.ToString();
        }

        private static string Corrupts(ItemIndex voidItem, Inventory inventory, out int absorbed)
        {
            absorbed = 0;
            var names = new List<string>();
            foreach (ContagiousItemManager.TransformationInfo info in ContagiousItemManager.transformationInfos)
            {
                if (info.transformedItem != voidItem)
                {
                    continue;
                }

                int count = inventory.GetItemCountEffective(info.originalItem);
                ItemDef original = ItemCatalog.GetItemDef(info.originalItem);
                if (count > 0 && original != null)
                {
                    absorbed += count;
                    names.Add($"{count} {Language.GetString(original.nameToken)}");
                }
            }

            return string.Join(", ", names);
        }

        // A formula that fails leaves the description on its own, and is logged once
        // From equal to to shows those values alone, with nothing for an item you don't have
        private static void AppendStats(StringBuilder text, ItemDef item, CharacterMaster master, int from, int to, string arrow, bool firstStackHeading)
        {
            int key = (int)item.itemIndex;
            if (to <= 0 || failed || brokenStats.Contains(key)
                || !ItemDefinitions.allItemDefinitions.TryGetValue(key, out ItemStatsDef? def)
                || def.descriptions.Count == 0)
            {
                return;
            }

            // Scrap only does something with the item that turns scrap into stats
            if (def.isScrap && master.inventory.GetItemCountEffective(DLC3Content.Items.StatsFromScrap) <= 0)
            {
                return;
            }

            try
            {
                List<float>? after = Values(def, master, to);
                if (after == null)
                {
                    return;
                }

                List<float>? now = from > 0 && from != to ? Values(def, master, from) : null;
                var lines = new StringBuilder("\n");
                if (from == 0 && firstStackHeading)
                {
                    lines.Append("\n<style=cStack>With this item:</style>");
                }

                AppendLines(lines, def, now, after, arrow);
                text.Append(lines);
            }
            catch (Exception e)
            {
                brokenStats.Add(key);
                log?.LogWarning($"Couldn't work out the stats for {item.name}, its tooltip shows the description only: {e}");
            }
        }

        // Equipment doesn't stack, so its lines are the values it has, as LookingGlass shows them
        private static string DescribeEquipment(EquipmentDef equipment, CharacterMaster? master, bool fullDescription, bool stats)
        {
            var text = new StringBuilder(Description(equipment.descriptionToken, equipment.pickupToken, fullDescription));
            if (stats)
            {
                AppendEquipmentDetails(text, equipment, master);
            }

            return text.ToString();
        }

        // Its stat lines, then its cooldown as the holder has it, in the same block
        private static void AppendEquipmentDetails(StringBuilder text, EquipmentDef equipment, CharacterMaster? master)
        {
            int before = text.Length;
            AppendEquipmentStats(text, equipment, master);
            if (equipment.cooldown <= 0f)
            {
                return;
            }

            if (text.Length == before)
            {
                text.Append('\n');
            }

            Inventory? inventory = master != null && master.inventory ? master.inventory : null;
            CooldownText.Append(text, equipment.cooldown, equipment.cooldown * CooldownScale(inventory));
        }

        // The game's own scale, Fuel Cells, Gesture, Clarity and the rest, private so read through reflection
        private static float CooldownScale(Inventory? inventory)
        {
            if (inventory == null || cooldownScaleMethod == null || cooldownScaleBroken)
            {
                return 1f;
            }

            try
            {
                return cooldownScaleMethod.Invoke(inventory, null) is float scale && scale > 0f ? scale : 1f;
            }
            catch (Exception e)
            {
                cooldownScaleBroken = true;
                log?.LogWarning($"Couldn't work out the equipment cooldown, tooltips show the base value: {e}");
                return 1f;
            }
        }

        private static void AppendEquipmentStats(StringBuilder text, EquipmentDef equipment, CharacterMaster? master)
        {
            // Equipment keys sit below zero so they can't collide with items
            int key = -1 - (int)equipment.equipmentIndex;
            if (failed || brokenStats.Contains(key)
                || !ItemDefinitions.allEquipmentDefinitions.TryGetValue((int)equipment.equipmentIndex, out ItemStatsDef? def)
                || def.descriptions.Count == 0)
            {
                return;
            }

            try
            {
                List<float>? values = def.calculateValuesFlat != null
                    ? def.calculateValuesFlat(0)
                    : def.calculateValuesNew?.Invoke(master != null ? master.luck : 0f, 1, 1f);
                if (values != null)
                {
                    var lines = new StringBuilder("\n");
                    AppendLines(lines, def, null, values, string.Empty);
                    text.Append(lines);
                }
            }
            catch (Exception e)
            {
                brokenStats.Add(key);
                log?.LogWarning($"Couldn't work out the stats for {equipment.name}, its tooltip shows the description only: {e}");
            }
        }

        private static string Description(string fullToken, string shortToken, bool fullDescription)
        {
            return Language.GetString(fullDescription && !Language.IsTokenInvalid(fullToken) ? fullToken : shortToken);
        }

        // In the order LookingGlass tries them
        private static List<float>? Values(ItemStatsDef def, CharacterMaster master, int count)
        {
            if (def.calculateValuesFlat != null)
            {
                return def.calculateValuesFlat(count);
            }

            if (def.calculateValuesNew != null)
            {
                return def.calculateValuesNew(master.luck, count, 1f);
            }

            if (def.calculateValues != null)
            {
                return def.calculateValues(master, count);
            }

            CharacterBody body = master.GetBody();
            return def.calculateValuesBody != null && body ? def.calculateValuesBody(body, count) : null;
        }

        private static void AppendLines(StringBuilder text, ItemStatsDef def, List<float>? now, List<float> after, string arrow)
        {
            int lines = Math.Min(def.descriptions.Count, Math.Min(def.valueTypes.Count, def.measurementUnits.Count));
            for (int i = 0; i < lines; i++)
            {
                text.Append("\n<color=white>").Append(def.descriptions[i]).Append("</color>");
                if (i >= after.Count)
                {
                    continue;
                }

                string style = Style(def.valueTypes[i]);
                string? next = Format(after[i], def.measurementUnits[i]);
                string? current = now != null && i < now.Count ? Format(now[i], def.measurementUnits[i]) : null;
                if (current != null && next != null && current != next)
                {
                    AppendValue(text, style, current);
                    text.Append(arrow);
                }

                if (next != null)
                {
                    AppendValue(text, style, next);
                }
            }
        }

        private static void AppendValue(StringBuilder text, string style, string value)
        {
            if (style.Length == 0)
            {
                text.Append(value);
                return;
            }

            text.Append("<style=").Append(style).Append('>').Append(value).Append("</style>");
        }

        // The game's own text styles, picked per kind of value as LookingGlass does
        private static string Style(ItemStatsDef.ValueType type)
        {
            switch (type)
            {
                case ItemStatsDef.ValueType.Healing:
                case ItemStatsDef.ValueType.Armor:
                    return "cIsHealing";
                case ItemStatsDef.ValueType.Damage:
                    return "cIsDamage";
                case ItemStatsDef.ValueType.Utility:
                    return "cIsUtility";
                case ItemStatsDef.ValueType.Health:
                    return "cIsHealth";
                case ItemStatsDef.ValueType.Void:
                    return "cIsVoid";
                case ItemStatsDef.ValueType.Gold:
                case ItemStatsDef.ValueType.HumanObjective:
                    return "cHumanObjective";
                case ItemStatsDef.ValueType.LunarObjective:
                    return "cLunarObjective";
                case ItemStatsDef.ValueType.Stack:
                    return "cStack";
                case ItemStatsDef.ValueType.WorldEvent:
                    return "cWorldEvent";
                case ItemStatsDef.ValueType.Artifact:
                    return "cArtifact";
                case ItemStatsDef.ValueType.UserSetting:
                    return "cUserSetting";
                case ItemStatsDef.ValueType.Death:
                    return "cDeath";
                case ItemStatsDef.ValueType.Sub:
                    return "cSub";
                case ItemStatsDef.ValueType.Mono:
                    return "cMono";
                case ItemStatsDef.ValueType.Shrine:
                    return "cShrine";
                case ItemStatsDef.ValueType.Event:
                    return "cEvent";
                default:
                    return string.Empty;
            }
        }

        // Null for a value that can't be shown, a plain string unit has none
        private static string? Format(float value, ItemStatsDef.MeasurementUnits unit)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return null;
            }

            CultureInfo c = CultureInfo.InvariantCulture;
            switch (unit)
            {
                case ItemStatsDef.MeasurementUnits.Meters:
                    return value.ToString("0.##", c) + "m";
                case ItemStatsDef.MeasurementUnits.Percentage:
                    return (value * 100f).ToString("0.##", c) + "%";
                case ItemStatsDef.MeasurementUnits.FlatHealth:
                    return value.ToString("0.##", c) + " HP";
                case ItemStatsDef.MeasurementUnits.PercentHealth:
                    return (value * 100f).ToString("0.##", c) + "% HP";
                case ItemStatsDef.MeasurementUnits.FlatHealing:
                    return value.ToString("0.##", c) + " HP/s";
                case ItemStatsDef.MeasurementUnits.PercentHealing:
                    return (value * 100f).ToString("0.##", c) + "% HP/s";
                case ItemStatsDef.MeasurementUnits.Number:
                    return value.ToString("0.##", c);
                case ItemStatsDef.MeasurementUnits.ProcCoeff:
                    return value.ToString("0.0##", c);
                case ItemStatsDef.MeasurementUnits.Money:
                    return value.ToString("0.#", c) + "$";
                case ItemStatsDef.MeasurementUnits.Seconds:
                    return value.ToString("0.##", c) + " seconds";
                default:
                    return null;
            }
        }
    }
}
