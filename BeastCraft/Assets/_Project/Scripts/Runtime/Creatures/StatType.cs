namespace BeastCraft.Creatures
{
    /// <summary>
    /// The combat stat axes. Referenced by <see cref="StatBlock"/>, gear modifiers and skill
    /// buff/debuff effects.
    /// <para>
    /// Values are persisted in authored assets (gear modifiers, skill effects), so they are
    /// append-only: a new axis takes the next number, and no existing axis is ever renumbered.
    /// </para>
    /// </summary>
    public enum StatType
    {
        HP = 0,
        Attack = 1,
        Defense = 2,
        SpecialAttack = 3,
        SpecialDefense = 4,
        Speed = 5,

        /// <summary>
        /// How many hex steps a unit may move on one of its own turns. A species-authored base
        /// that, unlike the other axes, does <em>not</em> scale with level (see
        /// <see cref="CreatureSpeciesSO.GetStatAtLevel"/>), but that gear modifiers and
        /// buff/debuff effects move like any other stat.
        /// </summary>
        MoveRange = 6
    }
}
