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
    /// Also owns which tiles each side may deploy onto before the fight starts — see
    /// <see cref="IsInDeploymentZone"/>. That sits here rather than in a type of its own because it
    /// is the same kind of fact as terrain blocking and occupancy: a per-tile property of this
    /// board, derived from this board's own <see cref="Radius"/>, that several callers need to ask
    /// about without carrying a second object around.
    /// </para>
    /// <para>
    /// <strong>Large units.</strong> A unit may cover several tiles (<see cref="UnitFootprint"/>):
    /// the grid records its anchor (<see cref="TryGetPosition"/>) and footprint
    /// (<see cref="GetFootprint"/>), and <see cref="GetOccupant"/> names it on every tile it
    /// covers. Placement is all or nothing and <see cref="RemoveUnit"/> frees every tile. A board
    /// of one-tile units runs exactly the code it always did.
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

        private readonly Dictionary<string, HexCoordinate> _tilesByOccupant;

        // The footprint of every placed unit that covers more than one tile, keyed by unit id. A
        // unit absent from it is Single, which is every unit in a battle without large enemies, so
        // the Single paths only ever ask whether this is empty. _tilesByOccupant keeps the anchor;
        // _occupantByIndex holds the id on every tile the footprint covers.
        private readonly Dictionary<string, UnitFootprint> _footprintByOccupant;

        // Built on first use of Tiles, in the same insertion order as ever: bounds checks are
        // arithmetic (see IsInBounds), so most boards never need the set at all.
        private HashSet<HexCoordinate> _tiles;

        // Occupancy and terrain blocking by tile, indexed by TileIndex, so the hot queries
        // (IsPassable, IsOccupied, IsBlocked -- asked once per neighbour by every path search) are
        // an array read instead of a hash lookup. _tilesByOccupant is the same occupancy keyed by
        // unit id; every write goes to both.
        private readonly int _span;
        private readonly string[] _occupantByIndex;
        private readonly bool[] _blockedByIndex;

        // Smallest |R| that still counts as a deployment row. Derived from DeploymentZoneDepth and
        // held because every zone query tests against it. Floored at 1 so the two zones can never
        // meet in the middle and share the R == 0 row, whatever Radius turns out to be.
        private readonly int _deploymentRowThreshold;

        public HexGrid(ArenaSize size)
        {
            Size = size;
            Radius = RadiusFor(size);
            DeploymentZoneDepth = Math.Max(1, (Radius + 1) / 2);
            _deploymentRowThreshold = Math.Max(1, (Radius - DeploymentZoneDepth) + 1);

            _tilesByOccupant = new Dictionary<string, HexCoordinate>(StringComparer.Ordinal);
            _footprintByOccupant = new Dictionary<string, UnitFootprint>(StringComparer.Ordinal);

            _span = (2 * Radius) + 1;
            _occupantByIndex = new string[_span * _span];
            _blockedByIndex = new bool[_span * _span];
        }

        /// <summary>The preset this board was built from.</summary>
        public ArenaSize Size { get; }

        /// <summary>Distance in hex steps from the centre tile to the outermost ring.</summary>
        public int Radius { get; }

        /// <summary>
        /// How many rows deep each side's deployment zone reaches in from its own edge of the
        /// board, counted in whole <c>R</c> rows. The remaining <c>2 * (Radius -
        /// DeploymentZoneDepth) + 1</c> rows in the middle are a neutral no-deploy band that
        /// belongs to neither side.
        /// <para>
        /// TUNABLE IMPLEMENTATION DEFAULT, NOT CONFIRMED BALANCE — exactly like the radius
        /// constants above, and for the same reason. Nothing about deployment geometry has been
        /// through encounter design; what is settled (decision 2) is only how many beasts a format
        /// deploys, not where they may stand. This is <c>ceil(Radius / 2)</c>, picked so roughly
        /// half the board is contested ground and each side still has real depth to arrange itself
        /// in. Expect it to move once encounter design gives it something to be balanced against.
        /// </para>
        /// </summary>
        public int DeploymentZoneDepth { get; }

        /// <summary>Total number of legal tiles on the board.</summary>
        public int TileCount
        {
            get { return 1 + (3 * Radius * (Radius + 1)); }
        }

        /// <summary>Every legal tile on the board, in no guaranteed order.</summary>
        public IEnumerable<HexCoordinate> Tiles
        {
            get
            {
                if (_tiles == null)
                {
                    _tiles = GenerateTiles();
                }

                return _tiles;
            }
        }

        /// <summary>
        /// The size of the dense index space <see cref="TileIndex"/> maps into: every legal tile
        /// has an index in <c>[0, TileIndexCapacity)</c> (some indices name no tile). Lets a search
        /// keep per-tile state in plain arrays rather than hash maps.
        /// </summary>
        public int TileIndexCapacity
        {
            get { return _span * _span; }
        }

        /// <summary>
        /// A dense, stable index for a legal tile (see <see cref="TileIndexCapacity"/>), or -1 when
        /// the coordinate is off the board. Two different legal tiles never share an index.
        /// </summary>
        public int TileIndex(HexCoordinate coordinate)
        {
            if (!IsInBounds(coordinate))
            {
                return -1;
            }

            return ((coordinate.Q + Radius) * _span) + coordinate.R + Radius;
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
            // The board is the hexagon of radius Radius: exactly the tiles GenerateTiles lists.
            int q = coordinate.Q;
            int r = coordinate.R;
            return q >= -Radius && q <= Radius && r >= -Radius && r <= Radius && q + r >= -Radius && q + r <= Radius;
        }

        /// <summary>
        /// True when a unit currently stands on this tile. Out-of-bounds tiles are never occupied.
        /// </summary>
        public bool IsOccupied(HexCoordinate coordinate)
        {
            int index = TileIndex(coordinate);
            return index >= 0 && _occupantByIndex[index] != null;
        }

        /// <summary>
        /// The id of the unit standing on this tile, or <c>null</c> when the tile is empty or off
        /// the board. Callers are expected to null-check rather than catch.
        /// </summary>
        public string GetOccupant(HexCoordinate coordinate)
        {
            int index = TileIndex(coordinate);
            return index < 0 ? null : _occupantByIndex[index];
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
        /// The footprint a unit was placed with (its anchor is <see cref="TryGetPosition"/>). A unit
        /// that is not on the board, or was placed as one tile, reads as
        /// <see cref="UnitFootprint.Single"/>.
        /// </summary>
        public UnitFootprint GetFootprint(string unitId)
        {
            UnitFootprint footprint;
            if (string.IsNullOrEmpty(unitId) || _footprintByOccupant.Count == 0 || !_footprintByOccupant.TryGetValue(unitId, out footprint))
            {
                return UnitFootprint.Single;
            }

            return footprint;
        }

        /// <summary>
        /// Places (or moves) a unit onto a tile. Fails without changing anything when the id is
        /// empty, the tile is off the board, or another unit already holds it. Re-placing a unit on
        /// the tile it already occupies succeeds and is a no-op.
        /// <para>
        /// A unit already on the board with a larger footprint keeps it: the tile is its new anchor
        /// and the move is exactly <see cref="TryPlaceUnit(string, HexCoordinate, UnitFootprint)"/>
        /// with the recorded footprint.
        /// </para>
        /// </summary>
        public bool TryPlaceUnit(string unitId, HexCoordinate coordinate)
        {
            if (string.IsNullOrEmpty(unitId) || !IsInBounds(coordinate))
            {
                return false;
            }

            UnitFootprint recorded;
            if (_footprintByOccupant.Count > 0 && _footprintByOccupant.TryGetValue(unitId, out recorded))
            {
                return TryPlaceFootprint(unitId, coordinate, recorded);
            }

            int index = TileIndex(coordinate);
            string existing = _occupantByIndex[index];
            if (existing != null)
            {
                return string.Equals(existing, unitId, StringComparison.Ordinal);
            }

            HexCoordinate previous;
            if (_tilesByOccupant.TryGetValue(unitId, out previous))
            {
                _occupantByIndex[TileIndex(previous)] = null;
            }

            _occupantByIndex[index] = unitId;
            _tilesByOccupant[unitId] = coordinate;
            return true;
        }

        /// <summary>
        /// Places (or moves) a unit of any <see cref="UnitFootprint"/> with its anchor on
        /// <paramref name="anchor"/>. <strong>All or nothing:</strong> every tile the footprint would
        /// cover must be on the board, free of terrain blocking, and either empty or already held by
        /// this same unit; otherwise nothing changes and this returns false. On success the unit's
        /// old tiles are all released and the new ones all taken, so a move never leaves a stray
        /// tile behind, and a unit may move onto tiles it already covers (a one-step shuffle).
        /// <para>
        /// <see cref="UnitFootprint.Single"/> for a unit not recorded as larger is exactly the
        /// two-argument <see cref="TryPlaceUnit(string, HexCoordinate)"/>, rules and all (that
        /// overload, unlike a larger footprint, does not refuse a terrain-blocked tile).
        /// </para>
        /// </summary>
        public bool TryPlaceUnit(string unitId, HexCoordinate anchor, UnitFootprint footprint)
        {
            if (footprint == UnitFootprint.Single && (_footprintByOccupant.Count == 0 || unitId == null || !_footprintByOccupant.ContainsKey(unitId)))
            {
                return TryPlaceUnit(unitId, anchor);
            }

            if (string.IsNullOrEmpty(unitId) || !IsInBounds(anchor))
            {
                return false;
            }

            return TryPlaceFootprint(unitId, anchor, footprint);
        }

        /// <summary>The all-or-nothing footprint placement behind both <c>TryPlaceUnit</c> overloads.</summary>
        private bool TryPlaceFootprint(string unitId, HexCoordinate anchor, UnitFootprint footprint)
        {
            HexCoordinate[] offsets = Footprints.OffsetArray(footprint);

            for (int i = 0; i < offsets.Length; i++)
            {
                int index = TileIndex(anchor + offsets[i]);
                if (index < 0 || _blockedByIndex[index])
                {
                    return false;
                }

                string existing = _occupantByIndex[index];
                if (existing != null && !string.Equals(existing, unitId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            ReleaseTiles(unitId);

            for (int i = 0; i < offsets.Length; i++)
            {
                _occupantByIndex[TileIndex(anchor + offsets[i])] = unitId;
            }

            _tilesByOccupant[unitId] = anchor;
            if (footprint == UnitFootprint.Single)
            {
                _footprintByOccupant.Remove(unitId);
            }
            else
            {
                _footprintByOccupant[unitId] = footprint;
            }

            return true;
        }

        /// <summary>Clears every tile a placed unit covers (its id records are left to the caller).</summary>
        private void ReleaseTiles(string unitId)
        {
            HexCoordinate previous;
            if (!_tilesByOccupant.TryGetValue(unitId, out previous))
            {
                return;
            }

            HexCoordinate[] offsets = Footprints.OffsetArray(GetFootprint(unitId));
            for (int i = 0; i < offsets.Length; i++)
            {
                int index = TileIndex(previous + offsets[i]);
                if (index >= 0 && string.Equals(_occupantByIndex[index], unitId, StringComparison.Ordinal))
                {
                    _occupantByIndex[index] = null;
                }
            }
        }

        /// <summary>
        /// Lifts a unit off the board, every tile of its footprint. Returns false when that unit was
        /// not placed to begin with.
        /// </summary>
        public bool RemoveUnit(string unitId)
        {
            HexCoordinate coordinate;
            if (string.IsNullOrEmpty(unitId) || !_tilesByOccupant.TryGetValue(unitId, out coordinate))
            {
                return false;
            }

            if (_footprintByOccupant.Count > 0 && _footprintByOccupant.ContainsKey(unitId))
            {
                ReleaseTiles(unitId);
                _footprintByOccupant.Remove(unitId);
                _tilesByOccupant.Remove(unitId);
                return true;
            }

            _tilesByOccupant.Remove(unitId);
            _occupantByIndex[TileIndex(coordinate)] = null;
            return true;
        }

        /// <summary>Clears all occupancy, leaving the tile set and terrain blocking intact.</summary>
        public void ClearOccupancy()
        {
            _tilesByOccupant.Clear();
            _footprintByOccupant.Clear();
            Array.Clear(_occupantByIndex, 0, _occupantByIndex.Length);
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

            _blockedByIndex[TileIndex(coordinate)] = blocked;
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
            int index = TileIndex(coordinate);
            return index < 0 || _blockedByIndex[index];
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
            return IsPassableAt(TileIndex(coordinate), movingUnitId);
        }

        /// <summary>
        /// <see cref="IsPassable"/> for a tile already turned into its <see cref="TileIndex"/>
        /// (-1, off the board, is never passable), so a search that has the index does not
        /// recompute it.
        /// </summary>
        internal bool IsPassableAt(int index, string movingUnitId)
        {
            if (index < 0 || _blockedByIndex[index])
            {
                return false;
            }

            string occupant = _occupantByIndex[index];
            if (occupant == null)
            {
                return true;
            }

            return !string.IsNullOrEmpty(movingUnitId)
                && string.Equals(occupant, movingUnitId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a unit of footprint <paramref name="footprint"/> could stand with its anchor on
        /// <paramref name="anchor"/>: every tile it would cover is <see cref="IsPassable"/> to
        /// <paramref name="movingUnitId"/> (on the board, not blocked, empty or its own). For
        /// <see cref="UnitFootprint.Single"/> this is exactly <see cref="IsPassable"/>.
        /// </summary>
        public bool CanStand(HexCoordinate anchor, UnitFootprint footprint, string movingUnitId)
        {
            return CanStandAt(TileIndex(anchor), anchor, footprint, movingUnitId);
        }

        /// <summary><see cref="CanStand"/> with the anchor's <see cref="TileIndex"/> already computed.</summary>
        internal bool CanStandAt(int anchorIndex, HexCoordinate anchor, UnitFootprint footprint, string movingUnitId)
        {
            if (!IsPassableAt(anchorIndex, movingUnitId))
            {
                return false;
            }

            if (footprint == UnitFootprint.Single)
            {
                return true;
            }

            HexCoordinate[] offsets = Footprints.OffsetArray(footprint);
            for (int i = 1; i < offsets.Length; i++)
            {
                if (!IsPassableAt(TileIndex(anchor + offsets[i]), movingUnitId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether every tile a footprint anchored on <paramref name="anchor"/> covers is in
        /// <paramref name="team"/>'s deployment zone (<see cref="IsInDeploymentZone"/>). A pure shape
        /// query: terrain and occupancy are ignored. For <see cref="UnitFootprint.Single"/> it is
        /// <see cref="IsInDeploymentZone"/>.
        /// </summary>
        public bool FitsDeploymentZone(HexCoordinate anchor, UnitFootprint footprint, BattleTeam team)
        {
            HexCoordinate[] offsets = Footprints.OffsetArray(footprint);
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!IsInDeploymentZone(anchor + offsets[i], team))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Clears all terrain blocking, leaving the tile set and occupancy intact.</summary>
        public void ClearBlocked()
        {
            Array.Clear(_blockedByIndex, 0, _blockedByIndex.Length);
        }

        /// <summary>
        /// True when a tile is one this team may deploy onto before the fight starts.
        /// <para>
        /// The board is split into three bands along the axial <c>R</c> axis:
        /// <see cref="BattleTeam.Player"/> owns the <see cref="DeploymentZoneDepth"/> rows with the
        /// most positive <c>R</c>, <see cref="BattleTeam.Enemy"/> owns the mirror-image rows with
        /// the most negative <c>R</c>, and the rows between them are neutral and belong to nobody.
        /// </para>
        /// <para>
        /// <strong>Why <c>R</c>.</strong> All three cube axes split a hexagon into two congruent
        /// regions — negating every cube component is a 180° rotation that maps the board onto
        /// itself and each zone exactly onto the other — so symmetry does not pick between them.
        /// <c>R</c> is chosen because it is the axis <see cref="HexCoordinate"/> already calls the
        /// "row" axis: a constant-<c>R</c> band is a single straight run of tiles across the board,
        /// so the front line reads as a straight line and "your half / their half" is legible
        /// without a diagram. Splitting on <c>Q</c> or the implied <c>S</c> is geometrically
        /// identical but lands the front line on a diagonal, which is harder to read and harder to
        /// describe to a player.
        /// </para>
        /// <para>
        /// A pure shape query, like <see cref="GetTilesInRange"/>: terrain and occupancy are
        /// ignored, because a blocked or taken tile is still on this side of the board. Off-board
        /// tiles are in nobody's zone. Any <paramref name="team"/> value other than
        /// <see cref="BattleTeam.Player"/> is read as the enemy side, matching how
        /// <see cref="RadiusFor"/> handles an unrecognised enum.
        /// </para>
        /// </summary>
        public bool IsInDeploymentZone(HexCoordinate coordinate, BattleTeam team)
        {
            if (!IsInBounds(coordinate))
            {
                return false;
            }

            if (team == BattleTeam.Player)
            {
                return coordinate.R >= _deploymentRowThreshold;
            }

            return coordinate.R <= -_deploymentRowThreshold;
        }

        /// <summary>
        /// Every tile <paramref name="team"/> may deploy onto, ordered by row and then along the
        /// row so the result is stable run to run — unlike <see cref="Tiles"/>, which is a set.
        /// Built from <see cref="IsInDeploymentZone"/>'s rule and carries all of its caveats:
        /// terrain and occupancy are ignored, so a caller that wants only the tiles a unit could
        /// actually stand on must filter this itself.
        /// <para>
        /// Sizing, against the radii the presets actually use (row <c>R</c> of a hexagon of radius
        /// <c>n</c> holds <c>2n + 1 - |R|</c> tiles): Small (radius 3, depth 2) gives 9 tiles per
        /// side, Medium (radius 5, depth 3) gives 21, Large (radius 7, depth 4) gives 38. The
        /// smallest of those still seats <see cref="BattleFormat.LargeGroup"/>'s six beasts with
        /// three tiles to spare, so every format fits on every arena preset.
        /// </para>
        /// </summary>
        public IReadOnlyList<HexCoordinate> GetDeploymentZone(BattleTeam team)
        {
            List<HexCoordinate> results = new List<HexCoordinate>();
            int lowestRow = team == BattleTeam.Player ? _deploymentRowThreshold : -Radius;
            int highestRow = team == BattleTeam.Player ? Radius : -_deploymentRowThreshold;

            for (int r = lowestRow; r <= highestRow; r++)
            {
                // A hexagon centred on the origin is symmetric under swapping the two axial axes,
                // so the clamp that bounds r for a given q bounds q for a given r unchanged.
                int lowerQ;
                int upperQ;
                RingRowBounds(Radius, r, out lowerQ, out upperQ);

                for (int q = lowerQ; q <= upperQ; q++)
                {
                    HexCoordinate candidate = new HexCoordinate(q, r);
                    if (IsInBounds(candidate))
                    {
                        results.Add(candidate);
                    }
                }
            }

            return results;
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
                int lowerR;
                int upperR;
                RingRowBounds(range, q, out lowerR, out upperR);

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
        /// Builds the tile set: a hexagon of hexes of <see cref="Radius"/> rings around the
        /// origin. The r-bounds clamp each q-column so the result is a hexagon rather than a
        /// rhombus.
        /// </summary>
        private HashSet<HexCoordinate> GenerateTiles()
        {
            HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>();

            for (int q = -Radius; q <= Radius; q++)
            {
                int lowerR;
                int upperR;
                RingRowBounds(Radius, q, out lowerR, out upperR);

                for (int r = lowerR; r <= upperR; r++)
                {
                    tiles.Add(new HexCoordinate(q, r));
                }
            }

            return tiles;
        }

        /// <summary>
        /// The inclusive r-range to walk for one q-column of a hexagon of
        /// <paramref name="radius"/> rings centred on the origin.
        /// <para>
        /// Sweeping q from <c>-radius</c> to <c>+radius</c> and r across the whole of that same
        /// span would trace a rhombus; clamping r against the implied third cube axis
        /// (<c>-q - r</c>, which must also stay within the radius) is what shears the rhombus back
        /// into a hexagon. Shared by <see cref="GenerateTiles"/> and
        /// <see cref="GetTilesInRange"/>, which describe the same shape at different radii, so the
        /// board's own bounds and a range query can never disagree about what a hexagon is.
        /// </para>
        /// </summary>
        private static void RingRowBounds(int radius, int q, out int lowerR, out int upperR)
        {
            lowerR = Math.Max(-radius, -q - radius);
            upperR = Math.Min(radius, -q + radius);
        }
    }
}
