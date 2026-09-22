using System;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One effect applied by a skill. A skill may carry several (e.g. damage plus a speed debuff).
    /// </summary>
    [Serializable]
    public class SkillEffect
    {
        public SkillEffectType EffectType = SkillEffectType.Damage;

        /// <summary>
        /// The stat affected. Only meaningful for <see cref="SkillEffectType.BuffStat"/> and
        /// <see cref="SkillEffectType.DebuffStat"/>; ignored for the other effect types.
        /// </summary>
        public StatType AffectedStat = StatType.Attack;

        /// <summary>
        /// Effect strength. Interpretation depends on <see cref="EffectType"/> (damage/heal amount,
        /// buff magnitude, status potency) and is finalized with the battle-system design.
        /// </summary>
        public float Magnitude;

        /// <summary>
        /// 0 means instant / one-time (damage, heal). Greater than 0 means the effect persists for
        /// that many turns (buffs, debuffs, statuses).
        /// </summary>
        public int DurationTurns;
    }
}
