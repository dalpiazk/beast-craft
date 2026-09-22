using System;
using BeastCraft.Battle;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// A skill a species learns, and the level at which it becomes available.
    /// </summary>
    [Serializable]
    public class SkillLearnEntry
    {
        /// <summary>Level at which the skill is learned.</summary>
        public int Level = 1;

        /// <summary>The skill learned at that level.</summary>
        public SkillSO Skill;
    }
}
