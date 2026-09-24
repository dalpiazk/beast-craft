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
    /// shake the board off the screen). Returns every problem found (empty = valid); never throws.
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

            if (data.SchemaVersion != VfxLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + VfxLibraryData.CurrentSchemaVersion + ".");
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
