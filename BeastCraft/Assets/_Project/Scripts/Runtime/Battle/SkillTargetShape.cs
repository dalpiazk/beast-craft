namespace BeastCraft.Battle
{
    /// <summary>
    /// The footprint a skill covers on the tactical grid. The grid topology is confirmed as
    /// hexagonal, and every shape here remains meaningful on a hex board: a line follows one of the
    /// six axial directions, a cross radiates along the axes from the origin tile, and an area
    /// burst is a hex ring/disc measured by axial distance.
    /// <para>
    /// <strong>The origin is always the caster's own tile, as it stands at the moment the skill
    /// fires.</strong> It is never a point chosen by a player or an AI: battles are auto-resolved,
    /// with the player positioning beasts before the fight rather than aiming their skills during
    /// it. A shape whose direction is not fixed by the caster alone — <see cref="Line"/> — derives
    /// that direction from the target it picks, not from an aim point. Consequently
    /// <c>SkillSO.Range</c> gates the whole footprint measured out from the caster, with nothing
    /// else for it to be relative to.
    /// </para>
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
