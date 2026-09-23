using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
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
    /// <strong>Driven by <see cref="BattleTurnExecutor"/>.</strong> The executor calls
    /// <see cref="TickModifiers"/> at the start of each unit's own turn and
    /// <see cref="Apply(SkillActivation, BattleUnit, System.Random)"/> for every skill that fires
    /// in it, the avatar's included, passing the battle's rng. This class supplies the mechanism
    /// and decides nothing about when it runs.
    /// </para>
    /// <para>
    /// <strong>Scaffold assumptions.</strong> The rules below are reasonable engineering defaults
    /// for a first pass, not producer-confirmed balance, and are cheap to revisit:
    /// <list type="bullet">
    /// <item><description>
    /// <strong>Damage and heals are stat-based; stat changes are flat.</strong> A
    /// <see cref="SkillEffectType.Damage"/> effect's <see cref="SkillEffect.Magnitude"/> is its
    /// <em>power</em>, fed to <see cref="DamageFormula"/> with the caster's level and attacking
    /// stat, the target's defending stat (the pair picked by <see cref="SkillSO.Category"/>) and
    /// the element chart — the fired skill's <see cref="SkillSO.Element"/> against the target's
    /// <see cref="BattleUnit.Elements"/> — then a crit roll off the caster's
    /// <see cref="StatBlock.CritChance"/> and a variance roll, both drawn from the battle's rng (see
    /// <see cref="DamageFormula"/> for the rolls, their fixed draw order and the null-rng
    /// deterministic fallback). No same-element bonus yet. A <see cref="SkillEffectType.Heal"/>'s
    /// magnitude is a percent of the caster's current <c>SpecialAttack</c>, times
    /// <see cref="HealScale"/> (see <see cref="ApplyHeal"/>): no defense, no crit, no variance. Buffs
    /// and debuffs still move a stat by exactly their magnitude (flat, or with
    /// <see cref="SkillEffect.IsPercent"/> that percent of the stat's current value). Neither heals
    /// nor stat changes are ever scaled by element or rolled.
    /// </description></item>
    /// <item><description>
    /// <strong>A defeated target takes nothing further.</strong> Once a target's HP reaches 0 it
    /// is skipped for every remaining effect in the same activation. See
    /// <see cref="Apply(SkillActivation, BattleUnit, System.Random)"/>.
    /// </description></item>
    /// <item><description>
    /// <strong><see cref="SkillEffectType.ApplyStatus"/> applies a <see cref="StatusType"/></strong>
    /// through <see cref="StatusEffects"/>, which owns every status rule. Non-damage effects are
    /// gated by <see cref="SkillEffect.Chance"/> (less the target's
    /// <see cref="BattleUnit.StatusResist"/> when hostile); damage effects may hit several times
    /// (<see cref="SkillEffect.HitCount"/>) and scale with the target's missing HP
    /// (<see cref="SkillEffect.ExecuteBonusPercent"/>); shields soak damage before HP. See
    /// <see cref="Apply(SkillActivation, BattleUnit, System.Random, HexGrid)"/> and the design doc,
    /// "Status effects and advanced skill effects".
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Effects only. This does not spend <see cref="SkillSO.ResourceCost"/> (still deferred), does
    /// not lift a defeated unit off the grid (<see cref="BattleTurnExecutor"/> does that right after
    /// each application; the board is passed in only so a knockback can move its target), does not
    /// check whether the battle has now been won, and does not decide when any of this happens. Non-throwing throughout, matching the rest of
    /// the namespace: null activations, null skills, null effects, null targets and an empty
    /// target list are all quiet no-ops rather than errors.
    /// </para>
    /// </summary>
    public static class SkillEffectApplier
    {
        /// <summary>
        /// The tunable multiplier on every heal: a heal restores
        /// <c>Magnitude / 100 x caster SpecialAttack x HealScale</c> HP. One place to move the whole
        /// heal curve without re-authoring every heal magnitude. See <see cref="ApplyHeal"/>.
        /// </summary>
        public const double HealScale = 1.0;

        /// <summary>
        /// Applies every <see cref="SkillSO.Effects"/> entry of the fired skill to every unit in
        /// <see cref="SkillActivation.Targets"/>.
        /// <para>
        /// <strong>Skill level.</strong> The effect list and magnitudes come from the activation's
        /// <see cref="SkillActivation.Instance"/>: <see cref="SkillInstance.Effects"/> (the authored
        /// effects plus any passed tier's bonus effects), each magnitude scaled by
        /// <see cref="SkillInstance.ScaleMagnitude"/> before it is used — as the damage power handed
        /// to <see cref="DamageFormula"/>, as the heal amount, or as the stat change. At level 1,
        /// tier 0 both are the authored values untouched.
        /// </para>
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
        /// <paramref name="caster"/> is checked first: a null or defeated caster applies nothing,
        /// which is the same stance <see cref="SkillLoadout.TickAndResolve"/> and
        /// <see cref="SkillTargetResolver.ResolveTargets"/> already take — a unit that is out of
        /// the fight does not land skills. Past that guard it feeds the damage formula: its
        /// <see cref="BattleUnit.Level"/> and its current attacking stat
        /// (<see cref="BattleUnit.Stats"/>, read as each damage effect lands, so a buff applied
        /// earlier in the fight counts). The attacking <em>element</em> still comes from the skill
        /// (<see cref="SkillSO.Element"/>), not from the caster's own
        /// <see cref="BattleUnit.Elements"/>. A heal reads the caster's current <c>SpecialAttack</c>;
        /// stat changes do not read the caster.
        /// </para>
        /// <para>
        /// An activation with no targets is a legal whiff, per
        /// <see cref="SkillActivation"/>'s own contract: the skill fired, it reset its cooldown,
        /// and it changed nothing.
        /// </para>
        /// <para>
        /// <strong>Randomness.</strong> Every damage effect that lands draws its crit roll and
        /// then its variance roll from <paramref name="rng"/> (see <see cref="DamageFormula.Roll"/>),
        /// in the target-major, authored-effect order above, and is recorded in
        /// <see cref="SkillActivation.Hits"/>. An effect skipped because its target is already
        /// defeated draws nothing. A null <paramref name="rng"/> is the deterministic fallback: no
        /// variance, no crit.
        /// </para>
        /// </summary>
        public static void Apply(SkillActivation activation, BattleUnit caster, System.Random rng)
        {
            Apply(activation, caster, rng, null);
        }

        /// <summary>
        /// <see cref="Apply(SkillActivation, BattleUnit, System.Random)"/> on a board, which only a
        /// <see cref="StatusType.Knockback"/> reads (without one a knockback does nothing).
        /// <see cref="BattleTurnExecutor"/> always passes its grid.
        /// <para>
        /// <strong>Advanced effects.</strong>
        /// <list type="bullet">
        /// <item><description>
        /// <strong>Chance.</strong> Every <em>non-damage</em> effect checks
        /// <see cref="GetEffectiveChance"/> per target (<see cref="RollChance"/>) before it does
        /// anything; damage always lands. A failed check skips that effect for that target only.
        /// </description></item>
        /// <item><description>
        /// <strong>Multi-hit.</strong> A damage effect deals <see cref="SkillEffect.HitCount"/> hits
        /// to each target, each through the whole pipeline — its own crit and variance rolls, the
        /// execute bonus at the target's HP at that moment, shield absorption, the defeat check —
        /// and stops as soon as the target is defeated. Each hit is recorded in
        /// <see cref="SkillActivation.Hits"/>.
        /// </description></item>
        /// <item><description>
        /// <strong>Shields.</strong> Damage (hits and damage-over-time ticks alike) is taken from the
        /// target's <see cref="StatusType.Shield"/> first and only the rest from HP.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// <strong>Draw order.</strong> Target-major, then authored effect order, as before. Within
        /// that, a damage effect draws crit then variance for each hit in turn (nothing for a hit
        /// that never happens because the target fell), and a non-damage effect draws <em>one</em>
        /// <c>rng.Next(100)</c> for its chance check — only when the effective chance is below 100,
        /// so an effect that always lands (every effect at its default <c>Chance</c>, cast on a
        /// target with no resistance) draws nothing and replays exactly as before chance existed.
        /// Nothing else draws: the status itself, a damage-over-time snapshot and a knockback are
        /// all draw-free.
        /// </para>
        /// </summary>
        public static void Apply(SkillActivation activation, BattleUnit caster, System.Random rng, HexGrid grid)
        {
            if (activation == null || activation.Skill == null || caster == null || caster.IsDefeated)
            {
                return;
            }

            SkillInstance instance = activation.Instance ?? new SkillInstance(activation.Skill);
            IReadOnlyList<SkillEffect> effects = instance.Effects;
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
                        ApplyEffect(activation, caster, target, effects[e], instance.ScaleMagnitude(effects[e].Magnitude), rng, grid);
                    }
                }
            }
        }

        /// <summary>
        /// <see cref="Apply(SkillActivation, BattleUnit, System.Random)"/> on the deterministic
        /// fallback (no rng: every hit at a 100% roll, never a crit). For callers and tests that want
        /// exact, roll-free numbers; the battle loop always passes its rng.
        /// </summary>
        public static void Apply(SkillActivation activation, BattleUnit caster)
        {
            Apply(activation, caster, null);
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
        /// <see cref="BattleTurnExecutor.ExecuteTurn"/> calls this as the first step of every
        /// unit's turn, before that turn reads the unit's <see cref="BattleUnit.MoveRange"/> or
        /// fires anything, so a modifier on its last turn has already expired by then.
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

        /// <summary>
        /// Routes one effect to its handler. The whole of the effect vocabulary.
        /// <paramref name="activation"/> (for its skill's element and damage category, and to record
        /// the hit), <paramref name="caster"/> (for its attacking stat, crit chance, team, and a
        /// shield's or damage-over-time's source stats), <paramref name="rng"/> (damage rolls and
        /// chance checks) and <paramref name="grid"/> (knockback only) are passed through.
        /// <paramref name="magnitude"/> is the effect's <see cref="SkillEffect.Magnitude"/> already
        /// scaled for the skill's level; the handlers read it instead of the authored field.
        /// <para>
        /// Damage always lands. Every other effect first passes its chance check
        /// (<see cref="GetEffectiveChance"/>, <see cref="RollChance"/>) or does nothing to this
        /// target.
        /// </para>
        /// </summary>
        private static void ApplyEffect(SkillActivation activation, BattleUnit caster, BattleUnit target, SkillEffect effect, float magnitude, System.Random rng,
                                        HexGrid grid)
        {
            if (effect.EffectType == SkillEffectType.Damage)
            {
                ApplyDamage(activation, caster, target, effect, magnitude, rng);
                return;
            }

            if (!RollChance(GetEffectiveChance(effect, caster, target), rng))
            {
                return;
            }

            switch (effect.EffectType)
            {
                case SkillEffectType.Heal:
                    ApplyHeal(caster, target, magnitude);
                    break;

                case SkillEffectType.BuffStat:
                    ApplyStatChange(target, effect, magnitude, 1);
                    break;

                case SkillEffectType.DebuffStat:
                    ApplyStatChange(target, effect, magnitude, -1);
                    break;

                case SkillEffectType.ApplyStatus:
                    // Every status rule lives in StatusEffects; StatusType.None applies nothing.
                    StatusEffects.Apply(activation.Skill, caster, target, effect, magnitude, grid);
                    break;

                default:
                    // An effect type added to the enum without an arm here. Ignored rather than
                    // thrown, matching this namespace's non-throwing stance.
                    break;
            }
        }

        /// <summary>
        /// The percent chance a non-damage <paramref name="effect"/> lands on
        /// <paramref name="target"/>, in [0, 100]: <see cref="SkillEffect.Chance"/> (read as 100 when
        /// it is 0 or below, or above 100), and — for a <em>hostile</em> application, onto a target on
        /// the other team from <paramref name="caster"/> — multiplied by
        /// <c>(100 - target.StatusResist) / 100</c> in integers, truncating: an 85% taunt on a 50%
        /// resistant boss is 42%. Effects on the caster's own side are never resisted. A null
        /// effect has no chance.
        /// </summary>
        public static int GetEffectiveChance(SkillEffect effect, BattleUnit caster, BattleUnit target)
        {
            if (effect == null)
            {
                return 0;
            }

            int chance = effect.Chance <= 0 || effect.Chance > SkillEffect.AlwaysChance ? SkillEffect.AlwaysChance : effect.Chance;

            if (caster != null && target != null && target.Team != caster.Team)
            {
                chance = chance * (100 - target.StatusResist) / 100;
            }

            return chance < 0 ? 0 : chance;
        }

        /// <summary>
        /// The chance check: an effective chance of 100 or more always succeeds and draws nothing;
        /// below that it takes exactly one draw and succeeds when <c>rng.Next(100) &lt; chance</c>
        /// (so a chance of 0 draws and always fails). A null <paramref name="rng"/> is the
        /// deterministic fallback: only a certain effect lands — no luck either way, just as the
        /// fallback never crits.
        /// </summary>
        public static bool RollChance(int effectiveChance, System.Random rng)
        {
            if (effectiveChance >= SkillEffect.AlwaysChance)
            {
                return true;
            }

            return rng != null && rng.Next(100) < effectiveChance;
        }

        /// <summary>
        /// Spends HP, <see cref="SkillEffect.HitCount"/> times. Each hit's amount is
        /// <see cref="DamageFormula.Roll(BattleUnit, BattleUnit, SkillSO, float, System.Random, double)"/> of
        /// <paramref name="caster"/> against <paramref name="target"/>, with <paramref name="power"/>
        /// (the effect's <see cref="SkillEffect.Magnitude"/> scaled for the skill's level, see
        /// <see cref="SkillInstance.ScaleMagnitude"/>) as the power, <paramref name="rng"/> for the crit
        /// and variance rolls, and the execute multiplier
        /// (<see cref="DamageFormula.GetExecuteMultiplier"/>) at the target's HP as that hit lands.
        /// The roll is recorded on <paramref name="activation"/> (<see cref="SkillActivation.Hits"/>)
        /// and spent through <see cref="SpendHp"/>: shield first, then HP clamped into
        /// <c>[0, Stats.Hp]</c>, so an overkill hit lands the unit on exactly 0 rather than in
        /// negative territory that a later heal would have to climb out of. Hitting stops the moment
        /// the target is defeated; the hits that never happen draw nothing.
        /// <para>
        /// All of the arithmetic — stat selection, the element multiplier, the crit and variance
        /// rolls, the execute bonus and the single truncation to whole HP after them — lives in
        /// <see cref="DamageFormula"/>; this method only spends what it returns. A zero or negative
        /// power deals nothing, so a damage effect can no longer read as a heal. At the defaults (one
        /// hit, no execute bonus) this is exactly the original single roll.
        /// </para>
        /// </summary>
        private static void ApplyDamage(SkillActivation activation, BattleUnit caster, BattleUnit target, SkillEffect effect, float power, System.Random rng)
        {
            int hits = effect.HitCount < 1 ? 1 : effect.HitCount;

            for (int hit = 0; hit < hits && !target.IsDefeated; hit++)
            {
                double execute = DamageFormula.GetExecuteMultiplier(effect.ExecuteBonusPercent, target.CurrentHp, target.Stats.Hp);
                DamageRoll roll = DamageFormula.Roll(caster, target, activation.Skill, power, rng, execute);
                int absorbed = SpendHp(target, roll.Amount);
                activation.RecordHit(new DamageHit(target, roll, absorbed));
            }
        }

        /// <summary>
        /// Takes <paramref name="amount"/> off a unit: its <see cref="StatusType.Shield"/> soaks what
        /// it can first (see <see cref="StatusEffects"/>), the rest comes off HP through the clamp,
        /// and a unit brought to 0 HP is marked defeated. Returns what the shield absorbed. Every
        /// damage hit and every damage-over-time tick goes through here.
        /// <para>
        /// Setting <see cref="BattleUnit.IsDefeated"/> is an explicit step here, on purpose.
        /// <see cref="BattleUnit.CurrentHp"/> is a plain property with no side effects, so defeat
        /// is something the code that spends the HP decides and writes, visibly, at the one place
        /// it can happen — not a hidden consequence of a setter.
        /// </para>
        /// </summary>
        internal static int SpendHp(BattleUnit unit, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int absorbed = StatusEffects.Absorb(unit, amount);
            SetCurrentHp(unit, unit.CurrentHp - (amount - absorbed));

            if (unit.CurrentHp <= 0)
            {
                unit.IsDefeated = true;
            }

            return absorbed;
        }

        /// <summary>
        /// Restores <c>Magnitude / 100 x caster.Stats.SpecialAttack x </c><see cref="HealScale"/> HP,
        /// rounded to the nearest whole HP and clamped at <c>Stats.Hp</c> so healing cannot
        /// overfill a unit. <paramref name="magnitude"/> is already level-scaled
        /// (<see cref="SkillInstance.ScaleMagnitude"/>), so a heal grows with its skill level too.
        /// <para>
        /// <strong>Why SpecialAttack, and why nothing else.</strong> A flat heal was huge at level 1
        /// and negligible at level 100, because HP grows with level and the heal did not. The
        /// caster's <c>SpecialAttack</c> grows on the same growth curve as the target's HP, so a heal
        /// restores roughly the same <em>share</em> of HP at every level. The avatar is the caster of
        /// its passives, so a passive heal reads the avatar's <c>SpecialAttack</c>. There is no
        /// defense term (the target is a friend), and no crit and no variance roll: a heal takes no
        /// rng draws, so adding stat scaling did not change any battle's draw sequence.
        /// </para>
        /// <para>
        /// <strong>Rounded, not truncated</strong> (unlike damage and shields): at level 1 a beast's
        /// HP is 14-22 and a heal a handful of points, so truncation would cost a heal up to a whole
        /// HP, several percent of the target, and the heal's share of HP would no longer be the same
        /// at every level. Rounding keeps that error to half a point either way.
        /// </para>
        /// <para>
        /// Healing a <em>defeated</em> unit never reaches here: <c>Apply</c> skips a
        /// defeated target before the effect runs, so a heal cannot revive. That follows from
        /// there being no revival mechanic anywhere in the design — reviving would be a real
        /// combat rule with real balance weight, and having it fall out as a side effect of any
        /// heal that happens to be authored after a damage effect would be inventing it by
        /// accident. Defeated stays defeated until something is designed to undo it.
        /// </para>
        /// </summary>
        private static void ApplyHeal(BattleUnit caster, BattleUnit target, float magnitude)
        {
            SetCurrentHp(target, target.CurrentHp + GetHealAmount(caster, magnitude));
        }

        /// <summary>
        /// The HP a heal of (level-scaled) <paramref name="magnitude"/> cast by
        /// <paramref name="caster"/> restores: <c>Magnitude / 100 x SpecialAttack x </c>
        /// <see cref="HealScale"/>, rounded to the nearest whole HP (halves away from zero). A null
        /// caster heals nothing.
        /// </summary>
        public static int GetHealAmount(BattleUnit caster, float magnitude)
        {
            if (caster == null)
            {
                return 0;
            }

            return (int)Math.Round(magnitude * (double)caster.Stats.SpecialAttack * HealScale / 100.0, MidpointRounding.AwayFromZero);
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
        private static void ApplyStatChange(BattleUnit target, SkillEffect effect, float magnitude, int sign)
        {
            if (effect.DurationTurns > 0)
            {
                RemoveWeakestModifierAtCap(target, effect, effect.MaxStacks < 1 ? 1 : effect.MaxStacks);
            }

            int amount = effect.IsPercent
                ? (int)(target.Stats.GetStat(effect.AffectedStat) * (double)magnitude / 100.0)
                : ToAmount(magnitude);
            int applied = AddToStat(target, effect.AffectedStat, sign * amount);

            if (applied != 0 && effect.DurationTurns > 0)
            {
                target.ActiveStatModifiers.Add(new ActiveStatModifier(effect.AffectedStat, applied, effect.DurationTurns, effect));
            }
        }

        /// <summary>
        /// Makes room for one more timed copy of <paramref name="effect"/> on the unit: while it
        /// already carries <paramref name="cap"/> or more modifiers from that same authored effect,
        /// the one with the fewest turns left (the earliest applied on a tie) is reverted — exactly
        /// as if it had expired — and dropped. With the default cap of 1 a re-application therefore
        /// refreshes: the old copy comes out and the new one goes in, on a full duration. Copies
        /// from different effects never count against each other, and hand-built modifiers (no
        /// origin) are never touched.
        /// </summary>
        private static void RemoveWeakestModifierAtCap(BattleUnit target, SkillEffect effect, int cap)
        {
            List<ActiveStatModifier> active = target.ActiveStatModifiers;

            while (true)
            {
                int count = 0;
                int weakest = -1;

                for (int i = 0; i < active.Count; i++)
                {
                    if (active[i] == null || active[i].Origin != effect)
                    {
                        continue;
                    }

                    count++;

                    if (weakest < 0 || active[i].RemainingTurns < active[weakest].RemainingTurns)
                    {
                        weakest = i;
                    }
                }

                if (count < cap)
                {
                    return;
                }

                AddToStat(target, active[weakest].Stat, -active[weakest].Delta);
                active.RemoveAt(weakest);
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
        /// Every axis goes through here, <see cref="StatType.MoveRange"/> and
        /// <see cref="StatType.CritChance"/> included, so a move-range buff or debuff changes
        /// <see cref="BattleUnit.MoveRange"/> (which reads <see cref="BattleUnit.Stats"/>), and a
        /// crit buff changes the chance <see cref="DamageFormula.RollCrit"/> reads, with no special
        /// case. Crit chance is held at 0 like every other stat but is not capped at 100 here: a
        /// buff past 100 is kept in full and simply clamped when rolled, so its reversion is exact.
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
        /// under a negative authored heal magnitude that makes the heal read as damage.
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
        /// The whole-number amount an authored <see cref="SkillEffect.Magnitude"/> is worth to a
        /// stat change. HP and stats are integers while magnitude is a float, so authoring
        /// a 7.9 heal is worth 7: it truncates toward zero rather than rounding, which keeps a
        /// fractional magnitude from quietly buying a point it did not author. Damage does not come
        /// through here; <see cref="DamageFormula"/> applies the same truncation once, at the end
        /// of its own float math.
        /// </summary>
        private static int ToAmount(float magnitude)
        {
            return (int)magnitude;
        }
    }
}
