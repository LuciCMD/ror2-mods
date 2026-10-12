// Ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: internal and sealed, namespace, a commented-out line dropped, nullable checks and
// one analyser rule off

#nullable disable
#pragma warning disable CA1805 // LookingGlass's style, kept as ported

using RoR2;
using System;
using System.Collections.Generic;

namespace JustBeBetter.ItemStats
{
    internal sealed class ItemStatsDef
    {
        //Much thanks to https://github.com/ontrigger/ItemStatsMod for being a great guidline for setting item calculations up
        public enum ValueType
        {
            Healing,
            Damage,
            Utility,
            Health,
            Void,
            HumanObjective,
            LunarObjective,
            Stack,
            WorldEvent,
            Artifact,
            UserSetting,
            Death,
            Sub,
            Mono,
            Shrine,
            Event,
            Gold,
            Armor,
            None,
        }
        public enum MeasurementUnits
        {
            Meters,
            Percentage,
            FlatHealth,
            FlatHealing,
            Number,
            Money,
            Seconds,
            PercentHealth,
            PercentHealing,
            ProcCoeff, //0.0##
            PlainString
        }
        public enum ChanceScaling
        {
            Linear,
            Hyperbolic,
            DoesNotScale,
            RunicLens, //Linear, capped at 75%
            Health //BetterUI had LeechSeed/Scythe here so idk?
        }
        public List<string> descriptions = new List<string>();
        public List<ValueType> valueTypes = new List<ValueType>();
        public List<MeasurementUnits> measurementUnits = new List<MeasurementUnits>();

        //Passing along values is work, less work surely more optimized, i hope
        public Func<CharacterBody, int, List<float>> calculateValuesBody = null; //If Needs Body
        public Func<CharacterMaster, int, List<float>> calculateValues = null;
        public Func<int, List<float>> calculateValuesFlat = null;               //If flat and uninfluenced
        public Func<float, int, float, List<float>> calculateValuesNew = null;   //If Luck or Proc affected

        public bool isScrap = false;
        public bool hasChance = false;
        public ChanceScaling chanceScaling = ChanceScaling.Linear;

        public ItemStatsDef(List<string> descriptions, List<ValueType> valueTypes, List<MeasurementUnits> measurementUnits, Func<CharacterBody, int, List<float>> calculateValues)
        {
            this.descriptions = descriptions;
            this.valueTypes = valueTypes;
            this.measurementUnits = measurementUnits;
            this.calculateValuesBody = calculateValues;
        }
        public ItemStatsDef(List<string> descriptions, List<ValueType> valueTypes, List<MeasurementUnits> measurementUnits, Func<CharacterMaster, int, List<float>> calculateValues)
        {
            this.descriptions = descriptions;
            this.valueTypes = valueTypes;
            this.measurementUnits = measurementUnits;
            this.calculateValues = calculateValues;
        }
        public ItemStatsDef(List<string> descriptions, List<ValueType> valueTypes, List<MeasurementUnits> measurementUnits, Func<float, int, float, List<float>> calculateValues)
        {
            this.descriptions = descriptions;
            this.valueTypes = valueTypes;
            this.measurementUnits = measurementUnits;
            this.calculateValuesNew = calculateValues;
        }
        public ItemStatsDef() { }
    }
}
