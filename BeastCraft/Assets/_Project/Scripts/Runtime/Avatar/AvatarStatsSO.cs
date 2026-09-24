using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Avatar
{
    /// <summary>
    /// The player avatar's authored base stats, before any <see cref="AvatarGearSO"/> is applied.
    /// Pass <see cref="BaseStats"/> to <c>BattleAvatar.Create</c> together with the equipped avatar
    /// gear.
    /// <para>
    /// A flat block with no growth curve: the avatar has no progression level, and avatar
    /// progression is an open design question rather than something this asset pre-empts. (The
    /// damage formula's caster level for the avatar is a per-battle input to
    /// <c>BattleAvatar.Create</c>, not something stored here.) <see cref="StatBlock.MoveRange"/>
    /// and <see cref="StatBlock.Speed"/> are carried for completeness but mean nothing for a
    /// participant that is off the grid and takes no initiative turn. <see cref="StatBlock.CritChance"/>
    /// does apply: the avatar's damage effects roll crits off it like a beast's (0 by default, so
    /// an avatar crits only if its base or its gear gives it a chance).
    /// </para>
    /// <para>
    /// Namespace note: <c>BeastCraft.Avatar</c> hides <c>UnityEngine.Avatar</c> (Mecanim's rig
    /// avatar) for any code inside <c>BeastCraft.*</c>, because the simple name <c>Avatar</c>
    /// resolves to this namespace before the <c>using UnityEngine;</c> type. Code there that needs
    /// Mecanim's type must write <c>UnityEngine.Avatar</c> fully qualified.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Avatar/Avatar Stats", fileName = "AvatarStats")]
    public class AvatarStatsSO : ScriptableObject
    {
        /// <summary>The avatar's stats with nothing equipped.</summary>
        public StatBlock BaseStats;

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

            return Mathf.RoundToInt(baseStat * Growth.GetScaleAtLevel(level));
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
