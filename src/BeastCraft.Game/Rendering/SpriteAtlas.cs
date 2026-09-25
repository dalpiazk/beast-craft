using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BeastCraft.Presentation.Content;
using BeastCraft.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>One loaded sprite: its texture and its manifest entry (pivot, scale, filter, tint, clips).</summary>
    public sealed class ArtSprite
    {
        internal ArtSprite(ArtSpriteData data, Texture2D texture, Color tint)
        {
            Data = data;
            Texture = texture;
            Tint = tint;
            Sampler = data.Filter == ArtFilter.Point ? SamplerState.PointClamp : SamplerState.LinearClamp;
            Pivot = new Vector2(data.PivotX, data.PivotY);
        }

        public ArtSpriteData Data { get; }

        public Texture2D Texture { get; }

        /// <summary>The manifest's Tint (white without one): every draw is multiplied by it.</summary>
        public Color Tint { get; }

        /// <summary>Point sampling for pixel art, linear for illustrated art (the manifest's Filter).</summary>
        public SamplerState Sampler { get; }

        /// <summary>The anchor in source pixels from the frame's top-left.</summary>
        public Vector2 Pivot { get; }

        public string Name
        {
            get { return Data.Name; }
        }

        /// <summary>The source rectangle of frame <paramref name="frame"/> (clamped to the strip).</summary>
        public Rectangle Frame(int frame)
        {
            int index = Math.Max(0, Math.Min(Data.Frames - 1, frame));
            return new Rectangle(index * Data.FrameWidth, 0, Data.FrameWidth, Data.FrameHeight);
        }
    }

    /// <summary>
    /// The art at runtime: every <c>sprite</c> entry of the art manifest (schema v2, see
    /// <see cref="ArtManifestData"/>) loaded from its PNG through the content's
    /// <see cref="IContentSource"/> with <see cref="Texture2D.FromStream(GraphicsDevice, Stream)"/>
    /// (no content pipeline), premultiplied on load when the manifest says the PNG is straight
    /// alpha (SpriteBatch blends premultiplied), plus the palette as colours. A <c>linear</c>
    /// sprite (illustrated art, drawn several times smaller than its PNG at board size) also gets
    /// a mip chain, box-filtered from the premultiplied pixels, so the linear sampler's mip level
    /// keeps its outline and detail from shimmering or breaking up; <c>point</c> pixel art has
    /// none. Files are opened at <see cref="ArtManifestData.ResolveFile"/>. Entries of kind
    /// <c>spine</c> are reserved and skipped: a Spine renderer would load them beside this.
    /// </summary>
    public sealed class SpriteAtlas : IDisposable
    {
        private readonly Dictionary<string, ArtSprite> _sprites = new Dictionary<string, ArtSprite>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _palette = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byArtKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Texture2D> _files = new List<Texture2D>();

        public SpriteAtlas(GraphicsDevice device, GameContent content)
        {
            // An alias entry reuses another sprite's file: each file is loaded once.
            Dictionary<string, Texture2D> byFile = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (ArtSpriteData sprite in content.Art.Sprites)
            {
                if (sprite.Kind != ArtSpriteKind.Sprite)
                {
                    continue;
                }

                if (!byFile.TryGetValue(sprite.File, out Texture2D texture))
                {
                    using (Stream stream = content.Source.Open(ArtManifestData.ResolveFile(sprite.File)))
                    {
                        texture = Texture2D.FromStream(device, stream);
                    }

                    texture = Prepare(device, texture, !sprite.Premultiplied, sprite.Filter == ArtFilter.Linear);

                    byFile.Add(sprite.File, texture);
                    _files.Add(texture);
                }

                Color tint = string.IsNullOrEmpty(sprite.Tint) ? Color.White : ParseHex(sprite.Tint);
                _sprites[sprite.Name] = new ArtSprite(sprite, texture, tint);
                if (!string.IsNullOrEmpty(sprite.ArtKey) && !_byArtKey.ContainsKey(sprite.ArtKey))
                {
                    _byArtKey.Add(sprite.ArtKey, sprite.Name);
                }
            }

            foreach (KeyValuePair<string, string> entry in content.Art.Palette)
            {
                _palette[entry.Key] = ParseHex(entry.Value);
            }

            Pixel = new Texture2D(device, 1, 1);
            Pixel.SetData(new[] { Color.White });
        }

        /// <summary>A 1x1 white texture for rectangles and bars.</summary>
        public Texture2D Pixel { get; }

        /// <summary>The sprite named <paramref name="name"/>, or null.</summary>
        public ArtSprite Sprite(string name)
        {
            return name != null && _sprites.TryGetValue(name, out ArtSprite sprite) ? sprite : null;
        }

        /// <summary>The sprite whose ArtKey is <paramref name="artKey"/> (a species' or enemy's), or null.</summary>
        public ArtSprite ByArtKey(string artKey)
        {
            return artKey != null && _byArtKey.TryGetValue(artKey, out string name) ? Sprite(name) : null;
        }

        /// <summary>Palette char to colour; <paramref name="fallback"/> for an empty or unknown char.</summary>
        public Color Palette(string ch, Color fallback)
        {
            return !string.IsNullOrEmpty(ch) && _palette.TryGetValue(ch, out Color color) ? color : fallback;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _files)
            {
                texture.Dispose();
            }

            Pixel.Dispose();
        }

        /// <summary>
        /// The loaded texture premultiplied (when <paramref name="premultiply"/>) and, with
        /// <paramref name="mipmaps"/>, copied into a mipmapped texture whose levels are
        /// <see cref="Downsample"/>d from the level above (the loaded one is disposed).
        /// </summary>
        private static Texture2D Prepare(GraphicsDevice device, Texture2D texture, bool premultiply, bool mipmaps)
        {
            if (!premultiply && !mipmaps)
            {
                return texture;
            }

            int width = texture.Width;
            int height = texture.Height;
            Color[] data = new Color[width * height];
            texture.GetData(data);
            if (premultiply)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = Color.FromNonPremultiplied(data[i].R, data[i].G, data[i].B, data[i].A);
                }
            }

            if (!mipmaps)
            {
                texture.SetData(data);
                return texture;
            }

            texture.Dispose();
            Texture2D mipped = new Texture2D(device, width, height, true, SurfaceFormat.Color);
            for (int level = 0; level < mipped.LevelCount; level++)
            {
                if (level > 0)
                {
                    data = Downsample(data, ref width, ref height);
                }

                mipped.SetData(level, null, data, 0, data.Length);
            }

            return mipped;
        }

        /// <summary>
        /// One mip level down: each pixel the mean of the (up to) 2x2 premultiplied pixels above it,
        /// so transparent pixels add nothing to the colour. <paramref name="width"/> and
        /// <paramref name="height"/> become the new size (halved, at least 1).
        /// </summary>
        private static Color[] Downsample(Color[] source, ref int width, ref int height)
        {
            int w = Math.Max(1, width / 2);
            int h = Math.Max(1, height / 2);
            Color[] result = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Min(height - 1, 2 * y);
                int y1 = Math.Min(height - 1, 2 * y + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Min(width - 1, 2 * x);
                    int x1 = Math.Min(width - 1, 2 * x + 1);
                    Color a = source[y0 * width + x0];
                    Color b = source[y0 * width + x1];
                    Color c = source[y1 * width + x0];
                    Color d = source[y1 * width + x1];
                    result[y * w + x] = new Color((a.R + b.R + c.R + d.R + 2) / 4, (a.G + b.G + c.G + d.G + 2) / 4,
                                                  (a.B + b.B + c.B + d.B + 2) / 4, (a.A + b.A + c.A + d.A + 2) / 4);
                }
            }

            width = w;
            height = h;
            return result;
        }

        /// <summary><c>#rrggbb</c> to a colour.</summary>
        public static Color ParseHex(string hex)
        {
            string h = hex.TrimStart('#');
            return new Color(int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                             int.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                             int.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
