using System.Collections.Generic;
using UnityEngine;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Authored definition of an equippable piece of beast gear. Gear equips onto creatures and
    /// affects their combat stats.
    /// <para>
    /// Creature-only. The player avatar has its own stat gear, <c>BeastCraft.Avatar.AvatarGearSO</c>,
    /// a separate type with its own slots so the two can never be cross-equipped. Neither kind of
    /// gear has anything to do with the avatar's appearance: avatar cosmetics live in the
    /// customization system and carry no stats.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Battle/Gear", fileName = "NewGear")]
    public class GearSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string GearId;

        /// <summary>Player-facing gear name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Inventory icon.</summary>
        public Sprite Icon;

        /// <summary>Which creature slot this occupies.</summary>
        public GearSlot Slot = GearSlot.WeaponOrCore;

        /// <summary>Stat adjustments granted while equipped.</summary>
        public List<StatModifier> Modifiers = new List<StatModifier>();

        // Plain int rather than an enum: rarity tiers are a live-balance concern, and adding a tier
        // should not require a code change.
        /// <summary>Rarity tier, 0 = common and upward.</summary>
        public int Rarity;

        /// <summary>Minimum creature level required to equip this.</summary>
        public int MinimumLevel = 1;
    }
}
