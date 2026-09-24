using System;
using System.Collections.Generic;

namespace BeastCraft.Vfx
{
    // ------------------------------------------------------------------------------------------
    // The skill VFX library (Data/Vfx/vfx-library.json): how each skill LOOKS when it fires.
    // Presentation data only -- nothing here is read by the battle, and changing it can never
    // change a battle's outcome. Plain serializable data (public fields), read with FieldJson.
    // Sheets are named by the pixel-art manifest's sprite Name and colours by palette char
    // (Art/Pixel/pixel-art-manifest.json, written by Tooling/PixelArt/build.py). The rules are in
    // VfxLibraryValidator; the timeline that plays a spec is BeastCraft.Presentation.VfxTimeline.
    // ------------------------------------------------------------------------------------------

    /// <summary>The whole library: one default per element, then per-skill overrides.</summary>
    [Serializable]
    public class VfxLibraryData
    {
        /// <summary>The file's path relative to the Unity project folder (like the other data files).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Vfx/vfx-library.json";

        /// <summary>The only schema version this build reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>
        /// Exactly one per <c>Element</c> value, <c>None</c> included: what a skill of that element
        /// looks like when the library has no entry for the skill itself.
        /// </summary>
        public VfxElementDefaultData[] ElementDefaults = new VfxElementDefaultData[0];

        /// <summary>Per-skill effects, by skill id (beast skills, avatar actives and enemy-library skills).</summary>
        public VfxSkillEffectData[] Skills = new VfxSkillEffectData[0];
    }

    /// <summary>An element's default effect.</summary>
    [Serializable]
    public class VfxElementDefaultData
    {
        /// <summary>An <c>Element</c> member name, exactly as written (<c>None</c> included).</summary>
        public string Element;

        public VfxEffectData Effect;
    }

    /// <summary>One skill's own effect.</summary>
    [Serializable]
    public class VfxSkillEffectData
    {
        public string SkillId;

        public VfxEffectData Effect;
    }

    /// <summary>
    /// One effect, played from the caster to each target: an optional travelling projectile, then at
    /// impact a hit-stop, a flipbook, a particle burst, a screen shake, a hit flash on the target and
    /// a floating damage number. Every part but the motion is optional (null = none).
    /// </summary>
    [Serializable]
    public class VfxEffectData
    {
        /// <summary><c>Projectile</c> (flies caster to target over <see cref="TravelMs"/>) or <c>Instant</c> (impact at once).</summary>
        public string Motion = VfxMotion.Instant;

        /// <summary>Projectile flight time in ms (0 for <c>Instant</c>).</summary>
        public int TravelMs;

        /// <summary>What flies (Projectile only; null = nothing visible flies, the impact still waits).</summary>
        public VfxSpriteData Projectile;

        /// <summary>The impact flipbook, played on each target.</summary>
        public VfxFlipbookData Flipbook;

        /// <summary>The impact particle burst, on each target.</summary>
        public VfxParticleData Particles;

        public VfxShakeData ScreenShake;

        public VfxFlashData HitFlash;

        /// <summary>Everything freezes this long at impact (ms; 0 = none).</summary>
        public int HitStopMs;

        public VfxDamageNumberData DamageNumber;
    }

    /// <summary>The motion names.</summary>
    public static class VfxMotion
    {
        public const string Instant = "Instant";
        public const string Projectile = "Projectile";
    }

    /// <summary>A single sprite (frame 0 of a sheet), optionally tinted.</summary>
    [Serializable]
    public class VfxSpriteData
    {
        /// <summary>A sprite Name in the pixel-art manifest.</summary>
        public string Sheet;

        /// <summary>A palette char to tint with, or null/empty for the sprite's own colours.</summary>
        public string Tint;

        /// <summary>Whole-number draw scale (1-4).</summary>
        public int Scale = 1;

        /// <summary>Additive blend (glows) rather than alpha blend.</summary>
        public bool Additive;
    }

    /// <summary>A flipbook: a horizontal strip of equal frames played once.</summary>
    [Serializable]
    public class VfxFlipbookData
    {
        /// <summary>A sprite Name in the pixel-art manifest.</summary>
        public string Sheet;

        /// <summary>Frame size in pixels; must equal the manifest's.</summary>
        public int FrameWidth;

        public int FrameHeight;

        /// <summary>Frames to play (1 to the sheet's frame count).</summary>
        public int Frames;

        /// <summary>Playback rate, 1-60.</summary>
        public int Fps = 12;

        /// <summary>A palette char to tint with, or null/empty for the sheet's own colours.</summary>
        public string Tint;

        public int Scale = 1;

        public bool Additive;
    }

    /// <summary>A burst of particles from the impact point.</summary>
    [Serializable]
    public class VfxParticleData
    {
        /// <summary>The particle sprite: a sprite Name in the pixel-art manifest.</summary>
        public string Sheet;

        /// <summary>How many (1-64).</summary>
        public int Count;

        /// <summary>Launch speed range, pixels per second (0-400, min &lt;= max).</summary>
        public float SpeedMin;

        public float SpeedMax;

        /// <summary>How long each particle lives (50-3000 ms).</summary>
        public int LifetimeMs;

        /// <summary>Palette chars each particle picks from (at least one).</summary>
        public string[] Colors = new string[0];

        /// <summary>Downward acceleration, pixels per second squared (-1000-1000; negative rises).</summary>
        public float Gravity;

        public bool Additive = true;
    }

    /// <summary>A decaying screen shake.</summary>
    [Serializable]
    public class VfxShakeData
    {
        /// <summary>Peak offset in virtual pixels (0-8).</summary>
        public float Amplitude;

        /// <summary>0-1000 ms.</summary>
        public int DurationMs;
    }

    /// <summary>A tint flash on each target.</summary>
    [Serializable]
    public class VfxFlashData
    {
        /// <summary>A palette char.</summary>
        public string Tint;

        /// <summary>0-500 ms.</summary>
        public int DurationMs;
    }

    /// <summary>The floating damage number over each damaged target.</summary>
    [Serializable]
    public class VfxDamageNumberData
    {
        /// <summary>Palette char of an ordinary hit's number.</summary>
        public string Color;

        /// <summary>Palette char of a critical hit's number (null/empty = <see cref="Color"/>).</summary>
        public string CritColor;

        /// <summary>How long it floats (100-2000 ms).</summary>
        public int RiseMs = 700;

        /// <summary>How far it rises (0-40 virtual pixels).</summary>
        public int RisePx = 12;
    }

    /// <summary>
    /// The pixel-art manifest (<c>Art/Pixel/pixel-art-manifest.json</c>, written by
    /// <c>Tooling/PixelArt/build.py</c>) as far as the VFX checks and the renderer need it.
    /// </summary>
    [Serializable]
    public class PixelArtManifestData
    {
        /// <summary>The manifest's path relative to the Unity project folder.</summary>
        public const string ProjectRelativePath = "Assets/_Project/Art/Pixel/pixel-art-manifest.json";

        public int SchemaVersion;

        /// <summary>Palette char to <c>#rrggbb</c>.</summary>
        public Dictionary<string, string> Palette = new Dictionary<string, string>();

        public PixelSpriteData[] Sprites = new PixelSpriteData[0];

        /// <summary>The sprite named <paramref name="name"/>, or null.</summary>
        public PixelSpriteData Find(string name)
        {
            if (string.IsNullOrEmpty(name) || Sprites == null)
            {
                return null;
            }

            foreach (PixelSpriteData sprite in Sprites)
            {
                if (sprite != null && string.Equals(sprite.Name, name, StringComparison.Ordinal))
                {
                    return sprite;
                }
            }

            return null;
        }
    }

    /// <summary>One sprite of the manifest: a PNG strip of <see cref="Frames"/> frames.</summary>
    [Serializable]
    public class PixelSpriteData
    {
        public string Name;
        public string File;
        public int FrameWidth;
        public int FrameHeight;
        public int Frames;
        public int FrameMs;
        public string Kind;
        public string Label;
        public string ArtKey;
    }
}
