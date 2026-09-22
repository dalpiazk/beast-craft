namespace BeastCraft.Battle
{
    /// <summary>
    /// The one tie-break every deterministic ordering in a battle falls back on: ordinal comparison
    /// of <see cref="BattleUnit.Id"/>.
    /// <para>
    /// Both orderings that need it — <see cref="TurnManager"/>'s initiative sort and
    /// <see cref="SkillTargetResolver"/>'s roster sort — must agree on it, so it lives in one place
    /// rather than being hand-rolled twice. The rationale is the same in both cases and is worth
    /// stating once, here: keying on the id makes an order a pure function of the roster's
    /// <em>contents</em>, never of however the roster happened to be assembled or of an unstable
    /// sort's internal choices. That is what lets the order be recomputed mid-battle without
    /// silently reshuffling equal-keyed units, and what lets a client and a server-authoritative
    /// re-simulation of the same battle reach the same answer.
    /// </para>
    /// <para>
    /// Ordinal, not culture-aware: ids are opaque keys, not display text, so the comparison must not
    /// shift with the machine's locale. It is deliberately <em>only</em> the tie-break primitive —
    /// callers that rank on something else first (speed, a targeting stat) keep that logic to
    /// themselves and defer here just for the last word.
    /// </para>
    /// </summary>
    internal static class BattleUnitOrder
    {
        /// <summary>
        /// Ordinal comparison of two units' <see cref="BattleUnit.Id"/>s, shaped as a
        /// <see cref="System.Comparison{T}"/> so it can be handed straight to
        /// <see cref="System.Collections.Generic.List{T}.Sort(System.Comparison{T})"/>.
        /// </summary>
        internal static int CompareById(BattleUnit a, BattleUnit b)
        {
            return string.CompareOrdinal(a.Id, b.Id);
        }
    }
}
