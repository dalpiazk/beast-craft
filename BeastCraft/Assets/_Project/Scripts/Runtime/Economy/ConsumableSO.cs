using System.Collections.Generic;
using BeastCraft.Battle;
using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>
    /// A battle item (a draught, a salve, a bomb): used from the pack as the battle begins (at most
    /// <see cref="ConsumableLoadout.MaxPerBattle"/> per battle), spent whatever the outcome.
    /// Created from <c>consumable-library.json</c> by <see cref="ConsumableLibrary"/> (and the
    /// Editor importer). Its effects are ordinary <see cref="SkillEffect"/>s, applied through
    /// <see cref="SkillEffectApplier"/> by <see cref="ConsumableLoadout"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Economy/Consumable", fileName = "NewConsumable")]
    public class ConsumableSO : ScriptableObject
    {
        /// <summary>Stable id persisted in save data. Never rename after ship.</summary>
        public string ConsumableId;

        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Inventory icon. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string Icon;

        /// <summary>Rarity tier, 0 = common and upward.</summary>
        public int Rarity;

        /// <summary>The first region (1-based) whose Traders stock it.</summary>
        public int MinRegion = 1;

        /// <summary>The most of it the pack holds.</summary>
        public int MaxStack = ConsumableLibraryValidator.DefaultMaxStack;

        /// <summary>Who its effects land on.</summary>
        public ConsumableRecipients Recipients = ConsumableRecipients.Team;

        /// <summary>The effects, as a skill's.</summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();
    }
}
