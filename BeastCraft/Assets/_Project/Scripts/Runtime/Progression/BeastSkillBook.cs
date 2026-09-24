using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Progression
{
    /// <summary>
    /// One beast's skills: every skill it has <em>acquired</em>, each with its own
    /// <see cref="SkillProgress"/>, and the <see cref="EquipSlotCount"/> slots saying which of them
    /// it takes into battle and in what order. See the battle-system design doc, "Skill
    /// progression".
    /// <para>
    /// <strong>Slot order is fire priority.</strong> The equipped slots become the beast's
    /// <see cref="SkillLoadout"/> stack in slot order
    /// (<see cref="BattleUnitFactory.BuildLoadout"/>), and stack order is what sequences skills that
    /// come off cooldown together. <see cref="SkillBook.SwapSlots"/> exists to reorder that priority.
    /// </para>
    /// <para>
    /// <strong>Equip rules</strong> are <see cref="SkillBook"/>'s, shared with the avatar's books:
    /// only a learned skill may be equipped; a skill occupies at most one slot; slots may be empty.
    /// The lineup is meant to be changed between battles; nothing here is read mid-battle, because
    /// the loadout is built once from it.
    /// </para>
    /// <para>
    /// Plain serializable data with public fields (<see cref="SkillBook.Known"/>,
    /// <see cref="SkillBook.Equipped"/>), like the roster DTOs, so a future save system can write it
    /// as-is. Skills are named by their stable <see cref="SkillSO.SkillId"/>; the assets themselves
    /// are resolved through a lookup when a loadout is built. The progression rules are
    /// <see cref="SkillProgression"/>'s. Non-throwing: bad input is reported, not thrown.
    /// </para>
    /// </summary>
    [Serializable]
    public class BeastSkillBook : SkillBook
    {
        /// <summary>How many skills a beast takes into battle. Tunable, but save data sizes to it.</summary>
        public const int EquipSlotCount = 3;

        /// <summary><see cref="EquipSlotCount"/>.</summary>
        public override int SlotCount
        {
            get { return EquipSlotCount; }
        }

        /// <summary><see cref="SkillBook.Learn(string)"/> by the skill's <see cref="SkillSO.SkillId"/>. False for a null skill.</summary>
        public bool Learn(SkillSO skill)
        {
            return skill != null && Learn(skill.SkillId);
        }

        /// <summary>
        /// Learns every <see cref="CreatureSpeciesSO.LearnableSkills"/> entry whose
        /// <see cref="SkillLearnEntry.Level"/> is at most <paramref name="beastLevel"/> and that is
        /// not already known — the species' level-up acquisition source. Other sources (drops,
        /// rewards) call <see cref="Learn(SkillSO)"/> directly. Returns how many were newly learned;
        /// equips nothing.
        /// </summary>
        public int LearnAvailable(CreatureSpeciesSO species, int beastLevel)
        {
            if (species == null || species.LearnableSkills == null)
            {
                return 0;
            }

            int learned = 0;

            for (int i = 0; i < species.LearnableSkills.Count; i++)
            {
                SkillLearnEntry entry = species.LearnableSkills[i];

                if (entry != null && entry.Level <= beastLevel && Learn(entry.Skill))
                {
                    learned++;
                }
            }

            return learned;
        }

        /// <summary>
        /// Credits practice XP after a battle: for every <paramref name="usesBySkillId"/> entry this
        /// book knows, <see cref="SkillProgression.AwardPractice"/> with the skill's
        /// <see cref="SkillSO.Progression"/> from <paramref name="skillLookup"/>. Skills the book does
        /// not know are ignored; a skill the lookup cannot resolve uses the default definition.
        /// Typically fed one unit's counts from <see cref="BattleSkillUsage.CountFiredSkillsFor"/>.
        /// Returns the total levels gained across all skills.
        /// </summary>
        public int AwardPractice(IReadOnlyDictionary<string, int> usesBySkillId, Func<string, SkillSO> skillLookup)
        {
            return base.AwardPractice(usesBySkillId, SkillProgressionLookup(skillLookup));
        }

        /// <summary>
        /// Adapts a skill lookup into the definition lookup <see cref="SkillBook.AwardPractice(IReadOnlyDictionary{string, int}, Func{string, SkillProgressionDefinition})"/>
        /// takes: the resolved skill's <see cref="SkillSO.Progression"/>, or <c>null</c> (the
        /// defaults) when the lookup is null or cannot resolve the id.
        /// </summary>
        internal static Func<string, SkillProgressionDefinition> SkillProgressionLookup(Func<string, SkillSO> skillLookup)
        {
            if (skillLookup == null)
            {
                return null;
            }

            return id =>
            {
                SkillSO skill = skillLookup(id);
                return skill == null ? null : skill.Progression;
            };
        }
    }
}
