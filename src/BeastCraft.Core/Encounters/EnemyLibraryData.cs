using System;
using BeastCraft.Creatures;
using BeastCraft.Skills;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The plain-data shape of <c>data/Encounters/enemy-library.json</c>: every PvE enemy type the
    /// game fields — stats, stance, size and kit. Enemies are not roster beasts (they are never
    /// owned, levelled or saved); <see cref="EnemyCatalog"/> turns each (enemy, element) pair into
    /// an in-memory <c>CreatureSpeciesSO</c> and skill kit on demand, so an enemy goes through the
    /// same stat assembly (<c>BattleUnitFactory.CreateBeast</c>) as a beast.
    /// <para>
    /// Like the roster and skill-library DTOs these are plain serializable classes with public fields
    /// and no Unity-object references, read by <c>JsonUtility</c> inside Unity (the Editor importer)
    /// and by <c>System.Text.Json</c> with <c>IncludeFields = true</c> outside it (the balance
    /// simulator, the EditMode test runner). JSON keys are the field names exactly; enums are member
    /// names, parsed case-sensitively by <see cref="EnemyLibraryValidator"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class EnemyLibraryData
    {
        /// <summary>Path of the file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Encounters/enemy-library.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>
        /// The <c>beast-roster.json</c> growth curve (by <c>CurveId</c>) every enemy's stats scale
        /// with, exactly as a beast's do.
        /// </summary>
        public string GrowthCurveId;

        /// <summary>The enemy types, in file order (the generator's tie-break order within a stance).</summary>
        public EnemyData[] Enemies = new EnemyData[0];
    }

    /// <summary>One enemy type.</summary>
    [Serializable]
    public class EnemyData
    {
        /// <summary>
        /// Stable lowercase snake_case id. Must not collide with a roster <c>SpeciesId</c>: the
        /// enemy's in-memory species carries it as its <c>SpeciesId</c>.
        /// </summary>
        public string EnemyId;

        public string DisplayName;

        public string Description;

        /// <summary>Free text for tooling and design ("boss", "melee tank", ...). Not read by battles.</summary>
        public string Role;

        /// <summary>
        /// The type's weight in a shape's threat budget: a hand-set estimate, in "standard enemy = 2"
        /// units, of how much it adds to an encounter. Must be above 0.
        /// </summary>
        public double Threat;

        /// <summary>A <c>CombatStance</c> name. Missing: <c>Vanguard</c>.</summary>
        public string Stance;

        /// <summary>
        /// <c>BattleUnit.StatusResist</c>: percent (0-100) knocked off the chance of every hostile
        /// non-damage effect aimed at the enemy.
        /// </summary>
        public int StatusResist;

        /// <summary>
        /// A <c>UnitFootprint</c> name: <c>Single</c> (missing), <c>Triangle</c> (3 tiles) or
        /// <c>Hex7</c> (7 tiles; never fits a Small arena). Ranges to and from a large unit count
        /// from its nearest tile.
        /// </summary>
        public string Footprint;

        /// <summary>Base stats at max level, scaled by the file's growth curve (CritChance and MoveRange are exempt).</summary>
        public StatBlock BaseStats;

        /// <summary>
        /// The kit, in fire-priority order. Same shape as the skill library's skills, with two
        /// differences: <see cref="SkillData.Element"/> must be empty (every skill takes the unit's
        /// element, so one type serves every element), and ids are local to the enemy (they may
        /// repeat a beast skill's id; they are never looked up globally).
        /// </summary>
        public SkillData[] Skills = new SkillData[0];

        /// <summary>
        /// Presentation only: the art key the viewer draws this enemy with (a sprite's
        /// <c>ArtKey</c> in the art manifest), e.g. <c>"enemy/giant"</c>. Optional, but every shipped
        /// enemy sets one; an enemy without art of its own points at a manifest alias entry (another
        /// sprite plus a tint). Never read by battles.
        /// </summary>
        public string ArtKey;
    }
}
