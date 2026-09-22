using System.Collections.Generic;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Turns a <see cref="SkillActivation"/> — a skill that fired and the units it landed on — into
    /// the actual state changes on those units: HP spent, HP restored, stats moved.
    /// <para>
    /// The counterpart to <see cref="SkillTargetResolver"/>, and static for the same reason: this
    /// is a pure function of the activation and the units in it, owned by neither side of the
    /// exchange. <see cref="BattleUnit"/> stays a passive data record, so all of the logic that
    /// reads and writes its HP and stats lives here rather than on it.
    /// </para>
    /// <para>
    /// <strong>Not wired to anything.</strong> Nothing in this codebase calls
    /// <see cref="Apply"/> or <see cref="TickModifiers"/> yet — the turn executor that would is a
    /// later pass. This provides the mechanism; the wiring is somebody else's deliverable.
    /// </para>
    /// <para>
    /// <strong>Scaffold assumptions.</strong> The rules below are reasonable engineering defaults
    /// for a first pass, not producer-confirmed balance, and are cheap to revisit:
    /// <list type="bullet">
    /// <item><description>
    /// <strong>No damage formula.</strong> <see cref="SkillEffect.Magnitude"/> is applied flat,
    /// straight against HP. No Attack-versus-Defense math, no stat scaling, no crit, no type
    /// chart, no variance. That is a distinct balancing pass, and it is the reason
    /// <see cref="Apply"/> still takes a caster it barely reads.
    /// </description></item>
    /// <item><description>
    /// <strong>A defeated target takes nothing further.</strong> Once a target's HP reaches 0 it
    /// is skipped for every remaining effect in the same activation. See <see cref="Apply"/>.
    /// </description></item>
    /// <item><description>
    /// <strong><see cref="SkillEffectType.ApplyStatus"/> does nothing.</strong> There is no
    /// status-effect system to apply it to. See the switch arm in <c>ApplyEffect</c>.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Effects only. This does not spend <see cref="SkillSO.ResourceCost"/> (still deferred), does
    /// not lift a defeated unit off the grid, does not check whether the battle has now been won,
    /// and does not decide when any of this happens. Non-throwing throughout, matching the rest of
    /// the namespace: null activations, null skills, null effects, null targets and an empty
    /// target list are all quiet no-ops rather than errors.
    /// </para>
    /// </summary>
    public static class SkillEffectApplier
    {
        /// <summary>
        /// Applies every <see cref="SkillSO.Effects"/> entry of the fired skill to every unit in
        /// <see cref="SkillActivation.Targets"/>.
        /// <para>
        /// Iteration is target-major: each target runs the whole effect list in authored order
        /// before the next target starts. Authored order is the only thing sequencing a
        /// multi-effect skill, exactly as stack order is the only thing sequencing a multi-skill
        /// turn in <see cref="SkillLoadout.Tick"/>.
        /// </para>
        /// <para>
        /// <strong>A target defeated mid-list is skipped for the rest of that list.</strong> If a
        /// <see cref="SkillEffectType.Damage"/> effect drops a target to 0 HP, the
        /// <see cref="SkillEffectType.Heal"/> or buff authored after it does not land on that
        /// target — and nor would a second damage effect, so nothing can be hit while already
        /// dead. This is the same rule <see cref="SkillTargetResolver"/> already applies at
        /// selection time, where only <c>!IsDefeated</c> units are ever eligible; a unit that dies
        /// half a step later is the same situation, and the alternative (a skill that kills a unit
        /// and then heals it back up in the same breath) has no revival mechanic to justify it.
        /// Other targets in the same activation are unaffected — the skip is per target.
        /// </para>
        /// <para>
        /// <paramref name="caster"/> is checked and otherwise unread. A null or defeated caster
        /// applies nothing, which is the same stance
        /// <see cref="SkillLoadout.TickAndResolve"/> and
        /// <see cref="SkillTargetResolver.ResolveTargets"/> already take — a unit that is out of
        /// the fight does not land skills. Beyond that guard the caster contributes nothing today
        /// because there is no damage formula for it to contribute to; it is on the signature so
        /// that adding one does not have to churn every call site, and the turn executor that will
        /// call this has the caster in hand anyway.
        /// </para>
        /// <para>
        /// An activation with no targets is a legal whiff, per
        /// <see cref="SkillActivation"/>'s own contract: the skill fired, it reset its cooldown,
        /// and it changed nothing.
        /// </para>
        /// </summary>
        public static void Apply(SkillActivation activation, BattleUnit caster)
        {
            if (activation == null || activation.Skill == null || caster == null || caster.IsDefeated)
            {
                return;
            }

            List<SkillEffect> effects = activation.Skill.Effects;
            IReadOnlyList<BattleUnit> targets = activation.Targets;

            if (effects == null || effects.Count == 0 || targets == null)
            {
                return;
            }

            for (int t = 0; t < targets.Count; t++)
            {
                BattleUnit target = targets[t];

                if (target == null)
                {
                    continue;
                }

                for (int e = 0; e < effects.Count; e++)
                {
                    // Re-checked every effect, not once per target: this is what stops an effect
                    // landing on a unit an earlier effect in the same list has just defeated.
                    if (target.IsDefeated)
                    {
                        break;
                    }

                    if (effects[e] != null)
                    {
                        ApplyEffect(target, effects[e]);
                    }
                }
            }
        }

        /// <summary>
        /// Advances one unit's timed buffs and debuffs by a single turn, reverting any that have
        /// run out.
        /// <para>
        /// <strong>Call this once per turn of the unit passed in — its own turn, not the turn of
        /// whoever applied the modifier.</strong> A debuff cast by a fast unit on a slow one is
        /// counted in the slow unit's turns, so the two clocks genuinely differ and the caster's
        /// turn is the wrong hook. Whether the call sits at the start or the end of that turn is
        /// the turn executor's choice, as long as it is consistent; nothing here depends on it.
        /// </para>
        /// <para>
        /// <strong>Nothing calls this.</strong> No turn executor exists yet to drive it — that is
        /// a later pass. This is the hook that pass will need, provided now so the duration half
        /// of <see cref="SkillEffectType.BuffStat"/> is a complete mechanism rather than a
        /// half-built one. Until it is wired up, timed modifiers apply and simply never expire.
        /// </para>
        /// <para>
        /// A modifier with <see cref="ActiveStatModifier.RemainingTurns"/> of 2 survives one call
        /// and reverts on the second. Reverting subtracts the delta that was actually applied
        /// (see <see cref="ActiveStatModifier.Delta"/>), so a modifier that was clamped on the way
        /// in cannot overshoot on the way out. Each entry runs its own countdown, so two buffs on
        /// the same stat with different durations expire independently.
        /// </para>
        /// <para>
        /// A null unit is a no-op. Defeated units are not special-cased because they take no turn,
        /// so nothing should be ticking them in the first place.
        /// </para>
        /// </summary>
        public static void TickModifiers(BattleUnit unit)
        {
            if (unit == null || unit.ActiveStatModifiers == null)
            {
                return;
            }

            List<ActiveStatModifier> active = unit.ActiveStatModifiers;

            // Walked backwards so that removing an expired entry cannot skip the next one.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ActiveStatModifier modifier = active[i];

                if (modifier == null)
                {
                    active.RemoveAt(i);
                    continue;
                }

                modifier.RemainingTurns -= 1;

                if (modifier.RemainingTurns > 0)
                {
                    continue;
                }

                AddToStat(unit, modifier.Stat, -modifier.Delta);
                active.RemoveAt(i);
            }
        }

        /// <summary>Routes one effect to its handler. The whole of the effect vocabulary.</summary>
        private static void ApplyEffect(BattleUnit target, SkillEffect effect)
        {
            switch (effect.EffectType)
            {
                case SkillEffectType.Damage:
                    ApplyDamage(target, effect);
                    break;

                case SkillEffectType.Heal:
                    ApplyHeal(target, effect);
                    break;

                case SkillEffectType.BuffStat:
                    ApplyStatChange(target, effect, 1);
                    break;

                case SkillEffectType.DebuffStat:
                    ApplyStatChange(target, effect, -1);
                    break;

                case SkillEffectType.ApplyStatus:
                    // Deliberately empty, and not a bug.
                    //
                    // There is no status-effect system anywhere in this codebase's data model: no
                    // poison, no stun, no burn, nothing that a status could be an instance of.
                    // SkillEffect can author an ApplyStatus effect, but it carries only a
                    // Magnitude and a DurationTurns — there is no field naming *which* status,
                    // because the set of statuses has never been designed. Applying something
                    // here would mean inventing that design in the effect applier, which is the
                    // wrong place and the wrong pass for it.
                    //
                    // So this is a documented gap, not an oversight: authoring an ApplyStatus
                    // effect on a skill today does nothing at all. It does not throw, because a
                    // half-authored asset should not be able to kill a battle, and it does not
                    // log, because it would log once per affected unit per activation and drown
                    // the console in a message nobody can act on. When the status system is
                    // designed, this arm is where it plugs in.
                    break;

                default:
                    // An effect type added to the enum without an arm here. Ignored rather than
                    // thrown, matching this namespace's non-throwing stance.
                    break;
            }
        }

        /// <summary>
        /// Spends HP. The magnitude is applied flat — there is no formula, per this class's
        /// scaffold assumptions — and the result is clamped into <c>[0, Stats.Hp]</c>, so an
        /// overkill hit lands the unit on exactly 0 rather than in negative territory that a later
        /// heal would have to climb out of.
        /// <para>
        /// Setting <see cref="BattleUnit.IsDefeated"/> is an explicit step here, on purpose.
        /// <see cref="BattleUnit.CurrentHp"/> is a plain property with no side effects, so defeat
        /// is something the code that spends the HP decides and writes, visibly, at the one place
        /// it can happen — not a hidden consequence of a setter.
        /// </para>
        /// </summary>
        private static void ApplyDamage(BattleUnit target, SkillEffect effect)
        {
            SetCurrentHp(target, target.CurrentHp - ToAmount(effect.Magnitude));

            if (target.CurrentHp <= 0)
            {
                target.IsDefeated = true;
            }
        }

        /// <summary>
        /// Restores HP, flat and clamped at <c>Stats.Hp</c> so healing cannot overfill a unit.
        /// <para>
        /// Healing a <em>defeated</em> unit never reaches here: <see cref="Apply"/> skips a
        /// defeated target before the effect runs, so a heal cannot revive. That follows from
        /// there being no revival mechanic anywhere in the design — reviving would be a real
        /// combat rule with real balance weight, and having it fall out as a side effect of any
        /// heal that happens to be authored after a damage effect would be inventing it by
        /// accident. Defeated stays defeated until something is designed to undo it.
        /// </para>
        /// </summary>
        private static void ApplyHeal(BattleUnit target, SkillEffect effect)
        {
            SetCurrentHp(target, target.CurrentHp + ToAmount(effect.Magnitude));
        }

        /// <summary>
        /// Moves one stat axis, instantly and permanently when
        /// <see cref="SkillEffect.DurationTurns"/> is 0, or for that many of the target's own turns
        /// when it is greater.
        /// <para>
        /// <strong>Sign convention: the effect type carries the sign, the magnitude does not.</strong>
        /// <paramref name="sign"/> is +1 for <see cref="SkillEffectType.BuffStat"/> and -1 for
        /// <see cref="SkillEffectType.DebuffStat"/>, so both are authored as positive numbers —
        /// a "-5 Attack" debuff is authored as <c>DebuffStat</c> with a <c>Magnitude</c> of 5.
        /// That is what the data model already implies: <see cref="SkillEffect"/> splits buff and
        /// debuff into two enum arms and calls the field "effect strength", which only earns its
        /// keep if the arm is what makes it up or down. It is also the one place this codebase
        /// differs from the gear path — <see cref="StatModifier.FlatBonus"/> is a genuinely signed
        /// int with no accompanying type to carry the sign, so a cursed item authors a negative
        /// bonus directly. The two are not in conflict; they just encode it in different places.
        /// A negative <c>Magnitude</c> on a <c>DebuffStat</c> is therefore an authoring mistake
        /// that reads as a buff. It is taken at face value rather than corrected, because
        /// second-guessing the asset would hide the mistake instead of surfacing it, and the
        /// clamps below keep it from corrupting anything.
        /// </para>
        /// <para>
        /// Only <c>DurationTurns &gt; 0</c> is tracked for reversion. An instant modifier is a
        /// permanent change to the stat block with nothing left to revert, per
        /// <see cref="SkillEffect.DurationTurns"/>'s own documentation, so it never enters
        /// <see cref="BattleUnit.ActiveStatModifiers"/> — and neither does a timed one that ended
        /// up moving the stat by nothing, since reverting zero is a no-op.
        /// </para>
        /// </summary>
        private static void ApplyStatChange(BattleUnit target, SkillEffect effect, int sign)
        {
            int applied = AddToStat(target, effect.AffectedStat, sign * ToAmount(effect.Magnitude));

            if (applied != 0 && effect.DurationTurns > 0)
            {
                target.ActiveStatModifiers.Add(new ActiveStatModifier(effect.AffectedStat, applied, effect.DurationTurns));
            }
        }

        /// <summary>
        /// Adds a signed delta to one stat axis and reports how much actually landed, which is the
        /// number <see cref="ActiveStatModifier.Delta"/> has to remember for reverting.
        /// <para>
        /// Stats are held at or above 0: a debuff bigger than the stat it is draining takes the
        /// stat to 0 and reports only the part it managed to take, rather than driving the stat
        /// negative and inverting whatever reads it. A debuff that reduces
        /// <see cref="StatType.HP"/> reduces the <em>maximum</em>, so
        /// <see cref="BattleUnit.CurrentHp"/> is pulled down with it to keep the
        /// <c>CurrentHp &lt;= Stats.Hp</c> invariant; the reverse is not true, since a unit whose
        /// max HP goes up does not get the difference handed to it as healing. Reaching 0 max HP
        /// does not defeat a unit — defeat is <see cref="ApplyDamage"/>'s call and only its call.
        /// </para>
        /// <para>
        /// <see cref="StatBlock"/> is a struct, so the copy has to be written back to
        /// <see cref="BattleUnit.Stats"/> explicitly; mutating the property's value in place would
        /// not compile, and mutating a local copy without the write-back would compile and do
        /// nothing.
        /// </para>
        /// </summary>
        private static int AddToStat(BattleUnit target, StatType stat, int delta)
        {
            StatBlock stats = target.Stats;
            int current = stats.GetStat(stat);
            int applied = current + delta < 0 ? -current : delta;

            if (applied == 0)
            {
                return 0;
            }

            stats.SetStat(stat, current + applied);
            target.Stats = stats;

            if (stat == StatType.HP && target.CurrentHp > stats.Hp)
            {
                target.CurrentHp = stats.Hp;
            }

            return applied;
        }

        /// <summary>
        /// The single place <see cref="BattleUnit.CurrentHp"/> is written, holding
        /// <c>0 &lt;= CurrentHp &lt;= Stats.Hp</c> on every path into it. Routing both damage and
        /// healing through one clamp means neither can break the invariant on its own, including
        /// under a negative authored magnitude that makes damage read as a heal or vice versa.
        /// </summary>
        private static void SetCurrentHp(BattleUnit unit, int value)
        {
            int max = unit.Stats.Hp < 0 ? 0 : unit.Stats.Hp;

            if (value < 0)
            {
                value = 0;
            }
            else if (value > max)
            {
                value = max;
            }

            unit.CurrentHp = value;
        }

        /// <summary>
        /// The whole-number amount an authored <see cref="SkillEffect.Magnitude"/> is worth. HP and
        /// stats are integers while magnitude is a float, so authoring 7.9 damage is worth 7: it
        /// truncates toward zero rather than rounding, which keeps a fractional magnitude from
        /// quietly buying a point it did not author. Fractions have no meaning until there is a
        /// damage formula to multiply them into, at which point this is where rounding gets
        /// decided properly.
        /// </summary>
        private static int ToAmount(float magnitude)
        {
            return (int)magnitude;
        }
    }
}
