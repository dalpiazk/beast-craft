using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Presentation.Board
{
    /// <summary>
    /// Where a unit could move this turn, and the route a walk takes, on the battle's own board: the
    /// same <see cref="HexGrid.CanStand"/> and <see cref="HexPathfinder"/> rules the battle moves by,
    /// so obstacles (blocked tiles) and other units are never in a preview and a walk goes round them.
    /// Pure queries: the grid is read, never written.
    /// </summary>
    public static class MovementPreview
    {
        /// <summary>
        /// Every anchor a unit of <paramref name="footprint"/> standing on <paramref name="start"/>
        /// (<paramref name="unitId"/>, its own tiles counting as free) can reach in at most
        /// <paramref name="moveRange"/> steps, each step onto an anchor it can stand on. Excludes the
        /// start. A breadth-first search in the pathfinder's neighbour order, so the order is stable.
        /// </summary>
        public static List<HexCoordinate> Reach(HexGrid grid, HexCoordinate start, UnitFootprint footprint, string unitId, int moveRange)
        {
            List<HexCoordinate> reached = new List<HexCoordinate>();
            if (grid == null || moveRange <= 0)
            {
                return reached;
            }

            Dictionary<HexCoordinate, int> steps = new Dictionary<HexCoordinate, int> { { start, 0 } };
            Queue<HexCoordinate> open = new Queue<HexCoordinate>();
            open.Enqueue(start);
            while (open.Count > 0)
            {
                HexCoordinate at = open.Dequeue();
                int next = steps[at] + 1;
                if (next > moveRange)
                {
                    continue;
                }

                foreach (HexCoordinate direction in HexCoordinate.AxialDirections)
                {
                    HexCoordinate to = at + direction;
                    if (steps.ContainsKey(to) || !grid.CanStand(to, footprint, unitId))
                    {
                        continue;
                    }

                    steps.Add(to, next);
                    reached.Add(to);
                    open.Enqueue(to);
                }
            }

            return reached;
        }

        /// <summary>
        /// The anchors a walk from <paramref name="from"/> to <paramref name="to"/> passes through,
        /// both ends included, going round <paramref name="grid"/>'s obstacles (its blocked tiles;
        /// units are ignored, since the battle has already moved them): a shortest route on a copy
        /// holding only the obstacles. Just the two ends when there is no route or no grid.
        /// </summary>
        public static List<HexCoordinate> WalkPath(HexGrid grid, HexCoordinate from, HexCoordinate to, UnitFootprint footprint)
        {
            List<HexCoordinate> ends = new List<HexCoordinate> { from, to };
            if (grid == null || from == to)
            {
                return ends;
            }

            HexGrid terrain = new HexGrid(grid.Size);
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (grid.IsBlocked(tile))
                {
                    terrain.SetBlocked(tile, true);
                }
            }

            IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(terrain, from, to, null, footprint);
            return path == null || path.Count < 2 ? ends : new List<HexCoordinate>(path);
        }
    }
}
