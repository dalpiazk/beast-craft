using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// Structural checks for <see cref="EncounterLibraryData"/> against the enemy library (and the
    /// drop tables, when given): ids present, unique and snake_case; every enemy reference resolves;
    /// every enum name parses; threat budgets and counts are sane; the element-scheme weights can
    /// draw something; every shape's worst case (each slot at its Max, each unit its slot's largest
    /// type) and every template seats on its arena's enemy deployment zone the way a battle packs it
    /// (<see cref="EncounterFit"/>: a seven-tile enemy never fits a Small arena); and the shape ids
    /// are exactly the drop tables' shape ids, so every cleared encounter has a drop cell.
    /// </summary>
    public static class EncounterLibraryValidator
    {
        /// <summary>At most this many enemies per encounter (the simulator's unit ids are two digits).</summary>
        public const int MaxEnemies = 99;

        /// <summary>The largest <see cref="EncounterLibraryData.DifficultyScale"/> or template override accepted.</summary>
        public const double MaxDifficulty = 10.0;

        /// <summary>Returns every problem found; an empty list means the library is importable.</summary>
        /// <param name="library">The encounter library.</param>
        /// <param name="enemies">The enemy library every enemy id must resolve in. Null skips those checks.</param>
        /// <param name="dropTables">The drop tables whose shape ids must match. Null skips that check.</param>
        public static List<string> Validate(EncounterLibraryData library, EnemyLibraryData enemies, DropTableData dropTables)
        {
            List<string> errors = new List<string>();

            if (library == null)
            {
                errors.Add("Encounter library is null (the JSON did not parse).");
                return errors;
            }

            if (library.SchemaVersion != EncounterLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + library.SchemaVersion + "; this code reads version " + EncounterLibraryData.CurrentSchemaVersion + ".");
            }

            if (!(library.DifficultyScale > 0.0) || library.DifficultyScale > MaxDifficulty)
            {
                errors.Add("DifficultyScale is " + library.DifficultyScale + "; it must be above 0 and at most " + MaxDifficulty + ".");
            }

            ValidateSchemes(library.SchemeWeights, errors);

            Dictionary<string, EnemyData> byId = null;
            if (enemies != null)
            {
                byId = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
                foreach (EnemyData enemy in enemies.Enemies ?? new EnemyData[0])
                {
                    if (enemy != null && !string.IsNullOrEmpty(enemy.EnemyId) && !byId.ContainsKey(enemy.EnemyId))
                    {
                        byId.Add(enemy.EnemyId, enemy);
                    }
                }
            }

            HashSet<string> shapeIds = new HashSet<string>(StringComparer.Ordinal);
            if (library.Shapes == null || library.Shapes.Length == 0)
            {
                errors.Add("No Shapes defined.");
            }
            else
            {
                for (int i = 0; i < library.Shapes.Length; i++)
                {
                    ValidateShape(library.Shapes[i], i, byId, shapeIds, errors);
                }
            }

            HashSet<string> templateIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; library.Templates != null && i < library.Templates.Length; i++)
            {
                ValidateTemplate(library.Templates[i], i, byId, shapeIds, templateIds, errors);
            }

            if (dropTables != null)
            {
                HashSet<string> dropShapes = new HashSet<string>(dropTables.Shapes ?? new string[0], StringComparer.Ordinal);
                foreach (string id in shapeIds)
                {
                    if (!dropShapes.Contains(id))
                    {
                        errors.Add("Shape '" + id + "' is not a shape in drop-tables.json; a clear of it would drop nothing.");
                    }
                }

                foreach (string id in dropShapes)
                {
                    if (!shapeIds.Contains(id))
                    {
                        errors.Add("drop-tables.json shape '" + id + "' is not a shape in the encounter library.");
                    }
                }
            }

            return errors;
        }

        /// <summary>Parses an <see cref="ArenaSize"/> name exactly as written (names only; missing is not an arena).</summary>
        public static bool TryParseArena(string text, out ArenaSize arena)
        {
            arena = ArenaSize.Medium;
            if (string.IsNullOrEmpty(text) || !Enum.IsDefined(typeof(ArenaSize), text))
            {
                return false;
            }

            arena = (ArenaSize)Enum.Parse(typeof(ArenaSize), text);
            return true;
        }

        private static void ValidateSchemes(SchemeWeightData[] schemes, List<string> errors)
        {
            if (schemes == null || schemes.Length == 0)
            {
                errors.Add("No SchemeWeights defined.");
                return;
            }

            HashSet<ElementScheme> seen = new HashSet<ElementScheme>();
            long total = 0;
            for (int i = 0; i < schemes.Length; i++)
            {
                SchemeWeightData entry = schemes[i];
                if (entry == null)
                {
                    errors.Add("SchemeWeights[" + i + "] is null.");
                    continue;
                }

                string label = "SchemeWeights[" + i + "]";
                if (string.IsNullOrEmpty(entry.Scheme) || !Enum.IsDefined(typeof(ElementScheme), entry.Scheme))
                {
                    errors.Add(label + ": Scheme '" + entry.Scheme + "' is not Uniform, PerType, PerUnit or None.");
                }
                else if (!seen.Add((ElementScheme)Enum.Parse(typeof(ElementScheme), entry.Scheme)))
                {
                    errors.Add(label + ": Scheme '" + entry.Scheme + "' is listed twice.");
                }

                if (entry.Weight < 0)
                {
                    errors.Add(label + ": Weight must not be negative.");
                }
                else
                {
                    total += entry.Weight;
                }
            }

            if (total <= 0)
            {
                errors.Add("SchemeWeights: the weights must add up to more than 0.");
            }
        }

        private static void ValidateShape(EncounterShapeData shape, int index, Dictionary<string, EnemyData> enemies, HashSet<string> ids, List<string> errors)
        {
            if (shape == null)
            {
                errors.Add("Shape #" + index + " is null.");
                return;
            }

            string where = "Shape '" + shape.ShapeId + "'";
            if (!BeastRosterValidator.IsSnakeCaseId(shape.ShapeId))
            {
                errors.Add(where + ": ShapeId must be lowercase snake_case.");
            }
            else if (!ids.Add(shape.ShapeId))
            {
                errors.Add(where + ": duplicate ShapeId.");
            }

            if (!TryParseArena(shape.Arena, out ArenaSize arena))
            {
                errors.Add(where + ": Arena '" + shape.Arena + "' is not Small, Medium or Large.");
                return;
            }

            if (shape.ThreatMin > shape.ThreatMax || !(shape.ThreatMax > 0.0) || shape.ThreatMin < 0.0)
            {
                errors.Add(where + ": needs 0 <= ThreatMin <= ThreatMax and ThreatMax > 0.");
            }

            if (shape.MinDistinctTypes < 1)
            {
                errors.Add(where + ": MinDistinctTypes must be >= 1.");
            }

            if (shape.Variants == null || shape.Variants.Length == 0)
            {
                errors.Add(where + ": no Variants.");
                return;
            }

            int zoneSize = new HexGrid(arena).GetDeploymentZone(BattleTeam.Enemy).Count;
            foreach (EncounterVariantData variant in shape.Variants)
            {
                if (variant == null)
                {
                    errors.Add(where + ": a variant is null.");
                    continue;
                }

                string variantWhere = where + " variant '" + variant.Label + "'";
                if (variant.Weight < 1 || variant.Slots == null || variant.Slots.Length == 0)
                {
                    errors.Add(variantWhere + ": needs Weight >= 1 and at least one slot.");
                    continue;
                }

                int max = 0;
                List<UnitFootprint> worst = new List<UnitFootprint>();
                foreach (EncounterSlotData slot in variant.Slots)
                {
                    if (slot == null || slot.Types == null || slot.Types.Length == 0 || slot.Min < 0 || slot.Max < slot.Min)
                    {
                        errors.Add(variantWhere + ": each slot needs Types and 0 <= Min <= Max.");
                        continue;
                    }

                    max += slot.Max;
                    UnitFootprint largest = UnitFootprint.Single;
                    foreach (string type in slot.Types)
                    {
                        if (enemies == null)
                        {
                            continue;
                        }

                        if (type == null || !enemies.TryGetValue(type, out EnemyData data))
                        {
                            errors.Add(variantWhere + ": unknown enemy type '" + type + "'.");
                        }
                        else if (EnemyLibraryValidator.TryParseFootprint(data.Footprint, out UnitFootprint footprint) &&
                                 Footprints.TileCount(footprint) > Footprints.TileCount(largest))
                        {
                            largest = footprint;
                        }
                    }

                    for (int i = 0; i < slot.Max && i <= MaxEnemies; i++)
                    {
                        worst.Add(largest);
                    }
                }

                // Worst case: every slot at its Max, each unit its slot's largest type, the largest
                // placed first (the generator's order puts the Vanguard bosses first).
                worst.Sort((a, b) => Footprints.TileCount(b).CompareTo(Footprints.TileCount(a)));
                if (max > MaxEnemies || !EncounterFit.Fits(arena, worst))
                {
                    errors.Add(variantWhere + ": up to " + max + " enemies (every slot at Max, each its slot's largest type) do not fit the " + arena +
                               " enemy deployment zone (" + zoneSize + " tiles; large enemies need their whole footprint inside it) or exceed " + MaxEnemies + ".");
                }
            }
        }

        private static void ValidateTemplate(EncounterTemplateData template, int index, Dictionary<string, EnemyData> enemies, HashSet<string> shapeIds,
                                             HashSet<string> ids, List<string> errors)
        {
            if (template == null)
            {
                errors.Add("Template #" + index + " is null.");
                return;
            }

            string where = "Template '" + template.EncounterId + "'";
            if (!BeastRosterValidator.IsSnakeCaseId(template.EncounterId))
            {
                errors.Add(where + ": EncounterId must be lowercase snake_case.");
            }
            else if (!ids.Add(template.EncounterId))
            {
                errors.Add(where + ": duplicate EncounterId.");
            }

            if (string.IsNullOrEmpty(template.ShapeId) || !shapeIds.Contains(template.ShapeId))
            {
                errors.Add(where + ": ShapeId '" + template.ShapeId + "' is not a shape in this library (it sets the difficulty row and the drop cell).");
            }

            if (template.DifficultyOverride < 0.0 || template.DifficultyOverride > MaxDifficulty)
            {
                errors.Add(where + ": DifficultyOverride must be 0 (none) or above 0 and at most " + MaxDifficulty + ".");
            }

            if (!TryParseArena(template.Arena, out ArenaSize arena))
            {
                errors.Add(where + ": Arena '" + template.Arena + "' is not Small, Medium or Large.");
                return;
            }

            if (template.Groups == null || template.Groups.Length == 0)
            {
                errors.Add(where + ": no enemy groups.");
                return;
            }

            int total = 0;
            List<UnitFootprint> footprints = new List<UnitFootprint>();
            foreach (EncounterGroupData group in template.Groups)
            {
                if (group == null)
                {
                    errors.Add(where + ": a group is null.");
                    continue;
                }

                string groupWhere = where + " group '" + group.EnemyId + "'";
                if (group.Count < 1)
                {
                    errors.Add(groupWhere + ": Count must be at least 1.");
                    continue;
                }

                total += group.Count;
                foreach (string name in group.Elements ?? new string[0])
                {
                    if (!BeastRosterValidator.TryParseElement(name, out Element _))
                    {
                        errors.Add(groupWhere + ": unknown element '" + name + "'.");
                    }
                }

                UnitFootprint footprint = UnitFootprint.Single;
                if (enemies != null)
                {
                    if (group.EnemyId == null || !enemies.TryGetValue(group.EnemyId, out EnemyData data))
                    {
                        errors.Add(groupWhere + ": unknown enemy.");
                        continue;
                    }

                    EnemyLibraryValidator.TryParseFootprint(data.Footprint, out footprint);
                }

                for (int i = 0; i < group.Count && footprints.Count <= MaxEnemies; i++)
                {
                    footprints.Add(footprint);
                }
            }

            if (total > MaxEnemies)
            {
                errors.Add(where + ": more than " + MaxEnemies + " enemies.");
            }
            else if (!EncounterFit.Fits(arena, footprints))
            {
                errors.Add(where + ": its " + total + " enemies do not fit the " + arena + " enemy deployment zone (large enemies need their whole footprint inside it).");
            }
        }
    }
}
