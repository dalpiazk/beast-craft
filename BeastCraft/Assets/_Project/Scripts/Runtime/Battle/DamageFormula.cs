using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// How much HP one <see cref="SkillEffectType.Damage"/> effect takes off one target: the
    /// effect's power as a percentage of the caster's attacking stat, mitigated by
    /// <c>A / (A + D)</c> against the target's defending stat, then the element chart, crit and
    /// variance. Adopted from Sword x Staff's damage pipeline; see
    /// <c>docs/balance/research-sword-x-staff.md</c> and the design doc, "Damage formula".
    /// <para>
    /// <strong>The formula, in full:</strong>
    /// <code>
    /// base   = Power / 100 * A * A / (A + DefenseWeight * D) * GlobalScale
    /// damage = max(MinimumDamage, truncate(base * element * crit * roll / 100))
    /// </code>
    /// where <c>crit</c> is <see cref="GetCritMultiplier"/> on a critical hit and 1 otherwise, and
    /// <c>roll</c> is a whole percent drawn uniformly from
    /// [<see cref="VarianceMinPercent"/>, <see cref="VarianceMaxPercent"/>].
    /// <list type="bullet">
    /// <item><description>
    /// <c>Power</c> is the authored <see cref="SkillEffect.Magnitude"/> of the damage effect, read
    /// as <strong>a percentage of the attacking stat</strong>: Power 120 is 120% of the caster's
    /// <c>Attack</c> (or <c>SpecialAttack</c>) before mitigation.
    /// </description></item>
    /// <item><description>
    /// <c>A</c> and <c>D</c> are picked by the skill's <see cref="SkillSO.Category"/>:
    /// <see cref="DamageCategory.Physical"/> is the caster's <c>Attack</c> against the target's
    /// <c>Defense</c>, <see cref="DamageCategory.Special"/> its <c>SpecialAttack</c> against the
    /// target's <c>SpecialDefense</c>. Both are read from <see cref="BattleUnit.Stats"/> at the
    /// moment the effect lands — the <em>current effective</em> stats, so a buff or debuff folded
    /// into them by <see cref="SkillEffectApplier"/> moves the damage.
    /// </description></item>
    /// <item><description>
    /// <strong>Mitigation</strong> is <c>A / (A + DefenseWeight * D)</c>: a share of the hit in
    /// (0, 1], never a subtraction, so defence has smooth diminishing returns (doubling Defense
    /// against an equal Attack takes the share from 1/2 to 1/3, not to zero) and a hit is never
    /// negated outright. <c>A</c> therefore appears twice — as the base and in the mitigation
    /// term — so Attack is worth a little more than linear, as in the reference.
    /// </description></item>
    /// <item><description>
    /// <strong>Level is not in the formula.</strong> Stats already scale with level through the
    /// growth curve, and with <c>A</c>, <c>D</c> and HP all on the same curve, a hit between two
    /// equally levelled beasts takes the same share of HP at every level (up to integer
    /// rounding). <see cref="BattleUnit.Level"/> is kept — other systems read it — but the damage
    /// formula ignores it.
    /// </description></item>
    /// <item><description>
    /// The element multiplier is
    /// <see cref="ElementChart.GetMultiplier(Element, System.Collections.Generic.IReadOnlyList{Element})"/>
    /// of the skill's <see cref="SkillSO.Element"/> against the target's
    /// <see cref="BattleUnit.Elements"/>. The caster's own elements still play no part.
    /// </description></item>
    /// <item><description>
    /// <strong>Critical hit.</strong> The caster's <see cref="StatBlock.CritChance"/> (current
    /// effective, so a <see cref="SkillEffectType.BuffStat"/> on
    /// <see cref="StatType.CritChance"/> or crit gear counts), clamped into [0, 100], is the percent
    /// chance that <c>rng.Next(100) &lt; chance</c>; see <see cref="RollCrit"/>. A crit multiplies
    /// the hit by <see cref="GetCritMultiplier"/>, which is <see cref="CritMultiplier"/> today. The
    /// target plays no part (there is no crit-damage or crit-resist stat yet).
    /// </description></item>
    /// <item><description>
    /// <strong>Variance.</strong> Every hit is then scaled by a whole-percent roll,
    /// <c>rng.Next(VarianceMinPercent, VarianceMaxPercent + 1)</c>; see <see cref="RollVariance"/>.
    /// Integer percents keep the roll exact and reproducible from a seed; a 100% roll is applied as
    /// the identity, so it reproduces the pre-variance number bit for bit.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>The random draws, in a fixed order.</strong> Each damage effect that lands on one
    /// target (a live caster, target and skill) takes exactly two draws from the battle's
    /// <see cref="System.Random"/>: first the crit roll, then the variance roll. Always both, even
    /// when the crit chance is 0 or 100 or the power is 0, so the number of draws never depends on
    /// stats and a seeded battle replays identically. <see cref="SkillEffectApplier"/> walks
    /// targets then effects in its documented order, so the draw sequence of a whole battle is fixed
    /// by the seed. Heals and stat changes take no draws.
    /// </para>
    /// <para>
    /// <strong>The deterministic fallback.</strong> A <c>null</c> rng means no variance (a 100%
    /// roll) and no crit, and takes no draws. That is what
    /// <see cref="Compute(BattleUnit, BattleUnit, SkillSO, float)"/> and the raw four-argument
    /// <see cref="Compute(float, int, int, float)"/> compute, what
    /// <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit)"/> uses, and what tests
    /// needing exact numbers rely on. Tests that need a specific roll pass it explicitly through
    /// <see cref="Compute(float, int, int, float, int, bool)"/>.
    /// </para>
    /// <para>
    /// <strong>Arithmetic.</strong> The base is computed in double precision (IEEE basic
    /// operations only, so it is reproducible) and truncated toward zero to whole HP once, at the
    /// very end, after the element, crit and variance multipliers (in that order), so a fractional
    /// result never buys a point it did not earn. The guards: a <c>Power</c> of 0 or less deals 0 (a
    /// zero-power damage effect is a no-op, and a negative one never reads as a heal); any positive
    /// <c>Power</c> deals at least <see cref="MinimumDamage"/>, so a resisted hit is never worth
    /// nothing; a negative <c>A</c> or <c>D</c> is treated as 0. A <c>D</c> of 0 needs no guard
    /// (the mitigation is then exactly 1), and <c>A = 0</c> makes the base exactly 0 without
    /// dividing. Stats are clamped at 0 on every path that writes them, so the stat guards are for
    /// hand-built blocks.
    /// </para>
    /// <para>
    /// <strong>A zero attacking stat deals the <see cref="MinimumDamage"/> floor.</strong> With
    /// <c>A = 0</c> the base is 0, so a unit with no attacking stat — notably the avatar built by the
    /// zero-stat <see cref="BattleAvatar.Create(SkillLoadout, string)"/> — deals exactly
    /// <see cref="MinimumDamage"/> per positive-power damage effect, whatever the element. (The old
    /// level-term formula's +2 offset let it chip 2, times the element.) An avatar meant to hit
    /// harder should be given stats.
    /// </para>
    /// <para>
    /// <strong>Tunable starting default, not confirmed balance.</strong> The shape follows the
    /// reference; <see cref="DefenseWeight"/> and <see cref="GlobalScale"/> start at 1 and the kit
    /// powers were rescaled so a neutral hit between two average level-50 roster beasts removes the
    /// same share of HP as under the previous formula (see the tuning log). <strong>Still
    /// deferred:</strong> a same-element bonus (STAB), flat skill damage, damage boost/resistance
    /// and a crit-damage stat. Healing scales with the caster's <c>SpecialAttack</c> but takes no
    /// defense, crit or variance, and stat changes still move stats by their authored magnitude;
    /// neither goes through here (see <see cref="SkillEffectApplier.HealScale"/>).
    /// </para>
    /// <para>
    /// Pure and static, like <see cref="ElementChart"/> and <see cref="StatCalculator"/>: a
    /// function of its arguments that mutates nothing, and non-throwing — a null caster, target or
    /// skill yields 0 rather than an exception.
    /// </para>
    /// </summary>
    public static class DamageFormula
    {
        /// <summary>
        /// Divisor that turns <c>Power</c> into a fraction of the attacking stat: Power is authored
        /// in percent, so Power 100 is 100% of <c>A</c>.
        /// </summary>
        public const double PowerPercent = 100.0;

        /// <summary>
        /// Weight of the defending stat in the mitigation term <c>A / (A + DefenseWeight * D)</c>.
        /// At 1, equal Attack and Defense halve a hit. Raising it makes Defense worth more against
        /// every Attack. Must stay non-negative.
        /// </summary>
        public const double DefenseWeight = 1.0;

        /// <summary>
        /// Uniform multiplier on every hit, the lever for overall fight length without touching
        /// every skill's power. 1 today: the kit powers themselves were rescaled instead.
        /// </summary>
        public const double GlobalScale = 1.0;

        /// <summary>
        /// The least any positive-power hit deals, after truncation and the element multiplier.
        /// Because the floor is applied <em>after</em> the multiplier, a 0x "immune" matchup would
        /// still deal this much. <see cref="ElementChart"/> only returns 2x, 1.25x, 0.5x and 1x
        /// today; if an immunity is ever added, <see cref="Compute(float, int, int, float, int, bool)"/>
        /// must special-case a zero multiplier to return 0 (a crit or a high roll must not lift it
        /// off 0 either).
        /// </summary>
        public const int MinimumDamage = 1;

        /// <summary>Lowest variance roll, in whole percent (inclusive).</summary>
        public const int VarianceMinPercent = 90;

        /// <summary>Highest variance roll, in whole percent (inclusive).</summary>
        public const int VarianceMaxPercent = 110;

        /// <summary>The roll that leaves a hit unchanged; the deterministic fallback's roll.</summary>
        public const int NeutralVariancePercent = 100;

        /// <summary>What a critical hit multiplies the hit by, after the element multiplier.</summary>
        public const float CritMultiplier = 1.5f;

        /// <summary>
        /// The floor of the crit multiplier, from the reference's
        /// <c>max(1.3, 1 + critDamage - critDamageReduction)</c>: however much crit-damage
        /// reduction a target stacks, a crit is always worth at least this. There is no crit-damage
        /// or crit-damage-reduction stat yet, so today the floor never binds; it is here so that
        /// when one lands, it lands through <see cref="GetCritMultiplier"/> with the floor already
        /// in place.
        /// </summary>
        public const float MinCritMultiplier = 1.3f;

        /// <summary>The crit chance is clamped to at least this (percent) when rolled.</summary>
        public const int MinCritChance = 0;

        /// <summary>The crit chance is clamped to at most this (percent) when rolled: 100 always crits.</summary>
        public const int MaxCritChance = 100;

        /// <summary>
        /// The damage <paramref name="caster"/> deals to <paramref name="target"/> with one damage
        /// effect of <paramref name="power"/> belonging to <paramref name="skill"/>, on the
        /// deterministic fallback: no variance and no crit. The stats are picked by
        /// <see cref="SkillSO.Category"/> and the element multiplier is the skill's element against
        /// the target's. A null caster, target or skill deals 0.
        /// </summary>
        public static int Compute(BattleUnit caster, BattleUnit target, SkillSO skill, float power)
        {
            return Roll(caster, target, skill, power, null).Amount;
        }

        /// <summary>
        /// One damage effect landing, with its random rolls: the crit roll then the variance roll
        /// are drawn from <paramref name="rng"/> (exactly two draws, in that order, whatever the
        /// stats), and the result reports both alongside the amount. A null
        /// <paramref name="rng"/> is the deterministic fallback: a 100% roll, no crit, no draws. A
        /// null caster, target or skill deals 0 and takes no draws.
        /// </summary>
        public static DamageRoll Roll(BattleUnit caster, BattleUnit target, SkillSO skill, float power, System.Random rng)
        {
            return Roll(caster, target, skill, power, rng, 1.0);
        }

        /// <summary>
        /// <see cref="Roll(BattleUnit, BattleUnit, SkillSO, float, System.Random)"/> with an extra
        /// multiplier applied last, before truncation — the execute bonus (see
        /// <see cref="GetExecuteMultiplier"/>). Exactly 1 is the identity and is not multiplied at
        /// all, so it reproduces the plain roll bit for bit. Same draws, in the same order.
        /// </summary>
        public static DamageRoll Roll(BattleUnit caster, BattleUnit target, SkillSO skill, float power, System.Random rng, double bonusMultiplier)
        {
            if (caster == null || target == null || skill == null)
            {
                return new DamageRoll(0, false, NeutralVariancePercent);
            }

            bool isCrit = RollCrit(caster.Stats.CritChance, rng);
            int variancePercent = RollVariance(rng);

            int attack = GetAttackStat(caster.Stats, skill.Category);
            int defense = GetDefenseStat(target.Stats, skill.Category);
            float multiplier = ElementChart.GetMultiplier(skill.Element, target.Elements);

            return new DamageRoll(Compute(power, attack, defense, multiplier, variancePercent, isCrit, bonusMultiplier), isCrit, variancePercent);
        }

        /// <summary>
        /// The execute multiplier for a hit carrying <paramref name="executeBonusPercent"/> against a
        /// target at <paramref name="currentHp"/> of <paramref name="maxHp"/>:
        /// <c>1 + bonus / 100 * (maxHp - currentHp) / maxHp</c>. Linear in missing HP — 1 at full
        /// health, <c>1 + bonus/100</c> at 0 HP (a 100% bonus is ×1.5 at half HP, ×2 at 0 HP; a
        /// living target never quite reaches the maximum). Exactly 1 (the identity) when the bonus is 0 or below, the maximum
        /// is 0 or below, or the target is at full health; current HP is clamped into [0, max].
        /// </summary>
        public static double GetExecuteMultiplier(int executeBonusPercent, int currentHp, int maxHp)
        {
            if (executeBonusPercent <= 0 || maxHp <= 0)
            {
                return 1.0;
            }

            int current = currentHp < 0 ? 0 : currentHp > maxHp ? maxHp : currentHp;
            int missing = maxHp - current;

            if (missing == 0)
            {
                return 1.0;
            }

            return 1.0 + ((double)executeBonusPercent * missing / (100.0 * maxHp));
        }

        /// <summary>
        /// The formula on raw numbers on the deterministic fallback (a 100% roll, no crit), with
        /// every guard this class documents: the whole HP a hit is worth once
        /// <paramref name="elementMultiplier"/> has been applied to <see cref="ComputeBase"/> and the
        /// result truncated.
        /// </summary>
        public static int Compute(float power, int attack, int defense, float elementMultiplier)
        {
            return Compute(power, attack, defense, elementMultiplier, NeutralVariancePercent, false);
        }

        /// <summary>
        /// The formula on raw numbers with explicit rolls: the deterministic path tests use to pin
        /// a specific variance roll or crit. Order of operations:
        /// <c>base * elementMultiplier * (isCrit ? GetCritMultiplier(0) : 1) * variancePercent / 100</c>,
        /// then truncated, then floored at <see cref="MinimumDamage"/>; a <paramref name="power"/>
        /// of 0 or less deals 0 before any of it. <paramref name="variancePercent"/> is taken as
        /// given (it is not clamped into the roll range, so a test can probe outside it), except
        /// that a negative value is treated as 0; exactly <see cref="NeutralVariancePercent"/> is
        /// the identity, applied as no multiplication at all so it is bit-exact.
        /// </summary>
        public static int Compute(float power, int attack, int defense, float elementMultiplier, int variancePercent, bool isCrit)
        {
            return Compute(power, attack, defense, elementMultiplier, variancePercent, isCrit, 1.0);
        }

        /// <summary>
        /// <see cref="Compute(float, int, int, float, int, bool)"/> with
        /// <paramref name="bonusMultiplier"/> (the execute bonus) applied after the variance roll and
        /// before truncation. Exactly 1 is not multiplied at all, so it is bit-exact with the
        /// six-argument form; a negative value is treated as 0.
        /// </summary>
        public static int Compute(float power, int attack, int defense, float elementMultiplier, int variancePercent, bool isCrit, double bonusMultiplier)
        {
            if (power <= 0f)
            {
                return 0;
            }

            double scaled = ComputeBase(power, attack, defense) * elementMultiplier;

            if (isCrit)
            {
                scaled *= GetCritMultiplier(0f);
            }

            if (variancePercent != NeutralVariancePercent)
            {
                scaled = scaled * (variancePercent < 0 ? 0 : variancePercent) / 100.0;
            }

            if (bonusMultiplier != 1.0)
            {
                scaled *= bonusMultiplier < 0.0 ? 0.0 : bonusMultiplier;
            }

            int damage = (int)scaled;

            return damage < MinimumDamage ? MinimumDamage : damage;
        }

        /// <summary>
        /// The un-truncated base, before the element multiplier:
        /// <c>Power / 100 * A * A / (A + DefenseWeight * D) * GlobalScale</c>. Exposed so the
        /// balance simulator and tests can read the curve without re-deriving it. Applies the
        /// attack and defense guards (negative reads as 0; <c>A = 0</c> is exactly 0) but not the
        /// power guard, which belongs to the final amount (see
        /// <see cref="Compute(float, int, int, float, int, bool)"/>).
        /// </summary>
        public static double ComputeBase(float power, int attack, int defense)
        {
            if (attack <= 0)
            {
                return 0.0;
            }

            double a = attack;
            double d = defense < 0 ? 0.0 : defense;

            // One division, last: with integral stats and power every product is exact in a double,
            // so a hit whose true value is a whole number never truncates to one less.
            return power * a * a * GlobalScale / ((a + (DefenseWeight * d)) * PowerPercent);
        }

        /// <summary>
        /// The multiplier a critical hit applies: <c>max(MinCritMultiplier, CritMultiplier -
        /// critDamageReduction)</c>, the reference's <c>max(1.3, 1 + critDamage - reduction)</c>
        /// with today's fixed crit damage. Nothing supplies a reduction yet, so the formula always
        /// passes 0 and a crit is exactly <see cref="CritMultiplier"/>.
        /// </summary>
        public static float GetCritMultiplier(float critDamageReduction)
        {
            float multiplier = CritMultiplier - critDamageReduction;
            return multiplier < MinCritMultiplier ? MinCritMultiplier : multiplier;
        }

        /// <summary>
        /// <paramref name="critChance"/> clamped into [<see cref="MinCritChance"/>,
        /// <see cref="MaxCritChance"/>]: the chance actually rolled against.
        /// </summary>
        public static int ClampCritChance(int critChance)
        {
            if (critChance < MinCritChance)
            {
                return MinCritChance;
            }

            return critChance > MaxCritChance ? MaxCritChance : critChance;
        }

        /// <summary>
        /// The crit roll: <c>rng.Next(100) &lt; ClampCritChance(critChance)</c>. Always takes
        /// exactly one draw when <paramref name="rng"/> is not null, even at 0 or 100, so the draw
        /// count never depends on the stat. A null <paramref name="rng"/> never crits and draws
        /// nothing.
        /// </summary>
        public static bool RollCrit(int critChance, System.Random rng)
        {
            if (rng == null)
            {
                return false;
            }

            return rng.Next(100) < ClampCritChance(critChance);
        }

        /// <summary>
        /// The variance roll: a whole percent uniform on [<see cref="VarianceMinPercent"/>,
        /// <see cref="VarianceMaxPercent"/>], one draw. A null <paramref name="rng"/> returns
        /// <see cref="NeutralVariancePercent"/> and draws nothing.
        /// </summary>
        public static int RollVariance(System.Random rng)
        {
            return rng == null ? NeutralVariancePercent : rng.Next(VarianceMinPercent, VarianceMaxPercent + 1);
        }

        /// <summary>
        /// The caster-side stat <paramref name="category"/> reads: <c>Attack</c> or
        /// <c>SpecialAttack</c>. Any value other than <see cref="DamageCategory.Special"/> — including
        /// one cast past the end of the enum — reads as <see cref="DamageCategory.Physical"/>, the
        /// default, matching this namespace's non-throwing stance. The same holds for
        /// <see cref="GetDefenseStat"/>.
        /// </summary>
        public static int GetAttackStat(StatBlock stats, DamageCategory category)
        {
            return category == DamageCategory.Special ? stats.SpecialAttack : stats.Attack;
        }

        /// <summary>The target-side stat <paramref name="category"/> reads: <c>Defense</c> or <c>SpecialDefense</c>.</summary>
        public static int GetDefenseStat(StatBlock stats, DamageCategory category)
        {
            return category == DamageCategory.Special ? stats.SpecialDefense : stats.Defense;
        }
    }

    /// <summary>
    /// One damage effect's result on one target, as <see cref="DamageFormula.Roll"/> computed it:
    /// the amount and the two rolls behind it. A small value record, so a caller can report a crit
    /// without re-deriving it.
    /// </summary>
    public readonly struct DamageRoll
    {
        public DamageRoll(int amount, bool isCrit, int variancePercent)
        {
            Amount = amount;
            IsCrit = isCrit;
            VariancePercent = variancePercent;
        }

        /// <summary>The whole HP the hit is worth, before it is clamped to the target's remaining HP.</summary>
        public int Amount { get; }

        /// <summary>Whether the crit roll succeeded (as drawn, even on a zero-power hit that deals 0).</summary>
        public bool IsCrit { get; }

        /// <summary>The variance roll in whole percent; <see cref="DamageFormula.NeutralVariancePercent"/> on the deterministic fallback.</summary>
        public int VariancePercent { get; }
    }
}
