namespace BeastCraft.Battle
{
    /// <summary>
    /// Equipment slots on a creature, for <see cref="GearSO"/>. The names are worded to read
    /// sensibly for a beast as well as for conventional equipment. The player avatar does not use
    /// these: its stat gear has its own slot set, <c>BeastCraft.Avatar.AvatarGearSlot</c>, and its
    /// cosmetics (which carry no stats) live in the customization system.
    /// </summary>
    public enum GearSlot
    {
        /// <summary>Offensive piece: a weapon, or a creature-borne power core / focus.</summary>
        WeaponOrCore = 0,

        /// <summary>Defensive piece: armor, or a creature's barding / shell plating.</summary>
        ArmorOrShell = 1,

        /// <summary>Utility piece: charm, collar, trinket.</summary>
        Accessory = 2
    }
}
