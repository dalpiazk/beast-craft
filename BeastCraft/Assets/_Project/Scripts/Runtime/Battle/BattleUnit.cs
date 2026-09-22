using System.Collections.Generic;
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
    /// Intentionally minimal: the identity, allegiance, stats and position that the grid and the
    /// turn manager need to reference, plus the equipped skill stack the rotation ticks, the live
    /// HP pool effects spend against, and the timed stat modifiers riding on the unit. This is
    /// NOT the final creature-instance runtime model — the real one will carry level, equipped
    /// gear, status effects and a link back to its <c>CreatureSpeciesSO</c>, and this type will
    /// either grow into it or be replaced by it.
    /// </para>
    /// <para>
    /// A passive data record: it holds battle state but runs no battle logic. Nothing here
    /// computes, decides or reacts — no setter has a side effect, so
    /// <see cref="CurrentHp"/> hitting zero does <em>not</em> quietly flip
    /// <see cref="IsDefeated"/>. Whatever writes the HP is responsible for writing the flag too,
    /// as a visible step; see <see cref="SkillEffectApplier"/>, which is the one place that does.
    /// </para>
    /// </summary>
    public class BattleUnit
    {
        /// <summary>
        /// Builds a combatant. <paramref name="skills"/> is the authored skill stack this unit
        /// rotates through; leaving it off gives the unit an empty loadout rather than a null one,
        /// so <see cref="Skills"/> is always safe to tick.
        /// <para>
        /// <see cref="CurrentHp"/> starts at <paramref name="stats"/>'s <c>Hp</c>: every unit
        /// enters a battle at full health. Carrying damage in from a previous fight would need a
        /// persistent creature-instance model, which does not exist yet.
        /// </para>
        /// </summary>
        public BattleUnit(string id, BattleTeam team, StatBlock stats, HexCoordinate position, SkillLoadout skills = null)
        {
            Id = id;
            Team = team;
            Stats = stats;
            CurrentHp = stats.Hp;
            Position = position;
            Skills = skills ?? new SkillLoadout(null);
            ActiveStatModifiers = new List<ActiveStatModifier>();
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
        /// Live health. <see cref="Stats"/>'s <c>Hp</c> is the <em>maximum</em>; this is what is
        /// left of it, and it is what damage spends and healing restores.
        /// <para>
        /// Settable for the same reason <see cref="Stats"/> and <see cref="Position"/> are: the
        /// pass that assembles a unit from its creature instance does not exist yet, so a caller
        /// may need to seed or correct this after construction.
        /// </para>
        /// <para>
        /// A plain number with no behaviour attached. It does not clamp itself to
        /// <c>Stats.Hp</c>, and reaching 0 does not set <see cref="IsDefeated"/> — both are the
        /// job of whatever writes it. <see cref="SkillEffectApplier"/> holds the invariant
        /// <c>0 &lt;= CurrentHp &lt;= Stats.Hp</c> for every write it makes.
        /// </para>
        /// </summary>
        public int CurrentHp { get; set; }

        /// <summary>
        /// The tile this unit stands on. Kept in step with <see cref="Grid.HexGrid"/> occupancy by
        /// whatever moves the unit; the grid remains the authority on which tile is taken.
        /// <para>
        /// <see cref="BattleTurnExecutor"/> is the one thing that moves a unit today, and it writes
        /// both halves together: the grid placement first, this property only once the grid has
        /// accepted it.
        /// </para>
        /// </summary>
        public HexCoordinate Position { get; set; }

        /// <summary>
        /// How many hex steps this unit may move on one of its own turns, as a whole-turn budget
        /// spent across every skill it attempts that turn (see <see cref="BattleTurnExecutor"/>).
        /// <para>
        /// Not read from <see cref="Stats"/>, because there is no move-range stat to read.
        /// <see cref="StatBlock"/> models the six combat axes and nothing else, and adding a seventh
        /// would change the shape of every authored species asset for a number the design has not
        /// settled yet — whether move range comes from the species, from gear, from a status, or is
        /// simply flat. So it lives here, on the battle-side unit, where the pass that assembles a
        /// unit from its creature instance can set it from whatever source that answer turns out to
        /// name.
        /// </para>
        /// <para>
        /// Settable rather than a constructor parameter for the same reason
        /// <see cref="CurrentHp"/> is settable: the assembling pass does not exist yet. Unlike
        /// <c>CurrentHp</c>, though, there is nothing on <see cref="Stats"/> to seed it from, so it
        /// is not taken at construction at all.
        /// </para>
        /// <para>
        /// Defaults to 0, which means "does not move" and is the honest default rather than a
        /// guessed one: every code path that consults it treats 0 as a unit that must already be in
        /// range to act, so nothing moves until a caller deliberately says how far it may. A
        /// negative value is treated as 0 by the executor rather than corrected here — this stays a
        /// passive record.
        /// </para>
        /// </summary>
        public int MoveRange { get; set; }

        /// <summary>
        /// The unit's equipped skill stack and its live cooldown counters. Driven once per turn by
        /// <see cref="BattleTurnExecutor"/>, which ticks it, works out which of the ready slots can
        /// actually reach something, and marks those fired.
        /// <para>
        /// Settable for the same reason <see cref="Stats"/> is: the pass that assembles a unit from
        /// its creature instance and its learned skills does not exist yet, so a loadout may need to
        /// be swapped in after construction. Expected to hold an empty loadout rather than
        /// <c>null</c>.
        /// </para>
        /// </summary>
        public SkillLoadout Skills { get; set; }

        /// <summary>
        /// The timed stat buffs and debuffs currently riding on this unit, each holding the signed
        /// delta already folded into <see cref="Stats"/> and how many of <em>this unit's own</em>
        /// turns are left before it is subtracted back out.
        /// <para>
        /// Lives on the affected unit rather than on the caster or in a side table because that is
        /// whose turns the countdown is measured in, and because a lookup keyed off the roster
        /// would be one more thing to keep in step with it.
        /// </para>
        /// <para>
        /// A plain mutable list, and deliberately so: the unit owns the storage but none of the
        /// logic. <see cref="SkillEffectApplier"/> is the only writer — it fills the list in
        /// <see cref="SkillEffectApplier.Apply"/> and drains it in
        /// <see cref="SkillEffectApplier.TickModifiers"/>. Never <c>null</c>; empty is the normal
        /// state, and an instant (<c>DurationTurns == 0</c>) modifier never appears here at all.
        /// </para>
        /// </summary>
        public List<ActiveStatModifier> ActiveStatModifiers { get; }

        /// <summary>
        /// True once the unit is out of the fight. Defeated units are skipped by the turn order and
        /// are expected to be lifted off the grid by the caller.
        /// </summary>
        public bool IsDefeated { get; set; }
    }
}
