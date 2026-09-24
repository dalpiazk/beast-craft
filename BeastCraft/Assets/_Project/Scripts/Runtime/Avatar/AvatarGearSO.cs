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
    /// <see cref="MinimumLevel"/> gates equipping on the avatar's own level (<c>AvatarProgress</c>,
    /// <c>GearRules.EquipAvatarGear</c>), as beast gear is gated on the beast's; the stats are not
    /// re-gated in battle (the avatar is never de-levelled). The resulting stats feed the avatar's
    /// damaging skills through the damage formula — see <see cref="BattleAvatar"/>.
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

        /// <summary>Inventory icon. The only art this asset carries; it is never drawn on the avatar. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string Icon;

        /// <summary>Which avatar slot this occupies.</summary>
        public AvatarGearSlot Slot = AvatarGearSlot.Weapon;

        /// <summary>Stat adjustments granted to the avatar while equipped.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        // Plain int rather than an enum, matching GearSO: rarity tiers are a live-balance concern.
        /// <summary>Rarity tier, 0 = common and upward.</summary>
        public int Rarity;

        /// <summary>Minimum avatar level required to equip this (its gear band's floor). 1 = no gate.</summary>
        public int MinimumLevel = 1;
    }
}
