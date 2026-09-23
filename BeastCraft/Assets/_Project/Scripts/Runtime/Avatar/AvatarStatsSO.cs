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
    }
}
