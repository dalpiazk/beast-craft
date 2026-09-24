namespace BeastCraft.Bonds
{
    /// <summary>
    /// What a <see cref="TeamBondSO"/> counts on the player's team to decide whether it is active
    /// and at which tier. See <see cref="TeamBondResolver"/> for the exact counting rule of each,
    /// and the battle-system design doc, "Team bonds".
    /// <para>
    /// Values are explicit and serialized into assets: append new conditions at the end with a new
    /// value, never renumber.
    /// </para>
    /// </summary>
    public enum TeamBondCondition
    {
        /// <summary>
        /// Team members of <see cref="TeamBondSO.Stance"/>. The count is how many members have
        /// that stance; they are the bond's members.
        /// </summary>
        Stance = 0,

        /// <summary>
        /// The element set <see cref="TeamBondSO.Elements"/>. The count is how many of the set's
        /// elements the team covers (each element once, however many members carry it), so a
        /// two-element bond needs both halves; the members are every beast carrying at least one of
        /// the set's elements.
        /// </summary>
        Elements = 1,

        /// <summary>
        /// The species set <see cref="TeamBondSO.SpeciesIds"/>. The count is how many of the listed
        /// species are on the team (each species once); the members are every beast of a listed
        /// species.
        /// </summary>
        Species = 2,

        /// <summary>
        /// How many distinct <see cref="Creatures.CombatStance"/>s the team fields (1 to 3). Every
        /// beast is a member.
        /// </summary>
        DistinctStances = 3,
    }
}
