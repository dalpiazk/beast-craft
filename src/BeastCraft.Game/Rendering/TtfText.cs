using System;
using System.IO;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Text;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The UI's two typefaces (<see cref="GameContent.UiFontPath"/>, Fredoka, <see cref="UiFontFace.Heading"/>;
    /// <see cref="GameContent.UiBodyFontPath"/>, Atkinson Hyperlegible, <see cref="UiFontFace.Body"/>) behind
    /// <see cref="ITextRenderer"/>, each rasterised at run time by FontStashSharp into its own glyph
    /// atlas. Sizes keep <see cref="ITextRenderer"/>'s meaning — the cap height in the current space
    /// — and text is placed with its capitals' top at the given point, as the pixel font does, so
    /// layout code does not change with the font. Glyphs are rasterised at the size they land on
    /// screen (the current transform's scale), rounded to a few pixel sizes so a zooming camera does
    /// not fill the atlas, and drawn with linear filtering. <see cref="TryLoad"/> fails (and the
    /// viewer keeps <see cref="PixelText"/>) only when the heading font is missing or unreadable; a
    /// missing or unreadable body font is not fatal — <see cref="UiFontFace.Body"/> then draws in the
    /// heading face instead (logged once, by the caller, from <see cref="BodyError"/>).
    /// </summary>
    public sealed class TtfText : ITextRenderer
    {
        private const float ReferencePx = 64f;

        private readonly Face _heading;
        private readonly Face _body;

        private TtfText(Face heading, Face body)
        {
            _heading = heading;
            _body = body ?? heading;
        }

        /// <summary>The loaded heading font's name, for the log.</summary>
        public string Name { get; private set; }

        /// <summary>Set when the body font could not be loaded (the heading face is used for <see cref="UiFontFace.Body"/> text instead); null when it loaded, or none was asked for.</summary>
        public string BodyError { get; private set; }

        /// <summary>
        /// The heading font from <paramref name="source"/> at <paramref name="headingPath"/>
        /// (content-root relative; required — null with <paramref name="error"/> set when it is
        /// missing, an un-pulled Git LFS pointer included, or cannot be read as a font) and, from
        /// <paramref name="bodyPath"/>, the body font (optional: a problem loading it does not fail
        /// this call, only sets <see cref="BodyError"/> on the result).
        /// </summary>
        public static TtfText TryLoad(IContentSource source, string headingPath, string bodyPath, out string error)
        {
            Face heading = LoadFace(source, headingPath, out error);
            if (heading == null)
            {
                return null;
            }

            string bodyError = null;
            Face body = string.IsNullOrEmpty(bodyPath) ? null : LoadFace(source, bodyPath, out bodyError);
            return new TtfText(heading, body) { Name = Path.GetFileNameWithoutExtension(headingPath), BodyError = body == null ? bodyError : null };
        }

        private static Face LoadFace(IContentSource source, string path, out string error)
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
                SpriteFontBase reference = fonts.GetFont(ReferencePx);
                Bounds cap = reference.TextBounds("H", Vector2.Zero);
                float height = cap.Y2 - cap.Y;
                if (!(height > 1f))
                {
                    fonts.Dispose();
                    error = path + ": the font has no capital H.";
                    return null;
                }

                return new Face { Fonts = fonts, Reference = reference, CapPerPx = height / ReferencePx, CapTopPerPx = cap.Y / ReferencePx };
            }
            catch (Exception exception)
            {
                error = path + ": " + exception.Message;
                return null;
            }
        }

        public float Measure(string text, float size)
        {
            return Measure(text, size, UiFontFace.Heading);
        }

        public float LineHeight(float size)
        {
            return LineHeight(size, UiFontFace.Heading);
        }

        public void Draw(SpriteRenderer renderer, string text, Vector2 topLeft, float size, Color color, Color? shadow = null)
        {
            Draw(renderer, text, topLeft, size, color, UiFontFace.Heading, shadow);
        }

        public float Measure(string text, float size, UiFontFace face)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }

            Face f = Of(face);
            return f.Reference.MeasureString(text).X * PixelSize(f, size) / ReferencePx;
        }

        public float LineHeight(float size, UiFontFace face)
        {
            // Capitals, descenders and a little air: about 0.9 cap heights for both families (checked
            // against Atkinson Hyperlegible's own metrics when the body font was wired in).
            return PixelSize(Of(face), size) * 0.9f;
        }

        public void Draw(SpriteRenderer renderer, string text, Vector2 topLeft, float size, Color color, UiFontFace face, Color? shadow = null)
        {
            if (string.IsNullOrEmpty(text) || size <= 0f)
            {
                return;
            }

            Face f = Of(face);
            float px = PixelSize(f, size);
            float screen = Math.Max(0.01f, renderer.TransformScale);
            float raster = Quantize(px * screen);
            SpriteFontBase font = f.Fonts.GetFont(raster);
            float k = px / raster;
            Vector2 at = new Vector2(topLeft.X, topLeft.Y - f.CapTopPerPx * px);
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
            _heading.Fonts.Dispose();
            if (_body != _heading)
            {
                _body.Fonts.Dispose();
            }
        }

        private Face Of(UiFontFace face)
        {
            return face == UiFontFace.Body ? _body : _heading;
        }

        /// <summary>The font's pixel size (FontStashSharp's size) whose capitals are <paramref name="capHeight"/> tall.</summary>
        private static float PixelSize(Face face, float capHeight)
        {
            return capHeight / face.CapPerPx;
        }

        /// <summary>Rasterised sizes: whole pixels up to 32, then steps of 4, so zooming reuses a few atlas entries.</summary>
        private static float Quantize(float px)
        {
            float rounded = px <= 32f ? (float)Math.Round(px) : (float)Math.Round(px / 4f) * 4f;
            return Math.Max(6f, Math.Min(256f, rounded));
        }

        /// <summary>One loaded typeface's atlas and cap-height metrics.</summary>
        private sealed class Face
        {
            public FontSystem Fonts;
            public SpriteFontBase Reference;
            public float CapPerPx;
            public float CapTopPerPx;
        }
    }
}
