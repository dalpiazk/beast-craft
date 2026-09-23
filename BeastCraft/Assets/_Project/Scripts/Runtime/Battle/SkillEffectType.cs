namespace BeastCraft.Battle
{
    /// <summary>
    /// What a single <see cref="SkillEffect"/> does to each unit it lands on. The values are
    /// serialized into assets, so they are explicit and never renumbered.
    /// </summary>
    public enum SkillEffectType
    {
        Damage = 0,
        Heal = 1,
        BuffStat = 2,
        DebuffStat = 3,

        /// <summary>
        /// Applies the <see cref="StatusType"/> named by <see cref="SkillEffect.Status"/>; see
        /// <see cref="StatusEffects"/>. Does nothing when that is <see cref="StatusType.None"/>.
        /// </summary>
        ApplyStatus = 4
    }
}
