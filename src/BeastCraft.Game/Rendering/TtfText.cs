using System;
using System.IO;
using BeastCraft.Presentation.Content;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The UI typeface (<see cref="GameContent.UiFontPath"/>, a TrueType font) behind
    /// <see cref="ITextRenderer"/>, rasterised at run time by FontStashSharp into a glyph atlas.
    /// Sizes keep <see cref="ITextRenderer"/>'s meaning — the cap height in the current space —
    /// and text is placed with its capitals' top at the given point, as the pixel font does, so
    /// layout code does not change with the font. Glyphs are rasterised at the size they land on
    /// screen (the current transform's scale), rounded to a few pixel sizes so a zooming camera
    /// does not fill the atlas, and drawn with linear filtering. <see cref="TryLoad"/> fails (and
    /// the viewer keeps <see cref="PixelText"/>) when the font is missing or unreadable.
    /// </summary>
    public sealed class TtfText : ITextRenderer
    {
        private const float ReferencePx = 64f;

        private readonly FontSystem _fonts;
        private readonly SpriteFontBase _reference;
        private readonly float _capPerPx;
        private readonly float _capTopPerPx;

        private TtfText(FontSystem fonts)
        {
            _fonts = fonts;
            _reference = fonts.GetFont(ReferencePx);
            Bounds cap = _reference.TextBounds("H", Vector2.Zero);
            float height = cap.Y2 - cap.Y;
            if (!(height > 1f))
            {
                throw new InvalidDataException("The font has no capital H.");
            }

            _capPerPx = height / ReferencePx;
            _capTopPerPx = cap.Y / ReferencePx;
        }

        /// <summary>The loaded font's name, for the log.</summary>
        public string Name { get; private set; }

        /// <summary>
        /// The UI font from <paramref name="source"/> at <paramref name="path"/> (content-root
        /// relative); null with <paramref name="error"/> set when it is missing (an un-pulled Git LFS
        /// pointer included) or cannot be read as a font.
        /// </summary>
        public static TtfText TryLoad(IContentSource source, string path, out string error)
        {
            error = null;
            try
            {
                if (source == null || !source.Exists(path))
                {
                    error = "no " + path;
                    return null;
                }

                byte[] data;
                using (Stream stream = source.Open(path))
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    data = memory.ToArray();
                }

                if (data.Length < 12 || data[0] == (byte)'v')
                {
                    error = path + " is not a font (a Git LFS pointer? run git lfs pull)";
                    return null;
                }

                FontSystem fonts = new FontSystem();
                fonts.AddFont(data);
                return new TtfText(fonts) { Name = Path.GetFileNameWithoutExtension(path) };
            }
            catch (Exception exception)
            {
                error = path + ": " + exception.Message;
                return null;
            }
        }

        public float Measure(string text, float size)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            return _reference.MeasureString(text).X * PixelSize(size) / ReferencePx;
        }

        public float LineHeight(float size)
        {
            // Capitals, descenders and a little air: about 1.55 cap heights for this family.
            return PixelSize(size) * 0.9f;
        }

        public void Draw(SpriteRenderer renderer, string text, Vector2 topLeft, float size, Color color, Color? shadow = null)
        {
            if (string.IsNullOrEmpty(text) || size <= 0f)
            {
                return;
            }

            float px = PixelSize(size);
            float screen = Math.Max(0.01f, renderer.TransformScale);
            float raster = Quantize(px * screen);
            SpriteFontBase font = _fonts.GetFont(raster);
            float k = px / raster;
            Vector2 at = new Vector2(topLeft.X, topLeft.Y - _capTopPerPx * px);
            SpriteBatch batch = renderer.Batch(SamplerState.LinearClamp);
            if (shadow.HasValue)
            {
                float offset = Math.Max(1f / screen, size * 0.08f);
                font.DrawText(batch, text, at + new Vector2(offset, offset), shadow.Value, 0f, Vector2.Zero, new Vector2(k, k));
            }

            font.DrawText(batch, text, at, color, 0f, Vector2.Zero, new Vector2(k, k));
        }

        public void Dispose()
        {
            _fonts.Dispose();
        }

        /// <summary>The font's pixel size (FontStashSharp's size) whose capitals are <paramref name="capHeight"/> tall.</summary>
        private float PixelSize(float capHeight)
        {
            return capHeight / _capPerPx;
        }

        /// <summary>Rasterised sizes: whole pixels up to 32, then steps of 4, so zooming reuses a few atlas entries.</summary>
        private static float Quantize(float px)
        {
            float rounded = px <= 32f ? (float)Math.Round(px) : (float)Math.Round(px / 4f) * 4f;
            return Math.Max(6f, Math.Min(256f, rounded));
        }
    }
}
