using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// Checks <c>vfx-library.json</c> (<see cref="VfxLibraryData"/>): the schema version; exactly one
    /// default per <see cref="Element"/> (<c>None</c> included), so every skill has an effect; skill
    /// ids unique and, given the known ids, resolving to a real skill; and every effect sane — a
    /// known motion, a projectile that takes time to fly, sheets that exist in the art manifest
    /// with the frame size the spec claims and enough frames, colours that are palette chars, and
    /// every number inside the ranges below (so a typo cannot freeze the game for ten seconds or
    /// shake the board off the screen). Schema v2 adds layers (a known type, anchor, blend and
    /// depth; timing, scale, radius and counts in range; the type's sheet in the manifest with
    /// enough frames) and effect-type defaults (known keys, each at most once; an aura only on a
    /// lasting status, with its sheets in the manifest). Returns every problem found (empty =
    /// valid); never throws.
    /// </summary>
    public static class VfxLibraryValidator
    {
        public const int MaxTravelMs = 2000;
        public const int MaxFps = 60;
        public const int MaxScale = 4;
        public const int MaxParticles = 64;
        public const float MaxParticleSpeed = 400f;
        public const int MinParticleLifetimeMs = 50;
        public const int MaxParticleLifetimeMs = 3000;
        public const float MaxGravity = 1000f;
        public const float MaxShakeAmplitude = 8f;
        public const int MaxShakeMs = 1000;
        public const int MaxFlashMs = 500;
        public const int MaxHitStopMs = 250;
        public const int MinRiseMs = 100;
        public const int MaxRiseMs = 2000;
        public const int MaxRisePx = 40;
        public const int MinLayerStartMs = -2000;
        public const int MaxLayerStartMs = 3000;
        public const int MaxLayerMs = 4000;
        public const float MinLayerScale = 0.1f;
        public const float MaxLayerScale = 8f;
        public const float MaxLayerRadius = 8f;
        public const int MaxLayerCount = 32;
        public const float MaxGlyphRisePx = 64f;
        public const float MaxGlyphSpin = 4f;
        public const float MinAuraScale = 0.25f;
        public const float MaxAuraScale = 4f;
        public const int MinPulseMs = 200;
        public const int MaxPulseMs = 4000;

        /// <summary>
        /// Validates <paramref name="data"/>. <paramref name="knownSkillIds"/> (null skips the check)
        /// are the ids a skill entry may name; <paramref name="art"/> (null skips the sheet and
        /// palette checks) is the art manifest the sheets and colours must exist in.
        /// </summary>
        public static List<string> Validate(VfxLibraryData data, ICollection<string> knownSkillIds, ArtManifestData art)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No VFX library data.");
                return errors;
            }

            if (data.SchemaVersion < VfxLibraryData.MinSchemaVersion || data.SchemaVersion > VfxLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + VfxLibraryData.MinSchemaVersion + "-" +
                           VfxLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<Element> seen = new HashSet<Element>();
            VfxElementDefaultData[] defaults = data.ElementDefaults ?? new VfxElementDefaultData[0];
            for (int i = 0; i < defaults.Length; i++)
            {
                VfxElementDefaultData entry = defaults[i];
                string at = "ElementDefaults[" + i + "]";
                if (entry == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (!BeastRosterValidator.TryParseElement(entry.Element, out Element element))
                {
                    errors.Add(at + ": '" + entry.Element + "' is not an element.");
                    continue;
                }

                at = "ElementDefaults '" + entry.Element + "'";
                if (!seen.Add(element))
                {
                    errors.Add(at + " is listed twice.");
                }

                ValidateEffect(entry.Effect, at, art, errors);
            }

            foreach (Element element in (Element[])Enum.GetValues(typeof(Element)))
            {
                if (!seen.Contains(element))
                {
                    errors.Add("ElementDefaults has no entry for " + element + "; every element needs a default.");
                }
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            VfxEffectTypeDefaultData[] byType = data.EffectDefaults ?? new VfxEffectTypeDefaultData[0];
            for (int i = 0; i < byType.Length; i++)
            {
                VfxEffectTypeDefaultData entry = byType[i];
                if (entry == null)
                {
                    errors.Add("EffectDefaults[" + i + "] is null.");
                    continue;
                }

                string at = "EffectDefaults '" + entry.Key + "'";
                if (!VfxEffectKey.IsKey(entry.Key))
                {
                    errors.Add(at + ": not an effect key (" + string.Join(", ", VfxEffectKey.All) + ").");
                    continue;
                }

                if (!keys.Add(entry.Key))
                {
                    errors.Add(at + " is listed twice.");
                }

                if (entry.Effect == null && entry.Aura == null)
                {
                    errors.Add(at + ": has neither an Effect nor an Aura.");
                }

                if (entry.Effect != null)
                {
                    ValidateEffect(entry.Effect, at, art, errors);
                }

                if (entry.Aura != null)
                {
                    if (!VfxEffectKey.IsLasting(entry.Key))
                    {
                        errors.Add(at + ": an Aura needs a lasting effect (" + string.Join(", ", VfxEffectKey.Lasting) + ").");
                    }

                    ValidateAura(entry.Aura, at + " Aura", art, errors);
                }
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            VfxSkillEffectData[] skills = data.Skills ?? new VfxSkillEffectData[0];
            for (int i = 0; i < skills.Length; i++)
            {
                VfxSkillEffectData entry = skills[i];
                if (entry == null)
                {
                    errors.Add("Skills[" + i + "] is null.");
                    continue;
                }

                string at = "Skill '" + entry.SkillId + "'";
                if (string.IsNullOrEmpty(entry.SkillId))
                {
                    errors.Add("Skills[" + i + "] has no SkillId.");
                    continue;
                }

                if (!ids.Add(entry.SkillId))
                {
                    errors.Add(at + " is listed twice.");
                }

                if (knownSkillIds != null && !knownSkillIds.Contains(entry.SkillId))
                {
                    errors.Add(at + " is not a skill in the skill or enemy library.");
                }

                ValidateEffect(entry.Effect, at, art, errors);
            }

            return errors;
        }

        /// <summary>The checks on one effect; <paramref name="at"/> prefixes every message.</summary>
        public static void ValidateEffect(VfxEffectData effect, string at, ArtManifestData art, List<string> errors)
        {
            if (effect == null)
            {
                errors.Add(at + ": no Effect.");
                return;
            }

            if (effect.Motion == VfxMotion.Projectile)
            {
                if (effect.TravelMs < 1 || effect.TravelMs > MaxTravelMs)
                {
                    errors.Add(at + ": a Projectile needs TravelMs in 1-" + MaxTravelMs + " (is " + effect.TravelMs + ").");
                }
            }
            else if (effect.Motion == VfxMotion.Instant)
            {
                if (effect.TravelMs != 0)
                {
                    errors.Add(at + ": an Instant effect has TravelMs 0 (is " + effect.TravelMs + ").");
                }

                if (effect.Projectile != null)
                {
                    errors.Add(at + ": an Instant effect has no Projectile.");
                }
            }
            else
            {
                errors.Add(at + ": Motion '" + effect.Motion + "' is not " + VfxMotion.Projectile + " or " + VfxMotion.Instant + ".");
            }

            if (effect.Projectile != null)
            {
                Sheet(effect.Projectile.Sheet, at + " Projectile", art, errors);
                Color(effect.Projectile.Tint, true, at + " Projectile Tint", art, errors);
                Range(effect.Projectile.Scale, 1, MaxScale, at + " Projectile Scale", errors);
            }

            if (effect.Flipbook != null)
            {
                ValidateFlipbook(effect.Flipbook, at + " Flipbook", art, errors);
            }

            if (effect.Particles != null)
            {
                ValidateParticles(effect.Particles, at + " Particles", art, errors);
            }

            if (effect.ScreenShake != null)
            {
                Range(effect.ScreenShake.Amplitude, 0f, MaxShakeAmplitude, at + " ScreenShake Amplitude", errors);
                Range(effect.ScreenShake.DurationMs, 0, MaxShakeMs, at + " ScreenShake DurationMs", errors);
            }

            if (effect.HitFlash != null)
            {
                Color(effect.HitFlash.Tint, false, at + " HitFlash Tint", art, errors);
                Range(effect.HitFlash.DurationMs, 0, MaxFlashMs, at + " HitFlash DurationMs", errors);
            }

            Range(effect.HitStopMs, 0, MaxHitStopMs, at + " HitStopMs", errors);

            if (effect.DamageNumber != null)
            {
                Color(effect.DamageNumber.Color, false, at + " DamageNumber Color", art, errors);
                Color(effect.DamageNumber.CritColor, true, at + " DamageNumber CritColor", art, errors);
                Range(effect.DamageNumber.RiseMs, MinRiseMs, MaxRiseMs, at + " DamageNumber RiseMs", errors);
                Range(effect.DamageNumber.RisePx, 0, MaxRisePx, at + " DamageNumber RisePx", errors);
            }

            VfxLayerData[] layers = effect.Layers ?? new VfxLayerData[0];
            for (int i = 0; i < layers.Length; i++)
            {
                ValidateLayer(layers[i], at + " Layers[" + i + "]", art, errors);
            }
        }

        /// <summary>The checks on one layer; <paramref name="at"/> prefixes every message.</summary>
        public static void ValidateLayer(VfxLayerData layer, string at, ArtManifestData art, List<string> errors)
        {
            if (layer == null)
            {
                errors.Add(at + " is null.");
                return;
            }

            if (Array.IndexOf(VfxLayerType.All, layer.Type) < 0)
            {
                errors.Add(at + ": Type '" + layer.Type + "' is not one of " + string.Join(", ", VfxLayerType.All) + ".");
                return;
            }

            at += " (" + layer.Type + ")";
            if (layer.Anchor != VfxAnchor.Target && layer.Anchor != VfxAnchor.Area && layer.Anchor != VfxAnchor.Caster)
            {
                errors.Add(at + ": Anchor '" + layer.Anchor + "' is not Target, Area or Caster.");
            }

            if (layer.Blend != VfxBlend.Alpha && layer.Blend != VfxBlend.Additive)
            {
                errors.Add(at + ": Blend '" + layer.Blend + "' is not Alpha or Additive.");
            }

            if (!string.IsNullOrEmpty(layer.Depth) && layer.Depth != VfxDepth.Ground && layer.Depth != VfxDepth.Over)
            {
                errors.Add(at + ": Depth '" + layer.Depth + "' is not Ground or Over.");
            }

            Range(layer.StartMs, MinLayerStartMs, MaxLayerStartMs, at + " StartMs", errors);
            Range(layer.DurationMs, 1, MaxLayerMs, at + " DurationMs", errors);
            Range(layer.FadeInMs, 0, Math.Max(0, layer.DurationMs), at + " FadeInMs", errors);
            Range(layer.FadeOutMs, 0, Math.Max(0, layer.DurationMs), at + " FadeOutMs", errors);

            if (layer.Type == VfxLayerType.Particles)
            {
                if (layer.Particles == null)
                {
                    errors.Add(at + ": no Particles.");
                }
                else
                {
                    ValidateParticles(layer.Particles, at + " Particles", art, errors);
                }

                return;
            }

            ArtSpriteData sheet = Sheet(layer.Sheet, at, art, errors);
            Color(layer.Tint, true, at + " Tint", art, errors);
            Range(layer.Scale, MinLayerScale, MaxLayerScale, at + " Scale", errors);
            if (layer.EndScale != 0f)
            {
                Range(layer.EndScale, MinLayerScale, MaxLayerScale, at + " EndScale", errors);
            }

            Range(layer.StartRadius, 0f, MaxLayerRadius, at + " StartRadius", errors);
            Range(layer.EndRadius, -MaxLayerRadius, MaxLayerRadius, at + " EndRadius", errors);

            if (layer.Type == VfxLayerType.Flipbook)
            {
                Range(layer.Fps, 1, MaxFps, at + " Fps", errors);
                if (layer.Frames < 1)
                {
                    errors.Add(at + ": Frames must be at least 1.");
                }
                else if (sheet != null && layer.Frames > sheet.Frames)
                {
                    errors.Add(at + ": " + layer.Frames + " frames, but sheet '" + sheet.Name + "' has " + sheet.Frames + ".");
                }
            }

            if (layer.Type == VfxLayerType.RadialBurst || layer.Type == VfxLayerType.Glyphs)
            {
                Range(layer.Count, 1, MaxLayerCount, at + " Count", errors);
            }

            if (layer.Type == VfxLayerType.Glyphs)
            {
                Range(layer.RisePx, 0f, MaxGlyphRisePx, at + " RisePx", errors);
                Range(layer.Spin, -MaxGlyphSpin, MaxGlyphSpin, at + " Spin", errors);
            }
        }

        /// <summary>The checks on an aura; <paramref name="at"/> prefixes every message.</summary>
        public static void ValidateAura(VfxAuraData aura, string at, ArtManifestData art, List<string> errors)
        {
            if (string.IsNullOrEmpty(aura.Sheet) && string.IsNullOrEmpty(aura.Icon))
            {
                errors.Add(at + ": has neither a Sheet nor an Icon.");
            }

            if (!string.IsNullOrEmpty(aura.Sheet))
            {
                Sheet(aura.Sheet, at, art, errors);
            }

            if (!string.IsNullOrEmpty(aura.Icon))
            {
                Sheet(aura.Icon, at + " Icon", art, errors);
            }

            Color(aura.Tint, true, at + " Tint", art, errors);
            Color(aura.IconTint, true, at + " IconTint", art, errors);
            Range(aura.Scale, MinAuraScale, MaxAuraScale, at + " Scale", errors);
            Range(aura.PulseMs, MinPulseMs, MaxPulseMs, at + " PulseMs", errors);
            if (aura.Blend != VfxBlend.Alpha && aura.Blend != VfxBlend.Additive)
            {
                errors.Add(at + ": Blend '" + aura.Blend + "' is not Alpha or Additive.");
            }

            if (aura.Depth != VfxDepth.Ground && aura.Depth != VfxDepth.Over)
            {
                errors.Add(at + ": Depth '" + aura.Depth + "' is not Ground or Over.");
            }
        }

        private static void ValidateFlipbook(VfxFlipbookData flipbook, string at, ArtManifestData art, List<string> errors)
        {
            ArtSpriteData sheet = Sheet(flipbook.Sheet, at, art, errors);
            Range(flipbook.Fps, 1, MaxFps, at + " Fps", errors);
            Range(flipbook.Scale, 1, MaxScale, at + " Scale", errors);
            Color(flipbook.Tint, true, at + " Tint", art, errors);

            if (flipbook.FrameWidth < 1 || flipbook.FrameHeight < 1)
            {
                errors.Add(at + ": FrameWidth and FrameHeight must be at least 1.");
            }

            if (flipbook.Frames < 1)
            {
                errors.Add(at + ": Frames must be at least 1.");
            }

            if (sheet == null)
            {
                return;
            }

            if (sheet.FrameWidth != flipbook.FrameWidth || sheet.FrameHeight != flipbook.FrameHeight)
            {
                errors.Add(at + ": frame size " + flipbook.FrameWidth + "x" + flipbook.FrameHeight + " is not sheet '" + sheet.Name + "''s " +
                           sheet.FrameWidth + "x" + sheet.FrameHeight + ".");
            }

            if (flipbook.Frames > sheet.Frames)
            {
                errors.Add(at + ": " + flipbook.Frames + " frames, but sheet '" + sheet.Name + "' has " + sheet.Frames + ".");
            }
        }

        private static void ValidateParticles(VfxParticleData particles, string at, ArtManifestData art, List<string> errors)
        {
            Sheet(particles.Sheet, at, art, errors);
            Range(particles.Count, 1, MaxParticles, at + " Count", errors);
            Range(particles.SpeedMin, 0f, MaxParticleSpeed, at + " SpeedMin", errors);
            Range(particles.SpeedMax, 0f, MaxParticleSpeed, at + " SpeedMax", errors);
            if (particles.SpeedMin > particles.SpeedMax)
            {
                errors.Add(at + ": SpeedMin " + particles.SpeedMin + " is above SpeedMax " + particles.SpeedMax + ".");
            }

            Range(particles.LifetimeMs, MinParticleLifetimeMs, MaxParticleLifetimeMs, at + " LifetimeMs", errors);
            Range(particles.Gravity, -MaxGravity, MaxGravity, at + " Gravity", errors);

            if (particles.Colors == null || particles.Colors.Length == 0)
            {
                errors.Add(at + ": Colors needs at least one palette char.");
                return;
            }

            for (int i = 0; i < particles.Colors.Length; i++)
            {
                Color(particles.Colors[i], false, at + " Colors[" + i + "]", art, errors);
            }
        }

        private static ArtSpriteData Sheet(string name, string at, ArtManifestData art, List<string> errors)
        {
            if (string.IsNullOrEmpty(name))
            {
                errors.Add(at + ": no Sheet.");
                return null;
            }

            if (art == null)
            {
                return null;
            }

            ArtSpriteData sheet = art.Find(name);
            if (sheet == null)
            {
                errors.Add(at + ": sheet '" + name + "' is not in the art manifest.");
            }
            else if (sheet.Kind == ArtSpriteKind.Spine)
            {
                errors.Add(at + ": sheet '" + name + "' is a spine skeleton, not a sprite sheet.");
                return null;
            }

            return sheet;
        }

        private static void Color(string ch, bool optional, string at, ArtManifestData art, List<string> errors)
        {
            if (string.IsNullOrEmpty(ch))
            {
                if (!optional)
                {
                    errors.Add(at + ": no colour (a palette char).");
                }

                return;
            }

            if (ch.Length != 1)
            {
                errors.Add(at + ": '" + ch + "' is not a single palette char.");
                return;
            }

            if (art != null && (art.Palette == null || !art.Palette.ContainsKey(ch)))
            {
                errors.Add(at + ": '" + ch + "' is not in the palette.");
            }
        }

        private static void Range(int value, int min, int max, string at, List<string> errors)
        {
            if (value < min || value > max)
            {
                errors.Add(at + " " + value + " is outside " + min + "-" + max + ".");
            }
        }

        private static void Range(float value, float min, float max, string at, List<string> errors)
        {
            if (float.IsNaN(value) || value < min || value > max)
            {
                errors.Add(at + " " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is outside " +
                           min.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" +
                           max.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
        }
    }
}
