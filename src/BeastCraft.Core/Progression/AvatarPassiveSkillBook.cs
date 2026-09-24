using System;
using BeastCraft.Avatar;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The avatar's passive skills: every one it has acquired, with its progress, and the
    /// <see cref="AvatarSkillBook.PassiveSlotCount"/> slots it takes into battle (slot order is the
    /// order passives are evaluated in when several share a trigger). Passives are
    /// <see cref="PassiveSkillSO"/>s, named by <see cref="PassiveSkillSO.PassiveId"/>, and progress
    /// on exactly the rules beast skills do (<see cref="SkillProgression"/>, with the passive's own
    /// <see cref="PassiveSkillSO.Progression"/>). All equip and practice rules are
    /// <see cref="SkillBook"/>'s. Held inside an <see cref="AvatarSkillBook"/>.
    /// </summary>
    [Serializable]
    public class AvatarPassiveSkillBook : SkillBook
    {
        /// <summary><see cref="AvatarSkillBook.PassiveSlotCount"/>.</summary>
        public override int SlotCount
        {
            get { return AvatarSkillBook.PassiveSlotCount; }
        }

        /// <summary><see cref="SkillBook.Learn(string)"/> by the passive's <see cref="PassiveSkillSO.PassiveId"/>. False for a null passive.</summary>
        public bool Learn(PassiveSkillSO passive)
        {
            return passive != null && Learn(passive.PassiveId);
        }
    }
}
