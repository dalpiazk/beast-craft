using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// One beast and the tile it is proposed to start on: the unit of work
    /// <see cref="PlacementValidator.TryPlaceAll"/> commits.
    /// <para>
    /// This exists because <see cref="HexGrid.TryPlaceUnit"/> is keyed by unit id, not by
    /// coordinate — a bare list of tiles says where somebody should stand but not who, so it cannot
    /// be committed to the board. Validation itself needs no ids, which is why
    /// <see cref="PlacementValidator.Validate(HexGrid, BattleTeam, BattleFormat, System.Collections.Generic.IReadOnlyList{HexCoordinate}, System.Collections.Generic.IReadOnlyList{HexCoordinate})"/>
    /// still takes plain coordinates: a placement UI dragging a marker around wants to ask "is this
    /// layout legal" long before it has decided which beast goes in which slot.
    /// </para>
    /// <para>
    /// A <see cref="BattleUnit"/> id rather than the unit itself, matching how
    /// <see cref="HexGrid"/> tracks occupancy throughout: the grid is deliberately free of any
    /// reference to the battle-side unit model, and pre-battle placement runs before those units
    /// are necessarily assembled.
    /// </para>
    /// </summary>
    public readonly struct PlacementRequest
    {
        public PlacementRequest(string unitId, HexCoordinate position)
        {
            UnitId = unitId;
            Position = position;
        }

        /// <summary>
        /// The id the beast will occupy its tile under, matching <see cref="BattleUnit.Id"/>. Null
        /// or empty is a validation failure (<see cref="PlacementStatus.MissingUnitId"/>) rather
        /// than an exception, so a half-filled layout can be checked without blowing up.
        /// </summary>
        public string UnitId { get; }

        /// <summary>The tile this beast is proposed to start the battle on.</summary>
        public HexCoordinate Position { get; }
    }
}
