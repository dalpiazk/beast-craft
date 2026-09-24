namespace BeastCraft.Bonds
{
    /// <summary>
    /// The order a <see cref="BondReaction"/>'s candidate reactors (the bond's members) are tried in;
    /// the first that passes every check reacts.
    /// <para>
    /// Values are explicit and serialized into assets: append at the end, never renumber.
    /// </para>
    /// </summary>
    public enum BondReactorOrder
    {
        /// <summary>Team order.</summary>
        TeamOrder = 0,

        /// <summary>Highest current HP as a share of max HP first (exact, in 64-bit integers); ties in team order.</summary>
        HealthiestFirst = 1,
    }
}
