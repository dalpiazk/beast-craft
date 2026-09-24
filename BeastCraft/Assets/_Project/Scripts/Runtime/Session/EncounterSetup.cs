using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Session
{
    /// <summary>
    /// One enemy of an <see cref="EncounterSetup"/>, described generically (encounters are not game
    /// content yet): a species at a level, an optional loadout and an optional position.
    /// </summary>
    public class EnemySpec
    {
        /// <summary>The battle unit id. Empty means <c>"enemy" + (index + 1)</c>. Must be unique in the battle.</summary>
        public string UnitId;

        /// <summary>A species id resolved through <see cref="BattleContent.GetSpecies"/>. Required.</summary>
        public string SpeciesId;

        /// <summary>The enemy's level (below 1 reads as 1).</summary>
        public int Level = 1;

        /// <summary>
        /// Skill ids in fire-priority order, each resolved through <see cref="BattleContent.GetSkill"/>
        /// at level 1, tier 0. Null means the species' <c>DefaultLoadout</c>; an empty list means no skills.
        /// </summary>
        public List<string> SkillIds;

        /// <summary>
        /// Where the enemy's anchor stands. Null means auto-placed: packed front-most first into the
        /// enemy deployment zone after every explicitly positioned enemy (<c>DeploymentPacker</c>).
        /// An explicit position must fit the enemy deployment zone with the species' footprint.
        /// </summary>
        public HexCoordinate? Position;

        /// <summary>Percent of hostile non-damage effect chance shrugged off (0-100).</summary>
        public int StatusResist;

        public EnemySpec()
        {
        }

        public EnemySpec(string speciesId, int level)
        {
            SpeciesId = speciesId;
            Level = level;
        }
    }

    /// <summary>
    /// The opposition and the board: the arena, and the enemies either as <see cref="Enemies"/>
    /// specs, as <see cref="PrebuiltEnemies"/> the caller built itself, or both.
    /// </summary>
    public class EncounterSetup
    {
        /// <summary>The board.</summary>
        public ArenaSize Arena = ArenaSize.Medium;

        /// <summary>Enemies the session builds, in order.</summary>
        public List<EnemySpec> Enemies = new List<EnemySpec>();

        /// <summary>
        /// Enemies the caller already built (<see cref="BattleTeam.Enemy"/>, ids unique). Each is
        /// placed at its own <see cref="BattleUnit.Position"/> with its footprint, before any spec
        /// is auto-placed. Added to the roster after the spec enemies.
        /// </summary>
        public List<BattleUnit> PrebuiltEnemies = new List<BattleUnit>();
    }
}
