using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Progression;

namespace BeastCraft.Economy
{
    /// <summary>
    /// Checks <see cref="CosmeticLibraryData"/>: category ids lowercase snake_case and globally
    /// unique (unlock keys are <c>"categoryId/optionId"</c>); a scope of <c>avatar</c> or (given the
    /// roster) a known species; a colour category with a <c>#RRGGBB</c> default and no options; a
    /// discrete category with option ids unique within it, exactly one default (Source
    /// <c>default</c>, <c>IsDefault</c>), at least one starter look, every other option from a known
    /// source, rarity 0-1, MinRegion 1-10 and an art key; a <c>boss</c> option naming (given the
    /// regions) a known region, a <c>milestone</c> option a milestone of the file, every other
    /// option no unlock id. Milestones: ids unique, kind <c>BeastLevel</c> / <c>AvatarLevel</c> (1-100)
    /// or <c>BossesCleared</c> (1-10). With the roster, every species has at least one category;
    /// the avatar always must. No stats anywhere — looks never change a battle.
    /// </summary>
    public static class CosmeticLibraryValidator
    {
        /// <summary>The milestone kinds.</summary>
        public static readonly string[] MilestoneKinds = { "BeastLevel", "AvatarLevel", "BossesCleared" };

        public static List<string> Validate(CosmeticLibraryData data)
        {
            return Validate(data, null, null);
        }

        /// <summary>Every problem; empty = importable. <paramref name="speciesIds"/> and <paramref name="regionIds"/> (null = not checked) are the roster's and <c>regions.json</c>'s.</summary>
        public static List<string> Validate(CosmeticLibraryData data, ICollection<string> speciesIds, ICollection<string> regionIds)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("The cosmetic library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != CosmeticLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + CosmeticLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> milestones = new HashSet<string>(StringComparer.Ordinal);
            foreach (MilestoneData m in data.Milestones ?? new MilestoneData[0])
            {
                if (m == null)
                {
                    errors.Add("A milestone is null.");
                    continue;
                }

                string at = "Milestone '" + m.MilestoneId + "'";
                if (!DropTableValidator.IsSnakeCase(m.MilestoneId) || !milestones.Add(m.MilestoneId))
                {
                    errors.Add(at + ": id is not lowercase snake_case, or is used twice.");
                }

                int max = m.Kind == "BossesCleared" ? 10 : 100;
                if (Array.IndexOf(MilestoneKinds, m.Kind) < 0)
                {
                    errors.Add(at + ": Kind must be " + string.Join(", ", MilestoneKinds) + ".");
                }
                else if (m.Threshold < 1 || m.Threshold > max)
                {
                    errors.Add(at + ": Threshold must be 1 to " + max + ".");
                }
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> scopes = new HashSet<string>(StringComparer.Ordinal);
            foreach (CosmeticCategoryData c in data.Categories ?? new CosmeticCategoryData[0])
            {
                if (c == null)
                {
                    errors.Add("A category is null.");
                    continue;
                }

                string at = "Category '" + c.CategoryId + "'";
                if (!DropTableValidator.IsSnakeCase(c.CategoryId) || !ids.Add(c.CategoryId))
                {
                    errors.Add(at + ": id is not lowercase snake_case, or is used twice (category ids are global).");
                }

                if (string.IsNullOrEmpty(c.DisplayName))
                {
                    errors.Add(at + ": no DisplayName.");
                }

                if (c.Scope != CosmeticLibrary.AvatarScope && (string.IsNullOrEmpty(c.Scope) || (speciesIds != null && !speciesIds.Contains(c.Scope))))
                {
                    errors.Add(at + ": Scope '" + c.Scope + "' is neither avatar nor a roster species.");
                }
                else
                {
                    scopes.Add(c.Scope);
                }

                if (c.ValueType == "ColorPicker")
                {
                    if (!IsHexColor(c.DefaultColor))
                    {
                        errors.Add(at + ": DefaultColor '" + c.DefaultColor + "' is not #RRGGBB.");
                    }

                    if (c.Options != null && c.Options.Length > 0)
                    {
                        errors.Add(at + ": a colour category has no options.");
                    }

                    continue;
                }

                if (c.ValueType != "DiscreteOption")
                {
                    errors.Add(at + ": ValueType must be DiscreteOption or ColorPicker.");
                    continue;
                }

                ValidateOptions(c, at, milestones, regionIds, errors);
            }

            if (!scopes.Contains(CosmeticLibrary.AvatarScope))
            {
                errors.Add("The avatar has no cosmetic category.");
            }

            if (speciesIds != null)
            {
                foreach (string species in speciesIds)
                {
                    if (!scopes.Contains(species))
                    {
                        errors.Add("Species '" + species + "' has no cosmetic category (looks are per species).");
                    }
                }
            }

            return errors;
        }

        private static void ValidateOptions(CosmeticCategoryData c, string at, HashSet<string> milestones, ICollection<string> regionIds, List<string> errors)
        {
            CosmeticOptionData[] options = c.Options ?? new CosmeticOptionData[0];
            if (options.Length < 2)
            {
                errors.Add(at + ": a discrete category needs a default and at least one more look.");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int defaults = 0;
            int starters = 0;
            foreach (CosmeticOptionData o in options)
            {
                if (o == null)
                {
                    errors.Add(at + ": an option is null.");
                    continue;
                }

                string where = at + " option '" + o.OptionId + "'";
                if (!DropTableValidator.IsSnakeCase(o.OptionId) || !seen.Add(o.OptionId))
                {
                    errors.Add(where + ": id is not lowercase snake_case, or is used twice in the category.");
                }

                if (string.IsNullOrEmpty(o.DisplayName) || string.IsNullOrEmpty(o.ArtKey))
                {
                    errors.Add(where + ": needs a DisplayName and an ArtKey.");
                }

                if (Array.IndexOf(CosmeticLibrary.Sources, o.Source) < 0)
                {
                    errors.Add(where + ": Source '" + o.Source + "' is not one of " + string.Join(", ", CosmeticLibrary.Sources) + ".");
                }

                if (o.IsDefault != (o.Source == CosmeticLibrary.SourceDefault))
                {
                    errors.Add(where + ": the default look (IsDefault) and only it has Source default.");
                }

                defaults += o.IsDefault ? 1 : 0;
                starters += o.Source == CosmeticLibrary.SourceStarter ? 1 : 0;
                if (o.Rarity < 0 || o.Rarity > 1)
                {
                    errors.Add(where + ": Rarity must be 0 or 1.");
                }

                if (o.MinRegion < 1 || o.MinRegion > 10)
                {
                    errors.Add(where + ": MinRegion must be 1 to 10.");
                }

                bool boss = o.Source == CosmeticLibrary.SourceBoss;
                bool milestone = o.Source == CosmeticLibrary.SourceMilestone;
                if (boss && (string.IsNullOrEmpty(o.UnlockId) || (regionIds != null && !regionIds.Contains(o.UnlockId))))
                {
                    errors.Add(where + ": a boss look names the region whose lair grants it (UnlockId).");
                }
                else if (milestone && !milestones.Contains(o.UnlockId ?? string.Empty))
                {
                    errors.Add(where + ": a milestone look names a milestone of the file (UnlockId).");
                }
                else if (!boss && !milestone && !string.IsNullOrEmpty(o.UnlockId))
                {
                    errors.Add(where + ": only boss and milestone looks have an UnlockId.");
                }
            }

            if (defaults != 1)
            {
                errors.Add(at + ": has " + defaults + " default looks; exactly one is required.");
            }

            if (starters < 1)
            {
                errors.Add(at + ": has no starter look.");
            }
        }

        private static bool IsHexColor(string hex)
        {
            return hex != null && hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int _);
        }
    }
}
