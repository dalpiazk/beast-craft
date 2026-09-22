using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>Which side of the battle a unit fights for.</summary>
    public enum BattleTeam
    {
        Player = 0,
        Enemy = 1
    }

    /// <summary>
    /// A combatant occupying a tile in an active battle.
    /// <para>
    /// Intentionally minimal: just the identity, allegiance, stats and position that the grid and
    /// the turn manager need to reference. This is NOT the final creature-instance runtime model —
    /// the real one will carry level, current HP, equipped gear, learned skills, status effects and
    /// a link back to its <c>CreatureSpeciesSO</c>, and this type will either grow into it or be
    /// replaced by it.
    /// </para>
    /// </summary>
    public class BattleUnit
    {
        public BattleUnit(string id, BattleTeam team, StatBlock stats, HexCoordinate position)
        {
            Id = id;
            Team = team;
            Stats = stats;
            Position = position;
        }

        /// <summary>
        /// Stable identifier for this combatant, unique within a single battle. This is the key the
        /// grid tracks occupancy by, so it must not change while the unit is on the board.
        /// </summary>
        public string Id { get; }

        /// <summary>The side this unit fights for.</summary>
        public BattleTeam Team { get; }

        /// <summary>
        /// The unit's effective combat stats. Settable because later passes layer gear and
        /// buff/debuff modifiers on top of the base block; no such logic exists yet.
        /// </summary>
        public StatBlock Stats { get; set; }

        /// <summary>
        /// The tile this unit stands on. Kept in step with <see cref="Grid.HexGrid"/> occupancy by
        /// whatever moves the unit; the grid remains the authority on which tile is taken.
        /// </summary>
        public HexCoordinate Position { get; set; }

        /// <summary>
        /// True once the unit is out of the fight. Defeated units are skipped by the turn order and
        /// are expected to be lifted off the grid by the caller.
        /// </summary>
        public bool IsDefeated { get; set; }
    }
}
