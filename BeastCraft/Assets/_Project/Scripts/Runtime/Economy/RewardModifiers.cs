namespace BeastCraft.Economy
{
    /// <summary>
    /// What the caller of <c>BattleSession.ApplyRewards</c> adds to a clear's economy rewards: a gold
    /// multiplier and flat bonus (the campaign's dens, passes and lairs; see
    /// <c>CampaignRules.RewardModifiersFor</c>). <see cref="None"/> changes nothing. Plain data; never
    /// mutate <see cref="None"/>.
    /// </summary>
    public sealed class RewardModifiers
    {
        /// <summary>No modifiers: gold x1, no bonus, no gear or cosmetic drops.</summary>
        public static readonly RewardModifiers None = new RewardModifiers();

        /// <summary>Multiplies the clear's gold (after the first-clear bonus). 1 = unchanged.</summary>
        public double GoldMultiplier = 1.0;

        /// <summary>Flat gold added after the multiplier.</summary>
        public int BonusGold;
    }
}
