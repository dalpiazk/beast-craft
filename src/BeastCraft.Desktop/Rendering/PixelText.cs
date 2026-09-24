using System;
using BeastCraft.Presentation.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Desktop.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The built-in <see cref="PixelFont"/> as one texture (all glyphs in a row, built from code at
    /// start-up) and a way to draw strings with it at a whole-number scale.
    /// </summary>
    public sealed class PixelText : IDisposable
    {
        private readonly Texture2D _atlas;

        public PixelText(GraphicsDevice device)
        {
            string chars = PixelFont.Characters;
            int width = chars.Length * PixelFont.GlyphWidth;
            Color[] data = new Color[width * PixelFont.GlyphHeight];
            for (int i = 0; i < chars.Length; i++)
            {
                string[] rows = PixelFont.Glyph(chars[i]);
                for (int y = 0; y < PixelFont.GlyphHeight; y++)
                {
                    for (int x = 0; x < PixelFont.GlyphWidth; x++)
                    {
                        data[y * width + i * PixelFont.GlyphWidth + x] = rows[y][x] == '#' ? Color.White : Color.Transparent;
                    }
                }
            }

            _atlas = new Texture2D(device, width, PixelFont.GlyphHeight);
            _atlas.SetData(data);
        }

        /// <summary>Width of <paramref name="text"/> at <paramref name="scale"/>.</summary>
        public static int Measure(string text, int scale = 1)
        {
            return PixelFont.Measure(text) * scale;
        }

        /// <summary>Draws <paramref name="text"/> with its top-left at (x, y), with a one-pixel drop shadow when <paramref name="shadow"/> is set.</summary>
        public void Draw(SpriteBatch batch, string text, int x, int y, Color color, int scale = 1, Color? shadow = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (shadow.HasValue)
            {
                DrawRun(batch, text, x + scale, y + scale, shadow.Value, scale);
            }

            DrawRun(batch, text, x, y, color, scale);
        }

        /// <summary>Draws <paramref name="text"/> centred on x.</summary>
        public void DrawCentered(SpriteBatch batch, string text, int centerX, int y, Color color, int scale = 1, Color? shadow = null)
        {
            Draw(batch, text, centerX - Measure(text, scale) / 2, y, color, scale, shadow);
        }

        public void Dispose()
        {
            _atlas.Dispose();
        }

        private void DrawRun(SpriteBatch batch, string text, int x, int y, Color color, int scale)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    continue;
                }

                Rectangle source = new Rectangle(PixelFont.IndexOf(text[i]) * PixelFont.GlyphWidth, 0, PixelFont.GlyphWidth, PixelFont.GlyphHeight);
                Rectangle target = new Rectangle(x + i * PixelFont.Advance * scale, y, PixelFont.GlyphWidth * scale, PixelFont.GlyphHeight * scale);
                batch.Draw(_atlas, target, source, color);
            }
        }
    }
}
