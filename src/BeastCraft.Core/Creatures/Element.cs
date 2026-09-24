namespace BeastCraft.Creatures
{
    /// <summary>
    /// The elemental affinities a species can carry (<see cref="CreatureSpeciesSO.Elements"/>) and
    /// a skill can deal damage in (<c>SkillSO.Element</c>). How one element fares against another
    /// is <c>BeastCraft.Battle.ElementChart</c>'s job, not this enum's.
    /// <para>
    /// Values are explicit and serialized into authored assets by number. Never rename or
    /// renumber after ship; append new elements at the end with a new value.
    /// </para>
    /// </summary>
    public enum Element
    {
        /// <summary>
        /// No element. A neutral skill, or an absent affinity; always a 1x multiplier on either
        /// side of <c>ElementChart</c>.
        /// </summary>
        None = 0,
        Fire = 1,
        Water = 2,
        Earth = 3,
        Air = 4,
        Lightning = 5,
        Ice = 6,
        Nature = 7,
        Metal = 8,
        Light = 9,
        Dark = 10
    }
}
