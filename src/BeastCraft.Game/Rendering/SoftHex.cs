using System;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// A white, anti-aliased pointy-top hex the size of a tile's sprite box (drawn at
    /// <c>HexLayout.TileWidth</c> x <c>TileHeight</c>), made on the device: the tint the deployment
    /// zones and unit footprints use over a painted backdrop, where the pixel hex mask's stair-stepped
    /// edge would show. Premultiplied, mipmapped, drawn linear-filtered. Deterministic (supersampled
    /// coverage, no randomness).
    /// </summary>
    public static class SoftHex
    {
        /// <summary>Texture size: four texels to a board pixel of the 32x36 tile.</summary>
        public const int Width = 128;

        public const int Height = 144;

        private const int Samples = 4;

        public static Texture2D Create(GraphicsDevice device)
        {
            Texture2D texture = new Texture2D(device, Width, Height, true, SurfaceFormat.Color);
            Color[] data = new Color[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / Samples) / Width * 2f - 1f;
                            float v = (y + (sy + 0.5f) / Samples) / Height * 2f - 1f;
                            if (Inside(u, v))
                            {
                                inside++;
                            }
                        }
                    }

                    int a = inside * 255 / (Samples * Samples);
                    data[y * Width + x] = new Color(a, a, a, a);
                }
            }

            int w = Width;
            int h = Height;
            for (int level = 0; level < texture.LevelCount; level++)
            {
                if (level > 0)
                {
                    data = Halve(data, ref w, ref h);
                }

                texture.SetData(level, null, data, 0, data.Length);
            }

            return texture;
        }

        /// <summary>
        /// Whether (<paramref name="u"/>, <paramref name="v"/>) (-1 to 1 across the box) is inside the
        /// pointy-top hex whose side corners sit a quarter of the height from the top and bottom.
        /// </summary>
        public static bool Inside(float u, float v)
        {
            float au = Math.Abs(u);
            float av = Math.Abs(v);
            return au <= 1f && av <= 1f - au / 2f;
        }

        private static Color[] Halve(Color[] source, ref int width, ref int height)
        {
            int w = Math.Max(1, width / 2);
            int h = Math.Max(1, height / 2);
            Color[] result = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            sum += source[Math.Min(height - 1, 2 * y + dy) * width + Math.Min(width - 1, 2 * x + dx)].A;
                        }
                    }

                    int a = (sum + 2) / 4;
                    result[y * w + x] = new Color(a, a, a, a);
                }
            }

            width = w;
            height = h;
            return result;
        }
    }
}
