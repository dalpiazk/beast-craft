namespace BeastCraft.Battle
{
    /// <summary>
    /// One damage effect that landed on one target during a <see cref="SkillActivation"/>: who was
    /// hit and the <see cref="DamageRoll"/> behind it (amount, crit, variance roll). Recorded by
    /// <see cref="SkillEffectApplier"/> into <see cref="SkillActivation.Hits"/> so a caller (a
    /// battle log, the balance simulator) can tell a crit from an ordinary hit without re-deriving
    /// it. A report, not an event system: nothing subscribes to it and nothing reads it back into
    /// the battle.
    /// </summary>
    public readonly struct DamageHit
    {
        public DamageHit(BattleUnit target, DamageRoll roll)
        {
            Target = target;
            Roll = roll;
        }

        /// <summary>The unit the damage effect landed on.</summary>
        public BattleUnit Target { get; }

        /// <summary>
        /// The formula's result: <see cref="DamageRoll.Amount"/> is what the hit was worth before
        /// being clamped to the target's remaining HP, so an overkill hit reports its full value.
        /// </summary>
        public DamageRoll Roll { get; }
    }
}
