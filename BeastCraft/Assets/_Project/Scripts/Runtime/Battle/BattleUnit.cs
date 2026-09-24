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
    /// HP pool effects spend against, the timed stat modifiers and statuses riding on the unit, and
    /// the level the damage formula reads. This is NOT the final creature-instance runtime model —
    /// the real one will carry equipped gear and a link back to its
    /// <c>CreatureSpeciesSO</c>, and this type will either grow into it or be replaced by it.
    /// </para>
    /// <para>
    /// A beast is built from its species, level and gear by <see cref="BattleUnitFactory"/>, which
    /// assembles <see cref="Stats"/> through <see cref="StatCalculator"/>. The constructor stays
    /// public for anything that already has a finished stat block, such as the avatar
    /// (<see cref="BattleAvatar"/>) and tests.
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
        /// <para>
        /// <paramref name="stats"/> is also where <see cref="MoveRange"/> comes from — its
        /// <see cref="StatBlock.MoveRange"/> — so a unit built from a bare six-axis block does not
        /// move.
        /// </para>
        /// <para>
        /// <paramref name="elements"/> is the unit's elemental affinity, normally its species'
        /// <c>CreatureSpeciesSO.Elements</c>. Leaving it off gives an empty list — no affinity,
        /// neutral to every element — rather than a null one. The list is copied, so later edits
        /// to the caller's array (or the species asset) do not reach a unit already in battle.
        /// </para>
        /// <para>
        /// <paramref name="level"/> is the unit's <see cref="Level"/> (a record only: the damage
        /// formula no longer reads it; see <see cref="Level"/>). It defaults to 1 so every existing call site keeps
        /// compiling; <see cref="BattleUnitFactory.CreateBeast"/> passes the beast's real level.
        /// Anything below 1 is stored as 1.
        /// </para>
        /// <para>
        /// <paramref name="stance"/> is the unit's <see cref="Stance"/>, normally its species'
        /// <c>CreatureSpeciesSO.Stance</c>. It defaults to <see cref="CombatStance.Vanguard"/>, whose
        /// movement is the plain approach rule, so every existing call site keeps its behaviour.
        /// </para>
        /// <para>
        /// <paramref name="statusResist"/> is the unit's <see cref="StatusResist"/>, clamped into
        /// [0, 100]. It defaults to 0 — no resistance — so every existing call site keeps its
        /// behaviour.
        /// </para>
        /// <para>
        /// <paramref name="footprint"/> is the unit's <see cref="Footprint"/>, normally its species'
        /// <c>CreatureSpeciesSO.Footprint</c>. It defaults to <see cref="UnitFootprint.Single"/>, one
        /// tile, so every existing call site keeps its behaviour.
        /// </para>
        /// </summary>
        public BattleUnit(string id, BattleTeam team, StatBlock stats, HexCoordinate position, SkillLoadout skills = null,
                          IReadOnlyList<Element> elements = null, int level = 1, CombatStance stance = CombatStance.Vanguard,
                          int statusResist = 0, UnitFootprint footprint = UnitFootprint.Single)
        {
            Id = id;
            Team = team;
            Stats = stats;
            CurrentHp = stats.Hp;
            Position = position;
            Skills = skills ?? new SkillLoadout(null);
            ActiveStatModifiers = new List<ActiveStatModifier>();
            Elements = CopyElements(elements);
            Level = level < 1 ? 1 : level;
            Stance = stance;
            StatusResist = statusResist < 0 ? 0 : statusResist > 100 ? 100 : statusResist;
            Footprint = footprint;
            _statuses = new List<ActiveStatus>();
        }

        private readonly List<ActiveStatus> _statuses;

        /// <summary>
        /// Stable identifier for this combatant, unique within a single battle. This is the key the
        /// grid tracks occupancy by, so it must not change while the unit is on the board.
        /// </summary>
        public string Id { get; }

        /// <summary>The side this unit fights for.</summary>
        public BattleTeam Team { get; }

        /// <summary>
        /// The unit's effective combat stats, move range included.
        /// <para>
        /// Starts as the assembled block — species base at level plus gear, see
        /// <see cref="StatCalculator"/> — and is settable because timed buffs and debuffs are
        /// folded straight into it: <see cref="SkillEffectApplier"/> writes the moved stat back
        /// here and records the delta in <see cref="ActiveStatModifiers"/> so it can take it back
        /// out on expiry. There is no separate overlay, so this is always the number to read.
        /// </para>
        /// </summary>
        public StatBlock Stats { get; set; }

        /// <summary>
        /// Live health. <see cref="Stats"/>'s <c>Hp</c> is the <em>maximum</em>; this is what is
        /// left of it, and it is what damage spends and healing restores.
        /// <para>
        /// Settable because damage and healing write it (through <see cref="SkillEffectApplier"/>),
        /// and so that a caller can seed it after construction — a unit is always built at full
        /// health, and there is still no persistent creature-instance model to carry damage in
        /// from a previous fight.
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
        /// <para>
        /// Once the unit is defeated the executor lifts it off the grid, but this keeps the tile it
        /// fell on, for logs and results. From then on it is a record, not occupancy: the tile may
        /// be taken by someone else, and nothing reads a defeated unit's position for play.
        /// </para>
        /// <para>
        /// For a large unit (see <see cref="Footprint"/>) this is the footprint's <em>anchor</em> — the
        /// centre of a <see cref="UnitFootprint.Hex7"/> — and the unit also covers the footprint's
        /// other tiles; distances to it are measured to its nearest tile
        /// (<see cref="FootprintMath"/>).
        /// </para>
        /// </summary>
        public HexCoordinate Position { get; set; }

        /// <summary>
        /// How many tiles this unit covers and in what shape, anchored on <see cref="Position"/>.
        /// Fixed at construction, like <see cref="Stance"/>. Every beast is
        /// <see cref="UnitFootprint.Single"/>; only large enemies are bigger. See the design doc,
        /// "Unit footprints".
        /// </summary>
        public UnitFootprint Footprint { get; }

        /// <summary>
        /// How many hex steps this unit may move on one of its own turns, as a whole-turn budget
        /// spent across every skill it attempts that turn (see <see cref="BattleTurnExecutor"/>).
        /// <para>
        /// Read straight from <see cref="Stats"/>: move range is a stat
        /// (<see cref="StatType.MoveRange"/>). The species authors a base that does not scale with
        /// level, gear modifiers add to it like any other stat when <see cref="StatCalculator"/>
        /// assembles the unit, and a <see cref="SkillEffectType.BuffStat"/> or
        /// <see cref="SkillEffectType.DebuffStat"/> on <see cref="StatType.MoveRange"/> moves it
        /// mid-battle — timed ones reverting on the unit's own turns — because those are folded
        /// into <see cref="Stats"/> directly. Keeping it a read-through rather than a copy means
        /// there is exactly one number to change and nothing to keep in step with it.
        /// </para>
        /// <para>
        /// Read-only for the same reason: to change a unit's move range, change its
        /// <see cref="Stats"/>. It is kept as its own property because the movement budget is read
        /// in several places and "the unit's move range" is the clearer name for it.
        /// </para>
        /// <para>
        /// 0 means "does not move": the unit must already be in range to act. Debuffs clamp stats
        /// at 0, so it cannot go negative through the effect path, but a hand-built
        /// <see cref="StatBlock"/> could hold a negative value; the executor reads that as 0
        /// rather than correcting it here — this stays a passive record.
        /// </para>
        /// </summary>
        public int MoveRange => Stats.MoveRange;

        /// <summary>
        /// The unit's equipped skill stack and its live cooldown counters. Driven once per turn by
        /// <see cref="BattleTurnExecutor"/>, which ticks it, works out which of the ready slots can
        /// actually reach something, and marks those fired.
        /// <para>
        /// Settable so a loadout can be swapped in after construction: the equipped stack is a
        /// player choice that neither the species nor <see cref="BattleUnitFactory"/> derives, so
        /// it is passed in rather than assembled. Expected to hold an empty loadout rather than
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
        /// <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit, System.Random)"/> and drains it in
        /// <see cref="SkillEffectApplier.TickModifiers"/>. Never <c>null</c>; empty is the normal
        /// state, and an instant (<c>DurationTurns == 0</c>) modifier never appears here at all.
        /// </para>
        /// </summary>
        public List<ActiveStatModifier> ActiveStatModifiers { get; }

        /// <summary>
        /// The unit's elemental affinities, consulted when it is <em>hit</em> by a damaging skill:
        /// <see cref="DamageFormula"/> scales the damage by
        /// <see cref="ElementChart.GetMultiplier(Element, IReadOnlyList{Element})"/> of the
        /// skill's <see cref="SkillSO.Element"/> against this list. The unit's own elements play no
        /// part in the damage it <em>deals</em> — the skill carries the attacking element.
        /// <para>
        /// Read-only and fixed at construction: an element is what a creature <em>is</em>, not a
        /// battle state, and nothing in the design changes it mid-fight. This is the minimal
        /// stand-in for the link back to <c>CreatureSpeciesSO</c> the real creature-instance model
        /// will carry. Never <c>null</c>; empty means no affinity.
        /// </para>
        /// </summary>
        public IReadOnlyList<Element> Elements { get; }

        /// <summary>
        /// The unit's level, always at least 1. <strong>Not</strong> part of
        /// <see cref="DamageFormula"/>: the formula used to carry a Pokemon-style level term, but
        /// stats already scale with level through the growth curve, so the level now reaches damage
        /// only through the stats it assembled. Kept for everything else that asks a unit its level
        /// (reporting, the avatar, future progression rules).
        /// <para>
        /// For a beast this is the level its stats were assembled at (see
        /// <see cref="BattleUnitFactory.CreateBeast"/>); it is only recorded here, and changing it
        /// would not re-assemble <see cref="Stats"/>. For the avatar, which has no progression yet,
        /// it is whatever battle level <see cref="BattleAvatar"/> was given.
        /// </para>
        /// <para>
        /// Read-only and fixed at construction, like <see cref="Elements"/>: nothing levels a unit
        /// mid-battle. The constructor stores anything below 1 as 1 — the one correction this
        /// passive record makes, because a level-0 or negative unit has no meaning.
        /// </para>
        /// </summary>
        public int Level { get; }

        /// <summary>
        /// How this unit positions itself (see <see cref="CombatStance"/>), read by
        /// <see cref="BattleTurnExecutor"/> when it picks where an approach stops, whether a melee
        /// slot may walk, and whether leftover movement is spent retreating.
        /// <para>
        /// Read-only and fixed at construction, like <see cref="Elements"/>: it is a property of
        /// what the creature is, copied from its species by <see cref="BattleUnitFactory"/>, and
        /// nothing changes it mid-fight.
        /// </para>
        /// </summary>
        public CombatStance Stance { get; }

        /// <summary>
        /// Percent (0-100) knocked off the chance of every <em>hostile</em> non-damage effect that
        /// targets this unit — a debuff, a status or a knockback cast by the other team:
        /// the effective chance is <c>SkillEffect.Chance * (100 - StatusResist) / 100</c>. Effects
        /// from the unit's own side (heals, buffs, shields) ignore it. See
        /// <see cref="SkillEffectApplier"/>.
        /// <para>
        /// Read-only and fixed at construction, like <see cref="Stance"/>: a trait of what the unit
        /// is (the balance simulator gives its bosses resistance), not a battle state.
        /// </para>
        /// </summary>
        public int StatusResist { get; }

        /// <summary>
        /// The statuses currently riding on this unit (<see cref="StatusType"/>), in the order they
        /// were applied. A read-only view: <see cref="StatusEffects"/> is the only writer, applying
        /// them through <see cref="SkillEffectApplier"/> and ticking them on the unit's own turns
        /// from <see cref="BattleTurnExecutor"/>. Never <c>null</c>; empty is the normal state.
        /// </summary>
        public IReadOnlyList<ActiveStatus> Statuses
        {
            get { return _statuses; }
        }

        /// <summary>The mutable status list behind <see cref="Statuses"/>, for <see cref="StatusEffects"/> only.</summary>
        internal List<ActiveStatus> StatusList
        {
            get { return _statuses; }
        }

        /// <summary>
        /// True once the unit is out of the fight. Defeated units are skipped by the turn order and
        /// by targeting, and <see cref="BattleTurnExecutor"/> lifts them off the grid as soon as
        /// they fall (their <see cref="Position"/> is kept as a record).
        /// </summary>
        public bool IsDefeated { get; set; }

        private static Element[] CopyElements(IReadOnlyList<Element> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return new Element[0];
            }

            Element[] copy = new Element[elements.Count];

            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = elements[i];
            }

            return copy;
        }
    }
}
