using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Presentation.Board
{
    /// <summary>A straight line between two board points.</summary>
    public readonly struct Segment
    {
        public Segment(Vec2 from, Vec2 to)
        {
            From = from;
            To = to;
        }

        public Vec2 From { get; }

        public Vec2 To { get; }

        public float Length
        {
            get
            {
                float dx = To.X - From.X;
                float dy = To.Y - From.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    /// <summary>
    /// The hex grid drawn over a painted backdrop as lines: every edge of every tile, each shared
    /// edge once (so a semi-transparent line is equally faint everywhere), on the same pointy-top
    /// geometry as the tiles (<see cref="HexLayout.TileWidth"/> x <see cref="HexLayout.TileHeight"/>,
    /// whose corners meet their neighbours' exactly on the layout's whole-pixel steps). Pure.
    /// </summary>
    public static class HexGridLines
    {
        /// <summary>
        /// The six corners of <paramref name="tile"/> (board pixels), clockwise from the top:
        /// top, upper right, lower right, bottom, lower left, upper left.
        /// </summary>
        public static Vec2[] Corners(HexLayout layout, HexCoordinate tile)
        {
            Vec2 c = layout.Center(tile);
            float hw = HexLayout.TileWidth / 2f;
            float hh = HexLayout.TileHeight / 2f;
            float qh = HexLayout.TileHeight / 4f;
            return new[]
            {
                new Vec2(c.X, c.Y - hh), new Vec2(c.X + hw, c.Y - qh), new Vec2(c.X + hw, c.Y + qh), new Vec2(c.X, c.Y + hh), new Vec2(c.X - hw, c.Y + qh),
                new Vec2(c.X - hw, c.Y - qh)
            };
        }

        /// <summary>Every edge of <paramref name="tiles"/>, each once, in tile then corner order.</summary>
        public static List<Segment> Of(IEnumerable<HexCoordinate> tiles, HexLayout layout)
        {
            List<Segment> segments = new List<Segment>();
            HashSet<(long, long)> seen = new HashSet<(long, long)>();
            foreach (HexCoordinate tile in tiles ?? new HexCoordinate[0])
            {
                Vec2[] corners = Corners(layout, tile);
                for (int i = 0; i < corners.Length; i++)
                {
                    Vec2 a = corners[i];
                    Vec2 b = corners[(i + 1) % corners.Length];
                    if (seen.Add(Key(a, b)))
                    {
                        segments.Add(new Segment(a, b));
                    }
                }
            }

            return segments;
        }

        /// <summary>An order-free key for the edge a-b: corners lie on quarter pixels, so four times them rounded are exact integers.</summary>
        private static (long, long) Key(Vec2 a, Vec2 b)
        {
            long ka = Point(a);
            long kb = Point(b);
            return ka < kb ? (ka, kb) : (kb, ka);
        }

        private static long Point(Vec2 p)
        {
            long x = (long)Math.Round(p.X * 4f);
            long y = (long)Math.Round(p.Y * 4f);
            return (x << 32) ^ (y & 0xFFFFFFFFL);
        }
    }
}
