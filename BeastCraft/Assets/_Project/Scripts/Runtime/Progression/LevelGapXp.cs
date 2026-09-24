namespace BeastCraft.Progression
{
    /// <summary>
    /// The level-gap falloff on battle XP: a beast or the avatar fighting an encounter below its own
    /// level earns only a share of the battle's XP, falling to nothing five levels down, so old
    /// content cannot be farmed for levels. Applied to the WHOLE battle XP (participation included)
    /// by <see cref="BeastProgression.AwardBattle"/>, <see cref="BeastProgression.BenchXp"/> and
    /// <see cref="AvatarProgression.AwardBattle"/>, on the level before the award. Skill practice
    /// XP is not touched.
    /// <para>
    /// <strong>The table</strong> (gap = own level − encounter level): 0 or below 100%, +1 60%,
    /// +2 25%, +3 10%, +4 5%, +5 and above 0%. The share is applied with integer maths, rounding
    /// down. <strong>Tunable starting defaults, not confirmed balance</strong>; the campaign pacing
    /// model (balance simulator <c>--mode campaign</c>) measures them.
    /// </para>
    /// </summary>
    public static class LevelGapXp
    {
        /// <summary>Percent paid at each gap 0..4 above the encounter (index = gap); any larger gap pays 0.</summary>
        private static readonly int[] PercentByGap = { 100, 60, 25, 10, 5 };

        /// <summary>The largest gap that still pays anything.</summary>
        public static int MaxPaidGap
        {
            get { return PercentByGap.Length - 1; }
        }

        /// <summary>
        /// Own level minus encounter level, each read as at least 1 (positive = fighting below
        /// one's level).
        /// </summary>
        public static int Gap(int ownLevel, int encounterLevel)
        {
            return (ownLevel < 1 ? 1 : ownLevel) - (encounterLevel < 1 ? 1 : encounterLevel);
        }

        /// <summary>The percent of battle XP paid at <paramref name="gap"/> (<see cref="Gap"/>): 100 at 0 or below, then the table, then 0.</summary>
        public static int Percent(int gap)
        {
            if (gap <= 0)
            {
                return 100;
            }

            return gap < PercentByGap.Length ? PercentByGap[gap] : 0;
        }

        /// <summary><paramref name="xp"/> (negative read as 0) times <see cref="Percent"/> of <paramref name="gap"/>, rounded down.</summary>
        public static int Apply(int xp, int gap)
        {
            if (xp <= 0)
            {
                return 0;
            }

            return (int)((long)xp * Percent(gap) / 100);
        }
    }
}
