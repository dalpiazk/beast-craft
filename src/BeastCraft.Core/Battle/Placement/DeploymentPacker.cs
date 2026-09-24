using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// Seats a side's units automatically, front-most first: the layout the balance simulator uses
    /// for enemies (and for its player teams), and a starting point for any encounter that places
    /// its enemies without an authored layout. Units of any <see cref="UnitFootprint"/> are packed
    /// greedily, in the order given: each takes the first anchor in <see cref="FrontOrder"/> whose
    /// whole footprint lies in the side's deployment zone (<see cref="HexGrid.FitsDeploymentZone"/>)
    /// on tiles that are free — not blocked, not occupied on the grid, and not taken by an earlier
    /// unit of the same pack.
    /// <para>
    /// For one-tile units this is exactly the first <c>n</c> tiles of <see cref="FrontOrder"/>
    /// (minus any already blocked or occupied). A large unit placed first takes the front-most
    /// anchor it fits at — a seven-tile boss on a Medium board sits centred on the middle row of
    /// its zone — and the units after it fill in around it. On a Small board (a zone two rows deep)
    /// a seven-tile unit fits nowhere, and packing fails.
    /// </para>
    /// <para>
    /// A pure query: the grid is read, never written. Deterministic.
    /// </para>
    /// </summary>
    public static class DeploymentPacker
    {
        /// <summary>
        /// Every tile of <paramref name="team"/>'s deployment zone, front-most first: nearest the
        /// centre line (smallest <c>|R|</c>), then outward from the board's vertical centre line
        /// (smallest <c>|2Q + R|</c>), then by <c>Q</c>. A strict total order.
        /// </summary>
        public static List<HexCoordinate> FrontOrder(HexGrid grid, BattleTeam team)
        {
            List<HexCoordinate> zone = new List<HexCoordinate>(grid.GetDeploymentZone(team));
            zone.Sort((a, b) =>
            {
                int byRow = Math.Abs(a.R).CompareTo(Math.Abs(b.R));
                if (byRow != 0)
                {
                    return byRow;
                }

                int byOffset = Math.Abs((2 * a.Q) + a.R).CompareTo(Math.Abs((2 * b.Q) + b.R));
                return byOffset != 0 ? byOffset : a.Q.CompareTo(b.Q);
            });

            return zone;
        }

        /// <summary>
        /// Packs units of the given footprints, in order, into <paramref name="team"/>'s zone (see
        /// the class notes). On success returns true and fills <paramref name="anchors"/> with one
        /// anchor per footprint and <paramref name="covered"/> with every tile the pack covers
        /// (each unit's tiles anchor first, in unit order). On failure returns false; the lists
        /// then hold the units that did fit, and the first that did not is at index
        /// <c>anchors.Count</c>.
        /// </summary>
        public static bool TryPack(HexGrid grid, BattleTeam team, IReadOnlyList<UnitFootprint> footprints, List<HexCoordinate> anchors,
                                   List<HexCoordinate> covered)
        {
            anchors.Clear();
            covered.Clear();

            if (grid == null || footprints == null)
            {
                return false;
            }

            List<HexCoordinate> order = FrontOrder(grid, team);
            HashSet<HexCoordinate> taken = new HashSet<HexCoordinate>();

            for (int u = 0; u < footprints.Count; u++)
            {
                UnitFootprint footprint = footprints[u];
                IReadOnlyList<HexCoordinate> offsets = Footprints.Offsets(footprint);
                bool placed = false;

                for (int i = 0; i < order.Count && !placed; i++)
                {
                    HexCoordinate anchor = order[i];

                    if (!grid.FitsDeploymentZone(anchor, footprint, team))
                    {
                        continue;
                    }

                    bool free = true;
                    for (int o = 0; o < offsets.Count && free; o++)
                    {
                        HexCoordinate tile = anchor + offsets[o];
                        free = !taken.Contains(tile) && !grid.IsBlocked(tile) && !grid.IsOccupied(tile);
                    }

                    if (!free)
                    {
                        continue;
                    }

                    anchors.Add(anchor);
                    for (int o = 0; o < offsets.Count; o++)
                    {
                        taken.Add(anchor + offsets[o]);
                        covered.Add(anchor + offsets[o]);
                    }

                    placed = true;
                }

                if (!placed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
