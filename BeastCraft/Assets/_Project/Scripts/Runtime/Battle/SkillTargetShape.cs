namespace BeastCraft.Battle
{
    /// <summary>
    /// The footprint a skill covers on the tactical grid. The grid topology is confirmed as
    /// hexagonal, and every shape here remains meaningful on a hex board: a line follows one of the
    /// six axial directions, a cross radiates along the axes from the origin tile, and an area
    /// burst is a hex ring/disc measured by axial distance.
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
