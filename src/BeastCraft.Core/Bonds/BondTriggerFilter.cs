namespace BeastCraft.Bonds
{
    /// <summary>
    /// Which of the team's beasts may be the triggering ally of an ally trigger
    /// (<see cref="BondTrigger.EnemyTargetsAlly"/>, <see cref="BondTrigger.AllyCrit"/>,
    /// <see cref="BondTrigger.AllyHitByEnemy"/>, <see cref="BondTrigger.AllyBelowHpPercent"/>,
    /// <see cref="BondTrigger.AllyTurnStartAfflicted"/>, <see cref="BondTrigger.AllyDefeated"/>).
    /// <para>
    /// Values are explicit and serialized into assets: append at the end, never renumber.
    /// </para>
    /// </summary>
    public enum BondTriggerFilter
    {
        /// <summary>Any beast of the team.</summary>
        Any = 0,

        /// <summary>Only the bond's members (a Ranged member hit, for a Ranged bond).</summary>
        Members = 1,

        /// <summary>Only beasts that are not the bond's members (the non-Vanguards a Vanguard bond guards).</summary>
        NonMembers = 2,
    }
}
