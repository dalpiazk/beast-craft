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
    /// damage = truncate(base * ElementChart multiplier)
    /// </code>
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
    /// </list>
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
    /// once, at the very end, after the element multiplier — the same truncation stance
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
    /// against. <strong>Deliberately deferred:</strong> random variance, critical hits and a
    /// same-element bonus (STAB). The first two would make a battle non-deterministic, and the
    /// headless balance simulator needs a fixed roster and seed to give the same answer every
    /// time; the third is a balance lever to add once there are fights to measure it on. Healing is
    /// still flat and stat changes still move stats by their authored magnitude; neither goes
    /// through here.
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
        /// an immunity is ever added, <see cref="Compute(int, float, int, int, float)"/> must
        /// special-case a zero multiplier to return 0.
        /// </summary>
        public const int MinimumDamage = 1;

        /// <summary>
        /// The damage <paramref name="caster"/> deals to <paramref name="target"/> with one damage
        /// effect of <paramref name="power"/> belonging to <paramref name="skill"/>: the stats are
        /// picked by <see cref="SkillSO.Category"/>, the level is the caster's, and the element
        /// multiplier is the skill's element against the target's. A null caster, target or skill
        /// deals 0.
        /// </summary>
        public static int Compute(BattleUnit caster, BattleUnit target, SkillSO skill, float power)
        {
            if (caster == null || target == null || skill == null)
            {
                return 0;
            }

            int attack = GetAttackStat(caster.Stats, skill.Category);
            int defense = GetDefenseStat(target.Stats, skill.Category);
            float multiplier = ElementChart.GetMultiplier(skill.Element, target.Elements);

            return Compute(caster.Level, power, attack, defense, multiplier);
        }

        /// <summary>
        /// The formula on raw numbers, with every guard this class documents: the whole HP a hit
        /// is worth once <paramref name="elementMultiplier"/> has been applied to
        /// <see cref="ComputeBase"/> and the result truncated.
        /// </summary>
        public static int Compute(int level, float power, int attack, int defense, float elementMultiplier)
        {
            if (power <= 0f)
            {
                return 0;
            }

            int damage = (int)(ComputeBase(level, power, attack, defense) * elementMultiplier);

            return damage < MinimumDamage ? MinimumDamage : damage;
        }

        /// <summary>
        /// The un-truncated <c>base</c> term, before the element multiplier. Exposed so the
        /// balance simulator and tests can read the curve without re-deriving it. Applies the
        /// level, attack and defense guards but not the power guard, which belongs to the final
        /// amount (see <see cref="Compute(int, float, int, int, float)"/>).
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
}
