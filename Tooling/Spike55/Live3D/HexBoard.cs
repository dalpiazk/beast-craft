using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>A hex grid on the XZ plane (Y up) mirroring the real game's own hex convention
    /// (<c>src/BeastCraft.Presentation/Board/HexLayout.cs</c>: pointy-top, "each row is shifted half a
    /// column right per row down", the same ColumnStep:RowStep ratio as that layout's 32:27 pixel
    /// tiles) instead of an arbitrary flat-top grid -- a lead-review fix: the first version used a
    /// flat-top layout with a hex size picked with no reference to the game's own scale, which packed
    /// 24 beasts into overlapping clumps (spacing narrower than a beast's own wingspan). `ColumnStep`
    /// is now sized from the body mesh's own bind-pose bounding box (see Game1's call to
    /// <see cref="SetScale"/>) so a beast at rest roughly fills, rather than bursts out of, its own
    /// hex cell -- matching how <c>docs/spikes/055/board_mock_lowpoly.png</c> (this spike's own
    /// earlier board mock) reads. Default arena is 8 wide x 11 tall ("Medium", the same size the
    /// Verdant Hollow backdrop this spike uses -- content/art/backdrops/r01/sun0/medium.png -- is
    /// painted for, per docs/art/hollow-art-slots.md).</summary>
    public static class HexBoard
    {
        public const int ArenaWidth = 8;
        public const int ArenaHeight = 11;

        /// <summary>World-space horizontal distance between columns on the same row. Set once via
        /// <see cref="SetScale"/> from the beast mesh's own width so hex spacing is derived from the
        /// actual asset, not a guessed constant.</summary>
        public static float ColumnStep { get; private set; } = 1.5f;

        /// <summary>World-space vertical (Z) distance between rows -- same 27:32 ratio as the game's
        /// own HexLayout (RowStep:ColumnStep), so the board reads with the same proportions.</summary>
        public static float RowStep => ColumnStep * (27f / 32f);

        /// <summary>Hex corner radius (centre to corner), for drawing the grid lines only.</summary>
        public static float HexSize => ColumnStep / (float)Math.Sqrt(3.0);

        /// <summary>Sizes the hex grid from the beast's own bind-pose width. Lead-review fix round
        /// (2026-09-30, second pass): the original 1.4x clearance factor undersized every unit relative
        /// to its hex -- a direct pixel comparison against the real 2D battle screen
        /// (`BeastCraft.Desktop --screenshot ... --turns 0`, a player beast's sprite noticeably wider
        /// than its own hex, an enemy sprite filling roughly two-thirds of its hex) showed beasts should
        /// read as *larger* than one hex, not comfortably inside one with margin. 0.8x makes a beast
        /// about 1.25 hexes wide (`beastFootprint / 0.8`) -- "a bit larger than a hex", matching the 2D
        /// Griffin's wingspan overflowing its own hex in that reference shot -- while every other unit's
        /// own absolute size (e.g. the Swarmling, sized independently in `blender_export_live_swarmling
        /// .py`) reads proportionally larger on the same, now-smaller hex too.</summary>
        public static void SetScale(float beastFootprint)
        {
            ColumnStep = Math.Max(0.6f, beastFootprint * 0.8f);
        }

        /// <summary>Pointy-top hex centre for offset coordinates (col, row), matching
        /// HexLayout.CenterPixel's formula (x = ColumnStep*col + ColumnStep/2*row, y = RowStep*row) with
        /// screen-y replaced by world-z.</summary>
        public static Vector3 CellCenter(int col, int row)
        {
            float x = ColumnStep * col + ColumnStep / 2f * row;
            float z = RowStep * row;
            return new Vector3(x, 0f, z);
        }

        public static VertexPositionColor[] BuildGridLines(int cols, int rows, Color color)
        {
            var lines = new List<VertexPositionColor>();
            float size = HexSize;
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    var center = CellCenter(col, row);
                    for (int i = 0; i < 6; i++)
                    {
                        // Pointy-top corners: 60*i - 30 degrees (flat-top would be 60*i).
                        double a0 = Math.PI / 3.0 * i - Math.PI / 6.0;
                        double a1 = Math.PI / 3.0 * (i + 1) - Math.PI / 6.0;
                        var p0 = center + new Vector3(size * (float)Math.Cos(a0), 0f, size * (float)Math.Sin(a0));
                        var p1 = center + new Vector3(size * (float)Math.Cos(a1), 0f, size * (float)Math.Sin(a1));
                        lines.Add(new VertexPositionColor(p0, color));
                        lines.Add(new VertexPositionColor(p1, color));
                    }
                }
            }
            return lines.ToArray();
        }

        /// <summary>A horde-formation fill order for placing up to `count` beasts: back rows first,
        /// left to right, centred on the arena's middle column by default -- reads like an encounter
        /// lineup (docs/spikes/055-3d-mini-spike.md's fourth-pass screenshots) rather than a
        /// nearest-to-centre spiral. `centerColOverride` (producer-feedback fix round: "offset the swarm
        /// a little so facing varies naturally") shifts which column the fill centres on, so two sides
        /// built with different overrides don't end up perfectly column-aligned -- real nearest-enemy
        /// facing (Game1.UpdateFacing) on a perfectly mirrored formation computes a near-zero yaw for
        /// almost everyone (see docs/spikes/055-3d-mini-spike.md section 2.12's "honest finding"); a
        /// believable, not-perfectly-mirrored encounter avoids that by construction.</summary>
        public static List<(int col, int row)> FillOrder(int cols, int rows, int count, int startRow = 0, int? centerColOverride = null)
        {
            var ordered = new List<(int col, int row)>();
            int centerCol = centerColOverride ?? cols / 2;
            for (int row = startRow; row < rows && ordered.Count < count; row++)
            {
                // Walk columns outward from the centre so a partially-filled row stays centred.
                var rowCols = new List<int> { centerCol };
                for (int d = 1; d <= cols; d++)
                {
                    if (centerCol - d >= 0)
                        rowCols.Add(centerCol - d);
                    if (centerCol + d < cols)
                        rowCols.Add(centerCol + d);
                }
                foreach (var col in rowCols)
                {
                    if (ordered.Count >= count)
                        break;
                    ordered.Add((col, row));
                }
            }
            return ordered;
        }
    }
}
