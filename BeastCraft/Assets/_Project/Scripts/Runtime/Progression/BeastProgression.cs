using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The rules of a beast's own level: its XP curve and what a battle pays each beast that was
    /// fielded. Deliberately its own constants, parallel to but not coupled with
    /// <see cref="AvatarProgression"/>. Applied after a battle by <c>BattleSession.ApplyRewards</c>;
    /// paced in <c>docs/balance/pacing-report.md</c>, "Beast level".
    /// <para>
    /// <strong>Autonomous default, pending producer review.</strong> These numbers were chosen
    /// without a design brief, to one target: a beast fielded in every battle levels alongside the
    /// encounters, its median level within 3 of the encounter level at every checkpoint of the
    /// pacing campaign (balance simulator <c>--mode pacing</c>, "Beast level"), as the avatar does.
    /// All tunable; nothing else depends on the exact values.
    /// </para>
    /// <para>
    /// <strong>Who is paid.</strong> Every beast fielded in a finished battle earns
    /// <see cref="ParticipationXp"/>, won or lost, and also when it was knocked out. On a
    /// <see cref="BattleOutcome.PlayerVictory"/>, a fielded beast still standing at the end also
    /// earns the clear bonus <c>ClearBaseXp + ClearXpPerEnemyLevel × enemyLevel</c>; a beast knocked
    /// out before the end earns participation only. Beasts left on the bench earn nothing — the
    /// caller simply does not award them.
    /// </para>
    /// <para>
    /// <strong>The curve.</strong> A level costs <c>XpCurveBase + XpCurvePerLevel × level</c>. At an
    /// encounter level <c>L</c>, an 80% clear rate and a beast knocked out in 20% of won battles, a
    /// battle pays about <c>6 + 0.64 × (40 + 4 L) ≈ 31.6 + 2.56 L</c>, so the campaign's five battles
    /// per encounter level pay about <c>158 + 12.8 L</c> against a cost of <c>160 + 13 L</c>, so the
    /// beast keeps pace with the encounters without running away from them (pacing report: median
    /// exactly on the encounter level at every checkpoint, p10-p90 within 3). Fighting below one's
    /// level pays less, so grinding easy content is slow.
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
        public const int ClearBaseXp = 40;

        /// <summary>The clear bonus per enemy level.</summary>
        public const int ClearXpPerEnemyLevel = 4;

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

        /// <summary>Credits one finished battle to a fielded beast (<see cref="BattleXp"/>). Returns the levels gained.</summary>
        public static int AwardBattle(BeastProgress progress, BattleOutcome outcome, int enemyLevel, bool knockedOut)
        {
            return AddXp(progress, BattleXp(outcome, enemyLevel, knockedOut));
        }

        /// <summary>
        /// Adds XP (negative read as 0) and levels up while the bank covers the next level, up to
        /// <see cref="MaxLevel"/>, where the bank is held at 0. Returns the levels gained.
        /// </summary>
        public static int AddXp(BeastProgress progress, int xp)
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
