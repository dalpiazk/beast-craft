using System.Collections.Generic;
using BeastCraft.Battle;
using UnityEngine;

namespace BeastCraft.Avatar
{
    /// <summary>
    /// Authored definition of a piece of gear the player avatar equips to raise its stats.
    /// <para>
    /// <strong>Never rendered.</strong> Avatar gear is pure stats: it has no mesh, sprite layer or
    /// any other visual field, and equipping it changes nothing about how the avatar looks. The
    /// avatar's appearance lives entirely in the customization system
    /// (<c>AvatarCustomizationSchema</c>), which stays purely cosmetic and grants no stats. The
    /// two are independent — neither reads the other.
    /// </para>
    /// <para>
    /// <strong>A separate type from the beasts' <see cref="GearSO"/>, deliberately.</strong> The
    /// modifier data is the same shape (a <see cref="StatModifier"/> list, assembled by
    /// <see cref="StatCalculator"/>), but a distinct asset type and slot enum mean an equipment
    /// field typed for one can never be handed the other, so beast gear and avatar gear cannot be
    /// cross-equipped by construction rather than by a runtime check.
    /// </para>
    /// <para>
    /// There is no minimum-level field because the avatar has no level yet; avatar progression is
    /// an open design question. Nothing reads the resulting stats yet either — see
    /// <see cref="BattleAvatar"/>.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Avatar/Avatar Gear", fileName = "NewAvatarGear")]
    public class AvatarGearSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string AvatarGearId;

        /// <summary>Player-facing gear name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Inventory icon. The only art this asset carries; it is never drawn on the avatar.</summary>
        public Sprite Icon;

        /// <summary>Which avatar slot this occupies.</summary>
        public AvatarGearSlot Slot = AvatarGearSlot.Weapon;

        /// <summary>Stat adjustments granted to the avatar while equipped.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        // Plain int rather than an enum, matching GearSO: rarity tiers are a live-balance concern.
        /// <summary>Rarity tier, 0 = common and upward.</summary>
        public int Rarity;
    }
}
