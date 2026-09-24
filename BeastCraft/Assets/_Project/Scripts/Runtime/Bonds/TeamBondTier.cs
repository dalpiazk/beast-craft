using System;
using System.Collections.Generic;
using BeastCraft.Battle;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// One tier of a <see cref="TeamBondSO"/>: the count its condition must reach, and the effects
    /// the bond applies at that tier. Tiers replace one another rather than stack: a bond at tier 2
    /// applies tier 2's <see cref="Effects"/> only, so each tier is authored as its full effect list.
    /// </summary>
    [Serializable]
    public class TeamBondTier
    {
        /// <summary>The smallest condition count (see <see cref="TeamBondCondition"/>) that reaches this tier.</summary>
        public int MinCount = 2;

        /// <summary>
        /// Applied once at battle start to every recipient (see <see cref="TeamBondScope"/>), with
        /// the recipient as its own caster, in authored order. Ordinary <see cref="SkillEffect"/>s
        /// through <see cref="SkillEffectApplier"/>; a stat change with <c>DurationTurns</c> 0 lasts
        /// the whole battle.
        /// </summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();
    }
}
