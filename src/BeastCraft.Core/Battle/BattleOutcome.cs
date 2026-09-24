namespace BeastCraft.Battle
{
    /// <summary>
    /// How a battle ended, as reported by <see cref="BattleTurnExecutor.RunBattle"/>.
    /// <para>
    /// A win is "one team still has a living unit and the other does not", which is deliberately
    /// <em>not</em> <see cref="TurnManager.IsComplete"/> — see
    /// <see cref="BattleTurnExecutor.RunBattle"/> for why those two are different questions.
    /// </para>
    /// </summary>
    public enum BattleOutcome
    {
        /// <summary>Living units remain on <see cref="BattleTeam.Player"/> and none on the other side.</summary>
        PlayerVictory = 0,

        /// <summary>Living units remain on <see cref="BattleTeam.Enemy"/> and none on the other side.</summary>
        EnemyVictory = 1,

        /// <summary>
        /// Nobody is left standing on either side. Reachable in principle — an area skill can catch
        /// its own team, so a caster can take the last unit of both sides out in one cast — and it
        /// is also what an empty roster reports. Kept distinct from <see cref="Stalemate"/> because
        /// the battle genuinely resolved; it just resolved to nobody.
        /// </summary>
        MutualDefeat = 2,

        /// <summary>
        /// The battle was stopped without resolving: the time cap was reached (see
        /// <see cref="BattleTurnExecutor.DefaultMaxTime"/>), or the turn order ran out of units to
        /// act while both sides still had living members. Both mean the fight was not going to end
        /// on its own, and neither is a designed game rule.
        /// </summary>
        Stalemate = 3
    }
}
