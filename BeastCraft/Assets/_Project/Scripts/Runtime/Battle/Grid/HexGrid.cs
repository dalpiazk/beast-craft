using System;
using System.Collections.Generic;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A hexagon-shaped board of hex tiles, centred on <see cref="HexCoordinate.Zero"/> and sized
    /// by an <see cref="ArenaSize"/> preset. Owns the set of legal tiles and which unit stands on
    /// each of them.
    /// <para>
    /// Scaffolding only: this is the board's state and its bounds/occupancy queries. Movement cost,
    /// pathfinding, line of sight and terrain are all deliberately absent and land in a later pass.
    /// </para>
    /// </summary>
    public class HexGrid
    {
        // Ring radius, in hex steps from the centre tile, for each arena preset.
        //
        // TUNABLE IMPLEMENTATION DEFAULTS, NOT CONFIRMED BALANCE. What the producer confirmed is
        // that there are exactly three presets and that an encounter fixes one of them. These
        // radii are an engineering placeholder, picked so each preset comfortably seats its
        // battle format (1 / up to 4 / up to 6 beasts per side) with room to manoeuvre. Tile
        // count is 1 + 3r(r+1): 37 tiles at r=3, 91 at r=5, 169 at r=7. Expect them to move once
        // encounter design and movement ranges give them something to be balanced against.
        private const int SmallRadius = 3;
        private const int MediumRadius = 5;
        private const int LargeRadius = 7;

        private readonly HashSet<HexCoordinate> _tiles;
        private readonly Dictionary<HexCoordinate, string> _occupantsByTile;
        private readonly Dictionary<string, HexCoordinate> _tilesByOccupant;

        public HexGrid(ArenaSize size)
        {
            Size = size;
            Radius = RadiusFor(size);

            _tiles = new HashSet<HexCoordinate>();
            _occupantsByTile = new Dictionary<HexCoordinate, string>();
            _tilesByOccupant = new Dictionary<string, HexCoordinate>(StringComparer.Ordinal);

            GenerateTiles();
        }

        /// <summary>The preset this board was built from.</summary>
        public ArenaSize Size { get; }

        /// <summary>Distance in hex steps from the centre tile to the outermost ring.</summary>
        public int Radius { get; }

        /// <summary>Total number of legal tiles on the board.</summary>
        public int TileCount
        {
            get { return _tiles.Count; }
        }

        /// <summary>Every legal tile on the board, in no guaranteed order.</summary>
        public IEnumerable<HexCoordinate> Tiles
        {
            get { return _tiles; }
        }

        /// <summary>The radius a given preset maps to. See the radius constants for the caveat.</summary>
        public static int RadiusFor(ArenaSize size)
        {
            switch (size)
            {
                case ArenaSize.Small:
                    return SmallRadius;
                case ArenaSize.Medium:
                    return MediumRadius;
                case ArenaSize.Large:
                    return LargeRadius;
                default:
                    return MediumRadius;
            }
        }

        /// <summary>True when the coordinate names a tile that exists on this board.</summary>
        public bool IsInBounds(HexCoordinate coordinate)
        {
            return _tiles.Contains(coordinate);
        }

        /// <summary>
        /// True when a unit currently stands on this tile. Out-of-bounds tiles are never occupied.
        /// </summary>
        public bool IsOccupied(HexCoordinate coordinate)
        {
            return _occupantsByTile.ContainsKey(coordinate);
        }

        /// <summary>
        /// The id of the unit standing on this tile, or <c>null</c> when the tile is empty or off
        /// the board. Callers are expected to null-check rather than catch.
        /// </summary>
        public string GetOccupant(HexCoordinate coordinate)
        {
            string occupant;
            return _occupantsByTile.TryGetValue(coordinate, out occupant) ? occupant : null;
        }

        /// <summary>The tile a unit stands on, if it is currently placed on this board.</summary>
        public bool TryGetPosition(string unitId, out HexCoordinate coordinate)
        {
            if (string.IsNullOrEmpty(unitId))
            {
                coordinate = HexCoordinate.Zero;
                return false;
            }

            return _tilesByOccupant.TryGetValue(unitId, out coordinate);
        }

        /// <summary>
        /// Places (or moves) a unit onto a tile. Fails without changing anything when the id is
        /// empty, the tile is off the board, or another unit already holds it. Re-placing a unit on
        /// the tile it already occupies succeeds and is a no-op.
        /// </summary>
        public bool TryPlaceUnit(string unitId, HexCoordinate coordinate)
        {
            if (string.IsNullOrEmpty(unitId) || !IsInBounds(coordinate))
            {
                return false;
            }

            string existing;
            if (_occupantsByTile.TryGetValue(coordinate, out existing))
            {
                return string.Equals(existing, unitId, StringComparison.Ordinal);
            }

            HexCoordinate previous;
            if (_tilesByOccupant.TryGetValue(unitId, out previous))
            {
                _occupantsByTile.Remove(previous);
            }

            _occupantsByTile[coordinate] = unitId;
            _tilesByOccupant[unitId] = coordinate;
            return true;
        }

        /// <summary>
        /// Lifts a unit off the board. Returns false when that unit was not placed to begin with.
        /// </summary>
        public bool RemoveUnit(string unitId)
        {
            HexCoordinate coordinate;
            if (string.IsNullOrEmpty(unitId) || !_tilesByOccupant.TryGetValue(unitId, out coordinate))
            {
                return false;
            }

            _tilesByOccupant.Remove(unitId);
            _occupantsByTile.Remove(coordinate);
            return true;
        }

        /// <summary>Clears all occupancy, leaving the tile set intact.</summary>
        public void ClearOccupancy()
        {
            _occupantsByTile.Clear();
            _tilesByOccupant.Clear();
        }

        /// <summary>
        /// Every in-bounds tile within <paramref name="range"/> hex steps of the origin, including
        /// the origin itself. A negative range yields nothing; a zero range yields just the origin
        /// (when it is on the board). Occupancy is ignored — this is a pure shape query, which is
        /// what skill target shapes and move previews both need before they filter it.
        /// </summary>
        public IReadOnlyList<HexCoordinate> GetTilesInRange(HexCoordinate origin, int range)
        {
            List<HexCoordinate> results = new List<HexCoordinate>();
            if (range < 0)
            {
                return results;
            }

            for (int q = -range; q <= range; q++)
            {
                int lowerR = Math.Max(-range, -q - range);
                int upperR = Math.Min(range, -q + range);

                for (int r = lowerR; r <= upperR; r++)
                {
                    HexCoordinate candidate = origin + new HexCoordinate(q, r);
                    if (IsInBounds(candidate))
                    {
                        results.Add(candidate);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Fills the tile set with a hexagon of hexes of <see cref="Radius"/> rings around the
        /// origin. The r-bounds clamp each q-column so the result is a hexagon rather than a
        /// rhombus.
        /// </summary>
        private void GenerateTiles()
        {
            for (int q = -Radius; q <= Radius; q++)
            {
                int lowerR = Math.Max(-Radius, -q - Radius);
                int upperR = Math.Min(Radius, -q + Radius);

                for (int r = lowerR; r <= upperR; r++)
                {
                    _tiles.Add(new HexCoordinate(q, r));
                }
            }
        }
    }
}
