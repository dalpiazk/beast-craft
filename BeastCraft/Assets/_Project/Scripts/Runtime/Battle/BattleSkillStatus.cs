using BeastCraft.Creatures;

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
    /// The non-firing values are split rather than collapsed into one "did not fire" because they
    /// are genuinely different situations to read in a log or a test — nothing to shoot at at all,
    /// something to shoot at but no route to it, a route that this turn could not afford, and a
    /// walk the unit's stance declines — and because the first and last of them never consult the
    /// movement budget.
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
        /// unit could have afforded never came into it. No movement was spent: with no route at all
        /// there is nothing to make a partial approach along.
        /// </summary>
        Unreachable = 2,

        /// <summary>
        /// A candidate exists and a route to within range of it exists, but it is longer than the
        /// movement this unit has left this turn — either because an earlier skill in the same turn
        /// already spent the budget, or because the unit's <see cref="BattleUnit.MoveRange"/> was
        /// never enough on its own. The unit makes a <strong>partial approach</strong>: it walks that
        /// cheapest route for exactly the movement it has left (possibly none, if an earlier skill
        /// spent it all), recorded as the outcome's <see cref="BattleSkillOutcome.MovementSpent"/>,
        /// and then holds. Next turn it is that much closer. Later slots in the stack are still
        /// attempted from the tile it stopped on, with nothing left to walk with.
        /// </summary>
        OutOfMovement = 3,

        /// <summary>
        /// A candidate exists but is out of reach, and the unit's <see cref="CombatStance"/> forbids
        /// walking in for this skill: a <see cref="CombatStance.Ranged"/> unit never closes to melee
        /// (an enemy-side picking skill with <c>Range &lt;= 1</c>). Nothing is spent and nothing is
        /// walked; the slot keeps its 0 and fires on a later turn only if a target is then already
        /// in range (for instance an enemy that walked up to it). Later slots are attempted as
        /// usual with the whole remaining budget.
        /// </summary>
        HeldByStance = 4
    }
}
