using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using JustBeBetter.ItemStats;
using JustBeBetter.Shared;
using RoR2;
using UnityEngine;

namespace JustBeBetter.SkillInfo
{
    // The lines added under a skill's description: cooldown, proc coefficient, LookingGlass's notes and the
    // chance each held proc item has with it
    internal static class SkillText
    {
        private static readonly HashSet<int> brokenProcs = new HashSet<int>();

        internal static ManualLogSource? Log { get; set; }

        // currentCooldown is null outside a run, where the base value is all there is
        // An unknown skill gets no proc line and no item procs, since a guessed coefficient would mislead
        internal static string Extra(string nameToken, float baseCooldown, float? currentCooldown, CharacterMaster? master, Lines lines)
        {
            var text = new StringBuilder();
            if (lines.Cooldown && baseCooldown > 0f)
            {
                CooldownText.Append(text, baseCooldown, currentCooldown ?? baseCooldown);
            }

            // -1 marks a skill that does no damage, such as a dash, so it shows nothing
            bool known = nameToken != null && ProcCoefficientData.hasProcCoefficient(nameToken);
            float coefficient = known ? ProcCoefficientData.GetProcCoefficient(nameToken!) : 0f;
            bool showsProc = lines.ProcCoefficient && known && coefficient >= 0f;
            if (showsProc)
            {
                text.Append("\nProc Coefficient: <style=cIsDamage>").Append(coefficient.ToString("0.0##", CultureInfo.InvariantCulture)).Append("</style>");
            }

            // Notes that don't start a line carry on the proc line, a second coefficient, so they need it shown
            if (lines.ExtraNotes && known && ProcCoefficientData.hasExtra(nameToken!))
            {
                string extra = ProcCoefficientData.GetExtraInfo(nameToken!);
                if ((extra.Length > 0 && extra[0] == '\n') || showsProc)
                {
                    text.Append(extra);
                }
            }

            if (lines.ItemProcs && known && coefficient > 0f && master != null && master.inventory)
            {
                AppendItemProcs(text, master, coefficient);
            }

            return text.Length > 0 ? "\n" + text : string.Empty;
        }

        // In the stat table's order, which LookingGlass sorts by tier
        private static void AppendItemProcs(StringBuilder text, CharacterMaster master, float coefficient)
        {
            var procs = new StringBuilder();
            foreach (KeyValuePair<int, ItemStatsDef> pair in ItemDefinitions.allItemDefinitions)
            {
                ItemStatsDef def = pair.Value;
                if (!def.hasChance || def.calculateValuesNew == null || brokenProcs.Contains(pair.Key))
                {
                    continue;
                }

                int count = master.inventory.GetItemCountEffective((ItemIndex)pair.Key);
                ItemDef item = ItemCatalog.GetItemDef((ItemIndex)pair.Key);
                if (count <= 0 || item == null)
                {
                    continue;
                }

                try
                {
                    AppendProc(procs, item, def, count, master.luck, coefficient);
                }
                catch (Exception e)
                {
                    brokenProcs.Add(pair.Key);
                    Log?.LogWarning($"Couldn't work out {item.name}'s chance for a skill, it's left out: {e}");
                }
            }

            if (procs.Length > 0)
            {
                text.Append("\n\n<style=cStack>Your item procs:</style>").Append(procs);
            }
        }

        private static void AppendProc(StringBuilder procs, ItemDef item, ItemStatsDef def, int count, float luck, float coefficient)
        {
            List<float> values = def.calculateValuesNew(luck, count, coefficient);
            if (values == null || values.Count == 0 || float.IsNaN(values[0]) || float.IsInfinity(values[0]))
            {
                return;
            }

            procs.Append('\n').Append(Language.GetString(item.nameToken)).Append(": ");
            if (def.chanceScaling == ItemStatsDef.ChanceScaling.Health)
            {
                procs.Append("<style=cIsHealing>").Append(values[0].ToString("0.#", CultureInfo.InvariantCulture)).Append(" HP</style>");
                return;
            }

            procs.Append("<style=cIsDamage>").Append((values[0] * 100f).ToString("0.#", CultureInfo.InvariantCulture)).Append("%</style>");

            // Stacks for a sure proc, as LookingGlass counts them, Runic Lens tops out at 75%
            float cap = def.chanceScaling == ItemStatsDef.ChanceScaling.Linear ? 1f
                : def.chanceScaling == ItemStatsDef.ChanceScaling.RunicLens ? 0.75f
                : 0f;
            if (cap > 0f)
            {
                List<float> one = def.calculateValuesNew(0f, 1, coefficient);
                if (one != null && one.Count > 0 && one[0] > 0f)
                {
                    procs.Append(" <style=cStack>(").Append(Mathf.CeilToInt(cap / one[0]).ToString(CultureInfo.InvariantCulture)).Append(" to cap)</style>");
                }
            }
        }

        internal readonly struct Lines
        {
            internal Lines(bool cooldown, bool procCoefficient, bool extraNotes, bool itemProcs)
            {
                Cooldown = cooldown;
                ProcCoefficient = procCoefficient;
                ExtraNotes = extraNotes;
                ItemProcs = itemProcs;
            }

            internal bool Cooldown { get; }

            internal bool ProcCoefficient { get; }

            internal bool ExtraNotes { get; }

            internal bool ItemProcs { get; }
        }
    }
}
