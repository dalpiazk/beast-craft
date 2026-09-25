using System;
using System.Collections.Generic;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// Checks the art manifest (<see cref="ArtManifestData"/>), after <see cref="ArtManifestData.Normalize"/>:
    /// a readable schema version; unique, non-empty names; files that resolve inside the content
    /// root (<see cref="ArtManifestData.ResolveFile"/>); a known kind; positive frame
    /// sizes and counts; a pivot on the frame, a positive pixels-per-unit and a known filter; tints
    /// as <c>#rrggbb</c>; every clip naming an existing sheet, in-range frames and a sane rate; the
    /// palette as <c>#rrggbb</c> by single chars. A <c>spine</c> entry is only checked for a name and
    /// a file (the renderer skips it). Returns every problem found (empty = valid); never throws.
    /// </summary>
    public static class ArtManifestValidator
    {
        public const int MaxFps = 60;

        public static List<string> Validate(ArtManifestData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No art manifest.");
                return errors;
            }

            if (data.SchemaVersion < ArtManifestData.MinSchemaVersion || data.SchemaVersion > ArtManifestData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + ArtManifestData.MinSchemaVersion + "-" +
                           ArtManifestData.CurrentSchemaVersion + ".");
            }

            foreach (KeyValuePair<string, string> entry in data.Palette ?? new Dictionary<string, string>())
            {
                if (entry.Key == null || entry.Key.Length != 1 || !IsHexColor(entry.Value))
                {
                    errors.Add("Palette '" + entry.Key + "': needs a single char and a #rrggbb colour (is '" + entry.Value + "').");
                }
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            ArtSpriteData[] sprites = data.Sprites ?? new ArtSpriteData[0];
            for (int i = 0; i < sprites.Length; i++)
            {
                ArtSpriteData sprite = sprites[i];
                if (sprite == null)
                {
                    errors.Add("Sprites[" + i + "] is null.");
                    continue;
                }

                string at = "Sprite '" + sprite.Name + "'";
                if (string.IsNullOrEmpty(sprite.Name))
                {
                    errors.Add("Sprites[" + i + "] has no Name.");
                }
                else if (!names.Add(sprite.Name))
                {
                    errors.Add(at + " is listed twice.");
                }

                if (string.IsNullOrEmpty(sprite.File))
                {
                    errors.Add(at + ": no File.");
                }
                else if (ArtManifestData.ResolveFile(sprite.File) == null)
                {
                    errors.Add(at + ": File '" + sprite.File + "' is not a relative path (forward slashes) inside the content root.");
                }

                if (sprite.Kind == ArtSpriteKind.Spine)
                {
                    continue;
                }

                if (sprite.Kind != ArtSpriteKind.Sprite)
                {
                    errors.Add(at + ": Kind '" + sprite.Kind + "' is not " + ArtSpriteKind.Sprite + " or " + ArtSpriteKind.Spine + ".");
                    continue;
                }

                if (sprite.FrameWidth < 1 || sprite.FrameHeight < 1 || sprite.Frames < 1)
                {
                    errors.Add(at + ": FrameWidth, FrameHeight and Frames must be at least 1.");
                }

                if (float.IsNaN(sprite.PivotX) || float.IsNaN(sprite.PivotY) || sprite.PivotX < 0f || sprite.PivotY < 0f ||
                    sprite.PivotX > sprite.FrameWidth || sprite.PivotY > sprite.FrameHeight)
                {
                    errors.Add(at + ": pivot (" + sprite.PivotX + ", " + sprite.PivotY + ") is off its " + sprite.FrameWidth + "x" +
                               sprite.FrameHeight + " frame.");
                }

                if (!(sprite.PixelsPerUnit > 0f))
                {
                    errors.Add(at + ": PixelsPerUnit must be above 0.");
                }

                if (sprite.Filter != ArtFilter.Point && sprite.Filter != ArtFilter.Linear)
                {
                    errors.Add(at + ": Filter '" + sprite.Filter + "' is not " + ArtFilter.Point + " or " + ArtFilter.Linear + ".");
                }

                if (!string.IsNullOrEmpty(sprite.Tint) && !IsHexColor(sprite.Tint))
                {
                    errors.Add(at + ": Tint '" + sprite.Tint + "' is not #rrggbb.");
                }
            }

            foreach (ArtSpriteData sprite in sprites)
            {
                if (sprite == null || sprite.Animations == null)
                {
                    continue;
                }

                HashSet<string> clips = new HashSet<string>(StringComparer.Ordinal);
                foreach (ArtAnimationData clip in sprite.Animations)
                {
                    string at = "Sprite '" + sprite.Name + "' clip '" + (clip == null ? null : clip.Name) + "'";
                    if (clip == null || string.IsNullOrEmpty(clip.Name))
                    {
                        errors.Add("Sprite '" + sprite.Name + "': a clip has no Name.");
                        continue;
                    }

                    if (!clips.Add(clip.Name))
                    {
                        errors.Add(at + " is listed twice.");
                    }

                    ArtSpriteData sheet = string.IsNullOrEmpty(clip.Sheet) ? sprite : data.Find(clip.Sheet);
                    if (sheet == null)
                    {
                        errors.Add(at + ": sheet '" + clip.Sheet + "' is not in the manifest.");
                        continue;
                    }

                    if (clip.Fps < 1 || clip.Fps > MaxFps)
                    {
                        errors.Add(at + ": Fps " + clip.Fps + " is outside 1-" + MaxFps + ".");
                    }

                    if (clip.Frames == null || clip.Frames.Length == 0)
                    {
                        errors.Add(at + ": no Frames.");
                        continue;
                    }

                    foreach (int frame in clip.Frames)
                    {
                        if (frame < 0 || frame >= sheet.Frames)
                        {
                            errors.Add(at + ": frame " + frame + " is not one of sheet '" + sheet.Name + "''s " + sheet.Frames + ".");
                        }
                    }
                }
            }

            return errors;
        }

        /// <summary>Whether <paramref name="text"/> is <c>#rrggbb</c>.</summary>
        public static bool IsHexColor(string text)
        {
            if (text == null || text.Length != 7 || text[0] != '#')
            {
                return false;
            }

            for (int i = 1; i < 7; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
