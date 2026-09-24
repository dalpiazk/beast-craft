using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The enemy library as battle-ready objects: for an (enemy, element) pair, an in-memory
    /// <see cref="CreatureSpeciesSO"/> (<see cref="Species"/>) and skill kit (<see cref="Kit"/>).
    /// Both are built on first request and cached, so every unit of the same type and element shares
    /// one species and one kit (a horde does not allocate a skill set per unit, and anything keyed on
    /// species identity — the balance simulator's calibration — sees one species per pair).
    /// <para>
    /// The species goes through <see cref="BeastRosterBuilder.ApplySpecies"/> (the roster's own field
    /// mapping) with the enemy's element as its only element; each skill goes through
    /// <see cref="SkillLibraryBuilder.ApplySkill"/> (the skill library's) and then takes the element.
    /// Enemies therefore fight with exactly the stat assembly and skill fields beasts do.
    /// </para>
    /// <para>
    /// Species and kits are read-only in battle, so sharing them across battles (and threads) is
    /// safe; building them is not thread-safe. Expects a library that passed
    /// <see cref="EnemyLibraryValidator"/>; an unknown id gives null.
    /// </para>
    /// </summary>
    public sealed class EnemyCatalog
    {
        private readonly Dictionary<string, EnemyData> _enemies = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
        private readonly List<EnemyData> _order = new List<EnemyData>();
        private readonly Dictionary<string, CreatureSpeciesSO[]> _species = new Dictionary<string, CreatureSpeciesSO[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillSO[][]> _kits = new Dictionary<string, SkillSO[][]>(StringComparer.Ordinal);

        private EnemyCatalog(GrowthRateCurve growthRate)
        {
            GrowthRate = growthRate;
        }

        /// <summary>The growth curve every enemy's stats scale with.</summary>
        public GrowthRateCurve GrowthRate { get; }

        /// <summary>Every enemy type, in library order.</summary>
        public IReadOnlyList<EnemyData> Enemies
        {
            get { return _order; }
        }

        /// <summary>
        /// A catalog over <paramref name="library"/>'s enemies, on <paramref name="growthRate"/> (the
        /// curve named by <see cref="EnemyLibraryData.GrowthCurveId"/>, resolved by the caller). Null
        /// entries and entries with an empty id are skipped; when two share an id the first wins.
        /// </summary>
        public static EnemyCatalog Build(EnemyLibraryData library, GrowthRateCurve growthRate)
        {
            return Build(library == null ? null : library.Enemies, growthRate);
        }

        /// <summary>A catalog over a plain list of enemies (see <see cref="Build(EnemyLibraryData, GrowthRateCurve)"/>).</summary>
        public static EnemyCatalog Build(IEnumerable<EnemyData> enemies, GrowthRateCurve growthRate)
        {
            EnemyCatalog catalog = new EnemyCatalog(growthRate);
            if (enemies == null)
            {
                return catalog;
            }

            foreach (EnemyData enemy in enemies)
            {
                if (enemy != null && !string.IsNullOrEmpty(enemy.EnemyId) && !catalog._enemies.ContainsKey(enemy.EnemyId))
                {
                    catalog._enemies.Add(enemy.EnemyId, enemy);
                    catalog._order.Add(enemy);
                }
            }

            return catalog;
        }

        /// <summary>Whether <paramref name="enemyId"/> is an enemy of this catalog.</summary>
        public bool Contains(string enemyId)
        {
            return Get(enemyId) != null;
        }

        /// <summary>The authored enemy with <paramref name="enemyId"/>, or null.</summary>
        public EnemyData Get(string enemyId)
        {
            return !string.IsNullOrEmpty(enemyId) && _enemies.TryGetValue(enemyId, out EnemyData enemy) ? enemy : null;
        }

        /// <summary>The enemy's <see cref="CombatStance"/> (Vanguard for an unknown id).</summary>
        public CombatStance StanceOf(string enemyId)
        {
            EnemyData enemy = Get(enemyId);
            BeastRosterValidator.TryParseStance(enemy == null ? null : enemy.Stance, out CombatStance stance);
            return stance;
        }

        /// <summary>The enemy's <see cref="UnitFootprint"/> (Single for an unknown id).</summary>
        public UnitFootprint FootprintOf(string enemyId)
        {
            EnemyData enemy = Get(enemyId);
            EnemyLibraryValidator.TryParseFootprint(enemy == null ? null : enemy.Footprint, out UnitFootprint footprint);
            return footprint;
        }

        /// <summary>
        /// The species a unit of <paramref name="enemyId"/> in <paramref name="element"/> fights as:
        /// the enemy's stats, stance and footprint on the catalog's growth curve, with
        /// <paramref name="element"/> as its only element (none for <see cref="Element.None"/>). Its
        /// <see cref="CreatureSpeciesSO.SpeciesId"/> is the enemy id. The same instance every call.
        /// Null for an unknown enemy.
        /// </summary>
        public CreatureSpeciesSO Species(string enemyId, Element element)
        {
            EnemyData enemy = Get(enemyId);
            if (enemy == null)
            {
                return null;
            }

            if (!_species.TryGetValue(enemyId, out CreatureSpeciesSO[] byElement))
            {
                byElement = new CreatureSpeciesSO[ElementSlots];
                _species.Add(enemyId, byElement);
            }

            int slot = Slot(element);
            if (byElement[slot] == null)
            {
                SpeciesData data = new SpeciesData
                {
                    SpeciesId = enemy.EnemyId,
                    DisplayName = enemy.DisplayName,
                    Description = enemy.Description,
                    Elements = element == Element.None ? new string[0] : new[] { element.ToString() },
                    BaseStats = enemy.BaseStats,
                    Stance = enemy.Stance,
                    Footprint = enemy.Footprint
                };

                CreatureSpeciesSO species = new CreatureSpeciesSO();
                species.name = enemy.EnemyId;
                BeastRosterBuilder.ApplySpecies(data, species, GrowthRate);
                byElement[slot] = species;
            }

            return byElement[slot];
        }

        /// <summary>
        /// The kit a unit of <paramref name="enemyId"/> in <paramref name="element"/> fights with, in
        /// fire-priority order: every authored skill, with <paramref name="element"/> as its element.
        /// Shared and read-only (the same list every call; never modify it or its skills). Null for
        /// an unknown enemy.
        /// </summary>
        public IReadOnlyList<SkillSO> Kit(string enemyId, Element element)
        {
            EnemyData enemy = Get(enemyId);
            if (enemy == null)
            {
                return null;
            }

            if (!_kits.TryGetValue(enemyId, out SkillSO[][] byElement))
            {
                byElement = new SkillSO[ElementSlots][];
                _kits.Add(enemyId, byElement);
            }

            int slot = Slot(element);
            if (byElement[slot] == null)
            {
                SkillData[] skills = enemy.Skills ?? new SkillData[0];
                List<SkillSO> kit = new List<SkillSO>();
                foreach (SkillData data in skills)
                {
                    if (data == null)
                    {
                        continue;
                    }

                    SkillSO skill = new SkillSO();
                    skill.name = data.SkillId;
                    SkillLibraryBuilder.ApplySkill(data, skill);
                    skill.Element = element;
                    kit.Add(skill);
                }

                byElement[slot] = kit.ToArray();
            }

            return byElement[slot];
        }

        /// <summary>One cache slot per <see cref="Element"/> value, None included.</summary>
        private const int ElementSlots = (int)Element.Dark + 1;

        private static int Slot(Element element)
        {
            int slot = (int)element;
            return slot < 0 || slot >= ElementSlots ? 0 : slot;
        }
    }
}
