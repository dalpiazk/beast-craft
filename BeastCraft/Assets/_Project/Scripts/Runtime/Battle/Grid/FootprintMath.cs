namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// Distances that respect <see cref="UnitFootprint"/>s: how far a tile, or another unit, is
    /// from the <em>nearest</em> tile a unit covers. Every range and adjacency rule in battle is
    /// measured with these, so a giant is in reach as soon as any of its seven tiles is.
    /// <para>
    /// <strong>Single is exactly <see cref="HexCoordinate.Distance"/>.</strong> Every method
    /// short-circuits to the same arithmetic on the anchor when the footprints involved are
    /// <see cref="UnitFootprint.Single"/>, so a battle of size-1 units measures exactly what it
    /// measured before footprints existed (one enum compare more per call, nothing else).
    /// </para>
    /// </summary>
    public static class FootprintMath
    {
        /// <summary>
        /// Hex steps from <paramref name="tile"/> to the nearest tile of a footprint anchored on
        /// <paramref name="anchor"/> (0 when the tile is one of them). <see cref="UnitFootprint.Hex7"/>
        /// is the closed form <c>max(0, d - 1)</c> from the centre (the footprint is the radius-1
        /// disc, and hex distance to a disc is distance to its centre less its radius);
        /// <see cref="UnitFootprint.Triangle"/> is the minimum over its three tiles.
        /// </summary>
        public static int DistanceTo(HexCoordinate tile, HexCoordinate anchor, UnitFootprint footprint)
        {
            if (footprint == UnitFootprint.Single)
            {
                return tile.Distance(anchor);
            }

            if (footprint == UnitFootprint.Hex7)
            {
                int d = tile.Distance(anchor);
                return d > 0 ? d - 1 : 0;
            }

            HexCoordinate[] offsets = Footprints.OffsetArray(footprint);
            int best = int.MaxValue;
            for (int i = 0; i < offsets.Length; i++)
            {
                int d = tile.Distance(anchor + offsets[i]);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Hex steps between the nearest tiles of two units: 1 when they are adjacent, whatever
        /// their sizes. With either unit <see cref="UnitFootprint.Single"/> this is
        /// <see cref="DistanceTo"/> from that unit's tile; with both large, the minimum of
        /// <see cref="DistanceTo"/> over <paramref name="a"/>'s tiles.
        /// </summary>
        public static int UnitDistance(HexCoordinate a, UnitFootprint aFootprint, HexCoordinate b, UnitFootprint bFootprint)
        {
            if (aFootprint == UnitFootprint.Single)
            {
                return DistanceTo(a, b, bFootprint);
            }

            if (bFootprint == UnitFootprint.Single)
            {
                return DistanceTo(b, a, aFootprint);
            }

            HexCoordinate[] offsets = Footprints.OffsetArray(aFootprint);
            int best = int.MaxValue;
            for (int i = 0; i < offsets.Length; i++)
            {
                int d = DistanceTo(a + offsets[i], b, bFootprint);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        /// <summary><see cref="UnitDistance(HexCoordinate, UnitFootprint, HexCoordinate, UnitFootprint)"/> between two units where they stand.</summary>
        public static int UnitDistance(BattleUnit a, BattleUnit b)
        {
            if (a.Footprint == UnitFootprint.Single && b.Footprint == UnitFootprint.Single)
            {
                return a.Position.Distance(b.Position);
            }

            return UnitDistance(a.Position, a.Footprint, b.Position, b.Footprint);
        }

        /// <summary>
        /// The tile of a footprint anchored on <paramref name="anchor"/> nearest to the unit
        /// covering <paramref name="otherFootprint"/> from <paramref name="otherAnchor"/>; ties go to the
        /// earlier offset (<see cref="Footprints.Offsets"/>' order, anchor first). A
        /// <see cref="UnitFootprint.Single"/> footprint is its anchor.
        /// </summary>
        public static HexCoordinate NearestTile(HexCoordinate anchor, UnitFootprint footprint, HexCoordinate otherAnchor, UnitFootprint otherFootprint)
        {
            if (footprint == UnitFootprint.Single)
            {
                return anchor;
            }

            HexCoordinate[] offsets = Footprints.OffsetArray(footprint);
            HexCoordinate best = anchor;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < offsets.Length; i++)
            {
                HexCoordinate tile = anchor + offsets[i];
                int d = DistanceTo(tile, otherAnchor, otherFootprint);
                if (d < bestDistance)
                {
                    best = tile;
                    bestDistance = d;
                }
            }

            return best;
        }
    }
}
