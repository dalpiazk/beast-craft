namespace BeastCraft.Avatar
{
    /// <summary>
    /// Equipment slots on the player avatar, for <see cref="AvatarGearSO"/>. A separate enum from
    /// the beasts' <c>GearSlot</c> on purpose: the avatar is a person, not a creature, so the names
    /// read as ordinary equipment, and keeping the two slot sets apart is part of what stops beast
    /// gear and avatar gear from being cross-equipped.
    /// <para>
    /// These slots hold stat gear only. They have nothing to do with what the avatar looks like —
    /// appearance is the customization system's job (<c>AvatarCustomizationSchema</c>), and none of
    /// that is statful.
    /// </para>
    /// <para>
    /// Values are explicit and persisted in save data: append new slots, never renumber.
    /// </para>
    /// </summary>
    public enum AvatarGearSlot
    {
        /// <summary>Offensive piece: a blade, staff, wand or similar.</summary>
        Weapon = 0,

        /// <summary>Defensive piece: armor, robes, a cloak.</summary>
        Armor = 1,

        /// <summary>Utility piece: ring, amulet, charm.</summary>
        Trinket = 2
    }
}
