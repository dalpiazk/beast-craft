namespace BeastCraft.Battle
{
    /// <summary>
    /// Which status a <see cref="SkillEffectType.ApplyStatus"/> effect puts on each unit it lands on
    /// (<see cref="SkillEffect.Status"/>). The rules live in <see cref="StatusEffects"/>; the design
    /// doc's "Status effects and advanced skill effects" section is the reference.
    /// <para>
    /// A small closed set. Flavours share a mechanic rather than getting a value each: Freeze is
    /// authored as <see cref="Stun"/>, Burn and Poison as <see cref="DamageOverTime"/>. The values
    /// are serialized into assets, so they are explicit and <strong>never renumbered</strong>; a new
    /// status is appended with the next free value.
    /// </para>
    /// </summary>
    public enum StatusType
    {
        /// <summary>No status. An <c>ApplyStatus</c> effect left at this does nothing.</summary>
        None = 0,

        /// <summary>
        /// The unit's enemy-side picking skills (<see cref="SkillTargetShape.SingleTarget"/> and
        /// <see cref="SkillTargetShape.Line"/>) must pick the unit that applied it while that unit is
        /// alive and a legal target, walking toward it when it is out of range. Latest taunt wins.
        /// </summary>
        Taunt = 1,

        /// <summary>
        /// The unit's turn is skipped: no movement, no skills, and its cooldowns do not tick. Its
        /// statuses and timed modifiers still tick and its gauge is spent as normal. Also Freeze.
        /// </summary>
        Stun = 2,

        /// <summary>
        /// Absorbs damage before HP. Worth <see cref="SkillEffect.Magnitude"/> percent of the
        /// caster's <c>Defense</c>. A unit holds one shield; the larger one is kept.
        /// </summary>
        Shield = 3,

        /// <summary>
        /// Deals a fixed amount, snapshotted when applied, at the start of each of the unit's own
        /// turns. Stacks up to <see cref="SkillEffect.MaxStacks"/>. Also Burn and Poison.
        /// </summary>
        DamageOverTime = 4,

        /// <summary>
        /// Pushes the unit <see cref="SkillEffect.Magnitude"/> hexes directly away from the caster,
        /// stopping at the first blocked, occupied or off-board tile. Instant: never stored.
        /// </summary>
        Knockback = 5
    }
}
