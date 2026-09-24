namespace BeastCraft.Battle
{
    /// <summary>
    /// How many beasts a side actively deploys onto the grid. The encounter picks the format; the
    /// format fixes the party size.
    /// </summary>
    public enum BattleFormat
    {
        /// <summary>A single beast per side. Max party size 1.</summary>
        Solo = 0,

        /// <summary>The standard squad fight. Max party size 4.</summary>
        SmallGroup = 1,

        /// <summary>The full set-piece fight. Max party size 6.</summary>
        LargeGroup = 2
    }

    /// <summary>Helpers for reading the rules a <see cref="BattleFormat"/> implies.</summary>
    public static class BattleFormatExtensions
    {
        /// <summary>
        /// The maximum number of beasts a side may deploy in this format: 1 for
        /// <see cref="BattleFormat.Solo"/>, 4 for <see cref="BattleFormat.SmallGroup"/>, 6 for
        /// <see cref="BattleFormat.LargeGroup"/>. An unrecognised value falls back to 1, which is
        /// the safest shape for a battle to start in rather than over-deploying.
        /// </summary>
        public static int MaxPartySize(this BattleFormat format)
        {
            switch (format)
            {
                case BattleFormat.Solo:
                    return 1;
                case BattleFormat.SmallGroup:
                    return 4;
                case BattleFormat.LargeGroup:
                    return 6;
                default:
                    return 1;
            }
        }
    }
}
