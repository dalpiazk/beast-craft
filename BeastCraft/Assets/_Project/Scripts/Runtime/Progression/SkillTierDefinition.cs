using System;
using System.Collections.Generic;
using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// One breakthrough gate on a skill's level track: the level at which practice stops until a
    /// material of high enough tier is spent, and what passing the gate grants.
    /// <para>
    /// Authored inside a <see cref="SkillProgressionDefinition"/>, in ascending
    /// <see cref="ThresholdLevel"/> order. A skill whose <see cref="SkillProgress.Tier"/> is
    /// <c>n</c> has passed the first <c>n</c> entries; the next entry's threshold is its level cap
    /// (see <see cref="SkillProgression.LevelCap"/>). See the battle-system design doc, "Skill
    /// progression".
    /// </para>
    /// <para>
    /// The bonuses are cumulative: a skill at tier 2 has the <see cref="CooldownReduction"/> and
    /// <see cref="BonusEffects"/> of both of the first two entries. Both default to nothing, so a
    /// gate that only unlocks further levels is the plain case.
    /// </para>
    /// </summary>
    [Serializable]
    public class SkillTierDefinition
    {
        /// <summary>
        /// The level at which leveling stops until this gate is passed. A skill reaches this level
        /// through XP as usual and then holds there (banking at most one level's worth of XP, see
        /// <see cref="SkillProgression"/>) until <see cref="SkillProgression.TryBreakthrough"/>
        /// succeeds. Clamped into [1, <see cref="SkillProgressionDefinition.MaxLevel"/>] when read,
        /// so a gate authored at or past the max level is a "mastery" gate: it blocks nothing but
        /// still grants its bonuses once passed.
        /// </summary>
        public int ThresholdLevel = 5;

        /// <summary>
        /// The least <see cref="SkillMaterialSO.Tier"/> a material must have to pass this gate. A
        /// higher-tier material also passes it.
        /// </summary>
        public int RequiredMaterialTier = 1;

        /// <summary>
        /// Turns taken off <see cref="SkillSO.Cooldown"/> once this gate is passed. Summed across
        /// every passed gate; the result is clamped at 0 (see
        /// <see cref="SkillInstance.EffectiveCooldown"/>). 0 grants nothing.
        /// </summary>
        public int CooldownReduction;

        /// <summary>
        /// Effects appended after <see cref="SkillSO.Effects"/> once this gate is passed, in gate
        /// order then authored order (see <see cref="SkillInstance.Effects"/>). They are scaled by
        /// the skill's level exactly like the base effects. Empty grants nothing.
        /// </summary>
        public List<SkillEffect> BonusEffects = new List<SkillEffect>();
    }
}
