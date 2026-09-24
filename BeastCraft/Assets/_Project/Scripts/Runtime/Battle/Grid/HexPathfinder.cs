using System.Collections.Generic;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A* shortest-path search across a <see cref="HexGrid"/>, honouring terrain blocking and unit
    /// occupancy via <see cref="HexGrid.IsPassable"/>.
    /// <para>
    /// Every step costs 1, matching the design's "move range measured in grid steps" model; there
    /// is no per-tile movement cost yet. With a uniform step cost, <see cref="HexCoordinate.Distance"/>
    /// is both admissible and consistent as a heuristic (it never overestimates, because it is the
    /// step count on an empty board), so a tile can be settled the first time it is expanded and
    /// never needs revisiting.
    /// </para>
    /// <para>
    /// <strong>Which open tile is expanded next</strong> is fixed, so the same board always yields
    /// the same path — a re-simulation elsewhere has to agree: the lowest f-score (steps taken plus
    /// estimated steps left), ties to the tile closer to the goal, and failing that to the tile
    /// queued first. A tile whose cost improves while it is queued keeps its place in the queue.
    /// </para>
    /// <para>
    /// Hand-rolled on purpose: <c>System.Collections.Generic.PriorityQueue</c> needs .NET 6+, which
    /// the netstandard2.1 target this code is compiled against in CI does not have. The open set is
    /// a binary heap keyed on exactly that order (f, then remaining distance, then the order tiles
    /// were queued in), with the cost decrease above done in place; it picks the same tile a linear
    /// scan of a queue-ordered list would. Per-tile search state (cost so far, came-from, open and
    /// closed marks) lives in flat arrays indexed by <see cref="HexGrid.TileIndex"/>, reused across
    /// calls on the same thread and invalidated by bumping a generation stamp rather than by
    /// clearing, so a search allocates nothing but the path it returns. The balance simulator runs
    /// millions of these searches; the storage is the only thing that is tuned for it.
    /// </para>
    /// </summary>
    public static class HexPathfinder
    {
        /// <summary>
        /// <see cref="HexCoordinate.AxialDirections"/> as a plain array (same six steps, same
        /// order), for the hot neighbour loops here and in <see cref="BattleTurnExecutor"/>. Never
        /// written after it is built.
        /// </summary>
        internal static readonly HexCoordinate[] Directions = CopyDirections();

        [System.ThreadStatic]
        private static Scratch _scratch;

        /// <summary>
        /// The cheapest route from <paramref name="start"/> to <paramref name="goal"/> for the unit
        /// named by <paramref name="movingUnitId"/>, as an ordered list of tiles including both
        /// endpoints.
        /// <para>
        /// Every tile but <paramref name="start"/> must satisfy
        /// <see cref="HexGrid.IsPassable"/> — the mover is already standing on its start tile, so
        /// that one is walked off regardless of what sits there.
        /// </para>
        /// <para>
        /// Returns an empty list — never <c>null</c>, and never throwing — when there is no route:
        /// the grid is null, either endpoint is off the board, the goal is impassable, or the goal
        /// is simply walled off. The unreachable case terminates promptly rather than hanging:
        /// every tile enters the closed set at most once and only in-bounds tiles are ever queued,
        /// so the search is bounded by <see cref="HexGrid.TileCount"/> expansions.
        /// </para>
        /// <para>
        /// A start equal to the goal yields a single-element list holding it, which is the
        /// "already there, zero steps" answer rather than a failure.
        /// </para>
        /// <para>
        /// <strong>One narrow consistency check, not validation.</strong> When
        /// <paramref name="movingUnitId"/> names a unit the grid <em>does</em> have on the board,
        /// and that unit is recorded on some tile other than <paramref name="start"/>, this returns
        /// an empty list: the caller has asked to route a unit from where it is not, and the answer
        /// would be a path the mover could never actually walk. A unit the grid has never heard of
        /// is a different thing entirely and is explicitly allowed — a hypothetical or preview query
        /// ("where would this unit go if it stood here?") has nothing to be inconsistent with, and
        /// so is searched normally. Nothing beyond that is checked: this is not a guarantee that the
        /// caller's world state is coherent, only a refusal of the one incoherence that is cheap to
        /// spot and certain to produce a wrong answer.
        /// </para>
        /// </summary>
        public static IReadOnlyList<HexCoordinate> FindPath(HexGrid grid, HexCoordinate start, HexCoordinate goal, string movingUnitId)
        {
            if (grid == null || !grid.IsInBounds(start) || !grid.IsInBounds(goal))
            {
                return new List<HexCoordinate>();
            }

            // The mover claims to be standing on `start`; if the grid has it on record somewhere
            // else, the two disagree and every tile after the first would be fiction. A unit with
            // no record at all is not a disagreement -- see the remarks on preview queries.
            if (!string.IsNullOrEmpty(movingUnitId))
            {
                HexCoordinate recorded;
                if (grid.TryGetPosition(movingUnitId, out recorded) && recorded != start)
                {
                    return new List<HexCoordinate>();
                }
            }

            if (start == goal)
            {
                return new List<HexCoordinate> { start };
            }

            if (!grid.IsPassable(goal, movingUnitId))
            {
                return new List<HexCoordinate>();
            }

            Scratch search = _scratch;
            if (search == null)
            {
                search = new Scratch();
                _scratch = search;
            }

            search.Begin(grid.TileIndexCapacity);
            int generation = search.Generation;
            int[] seen = search.Seen;
            int[] closed = search.Closed;
            int[] costFromStart = search.Cost;
            HexCoordinate[] cameFrom = search.CameFrom;

            int startIndex = grid.TileIndex(start);
            seen[startIndex] = generation;
            costFromStart[startIndex] = 0;
            search.Push(startIndex, start, start.Distance(goal));

            while (search.Count > 0)
            {
                int currentIndex = search.Pop();
                HexCoordinate current = search.Tile[currentIndex];

                if (current == goal)
                {
                    return BuildPath(grid, cameFrom, costFromStart[currentIndex], start, goal);
                }

                closed[currentIndex] = generation;
                int stepsToCurrent = costFromStart[currentIndex];

                // Walks the shared direction table and offsets in place rather than calling
                // HexCoordinate.Neighbors(), which would allocate a six-element array on every
                // single node expansion. Same six tiles, same fixed order.
                HexCoordinate[] directions = Directions;
                for (int i = 0; i < directions.Length; i++)
                {
                    HexCoordinate neighbor = current + directions[i];
                    int neighborIndex = grid.TileIndex(neighbor);

                    // Off-board tiles (index -1) are impassable, so they never reach the arrays.
                    if (!grid.IsPassableAt(neighborIndex, movingUnitId) || closed[neighborIndex] == generation)
                    {
                        continue;
                    }

                    int tentativeCost = stepsToCurrent + 1;

                    if (seen[neighborIndex] == generation && tentativeCost >= costFromStart[neighborIndex])
                    {
                        continue;
                    }

                    cameFrom[neighborIndex] = current;
                    costFromStart[neighborIndex] = tentativeCost;
                    seen[neighborIndex] = generation;

                    if (search.IsQueued(neighborIndex))
                    {
                        // Cheaper route to a tile already queued: same place in the queue, lower f.
                        search.Improved(neighborIndex);
                    }
                    else
                    {
                        search.Push(neighborIndex, neighbor, neighbor.Distance(goal));
                    }
                }
            }

            return new List<HexCoordinate>();
        }

        private static HexCoordinate[] CopyDirections()
        {
            IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;
            HexCoordinate[] copy = new HexCoordinate[directions.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = directions[i];
            }

            return copy;
        }

        /// <summary>
        /// Walks the came-from chain back from the goal and reverses it, so the caller gets the
        /// path in travel order, start first.
        /// </summary>
        private static List<HexCoordinate> BuildPath(HexGrid grid, HexCoordinate[] cameFrom, int steps, HexCoordinate start, HexCoordinate goal)
        {
            List<HexCoordinate> path = new List<HexCoordinate>(steps + 1);

            HexCoordinate step = goal;
            path.Add(step);

            while (step != start)
            {
                step = cameFrom[grid.TileIndex(step)];
                path.Add(step);
            }

            path.Reverse();
            return path;
        }

        /// <summary>
        /// One thread's reusable search state, all indexed by <see cref="HexGrid.TileIndex"/>. A
        /// tile's <see cref="Cost"/> and <see cref="CameFrom"/> mean something only while its
        /// <see cref="Seen"/> stamp is the current <see cref="Generation"/>, and it is closed only
        /// while its <see cref="Closed"/> stamp is. The open set is a binary heap of tile indices
        /// ordered by <see cref="Precedes"/>.
        /// </summary>
        private sealed class Scratch
        {
            public int Generation;
            public int[] Seen = new int[0];
            public int[] Closed = new int[0];
            public int[] Cost = new int[0];
            public HexCoordinate[] CameFrom = new HexCoordinate[0];

            /// <summary>The coordinate of each queued tile index.</summary>
            public HexCoordinate[] Tile = new HexCoordinate[0];

            /// <summary>Per tile: its distance to the goal (the heuristic; fixed per search).</summary>
            private int[] _remaining = new int[0];

            /// <summary>Per tile: when it was queued, the final tie-break.</summary>
            private int[] _queuedAt = new int[0];

            /// <summary>Per tile: its slot in <see cref="_heap"/> while queued, else -1.</summary>
            private int[] _heapSlot = new int[0];

            private int[] _heap = new int[0];
            private int _queued;

            /// <summary>How many tiles are queued.</summary>
            public int Count { get; private set; }

            /// <summary>Starts a new search over a board of <paramref name="capacity"/> tile indices.</summary>
            public void Begin(int capacity)
            {
                if (Seen.Length < capacity)
                {
                    Seen = new int[capacity];
                    Closed = new int[capacity];
                    Cost = new int[capacity];
                    CameFrom = new HexCoordinate[capacity];
                    Tile = new HexCoordinate[capacity];
                    _remaining = new int[capacity];
                    _queuedAt = new int[capacity];
                    _heapSlot = new int[capacity];
                    for (int i = 0; i < capacity; i++)
                    {
                        _heapSlot[i] = -1;
                    }

                    _heap = new int[capacity];
                    Generation = 0;
                }

                if (Generation == int.MaxValue)
                {
                    System.Array.Clear(Seen, 0, Seen.Length);
                    System.Array.Clear(Closed, 0, Closed.Length);
                    Generation = 0;
                }

                // A search that returned early (goal found) can leave tiles queued; unmark them.
                for (int i = 0; i < Count; i++)
                {
                    _heapSlot[_heap[i]] = -1;
                }

                Generation++;
                Count = 0;
                _queued = 0;
            }

            public bool IsQueued(int tile)
            {
                return _heapSlot[tile] >= 0;
            }

            public void Push(int tile, HexCoordinate coordinate, int remaining)
            {
                Tile[tile] = coordinate;
                _remaining[tile] = remaining;
                _queuedAt[tile] = _queued++;
                int slot = Count++;
                _heap[slot] = tile;
                _heapSlot[tile] = slot;
                SiftUp(slot);
            }

            /// <summary>Removes and returns the tile that precedes every other queued tile.</summary>
            public int Pop()
            {
                int top = _heap[0];
                _heapSlot[top] = -1;
                Count--;

                if (Count > 0)
                {
                    int last = _heap[Count];
                    _heap[0] = last;
                    _heapSlot[last] = 0;
                    SiftDown(0);
                }

                return top;
            }

            /// <summary>A queued tile's cost just fell: it can only move toward the top.</summary>
            public void Improved(int tile)
            {
                SiftUp(_heapSlot[tile]);
            }

            /// <summary>
            /// The expansion order: lower f (cost so far plus remaining distance), then lower
            /// remaining distance, then queued earlier. A strict total order, since no two tiles
            /// are queued at the same moment.
            /// </summary>
            private bool Precedes(int a, int b)
            {
                int totalA = Cost[a] + _remaining[a];
                int totalB = Cost[b] + _remaining[b];
                if (totalA != totalB)
                {
                    return totalA < totalB;
                }

                if (_remaining[a] != _remaining[b])
                {
                    return _remaining[a] < _remaining[b];
                }

                return _queuedAt[a] < _queuedAt[b];
            }

            private void SiftUp(int slot)
            {
                int tile = _heap[slot];
                while (slot > 0)
                {
                    int parentSlot = (slot - 1) / 2;
                    int parent = _heap[parentSlot];
                    if (!Precedes(tile, parent))
                    {
                        break;
                    }

                    _heap[slot] = parent;
                    _heapSlot[parent] = slot;
                    slot = parentSlot;
                }

                _heap[slot] = tile;
                _heapSlot[tile] = slot;
            }

            private void SiftDown(int slot)
            {
                int tile = _heap[slot];
                while (true)
                {
                    int child = (2 * slot) + 1;
                    if (child >= Count)
                    {
                        break;
                    }

                    if (child + 1 < Count && Precedes(_heap[child + 1], _heap[child]))
                    {
                        child++;
                    }

                    if (!Precedes(_heap[child], tile))
                    {
                        break;
                    }

                    _heap[slot] = _heap[child];
                    _heapSlot[_heap[slot]] = slot;
                    slot = child;
                }

                _heap[slot] = tile;
                _heapSlot[tile] = slot;
            }
        }
    }
}
