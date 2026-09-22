using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Avatar
{
    /// <summary>
    /// The player avatar's authored base stats, before any <see cref="AvatarGearSO"/> is applied.
    /// Pass <see cref="BaseStats"/> to <c>BattleAvatar.Create</c> together with the equipped avatar
    /// gear.
    /// <para>
    /// A flat block with no growth curve: the avatar has no level, and avatar progression is an
    /// open design question rather than something this asset pre-empts. <see cref="StatBlock.MoveRange"/>
    /// and <see cref="StatBlock.Speed"/> are carried for completeness but mean nothing for a
    /// participant that is off the grid and takes no initiative turn.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Avatar/Avatar Stats", fileName = "AvatarStats")]
    public class AvatarStatsSO : ScriptableObject
    {
        /// <summary>The avatar's stats with nothing equipped.</summary>
        public StatBlock BaseStats;
    }
}
