namespace BeastCraft.Battle
{
    /// <summary>
    /// Equipment slots on a creature. Gear equips onto creatures, not onto the player avatar
    /// (avatar items are purely cosmetic), so the names are worded to read sensibly for a beast as
    /// well as for conventional equipment.
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
