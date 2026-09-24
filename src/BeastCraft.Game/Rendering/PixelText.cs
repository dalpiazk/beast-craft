using System;
using BeastCraft.Presentation.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// How the viewer draws text: every string goes through this seam, sized by its cap height in
    /// the current transform's pixels, so the built-in pixel font (<see cref="PixelText"/>) can be
    /// swapped for a real typeface later — e.g. a TTF rasterised at start-up into a glyph atlas (a
    /// runtime rasteriser such as FontStashSharp, a NuGet dependency to weigh before adding) or a
    /// pre-baked SpriteFont — without touching the layout code.
    /// </summary>
    public interface ITextRenderer : IDisposable
    {
        /// <summary>The width of <paramref name="text"/> at cap height <paramref name="size"/>.</summary>
        float Measure(string text, float size);

        /// <summary>The distance from one line's top to the next's at <paramref name="size"/>.</summary>
        float LineHeight(float size);

        /// <summary>Draws <paramref name="text"/> with its top-left at <paramref name="topLeft"/>, with a drop shadow when <paramref name="shadow"/> is set.</summary>
        void Draw(SpriteRenderer renderer, string text, Vector2 topLeft, float size, Color color, Color? shadow = null);
    }

    /// <summary>Alignment helpers over any <see cref="ITextRenderer"/>.</summary>
    public static class TextRendererExtensions
    {
        public static void DrawCentered(this ITextRenderer text, SpriteRenderer renderer, string value, float centerX, float y, float size, Color color,
                                        Color? shadow = null)
        {
            text.Draw(renderer, value, new Vector2(centerX - text.Measure(value, size) / 2f, y), size, color, shadow);
        }

        public static void DrawRight(this ITextRenderer text, SpriteRenderer renderer, string value, float right, float y, float size, Color color,
                                     Color? shadow = null)
        {
            text.Draw(renderer, value, new Vector2(right - text.Measure(value, size), y), size, color, shadow);
        }

        /// <summary><paramref name="value"/> cut (with no ellipsis) to fit <paramref name="width"/>.</summary>
        public static string Fit(this ITextRenderer text, string value, float size, float width)
        {
            if (string.IsNullOrEmpty(value) || text.Measure(value, size) <= width)
            {
                return value;
            }

            int length = value.Length;
            while (length > 0 && text.Measure(value.Substring(0, length), size) > width)
            {
                length--;
            }

            return value.Substring(0, length);
        }
    }

    /// <summary>
    /// The built-in <see cref="PixelFont"/> (3x5 capitals) as one texture, drawn at the whole-number
    /// multiple of its 5 px glyph height nearest the asked size, point-sampled. The placeholder
    /// <see cref="ITextRenderer"/>: it matches the pixel placeholders and needs no font file.
    /// </summary>
    public sealed class PixelText : ITextRenderer
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

        public float Measure(string text, float size)
        {
            return PixelFont.Measure(text ?? string.Empty) * Scale(size);
        }

        public float LineHeight(float size)
        {
            return (PixelFont.GlyphHeight + 2) * Scale(size);
        }

        public void Draw(SpriteRenderer renderer, string text, Vector2 topLeft, float size, Color color, Color? shadow = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int scale = Scale(size);
            SpriteBatch batch = renderer.Batch(SamplerState.PointClamp);
            if (shadow.HasValue)
            {
                DrawRun(batch, text, topLeft.X + scale, topLeft.Y + scale, shadow.Value, scale);
            }

            DrawRun(batch, text, topLeft.X, topLeft.Y, color, scale);
        }

        public void Dispose()
        {
            _atlas.Dispose();
        }

        /// <summary>The whole-number glyph scale for a cap height of <paramref name="size"/> (at least 1).</summary>
        private static int Scale(float size)
        {
            return Math.Max(1, (int)Math.Round(size / PixelFont.GlyphHeight));
        }

        private void DrawRun(SpriteBatch batch, string text, float x, float y, Color color, int scale)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    continue;
                }

                Rectangle source = new Rectangle(PixelFont.IndexOf(text[i]) * PixelFont.GlyphWidth, 0, PixelFont.GlyphWidth, PixelFont.GlyphHeight);
                batch.Draw(_atlas, new Vector2(x + i * PixelFont.Advance * scale, y), source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
        }
    }
}
