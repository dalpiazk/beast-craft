namespace BeastCraft.Bonds
{
    /// <summary>
    /// Who an active <see cref="TeamBondSO"/>'s tier effects land on at battle start. Every
    /// recipient applies the effects to itself (it is its own caster), so a shield scales with the
    /// recipient's own <c>Defense</c> and a percent buff with its own stat.
    /// <para>
    /// Values are explicit and serialized into assets: append new scopes at the end with a new
    /// value, never renumber.
    /// </para>
    /// </summary>
    public enum TeamBondScope
    {
        /// <summary>Only the bond's members (the beasts that satisfy its condition).</summary>
        Members = 0,

        /// <summary>Every beast on the bonded team, members or not.</summary>
        Team = 1,
    }
}
