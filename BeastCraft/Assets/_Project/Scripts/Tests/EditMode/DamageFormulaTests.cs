using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="DamageFormula"/> on raw numbers and through <see cref="SkillEffectApplier"/>.
    /// Expected values are hand-computed from
    /// <c>Power / 100 * A * A / (A + D)</c> (DefenseWeight and GlobalScale both 1), times the
    /// element multiplier, truncated.
    /// </summary>
    public class DamageFormulaTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        // 1.0 * 100 * 100 / 200 = 50.
        [TestCase(100f, 100, 100, 50)]
        // 1.2 * 100 * 100 / 200 = 60.
        [TestCase(120f, 100, 100, 60)]
        // 0.5 * 30 * 30 / 50 = 9.
        [TestCase(50f, 30, 20, 9)]
        // 0.68 * 59 * 59 / 116 = 20.41 (the simulator's Blast between average level-50 beasts).
        [TestCase(68f, 59, 57, 20)]
        // 1.0 * 130 * 130 / 300 = 56.33.
        [TestCase(100f, 130, 170, 56)]
        // 1.0 * 100 * 100 / 400 = 25.
        [TestCase(100f, 100, 300, 25)]
        public void Compute_KnownValues(float power, int attack, int defense, int expected)
        {
            Assert.AreEqual(expected, DamageFormula.Compute(power, attack, defense, ElementChart.Neutral));
        }

        [Test]
        public void ComputeBase_IsUntruncated()
        {
            Assert.AreEqual(7.5, DamageFormula.ComputeBase(100f, 15, 15), 1e-9);
            Assert.AreEqual(3.92, DamageFormula.ComputeBase(49f, 16, 16), 1e-9);
            Assert.AreEqual(56.3333333, DamageFormula.ComputeBase(100f, 130, 170), 1e-6);
        }

        [Test]
        public void Constants_MatchTheDesign()
        {
            Assert.AreEqual(100.0, DamageFormula.PowerPercent);
            Assert.AreEqual(1.0, DamageFormula.DefenseWeight);
            Assert.AreEqual(1.0, DamageFormula.GlobalScale);
            Assert.AreEqual(1, DamageFormula.MinimumDamage);
        }

        [Test]
        public void Power_IsAPercentOfTheAttackingStat_BeforeMitigation()
        {
            // Against zero Defense the mitigation is exactly 1, so the hit is Power% of A.
            Assert.AreEqual(100, DamageFormula.Compute(100f, 100, 0, ElementChart.Neutral));
            Assert.AreEqual(120, DamageFormula.Compute(120f, 100, 0, ElementChart.Neutral));
            Assert.AreEqual(45, DamageFormula.Compute(45f, 100, 0, ElementChart.Neutral));
        }

        [Test]
        public void Mitigation_HasDiminishingReturns()
        {
            // A = 100 against D = 0, 100, 200, 300: shares 1, 1/2, 1/3, 1/4. Each extra 100 Defense
            // buys less, and no amount of Defense negates the hit.
            Assert.AreEqual(100, DamageFormula.Compute(100f, 100, 0, ElementChart.Neutral));
            Assert.AreEqual(50, DamageFormula.Compute(100f, 100, 100, ElementChart.Neutral));
            Assert.AreEqual(33, DamageFormula.Compute(100f, 100, 200, ElementChart.Neutral));
            Assert.AreEqual(25, DamageFormula.Compute(100f, 100, 300, ElementChart.Neutral));
            Assert.AreEqual(9, DamageFormula.Compute(100f, 100, 1000, ElementChart.Neutral));
        }

        [Test]
        public void Attack_CountsTwice_SoDoublingItMoreThanDoublesTheHit()
        {
            // 100 vs 100: 50. 200 vs 100: 200 * 200 / 300 = 133.33, 2.67x rather than 2x.
            Assert.AreEqual(50, DamageFormula.Compute(100f, 100, 100, ElementChart.Neutral));
            Assert.AreEqual(133, DamageFormula.Compute(100f, 200, 100, ElementChart.Neutral));
        }

        [Test]
        public void Physical_UsesAttackAgainstDefense_SpecialUsesSpecialAttackAgainstSpecialDefense()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 100, 1, 50, 1, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 1, 100, 1, 25, 0), 10);

            // Physical: 100 * 100 / 200 = 50. Special: 50 * 50 / 75 = 33.33.
            Assert.AreEqual(50, DamageFormula.Compute(caster, target, Skill(DamageCategory.Physical, Element.None, 100f), 100f));
            Assert.AreEqual(33, DamageFormula.Compute(caster, target, Skill(DamageCategory.Special, Element.None, 100f), 100f));
        }

        [Test]
        public void Category_DefaultsToPhysical()
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            _created.Add(skill);

            Assert.AreEqual(DamageCategory.Physical, skill.Category);
            Assert.AreEqual(0, (int)DamageCategory.Physical);
            Assert.AreEqual(1, (int)DamageCategory.Special);
        }

        [Test]
        public void ElementMultiplier_IsAppliedToTheUntruncatedBase()
        {
            // Base 7.5: x2 then truncate is 15; truncate then x2 would be 14.
            Assert.AreEqual(15, DamageFormula.Compute(100f, 15, 15, ElementChart.Strong));
            // Base 7.5 x0.5 = 3.75, truncated to 3.
            Assert.AreEqual(3, DamageFormula.Compute(100f, 15, 15, ElementChart.Weak));
        }

        [Test]
        public void ElementMultiplier_ComesFromTheSkillAgainstTheTarget()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10, Element.Water);
            BattleUnit target = Unit("target", new StatBlock(100, 20, 20, 20, 20, 0), 10, Element.Nature, Element.Metal);

            // Base 1.0 * 20 * 20 / 40 = 10; Fire is strong against both Nature and Metal: x4.
            Assert.AreEqual(40, DamageFormula.Compute(caster, target, Skill(DamageCategory.Physical, Element.Fire, 100f), 100f));
        }

        [Test]
        public void PositivePower_DealsAtLeastOne()
        {
            // Base ~0.00001, x0.25, truncated to 0, floored to 1.
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(1f, 1, 1000, ElementChart.Weak * ElementChart.Weak));
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        public void ZeroOrNegativePower_DealsNothing(float power)
        {
            Assert.AreEqual(0, DamageFormula.Compute(power, 500, 1, ElementChart.Strong));
        }

        [Test]
        public void NegativeDamageMagnitude_DoesNotHeal()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 50, 50, 50, 50, 0), 50);
            BattleUnit target = Unit("target", new StatBlock(100, 50, 50, 50, 50, 0), 50);
            target.CurrentHp = 40;

            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.None, -30f), new[] { target }), caster);

            Assert.AreEqual(40, target.CurrentHp);
        }

        [Test]
        public void ZeroOrNegativeDefense_MeansNoMitigation()
        {
            int atZero = DamageFormula.Compute(100f, 20, 0, ElementChart.Neutral);

            // 1.0 * 20 * 20 / 20 = 20: Power% of A, unmitigated.
            Assert.AreEqual(20, atZero);
            Assert.AreEqual(atZero, DamageFormula.Compute(100f, 20, -5, ElementChart.Neutral));
        }

        [Test]
        public void ZeroOrNegativeAttack_DealsTheMinimumDamageFloor()
        {
            Assert.AreEqual(0.0, DamageFormula.ComputeBase(500f, 0, 10));
            Assert.AreEqual(0.0, DamageFormula.ComputeBase(500f, -20, 10));
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(500f, 0, 10, ElementChart.Neutral));
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(500f, -20, 10, ElementChart.Neutral));
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(500f, 0, 0, ElementChart.Strong));
        }

        [Test]
        public void LevelMultiplier_EqualLevels_IsTheIdentity_BitExact()
        {
            for (int level = 1; level <= 100; level++)
            {
                Assert.AreEqual(1.0, DamageFormula.GetLevelMultiplier(level, level));
            }

            // Equal levels at 1 and at 100 hit exactly as the formula without the level term.
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 100f);
            Assert.AreEqual(10, DamageFormula.Compute(Unit("a", new StatBlock(100, 20, 20, 20, 20, 0), 1), Unit("b", new StatBlock(100, 20, 20, 20, 20, 0), 1), skill, 100f));
            Assert.AreEqual(10, DamageFormula.Compute(Unit("a", new StatBlock(100, 20, 20, 20, 20, 0), 100), Unit("b", new StatBlock(100, 20, 20, 20, 20, 0), 100), skill, 100f));

            // The eight-argument form at exactly 1 is the seven-argument form, bit for bit.
            float[] powers = { 1f, 49f, 68f, 100f, 137.5f };
            int[] stats = { 0, 7, 59, 130, 999 };
            float[] elements = { 0.5f, 1f, 1.25f, 2f };

            foreach (float power in powers)
            {
                foreach (int attack in stats)
                {
                    foreach (int defense in stats)
                    {
                        foreach (float element in elements)
                        {
                            Assert.AreEqual(DamageFormula.Compute(power, attack, defense, element, 97, true, 1.3),
                                            DamageFormula.Compute(power, attack, defense, element, 97, true, 1.3, 1.0));
                        }
                    }
                }
            }
        }

        [Test]
        public void LevelMultiplier_OneLevel_IsOnePlusOrMinusK()
        {
            double k = DamageFormula.LevelDifferencePerLevel + DamageFormula.LevelDifferenceConvex;

            Assert.AreEqual(1.0 + k, DamageFormula.GetLevelMultiplier(51, 50), 1e-12);
            Assert.AreEqual(1.0 - k, DamageFormula.GetLevelMultiplier(49, 50), 1e-12);
            Assert.AreEqual(1.0 + k, DamageFormula.GetLevelMultiplier(2, 1), 1e-12);
        }

        [Test]
        public void LevelMultiplier_IsMonotone_AndClampedAtTheCap()
        {
            Assert.AreEqual(1.0 + DamageFormula.LevelDifferenceCap, DamageFormula.GetLevelMultiplier(100, 1), 1e-12);
            Assert.AreEqual(1.0 - DamageFormula.LevelDifferenceCap, DamageFormula.GetLevelMultiplier(1, 100), 1e-12);

            double previous = 0.0;

            for (int attacker = 1; attacker <= 100; attacker++)
            {
                double multiplier = DamageFormula.GetLevelMultiplier(attacker, 50);
                Assert.GreaterOrEqual(multiplier, previous);
                Assert.GreaterOrEqual(multiplier, 1.0 - DamageFormula.LevelDifferenceCap - 1e-12);
                Assert.LessOrEqual(multiplier, 1.0 + DamageFormula.LevelDifferenceCap + 1e-12);
                previous = multiplier;
            }
        }

        [Test]
        public void LevelDifference_AppliesInBothDirections()
        {
            StatBlock stats = new StatBlock(1000, 100, 100, 100, 100, 0);
            BattleUnit high = Unit("high", stats, 55);
            BattleUnit low = Unit("low", stats, 50);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 100f);

            // Base 1.0 * 100 * 100 / 200 = 50, times the level multiplier, truncated once.
            int up = DamageFormula.Compute(high, low, skill, 100f);
            int down = DamageFormula.Compute(low, high, skill, 100f);

            Assert.AreEqual((int)(50.0 * DamageFormula.GetLevelMultiplier(55, 50)), up);
            Assert.AreEqual((int)(50.0 * DamageFormula.GetLevelMultiplier(50, 55)), down);
            Assert.Greater(up, 50);
            Assert.Less(down, 50);
        }

        [Test]
        public void LevelMultiplier_IsAppliedToTheUntruncatedHit_TruncatedOnce()
        {
            // 1.0 * 130 * 130 / 300 = 56.333; x1.035 = 58.305 -> 58. Truncating first would give
            // 56 x 1.035 = 57.96 -> 57.
            Assert.AreEqual(58, DamageFormula.Compute(100f, 130, 170, ElementChart.Neutral, DamageFormula.NeutralVariancePercent, false, 1.0, 1.035));

            // With element, crit, roll and execute: 56.333 x 1.25 x 1.5 x 0.93 x 1.1 x 0.9 = 97.29.
            Assert.AreEqual(97, DamageFormula.Compute(100f, 130, 170, 1.25f, 93, true, 1.1, 0.9));

            // A negative multiplier reads as 0 and lands on the floor; any positive power deals at least 1.
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(100f, 130, 170, ElementChart.Neutral, 100, false, 1.0, -1.0));
        }

        [Test]
        public void LevelDifference_NullRngPath_CarriesIt()
        {
            StatBlock stats = new StatBlock(1000, 100, 100, 100, 100, 0);
            BattleUnit high = Unit("high", stats, 60);
            BattleUnit low = Unit("low", stats, 50);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 100f);
            int expected = (int)(50.0 * DamageFormula.GetLevelMultiplier(60, 50));

            Assert.AreEqual(expected, DamageFormula.Compute(high, low, skill, 100f));
            Assert.AreEqual(expected, DamageFormula.Roll(high, low, skill, 100f, null).Amount);

            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { low }), high);
            Assert.AreEqual(1000 - expected, low.CurrentHp);
        }

        [Test]
        public void LevelDifference_TakesNoRandomDraws()
        {
            StatBlock stats = new StatBlock(1000, 100, 100, 100, 100, 30);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 100f);
            System.Random uneven = new System.Random(1234);
            System.Random even = new System.Random(1234);

            DamageRoll a = DamageFormula.Roll(Unit("a", stats, 90), Unit("b", stats, 10), skill, 100f, uneven);
            DamageRoll b = DamageFormula.Roll(Unit("a", stats, 10), Unit("b", stats, 10), skill, 100f, even);

            Assert.AreEqual(b.IsCrit, a.IsCrit);
            Assert.AreEqual(b.VariancePercent, a.VariancePercent);
            Assert.Greater(a.Amount, b.Amount);
            Assert.AreEqual(even.Next(), uneven.Next(), "the level term draws nothing, so both streams are in step");
        }

        [Test]
        public void LevelDifference_DamageOverTimeCarriesIt()
        {
            BattleUnit caster = new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 100, 10, 10, 10, 10, 3), HexCoordinate.Zero, null, null, 60);
            BattleUnit target = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(500, 10, 100, 10, 10, 10, 3), HexCoordinate.Zero, null, null, 50);
            SkillSO burn = ScriptableObject.CreateInstance<SkillSO>();
            burn.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 50f, DurationTurns = 2 });
            _created.Add(burn);

            SkillEffectApplier.Apply(new SkillActivation(burn, new[] { target }), caster);
            BattleTurnResult first = BattleTurnExecutor.ExecuteTurn(target, new List<BattleUnit> { caster, target }, null, null, null);

            // 50% power, A 100 vs D 100 = 25, times the caster's ten-level advantage.
            Assert.AreEqual((int)(25.0 * DamageFormula.GetLevelMultiplier(60, 50)), first.StatusDamage);
            Assert.Greater(first.StatusDamage, 25);
        }

        [Test]
        public void LevelDifference_HealsAndShieldsIgnoreIt()
        {
            SkillSO support = ScriptableObject.CreateInstance<SkillSO>();
            support.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 50f });
            support.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 40f, DurationTurns = 2 });
            _created.Add(support);

            int[] healed = new int[2];
            int[] shields = new int[2];
            int[] casterLevels = { 50, 90 };

            for (int i = 0; i < 2; i++)
            {
                BattleUnit caster = new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 10, 50, 60, 10, 10, 3), HexCoordinate.Zero, null, null, casterLevels[i]);
                BattleUnit ally = new BattleUnit("p2", BattleTeam.Player, new StatBlock(200, 10, 10, 10, 10, 10, 3), HexCoordinate.Zero, null, null, 50);
                ally.CurrentHp = 100;

                SkillEffectApplier.Apply(new SkillActivation(support, new[] { ally }), caster);
                healed[i] = ally.CurrentHp;
                shields[i] = StatusEffects.ShieldPoints(ally);
            }

            Assert.Greater(healed[0], 100, "test setup: the heal lands");
            Assert.Greater(shields[0], 0, "test setup: the shield lands");
            Assert.AreEqual(healed[0], healed[1]);
            Assert.AreEqual(shields[0], shields[1]);
        }

        [Test]
        public void LevelBelowOne_IsStoredAsOne()
        {
            Assert.AreEqual(1, Unit("u", new StatBlock(10, 0, 0, 0, 0, 0), 0).Level);
            Assert.AreEqual(1, new BattleUnit("u", BattleTeam.Enemy, new StatBlock(10, 0, 0, 0, 0, 0), HexCoordinate.Zero).Level);
        }

        [Test]
        public void CasterAttackBuff_RaisesLaterDamage()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 20, 20, 20, 20, 0), 10);
            SkillSO strike = Skill(DamageCategory.Physical, Element.None, 100f);
            SkillSO special = Skill(DamageCategory.Special, Element.None, 100f);
            SkillSO buff = StatSkill(SkillEffectType.BuffStat, StatType.Attack, 20f);

            SkillEffectApplier.Apply(new SkillActivation(buff, new[] { caster }), caster);
            SkillEffectApplier.Apply(new SkillActivation(strike, new[] { target }), caster);

            // Attack 40 vs Defense 20: 40 * 40 / 60 = 26.67.
            Assert.AreEqual(474, target.CurrentHp);

            // An Attack buff does nothing for a special skill: 20 * 20 / 40 = 10.
            SkillEffectApplier.Apply(new SkillActivation(special, new[] { target }), caster);
            Assert.AreEqual(464, target.CurrentHp);
        }

        [Test]
        public void TargetDefenseDebuff_RaisesDamage()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 20, 20, 20, 20, 0), 10);

            SkillEffectApplier.Apply(new SkillActivation(StatSkill(SkillEffectType.DebuffStat, StatType.Defense, 10f), new[] { target }), caster);
            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.None, 100f), new[] { target }), caster);

            // Attack 20 vs Defense 10: 20 * 20 / 30 = 13.33.
            Assert.AreEqual(487, target.CurrentHp);
        }

        [Test]
        public void StatfulAvatar_DealsDamageFromItsOwnStats()
        {
            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(50, 5, 5, 40, 5, 0), null, BattleAvatar.DefaultId, 30);
            BattleUnit enemy = Unit("enemy", new StatBlock(500, 20, 20, 20, 20, 0), 30);

            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Special, Element.None, 100f), new[] { enemy }), avatar);

            // SpecialAttack 40 vs SpecialDefense 20: 40 * 40 / 60 = 26.67.
            Assert.AreEqual(474, enemy.CurrentHp);
        }

        [Test]
        public void ZeroStatAvatar_DealsTheFloorWhateverThePowerOrElement()
        {
            BattleUnit avatar = BattleAvatar.Create(null);
            BattleUnit neutral = Unit("neutral", new StatBlock(500, 20, 20, 20, 20, 0), 50);
            BattleUnit weakToFire = Unit("nature", new StatBlock(500, 20, 20, 20, 20, 0), 50, Element.Nature);

            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.Fire, 1000f), new[] { neutral, weakToFire }), avatar);

            // Zero Attack: the base is exactly 0, so both hits land on the MinimumDamage floor.
            Assert.AreEqual(499, neutral.CurrentHp);
            Assert.AreEqual(499, weakToFire.CurrentHp);
        }

        [Test]
        public void NullArguments_DealNothing()
        {
            BattleUnit unit = Unit("u", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 100f);

            Assert.AreEqual(0, DamageFormula.Compute(null, unit, skill, 100f));
            Assert.AreEqual(0, DamageFormula.Compute(unit, null, skill, 100f));
            Assert.AreEqual(0, DamageFormula.Compute(unit, unit, null, 100f));
        }

        /// <summary>
        /// The level-invariance the formula relies on: between two identical beasts on the roster's
        /// shared <c>medium</c> curve, a Power-100 neutral hit takes the same share of max HP at every
        /// level up to integer rounding (7 of 15 at level 1, 28 of 57 at 50, 50 of 100 at 100).
        /// </summary>
        [Test]
        public void HitShare_IsFlatAcrossTheMediumCurve()
        {
            GrowthCurveData medium = Array.Find(BeastRosterTests.LoadRoster().GrowthCurves, c => c.CurveId == "medium");
            Assert.IsNotNull(medium);
            CreatureSpeciesSO species = Species(new StatBlock(100, 100, 100, 100, 100, 100, 3), medium);

            float[] shares = new float[3];
            int[] levels = { 1, 50, medium.MaxLevel };

            for (int i = 0; i < levels.Length; i++)
            {
                BattleUnit attacker = BattleUnitFactory.CreateBeast("a", BattleTeam.Player, species, levels[i], null, HexCoordinate.Zero);
                BattleUnit defender = BattleUnitFactory.CreateBeast("d", BattleTeam.Enemy, species, levels[i], null, HexCoordinate.Zero);
                int maxHp = defender.CurrentHp;

                SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.None, 100f), new[] { defender }), attacker);

                shares[i] = (float)(maxHp - defender.CurrentHp) / maxHp;
                Assert.That(shares[i], Is.InRange(0.45f, 0.5f), "level " + levels[i] + " hit share");
            }

            float lowest = Mathf.Min(shares[0], Mathf.Min(shares[1], shares[2]));
            float highest = Mathf.Max(shares[0], Mathf.Max(shares[1], shares[2]));
            Assert.That(highest / lowest, Is.LessThanOrEqualTo(1.1f), "hit share should be flat across levels up to rounding");
        }

        /// <summary>
        /// Pins the worked examples in the design doc ("Damage formula"): Phoenix hitting Golem with a
        /// Power-100 Fire skill (weak against Earth, 0.5x), physical and special, from the authored
        /// roster.
        /// </summary>
        [TestCase(1, 3, 5)]
        [TestCase(50, 12, 18)]
        [TestCase(100, 21, 33)]
        public void Roster_PhoenixIntoGolem_MatchesTheDesignDocExamples(int level, int physical, int special)
        {
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            BattleUnit phoenix = RosterBeast(roster, "phoenix", level);
            BattleUnit golem = RosterBeast(roster, "golem", level);

            Assert.AreEqual(physical, DamageFormula.Compute(phoenix, golem, Skill(DamageCategory.Physical, Element.Fire, 100f), 100f));
            Assert.AreEqual(special, DamageFormula.Compute(phoenix, golem, Skill(DamageCategory.Special, Element.Fire, 100f), 100f));
        }

        private BattleUnit RosterBeast(BeastRosterData roster, string speciesId, int level)
        {
            SpeciesData data = Array.Find(roster.Species, s => s.SpeciesId == speciesId);
            Assert.IsNotNull(data, speciesId);
            GrowthCurveData curve = Array.Find(roster.GrowthCurves, c => c.CurveId == data.GrowthCurveId);
            CreatureSpeciesSO species = Species(data.BaseStats, curve);
            species.Elements = Array.ConvertAll(data.Elements, name =>
            {
                Assert.IsTrue(BeastRosterValidator.TryParseElement(name, out Element element), name);
                return element;
            });

            return BattleUnitFactory.CreateBeast(speciesId, BattleTeam.Enemy, species, level, null, HexCoordinate.Zero);
        }

        private CreatureSpeciesSO Species(StatBlock baseStats, GrowthCurveData curveData)
        {
            GrowthRateCurve curve = ScriptableObject.CreateInstance<GrowthRateCurve>();
            curve.Curve = curveData.ToAnimationCurve();
            curve.MaxLevel = curveData.MaxLevel;
            _created.Add(curve);

            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            species.BaseStats = baseStats;
            species.GrowthRate = curve;
            _created.Add(species);
            return species;
        }

        private SkillSO Skill(DamageCategory category, Element element, float power)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            skill.Category = category;
            skill.Element = element;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power });
            _created.Add(skill);
            return skill;
        }

        private SkillSO StatSkill(SkillEffectType type, StatType stat, float magnitude)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            skill.Effects.Add(new SkillEffect { EffectType = type, AffectedStat = stat, Magnitude = magnitude });
            _created.Add(skill);
            return skill;
        }

        private static BattleUnit Unit(string id, StatBlock stats, int level, params Element[] elements)
        {
            return new BattleUnit(id, BattleTeam.Enemy, stats, HexCoordinate.Zero, null, elements, level);
        }
    }
}
