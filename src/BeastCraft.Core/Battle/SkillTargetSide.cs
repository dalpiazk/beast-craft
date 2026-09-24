namespace BeastCraft.Battle
{
    /// <summary>
    /// Which side of the fight a skill is allowed to land on, expressed <em>relative to the
    /// caster</em> rather than as an absolute <see cref="BattleTeam"/>.
    /// <para>
    /// Authoring a skill against the caster's own allegiance is what keeps a single authored asset
    /// usable by both sides: the same "hit the enemy" skill works whether a player beast or an
    /// enemy beast casts it. There are exactly two teams, so "the other side" is simply "not the
    /// caster's team" — no third, neutral allegiance is modelled.
    /// </para>
    /// </summary>
    public enum SkillTargetSide
    {
        /// <summary>Units whose <see cref="BattleTeam"/> differs from the caster's.</summary>
        Enemy = 0,

        /// <summary>
        /// Units on the caster's own <see cref="BattleTeam"/>. Whether the caster counts as one of
        /// its own allies depends on the shape — see <see cref="SkillTargetResolver"/>, which
        /// documents that per shape.
        /// </summary>
        Ally = 1
    }
}
