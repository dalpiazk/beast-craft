using System;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// A single stat adjustment contributed by a piece of gear.
    /// </summary>
    [Serializable]
    public class StatModifier
    {
        /// <summary>Which stat axis this modifier touches.</summary>
        public StatType Stat = StatType.Attack;

        /// <summary>Flat addition applied to the stat.</summary>
        public int FlatBonus;

        /// <summary>Proportional bonus, where 0.1 means +10%.</summary>
        public float PercentBonus;
    }
}
