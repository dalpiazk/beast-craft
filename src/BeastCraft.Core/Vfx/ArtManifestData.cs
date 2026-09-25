using System;
using System.Collections.Generic;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// The art manifest (<c>content/art/pixel/pixel-art-manifest.json</c>): the game's index of its
    /// 2D art, whatever the style. Schema v2 describes each sprite by what a renderer needs to place
    /// it — frame size, pivot (the feet point), pixels per world unit, texture filter, whether the
    /// PNG is premultiplied, named animation clips — so the placeholder pixel art (point filter,
    /// 32 px to a hex) and illustrated art (large PNGs, linear filter) go through the same code.
    /// <c>Kind: "spine"</c> is reserved for skeletal characters and ignored by this build.
    /// <para>
    /// The placeholder manifest is written by <c>Tooling/PixelArt/build.py</c>. Schema v1 (no
    /// pivot, scale, filter or clips) still reads: <see cref="Normalize"/> fills in exactly what v1
    /// meant (centre pivot, 32 px per unit, point filter, straight alpha) and moves v1's
    /// <c>Kind</c> (the art category) to <see cref="ArtSpriteData.Category"/>. The rules are in
    /// <see cref="ArtManifestValidator"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class ArtManifestData
    {
        /// <summary>The manifest's path relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/art/pixel/pixel-art-manifest.json";

        /// <summary>The schema this build writes; <see cref="MinSchemaVersion"/>-<see cref="CurrentSchemaVersion"/> read.</summary>
        public const int CurrentSchemaVersion = 2;

        public const int MinSchemaVersion = 1;

        /// <summary>
        /// World units: one unit is one hex column step, which the placeholder hex tiles draw as 32
        /// pixels. v1 sprites are that many pixels to a unit.
        /// </summary>
        public const float ReferencePixelsPerUnit = 32f;

        public int SchemaVersion;

        /// <summary>Palette char to <c>#rrggbb</c>: the colours VFX specs name.</summary>
        public Dictionary<string, string> Palette = new Dictionary<string, string>();

        public ArtSpriteData[] Sprites = new ArtSpriteData[0];

        /// <summary>The sprite named <paramref name="name"/>, or null.</summary>
        public ArtSpriteData Find(string name)
        {
            if (string.IsNullOrEmpty(name) || Sprites == null)
            {
                return null;
            }

            foreach (ArtSpriteData sprite in Sprites)
            {
                if (sprite != null && string.Equals(sprite.Name, name, StringComparison.Ordinal))
                {
                    return sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// A sprite's <see cref="ArtSpriteData.File"/> (relative to the manifest's folder, forward
        /// slashes, <c>..</c> allowed: the illustrated beasts live in <c>art/beasts/</c> beside
        /// <c>art/pixel/</c>) as a path relative to the content root, with every <c>.</c> and
        /// <c>..</c> resolved (<c>../beasts/golem/golem.png</c> is <c>art/beasts/golem/golem.png</c>).
        /// Null when it is empty, rooted, uses backslashes or climbs out of the content root.
        /// </summary>
        public static string ResolveFile(string file)
        {
            if (string.IsNullOrEmpty(file) || file.IndexOf('\\') >= 0 || file.IndexOf(':') >= 0 || file[0] == '/')
            {
                return null;
            }

            const string content = "content/";
            List<string> parts = new List<string>(ProjectRelativePath.Substring(content.Length).Split('/'));
            parts.RemoveAt(parts.Count - 1);
            foreach (string part in file.Split('/'))
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (parts.Count == 0)
                    {
                        return null;
                    }

                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            return parts.Count == 0 ? null : string.Join("/", parts);
        }

        /// <summary>The first sprite whose <see cref="ArtSpriteData.ArtKey"/> is <paramref name="artKey"/>, or null.</summary>
        public ArtSpriteData FindByArtKey(string artKey)
        {
            if (string.IsNullOrEmpty(artKey) || Sprites == null)
            {
                return null;
            }

            foreach (ArtSpriteData sprite in Sprites)
            {
                if (sprite != null && string.Equals(sprite.ArtKey, artKey, StringComparison.Ordinal))
                {
                    return sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// Brings a v1 manifest to the v2 shape in place (a v2 one is left alone, apart from an empty
        /// <c>Kind</c> or <c>Filter</c> read as <c>sprite</c> / <c>linear</c>): <c>Kind</c> becomes
        /// <see cref="ArtSpriteData.Category"/> and <c>Kind</c> becomes <c>sprite</c>; the pivot is
        /// the frame's centre, <see cref="ReferencePixelsPerUnit"/>, point filter, straight alpha.
        /// Returns <paramref name="data"/>.
        /// </summary>
        public static ArtManifestData Normalize(ArtManifestData data)
        {
            if (data == null || data.Sprites == null)
            {
                return data;
            }

            bool v1 = data.SchemaVersion == 1;
            foreach (ArtSpriteData sprite in data.Sprites)
            {
                if (sprite == null)
                {
                    continue;
                }

                if (v1)
                {
                    sprite.Category = sprite.Kind;
                    sprite.Kind = ArtSpriteKind.Sprite;
                    sprite.PivotX = sprite.FrameWidth / 2f;
                    sprite.PivotY = sprite.FrameHeight / 2f;
                    sprite.PixelsPerUnit = ReferencePixelsPerUnit;
                    sprite.Filter = ArtFilter.Point;
                    sprite.Premultiplied = false;
                }

                if (string.IsNullOrEmpty(sprite.Kind))
                {
                    sprite.Kind = ArtSpriteKind.Sprite;
                }

                if (string.IsNullOrEmpty(sprite.Filter))
                {
                    sprite.Filter = ArtFilter.Linear;
                }
            }

            return data;
        }
    }

    /// <summary>
    /// One sprite: a PNG holding a horizontal strip of <see cref="Frames"/> frames, each
    /// <see cref="FrameWidth"/> x <see cref="FrameHeight"/> pixels, and how to place it.
    /// </summary>
    [Serializable]
    public class ArtSpriteData
    {
        /// <summary>Unique within the manifest; VFX specs name sheets by it.</summary>
        public string Name;

        /// <summary>The image, relative to the manifest's folder (for <c>spine</c>: the skeleton file).</summary>
        public string File;

        /// <summary><c>sprite</c> (a PNG strip) or <c>spine</c> (reserved: skipped by this build's renderer).</summary>
        public string Kind;

        /// <summary>What the art is for: <c>beast</c>, <c>enemy</c>, <c>fx</c>, <c>hex</c>, <c>icon</c>, ... (free text).</summary>
        public string Category;

        public string Label;

        /// <summary>The key game data names this art by (species / enemy ArtKey); empty for art no data names.</summary>
        public string ArtKey;

        public int FrameWidth;

        public int FrameHeight;

        public int Frames;

        /// <summary>Frame time of the strip played as a whole (0 = a still or a sheet of stills).</summary>
        public int FrameMs;

        /// <summary>
        /// The anchor, in source pixels from the frame's top-left: for a character the point between
        /// its feet that stands on the tile centre; for an effect usually its centre.
        /// </summary>
        public float PivotX;

        public float PivotY;

        /// <summary>
        /// Source pixels per world unit (one hex column step). 32 for the placeholders; an
        /// illustrated 512 px beast drawn one hex wide would say 512.
        /// </summary>
        public float PixelsPerUnit;

        /// <summary><c>point</c> (pixel art: nearest-neighbour) or <c>linear</c> (illustrated art).</summary>
        public string Filter;

        /// <summary>True when the PNG's colours are already multiplied by alpha; false (straight alpha) is premultiplied on load.</summary>
        public bool Premultiplied;

        /// <summary>
        /// A colour (<c>#rrggbb</c>) the sprite is multiplied by when drawn, or null/empty for its
        /// own colours. An alias entry (a placeholder reusing another sprite's <see cref="File"/>
        /// under its own <see cref="ArtKey"/>) carries one so the reuse is told apart.
        /// </summary>
        public string Tint;

        /// <summary>Named clips (idle, attack, hit, ...). Optional: without one the sprite is frame 0.</summary>
        public ArtAnimationData[] Animations = new ArtAnimationData[0];

        /// <summary>The clip named <paramref name="name"/>, or null.</summary>
        public ArtAnimationData Animation(string name)
        {
            if (string.IsNullOrEmpty(name) || Animations == null)
            {
                return null;
            }

            foreach (ArtAnimationData clip in Animations)
            {
                if (clip != null && string.Equals(clip.Name, name, StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }
    }

    /// <summary>A named clip: frames of a sheet (this sprite, or another sprite's strip) played at a rate.</summary>
    [Serializable]
    public class ArtAnimationData
    {
        public string Name;

        /// <summary>The sprite whose frames play (its Name); null/empty = the sprite that owns the clip.</summary>
        public string Sheet;

        /// <summary>Frame indices into that sheet, in play order.</summary>
        public int[] Frames = new int[0];

        /// <summary>1-60.</summary>
        public int Fps = 8;

        public bool Loop = true;

        /// <summary>
        /// The frame index (into the sheet) to show <paramref name="ms"/> after the clip started: a
        /// looping clip wraps, a one-shot holds its last frame. 0 for an empty clip.
        /// </summary>
        public int FrameAt(int ms)
        {
            if (Frames == null || Frames.Length == 0)
            {
                return 0;
            }

            long step = Fps <= 0 ? 0 : (long)Math.Max(0, ms) * Fps / 1000;
            int index = Loop ? (int)(step % Frames.Length) : (int)Math.Min(step, Frames.Length - 1);
            return Frames[index];
        }
    }

    /// <summary>The <see cref="ArtSpriteData.Kind"/> names.</summary>
    public static class ArtSpriteKind
    {
        public const string Sprite = "sprite";

        /// <summary>Reserved: a Spine skeleton (File = the skeleton, with its atlas beside it). Not rendered yet.</summary>
        public const string Spine = "spine";
    }

    /// <summary>The <see cref="ArtSpriteData.Filter"/> names.</summary>
    public static class ArtFilter
    {
        public const string Point = "point";
        public const string Linear = "linear";
    }
}
