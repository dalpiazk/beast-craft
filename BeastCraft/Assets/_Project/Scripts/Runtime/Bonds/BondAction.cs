namespace BeastCraft.Bonds
{
    /// <summary>
    /// What a <see cref="BondReaction"/> does when it fires.
    /// <para>
    /// Values are explicit and serialized into assets: append at the end, never renumber.
    /// </para>
    /// </summary>
    public enum BondAction
    {
        /// <summary>Apply the reaction's effects, cast by the reacting member, to its <see cref="BondReaction.Target"/>.</summary>
        Apply = 0,

        /// <summary>
        /// <see cref="BondTrigger.EnemyTargetsAlly"/> only: the reacting member takes the hit in the
        /// targeted beast's place. Its effects land on itself first (a small shield, say), then the
        /// enemy skill's target list is swapped for the member.
        /// </summary>
        Intercept = 1,
    }
}
