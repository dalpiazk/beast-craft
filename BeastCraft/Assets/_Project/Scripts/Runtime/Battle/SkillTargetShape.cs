namespace BeastCraft.Battle
{
    /// <summary>
    /// The footprint a skill covers on the tactical grid. Deliberately generic: the grid topology
    /// (square vs hex) is not yet decided, and these shapes are meaningful under either.
    /// </summary>
    public enum SkillTargetShape
    {
        SingleTarget = 0,
        Line = 1,
        Cross = 2,
        AreaBurst = 3,
        AllEnemies = 4,
        AllAllies = 5,
        Self = 6
    }
}
