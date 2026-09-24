using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Creatures;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The arena fit dry run shared by the encounter validators, the encounter generator and the
    /// balance simulator: whether a lineup of enemies seats on an arena's enemy deployment zone,
    /// and the front-to-back order a generated lineup is placed in (<see cref="ComparePlacement"/>),
    /// so the generator and <see cref="EncounterLibraryValidator"/>'s worst-case check pack in the
    /// same order.
    /// </summary>
    public static class EncounterFit
    {
        /// <summary>
        /// Whether enemies of these footprints, packed in this order the way every battle packs them
        /// (<see cref="DeploymentPacker.TryPack"/>), all fit an empty board's enemy zone. A seven-tile
        /// enemy never fits a Small board (its zone is two rows deep).
        /// </summary>
        public static bool Fits(ArenaSize arena, IReadOnlyList<UnitFootprint> footprints)
        {
            return DeploymentPacker.TryPack(new HexGrid(arena), BattleTeam.Enemy, footprints, new List<HexCoordinate>(), new List<HexCoordinate>());
        }

        /// <summary>Front-to-back placement rank: melee screen first (Vanguard 0, Skirmisher 1), ranged at the back (2).</summary>
        public static int PlacementRank(CombatStance stance)
        {
            switch (stance)
            {
                case CombatStance.Vanguard:
                    return 0;
                case CombatStance.Skirmisher:
                    return 1;
                default:
                    return 2;
            }
        }

        /// <summary>
        /// The order a generated lineup's enemy types are placed in (<see cref="EncounterGenerator"/>):
        /// by <see cref="PlacementRank"/>, then by index in the enemy library. Negative when type A
        /// is placed before type B. Every unit of a type is placed together, in this type order.
        /// </summary>
        public static int ComparePlacement(CombatStance stanceA, int libraryIndexA, CombatStance stanceB, int libraryIndexB)
        {
            int byStance = PlacementRank(stanceA).CompareTo(PlacementRank(stanceB));
            return byStance != 0 ? byStance : libraryIndexA.CompareTo(libraryIndexB);
        }
    }
}
