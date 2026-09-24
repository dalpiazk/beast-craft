using System;
using BeastCraft.Creatures;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The encounter difficulty knob: one multiplier applied to every enemy's level-computed stats.
    /// The same rule the balance simulator calibrates with and a battle session fields enemies with,
    /// so a calibrated multiplier means the same thing in both.
    /// </summary>
    public static class EnemyScaling
    {
        /// <summary>
        /// HP, Attack, Defense, SpecialAttack and SpecialDefense scale by the multiplier (rounded
        /// half away from zero, floored at 1). Speed, MoveRange and CritChance do not: under the ATB
        /// gauge Speed is how many turns a unit gets, so scaling it would make the knob change the
        /// enemies' action economy rather than just their toughness and punch, move range is a small
        /// tactical integer, and crit chance is a probability that the knob should not turn into
        /// certainty.
        /// </summary>
        public static StatBlock Scale(StatBlock stats, double multiplier)
        {
            StatBlock scaled = stats;
            scaled.Hp = ScaleStat(stats.Hp, multiplier);
            scaled.Attack = ScaleStat(stats.Attack, multiplier);
            scaled.Defense = ScaleStat(stats.Defense, multiplier);
            scaled.SpecialAttack = ScaleStat(stats.SpecialAttack, multiplier);
            scaled.SpecialDefense = ScaleStat(stats.SpecialDefense, multiplier);
            return scaled;
        }

        private static int ScaleStat(int value, double multiplier)
        {
            int scaled = (int)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return scaled < 1 ? 1 : scaled;
        }
    }
}
