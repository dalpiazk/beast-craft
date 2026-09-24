using System;
using System.Collections.Generic;

namespace BeastCraft.Vfx
{
    // ------------------------------------------------------------------------------------------
    // The skill VFX library (Data/Vfx/vfx-library.json): how each skill LOOKS when it fires.
    // Presentation data only -- nothing here is read by the battle, and changing it can never
    // change a battle's outcome. Plain serializable data (public fields), read with FieldJson.
    // Sheets are named by the art manifest's sprite Name and colours by palette char
    // (ArtManifestData: content/art/pixel/pixel-art-manifest.json, written by
    // Tooling/PixelArt/build.py). The rules are in
    // VfxLibraryValidator; the timeline that plays a spec is BeastCraft.Presentation.VfxTimeline.
    //
    // Schema v2 adds layered effects (VfxEffectData.Layers: flipbooks, ground decals, shockwave
    // rings, radial bursts, particles and glyphs, each with its own start, duration and blend) and
    // per-effect-type defaults (EffectDefaults: heal, shield, taunt, stun, burn, poison, cleanse,
    // stat buff/debuff, knockback), each an on-apply effect plus an optional aura and icon shown
    // while the status lasts. A skill's effect resolves: its own entry, else its effect type's
    // default, else its element's default (damage-by-element). v1 files still read.
    // ------------------------------------------------------------------------------------------

    /// <summary>The whole library: one default per element, per-effect-type defaults, then per-skill overrides.</summary>
    [Serializable]
    public class VfxLibraryData
    {
        /// <summary>The file's path relative to the repository root (like the other data files).</summary>
        public const string ProjectRelativePath = "content/data/Vfx/vfx-library.json";

        /// <summary>The schema this build writes; 1 (no layers, no effect-type defaults) still reads.</summary>
        public const int CurrentSchemaVersion = 2;

        public const int MinSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>
        /// Exactly one per <c>Element</c> value, <c>None</c> included: what a skill of that element
        /// looks like when the library has no entry for the skill itself nor for its effect type —
        /// in practice, damage by element.
        /// </summary>
        public VfxElementDefaultData[] ElementDefaults = new VfxElementDefaultData[0];

        /// <summary>
        /// Per-effect-type defaults by <see cref="VfxEffectKey"/> (at most one each): the on-apply
        /// effect of a skill whose primary effect is that type (a heal, a taunt...), also played on
        /// every target a status newly lands on, and the aura/icon shown while the status lasts.
        /// </summary>
        public VfxEffectTypeDefaultData[] EffectDefaults = new VfxEffectTypeDefaultData[0];

        /// <summary>Per-skill effects, by skill id (beast skills, avatar actives and enemy-library skills).</summary>
        public VfxSkillEffectData[] Skills = new VfxSkillEffectData[0];
    }

    /// <summary>One effect type's defaults: what applying it looks like, and what it looks like while it lasts.</summary>
    [Serializable]
    public class VfxEffectTypeDefaultData
    {
        /// <summary>A <see cref="VfxEffectKey"/> name.</summary>
        public string Key;

        /// <summary>Played on the target when the effect lands (null = nothing).</summary>
        public VfxEffectData Effect;

        /// <summary>Shown on a unit for as long as the status lasts (statuses and stat changes only; null = none).</summary>
        public VfxAuraData Aura;
    }

    /// <summary>The effect-type keys (<see cref="VfxEffectTypeDefaultData.Key"/>).</summary>
    public static class VfxEffectKey
    {
        public const string Heal = "Heal";
        public const string Shield = "Shield";
        public const string Taunt = "Taunt";
        public const string Stun = "Stun";

        /// <summary>Damage over time from a Fire source.</summary>
        public const string Burn = "Burn";

        /// <summary>Damage over time from any other source.</summary>
        public const string Poison = "Poison";

        public const string Cleanse = "Cleanse";
        public const string BuffStat = "BuffStat";
        public const string DebuffStat = "DebuffStat";
        public const string Knockback = "Knockback";

        /// <summary>Every key, in a fixed order.</summary>
        public static readonly string[] All = { Heal, Shield, Taunt, Stun, Burn, Poison, Cleanse, BuffStat, DebuffStat, Knockback };

        /// <summary>The keys that last (a status or a timed stat change), so can carry an aura.</summary>
        public static readonly string[] Lasting = { Shield, Taunt, Stun, Burn, Poison, BuffStat, DebuffStat };

        public static bool IsKey(string key)
        {
            return Array.IndexOf(All, key) >= 0;
        }

        public static bool IsLasting(string key)
        {
            return Array.IndexOf(Lasting, key) >= 0;
        }
    }

    /// <summary>
    /// What a lasting status looks like on its unit: a looping sprite at its feet or over it that
    /// pulses, and a small icon above its HP bar.
    /// </summary>
    [Serializable]
    public class VfxAuraData
    {
        /// <summary>A sprite Name in the art manifest (null/empty = no aura sprite, icon only).</summary>
        public string Sheet;

        /// <summary>A palette char to tint with, or null/empty for the sheet's own colours.</summary>
        public string Tint;

        /// <summary>Draw scale (0.25-4) at the peak of the pulse.</summary>
        public float Scale = 1f;

        /// <summary>One pulse (scale and opacity breathe) every this many ms (200-4000).</summary>
        public int PulseMs = 1200;

        /// <summary><c>Alpha</c> or <c>Additive</c>.</summary>
        public string Blend = VfxBlend.Additive;

        /// <summary><c>Ground</c> (under the unit, at its feet) or <c>Over</c> (on it).</summary>
        public string Depth = VfxDepth.Ground;

        /// <summary>A sprite Name of the status icon shown above the HP bar (null/empty = none).</summary>
        public string Icon;

        /// <summary>A palette char to tint the icon with (null/empty = its own colours).</summary>
        public string IconTint;
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
    /// a floating damage number, plus (schema v2) any number of <see cref="Layers"/>, each with its
    /// own start and duration. Every part but the motion is optional (null = none).
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

        /// <summary>
        /// Schema v2: the layered look, composited in order (ground layers under the units, the
        /// rest over them), each timed from the hit-stop's release (<see cref="VfxLayerData.StartMs"/>).
        /// </summary>
        public VfxLayerData[] Layers = new VfxLayerData[0];
    }

    /// <summary>
    /// One layer of an effect. <see cref="Type"/> says what it draws; the other fields are read as
    /// that type needs (the validator holds each type to its own). Positions are in board pixels
    /// and radii in hexes (one hex column step, 32 board pixels), so a layer sized to the affected
    /// area fits any arena and any art.
    /// </summary>
    [Serializable]
    public class VfxLayerData
    {
        /// <summary>A <see cref="VfxLayerType"/> name.</summary>
        public string Type;

        /// <summary><c>Target</c> (on each target), <c>Area</c> (once, at the centre of the affected hexes) or <c>Caster</c>.</summary>
        public string Anchor = VfxAnchor.Target;

        /// <summary>When it starts, ms after the hit-stop releases (negative: before the impact; -2000 to 3000).</summary>
        public int StartMs;

        /// <summary>How long it lasts (1-4000 ms).</summary>
        public int DurationMs = 400;

        /// <summary><c>Alpha</c> or <c>Additive</c> (glows, light, fire).</summary>
        public string Blend = VfxBlend.Alpha;

        /// <summary><c>Ground</c> (under the units) or <c>Over</c>; null/empty = the type's own (a decal is on the ground, the rest over).</summary>
        public string Depth;

        /// <summary>A sprite Name in the art manifest (not read by <c>Particles</c>, which name their own).</summary>
        public string Sheet;

        /// <summary>A palette char to tint with, or null/empty for the sheet's own colours.</summary>
        public string Tint;

        /// <summary>Draw scale at the start (0.1-8); for a sized layer (decal, shockwave) ignored in favour of the radius.</summary>
        public float Scale = 1f;

        /// <summary>Draw scale at the end (0 = the same as <see cref="Scale"/>).</summary>
        public float EndScale;

        /// <summary>Fade in over this many ms from the start, and out over <see cref="FadeOutMs"/> to the end (each 0 to the duration).</summary>
        public int FadeInMs;

        public int FadeOutMs;

        /// <summary>Flipbook: frames to play (1 to the sheet's) at <see cref="Fps"/>; the last frame holds.</summary>
        public int Frames;

        public int Fps = 12;

        /// <summary>
        /// Shockwave, decal, burst and glyphs: radius in hexes at the start and end (0-8). An end of
        /// 0 or less is the radius of the affected area (<c>VfxArea</c>: every tile a target stands
        /// on), so a ring grows to exactly the hexes the skill hit.
        /// </summary>
        public float StartRadius;

        public float EndRadius;

        /// <summary>Radial burst: rays; glyphs: glyphs (1-32).</summary>
        public int Count = 6;

        /// <summary>Glyphs: how far they float up over the duration (board px, 0-64).</summary>
        public float RisePx;

        /// <summary>Glyphs: turns the ring makes over the duration (-4 to 4).</summary>
        public float Spin;

        /// <summary>Particles: the burst (its own sheet, count, speeds, lifetime, colours, gravity; its Additive is ignored for <see cref="Blend"/>).</summary>
        public VfxParticleData Particles;
    }

    /// <summary>The layer type names (<see cref="VfxLayerData.Type"/>).</summary>
    public static class VfxLayerType
    {
        /// <summary>A sheet's frames played once at the anchor, optionally growing.</summary>
        public const string Flipbook = "Flipbook";

        /// <summary>A scorch (or frost, or glow) on the ground at the anchor, sized to a radius, fading out.</summary>
        public const string GroundDecal = "GroundDecal";

        /// <summary>A ring whose radius grows from StartRadius to EndRadius (the affected area by default), fading.</summary>
        public const string Shockwave = "Shockwave";

        /// <summary>Count sprites flying outward from the anchor, rotated along their rays.</summary>
        public const string RadialBurst = "RadialBurst";

        /// <summary>A particle burst from the anchor.</summary>
        public const string Particles = "Particles";

        /// <summary>A few rune sprites circling the anchor and floating up.</summary>
        public const string Glyphs = "Glyphs";

        public static readonly string[] All = { Flipbook, GroundDecal, Shockwave, RadialBurst, Particles, Glyphs };
    }

    /// <summary>The anchor names (<see cref="VfxLayerData.Anchor"/>).</summary>
    public static class VfxAnchor
    {
        public const string Target = "Target";
        public const string Area = "Area";
        public const string Caster = "Caster";
    }

    /// <summary>The blend names.</summary>
    public static class VfxBlend
    {
        public const string Alpha = "Alpha";
        public const string Additive = "Additive";
    }

    /// <summary>The depth names.</summary>
    public static class VfxDepth
    {
        public const string Ground = "Ground";
        public const string Over = "Over";
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
        /// <summary>A sprite Name in the art manifest.</summary>
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
        /// <summary>A sprite Name in the art manifest.</summary>
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
        /// <summary>The particle sprite: a sprite Name in the art manifest.</summary>
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
}
