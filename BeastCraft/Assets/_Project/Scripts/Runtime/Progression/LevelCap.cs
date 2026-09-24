namespace BeastCraft.Progression
{
    /// <summary>
    /// The beast level cap's bank: a beast held at the cap keeps earning, but what it earns waits in
    /// <see cref="BeastProgress.BankedXp"/> and turns into levels only when the cap rises.
    /// <see cref="BeastProgression.AddXp(BeastProgress, int, int)"/> does the banking;
    /// <see cref="Release"/> spends the bank at a new cap. The cap itself comes from the campaign's
    /// seals (<c>Campaign.LevelCaps.BeastCap</c>); the avatar has no cap.
    /// <para>
    /// <strong>The rules.</strong> Below the cap a beast levels as usual. At the cap its
    /// <see cref="BeastProgress.Xp"/> fills to one short of the next level and the rest goes to the
    /// bank, and the two together hold at most the XP of <see cref="BankLevelLimit"/> levels (more is
    /// lost), so a raised cap pays out at most that many levels at once. A beast obtained above the
    /// cap keeps its level and only banks, counted from its own level. At the max level nothing is
    /// held. <strong>Tunable starting default</strong> (lead decision: 3 levels).
    /// </para>
    /// </summary>
    public static class LevelCap
    {
        /// <summary>At the cap, XP and bank together hold at most this many levels' worth.</summary>
        public const int BankLevelLimit = 3;

        /// <summary>
        /// Spends <paramref name="progress"/>'s bank at <paramref name="newCap"/>: levels up while
        /// below it and the XP plus bank covers the next level, then re-banks what is left under the
        /// same rules. Returns the levels gained (0 for a null progress or a cap it is not below).
        /// </summary>
        public static int Release(BeastProgress progress, int newCap)
        {
            return BeastProgression.AddXp(progress, 0, newCap);
        }

        /// <summary>
        /// How many whole levels <paramref name="progress"/>'s XP plus bank would buy with no cap
        /// (0 when it has nothing banked toward a level, or is null).
        /// </summary>
        public static int BankedLevels(BeastProgress progress)
        {
            if (progress == null)
            {
                return 0;
            }

            int level = progress.Level < 1 ? 1 : progress.Level;
            long pool = (long)(progress.Xp < 0 ? 0 : progress.Xp) + (progress.BankedXp < 0 ? 0 : progress.BankedXp);
            int levels = 0;
            while (level < BeastProgression.MaxLevel && pool >= BeastProgression.XpToNextLevel(level))
            {
                pool -= BeastProgression.XpToNextLevel(level);
                level++;
                levels++;
            }

            return levels;
        }
    }
}
