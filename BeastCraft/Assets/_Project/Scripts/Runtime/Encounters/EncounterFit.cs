using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The arena fit dry run shared by the encounter validators, the encounter generator and the
    /// balance simulator: whether a lineup of enemies seats on an arena's enemy deployment zone.
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
    }
}
