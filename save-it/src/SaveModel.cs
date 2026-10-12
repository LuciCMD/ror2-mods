using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace SaveIt
{
    internal enum SaveStatus
    {
        Active,
        Died,
        Won,
    }

    // Catalog entries are kept by name, never by index, so a save survives mod-list changes
    internal sealed class RunSave
    {
        internal const int FormatVersion = 1;

        internal string Id = Guid.NewGuid().ToString("N");
        internal string Name = string.Empty;
        internal SaveStatus Status = SaveStatus.Active;
        internal DateTime SavedUtc = DateTime.UtcNow;
        internal string GameMode = string.Empty;
        internal string Scene = string.Empty;
        internal ulong Seed;
        internal XElement? RuleBook;
        internal int StageClearCount;
        internal int LoopClearCount;
        internal int StageClearCountAtLoopStart;
        internal float Stopwatch;
        internal int ShopPortalCount;
        internal int PrestigeValue;
        internal int? Karma;
        internal List<KarmaTriggerSave> KarmaTriggers = new List<KarmaTriggerSave>();
        internal ulong TeamExperience;
        internal List<RngSave> Rngs = new List<RngSave>();
        internal List<string> EventFlags = new List<string>();
        internal List<KeyValuePair<string, string>> SuppressedItems = new List<KeyValuePair<string, string>>();
        internal SimulacrumSave? Simulacrum;
        internal List<PlayerSave> Players = new List<PlayerSave>();

        internal int StageNumber => StageClearCount + 1;
    }

    internal sealed class RngSave
    {
        internal string Name = string.Empty;
        internal ulong State0;
        internal ulong State1;
    }

    internal sealed class KarmaTriggerSave
    {
        internal string Identifier = string.Empty;
        internal float Progress;
        internal int TimesFlipped;
        internal bool IsComplete;
    }

    internal sealed class PlayerSave
    {
        internal ulong IdValue;
        internal string IdString = string.Empty;
        internal byte IdSubId;
        internal string DisplayName = string.Empty;
        internal string Body = string.Empty;
        internal XElement? Loadout;
        internal uint Money;
        internal uint VoidCoins;
        internal InventorySave Inventory = new InventorySave();
        internal List<MinionSave> Minions = new List<MinionSave>();

        // Artifact of Devotion's inventory shared by all of this player's lemurians
        internal InventorySave? DevotionInventory;
    }

    internal sealed class InventorySave
    {
        internal Dictionary<string, int> Permanent = new Dictionary<string, int>();
        internal Dictionary<string, int> Rented = new Dictionary<string, int>();

        // Raw value in stacks: whole part is the stack count, fraction is what's left of the top stack
        internal Dictionary<string, float> Temporary = new Dictionary<string, float>();

        internal List<EquipmentSave> Equipment = new List<EquipmentSave>();
        internal uint ActiveSlot;
        internal uint ActiveSet;
        internal string KeyItem = string.Empty;
        internal uint InfusionBonus;
        internal List<string> AcquisitionOrder = new List<string>();
    }

    internal sealed class EquipmentSave
    {
        internal uint Slot;
        internal uint Set;
        internal string Name = string.Empty;
        internal byte Charges;

        // Seconds left, since the run clock restarts at 0 on a new run
        internal float CooldownLeft;
    }

    internal sealed class MinionSave
    {
        internal string Master = string.Empty;

        // Set for drones a survivor's skill keeps alive (Operator); the game rebuilds those itself
        internal string SkillSlot = string.Empty;

        // Set for Artifact of Devotion lemurians: the item they were given and how often they've evolved
        internal string DevotionItem = string.Empty;
        internal int DevotionLevel;

        // Evolved lemurians change body (DevotedLemurianBruiserBody)
        internal string Body = string.Empty;
        internal InventorySave Inventory = new InventorySave();
    }

    internal sealed class SimulacrumSave
    {
        internal int WaveIndex;
        internal int EnemyItemPatternIndex;
        internal List<RngSave> Rngs = new List<RngSave>();
        internal InventorySave EnemyInventory = new InventorySave();
    }

    internal static class SaveXml
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly char[] Space = { ' ' };

        internal static XElement ToXml(RunSave save)
        {
            var root = new XElement(
                "SaveItRun",
                new XAttribute("version", RunSave.FormatVersion),
                new XAttribute("id", save.Id),
                new XAttribute("name", save.Name),
                new XAttribute("status", save.Status),
                new XAttribute("savedUtc", save.SavedUtc.ToString("o", Invariant)));

            var run = new XElement(
                "Run",
                new XAttribute("gameMode", save.GameMode),
                new XAttribute("scene", save.Scene),
                new XAttribute("seed", save.Seed.ToString(Invariant)),
                new XAttribute("stageClearCount", save.StageClearCount),
                new XAttribute("loopClearCount", save.LoopClearCount),
                new XAttribute("stageClearCountAtLoopStart", save.StageClearCountAtLoopStart),
                new XAttribute("stopwatch", save.Stopwatch.ToString("R", Invariant)),
                new XAttribute("shopPortalCount", save.ShopPortalCount),
                new XAttribute("prestigeValue", save.PrestigeValue),
                new XAttribute("teamExperience", save.TeamExperience.ToString(Invariant)));
            if (save.Karma.HasValue)
            {
                run.Add(new XAttribute("karma", save.Karma.Value));
            }

            if (save.RuleBook != null)
            {
                run.Add(new XElement("RuleBook", save.RuleBook));
            }

            run.Add(save.KarmaTriggers.Select(t => new XElement(
                "KarmaTrigger",
                new XAttribute("id", t.Identifier),
                new XAttribute("progress", t.Progress.ToString("R", Invariant)),
                new XAttribute("timesFlipped", t.TimesFlipped),
                new XAttribute("complete", t.IsComplete))));
            run.Add(save.Rngs.Select(RngToXml));
            run.Add(save.EventFlags.Select(flag => new XElement("EventFlag", flag)));
            run.Add(save.SuppressedItems.Select(pair =>
                new XElement("Suppressed", new XAttribute("item", pair.Key), new XAttribute("into", pair.Value))));
            if (save.Simulacrum != null)
            {
                run.Add(SimulacrumToXml(save.Simulacrum));
            }

            root.Add(run);
            root.Add(save.Players.Select(PlayerToXml));
            return root;
        }

        internal static RunSave FromXml(XElement root)
        {
            var save = new RunSave
            {
                Id = Str(root, "id", Guid.NewGuid().ToString("N")),
                Name = Str(root, "name", "Unnamed run"),
                Status = Enum.TryParse(Str(root, "status", "Active"), out SaveStatus status) ? status : SaveStatus.Active,
                SavedUtc = DateTime.TryParse(Str(root, "savedUtc", string.Empty), Invariant, DateTimeStyles.RoundtripKind, out DateTime saved)
                    ? saved
                    : DateTime.MinValue,
            };

            XElement? run = root.Element("Run");
            if (run == null)
            {
                return save;
            }

            save.GameMode = Str(run, "gameMode", "ClassicRun");
            save.Scene = Str(run, "scene", string.Empty);
            save.Seed = ULong(run, "seed");
            save.StageClearCount = Int(run, "stageClearCount");
            save.LoopClearCount = Int(run, "loopClearCount");
            save.StageClearCountAtLoopStart = Int(run, "stageClearCountAtLoopStart");
            save.Stopwatch = Float(run, "stopwatch");
            save.ShopPortalCount = Int(run, "shopPortalCount");
            save.PrestigeValue = Int(run, "prestigeValue");
            save.TeamExperience = ULong(run, "teamExperience");
            save.Karma = run.Attribute("karma") != null ? Int(run, "karma") : (int?)null;
            save.RuleBook = run.Element("RuleBook")?.Elements().FirstOrDefault();
            save.KarmaTriggers = run.Elements("KarmaTrigger")
                .Select(e => new KarmaTriggerSave
                {
                    Identifier = Str(e, "id", string.Empty),
                    Progress = Float(e, "progress"),
                    TimesFlipped = Int(e, "timesFlipped"),
                    IsComplete = Str(e, "complete", "false") == "true",
                })
                .ToList();
            save.Rngs = run.Elements("Rng").Select(RngFromXml).ToList();
            save.EventFlags = run.Elements("EventFlag").Select(e => e.Value).ToList();
            save.SuppressedItems = run.Elements("Suppressed")
                .Select(e => new KeyValuePair<string, string>(Str(e, "item", string.Empty), Str(e, "into", string.Empty)))
                .ToList();
            XElement? simulacrum = run.Element("Simulacrum");
            save.Simulacrum = simulacrum != null ? SimulacrumFromXml(simulacrum) : null;
            save.Players = root.Elements("Player").Select(PlayerFromXml).ToList();
            return save;
        }

        private static XElement RngToXml(RngSave rng)
        {
            return new XElement(
                "Rng",
                new XAttribute("name", rng.Name),
                new XAttribute("s0", rng.State0.ToString(Invariant)),
                new XAttribute("s1", rng.State1.ToString(Invariant)));
        }

        private static RngSave RngFromXml(XElement e)
        {
            return new RngSave { Name = Str(e, "name", string.Empty), State0 = ULong(e, "s0"), State1 = ULong(e, "s1") };
        }

        private static XElement SimulacrumToXml(SimulacrumSave s)
        {
            var e = new XElement(
                "Simulacrum",
                new XAttribute("waveIndex", s.WaveIndex),
                new XAttribute("enemyItemPatternIndex", s.EnemyItemPatternIndex));
            e.Add(s.Rngs.Select(RngToXml));
            e.Add(InventoryToXml("EnemyInventory", s.EnemyInventory));
            return e;
        }

        private static SimulacrumSave SimulacrumFromXml(XElement e)
        {
            return new SimulacrumSave
            {
                WaveIndex = Int(e, "waveIndex"),
                EnemyItemPatternIndex = Int(e, "enemyItemPatternIndex"),
                Rngs = e.Elements("Rng").Select(RngFromXml).ToList(),
                EnemyInventory = InventoryFromXml(e.Element("EnemyInventory")),
            };
        }

        private static XElement PlayerToXml(PlayerSave p)
        {
            var e = new XElement(
                "Player",
                new XAttribute("idValue", p.IdValue.ToString(Invariant)),
                new XAttribute("idString", p.IdString),
                new XAttribute("idSubId", p.IdSubId),
                new XAttribute("name", p.DisplayName),
                new XAttribute("body", p.Body),
                new XAttribute("money", p.Money),
                new XAttribute("voidCoins", p.VoidCoins));
            if (p.Loadout != null)
            {
                e.Add(new XElement("Loadout", p.Loadout));
            }

            e.Add(InventoryToXml("Inventory", p.Inventory));
            if (p.DevotionInventory != null)
            {
                e.Add(InventoryToXml("DevotionInventory", p.DevotionInventory));
            }

            e.Add(p.Minions.Select(MinionToXml));
            return e;
        }

        private static PlayerSave PlayerFromXml(XElement e)
        {
            return new PlayerSave
            {
                IdValue = ULong(e, "idValue"),
                IdString = Str(e, "idString", string.Empty),
                IdSubId = (byte)Int(e, "idSubId"),
                DisplayName = Str(e, "name", "?"),
                Body = Str(e, "body", string.Empty),
                Money = (uint)ULong(e, "money"),
                VoidCoins = (uint)ULong(e, "voidCoins"),
                Loadout = e.Element("Loadout")?.Elements().FirstOrDefault(),
                Inventory = InventoryFromXml(e.Element("Inventory")),
                DevotionInventory = e.Element("DevotionInventory") != null ? InventoryFromXml(e.Element("DevotionInventory")) : null,
                Minions = e.Elements("Minion").Select(MinionFromXml).ToList(),
            };
        }

        private static XElement MinionToXml(MinionSave m)
        {
            var e = new XElement("Minion", new XAttribute("master", m.Master), new XAttribute("skillSlot", m.SkillSlot));
            if (m.DevotionItem.Length > 0)
            {
                e.Add(
                    new XAttribute("devotionItem", m.DevotionItem),
                    new XAttribute("devotionLevel", m.DevotionLevel),
                    new XAttribute("body", m.Body));
            }

            e.Add(InventoryToXml("Inventory", m.Inventory));
            return e;
        }

        private static MinionSave MinionFromXml(XElement m)
        {
            return new MinionSave
            {
                Master = Str(m, "master", string.Empty),
                SkillSlot = Str(m, "skillSlot", string.Empty),
                DevotionItem = Str(m, "devotionItem", string.Empty),
                DevotionLevel = Int(m, "devotionLevel"),
                Body = Str(m, "body", string.Empty),
                Inventory = InventoryFromXml(m.Element("Inventory")),
            };
        }

        private static XElement InventoryToXml(string elementName, InventorySave inv)
        {
            var e = new XElement(
                elementName,
                new XAttribute("activeSlot", inv.ActiveSlot),
                new XAttribute("activeSet", inv.ActiveSet),
                new XAttribute("keyItem", inv.KeyItem),
                new XAttribute("infusionBonus", inv.InfusionBonus));
            e.Add(inv.Permanent.Select(kv => new XElement("Item", new XAttribute("name", kv.Key), new XAttribute("count", kv.Value))));
            e.Add(inv.Rented.Select(kv => new XElement("Rented", new XAttribute("name", kv.Key), new XAttribute("count", kv.Value))));
            e.Add(inv.Temporary.Select(kv =>
                new XElement("Temporary", new XAttribute("name", kv.Key), new XAttribute("raw", kv.Value.ToString("R", Invariant)))));
            e.Add(inv.Equipment.Select(eq => new XElement(
                "Equipment",
                new XAttribute("slot", eq.Slot),
                new XAttribute("set", eq.Set),
                new XAttribute("name", eq.Name),
                new XAttribute("charges", eq.Charges),
                new XAttribute("cooldownLeft", eq.CooldownLeft.ToString("R", Invariant)))));
            e.Add(new XElement("AcquisitionOrder", string.Join(" ", inv.AcquisitionOrder)));
            return e;
        }

        private static InventorySave InventoryFromXml(XElement? e)
        {
            var inv = new InventorySave();
            if (e == null)
            {
                return inv;
            }

            inv.ActiveSlot = (uint)ULong(e, "activeSlot");
            inv.ActiveSet = (uint)ULong(e, "activeSet");
            inv.KeyItem = Str(e, "keyItem", string.Empty);
            inv.InfusionBonus = (uint)ULong(e, "infusionBonus");
            foreach (XElement item in e.Elements("Item"))
            {
                inv.Permanent[Str(item, "name", string.Empty)] = Int(item, "count");
            }

            foreach (XElement item in e.Elements("Rented"))
            {
                inv.Rented[Str(item, "name", string.Empty)] = Int(item, "count");
            }

            foreach (XElement item in e.Elements("Temporary"))
            {
                inv.Temporary[Str(item, "name", string.Empty)] = Float(item, "raw");
            }

            inv.Equipment = e.Elements("Equipment").Select(eq => new EquipmentSave
            {
                Slot = (uint)ULong(eq, "slot"),
                Set = (uint)ULong(eq, "set"),
                Name = Str(eq, "name", string.Empty),
                Charges = (byte)Int(eq, "charges"),
                CooldownLeft = Float(eq, "cooldownLeft"),
            }).ToList();
            string order = e.Element("AcquisitionOrder")?.Value ?? string.Empty;
            inv.AcquisitionOrder = order.Split(Space, StringSplitOptions.RemoveEmptyEntries).ToList();
            return inv;
        }

        private static string Str(XElement e, string name, string fallback)
        {
            return e.Attribute(name)?.Value ?? fallback;
        }

        private static int Int(XElement e, string name)
        {
            return int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, Invariant, out int value) ? value : 0;
        }

        private static ulong ULong(XElement e, string name)
        {
            return ulong.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, Invariant, out ulong value) ? value : 0;
        }

        private static float Float(XElement e, string name)
        {
            return float.TryParse(e.Attribute(name)?.Value, NumberStyles.Float, Invariant, out float value) ? value : 0f;
        }
    }
}
