namespace BeastCraft.Battle
{
    /// <summary>
    /// What became of one skill slot that <see cref="SkillLoadout.Tick"/> offered on a unit's turn.
    /// <para>
    /// Exactly one of these is recorded per ready slot, in <see cref="BattleSkillOutcome"/>. Only
    /// <see cref="Fired"/> re-arms the slot; under every other value the slot keeps its 0 and is
    /// offered again on the unit's next turn, which is the producer-confirmed rule for a skill that
    /// could not reach anything.
    /// </para>
    /// <para>
    /// The three non-firing values are split rather than collapsed into one "did not fire" because
    /// they are genuinely different situations to read in a log or a test — nothing to shoot at at
    /// all, something to shoot at but no route to it, and a route that this turn could not afford —
    /// and because the first of them never even consults the movement budget.
    /// </para>
    /// </summary>
    public enum BattleSkillStatus
    {
        /// <summary>
        /// The skill resolved and its effects were applied, and the slot's cooldown was re-armed.
        /// A skill that fired and hit nobody is still <c>Fired</c>: a whiff is a legal outcome of
        /// firing, per <see cref="SkillActivation"/>.
        /// </summary>
        Fired = 0,

        /// <summary>
        /// A picking shape (<see cref="SkillTargetShape.SingleTarget"/> or
        /// <see cref="SkillTargetShape.Line"/>) found no eligible unit anywhere on the board — the
        /// whole side it may hit is defeated, or was never there. Nothing to chase, so no movement
        /// was attempted and none was spent.
        /// </summary>
        NoCandidate = 1,

        /// <summary>
        /// A candidate exists, but no route reaches a tile within the skill's range of it: the
        /// approach is walled off by terrain or bodies, or the grid is missing. The distance the
        /// unit could have afforded never came into it. No movement was spent.
        /// </summary>
        Unreachable = 2,

        /// <summary>
        /// A candidate exists and a route to within range of it exists, but it is longer than the
        /// movement this unit has left this turn — either because an earlier skill in the same turn
        /// already spent the budget, or because the unit's <see cref="BattleUnit.MoveRange"/> was
        /// never enough on its own. The unit stays put: it does not walk part of the way, because a
        /// partial approach spends the rest of the turn's budget to accomplish nothing and leaves
        /// the later skills in the stack worse off than standing still.
        /// </summary>
        OutOfMovement = 3
    }
}
