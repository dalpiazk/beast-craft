using System;
using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The avatar's active support skills: every one it has acquired, with its progress, and the
    /// <see cref="AvatarSkillBook.ActiveSlotCount"/> slots that become its
    /// <see cref="SkillLoadout"/> (slot order is fire priority, exactly as for a beast). Skills are
    /// ordinary <see cref="SkillSO"/>s, named by <see cref="SkillSO.SkillId"/>, and progress on the
    /// same rules as a beast's. All equip and practice rules are <see cref="SkillBook"/>'s. Held
    /// inside an <see cref="AvatarSkillBook"/>.
    /// </summary>
    [Serializable]
    public class AvatarActiveSkillBook : SkillBook
    {
        /// <summary><see cref="AvatarSkillBook.ActiveSlotCount"/>.</summary>
        public override int SlotCount
        {
            get { return AvatarSkillBook.ActiveSlotCount; }
        }

        /// <summary><see cref="SkillBook.Learn(string)"/> by the skill's <see cref="SkillSO.SkillId"/>. False for a null skill.</summary>
        public bool Learn(SkillSO skill)
        {
            return skill != null && Learn(skill.SkillId);
        }
    }
}
