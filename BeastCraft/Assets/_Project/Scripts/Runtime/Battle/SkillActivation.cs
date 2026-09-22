using System.Collections.Generic;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One skill that fired, paired with the units it landed on.
    /// <para>
    /// The output shape of <see cref="SkillLoadout.TickAndResolve"/>: a rotation can fire more than
    /// one skill on the same turn, so a turn's worth of casting is a list of these rather than a
    /// single skill-plus-targets pair.
    /// </para>
    /// <para>
    /// A named type rather than a tuple, matching how the rest of this namespace models small
    /// records (<see cref="StatModifier"/>, <see cref="SkillEffect"/>): the two halves are read in
    /// different places by different passes, and <c>Skill</c>/<c>Targets</c> survive refactoring
    /// where <c>Item1</c>/<c>Item2</c> would not.
    /// </para>
    /// <para>
    /// This records that the skill <em>fired</em>, not what it did. Applying
    /// <see cref="SkillSO.Effects"/>, spending <see cref="SkillSO.ResourceCost"/> and animating any
    /// of it are all later passes; an activation whose <see cref="Targets"/> is empty is a legal
    /// whiff (the skill still fired and still reset its cooldown), not an error.
    /// </para>
    /// </summary>
    public class SkillActivation
    {
        public SkillActivation(SkillSO skill, IReadOnlyList<BattleUnit> targets)
        {
            Skill = skill;
            Targets = targets ?? new List<BattleUnit>();
        }

        /// <summary>The skill that came up ready and fired.</summary>
        public SkillSO Skill { get; }

        /// <summary>
        /// The units the skill landed on, exactly as <see cref="SkillTargetResolver.ResolveTargets"/>
        /// returned them (ordinal by <see cref="BattleUnit.Id"/>). Never <c>null</c>; empty when the
        /// skill found nothing to hit.
        /// </summary>
        public IReadOnlyList<BattleUnit> Targets { get; }
    }
}
