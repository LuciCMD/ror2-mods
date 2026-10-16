using System;
using System.Collections.Generic;
using System.Linq;

namespace JustBeBetter.ItemStats
{
    // A modded item's stat lines, written from the mod's own code, with its config values read by name at the time
    // so a changed config shows straight away
    internal sealed class ModEntry
    {
        internal ModEntry(string typeName, params ModLine[] lines)
        {
            TypeName = typeName;
            Lines = lines;
        }

        // The item's class in the mod, which holds the config values
        internal string TypeName { get; }

        internal ModLine[] Lines { get; }

        // The first line without a number, as an odd config can divide by zero, checked before an entry is used
        // so a hover never shows a label with nothing after it
        internal (string Label, int Count)? FirstGap(ItemStatsDef def, int maxStacks)
        {
            for (int count = 1; count <= maxStacks; count++)
            {
                List<float> values = def.calculateValuesNew(0f, count, 1f);
                int bad = values.FindIndex(value => float.IsNaN(value) || float.IsInfinity(value));
                if (bad >= 0)
                {
                    return (Lines[bad].Label, count);
                }
            }

            return null;
        }

        // config gives a config value by its field name, and throws when the mod no longer has it
        internal ItemStatsDef ToDef(Func<string, float> config)
        {
            var def = new ItemStatsDef();
            foreach (ModLine line in Lines)
            {
                def.descriptions.Add(line.Label + ": ");
                def.valueTypes.Add(line.Type);
                def.measurementUnits.Add(line.Unit);
            }

            def.hasChance = Lines.Any(line => line.Chance);
            def.calculateValuesNew = (luck, count, proc) => Lines
                .Select(line => line.Chance ? Utils.CalculateChanceWithLuck(line.Value(config, count), luck) : line.Value(config, count))
                .ToList();
            return def;
        }
    }

    internal sealed class ModLine
    {
        internal ModLine(string label, ItemStatsDef.MeasurementUnits unit, ItemStatsDef.ValueType type, Func<Func<string, float>, int, float> value, bool chance = false)
        {
            Label = label;
            Unit = unit;
            Type = type;
            Value = value;
            Chance = chance;
        }

        internal string Label { get; }

        internal ItemStatsDef.MeasurementUnits Unit { get; }

        internal ItemStatsDef.ValueType Type { get; }

        internal Func<Func<string, float>, int, float> Value { get; }

        // Rolled with the holder's luck, as the game's CheckRoll does
        internal bool Chance { get; }
    }
}
