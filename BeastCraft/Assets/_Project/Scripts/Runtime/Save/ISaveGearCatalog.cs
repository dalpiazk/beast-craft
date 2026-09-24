using BeastCraft.Avatar;
using BeastCraft.Battle;

namespace BeastCraft.Save
{
    /// <summary>
    /// The gear definitions a save's gear may refer to, with what the equip rules need to know
    /// about each: its slot and, for beast gear, its minimum level. Used by <see cref="SaveValidator"/>
    /// (and <see cref="SaveSerializer"/>); the game's <c>BattleContent</c> implements it.
    /// </summary>
    public interface ISaveGearCatalog
    {
        /// <summary>Whether <paramref name="gearId"/> is a known <c>GearSO.GearId</c>, and if so its slot and minimum level.</summary>
        bool TryGetBeastGear(string gearId, out GearSlot slot, out int minimumLevel);

        /// <summary>Whether <paramref name="gearId"/> is a known <c>AvatarGearSO.AvatarGearId</c>, and if so its slot.</summary>
        bool TryGetAvatarGear(string gearId, out AvatarGearSlot slot);
    }
}
