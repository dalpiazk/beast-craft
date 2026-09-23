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
    /// come off cooldown together. <see cref="SwapSlots"/> exists to reorder that priority.
    /// </para>
    /// <para>
    /// <strong>Equip rules.</strong> Only a learned skill may be equipped; a skill occupies at most
    /// one slot; slots may be empty (an empty slot is <c>null</c> or <c>""</c> — Unity's
    /// serializer writes null strings back as empty ones). The lineup is meant to be changed
    /// between battles; nothing here is read mid-battle, because the loadout is built once from it.
    /// </para>
    /// <para>
    /// Plain serializable data with public fields, like the roster DTOs, so a future save system
    /// can write it as-is. Skills are named by their stable <see cref="SkillSO.SkillId"/>; the
    /// assets themselves are resolved through a lookup when a loadout is built. The progression
    /// rules are <see cref="SkillProgression"/>'s; this type keeps the book and applies the
    /// equip rules. Non-throwing: bad input is reported, not thrown.
    /// </para>
    /// </summary>
    [Serializable]
    public class BeastSkillBook
    {
        /// <summary>How many skills a beast takes into battle. Tunable, but save data sizes to it.</summary>
        public const int EquipSlotCount = 3;

        /// <summary>
        /// Every skill this beast has acquired, one entry per skill id, in the order learned. A
        /// skill is never forgotten by anything in this type.
        /// </summary>
        public List<SkillProgress> Known = new List<SkillProgress>();

        /// <summary>
        /// The equipped skill ids by slot, <see cref="EquipSlotCount"/> long; slot 0 fires first.
        /// Null or empty entries are empty slots. Resized to <see cref="EquipSlotCount"/> on the
        /// next write if loaded at another length (entries past the end are dropped).
        /// </summary>
        public string[] Equipped = new string[EquipSlotCount];

        /// <summary>Whether <paramref name="skillId"/> has been learned.</summary>
        public bool Knows(string skillId)
        {
            return GetProgress(skillId) != null;
        }

        /// <summary>The progress entry for <paramref name="skillId"/>, or <c>null</c> when it is not learned.</summary>
        public SkillProgress GetProgress(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || Known == null)
            {
                return null;
            }

            for (int i = 0; i < Known.Count; i++)
            {
                if (Known[i] != null && string.Equals(Known[i].SkillId, skillId, StringComparison.Ordinal))
                {
                    return Known[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Acquires a skill at level 1, tier 0. False — and no change — for a null or empty id or
        /// one already known; learning never resets an existing skill's progress.
        /// </summary>
        public bool Learn(string skillId)
        {
            if (string.IsNullOrEmpty(skillId) || Knows(skillId))
            {
                return false;
            }

            if (Known == null)
            {
                Known = new List<SkillProgress>();
            }

            Known.Add(new SkillProgress(skillId));
            return true;
        }

        /// <summary><see cref="Learn(string)"/> by the skill's <see cref="SkillSO.SkillId"/>. False for a null skill.</summary>
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
        /// The skill id in <paramref name="slotIndex"/>, or <c>null</c> when the slot is empty or out
        /// of range. An empty string is reported as <c>null</c>.
        /// </summary>
        public string GetEquipped(int slotIndex)
        {
            if (Equipped == null || slotIndex < 0 || slotIndex >= EquipSlotCount || slotIndex >= Equipped.Length)
            {
                return null;
            }

            return string.IsNullOrEmpty(Equipped[slotIndex]) ? null : Equipped[slotIndex];
        }

        /// <summary>The slot <paramref name="skillId"/> is equipped in, or -1 when it is not equipped.</summary>
        public int IndexOfEquipped(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                return -1;
            }

            for (int i = 0; i < EquipSlotCount; i++)
            {
                if (string.Equals(GetEquipped(i), skillId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Puts <paramref name="skillId"/> into <paramref name="slotIndex"/>, replacing whatever was
        /// there. Refused for an out-of-range slot, an unknown skill, or a skill already equipped in
        /// another slot (move it with <see cref="SwapSlots"/>, or unequip it first). Equipping a
        /// skill into the slot it already occupies is a successful no-op.
        /// </summary>
        public SkillEquipResult Equip(int slotIndex, string skillId)
        {
            if (slotIndex < 0 || slotIndex >= EquipSlotCount)
            {
                return SkillEquipResult.SlotOutOfRange;
            }

            if (!Knows(skillId))
            {
                return SkillEquipResult.UnknownSkill;
            }

            int current = IndexOfEquipped(skillId);

            if (current >= 0 && current != slotIndex)
            {
                return SkillEquipResult.AlreadyEquipped;
            }

            EnsureSlots();
            Equipped[slotIndex] = skillId;
            return SkillEquipResult.Equipped;
        }

        /// <summary>Empties <paramref name="slotIndex"/>. False when it is out of range or already empty.</summary>
        public bool Unequip(int slotIndex)
        {
            if (GetEquipped(slotIndex) == null)
            {
                return false;
            }

            EnsureSlots();
            Equipped[slotIndex] = null;
            return true;
        }

        /// <summary>
        /// Exchanges two slots' contents (either may be empty), which reorders fire priority. False
        /// when either index is out of range.
        /// </summary>
        public bool SwapSlots(int first, int second)
        {
            if (first < 0 || first >= EquipSlotCount || second < 0 || second >= EquipSlotCount)
            {
                return false;
            }

            EnsureSlots();
            string held = Equipped[first];
            Equipped[first] = Equipped[second];
            Equipped[second] = held;
            return true;
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
            if (usesBySkillId == null)
            {
                return 0;
            }

            int gained = 0;

            foreach (KeyValuePair<string, int> entry in usesBySkillId)
            {
                SkillProgress progress = GetProgress(entry.Key);

                if (progress == null)
                {
                    continue;
                }

                SkillSO skill = skillLookup == null ? null : skillLookup(entry.Key);
                gained += SkillProgression.AwardPractice(progress, skill == null ? null : skill.Progression, entry.Value);
            }

            return gained;
        }

        /// <summary>Makes <see cref="Equipped"/> exactly <see cref="EquipSlotCount"/> long, keeping what fits.</summary>
        private void EnsureSlots()
        {
            if (Equipped != null && Equipped.Length == EquipSlotCount)
            {
                return;
            }

            string[] resized = new string[EquipSlotCount];

            if (Equipped != null)
            {
                Array.Copy(Equipped, resized, Math.Min(Equipped.Length, EquipSlotCount));
            }

            Equipped = resized;
        }
    }
}
