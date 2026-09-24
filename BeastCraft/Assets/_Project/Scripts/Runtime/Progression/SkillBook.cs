using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The shared rules of every skill book: a set of <em>acquired</em> skills, each with its own
    /// <see cref="SkillProgress"/>, and <see cref="SlotCount"/> ordered slots saying which of them
    /// are taken into battle. <see cref="BeastSkillBook"/> (a beast's skills),
    /// <see cref="AvatarActiveSkillBook"/> and <see cref="AvatarPassiveSkillBook"/> (the avatar's)
    /// are thin subclasses that fix the slot count and add their own acquisition helpers; the equip
    /// and practice rules are written once, here. See the battle-system design doc, "Skill
    /// progression" and "Avatar passives".
    /// <para>
    /// <strong>Equip rules.</strong> Only a known id may be equipped; an id occupies at most one
    /// slot; slots may be empty (an empty slot is <c>null</c> or <c>""</c> — Unity's serializer
    /// writes null strings back as empty ones). Slot order is priority: fire order for active
    /// skills, trigger-evaluation order for passives.
    /// </para>
    /// <para>
    /// <strong>Keyed by id only.</strong> Entries are stable string ids (a skill's
    /// <c>SkillId</c>, a passive's <c>PassiveId</c>), never asset references, so the book is plain
    /// serializable save data; the assets are resolved through a lookup when a battle is built.
    /// Non-throwing: bad input is reported, not thrown.
    /// </para>
    /// <para>
    /// Abstract rather than configurable because the slot count is a rule of the book's kind, not
    /// save data: a serializer rebuilds the book through its parameterless constructor, which must
    /// already know how many slots it has.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class SkillBook
    {
        /// <summary>Starts with nothing known and <see cref="SlotCount"/> empty slots.</summary>
        protected SkillBook()
        {
            Equipped = new string[SlotCount];
        }

        /// <summary>How many slots this kind of book has. A constant per subclass.</summary>
        public abstract int SlotCount { get; }

        /// <summary>
        /// Every id this book has acquired, one entry each, in the order learned. Nothing in this
        /// type ever forgets one.
        /// </summary>
        public List<SkillProgress> Known = new List<SkillProgress>();

        /// <summary>
        /// The equipped ids by slot, <see cref="SlotCount"/> long; slot 0 has priority. Null or
        /// empty entries are empty slots. Resized to <see cref="SlotCount"/> on the next write if
        /// loaded at another length (entries past the end are dropped).
        /// </summary>
        public string[] Equipped;

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
        /// Acquires an id at level 1, tier 0. False — and no change — for a null or empty id or
        /// one already known; learning never resets existing progress.
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

        /// <summary>
        /// The id in <paramref name="slotIndex"/>, or <c>null</c> when the slot is empty or out of
        /// range. An empty string is reported as <c>null</c>.
        /// </summary>
        public string GetEquipped(int slotIndex)
        {
            if (Equipped == null || slotIndex < 0 || slotIndex >= SlotCount || slotIndex >= Equipped.Length)
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

            for (int i = 0; i < SlotCount; i++)
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
        /// there. Refused for an out-of-range slot, an unknown id, or an id already equipped in
        /// another slot (move it with <see cref="SwapSlots"/>, or unequip it first). Equipping an
        /// id into the slot it already occupies is a successful no-op.
        /// </summary>
        public SkillEquipResult Equip(int slotIndex, string skillId)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
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
        /// Exchanges two slots' contents (either may be empty), which reorders priority. False when
        /// either index is out of range.
        /// </summary>
        public bool SwapSlots(int first, int second)
        {
            if (first < 0 || first >= SlotCount || second < 0 || second >= SlotCount)
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
        /// Credits practice XP after a battle: for every <paramref name="usesById"/> entry this
        /// book knows, <see cref="SkillProgression.AwardPractice"/> with the definition
        /// <paramref name="definitionLookup"/> returns for that id (a null lookup or a null answer
        /// reads as the default definition). Ids the book does not know are ignored. Returns the
        /// total levels gained across all entries.
        /// </summary>
        public int AwardPractice(IReadOnlyDictionary<string, int> usesById, Func<string, SkillProgressionDefinition> definitionLookup)
        {
            if (usesById == null)
            {
                return 0;
            }

            int gained = 0;

            foreach (KeyValuePair<string, int> entry in usesById)
            {
                SkillProgress progress = GetProgress(entry.Key);

                if (progress == null)
                {
                    continue;
                }

                SkillProgressionDefinition definition = definitionLookup == null ? null : definitionLookup(entry.Key);
                gained += SkillProgression.AwardPractice(progress, definition, entry.Value);
            }

            return gained;
        }

        /// <summary>Makes <see cref="Equipped"/> exactly <see cref="SlotCount"/> long, keeping what fits.</summary>
        private void EnsureSlots()
        {
            int count = SlotCount;

            if (Equipped != null && Equipped.Length == count)
            {
                return;
            }

            string[] resized = new string[count];

            if (Equipped != null)
            {
                Array.Copy(Equipped, resized, Math.Min(Equipped.Length, count));
            }

            Equipped = resized;
        }
    }
}
