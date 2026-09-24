using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Presentation.Board
{
    /// <summary>
    /// Where the battle's hex tiles sit on the virtual screen: a pointy-top layout on whole pixels,
    /// matching the pixel-art hex tiles (<c>hex_*.png</c>, 32x36): neighbouring columns are
    /// <see cref="ColumnStep"/> px apart, rows <see cref="RowStep"/> px apart, and each row is
    /// shifted half a column right per row down (axial <c>q</c> right, <c>r</c> down; the player's
    /// side, positive <c>r</c>, is the bottom of the screen). Integer steps keep every tile on the
    /// pixel grid, so nothing shimmers when scaled up.
    /// </summary>
    public readonly struct HexLayout
    {
        /// <summary>Tile sprite width in pixels.</summary>
        public const int TileWidth = 32;

        /// <summary>Tile sprite height in pixels.</summary>
        public const int TileHeight = 36;

        /// <summary>Horizontal distance between neighbouring tiles on a row.</summary>
        public const int ColumnStep = 32;

        /// <summary>Vertical distance between rows (three quarters of the tile height).</summary>
        public const int RowStep = 27;

        /// <param name="originX">Screen x of the centre of tile (0, 0).</param>
        /// <param name="originY">Screen y of the centre of tile (0, 0).</param>
        public HexLayout(int originX, int originY)
        {
            OriginX = originX;
            OriginY = originY;
        }

        public int OriginX { get; }

        public int OriginY { get; }

        /// <summary>
        /// A layout that centres the board (a hexagon around tile (0, 0), as every arena is) in the
        /// given screen rectangle, on whole pixels. A board bigger than the rectangle
        /// (<see cref="BoardSize"/>) overflows it evenly.
        /// </summary>
        public static HexLayout Centered(int areaX, int areaY, int areaWidth, int areaHeight)
        {
            return new HexLayout(areaX + areaWidth / 2, areaY + areaHeight / 2);
        }

        /// <summary>The board's pixel size for a hexagon board of <paramref name="radius"/>: (width, height).</summary>
        public static (int Width, int Height) BoardSize(int radius)
        {
            int r = Math.Max(0, radius);
            return ((2 * r + 1) * ColumnStep, TileHeight + 2 * r * RowStep);
        }

        /// <summary>The centre of tile <paramref name="hex"/>, in whole pixels.</summary>
        public (int X, int Y) CenterPixel(HexCoordinate hex)
        {
            // 2 * ColumnStep * q + ColumnStep * r, halved: exact because ColumnStep is even.
            return (OriginX + ColumnStep * hex.Q + ColumnStep / 2 * hex.R, OriginY + RowStep * hex.R);
        }

        /// <summary>The centre of tile <paramref name="hex"/>.</summary>
        public Vec2 Center(HexCoordinate hex)
        {
            (int x, int y) = CenterPixel(hex);
            return new Vec2(x, y);
        }

        /// <summary>Where the tile sprite's top-left corner goes.</summary>
        public (int X, int Y) TileTopLeft(HexCoordinate hex)
        {
            (int x, int y) = CenterPixel(hex);
            return (x - TileWidth / 2, y - TileHeight / 2);
        }

        /// <summary>The centre of a unit of <paramref name="footprint"/> anchored on <paramref name="anchor"/>: the mean of its tiles' centres.</summary>
        public Vec2 FootprintCenter(HexCoordinate anchor, UnitFootprint footprint)
        {
            List<HexCoordinate> tiles = Footprints.Tiles(anchor, footprint);
            float x = 0f;
            float y = 0f;
            foreach (HexCoordinate tile in tiles)
            {
                (int cx, int cy) = CenterPixel(tile);
                x += cx;
                y += cy;
            }

            return new Vec2(x / tiles.Count, y / tiles.Count);
        }

        /// <summary>
        /// The tile under a screen point (the inverse of <see cref="Center"/>, rounded to the nearest
        /// hex in cube coordinates). Not bounds-checked: ask the grid whether the tile exists.
        /// </summary>
        public HexCoordinate TileAt(float x, float y)
        {
            double r = (y - OriginY) / (double)RowStep;
            double q = (x - OriginX) / (double)ColumnStep - r / 2.0;
            return CubeRound(q, r);
        }

        /// <summary>Rounds fractional axial coordinates to the nearest hex (the standard cube rounding).</summary>
        public static HexCoordinate CubeRound(double q, double r)
        {
            double s = -q - r;
            double rq = Math.Round(q, MidpointRounding.AwayFromZero);
            double rr = Math.Round(r, MidpointRounding.AwayFromZero);
            double rs = Math.Round(s, MidpointRounding.AwayFromZero);
            double dq = Math.Abs(rq - q);
            double dr = Math.Abs(rr - r);
            double ds = Math.Abs(rs - s);

            if (dq > dr && dq > ds)
            {
                rq = -rr - rs;
            }
            else if (dr > ds)
            {
                rr = -rq - rs;
            }

            return new HexCoordinate((int)rq, (int)rr);
        }
    }
}
