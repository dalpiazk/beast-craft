namespace BeastCraft.Avatar
{
    /// <summary>
    /// When an avatar <see cref="PassiveSkillSO"/> fires. See the battle-system design doc,
    /// "Avatar passives", for the exact hook points and their order.
    /// <para>
    /// Values are explicit and serialized into assets: append new triggers at the end with a new
    /// value, never renumber.
    /// </para>
    /// </summary>
    public enum PassiveTrigger
    {
        /// <summary>
        /// Applied once as the battle begins, before any <see cref="BattleStart"/> passive, and meant
        /// to last the whole battle: author its stat changes with <c>DurationTurns</c> 0, which the
        /// effect engine applies as a permanent change that never reverts.
        /// </summary>
        Aura = 0,

        /// <summary>A one-shot as the battle begins, after every <see cref="Aura"/>; its effects keep their authored durations.</summary>
        BattleStart = 1,

        /// <summary>An enemy beast was defeated by anything other than a passive's own effect.</summary>
        EnemyDefeated = 2,

        /// <summary>One of the player's beasts was defeated by anything other than a passive's own effect.</summary>
        AllyDefeated = 3,

        /// <summary>A damage hit landed by one of the player's beasts was a critical hit (once per critical hit).</summary>
        AllyCrit = 4,

        /// <summary>One of the player's beasts is starting its turn.</summary>
        AllyTurnStart = 5,

        /// <summary>
        /// One of the player's beasts has dropped below <see cref="PassiveSkillSO.HpThresholdPercent"/>
        /// of its max HP. Fires once per crossing: it re-arms only once that beast is seen at or
        /// above the threshold again.
        /// </summary>
        AllyBelowHpPercent = 6,
    }
}
