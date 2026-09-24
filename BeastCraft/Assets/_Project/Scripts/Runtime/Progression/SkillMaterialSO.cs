using UnityEngine;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Authored definition of a skill-training material: a rarer item that feeds XP straight into
    /// a skill (<see cref="SkillProgression.ApplyMaterial"/>) and is what opens a breakthrough gate
    /// (<see cref="SkillProgression.TryBreakthrough"/>).
    /// <para>
    /// Only the definition. Inventory, drops and consuming a stack are a later pass: the progression
    /// functions report whether a material was used, and the caller removes it.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Progression/Skill Material", fileName = "NewSkillMaterial")]
    public class SkillMaterialSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string MaterialId;

        /// <summary>Player-facing material name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Icon shown in inventory and skill-training screens. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string Icon;

        /// <summary>
        /// Rarity tier, 1 upward. A breakthrough gate passes with any material whose tier is at
        /// least its <see cref="SkillTierDefinition.RequiredMaterialTier"/>.
        /// </summary>
        public int Tier = 1;

        /// <summary>
        /// XP this material adds when fed to a skill with <see cref="SkillProgression.ApplyMaterial"/>.
        /// A negative value is read as 0. A breakthrough does not add it.
        /// </summary>
        public int XpValue = 100;
    }
}
