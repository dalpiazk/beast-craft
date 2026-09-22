namespace BeastCraft.Battle
{
    /// <summary>
    /// What a single <see cref="SkillEffect"/> does to each unit it lands on.
    /// </summary>
    public enum SkillEffectType
    {
        Damage = 0,
        Heal = 1,
        BuffStat = 2,
        DebuffStat = 3,
        ApplyStatus = 4
    }
}
