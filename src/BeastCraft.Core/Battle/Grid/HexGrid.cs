using System;
using System.Collections.Generic;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A rectangular board of pointy-top hex tiles, <see cref="Width"/> tiles across and
    /// <see cref="Height"/> rows deep, centred on <see cref="HexCoordinate.Zero"/> and sized by an
    /// <see cref="ArenaSize"/> preset. Owns the set of legal tiles, which unit stands on each of
    /// them, and which of them are blocked by terrain.
    /// <para>
    /// <strong>Shape.</strong> The board is an <em>odd-r offset rectangle</em>: every row holds
    /// exactly <see cref="Width"/> tiles, rows run from <see cref="MinRow"/> (the enemy's edge, the
    /// top of the screen) to <see cref="MaxRow"/> (the player's edge, the bottom), and odd rows sit
    /// half a tile to the right of even ones (<see cref="ColumnOf"/>, <see cref="FromOffset"/>).
    /// Only the <em>legality</em> of a tile is rectangular: addresses stay axial
    /// (<see cref="HexCoordinate"/>), and neighbours, distance and range are the same cube maths as
    /// ever. The left and right edges zigzag by half a tile from row to row, which is how any
    /// rectangle of pointy-top hexes looks. Every straight hex line crosses the board in one piece
    /// (a line that leaves it never re-enters), as it did on the hexagon.
    /// </para>
    /// <para>
    /// <strong>Why odd-r.</strong> Every preset has an odd number of rows centred on the
    /// <c>R = 0</c> row, so rows <c>R</c> and <c>-R</c> always have the same parity and line up
    /// tile for tile: the enemy half is the player half reflected top to bottom (<c>(Q, R)</c> to
    /// <c>(Q + R, -R)</c>, a symmetry of the hex grid that keeps each tile's screen column and every
    /// distance), which is what keeps the two deployment zones fair. Odd-r rather than even-r keeps
    /// the even rows, the centre row among them, unshifted, so tile (0, 0) is column 0 and
    /// <c>Q</c> still runs along the centre row. The columns (<see cref="MinColumn"/> to
    /// <see cref="MaxColumn"/>, <c>-(Width / 2)</c> up) put each side's <em>front</em> row
    /// symmetrically about the board's vertical centre line (the screen x of tile (0, 0)), the line
    /// <see cref="Placement.DeploymentPacker.FrontOrder"/> fills outward from: the front rows are
    /// even on Small and Large (odd widths, so the even rows are the centred ones) and odd on Medium
    /// (an even width, whose odd rows are the centred ones).
    /// </para>
    /// <para>
    /// Occupancy (a unit stands here) and terrain blocking (scenery sits here) are tracked
    /// separately and mean different things, so a tile can be either, both or neither.
    /// <see cref="IsPassable"/> is the query that combines them.
    /// </para>
    /// <para>
    /// Also owns which tiles each side may deploy onto before the fight starts — see
    /// <see cref="IsInDeploymentZone"/>. That sits here rather than in a type of its own because it
    /// is the same kind of fact as terrain blocking and occupancy: a per-tile property of this
    /// board, derived from this board's own <see cref="Height"/>, that several callers need to ask
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
        // Width x height, in tiles, for each arena preset: portrait-fit rectangles close to the old
        // hexagons' 37 / 91 / 169 tiles (a producer decision). What is confirmed is that there are
        // exactly three presets, that an encounter fixes one of them, and these dimensions; how
        // deep each side deploys is still a tunable default (see DeploymentZoneDepth). Every
        // height is odd, so the board is centred on the R = 0 row.
        private const int SmallWidth = 5;
        private const int SmallHeight = 7;
        private const int MediumWidth = 8;
        private const int MediumHeight = 11;
        private const int LargeWidth = 11;
        private const int LargeHeight = 15;

        private readonly Dictionary<string, HexCoordinate> _tilesByOccupant;

        // The footprint of every placed unit that covers more than one tile, keyed by unit id. A
        // unit absent from it is Single, which is every unit in a battle without large enemies, so
        // the Single paths only ever ask whether this is empty. _tilesByOccupant keeps the anchor;
        // _occupantByIndex holds the id on every tile the footprint covers.
        private readonly Dictionary<string, UnitFootprint> _footprintByOccupant;

        // Built on first use of Tiles, row by row from the top: bounds checks are arithmetic (see
        // IsInBounds), so most boards never need the set at all.
        private HashSet<HexCoordinate> _tiles;

        // Occupancy and terrain blocking by tile, indexed by TileIndex, so the hot queries
        // (IsPassable, IsOccupied, IsBlocked -- asked once per neighbour by every path search) are
        // an array read instead of a hash lookup. _tilesByOccupant is the same occupancy keyed by
        // unit id; every write goes to both.
        private readonly string[] _occupantByIndex;
        private readonly bool[] _blockedByIndex;

        // Smallest |R| that still counts as a deployment row. Derived from DeploymentZoneDepth and
        // held because every zone query tests against it. Floored at 1 so the two zones can never
        // meet in the middle and share the R == 0 row, whatever the height turns out to be.
        private readonly int _deploymentRowThreshold;

        public HexGrid(ArenaSize size)
        {
            Size = size;
            int width;
            int height;
            DimensionsFor(size, out width, out height);
            Width = width;
            Height = height;
            MinRow = -((height - 1) / 2);
            MaxRow = MinRow + height - 1;
            MinColumn = -(width / 2);
            MaxColumn = MinColumn + width - 1;

            int rowsPerSide = Math.Min(-MinRow, MaxRow);
            DeploymentZoneDepth = Math.Max(1, (rowsPerSide + 1) / 2);
            _deploymentRowThreshold = Math.Max(1, (rowsPerSide - DeploymentZoneDepth) + 1);

            _tilesByOccupant = new Dictionary<string, HexCoordinate>(StringComparer.Ordinal);
            _footprintByOccupant = new Dictionary<string, UnitFootprint>(StringComparer.Ordinal);

            _occupantByIndex = new string[width * height];
            _blockedByIndex = new bool[width * height];
        }

        /// <summary>The preset this board was built from.</summary>
        public ArenaSize Size { get; }

        /// <summary>Tiles per row; every row holds the same number.</summary>
        public int Width { get; }

        /// <summary>Rows, from <see cref="MinRow"/> (top, the enemy's edge) to <see cref="MaxRow"/> (bottom, the player's).</summary>
        public int Height { get; }

        /// <summary>The top row's <c>R</c>: the enemy's back row.</summary>
        public int MinRow { get; }

        /// <summary>The bottom row's <c>R</c>: the player's back row.</summary>
        public int MaxRow { get; }

        /// <summary>The leftmost offset column (<see cref="ColumnOf"/>), the same on every row.</summary>
        public int MinColumn { get; }

        /// <summary>The rightmost offset column (<see cref="ColumnOf"/>), the same on every row.</summary>
        public int MaxColumn { get; }

        /// <summary>
        /// How many rows deep each side's deployment zone reaches in from its own edge of the
        /// board, counted in whole <c>R</c> rows: 2 on Small, 3 on Medium, 4 on Large. The
        /// remaining <c>Height - 2 * DeploymentZoneDepth</c> rows in the middle (3 / 5 / 7) are a
        /// neutral no-deploy band that belongs to neither side.
        /// <para>
        /// TUNABLE IMPLEMENTATION DEFAULT, NOT CONFIRMED BALANCE. Nothing about deployment
        /// geometry has been through encounter design; what is settled (decision 2) is only how
        /// many beasts a format deploys, not where they may stand. This is <c>ceil(h / 2)</c> for the
        /// <c>h = (Height - 1) / 2</c> rows on each side of the centre line — the same 2 / 3 / 4 rows
        /// the hexagonal arenas had — so roughly half the board is contested ground and each side
        /// still has real depth to arrange itself in. It is also the fit constraint on large units:
        /// a seven-tile (<see cref="UnitFootprint.Hex7"/>) unit is three rows tall, so it fits the
        /// Medium and Large zones but never Small's.
        /// </para>
        /// </summary>
        public int DeploymentZoneDepth { get; }

        /// <summary>Total number of legal tiles on the board: <c>Width * Height</c>.</summary>
        public int TileCount
        {
            get { return Width * Height; }
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
        /// has an index in <c>[0, TileIndexCapacity)</c> (on a rectangle every index names a tile).
        /// Lets a search keep per-tile state in plain arrays rather than hash maps.
        /// </summary>
        public int TileIndexCapacity
        {
            get { return Width * Height; }
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

            return ((coordinate.R - MinRow) * Width) + ColumnOf(coordinate) - MinColumn;
        }

        /// <summary>
        /// The width and height, in tiles, a preset maps to: Small 5 x 7 (35 tiles), Medium 8 x 11
        /// (88), Large 11 x 15 (165). An unrecognised value reads as Medium.
        /// </summary>
        public static void DimensionsFor(ArenaSize size, out int width, out int height)
        {
            switch (size)
            {
                case ArenaSize.Small:
                    width = SmallWidth;
                    height = SmallHeight;
                    return;
                case ArenaSize.Large:
                    width = LargeWidth;
                    height = LargeHeight;
                    return;
                default:
                    width = MediumWidth;
                    height = MediumHeight;
                    return;
            }
        }

        /// <summary>
        /// A tile's odd-r offset column, <c>Q + floor(R / 2)</c>: the tiles of one column stack
        /// straight down the even rows and sit half a tile further right on the odd rows.
        /// </summary>
        public static int ColumnOf(HexCoordinate coordinate)
        {
            // An arithmetic shift floors, negative rows included.
            return coordinate.Q + (coordinate.R >> 1);
        }

        /// <summary>The tile in offset <paramref name="column"/> of row <paramref name="row"/>: the inverse of <see cref="ColumnOf"/>.</summary>
        public static HexCoordinate FromOffset(int column, int row)
        {
            return new HexCoordinate(column - (row >> 1), row);
        }

        /// <summary>True when the coordinate names a tile that exists on this board.</summary>
        public bool IsInBounds(HexCoordinate coordinate)
        {
            // The board is the offset rectangle: exactly the tiles GenerateTiles lists.
            int r = coordinate.R;
            if (r < MinRow || r > MaxRow)
            {
                return false;
            }

            int column = ColumnOf(coordinate);
            return column >= MinColumn && column <= MaxColumn;
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
        /// most positive <c>R</c> (the bottom of the screen), <see cref="BattleTeam.Enemy"/> owns the
        /// mirror-image rows with the most negative <c>R</c> (the top), and the rows between them
        /// are neutral and belong to nobody.
        /// </para>
        /// <para>
        /// <strong>Why <c>R</c>.</strong> <c>R</c> is the axis <see cref="HexCoordinate"/> calls the
        /// "row" axis and the one the rectangle's straight edges run along: a constant-<c>R</c> band
        /// is a single straight run of <see cref="Width"/> tiles across the board, so the front line
        /// reads as a straight line and "your half / their half" is legible without a diagram. The
        /// two zones are mirror images: reflecting the board top to bottom (<c>(Q, R)</c> to
        /// <c>(Q + R, -R)</c>; see the class notes) maps each exactly onto the other.
        /// </para>
        /// <para>
        /// A pure shape query, like <see cref="GetTilesInRange"/>: terrain and occupancy are
        /// ignored, because a blocked or taken tile is still on this side of the board. Off-board
        /// tiles are in nobody's zone. Any <paramref name="team"/> value other than
        /// <see cref="BattleTeam.Player"/> is read as the enemy side.
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
        /// Every tile <paramref name="team"/> may deploy onto, ordered by row (top first) and then
        /// left to right along the row, so the result is stable run to run — unlike
        /// <see cref="Tiles"/>, which is a set. Built from <see cref="IsInDeploymentZone"/>'s rule and
        /// carries all of its caveats: terrain and occupancy are ignored, so a caller that wants only
        /// the tiles a unit could actually stand on must filter this itself.
        /// <para>
        /// Sizing: every row holds <see cref="Width"/> tiles, so a side gets
        /// <c>Width * DeploymentZoneDepth</c>: Small (5 x 2) 10 tiles, Medium (8 x 3) 24, Large
        /// (11 x 4) 44. The smallest still seats <see cref="BattleFormat.LargeGroup"/>'s six beasts
        /// with four tiles to spare, so every format fits on every arena preset, and Large's seats
        /// the largest horde the encounter library draws (20 swarm enemies and 4 ranged).
        /// </para>
        /// </summary>
        public IReadOnlyList<HexCoordinate> GetDeploymentZone(BattleTeam team)
        {
            List<HexCoordinate> results = new List<HexCoordinate>();
            int lowestRow = team == BattleTeam.Player ? _deploymentRowThreshold : MinRow;
            int highestRow = team == BattleTeam.Player ? MaxRow : -_deploymentRowThreshold;

            for (int r = lowestRow; r <= highestRow; r++)
            {
                for (int column = MinColumn; column <= MaxColumn; column++)
                {
                    results.Add(FromOffset(column, r));
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
        /// Builds the tile set: the offset rectangle, <see cref="Width"/> tiles on each of the
        /// <see cref="Height"/> rows, row by row from the top and left to right along each row.
        /// </summary>
        private HashSet<HexCoordinate> GenerateTiles()
        {
            HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>();

            for (int r = MinRow; r <= MaxRow; r++)
            {
                for (int column = MinColumn; column <= MaxColumn; column++)
                {
                    tiles.Add(FromOffset(column, r));
                }
            }

            return tiles;
        }

        /// <summary>
        /// The inclusive r-range to walk for one q-column of a hexagon (a range disc) of
        /// <paramref name="radius"/> rings centred on the origin.
        /// <para>
        /// Sweeping q from <c>-radius</c> to <c>+radius</c> and r across the whole of that same
        /// span would trace a rhombus; clamping r against the implied third cube axis
        /// (<c>-q - r</c>, which must also stay within the radius) is what shears the rhombus back
        /// into a hexagon. <see cref="GetTilesInRange"/> walks it and keeps the in-bounds tiles.
        /// </para>
        /// </summary>
        private static void RingRowBounds(int radius, int q, out int lowerR, out int upperR)
        {
            lowerR = Math.Max(-radius, -q - radius);
            upperR = Math.Min(radius, -q + radius);
        }
    }
}
