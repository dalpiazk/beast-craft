namespace BeastCraft.Bonds
{
    /// <summary>
    /// The battle event a <see cref="BondReaction"/> answers. See <see cref="TeamBondLoadout"/> for
    /// exactly when each is checked, and the battle-system design doc, "Team bonds".
    /// <para>
    /// Values are explicit and serialized into assets: append new triggers at the end with a new
    /// value, never renumber.
    /// </para>
    /// </summary>
    public enum BondTrigger
    {
        /// <summary>No reaction: the tier is a battle-start bond only. The default, so a reaction Unity zero-fills is inert.</summary>
        None = 0,

        /// <summary>
        /// An enemy's <c>SingleTarget</c> skill has picked exactly one of the team's beasts, before it
        /// lands. The only trigger an <see cref="BondAction.Intercept"/> reaction may use.
        /// </summary>
        EnemyTargetsAlly = 1,

        /// <summary>A beast of the team landed a critical hit on its own turn. The reactor is never that beast.</summary>
        AllyCrit = 2,

        /// <summary>An enemy's <c>SingleTarget</c> skill hit a beast of the team. The reactor is never the beast hit.</summary>
        AllyHitByEnemy = 3,

        /// <summary>A member landed a damage hit on an enemy with its own skill. The reactor is that member.</summary>
        MemberHit = 4,

        /// <summary>A member landed a critical hit on an enemy with its own skill. The reactor is that member.</summary>
        MemberCrit = 5,

        /// <summary>
        /// A beast of the team is now strictly below <see cref="BondReaction.HpThresholdPercent"/> of
        /// its max HP: one attempt per crossing, re-armed once it is seen at or above it again.
        /// </summary>
        AllyBelowHpPercent = 6,

        /// <summary>
        /// A beast of the team is about to begin its turn stunned or carrying damage-over-time
        /// (checked before its statuses tick, so a cleanse here lets it act and spares it the damage).
        /// </summary>
        AllyTurnStartAfflicted = 7,

        /// <summary>A beast of the team was defeated.</summary>
        AllyDefeated = 8,
    }
}
