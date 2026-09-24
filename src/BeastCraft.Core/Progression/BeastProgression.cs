using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The rules of a beast's own level: its XP curve and what a battle pays each beast that was
    /// fielded. Deliberately its own constants, parallel to but not coupled with
    /// <see cref="AvatarProgression"/>. Applied after a battle by <c>BattleSession.ApplyRewards</c>;
    /// paced in <c>docs/balance/campaign-pacing-report.md</c> (the region campaign) and
    /// <c>docs/balance/pacing-report.md</c>, "Beast level".
    /// <para>
    /// <strong>Autonomous default, pending producer review.</strong> These numbers were chosen
    /// without a design brief, to one target: a fielded beast levels alongside the encounters, its
    /// median level within 3 of the node level at every gate and boss of the region campaign
    /// (balance simulator <c>--mode campaign</c>) and of the encounter level at every checkpoint of
    /// the pacing campaign (<c>--mode pacing</c>), as the avatar does. All tunable; nothing else
    /// depends on the exact values.
    /// </para>
    /// <para>
    /// <strong>Who is paid.</strong> Every beast fielded in a finished battle earns
    /// <see cref="ParticipationXp"/>, won or lost, and also when it was knocked out. On a
    /// <see cref="BattleOutcome.PlayerVictory"/>, a fielded beast still standing at the end also
    /// earns the clear bonus <c>ClearBaseXp + ClearXpPerEnemyLevel × enemyLevel</c>; a beast knocked
    /// out before the end earns participation only. A beast left on the bench earns a share of what
    /// a standing fielded beast earns (<see cref="BenchXp"/>): 10% at or above the enemy's level,
    /// 9% more per level below it, all of it from 10 levels down — so a reserve settles about 6
    /// levels behind the team (campaign report: 5-6 from region 3) instead of falling ever further
    /// back, and a new recruit catches up at the full rate.
    /// </para>
    /// <para>
    /// <strong>Bench numbers retuned from the lead's 50% + 7.5% per level, pending lead/user
    /// review.</strong> That rule was chosen for its outcome, "reserves about 6-7 levels behind",
    /// estimated with the fielded beasts earning their full XP. Under the level-gap falloff and
    /// knockouts they earn about 70% of it, so 50% + 7.5% settled the bench only 1-2 levels behind
    /// (<c>--mode campaign</c>); the same shape at 10% + 9% gives the intended 5-8.
    /// </para>
    /// <para>
    /// <strong>Falloff and cap.</strong> Every award is cut by the level-gap falloff on the beast's
    /// own level (<see cref="LevelGapXp"/>) and added under the campaign's beast level cap
    /// (<see cref="AddXp(BeastProgress, int, int)"/>, <see cref="LevelCap"/>).
    /// </para>
    /// <para>
    /// <strong>The curve.</strong> A level costs <c>XpCurveBase + XpCurvePerLevel × level</c>. The
    /// clear bonus was raised from <c>40 + 4 L</c> to <c>50 + 5 L</c> for the region campaign, whose
    /// battles clear about 70% of the time (80% squads, harder elites, gates and bosses; a loss is
    /// retried): at that rate and a beast knocked out in 20% of won battles a battle pays about
    /// <c>6 + 0.56 × (50 + 5 L) ≈ 34 + 2.8 L</c>, a level every 4.6-5 battles, so the fielded team
    /// arrives at each gate and boss within a level of it in about 540 battles
    /// (<c>--mode campaign</c>). Running ahead is held back by the level-gap falloff, which is why
    /// the pacing model's 80% campaign (five battles a level) now tracks one level above the
    /// encounters instead of on them. Fighting below one's level pays less, so grinding easy
    /// content is slow.
    /// </para>
    /// <para>
    /// Non-throwing: a null progress is a no-op; level and XP are normalized on every write.
    /// </para>
    /// </summary>
    public static class BeastProgression
    {
        /// <summary>A beast's highest level (the growth curves' max level).</summary>
        public const int MaxLevel = 100;

        /// <summary>The flat part of a level's cost.</summary>
        public const int XpCurveBase = 160;

        /// <summary>A level's cost grows by this much per level.</summary>
        public const int XpCurvePerLevel = 13;

        /// <summary>XP for every fielded beast in any finished battle: won or lost, standing or knocked out.</summary>
        public const int ParticipationXp = 6;

        /// <summary>The flat part of the clear bonus (a player victory, beast still standing).</summary>
        public const int ClearBaseXp = 50;

        /// <summary>The clear bonus per enemy level.</summary>
        public const int ClearXpPerEnemyLevel = 5;

        /// <summary>A benched beast's share of the battle's XP, in tenths of a percent, at or above the enemy's level (10%; see the class remarks).</summary>
        public const int BenchShareBasePermille = 100;

        /// <summary>The bench share grows by this much (tenths of a percent) per level the benched beast is below the enemy (9%), up to all of it.</summary>
        public const int BenchSharePerLevelPermille = 90;

        /// <summary>
        /// XP from <paramref name="level"/> to the next: <c>XpCurveBase + XpCurvePerLevel × level</c>,
        /// a level below 1 read as 1. 173 at level 1, 1,447 at level 99.
        /// </summary>
        public static int XpToNextLevel(int level)
        {
            int l = level < 1 ? 1 : level;
            return XpCurveBase + (XpCurvePerLevel * l);
        }

        /// <summary>Total XP from level 1 to <paramref name="level"/> (clamped to the max level). 0 at level 1 or below.</summary>
        public static int TotalXpToReach(int level)
        {
            int total = 0;
            int top = level > MaxLevel ? MaxLevel : level;
            for (int l = 1; l < top; l++)
            {
                total += XpToNextLevel(l);
            }

            return total;
        }

        /// <summary>
        /// What one battle pays a fielded beast: <see cref="ParticipationXp"/> for any outcome, plus
        /// the clear bonus <c>ClearBaseXp + ClearXpPerEnemyLevel × enemyLevel</c> on a
        /// <see cref="BattleOutcome.PlayerVictory"/> when it was not <paramref name="knockedOut"/>
        /// (an enemy level below 1 read as 1).
        /// </summary>
        public static int BattleXp(BattleOutcome outcome, int enemyLevel, bool knockedOut)
        {
            if (outcome != BattleOutcome.PlayerVictory || knockedOut)
            {
                return ParticipationXp;
            }

            int level = enemyLevel < 1 ? 1 : enemyLevel;
            return ParticipationXp + ClearBaseXp + (ClearXpPerEnemyLevel * level);
        }

        /// <summary>
        /// What one battle pays a fielded beast of <paramref name="beastLevel"/>:
        /// <see cref="BattleXp(BattleOutcome, int, bool)"/> after the level-gap falloff
        /// (<see cref="LevelGapXp"/>, on <paramref name="beastLevel"/> − <paramref name="enemyLevel"/>).
        /// </summary>
        public static int BattleXp(BattleOutcome outcome, int enemyLevel, bool knockedOut, int beastLevel)
        {
            return LevelGapXp.Apply(BattleXp(outcome, enemyLevel, knockedOut), LevelGapXp.Gap(beastLevel, enemyLevel));
        }

        /// <summary>
        /// Credits one finished battle to a fielded beast: <see cref="BattleXp(BattleOutcome, int, bool, int)"/>
        /// at its level before the award, added under <paramref name="levelCap"/>
        /// (<see cref="AddXp(BeastProgress, int, int)"/>; the default, <see cref="MaxLevel"/>, is no
        /// cap). Returns the levels gained.
        /// </summary>
        public static int AwardBattle(BeastProgress progress, BattleOutcome outcome, int enemyLevel, bool knockedOut, int levelCap = MaxLevel)
        {
            if (progress == null)
            {
                return 0;
            }

            return AddXp(progress, BattleXp(outcome, enemyLevel, knockedOut, progress.Level), levelCap);
        }

        /// <summary>
        /// The share of a battle's XP a beast left on the bench earns, in tenths of a percent:
        /// <c>BenchShareBasePermille + BenchSharePerLevelPermille × levels below the enemy</c>, at
        /// most 1000 (all of it). 100 at or above the enemy's level, 1000 from 10 levels below.
        /// </summary>
        public static int BenchSharePermille(int enemyLevel, int benchLevel)
        {
            int below = -LevelGapXp.Gap(benchLevel, enemyLevel);
            int permille = BenchShareBasePermille + (BenchSharePerLevelPermille * (below < 0 ? 0 : below));
            return permille > 1000 ? 1000 : permille;
        }

        /// <summary>
        /// What one battle pays a beast of <paramref name="benchLevel"/> left on the bench:
        /// <see cref="BenchSharePermille"/> of what a fielded beast still standing earns
        /// (<see cref="BattleXp(BattleOutcome, int, bool)"/>, rounded down), then the level-gap
        /// falloff on its own level. Never more than a standing fielded beast at the enemy's level.
        /// </summary>
        public static int BenchXp(BattleOutcome outcome, int enemyLevel, int benchLevel)
        {
            long share = (long)BattleXp(outcome, enemyLevel, false) * BenchSharePermille(enemyLevel, benchLevel) / 1000;
            return LevelGapXp.Apply((int)share, LevelGapXp.Gap(benchLevel, enemyLevel));
        }

        /// <summary>Credits one finished battle to a benched beast (<see cref="BenchXp"/> at its level before the award) under <paramref name="levelCap"/>. Returns the levels gained.</summary>
        public static int AwardBench(BeastProgress progress, BattleOutcome outcome, int enemyLevel, int levelCap = MaxLevel)
        {
            if (progress == null)
            {
                return 0;
            }

            return AddXp(progress, BenchXp(outcome, enemyLevel, progress.Level), levelCap);
        }

        /// <summary>
        /// Adds XP (negative read as 0) with no level cap: <see cref="AddXp(BeastProgress, int, int)"/>
        /// at <see cref="MaxLevel"/>. Returns the levels gained.
        /// </summary>
        public static int AddXp(BeastProgress progress, int xp)
        {
            return AddXp(progress, xp, MaxLevel);
        }

        /// <summary>
        /// Adds XP (negative read as 0) under <paramref name="levelCap"/> (clamped to 1-<see cref="MaxLevel"/>).
        /// The XP, anything already banked and the new XP are pooled; the beast levels up while it is
        /// below the cap and the pool covers the next level. Below the cap the rest is its
        /// <see cref="BeastProgress.Xp"/> (and the bank is empty). At or above the cap the pool is
        /// held (<see cref="LevelCap"/>): at most the XP of <see cref="LevelCap.BankLevelLimit"/>
        /// levels from its level (the excess is lost), <see cref="BeastProgress.Xp"/> filled to one
        /// short of the next level and the rest in <see cref="BeastProgress.BankedXp"/>. At
        /// <see cref="MaxLevel"/> both are 0. Returns the levels gained.
        /// </summary>
        public static int AddXp(BeastProgress progress, int xp, int levelCap)
        {
            if (progress == null)
            {
                return 0;
            }

            progress.Level = progress.Level < 1 ? 1 : progress.Level > MaxLevel ? MaxLevel : progress.Level;
            int cap = levelCap < 1 ? 1 : levelCap > MaxLevel ? MaxLevel : levelCap;
            long pool = (long)(progress.Xp < 0 ? 0 : progress.Xp) + (progress.BankedXp < 0 ? 0 : progress.BankedXp) + (xp < 0 ? 0 : xp);
            int gained = 0;

            while (progress.Level < cap && pool >= XpToNextLevel(progress.Level))
            {
                pool -= XpToNextLevel(progress.Level);
                progress.Level++;
                gained++;
            }

            if (progress.Level >= MaxLevel)
            {
                progress.Xp = 0;
                progress.BankedXp = 0;
                return gained;
            }

            if (progress.Level < cap)
            {
                progress.Xp = (int)pool;
                progress.BankedXp = 0;
                return gained;
            }

            int top = progress.Level + LevelCap.BankLevelLimit > MaxLevel ? MaxLevel : progress.Level + LevelCap.BankLevelLimit;
            long limit = (long)TotalXpToReach(top) - TotalXpToReach(progress.Level);
            if (pool > limit)
            {
                pool = limit;
            }

            long hold = XpToNextLevel(progress.Level) - 1;
            progress.Xp = (int)(pool < hold ? pool : hold);
            progress.BankedXp = (int)(pool - progress.Xp);
            return gained;
        }
    }
}
