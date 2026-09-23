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

        public EncounterData[] Encounters = new EncounterData[0];
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

    public class EnemyGroupData
    {
        public string EnemyId;
        public string DisplayName;
        public int Count;

        /// <summary>Element per unit, cycled over the group (unit i gets Elements[i % length]); empty = None.</summary>
        public string[] Elements = new string[0];

        /// <summary>Base stats at max level; scaled by the file's growth curve like a beast's.</summary>
        public StatBlock BaseStats;

        public EnemySkillData[] Skills = new EnemySkillData[0];

        /// <summary>
        /// A <see cref="CombatStance"/> name; missing or empty = <c>Vanguard</c>. A <c>Ranged</c>
        /// group needs a SingleTarget skill with Range &gt;= 2, since a Ranged unit never walks
        /// into melee.
        /// </summary>
        public string Stance;

        [System.Text.Json.Serialization.JsonIgnore]
        public Element[] ParsedElements = new Element[0];

        [System.Text.Json.Serialization.JsonIgnore]
        public CombatStance ParsedStance;
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
        /// A <see cref="SkillTargetingCriterion"/> name: <c>Distance</c> (the default when missing)
        /// or <c>Stat</c>. <c>Random</c> is rejected so battles never consume the rng for targeting.
        /// </summary>
        public string Targeting;

        /// <summary>A <see cref="SkillTargetingOrder"/> name; missing = <c>Lowest</c>.</summary>
        public string TargetingOrder;

        /// <summary>
        /// A <see cref="StatType"/> name, read only when <see cref="Targeting"/> is <c>Stat</c>;
        /// missing = <c>HP</c>. The resolver compares the stat block (maximum HP), not current HP.
        /// </summary>
        public string TargetingStat;

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

    /// <summary>One enemy on the board: its id, the species it is built from, and its kit per mode.</summary>
    public class EnemySlot
    {
        public string UnitId;
        public string GroupDisplayName;
        public Element Element;
        public CreatureSpeciesSO Species;
        public SkillSO[] ElementalKit;
        public SkillSO[] NeutralKit;

        public SkillSO[] KitFor(KitMode mode)
        {
            return mode == KitMode.Elemental ? ElementalKit : NeutralKit;
        }
    }

    /// <summary>A loaded, validated encounter, ready to field.</summary>
    public class Encounter
    {
        public EncounterData Data;
        public List<EnemySlot> Enemies = new List<EnemySlot>();

        public string Id
        {
            get { return Data.EncounterId; }
        }
    }

    /// <summary>
    /// Reads <c>Tooling/BalanceSim/encounters.json</c> with System.Text.Json, validates it, and turns
    /// each enemy into an in-memory <see cref="CreatureSpeciesSO"/> on the roster's growth curve, so
    /// enemies go through the same <see cref="BattleUnitFactory.CreateBeast"/> stat assembly as the
    /// beasts. None of this is game content.
    /// </summary>
    public static class EncounterLoader
    {
        public const string RepoRelativePath = "Tooling/BalanceSim/encounters.json";

        public static string ResolvePath(string explicitPath)
        {
            return RosterLoader.ResolveFile(explicitPath, RepoRelativePath);
        }

        public static List<Encounter> Load(string path, IReadOnlyDictionary<string, GrowthRateCurve> curves, SimOptions options, List<string> errors)
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

            if (file == null || file.Encounters == null || file.Encounters.Length == 0)
            {
                errors.Add("No encounters defined.");
                return null;
            }

            if (string.IsNullOrEmpty(file.GrowthCurveId) || !curves.TryGetValue(file.GrowthCurveId, out GrowthRateCurve curve))
            {
                errors.Add("GrowthCurveId '" + file.GrowthCurveId + "' is not a growth curve in beast-roster.json.");
                return null;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EncounterData encounter in file.Encounters)
            {
                Validate(encounter, ids, errors);
            }

            if (errors.Count > 0)
            {
                return null;
            }

            if (options.EncounterFilter != null)
            {
                foreach (string wanted in options.EncounterFilter)
                {
                    if (!ids.Contains(wanted))
                    {
                        errors.Add("--encounters: unknown encounter id '" + wanted + "'. Known: " + string.Join(", ", ids) + ".");
                    }
                }

                if (errors.Count > 0)
                {
                    return null;
                }
            }

            List<Encounter> result = new List<Encounter>();
            foreach (EncounterData data in file.Encounters)
            {
                if (options.EncounterFilter != null && !options.EncounterFilter.Contains(data.EncounterId))
                {
                    continue;
                }

                result.Add(Build(data, curve, options));
            }

            return result;
        }

        private static void Validate(EncounterData encounter, HashSet<string> ids, List<string> errors)
        {
            string where = "Encounter '" + encounter.EncounterId + "'";
            if (string.IsNullOrEmpty(encounter.EncounterId) || !ids.Add(encounter.EncounterId))
            {
                errors.Add(where + ": missing or duplicate EncounterId.");
                return;
            }

            if (!Enum.TryParse(encounter.Arena, false, out encounter.ParsedArena) || !Enum.IsDefined(typeof(ArenaSize), encounter.ParsedArena))
            {
                errors.Add(where + ": Arena '" + encounter.Arena + "' is not Small, Medium or Large.");
                return;
            }

            if (encounter.Groups == null || encounter.Groups.Length == 0)
            {
                errors.Add(where + ": no enemy groups.");
                return;
            }

            HexGrid grid = new HexGrid(encounter.ParsedArena);
            int zoneSize = grid.GetDeploymentZone(BattleTeam.Enemy).Count;
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

                // MoveRange >= 1: BattleTurnExecutor's partial approach lets any mobile unit close any
                // distance over several turns, but a unit with move 0 never leaves its deployment
                // tile, and a whole encounter of them against a team that cannot reach them either is
                // a round-cap stalemate.
                if (group.BaseStats.Hp < 1 || group.BaseStats.MoveRange < 1)
                {
                    errors.Add(groupWhere + ": BaseStats needs Hp >= 1 and MoveRange >= 1.");
                }

                if (group.Skills == null || group.Skills.Length == 0)
                {
                    errors.Add(groupWhere + ": no skills.");
                    continue;
                }

                if (!BeastRosterValidator.TryParseStance(group.Stance, out group.ParsedStance))
                {
                    errors.Add(groupWhere + ": Stance '" + group.Stance + "' is not Vanguard, Ranged or Skirmisher.");
                }

                bool hasSingleTarget = false;
                bool hasReach = false;
                foreach (EnemySkillData skill in group.Skills)
                {
                    string skillWhere = groupWhere + " skill '" + skill.SkillId + "'";
                    if (string.IsNullOrEmpty(skill.SkillId) || skill.Power <= 0f || skill.Range < 0 || skill.Cooldown < 0)
                    {
                        errors.Add(skillWhere + ": needs a SkillId, Power > 0, Range >= 0 and Cooldown >= 0.");
                    }

                    if (!Enum.TryParse(skill.Category, false, out skill.ParsedCategory) || !Enum.IsDefined(typeof(DamageCategory), skill.ParsedCategory))
                    {
                        errors.Add(skillWhere + ": Category '" + skill.Category + "' is not Physical or Special.");
                    }

                    ParseTargeting(skill, skillWhere, errors);

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
                // group without one would never leave its deployment tiles.
                if (!hasSingleTarget)
                {
                    errors.Add(groupWhere + ": needs at least one SingleTarget skill (the only shape that moves the unit).");
                }

                // A Ranged unit never walks in for a Range <= 1 skill (BattleTurnExecutor), so a Ranged
                // group with nothing longer would never leave its deployment tiles.
                if (hasSingleTarget && group.ParsedStance == CombatStance.Ranged && !hasReach)
                {
                    errors.Add(groupWhere + ": a Ranged group needs a SingleTarget skill with Range >= 2 (Ranged units never walk into melee).");
                }
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
                errors.Add(skillWhere + ": Targeting '" + skill.Targeting + "' must be Distance or Stat.");
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

        private static Encounter Build(EncounterData data, GrowthRateCurve curve, SimOptions options)
        {
            Encounter encounter = new Encounter { Data = data };
            int unit = 0;

            foreach (EnemyGroupData group in data.Groups)
            {
                SkillSO[] neutralKit = Kit.BuildEnemyKit(group.Skills, Element.None);
                Dictionary<Element, CreatureSpeciesSO> speciesByElement = new Dictionary<Element, CreatureSpeciesSO>();
                Dictionary<Element, SkillSO[]> kitByElement = new Dictionary<Element, SkillSO[]>();

                for (int i = 0; i < group.Count; i++)
                {
                    unit++;
                    Element element = options.EnemyElementOverride ??
                                      (group.ParsedElements.Length == 0 ? Element.None : group.ParsedElements[i % group.ParsedElements.Length]);

                    if (!speciesByElement.TryGetValue(element, out CreatureSpeciesSO species))
                    {
                        species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
                        species.name = group.EnemyId;
                        species.SpeciesId = group.EnemyId;
                        species.DisplayName = group.DisplayName;
                        species.BaseStats = group.BaseStats;
                        species.GrowthRate = curve;
                        species.Elements = element == Element.None ? new Element[0] : new[] { element };
                        species.Stance = group.ParsedStance;
                        speciesByElement[element] = species;
                        kitByElement[element] = Kit.BuildEnemyKit(group.Skills, element);
                    }

                    encounter.Enemies.Add(new EnemySlot
                    {
                        UnitId = "e" + unit.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                        GroupDisplayName = group.DisplayName,
                        Element = element,
                        Species = species,
                        ElementalKit = kitByElement[element],
                        NeutralKit = neutralKit
                    });
                }
            }

            return encounter;
        }
    }
}
