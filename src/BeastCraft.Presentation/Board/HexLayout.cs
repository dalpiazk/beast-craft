using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Layout;

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
        /// A layout with tile (0, 0) at the centre of the given screen rectangle, on whole pixels.
        /// (An arena's tiles can sit a quarter column off centre: see <see cref="BoardBounds"/>.)
        /// </summary>
        public static HexLayout Centered(int areaX, int areaY, int areaWidth, int areaHeight)
        {
            return new HexLayout(areaX + areaWidth / 2, areaY + areaHeight / 2);
        }

        /// <summary>
        /// The pixel bounds of every tile sprite of a <paramref name="width"/> x
        /// <paramref name="height"/> arena (<see cref="HexGrid"/>'s odd-r offset rectangle), in board
        /// space: tile (0, 0)'s centre is the origin. The odd rows jut half a column right of the
        /// even ones, so the box is <c>width + 1/2</c> columns wide, and for every preset it sits a
        /// quarter column off the origin (right on odd widths, left on even ones).
        /// </summary>
        public static Rect BoardBounds(int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int minColumn = -(w / 2);
            int maxColumn = minColumn + w - 1;
            int minRow = -((h - 1) / 2);
            int maxRow = minRow + h - 1;

            // Leftmost centre: an even row's first column (row 0 is always on the board). Rightmost:
            // an odd row's last column, half a step further right, whenever there is an odd row.
            float left = minColumn * ColumnStep - TileWidth / 2f;
            float right = maxColumn * ColumnStep + (h > 1 ? ColumnStep / 2f : 0f) + TileWidth / 2f;
            float top = minRow * RowStep - TileHeight / 2f;
            float bottom = maxRow * RowStep + TileHeight / 2f;
            return new Rect(left, top, right - left, bottom - top);
        }

        /// <summary>The pixel size of a <paramref name="width"/> x <paramref name="height"/> arena's tiles (<see cref="BoardBounds"/>): (width, height).</summary>
        public static (int Width, int Height) BoardSize(int width, int height)
        {
            Rect bounds = BoardBounds(width, height);
            return ((int)bounds.Width, (int)bounds.Height);
        }

        /// <summary>
        /// The off-board tiles that fill the half-tile notches along a <paramref name="width"/> x
        /// <paramref name="height"/> arena's zigzag sides: one per row, just past the row's short
        /// end (left of every odd row, right of every even row), so that drawn clipped to
        /// <see cref="BoardBounds"/> their inner halves square off both sides. Decoration only:
        /// none of them is on the board. Top to bottom.
        /// </summary>
        public static List<HexCoordinate> EdgeNotches(int width, int height)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int minColumn = -(w / 2);
            int maxColumn = minColumn + w - 1;
            int minRow = -((h - 1) / 2);
            List<HexCoordinate> notches = new List<HexCoordinate>(h);
            for (int row = minRow; row < minRow + h; row++)
            {
                bool odd = (row & 1) != 0;
                notches.Add(HexGrid.FromOffset(odd ? minColumn - 1 : maxColumn + 1, row));
            }

            return notches;
        }

        /// <summary>
        /// The pixel size of a hexagon of hexes of <paramref name="radius"/> rings (a range
        /// diagram's disc, not an arena): (width, height).
        /// </summary>
        public static (int Width, int Height) DiscSize(int radius)
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
