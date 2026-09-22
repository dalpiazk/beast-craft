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
    /// Hand-rolled on purpose. Boards top out at 169 tiles, so the open set is a plain list scanned
    /// linearly for the lowest f-score: a binary heap would be more code for no measurable win, and
    /// <c>System.Collections.Generic.PriorityQueue</c> needs .NET 6+, which the netstandard2.1
    /// target this code is compiled against in CI does not have.
    /// </para>
    /// </summary>
    public static class HexPathfinder
    {
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
            List<HexCoordinate> path = new List<HexCoordinate>();

            if (grid == null || !grid.IsInBounds(start) || !grid.IsInBounds(goal))
            {
                return path;
            }

            // The mover claims to be standing on `start`; if the grid has it on record somewhere
            // else, the two disagree and every tile after the first would be fiction. A unit with
            // no record at all is not a disagreement -- see the remarks on preview queries.
            if (!string.IsNullOrEmpty(movingUnitId))
            {
                HexCoordinate recorded;
                if (grid.TryGetPosition(movingUnitId, out recorded) && recorded != start)
                {
                    return path;
                }
            }

            if (start == goal)
            {
                path.Add(start);
                return path;
            }

            if (!grid.IsPassable(goal, movingUnitId))
            {
                return path;
            }

            // Open set as a parallel list/set pair: the list is what gets scanned for the best
            // node, the set is the O(1) membership test that keeps the scan from being the only
            // way to ask "is this queued already".
            List<HexCoordinate> open = new List<HexCoordinate>();
            HashSet<HexCoordinate> openLookup = new HashSet<HexCoordinate>();
            HashSet<HexCoordinate> closed = new HashSet<HexCoordinate>();
            Dictionary<HexCoordinate, HexCoordinate> cameFrom = new Dictionary<HexCoordinate, HexCoordinate>();
            Dictionary<HexCoordinate, int> costFromStart = new Dictionary<HexCoordinate, int>();

            open.Add(start);
            openLookup.Add(start);
            costFromStart[start] = 0;

            while (open.Count > 0)
            {
                HexCoordinate current = TakeCheapest(open, costFromStart, goal);
                openLookup.Remove(current);

                if (current == goal)
                {
                    return BuildPath(cameFrom, start, goal);
                }

                closed.Add(current);
                int stepsToCurrent = costFromStart[current];

                // Walks the shared direction table and offsets in place rather than calling
                // HexCoordinate.Neighbors(), which would allocate a six-element array on every
                // single node expansion. Same six tiles, same fixed order.
                IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;
                for (int i = 0; i < directions.Count; i++)
                {
                    HexCoordinate neighbor = current + directions[i];
                    if (closed.Contains(neighbor) || !grid.IsPassable(neighbor, movingUnitId))
                    {
                        continue;
                    }

                    int tentativeCost = stepsToCurrent + 1;

                    int knownCost;
                    if (costFromStart.TryGetValue(neighbor, out knownCost) && tentativeCost >= knownCost)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    costFromStart[neighbor] = tentativeCost;

                    if (openLookup.Add(neighbor))
                    {
                        open.Add(neighbor);
                    }
                }
            }

            return path;
        }

        /// <summary>
        /// Pops the open-set entry with the lowest f-score (steps taken plus estimated steps left).
        /// Ties go to the node closer to the goal, and failing that to the one queued first, so the
        /// same board always yields the same path — a re-simulation elsewhere has to agree.
        /// </summary>
        private static HexCoordinate TakeCheapest(List<HexCoordinate> open, Dictionary<HexCoordinate, int> costFromStart, HexCoordinate goal)
        {
            int bestIndex = 0;
            int bestRemaining = open[0].Distance(goal);
            int bestTotal = costFromStart[open[0]] + bestRemaining;

            for (int i = 1; i < open.Count; i++)
            {
                int remaining = open[i].Distance(goal);
                int total = costFromStart[open[i]] + remaining;

                if (total < bestTotal || (total == bestTotal && remaining < bestRemaining))
                {
                    bestIndex = i;
                    bestTotal = total;
                    bestRemaining = remaining;
                }
            }

            HexCoordinate cheapest = open[bestIndex];
            open.RemoveAt(bestIndex);
            return cheapest;
        }

        /// <summary>
        /// Walks the came-from chain back from the goal and reverses it, so the caller gets the
        /// path in travel order, start first.
        /// </summary>
        private static List<HexCoordinate> BuildPath(Dictionary<HexCoordinate, HexCoordinate> cameFrom, HexCoordinate start, HexCoordinate goal)
        {
            List<HexCoordinate> path = new List<HexCoordinate>();

            HexCoordinate step = goal;
            path.Add(step);

            while (step != start)
            {
                step = cameFrom[step];
                path.Add(step);
            }

            path.Reverse();
            return path;
        }
    }
}
