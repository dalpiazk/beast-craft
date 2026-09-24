using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Where one owner's copy of one skill has got to: its level, the XP banked toward the next
    /// level, and how many breakthrough gates it has passed.
    /// <para>
    /// Plain serializable data with public fields and no Unity-object references, like the roster
    /// DTOs, so a future save system can write it as-is (<c>JsonUtility</c> in Unity,
    /// <c>System.Text.Json</c> with <c>IncludeFields = true</c> outside it). The skill is named by
    /// its stable id rather than referenced, for the same reason. All of the rules live in
    /// <see cref="SkillProgression"/>; this type only holds the numbers.
    /// </para>
    /// </summary>
    [Serializable]
    public class SkillProgress
    {
        /// <summary>Parameterless, for serializers. Level 1, no XP, tier 0, no id.</summary>
        public SkillProgress()
        {
        }

        /// <summary>A freshly learned skill: level 1, no XP, tier 0.</summary>
        public SkillProgress(string skillId)
        {
            SkillId = skillId;
        }

        /// <summary>
        /// The <see cref="Battle.SkillSO.SkillId"/> (or other skill definition's stable id) this
        /// entry tracks. Persisted in save data, so it follows the skill id's never-rename rule.
        /// </summary>
        public string SkillId;

        /// <summary>Current level, 1-based. Read through the definition's clamp.</summary>
        public int Level = 1;

        /// <summary>
        /// XP banked toward the next level. Always below
        /// <see cref="SkillProgression.XpToNextLevel"/> of <see cref="Level"/> except at a
        /// breakthrough gate, where it may equal it (a full bank waiting for the gate to open), and
        /// 0 at the max level.
        /// </summary>
        public int Xp;

        /// <summary>
        /// How many breakthrough gates (<see cref="SkillProgressionDefinition.Tiers"/>) have been
        /// passed. 0 for a freshly learned skill.
        /// </summary>
        public int Tier;
    }
}
