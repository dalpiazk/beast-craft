using System;
using System.Collections.Generic;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A hexagon-shaped board of hex tiles, centred on <see cref="HexCoordinate.Zero"/> and sized
    /// by an <see cref="ArenaSize"/> preset. Owns the set of legal tiles, which unit stands on each
    /// of them, and which of them are blocked by terrain.
    /// <para>
    /// Occupancy (a unit stands here) and terrain blocking (scenery sits here) are tracked
    /// separately and mean different things, so a tile can be either, both or neither.
    /// <see cref="IsPassable"/> is the query that combines them.
    /// </para>
    /// <para>
    /// Scaffolding only: this is the board's state and its bounds/occupancy/terrain queries. Per-
    /// tile movement cost, line of sight and elevation are all deliberately absent — every step
    /// costs 1, which is what <see cref="HexPathfinder"/> assumes — and land in a later pass.
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
        private readonly HashSet<HexCoordinate> _blockedTiles;
        private readonly Dictionary<HexCoordinate, string> _occupantsByTile;
        private readonly Dictionary<string, HexCoordinate> _tilesByOccupant;

        public HexGrid(ArenaSize size)
        {
            Size = size;
            Radius = RadiusFor(size);

            _tiles = new HashSet<HexCoordinate>();
            _blockedTiles = new HashSet<HexCoordinate>();
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

        /// <summary>Clears all occupancy, leaving the tile set and terrain blocking intact.</summary>
        public void ClearOccupancy()
        {
            _occupantsByTile.Clear();
            _tilesByOccupant.Clear();
        }

        /// <summary>
        /// Marks a tile as blocked by terrain (a rock, a pit, a wall — scenery, not a unit), or
        /// clears that mark. Returns false without changing anything when the tile is off the
        /// board. Blocking a tile a unit already stands on is legal and does not evict the unit:
        /// the two are separate concerns, and it is up to encounter setup not to build that.
        /// </summary>
        public bool SetBlocked(HexCoordinate coordinate, bool blocked)
        {
            if (!IsInBounds(coordinate))
            {
                return false;
            }

            if (blocked)
            {
                _blockedTiles.Add(coordinate);
            }
            else
            {
                _blockedTiles.Remove(coordinate);
            }

            return true;
        }

        /// <summary>
        /// True when terrain makes this tile unusable. Off-board tiles read as blocked as well:
        /// they are not legal to stand on either, so callers get one answer to "can anything be
        /// here" without a separate bounds check. This ignores units entirely — see
        /// <see cref="IsOccupied"/> for those and <see cref="IsPassable"/> for both at once.
        /// </summary>
        public bool IsBlocked(HexCoordinate coordinate)
        {
            return !IsInBounds(coordinate) || _blockedTiles.Contains(coordinate);
        }

        /// <summary>
        /// Whether a given unit may stand on a tile. True when the tile is on the board, is not
        /// blocked by terrain, and is either empty or held by that same unit.
        /// <para>
        /// Two rules are deliberate. A unit never obstructs itself, so a mover's own tile is
        /// passable to it — otherwise it could not path out of where it is standing. And every
        /// other unit does obstruct, friend or foe: there is no flying, phasing or swapping yet,
        /// so units cannot be moved onto or routed through. Both are expected to gain exceptions
        /// once movement abilities exist.
        /// </para>
        /// <para>
        /// A null or empty <paramref name="movingUnitId"/> means "no particular unit", so any
        /// occupied tile is impassable to it.
        /// </para>
        /// </summary>
        public bool IsPassable(HexCoordinate coordinate, string movingUnitId)
        {
            if (IsBlocked(coordinate))
            {
                return false;
            }

            string occupant;
            if (!_occupantsByTile.TryGetValue(coordinate, out occupant))
            {
                return true;
            }

            return !string.IsNullOrEmpty(movingUnitId)
                && string.Equals(occupant, movingUnitId, StringComparison.Ordinal);
        }

        /// <summary>Clears all terrain blocking, leaving the tile set and occupancy intact.</summary>
        public void ClearBlocked()
        {
            _blockedTiles.Clear();
        }

        /// <summary>
        /// Every in-bounds tile within <paramref name="range"/> hex steps of the origin, including
        /// the origin itself. A negative range yields nothing; a zero range yields just the origin
        /// (when it is on the board). Occupancy and terrain are both ignored — this is a pure shape
        /// query, which is what skill target shapes and move previews both need before they filter
        /// it.
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
