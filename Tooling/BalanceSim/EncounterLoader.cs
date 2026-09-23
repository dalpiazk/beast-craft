using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using UnityEngine;

namespace BeastCraft.Tooling.BalanceSim
{
    // ------------------------------------------------------------------------------------------
    // encounters.json data model. SIMULATOR FIXTURES, NOT GAME CONTENT: these enemies exist only
    // so the simulator has something PvE-shaped to point the roster at. No game code reads them.
    // ------------------------------------------------------------------------------------------

    public class EncounterFileData
    {
        public int SchemaVersion;

        /// <summary>Growth curve (by CurveId, from beast-roster.json) every enemy's stats scale with.</summary>
        public string GrowthCurveId;

        /// <summary>The enemy type pool the generator draws compositions from.</summary>
        public EnemyTypeData[] EnemyTypes = new EnemyTypeData[0];

        /// <summary>Encounter shapes: the rules the generator draws compositions under.</summary>
        public ShapeData[] Shapes = new ShapeData[0];

        /// <summary>The hand-authored encounters of <c>--encounter-set fixed</c>.</summary>
        public EncounterData[] FixedEncounters = new EncounterData[0];
    }

    /// <summary>One kind of enemy: stats, stance and kit. Shared by the type pool and the fixed groups.</summary>
    public class EnemyTypeData
    {
        public string EnemyId;
        public string DisplayName;

        /// <summary>Free text for the report ("boss", "tank", ...).</summary>
        public string Role;

        /// <summary>
        /// The type's weight in a shape's threat budget (generated compositions only). A hand-set
        /// estimate of how much it adds to an encounter, in "standard enemy = 2" units.
        /// </summary>
        public double Threat;

        /// <summary>Base stats at max level; scaled by the file's growth curve like a beast's.</summary>
        public StatBlock BaseStats;

        public EnemySkillData[] Skills = new EnemySkillData[0];

        /// <summary>
        /// A <see cref="CombatStance"/> name; missing or empty = <c>Vanguard</c>. A <c>Ranged</c>
        /// type needs a SingleTarget skill with Range &gt;= 2, since a Ranged unit never walks
        /// into melee.
        /// </summary>
        public string Stance;

        /// <summary>
        /// <see cref="BattleUnit.StatusResist"/>: percent (0-100) knocked off the chance of every
        /// hostile non-damage effect (debuff, status, knockback) aimed at this enemy. Missing = 0.
        /// </summary>
        public int StatusResist;

        [System.Text.Json.Serialization.JsonIgnore]
        public CombatStance ParsedStance;
    }

    /// <summary>A fixed encounter's group: an enemy type, how many, and their elements.</summary>
    public class EnemyGroupData : EnemyTypeData
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
        public EnemyGroupData[] Groups = new EnemyGroupData[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public ArenaSize ParsedArena;
    }

    /// <summary>An encounter shape: which types may appear, how many, and the threat budget.</summary>
    public class ShapeData
    {
        public string ShapeId;
        public string DisplayName;
        public string Description;

        /// <summary>An <see cref="ArenaSize"/> name.</summary>
        public string Arena;

        /// <summary>[min, max] total threat a composition must land in (inclusive).</summary>
        public double[] ThreatBudget = new double[0];

        /// <summary>A composition needs at least this many distinct enemy types (the "mix" rule).</summary>
        public int MinDistinctTypes = 1;

        /// <summary>Alternative recipes; the generator picks one per draw, weighted by <see cref="ShapeVariantData.Weight"/>.</summary>
        public ShapeVariantData[] Variants = new ShapeVariantData[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public ArenaSize ParsedArena;
    }

    public class ShapeVariantData
    {
        public string Label;
        public int Weight = 1;
        public ShapeSlotData[] Slots = new ShapeSlotData[0];
    }

    /// <summary>Draw a count in [Min, Max], then each unit's type uniformly (with replacement) from Types.</summary>
    public class ShapeSlotData
    {
        public string[] Types = new string[0];
        public int Min;
        public int Max;
    }

    public class EnemySkillData
    {
        public string SkillId;

        /// <summary>A <see cref="DamageCategory"/> name.</summary>
        public string Category;

        /// <summary><c>SingleTarget</c> or <c>AreaBurst</c>.</summary>
        public string Shape;

        public float Power;
        public int Range;
        public int Cooldown;

        /// <summary>
        /// A <see cref="SkillTargetingCriterion"/> name: <c>Distance</c> (the default when missing),
        /// <c>Stat</c> or <c>CurrentHp</c>. <c>Random</c> is rejected so battles never consume the
        /// rng for targeting.
        /// </summary>
        public string Targeting;

        /// <summary>A <see cref="SkillTargetingOrder"/> name; missing = <c>Lowest</c>.</summary>
        public string TargetingOrder;

        /// <summary>
        /// A <see cref="StatType"/> name, read only when <see cref="Targeting"/> is <c>Stat</c>;
        /// missing = <c>HP</c>. <c>Stat</c> compares the stat block (maximum HP); use
        /// <c>CurrentHp</c> to go for whoever has the least HP left.
        /// </summary>
        public string TargetingStat;

        /// <summary><see cref="SkillEffect.HitCount"/> of the skill's damage effect; missing = 1.</summary>
        public int HitCount = 1;

        /// <summary><see cref="SkillEffect.ExecuteBonusPercent"/> of the skill's damage effect; missing = 0.</summary>
        public int ExecuteBonusPercent;

        /// <summary>
        /// <see cref="SkillSO.InitialCooldown"/>; missing = -1, the ordinary cooldown. 0 fires on the
        /// unit's first turn.
        /// </summary>
        public int InitialCooldown = SkillSO.UseCooldownAsInitial;

        /// <summary><see cref="SkillSO.MaxUsesPerBattle"/>; missing = 0, unlimited.</summary>
        public int MaxUsesPerBattle;

        /// <summary>
        /// Further effects applied after the damage effect, in order (buffs, debuffs, statuses).
        /// Missing = none. Each lands on the skill's own targets.
        /// </summary>
        public EnemyEffectData[] Effects = new EnemyEffectData[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public DamageCategory ParsedCategory;

        [System.Text.Json.Serialization.JsonIgnore]
        public SkillTargetShape ParsedShape;

        [System.Text.Json.Serialization.JsonIgnore]
        public SkillTargetingCriterion ParsedTargeting = SkillTargetingCriterion.Distance;

        [System.Text.Json.Serialization.JsonIgnore]
        public SkillTargetingOrder ParsedTargetingOrder = SkillTargetingOrder.Lowest;

        [System.Text.Json.Serialization.JsonIgnore]
        public StatType ParsedTargetingStat = StatType.HP;
    }

    /// <summary>
    /// One extra <see cref="SkillEffect"/> on an enemy skill, after its damage effect. Names are
    /// enum names; every field but <see cref="Type"/> is optional and defaults as the runtime field
    /// does.
    /// </summary>
    public class EnemyEffectData
    {
        /// <summary>A <see cref="SkillEffectType"/> name: <c>Damage</c>, <c>Heal</c>, <c>BuffStat</c>, <c>DebuffStat</c> or <c>ApplyStatus</c>.</summary>
        public string Type;

        /// <summary>A <see cref="StatusType"/> name, read by <c>ApplyStatus</c>.</summary>
        public string Status;

        /// <summary>A <see cref="StatType"/> name, read by <c>BuffStat</c> / <c>DebuffStat</c>; missing = Attack.</summary>
        public string Stat;

        public float Magnitude;
        public int DurationTurns;
        public int Chance = SkillEffect.AlwaysChance;
        public int MaxStacks = 1;
        public bool IsPercent;
        public int HitCount = 1;
        public int ExecuteBonusPercent;

        [System.Text.Json.Serialization.JsonIgnore]
        public SkillEffect Parsed;
    }

    /// <summary>Which encounters a run fights (<c>--encounter-set</c>).</summary>
    public enum EncounterSet
    {
        /// <summary>Compositions drawn by <see cref="EncounterGenerator"/> from the type pool and shapes.</summary>
        Generated = 0,

        /// <summary>The hand-authored <c>FixedEncounters</c>.</summary>
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

        /// <summary>The type's <see cref="EnemyTypeData.StatusResist"/>, handed to the unit.</summary>
        public int StatusResist;

        public CreatureSpeciesSO Species;
        public SkillSO[] ElementalKit;
        public SkillSO[] NeutralKit;

        public SkillSO[] KitFor(KitMode mode)
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

        /// <summary>How the generator assigned elements (<see cref="ElementScheme"/> name); "authored" for fixed encounters.</summary>
        public string ElementScheme = "authored";

        /// <summary>Summed type threat (generated compositions).</summary>
        public double Threat;

        /// <summary>The authored groups of a fixed encounter; null for a generated composition.</summary>
        public EncounterData FixedData;
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

        /// <summary>The shape's definition; null for a fixed encounter.</summary>
        public ShapeData Data;

        public List<Encounter> Compositions = new List<Encounter>();
    }

    /// <summary>Everything the PvE run fights, plus what the report needs to describe it.</summary>
    public class EncounterCatalog
    {
        public EncounterSet Set;
        public List<EncounterShape> Shapes = new List<EncounterShape>();

        /// <summary>The type pool (generated set), in file order.</summary>
        public List<EnemyTypeData> Types = new List<EnemyTypeData>();

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
    /// Reads <c>Tooling/BalanceSim/encounters.json</c> with System.Text.Json, validates it, and
    /// builds the run's encounters: the generated compositions (default) or the fixed encounters.
    /// Every enemy becomes an in-memory <see cref="CreatureSpeciesSO"/> on the roster's growth
    /// curve, so enemies go through the same <see cref="BattleUnitFactory.CreateBeast"/> stat
    /// assembly as the beasts. None of this is game content.
    /// </summary>
    public static class EncounterLoader
    {
        public const string RepoRelativePath = "Tooling/BalanceSim/encounters.json";
        public const int SchemaVersion = 2;

        public static string ResolvePath(string explicitPath)
        {
            return RosterLoader.ResolveFile(explicitPath, RepoRelativePath);
        }

        public static EncounterCatalog Load(string path, IReadOnlyDictionary<string, GrowthRateCurve> curves, SimOptions options, List<string> errors)
        {
            EncounterFileData file;
            try
            {
                JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
                file = JsonSerializer.Deserialize<EncounterFileData>(File.ReadAllText(path), json);
            }
            catch (Exception exception)
            {
                errors.Add("Could not read '" + path + "': " + exception.Message);
                return null;
            }

            if (file == null)
            {
                errors.Add("Empty encounters file.");
                return null;
            }

            if (file.SchemaVersion != SchemaVersion)
            {
                errors.Add("SchemaVersion " + file.SchemaVersion + " is not the supported " + SchemaVersion + ".");
                return null;
            }

            if (string.IsNullOrEmpty(file.GrowthCurveId) || !curves.TryGetValue(file.GrowthCurveId, out GrowthRateCurve curve))
            {
                errors.Add("GrowthCurveId '" + file.GrowthCurveId + "' is not a growth curve in beast-roster.json.");
                return null;
            }

            // Both sets are validated whichever one runs, so a broken half of the file is never silent.
            Dictionary<string, EnemyTypeData> types = new Dictionary<string, EnemyTypeData>(StringComparer.Ordinal);
            foreach (EnemyTypeData type in file.EnemyTypes ?? new EnemyTypeData[0])
            {
                string where = "Enemy type '" + type.EnemyId + "'";
                if (string.IsNullOrEmpty(type.EnemyId) || types.ContainsKey(type.EnemyId))
                {
                    errors.Add(where + ": missing or duplicate EnemyId.");
                    continue;
                }

                if (!(type.Threat > 0.0))
                {
                    errors.Add(where + ": Threat must be > 0.");
                }

                ValidateType(type, where, errors);
                types[type.EnemyId] = type;
            }

            HashSet<string> shapeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeData shape in file.Shapes ?? new ShapeData[0])
            {
                ValidateShape(shape, types, shapeIds, errors);
            }

            HashSet<string> fixedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EncounterData encounter in file.FixedEncounters ?? new EncounterData[0])
            {
                ValidateFixed(encounter, fixedIds, errors);
            }

            if (options.EncounterSet == EncounterSet.Generated && shapeIds.Count == 0)
            {
                errors.Add("No Shapes defined for the generated encounter set.");
            }

            if (options.EncounterSet == EncounterSet.Fixed && fixedIds.Count == 0)
            {
                errors.Add("No FixedEncounters defined.");
            }

            if (errors.Count > 0)
            {
                return null;
            }

            HashSet<string> known = options.EncounterSet == EncounterSet.Generated ? shapeIds : fixedIds;
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
                catalog.Types.AddRange(file.EnemyTypes);

                // Every shape is generated, filter or not, so a shape's compositions never depend on
                // which other shapes the run happens to include.
                List<EncounterShape> all = EncounterGenerator.Generate(file.EnemyTypes, file.Shapes, curve, options, errors);
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
            }
            else
            {
                foreach (EncounterData data in file.FixedEncounters)
                {
                    if (options.EncounterFilter != null && !options.EncounterFilter.Contains(data.EncounterId))
                    {
                        continue;
                    }

                    Encounter encounter = BuildFixed(data, curve, options);
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

        /// <summary>Stats, stance and kit rules shared by the type pool and the fixed groups.</summary>
        private static void ValidateType(EnemyTypeData type, string where, List<string> errors)
        {
            // MoveRange >= 1: BattleTurnExecutor's partial approach lets any mobile unit close any
            // distance over several turns, but a unit with move 0 never leaves its deployment
            // tile, and a whole encounter of them against a team that cannot reach them either is
            // a stalemate.
            if (type.BaseStats.Hp < 1 || type.BaseStats.MoveRange < 1)
            {
                errors.Add(where + ": BaseStats needs Hp >= 1 and MoveRange >= 1.");
            }

            // Same structural rule the roster validator applies: a crit chance is a percent.
            if (type.BaseStats.CritChance < BeastRosterValidator.MinCritChance || type.BaseStats.CritChance > BeastRosterValidator.MaxCritChance)
            {
                errors.Add(where + ": BaseStats.CritChance must be between " + BeastRosterValidator.MinCritChance + " and " +
                           BeastRosterValidator.MaxCritChance + ".");
            }

            if (!BeastRosterValidator.TryParseStance(type.Stance, out type.ParsedStance))
            {
                errors.Add(where + ": Stance '" + type.Stance + "' is not Vanguard, Ranged or Skirmisher.");
            }

            if (type.StatusResist < 0 || type.StatusResist > 100)
            {
                errors.Add(where + ": StatusResist must be between 0 and 100.");
            }

            if (type.Skills == null || type.Skills.Length == 0)
            {
                errors.Add(where + ": no skills.");
                return;
            }

            bool hasSingleTarget = false;
            bool hasReach = false;
            foreach (EnemySkillData skill in type.Skills)
            {
                string skillWhere = where + " skill '" + skill.SkillId + "'";
                if (string.IsNullOrEmpty(skill.SkillId) || skill.Power <= 0f || skill.Range < 0 || skill.Cooldown < 0)
                {
                    errors.Add(skillWhere + ": needs a SkillId, Power > 0, Range >= 0 and Cooldown >= 0.");
                }

                if (!Enum.TryParse(skill.Category, false, out skill.ParsedCategory) || !Enum.IsDefined(typeof(DamageCategory), skill.ParsedCategory))
                {
                    errors.Add(skillWhere + ": Category '" + skill.Category + "' is not Physical or Special.");
                }

                ParseTargeting(skill, skillWhere, errors);
                ParseAdvanced(skill, skillWhere, errors);

                if (skill.Shape == "SingleTarget")
                {
                    skill.ParsedShape = SkillTargetShape.SingleTarget;
                    hasSingleTarget = true;
                    hasReach |= skill.Range >= 2;
                }
                else if (skill.Shape == "AreaBurst")
                {
                    skill.ParsedShape = SkillTargetShape.AreaBurst;
                }
                else
                {
                    errors.Add(skillWhere + ": Shape '" + skill.Shape + "' must be SingleTarget or AreaBurst.");
                }
            }

            // SingleTarget is the only fixture shape BattleTurnExecutor walks a unit for (it
            // approaches, partially if need be, only to bring a picked focus into range), so a
            // type without one would never leave its deployment tiles.
            if (!hasSingleTarget)
            {
                errors.Add(where + ": needs at least one SingleTarget skill (the only shape that moves the unit).");
            }

            // A Ranged unit never walks in for a Range <= 1 skill (BattleTurnExecutor), so a Ranged
            // type with nothing longer would never leave its deployment tiles.
            if (hasSingleTarget && type.ParsedStance == CombatStance.Ranged && !hasReach)
            {
                errors.Add(where + ": a Ranged enemy needs a SingleTarget skill with Range >= 2 (Ranged units never walk into melee).");
            }
        }

        private static void ValidateShape(ShapeData shape, Dictionary<string, EnemyTypeData> types, HashSet<string> ids, List<string> errors)
        {
            string where = "Shape '" + shape.ShapeId + "'";
            if (string.IsNullOrEmpty(shape.ShapeId) || !ids.Add(shape.ShapeId))
            {
                errors.Add(where + ": missing or duplicate ShapeId.");
                return;
            }

            if (!TryParseArena(shape.Arena, out shape.ParsedArena))
            {
                errors.Add(where + ": Arena '" + shape.Arena + "' is not Small, Medium or Large.");
                return;
            }

            if (shape.ThreatBudget == null || shape.ThreatBudget.Length != 2 || shape.ThreatBudget[0] > shape.ThreatBudget[1] || shape.ThreatBudget[1] <= 0.0)
            {
                errors.Add(where + ": ThreatBudget must be [min, max] with min <= max and max > 0.");
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

            int zoneSize = new HexGrid(shape.ParsedArena).GetDeploymentZone(BattleTeam.Enemy).Count;
            foreach (ShapeVariantData variant in shape.Variants)
            {
                string variantWhere = where + " variant '" + variant.Label + "'";
                if (variant.Weight < 1 || variant.Slots == null || variant.Slots.Length == 0)
                {
                    errors.Add(variantWhere + ": needs Weight >= 1 and at least one slot.");
                    continue;
                }

                int max = 0;
                foreach (ShapeSlotData slot in variant.Slots)
                {
                    if (slot.Types == null || slot.Types.Length == 0 || slot.Min < 0 || slot.Max < slot.Min)
                    {
                        errors.Add(variantWhere + ": each slot needs Types and 0 <= Min <= Max.");
                        continue;
                    }

                    max += slot.Max;
                    foreach (string type in slot.Types)
                    {
                        if (!types.ContainsKey(type))
                        {
                            errors.Add(variantWhere + ": unknown enemy type '" + type + "'.");
                        }
                    }
                }

                if (max > zoneSize || max > 99)
                {
                    errors.Add(variantWhere + ": up to " + max + " enemies do not fit the " + shape.ParsedArena + " enemy deployment zone (" + zoneSize +
                               " tiles) or exceed 99.");
                }
            }
        }

        private static void ValidateFixed(EncounterData encounter, HashSet<string> ids, List<string> errors)
        {
            string where = "Fixed encounter '" + encounter.EncounterId + "'";
            if (string.IsNullOrEmpty(encounter.EncounterId) || !ids.Add(encounter.EncounterId))
            {
                errors.Add(where + ": missing or duplicate EncounterId.");
                return;
            }

            if (!TryParseArena(encounter.Arena, out encounter.ParsedArena))
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

            foreach (EnemyGroupData group in encounter.Groups)
            {
                string groupWhere = where + " group '" + group.EnemyId + "'";
                if (string.IsNullOrEmpty(group.EnemyId) || group.Count < 1)
                {
                    errors.Add(groupWhere + ": needs an EnemyId and a Count of at least 1.");
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
                ValidateType(group, groupWhere, errors);
            }

            if (total > zoneSize)
            {
                errors.Add(where + ": " + total + " enemies do not fit the " + encounter.ParsedArena + " enemy deployment zone (" + zoneSize + " tiles).");
            }

            if (total > 99)
            {
                errors.Add(where + ": more than 99 enemies (unit ids are two digits).");
            }
        }

        private static bool TryParseArena(string text, out ArenaSize arena)
        {
            return Enum.TryParse(text, false, out arena) && Enum.IsDefined(typeof(ArenaSize), arena);
        }

        /// <summary>
        /// Parses a skill's optional targeting fields. Missing values keep the original fixture
        /// rule, nearest first (<c>Distance</c> / <c>Lowest</c>). <c>Random</c> is refused: every
        /// fixture battle must stay independent of the rng's targeting draws.
        /// </summary>
        private static void ParseTargeting(EnemySkillData skill, string skillWhere, List<string> errors)
        {
            if (!string.IsNullOrEmpty(skill.Targeting) &&
                (!Enum.IsDefined(typeof(SkillTargetingCriterion), skill.Targeting) || !Enum.TryParse(skill.Targeting, false, out skill.ParsedTargeting) ||
                 skill.ParsedTargeting == SkillTargetingCriterion.Random))
            {
                errors.Add(skillWhere + ": Targeting '" + skill.Targeting + "' must be Distance, Stat, CurrentHp or HpFraction.");
            }

            if (!string.IsNullOrEmpty(skill.TargetingOrder) &&
                (!Enum.IsDefined(typeof(SkillTargetingOrder), skill.TargetingOrder) || !Enum.TryParse(skill.TargetingOrder, false, out skill.ParsedTargetingOrder)))
            {
                errors.Add(skillWhere + ": TargetingOrder '" + skill.TargetingOrder + "' must be Lowest or Highest.");
            }

            if (!string.IsNullOrEmpty(skill.TargetingStat) &&
                (!Enum.IsDefined(typeof(StatType), skill.TargetingStat) || !Enum.TryParse(skill.TargetingStat, false, out skill.ParsedTargetingStat)))
            {
                errors.Add(skillWhere + ": TargetingStat '" + skill.TargetingStat + "' is not a StatType name.");
            }
        }

        /// <summary>
        /// Validates a skill's optional advanced fields (hit count, execute bonus, initial cooldown,
        /// use limit) and parses its extra <see cref="EnemySkillData.Effects"/> into runtime
        /// <see cref="SkillEffect"/>s. All optional; missing values reproduce the plain fixture skill.
        /// </summary>
        private static void ParseAdvanced(EnemySkillData skill, string skillWhere, List<string> errors)
        {
            if (skill.HitCount < 1 || skill.ExecuteBonusPercent < 0 || skill.MaxUsesPerBattle < 0 || skill.InitialCooldown < SkillSO.UseCooldownAsInitial)
            {
                errors.Add(skillWhere + ": needs HitCount >= 1, ExecuteBonusPercent >= 0, MaxUsesPerBattle >= 0 and InitialCooldown >= -1.");
            }

            foreach (EnemyEffectData data in skill.Effects ?? new EnemyEffectData[0])
            {
                string effectWhere = skillWhere + " effect '" + data.Type + "'";
                SkillEffect effect = new SkillEffect
                {
                    Magnitude = data.Magnitude,
                    DurationTurns = data.DurationTurns,
                    Chance = data.Chance,
                    MaxStacks = data.MaxStacks,
                    IsPercent = data.IsPercent,
                    HitCount = data.HitCount,
                    ExecuteBonusPercent = data.ExecuteBonusPercent
                };

                if (string.IsNullOrEmpty(data.Type) || !Enum.IsDefined(typeof(SkillEffectType), data.Type) ||
                    !Enum.TryParse(data.Type, false, out effect.EffectType))
                {
                    errors.Add(effectWhere + ": Type must be a SkillEffectType name.");
                }

                if (!string.IsNullOrEmpty(data.Status) &&
                    (!Enum.IsDefined(typeof(StatusType), data.Status) || !Enum.TryParse(data.Status, false, out effect.Status)))
                {
                    errors.Add(effectWhere + ": Status '" + data.Status + "' is not a StatusType name.");
                }

                if (effect.EffectType == SkillEffectType.ApplyStatus && effect.Status == StatusType.None)
                {
                    errors.Add(effectWhere + ": ApplyStatus needs a Status.");
                }

                if (!string.IsNullOrEmpty(data.Stat) &&
                    (!Enum.IsDefined(typeof(StatType), data.Stat) || !Enum.TryParse(data.Stat, false, out effect.AffectedStat)))
                {
                    errors.Add(effectWhere + ": Stat '" + data.Stat + "' is not a StatType name.");
                }

                if (data.Chance < 1 || data.Chance > SkillEffect.AlwaysChance || data.MaxStacks < 1 || data.HitCount < 1 || data.DurationTurns < 0 ||
                    data.ExecuteBonusPercent < 0)
                {
                    errors.Add(effectWhere + ": needs Chance 1-100, MaxStacks >= 1, HitCount >= 1, DurationTurns >= 0 and ExecuteBonusPercent >= 0.");
                }

                data.Parsed = effect;
            }
        }

        private static Encounter BuildFixed(EncounterData data, GrowthRateCurve curve, SimOptions options)
        {
            Encounter encounter = new Encounter { Id = data.EncounterId, Arena = data.ParsedArena, FixedData = data };
            EnemyFactory factory = new EnemyFactory(curve);
            int unit = 0;

            foreach (EnemyGroupData group in data.Groups)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    unit++;
                    Element element = options.EnemyElementOverride ??
                                      (group.ParsedElements.Length == 0 ? Element.None : group.ParsedElements[i % group.ParsedElements.Length]);
                    encounter.Enemies.Add(factory.Slot(group, element, unit));
                }
            }

            return encounter;
        }
    }

    /// <summary>
    /// Builds enemy slots, sharing one species and one kit per (type, element) so a horde of
    /// identical units does not allocate a skill set per unit. Species and kits are read-only in
    /// battle, so sharing is safe across parallel battles.
    /// </summary>
    public class EnemyFactory
    {
        private readonly GrowthRateCurve _curve;
        private readonly Dictionary<EnemyTypeData, Dictionary<Element, CreatureSpeciesSO>> _species = new Dictionary<EnemyTypeData, Dictionary<Element, CreatureSpeciesSO>>();
        private readonly Dictionary<EnemyTypeData, Dictionary<Element, SkillSO[]>> _kits = new Dictionary<EnemyTypeData, Dictionary<Element, SkillSO[]>>();

        public EnemyFactory(GrowthRateCurve curve)
        {
            _curve = curve;
        }

        public EnemySlot Slot(EnemyTypeData type, Element element, int unitNumber)
        {
            if (!_species.TryGetValue(type, out Dictionary<Element, CreatureSpeciesSO> byElement))
            {
                byElement = new Dictionary<Element, CreatureSpeciesSO>();
                _species[type] = byElement;
                _kits[type] = new Dictionary<Element, SkillSO[]>();
            }

            if (!byElement.TryGetValue(element, out CreatureSpeciesSO species))
            {
                species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
                species.name = type.EnemyId;
                species.SpeciesId = type.EnemyId;
                species.DisplayName = type.DisplayName;
                species.BaseStats = type.BaseStats;
                species.GrowthRate = _curve;
                species.Elements = element == Element.None ? new Element[0] : new[] { element };
                species.Stance = type.ParsedStance;
                byElement[element] = species;
                _kits[type][element] = Kit.BuildEnemyKit(type.Skills, element);
            }

            if (!_kits[type].TryGetValue(Element.None, out SkillSO[] neutral))
            {
                neutral = Kit.BuildEnemyKit(type.Skills, Element.None);
                _kits[type][Element.None] = neutral;
            }

            return new EnemySlot
            {
                UnitId = "e" + unitNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                TypeId = type.EnemyId,
                GroupDisplayName = type.DisplayName,
                Element = element,
                Threat = type.Threat,
                StatusResist = type.StatusResist,
                Species = species,
                ElementalKit = _kits[type][element],
                NeutralKit = neutral
            };
        }
    }
}
