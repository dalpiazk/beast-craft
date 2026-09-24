namespace BeastCraft.Bonds
{
    /// <summary>
    /// Who an <see cref="BondAction.Apply"/> reaction's effects land on. Every reaction is cast by
    /// the reacting member (its stats, level, element).
    /// <para>
    /// Values are explicit and serialized into assets: append at the end, never renumber.
    /// </para>
    /// </summary>
    public enum BondReactionTarget
    {
        /// <summary>The reacting member itself.</summary>
        Self = 0,

        /// <summary>The team's beast the trigger was about (the one hit, afflicted, below the threshold, or critting).</summary>
        TriggeringAlly = 1,

        /// <summary>The enemy the triggering hit or crit landed on.</summary>
        TriggerTarget = 2,

        /// <summary>The enemy whose skill hit or targeted the team's beast.</summary>
        Attacker = 3,

        /// <summary>Every living enemy within <see cref="BondReaction.Range"/> of the reacting member.</summary>
        EnemiesNearReactor = 4,

        /// <summary>Every living beast of the team, in team order.</summary>
        Team = 5,
    }
}
