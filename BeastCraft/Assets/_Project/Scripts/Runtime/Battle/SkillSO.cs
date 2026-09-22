using System.Collections.Generic;
using UnityEngine;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Authored definition of a creature skill used in tactical grid battles.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Battle/Skill", fileName = "NewSkill")]
    public class SkillSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string SkillId;

        /// <summary>Player-facing skill name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Icon shown in the battle action bar and skill lists.</summary>
        public Sprite Icon;

        /// <summary>
        /// Cost in the per-creature battle resource. The resource's name ("mana" / "focus" /
        /// "stamina") is a design decision still open, so this stays an unqualified int.
        /// </summary>
        public int ResourceCost;

        /// <summary>Turns that must pass before this skill can be used again. 0 means no cooldown.</summary>
        public int Cooldown;

        /// <summary>The footprint this skill covers.</summary>
        public SkillTargetShape TargetShape = SkillTargetShape.SingleTarget;

        // TODO: Range and TargetShape semantics (tile distance metric, whether Range gates the
        // origin tile or the whole footprint) are finalized alongside the battle-system design doc,
        // which still has open questions on grid topology, party size and action economy.
        /// <summary>Reach in abstract grid steps from the caster.</summary>
        public int Range = 1;

        /// <summary>Everything this skill applies to each affected unit.</summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();
    }
}
