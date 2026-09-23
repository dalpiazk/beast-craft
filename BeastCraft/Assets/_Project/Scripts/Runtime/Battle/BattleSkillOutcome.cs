namespace BeastCraft.Battle
{
    /// <summary>
    /// What one ready skill slot did — or could not do — on a unit's turn: which slot, which skill,
    /// how it ended, what it landed on if it fired, and what the attempt cost in movement.
    /// <para>
    /// A named type rather than a tuple, matching how the rest of this namespace models small
    /// records (<see cref="SkillActivation"/>, <see cref="SkillEffect"/>, <see cref="StatModifier"/>).
    /// </para>
    /// <para>
    /// <strong>Why not just <see cref="SkillActivation"/>.</strong> That type records a skill that
    /// <em>fired</em> and who it hit, and it has no way to say "this came up ready and did not go
    /// off", which is the whole point of the confirmed movement rule. It also carries no slot index,
    /// and the slot is what a caller has to name to reason about the cooldown that stayed at 0 —
    /// the same skill may be equipped twice. So this wraps an activation rather than replacing it:
    /// when the skill fired, <see cref="Activation"/> is exactly what the resolver produced and what
    /// <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit, System.Random)"/> was handed.
    /// </para>
    /// <para>
    /// A record of what happened, not a handle to change it. Everything here is already done by the
    /// time the outcome exists.
    /// </para>
    /// </summary>
    public class BattleSkillOutcome
    {
        public BattleSkillOutcome(int slotIndex, SkillSO skill, BattleSkillStatus status, SkillActivation activation, int movementSpent)
        {
            SlotIndex = slotIndex;
            Skill = skill;
            Status = status;
            Activation = activation;
            MovementSpent = movementSpent;
        }

        /// <summary>
        /// Which slot of the caster's <see cref="SkillLoadout"/> this was, so a caller can ask that
        /// loadout what its cooldown now reads. A slot that did not fire is still sitting at 0.
        /// </summary>
        public int SlotIndex { get; }

        /// <summary>The skill equipped in that slot. Never <c>null</c> for an outcome that exists.</summary>
        public SkillSO Skill { get; }

        /// <summary>How the attempt ended, and therefore whether the slot was re-armed.</summary>
        public BattleSkillStatus Status { get; }

        /// <summary>
        /// The fired skill and the units it landed on, or <c>null</c> when
        /// <see cref="Status"/> is not <see cref="BattleSkillStatus.Fired"/>. An activation with an
        /// empty target list is a whiff, not a failure to fire.
        /// </summary>
        public SkillActivation Activation { get; }

        /// <summary>
        /// Hex steps this one attempt took out of the turn's shared movement budget. 0 whenever the
        /// caster was already in range, whenever the shape needed no approach, and for
        /// <see cref="BattleSkillStatus.NoCandidate"/> and <see cref="BattleSkillStatus.Unreachable"/>.
        /// For <see cref="BattleSkillStatus.OutOfMovement"/> it is the partial approach: every step
        /// the turn had left, walked toward a target the skill still could not reach — the one case
        /// in which a slot that did not fire spent movement.
        /// </summary>
        public int MovementSpent { get; }

        /// <summary>Shorthand for <c>Status == BattleSkillStatus.Fired</c>.</summary>
        public bool Fired
        {
            get { return Status == BattleSkillStatus.Fired; }
        }
    }
}
