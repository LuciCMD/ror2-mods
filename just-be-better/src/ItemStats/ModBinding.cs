using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoR2;

namespace JustBeBetter.ItemStats
{
    // Ties our entries for a mod's items to the mod as it's loaded: each item class's singleton gives its ItemDef,
    // and its static ConfigOption fields give the values
    // A mod that isn't installed binds nothing, and one that changed binds what still matches
    internal sealed class ModBinding
    {
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private readonly string assemblyName;
        private readonly IEnumerable<ModEntry> entries;
        private Dictionary<ItemIndex, ItemStatsDef>? defs;

        internal ModBinding(string assemblyName, IEnumerable<ModEntry> entries)
        {
            this.assemblyName = assemblyName;
            this.entries = entries;
        }

        // Problems found while binding, for the log, empty when all went well
        internal List<string> Problems { get; } = new List<string>();

        // Bound once the mod is loaded, and tried again on later hovers until then
        internal ItemStatsDef? For(ItemDef item)
        {
            defs ??= Bind();
            return defs != null && defs.TryGetValue(item.itemIndex, out ItemStatsDef def) ? def : null;
        }

        private Dictionary<ItemIndex, ItemStatsDef>? Bind()
        {
            var bound = new Dictionary<ItemIndex, ItemStatsDef>();
            Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
            if (assembly == null)
            {
                return null;
            }

            // Each entry on its own, so one that throws, a class that failed to start say, leaves the rest bound
            foreach (ModEntry entry in entries)
            {
                try
                {
                    BindEntry(assembly, entry, bound);
                }
                catch (Exception e)
                {
                    Problems.Add($"{entry.TypeName}: {e.GetBaseException().Message}");
                }
            }

            return bound;
        }

        private void BindEntry(Assembly assembly, ModEntry entry, Dictionary<ItemIndex, ItemStatsDef> bound)
        {
            Type? type = assembly.GetType(entry.TypeName, false);
            if (type == null)
            {
                Problems.Add($"{entry.TypeName} isn't in the mod any more");
                return;
            }

            // An item switched off in the mod's own config is never made, and can't be hovered either, so it's no problem
            object? instance = Instance(type);
            ItemDef? item = instance?.GetType().GetField("ItemDef", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as ItemDef;
            if (instance == null || item == null || item.itemIndex == ItemIndex.None)
            {
                return;
            }

            // Every value worked out now, so a renamed field or a config that gives no number shows here
            // rather than as an empty line on a hover
            Func<string, float> config = name => Config(type, instance, name);
            ItemStatsDef def = entry.ToDef(config);
            if (entry.FirstGap(def, CheckedStacks) is (string label, int count))
            {
                Problems.Add($"{entry.TypeName}: {label} has no number at {count} stacks with this config");
                return;
            }

            bound[item.itemIndex] = def;
        }

        private const int CheckedStacks = 20;

        // ItemBase<T>.instance, the item's one object
        private static object? Instance(Type type)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                object? item = t.GetProperty("instance", Static)?.GetValue(null);
                if (item != null)
                {
                    return item;
                }
            }

            return null;
        }

        // Most items keep their config static, a few on the item's object, Zenith Accelerator for one
        // A ConfigOption<T> converts to its value implicitly, so its op_Implicit gives it
        private static float Config(Type type, object instance, string name)
        {
            FieldInfo field = type.GetField(name, Static)
                ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(type.Name, name);
            object? option = (field.IsStatic ? field.GetValue(null) : field.GetValue(instance))
                ?? throw new InvalidOperationException($"{type.Name}.{name} isn't set yet");
            // Its base's operator too, should a mod hold a subclass of its option type
            MethodInfo? read = option.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .FirstOrDefault(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.IsAssignableFrom(option.GetType()) && m.ReturnType != typeof(void));
            object? value = read != null ? read.Invoke(null, new[] { option }) : option;
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
