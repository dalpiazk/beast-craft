namespace BeastCraft.Economy
{
    /// <summary>
    /// What the caller of <c>BattleSession.ApplyRewards</c> adds to a clear's economy rewards: a gold
    /// multiplier and flat bonus (the campaign's dens, passes and lairs; see
    /// <c>CampaignRules.RewardModifiersFor</c>), and the library gear drops are drawn from (null =
    /// no gear drops). <see cref="None"/> changes nothing. Plain data; never mutate <see cref="None"/>.
    /// </summary>
    public sealed class RewardModifiers
    {
        /// <summary>No modifiers: gold x1, no bonus, no gear or cosmetic drops.</summary>
        public static readonly RewardModifiers None = new RewardModifiers();

        /// <summary>Multiplies the clear's gold (after the first-clear bonus). 1 = unchanged.</summary>
        public double GoldMultiplier = 1.0;

        /// <summary>Flat gold added after the multiplier.</summary>
        public int BonusGold;

        /// <summary>The gear library drops are drawn from (<c>drop-tables.json</c> <c>GearDrops</c>); null = no gear drops.</summary>
        public GearLibrary Gear;

        /// <summary>
        /// The cosmetic library battle drops (<c>drop-tables.json</c> <c>CosmeticDrops</c>) are drawn
        /// from and whose milestone looks are checked after the battle; null = neither.
        /// </summary>
        public CosmeticLibrary Cosmetics;

        /// <summary>The <see cref="Gear"/> and <see cref="Cosmetics"/> of <paramref name="economy"/> on these modifiers (for chaining).</summary>
        public RewardModifiers With(EconomyContent economy)
        {
            Gear = economy == null ? null : economy.Gear;
            Cosmetics = economy == null ? null : economy.Cosmetics;
            return this;
        }
    }
}
