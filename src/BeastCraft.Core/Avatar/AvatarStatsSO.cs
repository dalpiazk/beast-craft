using BeastCraft.Creatures;

namespace BeastCraft.Avatar
{
    /// <summary>
    /// The player avatar's authored base stats and their growth with the avatar's level, before any
    /// <see cref="AvatarGearSO"/> is applied. The battle setup hands
    /// <see cref="GetStatsAtLevel"/> of the avatar's level (<c>AvatarProgress.Level</c>) to
    /// <c>BattleAvatar.Create</c> together with the equipped avatar gear (the
    /// <c>BattleAvatar.Create</c> overload that takes this asset does exactly that).
    /// <para>
    /// <strong>Level and growth.</strong> <see cref="BaseStats"/> are the max-level values and
    /// <see cref="Growth"/> scales them down to the avatar's level exactly as a species' curve scales
    /// a beast's (<see cref="GetStatAtLevel"/>); with no curve the block is flat at every level. The
    /// avatar's level is also its caster level in the damage formula.
    /// </para>
    /// <para>
    /// <strong>Speed matters.</strong> The avatar fills an ATB gauge of its own from its Speed (it is
    /// in the turn order, though never a target), so Speed sets how often its actives and passive
    /// cooldowns come round. It defaults to <see cref="DefaultSpeed"/> (the turn order's reference
    /// Speed, the middle of the roster's 88-110 band) and scales with level like the rest.
    /// <see cref="StatBlock.MoveRange"/> is carried for completeness but means nothing for a
    /// participant that is off the grid. <see cref="StatBlock.CritChance"/> does apply: the avatar's
    /// damage effects roll crits off it like a beast's (0 by default, so an avatar crits only if its
    /// base or its gear gives it a chance).
    /// </para>
    /// </summary>
    public class AvatarStatsSO : ContentAsset
    {
        /// <summary>
        /// The avatar's default max-level Speed: 100, the turn order's reference Speed
        /// (<c>TurnManager.ReferenceSpeed</c>), so its gauge keeps pace with an average beast.
        /// </summary>
        public const int DefaultSpeed = 100;

        /// <summary>
        /// The avatar's stats with nothing equipped, at max level (see <see cref="Growth"/>). A new
        /// asset starts with <see cref="DefaultSpeed"/> and every other stat 0.
        /// </summary>
        public StatBlock BaseStats = new StatBlock(0, 0, 0, 0, 0, DefaultSpeed);

        /// <summary>
        /// How the base scales with the avatar's level (<c>AvatarProgress.Level</c>), exactly as a
        /// species' <see cref="CreatureSpeciesSO.GrowthRate"/> scales its <c>BaseStats</c>:
        /// <see cref="BaseStats"/> are then the max-level values. Null keeps the block flat at every
        /// level (the behaviour before avatar levels existed).
        /// </summary>
        public GrowthRateCurve Growth;

        /// <summary>
        /// One stat at <paramref name="level"/>: <see cref="BaseStats"/> scaled by <see cref="Growth"/>
        /// and rounded, mirroring <see cref="CreatureSpeciesSO.GetStatAtLevel"/>. MoveRange and
        /// CritChance are exempt (small tactical numbers, never scaled); Speed scales like the rest.
        /// With no <see cref="Growth"/> the base is returned unscaled.
        /// </summary>
        public int GetStatAtLevel(StatType type, int level)
        {
            int baseStat = BaseStats.GetStat(type);

            if (Growth == null || type == StatType.MoveRange || type == StatType.CritChance)
            {
                return baseStat;
            }

            return MathUtil.RoundToInt(baseStat * Growth.GetScaleAtLevel(level));
        }

        /// <summary>
        /// Every stat at <paramref name="level"/> (<see cref="GetStatAtLevel"/>): the base block to
        /// hand to <c>BattleAvatar.Create</c> for an avatar of that level, before gear.
        /// </summary>
        public StatBlock GetStatsAtLevel(int level)
        {
            StatBlock stats = BaseStats;

            foreach (StatType type in (StatType[])System.Enum.GetValues(typeof(StatType)))
            {
                stats.SetStat(type, GetStatAtLevel(type, level));
            }

            return stats;
        }
    }
}
