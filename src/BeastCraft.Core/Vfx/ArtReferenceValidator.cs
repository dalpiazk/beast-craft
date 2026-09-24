using System.Collections.Generic;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// Holds the content's art references to the art manifest: every species' and enemy's
    /// <c>ArtKey</c> (<see cref="SpeciesData.ArtKey"/>, <see cref="EnemyData.ArtKey"/>) must name a
    /// sprite's <see cref="ArtSpriteData.ArtKey"/> in the manifest. (The VFX library's sheets are
    /// held to the manifest by <see cref="VfxLibraryValidator"/>.) Presentation only: nothing here
    /// can change a battle. Returns every problem found (empty = valid); never throws.
    /// </summary>
    public static class ArtReferenceValidator
    {
        /// <summary>
        /// Validates <paramref name="roster"/>'s and <paramref name="enemies"/>' art keys against
        /// <paramref name="art"/>. With <paramref name="requireKeys"/> every species and enemy must
        /// have one (the shipped content does); otherwise a missing key is allowed.
        /// </summary>
        public static List<string> Validate(BeastRosterData roster, EnemyLibraryData enemies, ArtManifestData art, bool requireKeys = false)
        {
            List<string> errors = new List<string>();
            if (art == null)
            {
                errors.Add("No art manifest.");
                return errors;
            }

            foreach (SpeciesData species in roster == null || roster.Species == null ? new SpeciesData[0] : roster.Species)
            {
                if (species != null)
                {
                    Check(species.ArtKey, "Species '" + species.SpeciesId + "'", art, requireKeys, errors);
                }
            }

            foreach (EnemyData enemy in enemies == null || enemies.Enemies == null ? new EnemyData[0] : enemies.Enemies)
            {
                if (enemy != null)
                {
                    Check(enemy.ArtKey, "Enemy '" + enemy.EnemyId + "'", art, requireKeys, errors);
                }
            }

            return errors;
        }

        /// <summary>Lowercase snake_case segments joined by '/', e.g. <c>beast/frost_wyrm</c>.</summary>
        public static bool IsWellFormed(string key)
        {
            if (string.IsNullOrEmpty(key) || key[0] == '/' || key[key.Length - 1] == '/')
            {
                return false;
            }

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || (c == '/' && key[i - 1] != '/');
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Check(string key, string at, ArtManifestData art, bool required, List<string> errors)
        {
            if (string.IsNullOrEmpty(key))
            {
                if (required)
                {
                    errors.Add(at + ": no ArtKey.");
                }

                return;
            }

            if (art.FindByArtKey(key) == null)
            {
                errors.Add(at + ": ArtKey '" + key + "' is not in the art manifest.");
            }
        }
    }
}
