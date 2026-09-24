using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Progression;

namespace BeastCraft.Tooling.BalanceSim
{
    // ------------------------------------------------------------------------------------------
    // The PvE run's opposition. The default, generated set is GAME CONTENT: the enemy library and
    // the encounter shapes in BeastCraft/Assets/_Project/Data/Encounters, read through the game's
    // own validators and EnemyCatalog. The legacy fixed set (--encounter-set fixed) stays a
    // simulator fixture in Tooling/BalanceSim/encounters.json, which no game code reads.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Tooling/BalanceSim/encounters.json</c>: the legacy fixed encounters (simulator fixtures,
    /// not game content).
    /// </summary>
    public class FixedEncounterFileData
    {
        public int SchemaVersion;

        /// <summary>Growth curve (by CurveId, from beast-roster.json) every fixed enemy's stats scale with.</summary>
        public string GrowthCurveId;

        public EncounterData[] FixedEncounters = new EncounterData[0];
    }

    /// <summary>A fixed encounter's group: an enemy in the enemy-library shape, how many, and their elements.</summary>
    public class FixedGroupData : EnemyData
    {
        public int Count;

        /// <summary>Element per unit, cycled over the group (unit i gets Elements[i % length]); empty = None.</summary>
        public string[] Elements = new string[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public Element[] ParsedElements = new Element[0];
    }

    public class EncounterData
    {
        public string EncounterId;
        public string DisplayName;
        public string Description;

        /// <summary>An <see cref="ArenaSize"/> name.</summary>
        public string Arena;

        /// <summary>Enemy groups, placed front-to-back in this order.</summary>
        public FixedGroupData[] Groups = new FixedGroupData[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public ArenaSize ParsedArena;
    }

    /// <summary>Which encounters a run fights (<c>--encounter-set</c>).</summary>
    public enum EncounterSet
    {
        /// <summary>Compositions drawn by the game's encounter generator from the encounter library's shapes.</summary>
        Generated = 0,

        /// <summary>The hand-authored <c>FixedEncounters</c> of <c>encounters.json</c>.</summary>
        Fixed = 1
    }

    /// <summary>One enemy on the board: its id, the species it is built from, and its kit per mode.</summary>
    public class EnemySlot
    {
        public string UnitId;
        public string TypeId;
        public string GroupDisplayName;
        public Element Element;
        public double Threat;

        /// <summary>The enemy's <see cref="EnemyData.StatusResist"/>, handed to the unit.</summary>
        public int StatusResist;

        public CreatureSpeciesSO Species;
        public IReadOnlyList<SkillSO> ElementalKit;
        public IReadOnlyList<SkillSO> NeutralKit;

        public IReadOnlyList<SkillSO> KitFor(KitMode mode)
        {
            return mode == KitMode.Elemental ? ElementalKit : NeutralKit;
        }
    }

    /// <summary>One concrete enemy lineup, ready to field: a fixed encounter or one generated composition.</summary>
    public class Encounter
    {
        /// <summary>Unique across the run; seeds every battle against it.</summary>
        public string Id;

        public ArenaSize Arena;
        public List<EnemySlot> Enemies = new List<EnemySlot>();

        /// <summary>How the generator assigned elements (a scheme name); "authored" for fixed encounters.</summary>
        public string ElementScheme = "authored";

        /// <summary>Summed type threat (generated compositions).</summary>
        public double Threat;

        /// <summary>The authored groups of a fixed encounter; null for a generated composition.</summary>
        public EncounterData FixedData;

        /// <summary>
        /// Where the enemies stand, worked out once on first use (<see cref="PveSimulator"/>): the
        /// same for every battle against this lineup. Written at most once per value; a race between
        /// threads computes the same layout twice, which is harmless.
        /// </summary>
        public EnemyLayout Layout;
    }

    /// <summary>
    /// An encounter's enemy layout: one anchor per enemy (in <see cref="Encounter.Enemies"/> order)
    /// and every tile the enemies cover, from <c>DeploymentPacker.TryPack</c>.
    /// </summary>
    public class EnemyLayout
    {
        public List<HexCoordinate> Anchors = new List<HexCoordinate>();
        public List<HexCoordinate> Covered = new List<HexCoordinate>();
    }

    /// <summary>
    /// What a PvE cell is calibrated over: a generated shape with its compositions, or a fixed
    /// encounter (a shape of one composition).
    /// </summary>
    public class EncounterShape
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public ArenaSize Arena;

        /// <summary>The shape's definition (game content); null for a fixed encounter.</summary>
        public EncounterShapeData Data;

        public List<Encounter> Compositions = new List<Encounter>();
    }

    /// <summary>Everything the PvE run fights, plus what the report needs to describe it.</summary>
    public class EncounterCatalog
    {
        public EncounterSet Set;
        public List<EncounterShape> Shapes = new List<EncounterShape>();

        /// <summary>The enemy library (generated set), in file order.</summary>
        public List<EnemyData> Types = new List<EnemyData>();

        /// <summary>The element-scheme weights the compositions were drawn with (generated set), in file order.</summary>
        public List<SchemeWeightData> SchemeWeights = new List<SchemeWeightData>();

        /// <summary>
        /// <c>--panel</c> only (else empty): the composition panel, one shape per entry of
        /// <see cref="Shapes"/> (same ids, same order) with <c>--panel</c>'s K compositions drawn from
        /// <see cref="SimOptions.PanelSeed"/>.
        /// </summary>
        public List<EncounterShape> PanelShapes = new List<EncounterShape>();

        /// <summary>Every composition of every shape, in shape then composition order.</summary>
        public List<Encounter> AllCompositions
        {
            get
            {
                List<Encounter> all = new List<Encounter>();
                foreach (EncounterShape shape in Shapes)
                {
                    all.AddRange(shape.Compositions);
                }

                return all;
            }
        }
    }

    /// <summary>
    /// Reads the run's encounters. The game content (<c>enemy-library.json</c>,
    /// <c>encounter-library.json</c>, cross-checked against the roster and <c>drop-tables.json</c>)
    /// goes through the game's <see cref="EnemyLibraryValidator"/> and
    /// <see cref="EncounterLibraryValidator"/>, and its enemies through <see cref="EnemyCatalog"/>, so
    /// the simulator fights exactly what the game fields. The legacy fixed set
    /// (<c>Tooling/BalanceSim/encounters.json</c>) is read with its own checks. Everything is
    /// validated whichever set runs, so a broken file is never silent.
    /// </summary>
    public static class EncounterLoader
    {
        /// <summary>The enemy library's path relative to the repo root.</summary>
        public const string EnemyLibraryRepoRelativePath = "BeastCraft/" + EnemyLibraryData.ProjectRelativePath;

        /// <summary>The encounter library's path relative to the repo root.</summary>
        public const string EncounterLibraryRepoRelativePath = "BeastCraft/" + EncounterLibraryData.ProjectRelativePath;

        /// <summary>The legacy fixed encounters' path relative to the repo root.</summary>
        public const string FixedRepoRelativePath = "Tooling/BalanceSim/encounters.json";

        /// <summary>The only <see cref="FixedEncounterFileData.SchemaVersion"/> read. 3 moved the generated set out to the game content.</summary>
        public const int FixedSchemaVersion = 3;

        /// <summary>
        /// Loads and validates every encounter file and builds the run's encounters: the generated
        /// compositions (default) or the fixed encounters. Returns null and fills
        /// <paramref name="errors"/> when a file is missing, unreadable or invalid.
        /// </summary>
        public static EncounterCatalog Load(SimOptions options, IReadOnlyDictionary<string, GrowthRateCurve> curves, List<string> errors)
        {
            BeastRosterData roster = Read<BeastRosterData>(RosterLoader.ResolvePath(options.RosterPath), RosterLoader.RepoRelativePath, "--roster", errors);
            EnemyLibraryData enemyLibrary = Read<EnemyLibraryData>(RosterLoader.ResolveFile(options.EnemyLibraryPath, EnemyLibraryRepoRelativePath),
                                                                   EnemyLibraryRepoRelativePath, "--enemy-library", errors);
            EncounterLibraryData encounterLibrary = Read<EncounterLibraryData>(RosterLoader.ResolveFile(options.EncounterLibraryPath, EncounterLibraryRepoRelativePath),
                                                                               EncounterLibraryRepoRelativePath, "--encounter-library", errors);
            DropTableData dropTables = Read<DropTableData>(RosterLoader.ResolveFile(options.DropTablesPath, PacingSimulator.DropTablesRepoRelativePath),
                                                           PacingSimulator.DropTablesRepoRelativePath, "--drop-tables", errors);
            FixedEncounterFileData fixedFile = Read<FixedEncounterFileData>(RosterLoader.ResolveFile(options.EncountersPath, FixedRepoRelativePath),
                                                                            FixedRepoRelativePath, "--encounters-file", errors);
            if (errors.Count > 0)
            {
                return null;
            }

            foreach (string error in EnemyLibraryValidator.Validate(enemyLibrary, roster))
            {
                errors.Add("enemy-library.json: " + error);
            }

            foreach (string error in EncounterLibraryValidator.Validate(encounterLibrary, enemyLibrary, dropTables))
            {
                errors.Add("encounter-library.json: " + error);
            }

            GrowthRateCurve curve = null;
            if (enemyLibrary.GrowthCurveId != null && !curves.TryGetValue(enemyLibrary.GrowthCurveId, out curve))
            {
                errors.Add("enemy-library.json: GrowthCurveId '" + enemyLibrary.GrowthCurveId + "' is not a growth curve in beast-roster.json.");
            }

            GrowthRateCurve fixedCurve = ValidateFixedFile(fixedFile, curves, errors);

            if (options.EncounterSet == EncounterSet.Generated && (encounterLibrary.Shapes == null || encounterLibrary.Shapes.Length == 0))
            {
                errors.Add("No Shapes defined for the generated encounter set.");
            }

            if (options.EncounterSet == EncounterSet.Fixed && (fixedFile.FixedEncounters == null || fixedFile.FixedEncounters.Length == 0))
            {
                errors.Add("No FixedEncounters defined.");
            }

            if (errors.Count > 0)
            {
                return null;
            }

            List<string> known = new List<string>();
            if (options.EncounterSet == EncounterSet.Generated)
            {
                foreach (EncounterShapeData shape in encounterLibrary.Shapes)
                {
                    known.Add(shape.ShapeId);
                }
            }
            else
            {
                foreach (EncounterData encounter in fixedFile.FixedEncounters)
                {
                    known.Add(encounter.EncounterId);
                }
            }

            if (options.EncounterFilter != null)
            {
                foreach (string wanted in options.EncounterFilter)
                {
                    if (!known.Contains(wanted))
                    {
                        errors.Add("--encounters: unknown " + (options.EncounterSet == EncounterSet.Generated ? "shape" : "encounter") + " id '" + wanted +
                                   "'. Known: " + string.Join(", ", known) + ".");
                    }
                }

                if (errors.Count > 0)
                {
                    return null;
                }
            }

            EncounterCatalog catalog = new EncounterCatalog { Set = options.EncounterSet };
            if (options.EncounterSet == EncounterSet.Generated)
            {
                EnemyCatalog enemies = EnemyCatalog.Build(enemyLibrary, curve);
                catalog.Types.AddRange(enemies.Enemies);
                catalog.SchemeWeights.AddRange(encounterLibrary.SchemeWeights);

                // Every shape is generated, filter or not, so a shape's compositions never depend on
                // which other shapes the run happens to include.
                List<EncounterShape> all = GeneratedEncounters.Generate(encounterLibrary, enemies, options, errors);
                if (all == null)
                {
                    return null;
                }

                foreach (EncounterShape shape in all)
                {
                    if (options.EncounterFilter == null || options.EncounterFilter.Contains(shape.Id))
                    {
                        catalog.Shapes.Add(shape);
                    }
                }

                if (options.PanelActive)
                {
                    List<EncounterShape> panel = GeneratedEncounters.Generate(encounterLibrary, enemies, options, errors, SimOptions.PanelSeed, options.PanelCompositions,
                                                                              "panel-");
                    if (panel == null)
                    {
                        return null;
                    }

                    foreach (EncounterShape shape in catalog.Shapes)
                    {
                        catalog.PanelShapes.Add(panel.Find(p => p.Id == shape.Id));
                    }
                }
            }
            else
            {
                foreach (EncounterData data in fixedFile.FixedEncounters)
                {
                    if (options.EncounterFilter != null && !options.EncounterFilter.Contains(data.EncounterId))
                    {
                        continue;
                    }

                    Encounter encounter = BuildFixed(data, fixedCurve, options);
                    EncounterShape shape = new EncounterShape
                    {
                        Id = data.EncounterId,
                        DisplayName = data.DisplayName,
                        Description = data.Description,
                        Arena = data.ParsedArena
                    };
                    shape.Compositions.Add(encounter);
                    catalog.Shapes.Add(shape);
                }
            }

            return catalog;
        }

        /// <summary>
        /// Reads one JSON file with System.Text.Json (fields included, as the game's DTOs need), or
        /// adds an error naming the flag that overrides its path.
        /// </summary>
        private static T Read<T>(string path, string repoRelativePath, string flag, List<string> errors) where T : class
        {
            if (path == null || !File.Exists(path))
            {
                errors.Add("Could not find " + repoRelativePath + "; run from inside the repo or pass " + flag + " <path>.");
                return null;
            }

            try
            {
                T data = JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
                if (data == null)
                {
                    errors.Add("'" + path + "' is empty.");
                }

                return data;
            }
            catch (Exception exception)
            {
                errors.Add("Could not read '" + path + "': " + exception.Message);
                return null;
            }
        }

        /// <summary>The fixed file's checks; returns its growth curve (null when invalid).</summary>
        private static GrowthRateCurve ValidateFixedFile(FixedEncounterFileData file, IReadOnlyDictionary<string, GrowthRateCurve> curves, List<string> errors)
        {
            if (file.SchemaVersion != FixedSchemaVersion)
            {
                errors.Add("encounters.json: SchemaVersion " + file.SchemaVersion + " is not the supported " + FixedSchemaVersion + ".");
                return null;
            }

            GrowthRateCurve curve = null;
            if (string.IsNullOrEmpty(file.GrowthCurveId) || !curves.TryGetValue(file.GrowthCurveId, out curve))
            {
                errors.Add("encounters.json: GrowthCurveId '" + file.GrowthCurveId + "' is not a growth curve in beast-roster.json.");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EncounterData encounter in file.FixedEncounters ?? new EncounterData[0])
            {
                ValidateFixed(encounter, ids, errors);
            }

            return curve;
        }

        private static void ValidateFixed(EncounterData encounter, HashSet<string> ids, List<string> errors)
        {
            string where = "Fixed encounter '" + encounter.EncounterId + "'";
            if (string.IsNullOrEmpty(encounter.EncounterId) || !ids.Add(encounter.EncounterId))
            {
                errors.Add(where + ": missing or duplicate EncounterId.");
                return;
            }

            if (!EncounterLibraryValidator.TryParseArena(encounter.Arena, out encounter.ParsedArena))
            {
                errors.Add(where + ": Arena '" + encounter.Arena + "' is not Small, Medium or Large.");
                return;
            }

            if (encounter.Groups == null || encounter.Groups.Length == 0)
            {
                errors.Add(where + ": no enemy groups.");
                return;
            }

            int zoneSize = new HexGrid(encounter.ParsedArena).GetDeploymentZone(BattleTeam.Enemy).Count;
            int total = 0;
            List<UnitFootprint> footprints = new List<UnitFootprint>();
            HashSet<string> groupIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (FixedGroupData group in encounter.Groups)
            {
                string groupWhere = where + " group '" + group.EnemyId + "'";
                if (string.IsNullOrEmpty(group.EnemyId) || !groupIds.Add(group.EnemyId) || group.Count < 1)
                {
                    errors.Add(groupWhere + ": needs a unique EnemyId and a Count of at least 1.");
                    continue;
                }

                total += group.Count;

                List<Element> elements = new List<Element>();
                foreach (string name in group.Elements ?? new string[0])
                {
                    if (BeastRosterValidator.TryParseElement(name, out Element element))
                    {
                        elements.Add(element);
                    }
                    else
                    {
                        errors.Add(groupWhere + ": unknown element '" + name + "'.");
                    }
                }

                group.ParsedElements = elements.ToArray();
                EnemyLibraryValidator.ValidateEnemy(group, groupWhere, errors);
                EnemyLibraryValidator.TryParseFootprint(group.Footprint, out UnitFootprint footprint);

                for (int i = 0; i < group.Count && i <= EncounterLibraryValidator.MaxEnemies; i++)
                {
                    footprints.Add(footprint);
                }
            }

            if (total <= EncounterLibraryValidator.MaxEnemies && !EncounterFit.Fits(encounter.ParsedArena, footprints))
            {
                errors.Add(where + ": " + total + " enemies do not fit the " + encounter.ParsedArena + " enemy deployment zone (" + zoneSize +
                           " tiles; large enemies need their whole footprint inside it).");
            }

            if (total > EncounterLibraryValidator.MaxEnemies)
            {
                errors.Add(where + ": more than " + EncounterLibraryValidator.MaxEnemies + " enemies (unit ids are two digits).");
            }
        }

        private static Encounter BuildFixed(EncounterData data, GrowthRateCurve curve, SimOptions options)
        {
            Encounter encounter = new Encounter { Id = data.EncounterId, Arena = data.ParsedArena, FixedData = data };
            EnemyFactory factory = new EnemyFactory(EnemyCatalog.Build(data.Groups, curve));
            int unit = 0;

            foreach (FixedGroupData group in data.Groups)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    unit++;
                    Element element = options.EnemyElementOverride ??
                                      (group.ParsedElements.Length == 0 ? Element.None : group.ParsedElements[i % group.ParsedElements.Length]);
                    encounter.Enemies.Add(factory.Slot(group.EnemyId, element, unit));
                }
            }

            return encounter;
        }
    }

    /// <summary>
    /// Builds enemy slots over an <see cref="EnemyCatalog"/>, which shares one species and one kit
    /// per (enemy, element), so a horde of identical units does not allocate a skill set per unit.
    /// Species and kits are read-only in battle, so sharing is safe across parallel battles.
    /// </summary>
    public class EnemyFactory
    {
        private readonly EnemyCatalog _catalog;

        public EnemyFactory(EnemyCatalog catalog)
        {
            _catalog = catalog;
        }

        public EnemySlot Slot(string enemyId, Element element, int unitNumber)
        {
            EnemyData enemy = _catalog.Get(enemyId);
            return new EnemySlot
            {
                UnitId = "e" + unitNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                TypeId = enemy.EnemyId,
                GroupDisplayName = enemy.DisplayName,
                Element = element,
                Threat = enemy.Threat,
                StatusResist = enemy.StatusResist,
                Species = _catalog.Species(enemyId, element),
                ElementalKit = _catalog.Kit(enemyId, element),
                NeutralKit = _catalog.Kit(enemyId, Element.None)
            };
        }
    }
}
