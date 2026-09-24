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
    /// This records that the skill <em>fired</em>; applying <see cref="SkillSO.Effects"/> is
    /// <see cref="SkillEffectApplier"/>'s job, and spending <see cref="SkillSO.ResourceCost"/> and
    /// animating any of it are later passes. The one thing the applier writes back is
    /// <see cref="Hits"/>, the damage rolls it made, so a crit can be reported. An activation whose
    /// <see cref="Targets"/> is empty is a legal whiff (the skill still fired and still reset its
    /// cooldown), not an error.
    /// </para>
    /// </summary>
    public class SkillActivation
    {
        /// <summary>
        /// An activation of <paramref name="skill"/> at level 1, tier 0 — the authored skill
        /// exactly. <see cref="Instance"/> is <c>null</c> when <paramref name="skill"/> is.
        /// </summary>
        public SkillActivation(SkillSO skill, IReadOnlyList<BattleUnit> targets)
            : this(skill == null ? null : new SkillInstance(skill), targets)
        {
        }

        /// <summary>
        /// An activation of a leveled skill: <see cref="SkillEffectApplier"/> applies
        /// <paramref name="instance"/>'s <see cref="SkillInstance.Effects"/> with its magnitudes
        /// scaled by its level. A null instance is tolerated (no skill, nothing applied).
        /// </summary>
        public SkillActivation(SkillInstance instance, IReadOnlyList<BattleUnit> targets)
        {
            Instance = instance;
            Skill = instance == null ? null : instance.Skill;
            Targets = targets ?? new List<BattleUnit>();
        }

        /// <summary>The skill that came up ready and fired.</summary>
        public SkillSO Skill { get; }

        /// <summary>
        /// The fired skill at the level and tier it fired at. <c>null</c> only when
        /// <see cref="Skill"/> is.
        /// </summary>
        public SkillInstance Instance { get; }

        /// <summary>
        /// The units the skill landed on, exactly as <see cref="SkillTargetResolver.ResolveTargets"/>
        /// returned them (ordinal by <see cref="BattleUnit.Id"/>). Never <c>null</c>; empty when the
        /// skill found nothing to hit.
        /// </summary>
        public IReadOnlyList<BattleUnit> Targets { get; }

        /// <summary>
        /// Every damage effect that landed, in the order it landed (target-major, then authored
        /// effect order; see <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit, System.Random)"/>).
        /// Never <c>null</c>; empty until the activation is applied, and for a skill with no damage
        /// effects or no targets. Heals and stat changes are not recorded.
        /// </summary>
        public IReadOnlyList<DamageHit> Hits
        {
            get { return (IReadOnlyList<DamageHit>)_hits ?? NoHits; }
        }

        private static readonly DamageHit[] NoHits = new DamageHit[0];

        // Allocated on the first hit, so the many activations that deal no damage stay cheap.
        private List<DamageHit> _hits;

        /// <summary>Appends one landed damage effect. Called by <see cref="SkillEffectApplier"/> only.</summary>
        internal void RecordHit(DamageHit hit)
        {
            if (_hits == null)
            {
                _hits = new List<DamageHit>();
            }

            _hits.Add(hit);
        }
    }
}
