namespace BeastCraft.Battle
{
    /// <summary>
    /// What kind of value a skill compares its candidates by when it has to pick one of them.
    /// Pairs with <see cref="SkillTargetingOrder"/>, which says which extreme of that value wins —
    /// the two compose the way <see cref="SkillEffect"/>'s effect type and affected stat do.
    /// <para>
    /// Battles are auto-resolved — the player positions beasts before the fight and each beast then
    /// acts on its own build — so this pair, not a player prompt, is what decides who gets hit. It
    /// only applies to the shapes that pick a single focus (see <see cref="SkillTargetShape"/>);
    /// the shapes that sweep a whole footprint hit every eligible unit in it and ignore both.
    /// </para>
    /// <para>
    /// Deliberately a small closed set rather than an extensibility hook. Adding a fourth criterion
    /// later is a one-line enum addition plus one <c>switch</c> arm in
    /// <see cref="SkillTargetResolver"/>, which is cheaper than maintaining a plugin seam for
    /// criteria nobody has asked for.
    /// </para>
    /// </summary>
    public enum SkillTargetingCriterion
    {
        /// <summary>
        /// No comparison at all: a uniform pick among the eligible candidates, drawn from the
        /// caller-supplied <see cref="System.Random"/> so a replayed battle picks the same unit.
        /// <see cref="SkillTargetingOrder"/> and <c>SkillSO.TargetingStat</c> are both ignored.
        /// </summary>
        Random = 0,

        /// <summary>
        /// Compare one authored <see cref="BeastCraft.Creatures.StatType"/>
        /// (<c>SkillSO.TargetingStat</c>) read through <c>StatBlock.GetStat</c> — not current HP,
        /// and not any aggregate of the stat block. This is the only criterion that consults
        /// <c>SkillSO.TargetingStat</c>.
        /// </summary>
        Stat = 1,

        /// <summary>
        /// Compare hex-step distance from the caster's live position to the candidate's, via
        /// <see cref="BeastCraft.Battle.Grid.HexCoordinate.Distance"/>. Measuring from the caster
        /// follows the confirmed rule that everything a skill does anchors to where the caster is
        /// standing right now. <c>SkillSO.TargetingStat</c> is ignored.
        /// </summary>
        Distance = 2
    }
}
