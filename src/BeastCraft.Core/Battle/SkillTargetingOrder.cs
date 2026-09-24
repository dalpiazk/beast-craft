namespace BeastCraft.Battle
{
    /// <summary>
    /// Which extreme of a <see cref="SkillTargetingCriterion"/>'s compared value a skill picks.
    /// Applies uniformly whatever the criterion measures, which is the point of splitting the two
    /// apart: "weakest" and "strongest" read as nonsense against a distance, while
    /// lowest/highest reads correctly against both a stat and a distance.
    /// <para>
    /// The meaningful combinations:
    /// <c>Stat</c>+<see cref="Lowest"/> is the weakest candidate by that stat,
    /// <c>Stat</c>+<see cref="Highest"/> the strongest,
    /// <c>Distance</c>+<see cref="Lowest"/> the one nearest the caster,
    /// <c>Distance</c>+<see cref="Highest"/> the one farthest from it,
    /// <c>CurrentHp</c>+<see cref="Lowest"/> the one with the least HP left, and
    /// <c>CurrentHp</c>+<see cref="Highest"/> the one with the most.
    /// Ignored entirely when the criterion is <see cref="SkillTargetingCriterion.Random"/>.
    /// </para>
    /// </summary>
    public enum SkillTargetingOrder
    {
        /// <summary>Pick the candidate with the smallest compared value.</summary>
        Lowest = 0,

        /// <summary>Pick the candidate with the largest compared value.</summary>
        Highest = 1
    }
}
