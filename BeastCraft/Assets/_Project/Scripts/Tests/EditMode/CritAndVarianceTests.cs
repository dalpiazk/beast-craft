using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Damage variance and critical hits: <see cref="DamageFormula"/> with explicit rolls, the
    /// null-rng deterministic fallback, the fixed draw order, crit chance clamping, the
    /// <see cref="StatType.CritChance"/> axis through the growth curve, buffs and gear, and the rng
    /// threading from <see cref="BattleTurnExecutor"/> through <see cref="SkillEffectApplier"/>.
    /// </summary>
    public class CritAndVarianceTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // The formula with explicit rolls. Power 39, A = D = 100: base = 0.39 * 100 * 100 / 200 = 19.5.
        // ---------------------------------------------------------------------------------------

        [TestCase(100, false, 19)] // 19.5
        [TestCase(90, false, 17)] // 17.55
        [TestCase(110, false, 21)] // 21.45
        [TestCase(100, true, 29)] // 29.25
        [TestCase(90, true, 26)] // 26.325
        [TestCase(110, true, 32)] // 32.175
        public void Compute_ExplicitRolls_KnownValues(int variancePercent, bool isCrit, int expected)
        {
            Assert.AreEqual(expected, DamageFormula.Compute(39f, 100, 100, ElementChart.Neutral, variancePercent, isCrit));
        }

        [Test]
        public void Compute_ElementCritAndVarianceMultiplyTogether()
        {
            // 19.5 * 2 * 1.5 * 1.1 = 64.35.
            Assert.AreEqual(64, DamageFormula.Compute(39f, 100, 100, ElementChart.Strong, 110, true));

            // 19.5 * 0.5 * 0.9 = 8.775.
            Assert.AreEqual(8, DamageFormula.Compute(39f, 100, 100, ElementChart.Weak, 90, false));
        }

        [Test]
        public void Compute_TruncatesOnceAtTheEnd()
        {
            // base 0.49 * 16 * 16 / 32 = 3.92: 3.92 * 1.5 * 0.9 = 5.292 -> 5. Truncating after each
            // step would give trunc(trunc(3.92) * 1.5) = 4, then trunc(4 * 0.9) = 3.
            Assert.AreEqual(5, DamageFormula.Compute(49f, 16, 16, ElementChart.Neutral, 90, true));
        }

        [Test]
        public void Compute_ZeroAttack_DealsTheFloorWhateverTheRolls()
        {
            // A = 0: base is exactly 0, so a crit, a high roll and a strong element all leave it at
            // the MinimumDamage floor.
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(500f, 0, 10, ElementChart.Strong, 110, true));
        }

        [Test]
        public void Compute_MinimumDamageFloorIsAppliedAfterTheRolls()
        {
            // A heavily resisted hit at the lowest roll still deals the floor.
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(1f, 1, 1000, ElementChart.Weak * ElementChart.Weak, DamageFormula.VarianceMinPercent, false));
        }

        [TestCase(0f)]
        [TestCase(-30f)]
        public void Compute_NonPositivePowerDealsNothingWhateverTheRolls(float power)
        {
            Assert.AreEqual(0, DamageFormula.Compute(power, 500, 1, ElementChart.Strong, DamageFormula.VarianceMaxPercent, true));
        }

        [TestCase(49f, 16, 16)]
        [TestCase(39f, 100, 100)]
        [TestCase(40f, 130, 170)]
        [TestCase(50f, 20, 1)]
        public void Compute_NeutralRollAndNoCrit_EqualsTheDeterministicOverload(float power, int attack, int defense)
        {
            foreach (float element in new[] { ElementChart.Weak, ElementChart.Neutral, ElementChart.Strong })
            {
                Assert.AreEqual(DamageFormula.Compute(power, attack, defense, element),
                                DamageFormula.Compute(power, attack, defense, element, DamageFormula.NeutralVariancePercent, false));
            }
        }

        [Test]
        public void Constants_MatchTheDesign()
        {
            Assert.AreEqual(90, DamageFormula.VarianceMinPercent);
            Assert.AreEqual(110, DamageFormula.VarianceMaxPercent);
            Assert.AreEqual(1.5f, DamageFormula.CritMultiplier);
            Assert.AreEqual(1.3f, DamageFormula.MinCritMultiplier);
        }

        [Test]
        public void CritMultiplier_IsFlooredAtMinCritMultiplier()
        {
            // max(1.3, 1.5 - reduction): no reduction exists yet, so the formula passes 0.
            Assert.AreEqual(DamageFormula.CritMultiplier, DamageFormula.GetCritMultiplier(0f));
            Assert.AreEqual(1.4f, DamageFormula.GetCritMultiplier(0.1f), 1e-6f);
            Assert.AreEqual(DamageFormula.MinCritMultiplier, DamageFormula.GetCritMultiplier(0.5f));
            Assert.AreEqual(DamageFormula.MinCritMultiplier, DamageFormula.GetCritMultiplier(10f));
        }

        // ---------------------------------------------------------------------------------------
        // The deterministic fallback and the draw order.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void NullRng_IsNoVarianceAndNoCrit()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 100), 50);
            BattleUnit target = Unit("t", new StatBlock(1000, 100, 100, 100, 100, 100), 50);
            SkillSO skill = DamageSkill(39f);

            DamageRoll roll = DamageFormula.Roll(caster, target, skill, 39f, null);

            Assert.IsFalse(roll.IsCrit, "a 100% crit chance must not crit without an rng");
            Assert.AreEqual(DamageFormula.NeutralVariancePercent, roll.VariancePercent);
            Assert.AreEqual(19, roll.Amount);
            Assert.AreEqual(19, DamageFormula.Compute(caster, target, skill, 39f));
            Assert.IsFalse(DamageFormula.RollCrit(100, null));
            Assert.AreEqual(DamageFormula.NeutralVariancePercent, DamageFormula.RollVariance(null));
        }

        [Test]
        public void ApplyWithoutRng_IsTheDeterministicFallback()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 100), 50);
            BattleUnit target = Unit("t", new StatBlock(1000, 100, 100, 100, 100, 100), 50);
            SkillActivation activation = new SkillActivation(DamageSkill(39f), new[] { target });

            SkillEffectApplier.Apply(activation, caster);

            Assert.AreEqual(1000 - 19, target.CurrentHp);
            Assert.AreEqual(1, activation.Hits.Count);
            Assert.IsFalse(activation.Hits[0].Roll.IsCrit);
        }

        [Test]
        public void Roll_DrawsCritThenVariance_ExactlyTwoDraws()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 50), 50);
            BattleUnit target = Unit("t", new StatBlock(1000, 100, 100, 100, 100, 100), 50);
            SkillSO skill = DamageSkill(39f);

            for (int seed = 0; seed < 50; seed++)
            {
                System.Random rng = new System.Random(seed);
                System.Random mirror = new System.Random(seed);

                DamageRoll roll = DamageFormula.Roll(caster, target, skill, 39f, rng);

                bool expectedCrit = mirror.Next(100) < 50;
                int expectedVariance = mirror.Next(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent + 1);
                Assert.AreEqual(expectedCrit, roll.IsCrit, "seed " + seed);
                Assert.AreEqual(expectedVariance, roll.VariancePercent, "seed " + seed);
                Assert.AreEqual(DamageFormula.Compute(39f, 100, 100, ElementChart.Neutral, expectedVariance, expectedCrit), roll.Amount, "seed " + seed);
                Assert.AreEqual(mirror.Next(), rng.Next(), "seed " + seed + ": the roll must take exactly two draws");
            }
        }

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(-20)]
        [TestCase(250)]
        public void RollCrit_AlwaysTakesOneDraw(int chance)
        {
            System.Random rng = new System.Random(7);
            System.Random mirror = new System.Random(7);

            DamageFormula.RollCrit(chance, rng);
            mirror.Next(100);

            Assert.AreEqual(mirror.Next(), rng.Next());
        }

        [Test]
        public void RollVariance_StaysInRange_AndReachesBothEnds()
        {
            System.Random rng = new System.Random(3);
            bool sawMin = false;
            bool sawMax = false;

            for (int i = 0; i < 5000; i++)
            {
                int roll = DamageFormula.RollVariance(rng);
                Assert.That(roll, Is.InRange(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent));
                sawMin |= roll == DamageFormula.VarianceMinPercent;
                sawMax |= roll == DamageFormula.VarianceMaxPercent;
            }

            Assert.IsTrue(sawMin && sawMax, "both ends of the roll are inclusive");
        }

        // ---------------------------------------------------------------------------------------
        // Crit chance clamping.
        // ---------------------------------------------------------------------------------------

        [TestCase(-5, 0)]
        [TestCase(0, 0)]
        [TestCase(7, 7)]
        [TestCase(100, 100)]
        [TestCase(150, 100)]
        public void ClampCritChance_ClampsIntoAPercent(int chance, int expected)
        {
            Assert.AreEqual(expected, DamageFormula.ClampCritChance(chance));
        }

        [TestCase(-10, false)]
        [TestCase(0, false)]
        [TestCase(100, true)]
        [TestCase(250, true)]
        public void RollCrit_OutOfRangeChancesAreClamped(int chance, bool alwaysCrits)
        {
            System.Random rng = new System.Random(11);

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(alwaysCrits, DamageFormula.RollCrit(chance, rng));
            }
        }

        [Test]
        public void RollCrit_RateTracksTheChance()
        {
            Assert.That(CritRate(10, 4000, 5), Is.InRange(8.0, 12.0));
            Assert.That(CritRate(50, 4000, 6), Is.InRange(46.0, 54.0));
        }

        // ---------------------------------------------------------------------------------------
        // The CritChance axis.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void StatType_CritChanceIsAppended()
        {
            Assert.AreEqual(6, (int)StatType.MoveRange);
            Assert.AreEqual(7, (int)StatType.CritChance);
        }

        [Test]
        public void StatBlock_CritChanceDefaultsToZero_RoundTrips_AndSums()
        {
            Assert.AreEqual(0, new StatBlock(10, 1, 2, 3, 4, 5, 3).CritChance);

            StatBlock block = new StatBlock(10, 1, 2, 3, 4, 5, 3, 12);
            Assert.AreEqual(12, block.GetStat(StatType.CritChance));

            block.SetStat(StatType.CritChance, 20);
            Assert.AreEqual(20, block.CritChance);
            Assert.AreEqual(3, block.MoveRange);

            StatBlock sum = new StatBlock(1, 1, 1, 1, 1, 1, 1, 5) + new StatBlock(0, 0, 0, 0, 0, 0, 0, 10);
            Assert.AreEqual(15, sum.CritChance);
        }

        [Test]
        public void GetStatAtLevel_CritChanceIgnoresGrowthCurve()
        {
            // Curve runs 0 -> 1: every scaled stat is 0 at level 1.
            CreatureSpeciesSO species = Species(new StatBlock(100, 50, 0, 0, 0, 0, 3, 12), AnimationCurve.Linear(0f, 0f, 1f, 1f));

            Assert.AreEqual(0, species.GetStatAtLevel(StatType.Attack, 1));
            Assert.AreEqual(12, species.GetStatAtLevel(StatType.CritChance, 1));
            Assert.AreEqual(12, species.GetStatAtLevel(StatType.CritChance, 6));
            Assert.AreEqual(12, species.GetStatAtLevel(StatType.CritChance, 11));
            Assert.AreEqual(12, StatCalculator.GetBaseStatsAtLevel(species, 1).CritChance);
        }

        [Test]
        public void GetStatAtLevel_CritChanceWithoutGrowthRate_ReturnsBase()
        {
            CreatureSpeciesSO species = new CreatureSpeciesSO();
            _created.Add(species);
            species.BaseStats = new StatBlock(10, 0, 0, 0, 0, 0, 2, 8);

            Assert.AreEqual(8, species.GetStatAtLevel(StatType.CritChance, 50));
        }

        [Test]
        public void Gear_ModifiesCritChance_FlatThenPercent()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 0, 0, 0, 0, 3, 5), AnimationCurve.Linear(0f, 1f, 1f, 2f));

            // Exempt from the curve at every level: 5 + 10 = 15, then x(1 + 1.0) = 30.
            Assert.AreEqual(15, StatCalculator.ComputeStats(species, 11, new[] { Gear(Mod(StatType.CritChance, 10, 0f)) }).CritChance);
            Assert.AreEqual(30, StatCalculator.ComputeStats(species, 6, new[] { Gear(Mod(StatType.CritChance, 10, 1f)) }).CritChance);
        }

        [Test]
        public void Gear_CritChanceFloorsAtZero_AndIsNotCappedAtAssembly()
        {
            StatBlock cursed = StatCalculator.ComputeStats(new StatBlock(10, 0, 0, 0, 0, 0, 3, 5), new[] { Mod(StatType.CritChance, -20, 0f) });
            StatBlock stacked = StatCalculator.ComputeStats(new StatBlock(10, 0, 0, 0, 0, 0, 3, 90), new[] { Mod(StatType.CritChance, 30, 0f) });

            Assert.AreEqual(0, cursed.CritChance, "crit chance takes the ordinary 0 floor, not the HP floor of 1");
            Assert.AreEqual(120, stacked.CritChance, "clamping to 100 happens when rolled, not when assembled");
            Assert.AreEqual(100, DamageFormula.ClampCritChance(stacked.CritChance));
        }

        [Test]
        public void BuffStat_OnCritChance_RaisesTheCritRate_AndRevertsOnExpiry()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 10), 50);

            SkillEffectApplier.Apply(new SkillActivation(StatSkill(SkillEffectType.BuffStat, 40f, 2), new[] { caster }), caster);

            Assert.AreEqual(50, caster.Stats.CritChance);
            double buffed = CritRateOf(caster, 4000, 21);
            Assert.That(buffed, Is.InRange(46.0, 54.0), "the buffed caster crits about half the time");

            SkillEffectApplier.TickModifiers(caster);
            SkillEffectApplier.TickModifiers(caster);

            Assert.AreEqual(10, caster.Stats.CritChance);
            Assert.That(CritRateOf(caster, 4000, 22), Is.InRange(8.0, 12.0), "back to the base rate once the buff expires");
        }

        [Test]
        public void BuffStat_OnCritChance_ToOneHundred_CritsEveryHit()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 0), 50);
            BattleUnit target = Unit("t", new StatBlock(100000, 100, 100, 100, 100, 100), 50);
            System.Random rng = new System.Random(5);

            SkillEffectApplier.Apply(new SkillActivation(StatSkill(SkillEffectType.BuffStat, 100f, 0), new[] { caster }), caster, rng);

            for (int i = 0; i < 200; i++)
            {
                SkillActivation strike = new SkillActivation(DamageSkill(39f), new[] { target });
                SkillEffectApplier.Apply(strike, caster, rng);
                Assert.IsTrue(strike.Hits[0].Roll.IsCrit, "hit " + i);
            }
        }

        [Test]
        public void DebuffStat_OnCritChance_ClampsAtZero_AndRevertsOnlyWhatItTook()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 5), 50);

            SkillEffectApplier.Apply(new SkillActivation(StatSkill(SkillEffectType.DebuffStat, 20f, 1), new[] { caster }), caster);

            Assert.AreEqual(0, caster.Stats.CritChance);
            Assert.AreEqual(0.0, CritRateOf(caster, 500, 9));

            SkillEffectApplier.TickModifiers(caster);

            Assert.AreEqual(5, caster.Stats.CritChance);
        }

        // ---------------------------------------------------------------------------------------
        // The applier and the executor.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Apply_RecordsHitsTargetMajor_AndHealsTakeNoDraws()
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 30), 50);
            BattleUnit first = Unit("t1", new StatBlock(1000, 100, 100, 100, 100, 100), 50);
            BattleUnit second = Unit("t2", new StatBlock(1000, 100, 100, 100, 100, 100), 50);
            SkillSO skill = DamageSkill(39f);
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 3f });
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 20f });
            SkillActivation activation = new SkillActivation(skill, new[] { first, second });
            System.Random rng = new System.Random(13);
            System.Random mirror = new System.Random(13);

            SkillEffectApplier.Apply(activation, caster, rng);

            Assert.AreEqual(4, activation.Hits.Count);
            Assert.AreSame(first, activation.Hits[0].Target);
            Assert.AreSame(first, activation.Hits[1].Target);
            Assert.AreSame(second, activation.Hits[2].Target);
            Assert.AreSame(second, activation.Hits[3].Target);

            int expectedFirst = 1000;
            int expectedSecond = 1000;
            for (int h = 0; h < 4; h++)
            {
                bool crit = mirror.Next(100) < 30;
                int variance = mirror.Next(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent + 1);
                float power = h % 2 == 0 ? 39f : 20f;
                int amount = DamageFormula.Compute(power, 100, 100, ElementChart.Neutral, variance, crit);
                Assert.AreEqual(crit, activation.Hits[h].Roll.IsCrit, "hit " + h);
                Assert.AreEqual(amount, activation.Hits[h].Roll.Amount, "hit " + h);

                if (h < 2)
                {
                    expectedFirst -= amount;
                }
                else
                {
                    expectedSecond -= amount;
                }

                if (h % 2 == 0)
                {
                    // The heal between the two damage effects: 3% of the caster's SpecialAttack 100, no roll.
                    if (h < 2)
                    {
                        expectedFirst += 3;
                    }
                    else
                    {
                        expectedSecond += 3;
                    }
                }
            }

            Assert.AreEqual(expectedFirst, first.CurrentHp);
            Assert.AreEqual(expectedSecond, second.CurrentHp);
            Assert.AreEqual(mirror.Next(), rng.Next(), "exactly two draws per damage effect, none for the heals");
        }

        [Test]
        public void Heal_ScalesWithSpecialAttack_AndTakesNoDraws()
        {
            // SpecialAttack 200: a magnitude-7 heal is 7% of 200 = 14 HP.
            BattleUnit target = Unit("t", new StatBlock(100, 1, 1, 200, 1, 1), 1);
            target.CurrentHp = 50;
            SkillSO heal = new SkillSO();
            heal.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 7f });
            _created.Add(heal);
            System.Random rng = new System.Random(2);
            System.Random mirror = new System.Random(2);

            SkillEffectApplier.Apply(new SkillActivation(heal, new[] { target }), target, rng);

            Assert.AreEqual(64, target.CurrentHp);
            Assert.AreEqual(mirror.Next(), rng.Next(), "a heal takes no draws");
        }

        [Test]
        public void SameSeed_GivesTheSameDamageSequence_DifferentSeedDoesNot()
        {
            List<int> a = DamageSequence(99);
            List<int> b = DamageSequence(99);
            List<int> c = DamageSequence(100);

            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, c);
        }

        [Test]
        public void ExecuteTurn_ThreadsTheBattleRngIntoDamage()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit attacker = new BattleUnit("a", BattleTeam.Player, new StatBlock(100, 100, 100, 100, 100, 100, 3, 100), new HexCoordinate(0, 1),
                                                 new SkillLoadout(new[] { MeleeSkill() }), null, 50);
            BattleUnit defender = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000, 100, 100, 100, 100, 100, 3), new HexCoordinate(0, 0), null, null, 50);
            Assert.IsTrue(grid.TryPlaceUnit(attacker.Id, attacker.Position));
            Assert.IsTrue(grid.TryPlaceUnit(defender.Id, defender.Position));

            BattleTurnResult result = BattleTurnExecutor.ExecuteTurn(attacker, new[] { attacker, defender }, grid, new System.Random(4), null);

            Assert.AreEqual(1, result.SkillOutcomes.Count);
            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            IReadOnlyList<DamageHit> hits = result.SkillOutcomes[0].Activation.Hits;
            Assert.AreEqual(1, hits.Count);
            Assert.IsTrue(hits[0].Roll.IsCrit, "a 100% crit chance crits once the battle rng reaches the formula");
            Assert.That(hits[0].Roll.VariancePercent, Is.InRange(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent));
            Assert.AreEqual(1000 - hits[0].Roll.Amount, defender.CurrentHp);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        private static double CritRate(int chance, int rolls, int seed)
        {
            System.Random rng = new System.Random(seed);
            int crits = 0;
            for (int i = 0; i < rolls; i++)
            {
                crits += DamageFormula.RollCrit(chance, rng) ? 1 : 0;
            }

            return (100.0 * crits) / rolls;
        }

        private double CritRateOf(BattleUnit caster, int hits, int seed)
        {
            BattleUnit target = Unit("t", new StatBlock(int.MaxValue, 100, 100, 100, 100, 100), 50);
            SkillSO skill = DamageSkill(39f);
            System.Random rng = new System.Random(seed);
            int crits = 0;

            for (int i = 0; i < hits; i++)
            {
                crits += DamageFormula.Roll(caster, target, skill, 39f, rng).IsCrit ? 1 : 0;
            }

            return (100.0 * crits) / hits;
        }

        private List<int> DamageSequence(int seed)
        {
            BattleUnit caster = Unit("c", new StatBlock(100, 100, 100, 100, 100, 100, 3, 25), 50);
            BattleUnit target = Unit("t", new StatBlock(1000000, 100, 100, 100, 100, 100), 50);
            SkillSO skill = DamageSkill(39f);
            System.Random rng = new System.Random(seed);
            List<int> amounts = new List<int>();

            for (int i = 0; i < 50; i++)
            {
                SkillActivation activation = new SkillActivation(skill, new[] { target });
                SkillEffectApplier.Apply(activation, caster, rng);
                amounts.Add(activation.Hits[0].Roll.Amount);
            }

            return amounts;
        }

        private SkillSO DamageSkill(float power)
        {
            SkillSO skill = new SkillSO();
            skill.Category = DamageCategory.Physical;
            skill.Element = Element.None;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power });
            _created.Add(skill);
            return skill;
        }

        private SkillSO MeleeSkill()
        {
            SkillSO skill = DamageSkill(39f);
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.Range = 1;
            skill.Cooldown = 1;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            return skill;
        }

        private SkillSO StatSkill(SkillEffectType type, float magnitude, int duration)
        {
            SkillSO skill = new SkillSO();
            skill.Effects.Add(new SkillEffect { EffectType = type, AffectedStat = StatType.CritChance, Magnitude = magnitude, DurationTurns = duration });
            _created.Add(skill);
            return skill;
        }

        private CreatureSpeciesSO Species(StatBlock baseStats, AnimationCurve curve)
        {
            GrowthRateCurve growth = new GrowthRateCurve();
            growth.Curve = curve;
            growth.MaxLevel = 11;
            _created.Add(growth);

            CreatureSpeciesSO species = new CreatureSpeciesSO();
            species.BaseStats = baseStats;
            species.GrowthRate = growth;
            _created.Add(species);
            return species;
        }

        private GearSO Gear(params StatModifier[] modifiers)
        {
            GearSO gear = new GearSO();
            gear.MinimumLevel = 1;
            gear.Modifiers.AddRange(modifiers);
            _created.Add(gear);
            return gear;
        }

        private static StatModifier Mod(StatType stat, int flat, float percent)
        {
            return new StatModifier { Stat = stat, FlatBonus = flat, PercentBonus = percent };
        }

        private static BattleUnit Unit(string id, StatBlock stats, int level)
        {
            return new BattleUnit(id, BattleTeam.Enemy, stats, HexCoordinate.Zero, null, null, level);
        }
    }
}
