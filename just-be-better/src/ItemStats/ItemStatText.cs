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

        // Items outside the table, logged once each with where their stats came from
        private static readonly HashSet<int> moddedLogged = new HashSet<int>();
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
        // anything it adds around it stays, such as a Hex item's cleanse line or a void item's corrupts line
        // An item about to be corrupted gets only its text, the card for its void item has the stats
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
                {
                    string full = fullDescription ? DroneText(drone, true) : string.Empty;
                    return full.Length > 0 ? SwapIn(gameText, drone.pickupToken, full) : gameText;
                }
                default:
                    return null;
            }
        }

        // A pinged thing's card reads as the pickers' tooltips do, what picking it would give the player it shows
        // to, so a corrupted item shows the void item it adds to and a void item the stacks it takes in
        // Swapped in for the game's one-liner, so a Hex keeps its Hallowed Shrine hint
        internal static string? DescribePing(object data, CharacterMaster? master, string gameText, bool fullDescription, bool stats, string arrow)
        {
            return data is ItemDef item
                ? SwapIn(gameText, item.pickupToken, DescribeItem(item, master, fullDescription, stats, true, arrow))
                : DescribePickup(data, master, gameText, fullDescription, stats, arrow, null);
        }

        private static string SwapIn(string gameText, string shortToken, string fullToken, bool fullDescription)
        {
            return fullDescription && !Language.IsTokenInvalid(fullToken)
                ? SwapIn(gameText, shortToken, Language.GetString(fullToken))
                : gameText;
        }

        private static string SwapIn(string gameText, string shortToken, string full)
        {
            // A modded drone can leave its short token unset, and GetString throws on null
            string shortText = string.IsNullOrEmpty(shortToken) ? string.Empty : Language.GetString(shortToken);
            return !string.IsNullOrEmpty(shortText) && gameText.Contains(shortText) ? gameText.Replace(shortText, full) : full;
        }

        // Drones fill their tokens unevenly, some only the skill text, some only the description
        internal static string DroneText(DroneDef drone, bool fullDescription)
        {
            string? token = DroneToken(drone.skillDescriptionToken, drone.descriptionToken, drone.pickupToken, fullDescription, HasText);
            return token != null ? Language.GetString(token) : string.Empty;
        }

        // An ally's summoner, an item, equipment, skill or artifact, empty when the game has text for neither token
        internal static string TokenText(string? full, string? brief, bool fullDescription)
        {
            string? token = DroneToken(null, full, brief, fullDescription, HasText);
            return token != null ? Language.GetString(token) : string.Empty;
        }

        // Not IsTokenInvalid, which only asks the current language while GetString falls back to English
        // GetString hands back the token itself when no language has it
        private static bool HasText(string token)
        {
            string text = Language.GetString(token);
            return !string.IsNullOrEmpty(text) && text != token;
        }

        // The first token the game has text for, so the Bomber's missing short text falls back to its description
        // instead of showing DRONE_BOMBER_FUNCTION, and null when none has any
        internal static string? DroneToken(string? skill, string? full, string? brief, bool fullDescription, Func<string, bool> known)
        {
            string?[] order = fullDescription ? new[] { skill, full, brief } : new[] { brief, full, skill };
            foreach (string? token in order)
            {
                if (!string.IsNullOrEmpty(token) && known(token!))
                {
                    return token;
                }
            }

            return null;
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
            if (to <= 0 || failed || brokenStats.Contains(key))
            {
                return;
            }

            // A modded item's entry is read from another mod or its text, so it's inside the try with the rest
            try
            {
                if (!ItemDefinitions.allItemDefinitions.TryGetValue(key, out ItemStatsDef? def))
                {
                    def = ModdedStats.For(item, out string source);
                    foreach (string problem in ModdedStats.TakeProblems())
                    {
                        log?.LogWarning($"A modded item's stats couldn't be tied to its mod, it falls back to LookingGlass or its text: {problem}");
                    }

                    if (moddedLogged.Add(key))
                    {
                        log?.LogInfo(def != null
                            ? $"Stats for {item.name} come from {source}"
                            : $"No stats for {item.name}, its description has no plain per-stack values");
                    }
                }

                if (def == null || def.descriptions.Count == 0)
                {
                    return;
                }

                // Scrap only does something with the item that turns scrap into stats
                if (def.isScrap && master.inventory.GetItemCountEffective(DLC3Content.Items.StatsFromScrap) <= 0)
                {
                    return;
                }

                var lines = new StringBuilder("\n");
                if (from == 0 && firstStackHeading)
                {
                    lines.Append("\n<style=cStack>With this item:</style>");
                }

                if (AppendStackLines(lines, def, master, from, to, arrow))
                {
                    text.Append(lines);
                }
            }
            catch (Exception e)
            {
                brokenStats.Add(key);
                log?.LogWarning($"Couldn't work out the stats for {item.name}, its tooltip shows the description only: {e}");
            }
        }

        // The plain chance on the line, and what the holder's luck makes of it on its own line below
        private static bool AppendStackLines(StringBuilder lines, ItemStatsDef def, CharacterMaster master, int from, int to, string arrow)
        {
            List<float>? after = Values(def, master, to, master.luck);
            if (after == null)
            {
                return false;
            }

            bool held = from > 0 && from != to;
            List<float>? now = held ? Values(def, master, from, master.luck) : null;
            bool lucky = master.luck != 0f && def.calculateValuesFlat == null && def.calculateValuesNew != null;
            List<float>? afterPlain = lucky ? Values(def, master, to, 0f) : null;
            List<float>? nowPlain = lucky && held ? Values(def, master, from, 0f) : null;
            AppendLines(lines, def, now, after, arrow, nowPlain, afterPlain);
            return true;
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
                float luck = master != null ? master.luck : 0f;
                List<float>? values = def.calculateValuesFlat != null
                    ? def.calculateValuesFlat(0)
                    : def.calculateValuesNew?.Invoke(luck, 1, 1f);
                List<float>? plain = def.calculateValuesFlat == null && luck != 0f ? def.calculateValuesNew?.Invoke(0f, 1, 1f) : null;
                if (values != null)
                {
                    var lines = new StringBuilder("\n");
                    AppendLines(lines, def, null, values, string.Empty, null, plain);
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
        // Luck only reaches a chance through calculateValuesNew, so the rest read the same at any luck
        private static List<float>? Values(ItemStatsDef def, CharacterMaster master, int count, float luck)
        {
            if (def.calculateValuesFlat != null)
            {
                return def.calculateValuesFlat(count);
            }

            if (def.calculateValuesNew != null)
            {
                return def.calculateValuesNew(luck, count, 1f);
            }

            if (def.calculateValues != null)
            {
                return def.calculateValues(master, count);
            }

            CharacterBody body = master.GetBody();
            return def.calculateValuesBody != null && body ? def.calculateValuesBody(body, count) : null;
        }

        // now and after are the holder's values, luck included, and the plain ones at no luck give the From Luck line
        private static void AppendLines(
            StringBuilder text, ItemStatsDef def, List<float>? now, List<float> after, string arrow, List<float>? nowPlain, List<float>? afterPlain)
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
                AppendChange(text, style, current, next, arrow);

                // Only under a value luck changes, so a chance's line gets one and its damage line doesn't
                // Either end counts, as a stack that caps both still had luck's share before it
                ItemStatsDef.MeasurementUnits unit = def.measurementUnits[i];
                string? nextLuck = afterPlain != null && i < afterPlain.Count ? Signed(after[i] - afterPlain[i], unit) : null;
                string? currentLuck = now != null && nowPlain != null && i < now.Count && i < nowPlain.Count
                    ? Signed(now[i] - nowPlain[i], unit)
                    : null;
                string? none = Signed(0f, unit);
                if (nextLuck != null && (nextLuck != none || (currentLuck != null && currentLuck != none)))
                {
                    text.Append("\n<style=cStack>  From Luck:</style> ");
                    AppendChange(text, style, currentLuck, nextLuck, arrow);
                }
            }
        }

        // A plus on gains, so luck's share reads as an addition to the value above
        private static string? Signed(float value, ItemStatsDef.MeasurementUnits unit)
        {
            string? text = Format(value, unit);
            string? zero = Format(0f, unit);

            // A sliver either way rounds to 0, and newer .NET writes a small negative as -0
            if (text == null || text.TrimStart('-') == zero)
            {
                return text == null ? null : zero;
            }

            return value > 0f ? "+" + text : text;
        }

        private static void AppendChange(StringBuilder text, string style, string? current, string? next, string arrow)
        {
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
        // A sliver below zero rounds to 0, and some runtimes keep its sign as -0
        private static string? Format(float value, ItemStatsDef.MeasurementUnits unit)
        {
            string? text = FormatSigned(value, unit);
            return text != null && value < 0f && text == "-" + FormatSigned(0f, unit) ? text.Substring(1) : text;
        }

        private static string? FormatSigned(float value, ItemStatsDef.MeasurementUnits unit)
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
