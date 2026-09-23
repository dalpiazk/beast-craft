using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// How much HP one <see cref="SkillEffectType.Damage"/> effect takes off one target: a
    /// Pokémon-style formula of the caster's level, the effect's power, the caster's attacking stat
    /// against the target's defending stat, and the element chart.
    /// <para>
    /// <strong>The formula, in full:</strong>
    /// <code>
    /// base   = ((2 * Level / 5 + 2) * Power * A / D) / 50 + 2
    /// damage = max(MinimumDamage, truncate(base * element * crit * roll / 100))
    /// </code>
    /// where <c>crit</c> is <see cref="CritMultiplier"/> on a critical hit and 1 otherwise, and
    /// <c>roll</c> is a whole percent drawn uniformly from
    /// [<see cref="VarianceMinPercent"/>, <see cref="VarianceMaxPercent"/>].
    /// <list type="bullet">
    /// <item><description>
    /// <c>Level</c> is the caster's <see cref="BattleUnit.Level"/>. <c>Power</c> is the authored
    /// <see cref="SkillEffect.Magnitude"/> of the damage effect.
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
    /// The element multiplier is
    /// <see cref="ElementChart.GetMultiplier(Element, System.Collections.Generic.IReadOnlyList{Element})"/>
    /// of the skill's <see cref="SkillSO.Element"/> against the target's
    /// <see cref="BattleUnit.Elements"/>, applied to the whole of <c>base</c> (the +2 included).
    /// The caster's own elements still play no part.
    /// </description></item>
    /// <item><description>
    /// <strong>Critical hit.</strong> The caster's <see cref="StatBlock.CritChance"/> (current
    /// effective, so a <see cref="SkillEffectType.BuffStat"/> on
    /// <see cref="StatType.CritChance"/> or crit gear counts), clamped into [0, 100], is the percent
    /// chance that <c>rng.Next(100) &lt; chance</c>; see <see cref="RollCrit"/>. A crit multiplies
    /// the hit by <see cref="CritMultiplier"/>. The target plays no part (there is no crit
    /// resistance yet).
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
    /// <see cref="Compute(BattleUnit, BattleUnit, SkillSO, float)"/> and the raw five-argument
    /// <see cref="Compute(int, float, int, int, float)"/> compute, what
    /// <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit)"/> uses, and what tests
    /// needing exact numbers rely on. Tests that need a specific roll pass it explicitly through
    /// <see cref="Compute(int, float, int, int, float, int, bool)"/>.
    /// </para>
    /// <para>
    /// <strong>Why the level term.</strong> <c>A / D</c> is level-invariant between two equally
    /// levelled beasts — both sides' stats grow along the same curve — while HP grows with level.
    /// The <c>(2 * Level / 5 + 2)</c> term grows damage alongside it, so that a hit between evenly
    /// matched beasts takes a roughly similar fraction of HP at level 1 and at level 100, and a
    /// fight does not get longer just because both sides levelled. It holds only loosely with the
    /// authored <c>medium</c> curve (HP grows about 6.7x from level 1 to 100, the level term 17.5x,
    /// and the +2 floor dominates at level 1); pinning it down is the balance simulator's job.
    /// </para>
    /// <para>
    /// <strong>Arithmetic.</strong> Everything is float math, truncated toward zero to whole HP
    /// once, at the very end, after the element, crit and variance multipliers (in that order) — the
    /// same truncation stance
    /// <see cref="SkillEffectApplier"/> has always taken, so a fractional result never buys a point
    /// it did not earn. The guards: a <c>Power</c> of 0 or less deals 0 (a zero-power damage effect
    /// is a no-op, and a negative one no longer reads as a heal); any positive <c>Power</c> deals at
    /// least <see cref="MinimumDamage"/>, so a resisted hit is never worth nothing;
    /// <c>D &lt;= 0</c> is treated as 1 so a zero-defence target cannot divide by zero; and a
    /// negative <c>A</c> is treated as 0. <c>Level</c> below 1 is treated as 1. Stats are clamped
    /// at 0 on every path that writes them, so the two stat guards are for hand-built blocks.
    /// </para>
    /// <para>
    /// <strong>A zero attacking stat still deals the +2 floor.</strong> With <c>A = 0</c> the
    /// first term vanishes and <c>base</c> is exactly 2, so a unit with no attacking stat — notably
    /// the avatar built by the zero-stat <see cref="BattleAvatar.Create(SkillLoadout, string)"/> —
    /// deals 2 times the element multiplier per damage effect whatever the authored power. That is
    /// deliberate: it keeps the formula free of special cases, and a chip of 2 is the honest
    /// reading of "no attacking stat". An avatar meant to hit harder should be given stats.
    /// </para>
    /// <para>
    /// <strong>Tunable starting default, not confirmed balance.</strong> The shape and every
    /// constant below are first-pass engineering defaults for the balance simulator to measure
    /// against. Variance and crits are in (a user decision; see
    /// <c>docs/balance/research-crit-variance-speed.md</c> for the research behind the numbers).
    /// A battle is now random, but reproducible from its seed, which is what the balance simulator
    /// relies on. <strong>Still deferred:</strong> a same-element bonus (STAB), a balance lever to
    /// add once there are fights to measure it on. Healing is still flat and takes no variance, and
    /// stat changes still move stats by their authored magnitude; neither goes through here.
    /// </para>
    /// <para>
    /// Pure and static, like <see cref="ElementChart"/> and <see cref="StatCalculator"/>: a
    /// function of its arguments that mutates nothing, and non-throwing — a null caster, target or
    /// skill yields 0 rather than an exception.
    /// </para>
    /// </summary>
    public static class DamageFormula
    {
        /// <summary>Multiplier on the caster's level in the level term (the <c>2</c> in <c>2 * Level / 5</c>).</summary>
        public const float LevelScale = 2f;

        /// <summary>Divisor of the level term (the <c>5</c> in <c>2 * Level / 5</c>).</summary>
        public const float LevelDivisor = 5f;

        /// <summary>Added to the level term, so a level-1 caster's term is 2.4 rather than 0.4.</summary>
        public const float LevelOffset = 2f;

        /// <summary>Divisor applied to <c>level term * Power * A / D</c>.</summary>
        public const float PowerDivisor = 50f;

        /// <summary>Added to every hit before the element multiplier; the floor a zero-attack hit lands at.</summary>
        public const float BaseOffset = 2f;

        /// <summary>
        /// The least any positive-power hit deals, after truncation and the element multiplier.
        /// Because the floor is applied <em>after</em> the multiplier, a 0x "immune" matchup would
        /// still deal this much. <see cref="ElementChart"/> only returns 2x, 0.5x and 1x today; if
        /// an immunity is ever added, <see cref="Compute(int, float, int, int, float, int, bool)"/>
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

        /// <summary>The crit chance is clamped to at least this (percent) when rolled.</summary>
        public const int MinCritChance = 0;

        /// <summary>The crit chance is clamped to at most this (percent) when rolled: 100 always crits.</summary>
        public const int MaxCritChance = 100;

        /// <summary>
        /// The damage <paramref name="caster"/> deals to <paramref name="target"/> with one damage
        /// effect of <paramref name="power"/> belonging to <paramref name="skill"/>, on the
        /// deterministic fallback: no variance and no crit. The stats are picked by
        /// <see cref="SkillSO.Category"/>, the level is the caster's, and the element multiplier is
        /// the skill's element against the target's. A null caster, target or skill deals 0.
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
            if (caster == null || target == null || skill == null)
            {
                return new DamageRoll(0, false, NeutralVariancePercent);
            }

            bool isCrit = RollCrit(caster.Stats.CritChance, rng);
            int variancePercent = RollVariance(rng);

            int attack = GetAttackStat(caster.Stats, skill.Category);
            int defense = GetDefenseStat(target.Stats, skill.Category);
            float multiplier = ElementChart.GetMultiplier(skill.Element, target.Elements);

            return new DamageRoll(Compute(caster.Level, power, attack, defense, multiplier, variancePercent, isCrit), isCrit, variancePercent);
        }

        /// <summary>
        /// The formula on raw numbers on the deterministic fallback (a 100% roll, no crit), with
        /// every guard this class documents: the whole HP a hit is worth once
        /// <paramref name="elementMultiplier"/> has been applied to <see cref="ComputeBase"/> and the
        /// result truncated.
        /// </summary>
        public static int Compute(int level, float power, int attack, int defense, float elementMultiplier)
        {
            return Compute(level, power, attack, defense, elementMultiplier, NeutralVariancePercent, false);
        }

        /// <summary>
        /// The formula on raw numbers with explicit rolls: the deterministic path tests use to pin
        /// a specific variance roll or crit. Order of operations:
        /// <c>base * elementMultiplier * (isCrit ? CritMultiplier : 1) * variancePercent / 100</c>,
        /// then truncated, then floored at <see cref="MinimumDamage"/>; a <paramref name="power"/>
        /// of 0 or less deals 0 before any of it. <paramref name="variancePercent"/> is taken as
        /// given (it is not clamped into the roll range, so a test can probe outside it), except
        /// that a negative value is treated as 0; exactly <see cref="NeutralVariancePercent"/> is
        /// the identity, applied as no multiplication at all so it is bit-exact.
        /// </summary>
        public static int Compute(int level, float power, int attack, int defense, float elementMultiplier, int variancePercent, bool isCrit)
        {
            if (power <= 0f)
            {
                return 0;
            }

            float scaled = ComputeBase(level, power, attack, defense) * elementMultiplier;

            if (isCrit)
            {
                scaled *= CritMultiplier;
            }

            if (variancePercent != NeutralVariancePercent)
            {
                scaled = scaled * (variancePercent < 0 ? 0 : variancePercent) / 100f;
            }

            int damage = (int)scaled;

            return damage < MinimumDamage ? MinimumDamage : damage;
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
        /// The un-truncated <c>base</c> term, before the element multiplier. Exposed so the
        /// balance simulator and tests can read the curve without re-deriving it. Applies the
        /// level, attack and defense guards but not the power guard, which belongs to the final
        /// amount (see <see cref="Compute(int, float, int, int, float, int, bool)"/>).
        /// </summary>
        public static float ComputeBase(int level, float power, int attack, int defense)
        {
            float l = level < 1 ? 1f : level;
            float a = attack < 0 ? 0f : attack;
            float d = defense <= 0 ? 1f : defense;

            float levelTerm = (LevelScale * l / LevelDivisor) + LevelOffset;

            return (levelTerm * power * a / d / PowerDivisor) + BaseOffset;
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
