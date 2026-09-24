using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The rules of the avatar's own level: its XP curve and what a battle pays. Deliberately its
    /// own constants, not coupled to <see cref="SkillProgression"/>. See the battle-system design
    /// doc, "Avatar level".
    /// <para>
    /// <strong>Tuned so the avatar levels alongside the encounters.</strong> A battle pays
    /// <see cref="ParticipationXp"/> win or lose, plus <c>ClearBaseXp + ClearXpPerEnemyLevel ×
    /// enemyLevel</c> on a clear (raised from <c>40 + 4 L</c> to <c>50 + 5 L</c> with the beasts' for
    /// the region campaign). At an encounter level <c>L</c> and the campaign's ~70% clear rate that
    /// is about <c>43 + 3.5 L</c> a battle; a level costs <c>XpCurveBase + XpCurvePerLevel × level</c>
    /// (<c>200 + 16 L</c>), so the avatar gains a level about every 4.6 battles and arrives at each
    /// gate and boss on its level (balance simulator <c>--mode campaign</c>; <c>--mode pacing</c>,
    /// "Avatar level", tracks one level above its encounters). Fighting below one's level pays less
    /// (the level-gap falloff, <see cref="LevelGapXp"/>: nothing from five levels down), so grinding
    /// easy content is slow. There is no avatar level cap. Tunable starting defaults, not confirmed
    /// balance.
    /// </para>
    /// <para>
    /// Non-throwing: a null progress is a no-op; level and XP are normalized on every write.
    /// </para>
    /// </summary>
    public static class AvatarProgression
    {
        /// <summary>The avatar's highest level (the medium growth curve's max level).</summary>
        public const int MaxLevel = 100;

        /// <summary>The flat part of a level's cost.</summary>
        public const int XpCurveBase = 200;

        /// <summary>A level's cost grows by this much per level.</summary>
        public const int XpCurvePerLevel = 16;

        /// <summary>XP for taking part in any finished battle, won or not.</summary>
        public const int ParticipationXp = 8;

        /// <summary>The flat part of the clear bonus (paid only on <see cref="BattleOutcome.PlayerVictory"/>).</summary>
        public const int ClearBaseXp = 50;

        /// <summary>The clear bonus per enemy level.</summary>
        public const int ClearXpPerEnemyLevel = 5;

        /// <summary>
        /// XP from <paramref name="level"/> to the next: <c>XpCurveBase + XpCurvePerLevel × level</c>,
        /// a level below 1 read as 1. 216 at level 1, 1,784 at level 99.
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
        /// What one battle pays: <see cref="ParticipationXp"/> for any outcome, plus the clear bonus
        /// <c>ClearBaseXp + ClearXpPerEnemyLevel × enemyLevel</c> on a
        /// <see cref="BattleOutcome.PlayerVictory"/> (an enemy level below 1 read as 1).
        /// </summary>
        public static int BattleXp(BattleOutcome outcome, int enemyLevel)
        {
            if (outcome != BattleOutcome.PlayerVictory)
            {
                return ParticipationXp;
            }

            int level = enemyLevel < 1 ? 1 : enemyLevel;
            return ParticipationXp + ClearBaseXp + (ClearXpPerEnemyLevel * level);
        }

        /// <summary>
        /// What one battle pays an avatar of <paramref name="avatarLevel"/>:
        /// <see cref="BattleXp(BattleOutcome, int)"/> after the level-gap falloff
        /// (<see cref="LevelGapXp"/>, on <paramref name="avatarLevel"/> − <paramref name="enemyLevel"/>).
        /// </summary>
        public static int BattleXp(BattleOutcome outcome, int enemyLevel, int avatarLevel)
        {
            return LevelGapXp.Apply(BattleXp(outcome, enemyLevel), LevelGapXp.Gap(avatarLevel, enemyLevel));
        }

        /// <summary>
        /// Credits one finished battle: <see cref="BattleXp(BattleOutcome, int, int)"/> at the
        /// avatar's level before the award (the avatar has no level cap). Returns the levels gained.
        /// </summary>
        public static int AwardBattle(AvatarProgress progress, BattleOutcome outcome, int enemyLevel)
        {
            if (progress == null)
            {
                return 0;
            }

            return AddXp(progress, BattleXp(outcome, enemyLevel, progress.Level));
        }

        /// <summary>
        /// Adds XP (negative read as 0) and levels up while the bank covers the next level, up to
        /// <see cref="MaxLevel"/>, where the bank is held at 0. Returns the levels gained.
        /// </summary>
        public static int AddXp(AvatarProgress progress, int xp)
        {
            if (progress == null)
            {
                return 0;
            }

            progress.Level = progress.Level < 1 ? 1 : progress.Level > MaxLevel ? MaxLevel : progress.Level;
            long bank = (long)(progress.Xp < 0 ? 0 : progress.Xp) + (xp < 0 ? 0 : xp);
            int gained = 0;

            while (progress.Level < MaxLevel && bank >= XpToNextLevel(progress.Level))
            {
                bank -= XpToNextLevel(progress.Level);
                progress.Level++;
                gained++;
            }

            progress.Xp = progress.Level >= MaxLevel ? 0 : (int)bank;
            return gained;
        }
    }
}
