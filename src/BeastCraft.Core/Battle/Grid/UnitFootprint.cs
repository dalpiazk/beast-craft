using System.Collections.Generic;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// How many tiles a unit covers on the board, and in what shape. Every beast is
    /// <see cref="Single"/> (the roster validator refuses anything else); only large enemies are
    /// bigger: a boss (giant) is <see cref="Hex7"/>, a mini-boss (champion) <see cref="Triangle"/>.
    /// <para>
    /// A footprint is a fixed set of offsets from the unit's <em>anchor</em>, which is
    /// <see cref="BattleUnit.Position"/> and the tile the grid records it on
    /// (<see cref="HexGrid.TryGetPosition"/>). There is no rotation: a large unit translates, it
    /// never turns. See <see cref="Footprints"/> for the offsets and the design doc, "Unit
    /// footprints", for the rules built on them.
    /// </para>
    /// </summary>
    public enum UnitFootprint
    {
        /// <summary>One tile: the anchor. Every beast, the avatar and every ordinary enemy.</summary>
        Single = 0,

        /// <summary>
        /// Three tiles: the anchor and its first two <see cref="HexCoordinate.AxialDirections"/>
        /// neighbours, <c>(1, 0)</c> and <c>(1, -1)</c>, so the two-tile edge (both on the anchor's
        /// row) faces the player side. The mini-boss size.
        /// </summary>
        Triangle = 1,

        /// <summary>Seven tiles: the anchor (the centre) and all six neighbours. The boss size.</summary>
        Hex7 = 2
    }

    /// <summary>
    /// The tile offsets of each <see cref="UnitFootprint"/>, relative to the anchor. The first
    /// offset is always the anchor itself, <c>(0, 0)</c>; the rest follow
    /// <see cref="HexCoordinate.AxialDirections"/>' fixed order, so every walk over a footprint is
    /// deterministic. The tables are shared and never written.
    /// </summary>
    public static class Footprints
    {
        private static readonly HexCoordinate[] SingleOffsets = { HexCoordinate.Zero };

        private static readonly HexCoordinate[] TriangleOffsets =
        {
            HexCoordinate.Zero,
            new HexCoordinate(1, 0),
            new HexCoordinate(1, -1)
        };

        private static readonly HexCoordinate[] Hex7Offsets =
        {
            HexCoordinate.Zero,
            new HexCoordinate(1, 0),
            new HexCoordinate(1, -1),
            new HexCoordinate(0, -1),
            new HexCoordinate(-1, 0),
            new HexCoordinate(-1, 1),
            new HexCoordinate(0, 1)
        };

        /// <summary>The offsets of a footprint, anchor first. An unknown value reads as <see cref="UnitFootprint.Single"/>.</summary>
        public static IReadOnlyList<HexCoordinate> Offsets(UnitFootprint footprint)
        {
            return OffsetArray(footprint);
        }

        /// <summary>How many tiles a footprint covers: 1, 3 or 7.</summary>
        public static int TileCount(UnitFootprint footprint)
        {
            return OffsetArray(footprint).Length;
        }

        /// <summary>The tiles a unit of this footprint anchored on <paramref name="anchor"/> covers, anchor first.</summary>
        public static List<HexCoordinate> Tiles(HexCoordinate anchor, UnitFootprint footprint)
        {
            HexCoordinate[] offsets = OffsetArray(footprint);
            List<HexCoordinate> tiles = new List<HexCoordinate>(offsets.Length);
            for (int i = 0; i < offsets.Length; i++)
            {
                tiles.Add(anchor + offsets[i]);
            }

            return tiles;
        }

        /// <summary>The shared offset table itself, for hot loops that must not allocate. Never write to it.</summary>
        internal static HexCoordinate[] OffsetArray(UnitFootprint footprint)
        {
            switch (footprint)
            {
                case UnitFootprint.Triangle:
                    return TriangleOffsets;
                case UnitFootprint.Hex7:
                    return Hex7Offsets;
                default:
                    return SingleOffsets;
            }
        }
    }
}
