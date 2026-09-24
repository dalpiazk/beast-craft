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
    /// draw something; every lineup a shape's variants can draw, placed in the generator's
    /// front-to-back order (<see cref="EncounterFit.ComparePlacement"/>), and every template seats on
    /// its arena's enemy deployment zone the way a battle packs it (<see cref="EncounterFit"/>: a
    /// seven-tile enemy never fits a Small arena); and the shape ids are exactly the drop tables'
    /// shape ids, so every cleared encounter has a drop cell.
    /// </summary>
    public static class EncounterLibraryValidator
    {
        /// <summary>At most this many enemies per encounter (the simulator's unit ids are two digits).</summary>
        public const int MaxEnemies = 99;

        /// <summary>The largest <see cref="EncounterLibraryData.DifficultyScale"/> or template override accepted.</summary>
        public const double MaxDifficulty = 10.0;

        /// <summary>
        /// At most this many distinct placement sequences are fit-checked per shape variant; a variant
        /// that can draw more is refused rather than half-checked (today's variants draw a few dozen).
        /// </summary>
        public const int MaxLineupsChecked = 20000;

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

            // Same rule as EnemyCatalog.Build (null and empty ids skipped, the first of a duplicate id
            // wins), so libraryOrder holds the index the generator orders types by.
            Dictionary<string, EnemyData> byId = null;
            Dictionary<string, int> libraryOrder = null;
            if (enemies != null)
            {
                byId = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
                libraryOrder = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (EnemyData enemy in enemies.Enemies ?? new EnemyData[0])
                {
                    if (enemy != null && !string.IsNullOrEmpty(enemy.EnemyId) && !byId.ContainsKey(enemy.EnemyId))
                    {
                        libraryOrder.Add(enemy.EnemyId, byId.Count);
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
                    ValidateShape(library.Shapes[i], i, byId, libraryOrder, shapeIds, errors);
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

        private static void ValidateShape(EncounterShapeData shape, int index, Dictionary<string, EnemyData> enemies, Dictionary<string, int> libraryOrder,
                                          HashSet<string> ids, List<string> errors)
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

            if (!(shape.TargetClear > 0.0 && shape.TargetClear < 100.0))
            {
                errors.Add(where + ": TargetClear " + shape.TargetClear.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                           " must be strictly between 0 and 100 (the percent clear rate its difficulty is calibrated to).");
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
                List<EncounterSlotData> slots = new List<EncounterSlotData>();
                foreach (EncounterSlotData slot in variant.Slots)
                {
                    if (slot == null || slot.Types == null || slot.Types.Length == 0 || slot.Min < 0 || slot.Max < slot.Min)
                    {
                        errors.Add(variantWhere + ": each slot needs Types and 0 <= Min <= Max.");
                        continue;
                    }

                    max += slot.Max;
                    slots.Add(slot);
                    foreach (string type in slot.Types)
                    {
                        if (enemies != null && (type == null || !enemies.ContainsKey(type)))
                        {
                            errors.Add(variantWhere + ": unknown enemy type '" + type + "'.");
                        }
                    }
                }

                if (max > MaxEnemies)
                {
                    errors.Add(variantWhere + ": up to " + max + " enemies (every slot at Max) exceed " + MaxEnemies + ".");
                    continue;
                }

                string unfit = FindUnfitLineup(arena, slots, enemies, libraryOrder, out int unfitCount, out bool tooMany);
                if (tooMany)
                {
                    errors.Add(variantWhere + ": can draw more than " + MaxLineupsChecked + " distinct placement sequences, too many to fit-check; narrow its slots.");
                }
                else if (unfit != null)
                {
                    errors.Add(variantWhere + ": " + unfitCount + " enemies it can draw (" + unfit + ", placed front to back in the generator's order) do not fit the " +
                               arena + " enemy deployment zone (" + zoneSize + " tiles; large enemies need their whole footprint inside it).");
                }
            }
        }

        /// <summary>
        /// Fit-checks every lineup a variant's <paramref name="slots"/> can draw, placed exactly as
        /// <see cref="EncounterGenerator"/> places it, and returns the first that does not seat
        /// (described as "3 x Single, 1 x Hex7" in placement order, with its size in
        /// <paramref name="count"/>), or null when every one does.
        /// <para>
        /// Why every lineup rather than one "largest" case: the generator places types front to back
        /// by <see cref="EncounterFit.ComparePlacement"/> (stance, then library order), not largest
        /// first, and <see cref="Battle.Placement.DeploymentPacker"/> packs greedily in that order.
        /// Greedy packing is not monotone: a large Ranged enemy seats behind nothing but not behind
        /// a screen of Vanguard singles that took the front row, and a different number of units
        /// ahead of a large one moves where it lands. So no one lineup (every slot at Max, each its
        /// slot's largest type, in any one order) bounds the rest, and the check walks every count
        /// vector the slots can produce.
        /// </para>
        /// <para>
        /// That stays small because placement depends only on the sequence of footprints: the
        /// variant's types, in placement order, collapse into runs of equal footprint (a run's types
        /// always sit next to each other, so only its total count matters). Today's library has at
        /// most three runs per variant. The threat budget and MinDistinctTypes are deliberately
        /// ignored (a superset of the draws the generator keeps), so a valid library never makes a
        /// draw fail the generator's fit safety net. Unknown types count as one-tile Vanguards (the
        /// catalog's defaults; they are reported separately).
        /// </para>
        /// </summary>
        private static string FindUnfitLineup(ArenaSize arena, List<EncounterSlotData> slots, Dictionary<string, EnemyData> enemies,
                                              Dictionary<string, int> libraryOrder, out int count, out bool tooMany)
        {
            count = 0;
            tooMany = false;

            // Every type the variant can draw, with its footprint and stance.
            List<string> types = new List<string>();
            Dictionary<string, UnitFootprint> footprintOf = new Dictionary<string, UnitFootprint>(StringComparer.Ordinal);
            Dictionary<string, CombatStance> stanceOf = new Dictionary<string, CombatStance>(StringComparer.Ordinal);
            foreach (EncounterSlotData slot in slots)
            {
                foreach (string type in slot.Types)
                {
                    string id = type ?? string.Empty;
                    if (footprintOf.ContainsKey(id))
                    {
                        continue;
                    }

                    EnemyData data = null;
                    if (enemies != null)
                    {
                        enemies.TryGetValue(id, out data);
                    }

                    EnemyLibraryValidator.TryParseFootprint(data == null ? null : data.Footprint, out UnitFootprint footprint);
                    BeastRosterValidator.TryParseStance(data == null ? null : data.Stance, out CombatStance stance);
                    types.Add(id);
                    footprintOf.Add(id, footprint);
                    stanceOf.Add(id, stance);
                }
            }

            // The generator's placement order (unknown types last; ties, which only unknown types
            // can have, by id so the check is deterministic).
            types.Sort((a, b) =>
            {
                int byPlacement = EncounterFit.ComparePlacement(stanceOf[a], LibraryIndex(libraryOrder, a), stanceOf[b], LibraryIndex(libraryOrder, b));
                return byPlacement != 0 ? byPlacement : string.CompareOrdinal(a, b);
            });

            // Runs of equal footprint, in placement order.
            List<UnitFootprint> runs = new List<UnitFootprint>();
            Dictionary<string, int> runOf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string type in types)
            {
                if (runs.Count == 0 || runs[runs.Count - 1] != footprintOf[type])
                {
                    runs.Add(footprintOf[type]);
                }

                runOf.Add(type, runs.Count - 1);
            }

            // Every reachable vector of run counts, slot by slot.
            Dictionary<string, int[]> vectors = new Dictionary<string, int[]>(StringComparer.Ordinal);
            vectors.Add(Key(new int[runs.Count]), new int[runs.Count]);
            foreach (EncounterSlotData slot in slots)
            {
                List<int> slotRuns = new List<int>();
                foreach (string type in slot.Types)
                {
                    int run = runOf[type ?? string.Empty];
                    if (!slotRuns.Contains(run))
                    {
                        slotRuns.Add(run);
                    }
                }

                Dictionary<string, int[]> next = new Dictionary<string, int[]>(StringComparer.Ordinal);
                foreach (int[] vector in vectors.Values)
                {
                    for (int n = slot.Min; n <= slot.Max; n++)
                    {
                        if (!Distribute(vector, slotRuns, 0, n, next))
                        {
                            tooMany = true;
                            return null;
                        }
                    }
                }

                vectors = next;
            }

            List<string> keys = new List<string>(vectors.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                int[] vector = vectors[key];
                List<UnitFootprint> lineup = new List<UnitFootprint>();
                List<string> parts = new List<string>();
                for (int r = 0; r < runs.Count; r++)
                {
                    for (int i = 0; i < vector[r]; i++)
                    {
                        lineup.Add(runs[r]);
                    }

                    if (vector[r] > 0)
                    {
                        parts.Add(vector[r] + " x " + runs[r]);
                    }
                }

                // The generator never keeps an empty draw.
                if (lineup.Count > 0 && !EncounterFit.Fits(arena, lineup))
                {
                    count = lineup.Count;
                    return string.Join(", ", parts);
                }
            }

            return null;
        }

        private static int LibraryIndex(Dictionary<string, int> libraryOrder, string enemyId)
        {
            return libraryOrder != null && libraryOrder.TryGetValue(enemyId, out int index) ? index : int.MaxValue;
        }

        /// <summary>
        /// Adds to <paramref name="into"/> every way of spreading <paramref name="remaining"/> units
        /// over <paramref name="slotRuns"/> (from index <paramref name="from"/> on) on top of
        /// <paramref name="vector"/>. False once <paramref name="into"/> holds more than
        /// <see cref="MaxLineupsChecked"/> vectors.
        /// </summary>
        private static bool Distribute(int[] vector, List<int> slotRuns, int from, int remaining, Dictionary<string, int[]> into)
        {
            if (from == slotRuns.Count - 1)
            {
                int[] result = (int[])vector.Clone();
                result[slotRuns[from]] += remaining;
                string key = Key(result);
                if (!into.ContainsKey(key))
                {
                    into.Add(key, result);
                }

                return into.Count <= MaxLineupsChecked;
            }

            for (int take = 0; take <= remaining; take++)
            {
                int[] partial = (int[])vector.Clone();
                partial[slotRuns[from]] += take;
                if (!Distribute(partial, slotRuns, from + 1, remaining - take, into))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Key(int[] vector)
        {
            return string.Join(",", vector);
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
