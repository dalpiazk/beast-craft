namespace BeastCraft.Creatures
{
    /// <summary>
    /// How a unit positions itself during the auto-resolved fight: the one piece of tactical
    /// behaviour a species carries beyond its skills. Read by <c>BattleTurnExecutor</c> when
    /// it chooses where an approach stops and what to do with movement left over once every ready
    /// slot has been attempted. See the battle-system design doc, "Combat stances".
    /// <para>
    /// A species property (<c>CreatureSpeciesSO.Stance</c>, authored in the roster JSON as the
    /// member name), copied onto the unit by <c>BattleUnitFactory</c> — not a per-battle
    /// order. Every stance keeps the confirmed movement rule (decision 7): one budget shared by the
    /// whole turn, spent by skills in stack order. A stance only changes tie-breaks, whether a melee
    /// slot may walk, and whether leftover budget is spent retreating.
    /// </para>
    /// <para>
    /// <strong>The values are explicit and must never be renumbered.</strong> Unity serializes an
    /// enum field on an asset by its integer value, so reordering or renumbering these would
    /// silently change the stance of every species asset already imported. Add new stances at the
    /// end with a new value.
    /// </para>
    /// </summary>
    public enum CombatStance
    {
        /// <summary>
        /// The front line, and the default. Approaches exactly as the plain movement rule does and
        /// does not avoid crowds. Among equally short approaches it prefers the stop tile nearest
        /// its closest living <see cref="Ranged"/> or <see cref="Skirmisher"/> ally, so it tends to
        /// stand between the enemy and its fragile team-mates. Never retreats.
        /// </summary>
        Vanguard = 0,

        /// <summary>
        /// Keeps its distance. Approaches only as far as a skill's range needs, never walks in to
        /// use a melee skill (<c>Range &lt;= 1</c>: such a slot fires only if a target is already
        /// adjacent), avoids stop tiles next to several enemies, and spends leftover movement
        /// backing away from the nearest enemy — but never beyond its longest single-target reach.
        /// </summary>
        Ranged = 1,

        /// <summary>
        /// Hit and run. Approaches and fires like a <see cref="Vanguard"/> (melee included), avoids
        /// crowded stop tiles, then spends leftover movement retreating by the same rule as
        /// <see cref="Ranged"/>.
        /// </summary>
        Skirmisher = 2
    }
}
