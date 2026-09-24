using System;
using System.Collections.Generic;
using BeastCraft.Skills;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Structural integrity checks for <see cref="DropTableData"/>: the rules a drop-table file must
    /// satisfy to import at all. Shape ids are present, unique and lowercase snake_case; bands are
    /// ascending, contiguous and cover levels 1 to <see cref="MaxEncounterLevel"/>; every band has
    /// exactly one cell per shape; chances are percents and quantities sane; pity thresholds are
    /// positive and one per tier; and, given the skill library's materials, every material id
    /// resolves and every pity tier names a tier some material has.
    /// <para>
    /// The pacing targets (battles to skill level 5 / 10 / 15 / 20) are deliberately NOT here: the
    /// balance simulator's <c>--mode pacing</c> measures them. Non-throwing.
    /// </para>
    /// </summary>
    public static class DropTableValidator
    {
        /// <summary>The highest encounter level the bands must cover (the growth curves' max level).</summary>
        public const int MaxEncounterLevel = 100;

        /// <summary>The most of one material a single entry (or a first-clear bonus) may grant.</summary>
        public const int MaxQuantity = 10;

        /// <summary>Returns every problem found, without the material cross-checks.</summary>
        public static List<string> Validate(DropTableData table)
        {
            return Validate(table, null);
        }

        /// <summary>
        /// Returns every problem found; an empty list means the table is importable. With
        /// <paramref name="materials"/> (the skill library's), also checks that every material id
        /// resolves and every pity tier exists.
        /// </summary>
        public static List<string> Validate(DropTableData table, SkillMaterialData[] materials)
        {
            List<string> errors = new List<string>();

            if (table == null)
            {
                errors.Add("Drop table is null (the JSON did not parse).");
                return errors;
            }

            if (table.SchemaVersion < DropTableData.MinSchemaVersion || table.SchemaVersion > DropTableData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + table.SchemaVersion + "; this code reads versions " + DropTableData.MinSchemaVersion + " to " +
                           DropTableData.CurrentSchemaVersion + ".");
            }

            Dictionary<string, int> tiers = null;
            if (materials != null)
            {
                tiers = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (SkillMaterialData m in materials)
                {
                    if (m != null && !string.IsNullOrEmpty(m.MaterialId))
                    {
                        tiers[m.MaterialId] = m.Tier;
                    }
                }
            }

            HashSet<string> shapes = ValidateShapes(table.Shapes, errors);
            ValidatePity(table.Pity, tiers, errors);
            ValidateBands(table.Bands, shapes, tiers, errors);
            ValidateEconomy(table, shapes, errors);
            return errors;
        }

        /// <summary>The most gold a <see cref="GoldData.Base"/>, <see cref="GoldData.PerLevel"/> or <see cref="GoldData.FirstClearBonus"/> may be.</summary>
        public const int MaxGoldValue = 10000;

        /// <summary>The largest <see cref="GoldData.VariancePct"/>.</summary>
        public const int MaxGoldVariancePct = 50;

        /// <summary>The highest gear rarity a drop may name (0 common, 1 rare, 2 epic).</summary>
        public const int MaxGearRarity = 2;

        /// <summary>
        /// The schema-2 economy sections: gold values in range and multipliers above 0 for known
        /// shapes, each at most once; gear and cosmetic drops for known shapes with a chance of 1 to
        /// 1000 per mille (each (shape, rarity) and each cosmetic shape at most once). A version-1
        /// file must leave them empty.
        /// </summary>
        private static void ValidateEconomy(DropTableData table, HashSet<string> shapes, List<string> errors)
        {
            GoldData gold = table.Gold;
            bool hasEconomy = (gold != null && (gold.Base != 0 || gold.PerLevel != 0 || gold.FirstClearBonus != 0 || (gold.ShapeMultipliers != null && gold.ShapeMultipliers.Length > 0))) ||
                              (table.GearDrops != null && table.GearDrops.Length > 0) || (table.CosmeticDrops != null && table.CosmeticDrops.Length > 0);
            if (hasEconomy && table.SchemaVersion < 2)
            {
                errors.Add("Gold, GearDrops and CosmeticDrops need SchemaVersion 2.");
            }

            if (gold != null)
            {
                CheckGold(gold.Base, "Gold.Base", errors);
                CheckGold(gold.PerLevel, "Gold.PerLevel", errors);
                CheckGold(gold.FirstClearBonus, "Gold.FirstClearBonus", errors);
                if (gold.VariancePct < 0 || gold.VariancePct > MaxGoldVariancePct)
                {
                    errors.Add("Gold.VariancePct is " + gold.VariancePct + "; it must be 0 to " + MaxGoldVariancePct + ".");
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                ShapeMultiplierData[] multipliers = gold.ShapeMultipliers ?? new ShapeMultiplierData[0];
                for (int i = 0; i < multipliers.Length; i++)
                {
                    ShapeMultiplierData m = multipliers[i];
                    string at = "Gold.ShapeMultipliers[" + i + "]";
                    if (m == null)
                    {
                        errors.Add(at + " is null.");
                        continue;
                    }

                    if (!shapes.Contains(m.Shape ?? string.Empty))
                    {
                        errors.Add(at + ": '" + m.Shape + "' is not one of the Shapes.");
                    }
                    else if (!seen.Add(m.Shape))
                    {
                        errors.Add(at + ": '" + m.Shape + "' is listed twice.");
                    }

                    if (!(m.Multiplier > 0f) || m.Multiplier > 10f)
                    {
                        errors.Add(at + ": Multiplier is " + m.Multiplier + "; it must be above 0 and at most 10.");
                    }
                }
            }

            HashSet<string> gearSeen = new HashSet<string>(StringComparer.Ordinal);
            GearDropData[] gear = table.GearDrops ?? new GearDropData[0];
            for (int i = 0; i < gear.Length; i++)
            {
                GearDropData d = gear[i];
                string at = "GearDrops[" + i + "]";
                if (d == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (!shapes.Contains(d.Shape ?? string.Empty))
                {
                    errors.Add(at + ": '" + d.Shape + "' is not one of the Shapes.");
                }

                if (d.Rarity < 0 || d.Rarity > MaxGearRarity)
                {
                    errors.Add(at + ": Rarity is " + d.Rarity + "; it must be 0 to " + MaxGearRarity + ".");
                }
                else if (!gearSeen.Add(d.Shape + "/" + d.Rarity))
                {
                    errors.Add(at + ": shape '" + d.Shape + "' rarity " + d.Rarity + " is listed twice.");
                }

                CheckPerMille(d.ChancePerMille, at, errors);
            }

            HashSet<string> cosmeticSeen = new HashSet<string>(StringComparer.Ordinal);
            CosmeticDropData[] cosmetics = table.CosmeticDrops ?? new CosmeticDropData[0];
            for (int i = 0; i < cosmetics.Length; i++)
            {
                CosmeticDropData d = cosmetics[i];
                string at = "CosmeticDrops[" + i + "]";
                if (d == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (!shapes.Contains(d.Shape ?? string.Empty))
                {
                    errors.Add(at + ": '" + d.Shape + "' is not one of the Shapes.");
                }
                else if (!cosmeticSeen.Add(d.Shape))
                {
                    errors.Add(at + ": '" + d.Shape + "' is listed twice.");
                }

                CheckPerMille(d.ChancePerMille, at, errors);
            }
        }

        private static void CheckGold(int value, string at, List<string> errors)
        {
            if (value < 0 || value > MaxGoldValue)
            {
                errors.Add(at + " is " + value + "; it must be 0 to " + MaxGoldValue + ".");
            }
        }

        private static void CheckPerMille(int chance, string at, List<string> errors)
        {
            if (chance < 1 || chance > 1000)
            {
                errors.Add(at + ": ChancePerMille is " + chance + "; it must be 1 to 1000.");
            }
        }

        /// <summary>Whether <paramref name="id"/> is lowercase snake_case: [a-z0-9_], starting with a letter.</summary>
        public static bool IsSnakeCase(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static HashSet<string> ValidateShapes(string[] shapes, List<string> errors)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            if (shapes == null || shapes.Length == 0)
            {
                errors.Add("No shapes defined.");
                return seen;
            }

            foreach (string shape in shapes)
            {
                if (!IsSnakeCase(shape))
                {
                    errors.Add("Shape '" + shape + "' is not a lowercase snake_case id.");
                }
                else if (!seen.Add(shape))
                {
                    errors.Add("Shape '" + shape + "' is listed twice.");
                }
            }

            return seen;
        }

        private static void ValidatePity(PityData[] pity, Dictionary<string, int> tiers, List<string> errors)
        {
            if (pity == null)
            {
                return;
            }

            HashSet<int> known = null;
            if (tiers != null)
            {
                known = new HashSet<int>(tiers.Values);
            }

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < pity.Length; i++)
            {
                PityData p = pity[i];
                if (p == null)
                {
                    errors.Add("Pity #" + i + " is null.");
                    continue;
                }

                if (p.Tier < 1)
                {
                    errors.Add("Pity #" + i + ": Tier is " + p.Tier + "; it must be at least 1.");
                }
                else if (!seen.Add(p.Tier))
                {
                    errors.Add("Pity: tier " + p.Tier + " is listed twice.");
                }
                else if (known != null && !known.Contains(p.Tier))
                {
                    errors.Add("Pity: no material has tier " + p.Tier + ".");
                }

                if (p.Threshold < 1)
                {
                    errors.Add("Pity tier " + p.Tier + ": Threshold is " + p.Threshold + "; it must be at least 1.");
                }
            }
        }

        private static void ValidateBands(LevelBandData[] bands, HashSet<string> shapes, Dictionary<string, int> tiers, List<string> errors)
        {
            if (bands == null || bands.Length == 0)
            {
                errors.Add("No level bands defined.");
                return;
            }

            int expectedMin = 1;
            for (int b = 0; b < bands.Length; b++)
            {
                LevelBandData band = bands[b];
                if (band == null)
                {
                    errors.Add("Band #" + b + " is null.");
                    continue;
                }

                string label = "Band " + band.MinLevel + "-" + band.MaxLevel;
                if (band.MinLevel != expectedMin)
                {
                    errors.Add(label + ": starts at level " + band.MinLevel + "; bands must be contiguous from level 1, so it must start at " + expectedMin + ".");
                }

                if (band.MaxLevel < band.MinLevel)
                {
                    errors.Add(label + ": MaxLevel is below MinLevel.");
                }

                expectedMin = band.MaxLevel + 1;

                CheckMaterial(band.FirstClearMaterialId, label + " FirstClearMaterialId", tiers, errors);
                if (band.FirstClearQuantity < 1 || band.FirstClearQuantity > MaxQuantity)
                {
                    errors.Add(label + ": FirstClearQuantity is " + band.FirstClearQuantity + "; it must be 1 to " + MaxQuantity + ".");
                }

                ValidateCells(band.Cells, label, shapes, tiers, errors);
            }

            LevelBandData last = bands[bands.Length - 1];
            if (last != null && last.MaxLevel != MaxEncounterLevel)
            {
                errors.Add("The last band ends at level " + last.MaxLevel + "; the bands must cover every level up to " + MaxEncounterLevel + ".");
            }
        }

        private static void ValidateCells(DropCellData[] cells, string label, HashSet<string> shapes, Dictionary<string, int> tiers, List<string> errors)
        {
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);

            if (cells != null)
            {
                for (int c = 0; c < cells.Length; c++)
                {
                    DropCellData cell = cells[c];
                    if (cell == null)
                    {
                        errors.Add(label + ": cell #" + c + " is null.");
                        continue;
                    }

                    string at = label + " '" + cell.Shape + "'";
                    if (!shapes.Contains(cell.Shape ?? string.Empty))
                    {
                        errors.Add(at + ": not one of the Shapes.");
                    }
                    else if (!covered.Add(cell.Shape))
                    {
                        errors.Add(at + ": the band has two cells for this shape.");
                    }

                    ValidateDrops(cell.Drops, at, tiers, errors);
                }
            }

            foreach (string shape in shapes)
            {
                if (!covered.Contains(shape))
                {
                    errors.Add(label + ": no cell for shape '" + shape + "'.");
                }
            }
        }

        private static void ValidateDrops(DropEntryData[] drops, string at, Dictionary<string, int> tiers, List<string> errors)
        {
            if (drops == null)
            {
                errors.Add(at + ": Drops is null.");
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < drops.Length; i++)
            {
                DropEntryData d = drops[i];
                string entry = at + " Drops[" + i + "]";
                if (d == null)
                {
                    errors.Add(entry + " is null.");
                    continue;
                }

                if (CheckMaterial(d.MaterialId, entry, tiers, errors) && !seen.Add(d.MaterialId))
                {
                    errors.Add(entry + ": '" + d.MaterialId + "' appears twice in the cell.");
                }

                if (d.Chance < 1 || d.Chance > 100)
                {
                    errors.Add(entry + ": Chance is " + d.Chance + "; it must be a percent, 1 to 100.");
                }

                if (d.MinQty < 1 || d.MaxQty < d.MinQty || d.MaxQty > MaxQuantity)
                {
                    errors.Add(entry + ": quantity " + d.MinQty + "-" + d.MaxQty + " must satisfy 1 <= MinQty <= MaxQty <= " + MaxQuantity + ".");
                }
            }
        }

        private static bool CheckMaterial(string id, string at, Dictionary<string, int> tiers, List<string> errors)
        {
            if (string.IsNullOrEmpty(id))
            {
                errors.Add(at + ": material id is missing.");
                return false;
            }

            if (tiers != null && !tiers.ContainsKey(id))
            {
                errors.Add(at + ": '" + id + "' is not a material in the skill library.");
                return false;
            }

            return true;
        }
    }
}
