namespace BeastCraft.Battle
{
    /// <summary>
    /// One status currently riding on a unit (<see cref="BattleUnit.Statuses"/>): what it is, who
    /// applied it, how many of the affected unit's own turns it has left, and — for a shield or a
    /// damage-over-time stack — the amount it carries.
    /// <para>
    /// A reference type for the same reason as <see cref="ActiveStatModifier"/>: the status engine
    /// (<see cref="StatusEffects"/>) writes <see cref="RemainingTurns"/> and
    /// <see cref="Amount"/> in place. Only that engine creates or changes these; everything else
    /// reads them through the unit's read-only view.
    /// </para>
    /// </summary>
    public sealed class ActiveStatus
    {
        public ActiveStatus(StatusType type, BattleUnit source, int remainingTurns, int amount, SkillEffect origin)
        {
            Type = type;
            Source = source;
            RemainingTurns = remainingTurns;
            Amount = amount;
            Origin = origin;
        }

        /// <summary>Which status this is.</summary>
        public StatusType Type { get; }

        /// <summary>
        /// The unit that applied it. For a <see cref="StatusType.Taunt"/> this is the unit that must
        /// be picked; for the others it is a record only.
        /// </summary>
        public BattleUnit Source { get; }

        /// <summary>
        /// The authored effect that applied it: the identity damage-over-time stacks are counted by
        /// against <see cref="SkillEffect.MaxStacks"/>.
        /// </summary>
        public SkillEffect Origin { get; }

        /// <summary>
        /// The affected unit's own turns this status is still in force for, counting the current one
        /// once it has begun. Decremented as each of the unit's turns begins; the status is removed
        /// when a turn ends with this at 0. See <see cref="StatusEffects.BeginTurn"/>.
        /// </summary>
        public int RemainingTurns { get; internal set; }

        /// <summary>
        /// Shield points left for a <see cref="StatusType.Shield"/> (spent by absorbed damage), or
        /// the per-turn damage of one <see cref="StatusType.DamageOverTime"/> stack (fixed when it
        /// was applied). 0 for the other statuses.
        /// </summary>
        public int Amount { get; internal set; }
    }
}
