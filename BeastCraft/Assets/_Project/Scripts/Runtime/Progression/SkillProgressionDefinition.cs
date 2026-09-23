using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The authored half of skill progression: how far a skill can level, how much stronger each
    /// level makes it, and the breakthrough gates on the way. The live half — where one owner's
    /// copy of the skill has got to — is <see cref="SkillProgress"/>, and the rules joining the two
    /// are <see cref="SkillProgression"/>.
    /// <para>
    /// <strong>Generic on purpose.</strong> It is a plain serializable block rather than fields
    /// spread over <see cref="Battle.SkillSO"/>, so any skill-definition type can embed one and
    /// reuse <see cref="SkillProgression"/> unchanged: beast skills do today
    /// (<see cref="Battle.SkillSO.Progression"/>), and avatar passive skills are expected to.
    /// </para>
    /// <para>
    /// <strong>Tunable starting defaults, not confirmed balance.</strong> Max level 20, 3% magnitude
    /// per level above 1 (so 1.57x at level 20), and gates at levels 5, 10 and 15 needing material
    /// tiers 1, 2 and 3. See the battle-system design doc, "Skill progression".
    /// </para>
    /// </summary>
    [Serializable]
    public class SkillProgressionDefinition
    {
        /// <summary>Default <see cref="MaxLevel"/>.</summary>
        public const int DefaultMaxLevel = 20;

        /// <summary>Default <see cref="MagnitudeGrowthPerLevel"/>, in percent.</summary>
        public const float DefaultMagnitudeGrowthPerLevel = 3f;

        /// <summary>
        /// The highest level the skill can reach. Read as at least 1; a skill authored with a max
        /// level of 1 never levels.
        /// </summary>
        public int MaxLevel = DefaultMaxLevel;

        /// <summary>
        /// Percent added to every effect magnitude per level above 1, linearly:
        /// <c>multiplier = 1 + MagnitudeGrowthPerLevel / 100 * (level - 1)</c>. Applies to every
        /// effect type (damage power, heal amount, buff and debuff size), never to durations. A
        /// negative value is read as 0: leveling never weakens a skill.
        /// </summary>
        public float MagnitudeGrowthPerLevel = DefaultMagnitudeGrowthPerLevel;

        /// <summary>
        /// The breakthrough gates, in ascending <see cref="SkillTierDefinition.ThresholdLevel"/>
        /// order. Empty means the skill levels straight to <see cref="MaxLevel"/> on XP alone.
        /// </summary>
        public List<SkillTierDefinition> Tiers = new List<SkillTierDefinition>
        {
            new SkillTierDefinition { ThresholdLevel = 5, RequiredMaterialTier = 1 },
            new SkillTierDefinition { ThresholdLevel = 10, RequiredMaterialTier = 2 },
            new SkillTierDefinition { ThresholdLevel = 15, RequiredMaterialTier = 3 },
        };

        /// <summary>
        /// What a missing definition reads as: the defaults above. Shared and read-only by
        /// convention — nothing in this namespace writes to it.
        /// </summary>
        internal static readonly SkillProgressionDefinition Fallback = new SkillProgressionDefinition();

        /// <summary><see cref="MaxLevel"/>, read as at least 1.</summary>
        public int EffectiveMaxLevel
        {
            get { return MaxLevel < 1 ? 1 : MaxLevel; }
        }

        /// <summary>How many breakthrough gates are authored. 0 for a null list.</summary>
        public int TierCount
        {
            get { return Tiers == null ? 0 : Tiers.Count; }
        }

        /// <summary>
        /// The gate at <paramref name="index"/> (0 is the first one to pass), or <c>null</c> when
        /// out of range or when the authored entry is itself null.
        /// </summary>
        public SkillTierDefinition GetTier(int index)
        {
            return Tiers != null && index >= 0 && index < Tiers.Count ? Tiers[index] : null;
        }

        /// <summary>
        /// <paramref name="level"/> clamped into [1, <see cref="EffectiveMaxLevel"/>].
        /// </summary>
        public int ClampLevel(int level)
        {
            if (level < 1)
            {
                return 1;
            }

            int max = EffectiveMaxLevel;
            return level > max ? max : level;
        }

        /// <summary><paramref name="tier"/> clamped into [0, <see cref="TierCount"/>].</summary>
        public int ClampTier(int tier)
        {
            if (tier < 0)
            {
                return 0;
            }

            int count = TierCount;
            return tier > count ? count : tier;
        }

        /// <summary>
        /// The factor every effect magnitude is multiplied by at <paramref name="level"/>:
        /// <c>1 + max(0, MagnitudeGrowthPerLevel) / 100 * (level - 1)</c>, with the level clamped
        /// first. Exactly 1 at level 1. Computed in double so a round percentage gives an exact
        /// multiplier (3% at level 11 is exactly 1.3).
        /// </summary>
        public double GetMagnitudeMultiplier(int level)
        {
            int clamped = ClampLevel(level);
            double growth = MagnitudeGrowthPerLevel < 0f ? 0.0 : MagnitudeGrowthPerLevel;

            return 1.0 + (growth * (clamped - 1) / 100.0);
        }
    }
}
