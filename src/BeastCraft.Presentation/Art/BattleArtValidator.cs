using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;
using BeastCraft.Creatures.Roster;
using BeastCraft.Presentation.Layout;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Art
{
    /// <summary>
    /// Checks <c>battle-art.json</c> (<see cref="BattleArtData"/>): the schema version; every backdrop
    /// for a known region (<c>regions.json</c>) and an <see cref="ArenaSize"/> name (a region's arena
    /// may have several paintings), each ArtKey once, a well-formed <c>ArtKey</c> of a sprite in the art manifest; its board rect inside
    /// the image and not empty; the image drawn undistorted (its pixels square to within
    /// <see cref="BackdropPlacement.AspectTolerance"/>) and reaching the canvas edges at the fit-all
    /// view (<see cref="BackdropPlacement.CanvasAtFitAll"/>) with room for the largest screen shake
    /// (<see cref="VfxLibraryValidator.MaxShakeAmplitude"/> board px) to spare; the overlay colours palette chars and
    /// its numbers in range; the skill-icon style's frame and ring sprites in the manifest, its
    /// rarities unique and every default and override naming one. Returns every problem found
    /// (empty = valid); never throws.
    /// </summary>
    public static class BattleArtValidator
    {
        public const float MinGridWidth = 0.5f;
        public const float MaxGridWidth = 6f;
        public const float MinIconScale = 0.2f;
        public const float MaxIconScale = 1.5f;

        /// <summary>
        /// Validates <paramref name="data"/>. <paramref name="regions"/> (null skips the check) holds
        /// the region ids a backdrop may name; <paramref name="art"/> (null skips the art checks) is
        /// the manifest its images and colours must be in.
        /// </summary>
        public static List<string> Validate(BattleArtData data, RegionLibraryData regions, ArtManifestData art)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No battle art data.");
                return errors;
            }

            if (data.SchemaVersion != BattleArtData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + BattleArtData.CurrentSchemaVersion + ".");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            BattleBackdropData[] backdrops = data.Backdrops ?? new BattleBackdropData[0];
            for (int i = 0; i < backdrops.Length; i++)
            {
                BattleBackdropData backdrop = backdrops[i];
                if (backdrop == null)
                {
                    errors.Add("Backdrops[" + i + "] is null.");
                    continue;
                }

                string at = "Backdrop '" + backdrop.RegionId + "' " + backdrop.Arena;
                if (regions != null && !Array.Exists(regions.Regions ?? new RegionData[0], r => r != null && r.RegionId == backdrop.RegionId))
                {
                    errors.Add(at + ": RegionId '" + backdrop.RegionId + "' is not a region.");
                }

                bool arenaKnown = TryParseArena(backdrop.Arena, out ArenaSize arena);
                if (!arenaKnown)
                {
                    errors.Add(at + ": Arena '" + backdrop.Arena + "' is not Small, Medium or Large.");
                }

                if (!seen.Add(backdrop.ArtKey ?? string.Empty))
                {
                    errors.Add(at + ": ArtKey '" + backdrop.ArtKey + "' is listed twice.");
                }

                ValidateRect(backdrop.BoardRect, at, errors, out bool rectOk);

                if (!ArtReferenceValidator.IsWellFormed(backdrop.ArtKey))
                {
                    errors.Add(at + ": ArtKey '" + backdrop.ArtKey + "' is not lowercase snake_case segments joined by '/'.");
                    continue;
                }

                if (art == null)
                {
                    continue;
                }

                ArtSpriteData image = art.FindByArtKey(backdrop.ArtKey);
                if (image == null || image.Kind != ArtSpriteKind.Sprite)
                {
                    errors.Add(at + ": ArtKey '" + backdrop.ArtKey + "' is not a sprite in the art manifest.");
                    continue;
                }

                if (!arenaKnown || !rectOk || image.FrameWidth < 1 || image.FrameHeight < 1)
                {
                    continue;
                }

                HexGrid.DimensionsFor(arena, out int width, out int height);
                float aspect = BackdropPlacement.PixelAspect(backdrop, width, height, image.FrameWidth, image.FrameHeight);
                if (Math.Abs(aspect - 1f) > BackdropPlacement.AspectTolerance)
                {
                    errors.Add(at + ": its board rect is " + Format(aspect) + "x as wide for its height as the " + width + "x" + height +
                               " arena's tiles; the " + image.FrameWidth + "x" + image.FrameHeight + " image would be stretched.");
                }

                Rect drawn = BackdropPlacement.ImageRect(backdrop, width, height);
                Rect canvas = BackdropPlacement.CanvasAtFitAll(width, height);
                float shake = VfxLibraryValidator.MaxShakeAmplitude;
                Rect shaken = new Rect(canvas.X - shake, canvas.Y - shake, canvas.Width + 2f * shake, canvas.Height + 2f * shake);
                if (!BackdropPlacement.Covers(drawn, shaken, 0.5f))
                {
                    errors.Add(at + ": the image (" + drawn + " in board space) does not reach past the canvas edges at the fit-all view (" + canvas +
                               ") by the largest screen shake (" + Format(shake) + " board px).");
                }
            }

            BoardOverlayData board = data.Board ?? new BoardOverlayData();
            Color(board.GridColor, "Board GridColor", art, errors);
            Color(board.PlayerZoneColor, "Board PlayerZoneColor", art, errors);
            Color(board.EnemyZoneColor, "Board EnemyZoneColor", art, errors);
            Range(board.GridAlpha, 0f, 1f, "Board GridAlpha", errors);
            Range(board.GridWidth, MinGridWidth, MaxGridWidth, "Board GridWidth", errors);
            Range(board.PlayerZoneAlpha, 0f, 1f, "Board PlayerZoneAlpha", errors);
            Range(board.EnemyZoneAlpha, 0f, 1f, "Board EnemyZoneAlpha", errors);
            Range(board.HudScrimAlpha, 0f, 1f, "Board HudScrimAlpha", errors);
            Color(board.ObstacleOutlineColor, "Board ObstacleOutlineColor", art, errors);
            Range(board.ObstacleOutlineAlpha, 0f, 1f, "Board ObstacleOutlineAlpha", errors);
            Color(board.MoveReachColor, "Board MoveReachColor", art, errors);
            Range(board.MoveReachAlpha, 0f, 1f, "Board MoveReachAlpha", errors);
            if (data.SkillIcons != null)
            {
                ValidateSkillIcons(data.SkillIcons, art, errors);
            }

            return errors;
        }

        /// <summary>The skill-icon style: its frame and rings in the manifest, rarities unique and named, scales in range.</summary>
        private static void ValidateSkillIcons(SkillIconStyleData style, ArtManifestData art, List<string> errors)
        {
            if (!string.IsNullOrEmpty(style.Frame))
            {
                Sprite(style.Frame, "SkillIcons Frame", art, errors);
            }

            Range(style.FrameScale, MinIconScale, MaxIconScale, "SkillIcons FrameScale", errors);
            Range(style.IconScale, MinIconScale, MaxIconScale, "SkillIcons IconScale", errors);
            Range(style.RingScale, MinIconScale, MaxIconScale, "SkillIcons RingScale", errors);

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            SkillRarityData[] rarities = style.Rarities ?? new SkillRarityData[0];
            for (int i = 0; i < rarities.Length; i++)
            {
                SkillRarityData rarity = rarities[i];
                if (rarity == null)
                {
                    errors.Add("SkillIcons Rarities[" + i + "] is null.");
                    continue;
                }

                string at = "SkillIcons rarity '" + rarity.Rarity + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(rarity.Rarity))
                {
                    errors.Add(at + ": Rarity must be lowercase snake_case.");
                }
                else if (!ids.Add(rarity.Rarity))
                {
                    errors.Add(at + " is listed twice.");
                }

                Sprite(rarity.Ring, at + " Ring", art, errors);
                if (!string.IsNullOrEmpty(rarity.Tint))
                {
                    Color(rarity.Tint, at + " Tint", art, errors);
                }
            }

            foreach ((string name, string id) in new[] { ("BeastRarity", style.BeastRarity), ("AvatarRarity", style.AvatarRarity), ("EnemyRarity", style.EnemyRarity) })
            {
                if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                {
                    errors.Add("SkillIcons " + name + " '" + id + "' is not a rarity.");
                }
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillRarityOverrideData entry in style.Overrides ?? new SkillRarityOverrideData[0])
            {
                if (entry == null)
                {
                    errors.Add("SkillIcons Overrides has a null entry.");
                    continue;
                }

                string at = "SkillIcons override '" + entry.ArtKey + "'";
                if (!ArtReferenceValidator.IsWellFormed(entry.ArtKey))
                {
                    errors.Add(at + ": ArtKey is not lowercase snake_case segments joined by '/'.");
                }
                else if (!keys.Add(entry.ArtKey))
                {
                    errors.Add(at + " is listed twice.");
                }
                else if (art != null && art.FindByArtKey(entry.ArtKey) == null)
                {
                    errors.Add(at + ": no skill icon has that ArtKey in the art manifest.");
                }

                if (!ids.Contains(entry.Rarity ?? string.Empty))
                {
                    errors.Add(at + ": Rarity '" + entry.Rarity + "' is not a rarity.");
                }
            }
        }

        private static void Sprite(string artKey, string at, ArtManifestData art, List<string> errors)
        {
            if (!ArtReferenceValidator.IsWellFormed(artKey))
            {
                errors.Add(at + ": ArtKey '" + artKey + "' is not lowercase snake_case segments joined by '/'.");
            }
            else if (art != null && (art.FindByArtKey(artKey) == null || art.FindByArtKey(artKey).Kind != ArtSpriteKind.Sprite))
            {
                errors.Add(at + ": ArtKey '" + artKey + "' is not a sprite in the art manifest.");
            }
        }

        /// <summary>
        /// The backdrops held to the battle layouts (<c>battle-layouts.json</c>): every layout's ArtKey
        /// is a backdrop of the same region and arena (the painting its obstacles are painted on), and
        /// every backdrop of a region that has layouts has one (a battle there always stands on a
        /// layout, so a painting without one would never be drawn, and its rocks would block nothing).
        /// </summary>
        public static List<string> ValidateLayouts(BattleArtData data, Encounters.BattleLayoutData layouts)
        {
            List<string> errors = new List<string>();
            if (data == null || layouts == null)
            {
                return errors;
            }

            HashSet<string> regions = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (Encounters.BattleLayoutEntryData layout in layouts.Layouts ?? new Encounters.BattleLayoutEntryData[0])
            {
                if (layout == null)
                {
                    continue;
                }

                regions.Add(layout.RegionId ?? string.Empty);
                keys.Add(layout.ArtKey ?? string.Empty);
                BattleBackdropData backdrop = data.BackdropByArtKey(layout.ArtKey);
                if (backdrop == null)
                {
                    errors.Add("Layout '" + layout.ArtKey + "': no backdrop has that ArtKey.");
                }
                else if (backdrop.RegionId != layout.RegionId || backdrop.Arena != layout.Arena)
                {
                    errors.Add("Layout '" + layout.ArtKey + "': its backdrop is " + backdrop.RegionId + " " + backdrop.Arena + ", the layout " + layout.RegionId + " " +
                               layout.Arena + ".");
                }
            }

            foreach (BattleBackdropData backdrop in data.Backdrops ?? new BattleBackdropData[0])
            {
                if (backdrop != null && regions.Contains(backdrop.RegionId ?? string.Empty) && !keys.Contains(backdrop.ArtKey ?? string.Empty))
                {
                    errors.Add("Backdrop '" + backdrop.ArtKey + "': region " + backdrop.RegionId + " has battle layouts, but none for this painting.");
                }
            }

            return errors;
        }

        /// <summary>An <see cref="ArenaSize"/> name, exactly as written.</summary>
        public static bool TryParseArena(string name, out ArenaSize arena)
        {
            arena = ArenaSize.Medium;
            return !string.IsNullOrEmpty(name) && Enum.TryParse(name, false, out arena) && Enum.IsDefined(typeof(ArenaSize), arena) && arena.ToString() == name;
        }

        private static void ValidateRect(BattleArtRect rect, string at, List<string> errors, out bool ok)
        {
            ok = false;
            if (rect == null)
            {
                errors.Add(at + ": no BoardRect.");
                return;
            }

            if (float.IsNaN(rect.X) || float.IsNaN(rect.Y) || !(rect.Width > 0f) || !(rect.Height > 0f) || rect.X < 0f || rect.Y < 0f ||
                rect.X + rect.Width > 1.0001f || rect.Y + rect.Height > 1.0001f)
            {
                errors.Add(at + ": BoardRect (" + Format(rect.X) + ", " + Format(rect.Y) + ", " + Format(rect.Width) + "x" + Format(rect.Height) +
                           ") must be a non-empty rectangle inside the image, in fractions of it (0-1).");
                return;
            }

            ok = true;
        }

        private static void Color(string ch, string at, ArtManifestData art, List<string> errors)
        {
            if (string.IsNullOrEmpty(ch) || ch.Length != 1)
            {
                errors.Add(at + ": '" + ch + "' is not a single palette char.");
                return;
            }

            if (art != null && (art.Palette == null || !art.Palette.ContainsKey(ch)))
            {
                errors.Add(at + ": '" + ch + "' is not in the palette.");
            }
        }

        private static void Range(float value, float min, float max, string at, List<string> errors)
        {
            if (float.IsNaN(value) || value < min || value > max)
            {
                errors.Add(at + " " + Format(value) + " is outside " + Format(min) + "-" + Format(max) + ".");
            }
        }

        private static string Format(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
