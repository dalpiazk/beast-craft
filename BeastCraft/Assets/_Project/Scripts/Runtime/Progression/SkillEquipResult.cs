namespace BeastCraft.Progression
{
    /// <summary>
    /// How a <see cref="SkillBook.Equip"/> attempt ended, on any skill book. Only
    /// <see cref="Equipped"/> changes the book.
    /// <para>
    /// Values are explicit and may be persisted or logged by number: append new results at the
    /// end with a new value, never renumber.
    /// </para>
    /// </summary>
    public enum SkillEquipResult
    {
        /// <summary>The skill is now in the slot (including when it already was).</summary>
        Equipped = 0,

        /// <summary>The slot index is outside [0, <see cref="SkillBook.SlotCount"/>) of the book.</summary>
        SlotOutOfRange = 1,

        /// <summary>The skill id is null, empty, or not one the book has learned.</summary>
        UnknownSkill = 2,

        /// <summary>The skill is already equipped in a different slot; a skill occupies at most one.</summary>
        AlreadyEquipped = 3,
    }
}
