using System;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One effect applied by a skill. A skill may carry several (e.g. damage plus a speed debuff).
    /// <para>
    /// <strong>Defaults reproduce the original effect exactly.</strong> Every field added for the
    /// advanced effects (<see cref="Chance"/>, <see cref="MaxStacks"/>, <see cref="IsPercent"/>,
    /// <see cref="HitCount"/>, <see cref="ExecuteBonusPercent"/>, <see cref="Status"/>) is inert at
    /// its default. The integer fields also treat an out-of-range value (0 or below) as that
    /// default, because Unity zero-fills a <c>[Serializable]</c> entry added to a list in the
    /// inspector instead of running the field initializers — a freshly authored effect must land
    /// once, always, rather than never. See the design doc, "Status effects and advanced skill
    /// effects".
    /// </para>
    /// </summary>
    [Serializable]
    public class SkillEffect
    {
        /// <summary>The chance a non-damage effect lands when nothing is set: always.</summary>
        public const int AlwaysChance = 100;

        public SkillEffectType EffectType = SkillEffectType.Damage;

        /// <summary>
        /// The stat affected. Only meaningful for <see cref="SkillEffectType.BuffStat"/> and
        /// <see cref="SkillEffectType.DebuffStat"/>; ignored for the other effect types.
        /// </summary>
        public StatType AffectedStat = StatType.Attack;

        /// <summary>
        /// Effect strength. Interpretation depends on <see cref="EffectType"/>: damage power (a
        /// percent of the attacking stat), heal amount, stat change (flat, or a percent when
        /// <see cref="IsPercent"/>), and for <see cref="SkillEffectType.ApplyStatus"/> the status's
        /// potency — a shield's percent of the caster's <c>Defense</c>, a damage-over-time stack's
        /// power, a knockback's distance in hexes. Scaled by the skill's level
        /// (<see cref="SkillInstance.ScaleMagnitude"/>) except for the knockback distance.
        /// </summary>
        public float Magnitude;

        /// <summary>
        /// 0 means instant / one-time (damage, heal). Greater than 0 means the effect persists for
        /// that many turns (buffs, debuffs, statuses) — the <em>affected</em> unit's own turns.
        /// </summary>
        public int DurationTurns;

        /// <summary>
        /// Which status an <see cref="SkillEffectType.ApplyStatus"/> effect applies. Ignored by the
        /// other effect types. <see cref="StatusType.None"/> (the default) applies nothing.
        /// </summary>
        public StatusType Status = StatusType.None;

        /// <summary>
        /// Percent chance, rolled per target, that a <em>non-damage</em> effect lands (damage always
        /// lands). A hostile application — onto a unit of the other team — is reduced by the
        /// target's <see cref="BattleUnit.StatusResist"/>:
        /// <c>Chance * (100 - resist) / 100</c>. Values of 0 or below, and above 100, read as 100.
        /// See <see cref="SkillEffectApplier"/> for the roll and its place in the draw order.
        /// </summary>
        public int Chance = AlwaysChance;

        /// <summary>
        /// How many copies of this effect one unit may carry at once, for a timed
        /// <see cref="SkillEffectType.BuffStat"/> / <see cref="SkillEffectType.DebuffStat"/> and a
        /// <see cref="StatusType.DamageOverTime"/>. Each copy keeps its own duration. At the cap a
        /// new application replaces the copy with the fewest turns left, so the default of 1 means
        /// re-application refreshes. 0 or below reads as 1.
        /// </summary>
        public int MaxStacks = 1;

        /// <summary>
        /// For <see cref="SkillEffectType.BuffStat"/> / <see cref="SkillEffectType.DebuffStat"/>:
        /// <see cref="Magnitude"/> is a percent of the unit's <em>current</em> value of the stat at
        /// the moment it lands (truncated to a whole number), not a flat amount. Ignored elsewhere.
        /// </summary>
        public bool IsPercent;

        /// <summary>
        /// For <see cref="SkillEffectType.Damage"/>: how many separate hits the effect deals to each
        /// target, each with its own crit and variance rolls, shield absorption and defeat check.
        /// Hitting stops once the target is defeated. 0 or below reads as 1.
        /// </summary>
        public int HitCount = 1;

        /// <summary>
        /// For <see cref="SkillEffectType.Damage"/>: extra damage, in percent, at the target's
        /// missing-HP fraction, applied before truncation: <c>× (1 + bonus/100 × missing/max)</c>.
        /// 0 (the default) or below adds nothing.
        /// </summary>
        public int ExecuteBonusPercent;
    }
}
