using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>A simple flat-top hex grid on the XZ plane (Y up), odd-column vertical offset layout --
    /// enough to give the battle board a recognisable hex-tactics look behind the beasts; not a port of
    /// the game's real hex-layout code (BeastCraft.Presentation has that, for 2D screen-space hexes --
    /// this is a from-scratch 3D-world version, purpose-built for this spike's camera).</summary>
    public static class HexBoard
    {
        public const float HexSize = 0.62f; // world units, centre-to-corner

        public static Vector3 CellCenter(int col, int row)
        {
            float x = HexSize * 1.5f * col;
            float z = HexSize * (float)Math.Sqrt(3.0) * (row + 0.5f * (col & 1));
            return new Vector3(x, 0f, z);
        }

        public static VertexPositionColor[] BuildGridLines(int cols, int rows, Color color)
        {
            var lines = new List<VertexPositionColor>();
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    var center = CellCenter(col, row);
                    for (int i = 0; i < 6; i++)
                    {
                        double a0 = Math.PI / 3.0 * i;
                        double a1 = Math.PI / 3.0 * (i + 1);
                        var p0 = center + new Vector3(HexSize * (float)Math.Cos(a0), 0f, HexSize * (float)Math.Sin(a0));
                        var p1 = center + new Vector3(HexSize * (float)Math.Cos(a1), 0f, HexSize * (float)Math.Sin(a1));
                        lines.Add(new VertexPositionColor(p0, color));
                        lines.Add(new VertexPositionColor(p1, color));
                    }
                }
            }
            return lines.ToArray();
        }

        /// <summary>Deterministic fill order for placing up to `count` beasts spaced out across the
        /// board (spiralling out from the centre cell), used by the stress test's 1/3/12/24 spawn steps.</summary>
        public static List<(int col, int row)> FillOrder(int cols, int rows, int count)
        {
            var all = new List<(int col, int row)>();
            for (int col = 0; col < cols; col++)
                for (int row = 0; row < rows; row++)
                    all.Add((col, row));

            int ccol = cols / 2, crow = rows / 2;
            all.Sort((a, b) =>
            {
                float da = (a.col - ccol) * (a.col - ccol) + (a.row - crow) * (a.row - crow);
                float db = (b.col - ccol) * (b.col - ccol) + (b.row - crow) * (b.row - crow);
                return da.CompareTo(db);
            });
            if (all.Count > count)
                all.RemoveRange(count, all.Count - count);
            return all;
        }
    }
}
