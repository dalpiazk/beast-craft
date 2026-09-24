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

    /// <summary>
    /// The pixel art at runtime: every sprite of the manifest loaded from its PNG (opened through
    /// the content's <see cref="IContentSource"/>) with
    /// <see cref="Texture2D.FromStream(GraphicsDevice, Stream)"/> (no content pipeline) and
    /// premultiplied for SpriteBatch's default blending, plus the palette as colours.
    /// </summary>
    public sealed class SpriteAtlas : IDisposable
    {
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, PixelSpriteData> _sprites = new Dictionary<string, PixelSpriteData>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _palette = new Dictionary<string, Color>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byArtKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Color> _tints = new Dictionary<string, Color>(StringComparer.Ordinal);

        public SpriteAtlas(GraphicsDevice device, GameContent content)
        {
            string manifest = GameContent.RelativeOf(PixelArtManifestData.ProjectRelativePath);
            string folder = manifest.Substring(0, manifest.LastIndexOf('/') + 1);
            // An alias entry reuses another sprite's file: each file is loaded once.
            Dictionary<string, Texture2D> byFile = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (PixelSpriteData sprite in content.Art.Sprites)
            {
                if (!byFile.TryGetValue(sprite.File, out Texture2D texture))
                {
                    using (Stream stream = content.Source.Open(folder + sprite.File))
                    {
                        texture = Texture2D.FromStream(device, stream);
                        Premultiply(texture);
                        byFile.Add(sprite.File, texture);
                    }
                }

                _textures[sprite.Name] = texture;
                _sprites[sprite.Name] = sprite;
                if (!string.IsNullOrEmpty(sprite.ArtKey) && !_byArtKey.ContainsKey(sprite.ArtKey))
                {
                    _byArtKey.Add(sprite.ArtKey, sprite.Name);
                }

                if (!string.IsNullOrEmpty(sprite.Tint))
                {
                    _tints[sprite.Name] = ParseHex(sprite.Tint);
                }
            }

            _files = new List<Texture2D>(byFile.Values);

            foreach (KeyValuePair<string, string> entry in content.Art.Palette)
            {
                _palette[entry.Key] = ParseHex(entry.Value);
            }

            Pixel = new Texture2D(device, 1, 1);
            Pixel.SetData(new[] { Color.White });
        }

        private readonly List<Texture2D> _files;

        /// <summary>A 1x1 white texture for rectangles and bars.</summary>
        public Texture2D Pixel { get; }

        /// <summary>The texture of sprite <paramref name="name"/>, or null.</summary>
        public Texture2D Texture(string name)
        {
            return name != null && _textures.TryGetValue(name, out Texture2D texture) ? texture : null;
        }

        /// <summary>The name of the sprite whose ArtKey is <paramref name="artKey"/>, or null.</summary>
        public string NameOfArtKey(string artKey)
        {
            return artKey != null && _byArtKey.TryGetValue(artKey, out string name) ? name : null;
        }

        /// <summary>The colour sprite <paramref name="name"/> is multiplied by (its manifest Tint; white without one).</summary>
        public Color TintOf(string name)
        {
            return name != null && _tints.TryGetValue(name, out Color tint) ? tint : Color.White;
        }

        /// <summary>The manifest entry of sprite <paramref name="name"/>, or null.</summary>
        public PixelSpriteData Sprite(string name)
        {
            return name != null && _sprites.TryGetValue(name, out PixelSpriteData sprite) ? sprite : null;
        }

        /// <summary>The source rectangle of frame <paramref name="frame"/> of a strip.</summary>
        public Rectangle Frame(string name, int frame)
        {
            PixelSpriteData sprite = Sprite(name);
            if (sprite == null)
            {
                return Rectangle.Empty;
            }

            int index = Math.Max(0, Math.Min(sprite.Frames - 1, frame));
            return new Rectangle(index * sprite.FrameWidth, 0, sprite.FrameWidth, sprite.FrameHeight);
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

        private static void Premultiply(Texture2D texture)
        {
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Color.FromNonPremultiplied(data[i].R, data[i].G, data[i].B, data[i].A);
            }

            texture.SetData(data);
        }

        private static Color ParseHex(string hex)
        {
            string h = hex.TrimStart('#');
            return new Color(int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                             int.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                             int.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
