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
    /// <c>((2 * Level / 5 + 2) * Power * A / D) / 50 + 2</c>, times the element multiplier,
    /// truncated.
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

        // (22 * 40 * 1) / 50 + 2 = 19.6.
        [TestCase(50, 40f, 100, 100, 19)]
        // (6 * 50 * 1.5) / 50 + 2 = 11.
        [TestCase(10, 50f, 30, 20, 11)]
        // (2.4 * 40 * 1) / 50 + 2 = 3.92.
        [TestCase(1, 40f, 15, 15, 3)]
        // (42 * 40 * 1) / 50 + 2 = 35.6.
        [TestCase(100, 40f, 100, 100, 35)]
        // (42 * 40 * 130 / 170) / 50 + 2 = 27.69.
        [TestCase(100, 40f, 130, 170, 27)]
        public void Compute_KnownValues(int level, float power, int attack, int defense, int expected)
        {
            Assert.AreEqual(expected, DamageFormula.Compute(level, power, attack, defense, ElementChart.Neutral));
        }

        [Test]
        public void ComputeBase_IsUntruncated()
        {
            Assert.AreEqual(3.92f, DamageFormula.ComputeBase(1, 40f, 15, 15), 0.0001f);
            Assert.AreEqual(19.6f, DamageFormula.ComputeBase(50, 40f, 100, 100), 0.0001f);
        }

        [Test]
        public void Physical_UsesAttackAgainstDefense_SpecialUsesSpecialAttackAgainstSpecialDefense()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 100, 1, 50, 1, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 1, 100, 1, 25, 0), 10);

            // Physical: (6 * 50 * 100 / 100) / 50 + 2 = 8. Special: (6 * 50 * 50 / 25) / 50 + 2 = 14.
            Assert.AreEqual(8, DamageFormula.Compute(caster, target, Skill(DamageCategory.Physical, Element.None, 50f), 50f));
            Assert.AreEqual(14, DamageFormula.Compute(caster, target, Skill(DamageCategory.Special, Element.None, 50f), 50f));
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
            // Base 3.92: x2 then truncate is 7; truncate then x2 would be 6.
            Assert.AreEqual(7, DamageFormula.Compute(1, 40f, 15, 15, ElementChart.Strong));
            // Base 3.92 x0.5 = 1.96, truncated to 1.
            Assert.AreEqual(1, DamageFormula.Compute(1, 40f, 15, 15, ElementChart.Weak));
        }

        [Test]
        public void ElementMultiplier_ComesFromTheSkillAgainstTheTarget()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10, Element.Water);
            BattleUnit target = Unit("target", new StatBlock(100, 20, 20, 20, 20, 0), 10, Element.Nature, Element.Metal);

            // Base (6 * 50) / 50 + 2 = 8; Fire is strong against both Nature and Metal: x4.
            Assert.AreEqual(32, DamageFormula.Compute(caster, target, Skill(DamageCategory.Physical, Element.Fire, 50f), 50f));
        }

        [Test]
        public void PositivePower_DealsAtLeastOne()
        {
            // Base ~2.0, x0.25 = 0.5, truncated to 0, floored to 1.
            Assert.AreEqual(DamageFormula.MinimumDamage, DamageFormula.Compute(1, 1f, 1, 1000, ElementChart.Weak * ElementChart.Weak));
        }

        [TestCase(0f)]
        [TestCase(-10f)]
        public void ZeroOrNegativePower_DealsNothing(float power)
        {
            Assert.AreEqual(0, DamageFormula.Compute(100, power, 500, 1, ElementChart.Strong));
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
        public void ZeroOrNegativeDefense_IsTreatedAsOne()
        {
            int atOne = DamageFormula.Compute(10, 50f, 20, 1, ElementChart.Neutral);

            // (6 * 50 * 20 / 1) / 50 + 2 = 122.
            Assert.AreEqual(122, atOne);
            Assert.AreEqual(atOne, DamageFormula.Compute(10, 50f, 20, 0, ElementChart.Neutral));
            Assert.AreEqual(atOne, DamageFormula.Compute(10, 50f, 20, -5, ElementChart.Neutral));
        }

        [Test]
        public void ZeroOrNegativeAttack_DealsTheBaseOffset()
        {
            Assert.AreEqual(2, DamageFormula.Compute(100, 500f, 0, 10, ElementChart.Neutral));
            Assert.AreEqual(2, DamageFormula.Compute(100, 500f, -20, 10, ElementChart.Neutral));
            Assert.AreEqual(4, DamageFormula.Compute(100, 500f, 0, 10, ElementChart.Strong));
        }

        [Test]
        public void Level_ScalesDamage()
        {
            // Level terms 2.4, 6, 22, 42 at power 50, A = D.
            Assert.AreEqual(4, DamageFormula.Compute(1, 50f, 20, 20, ElementChart.Neutral));
            Assert.AreEqual(8, DamageFormula.Compute(10, 50f, 20, 20, ElementChart.Neutral));
            Assert.AreEqual(24, DamageFormula.Compute(50, 50f, 20, 20, ElementChart.Neutral));
            Assert.AreEqual(44, DamageFormula.Compute(100, 50f, 20, 20, ElementChart.Neutral));
        }

        [Test]
        public void LevelBelowOne_IsTreatedAsOne()
        {
            Assert.AreEqual(DamageFormula.Compute(1, 50f, 20, 20, ElementChart.Neutral), DamageFormula.Compute(0, 50f, 20, 20, ElementChart.Neutral));
            Assert.AreEqual(1, Unit("u", new StatBlock(10, 0, 0, 0, 0, 0), 0).Level);
            Assert.AreEqual(1, new BattleUnit("u", BattleTeam.Enemy, new StatBlock(10, 0, 0, 0, 0, 0), HexCoordinate.Zero).Level);
        }

        [Test]
        public void CasterLevel_IsWhatCounts()
        {
            BattleUnit low = Unit("low", new StatBlock(100, 20, 20, 20, 20, 0), 1);
            BattleUnit high = Unit("high", new StatBlock(100, 20, 20, 20, 20, 0), 100);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 50f);

            Assert.AreEqual(44, DamageFormula.Compute(high, low, skill, 50f));
            Assert.AreEqual(4, DamageFormula.Compute(low, high, skill, 50f));
        }

        [Test]
        public void CasterAttackBuff_RaisesLaterDamage()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 20, 20, 20, 20, 0), 10);
            SkillSO strike = Skill(DamageCategory.Physical, Element.None, 50f);
            SkillSO special = Skill(DamageCategory.Special, Element.None, 50f);
            SkillSO buff = StatSkill(SkillEffectType.BuffStat, StatType.Attack, 20f);

            SkillEffectApplier.Apply(new SkillActivation(buff, new[] { caster }), caster);
            SkillEffectApplier.Apply(new SkillActivation(strike, new[] { target }), caster);

            // Attack 40 vs Defense 20: (6 * 50 * 2) / 50 + 2 = 14.
            Assert.AreEqual(486, target.CurrentHp);

            // An Attack buff does nothing for a special skill: (6 * 50 * 1) / 50 + 2 = 8.
            SkillEffectApplier.Apply(new SkillActivation(special, new[] { target }), caster);
            Assert.AreEqual(478, target.CurrentHp);
        }

        [Test]
        public void TargetDefenseDebuff_RaisesDamage()
        {
            BattleUnit caster = Unit("caster", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            BattleUnit target = Unit("target", new StatBlock(500, 20, 20, 20, 20, 0), 10);

            SkillEffectApplier.Apply(new SkillActivation(StatSkill(SkillEffectType.DebuffStat, StatType.Defense, 10f), new[] { target }), caster);
            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.None, 50f), new[] { target }), caster);

            // Attack 20 vs Defense 10: (6 * 50 * 2) / 50 + 2 = 14.
            Assert.AreEqual(486, target.CurrentHp);
        }

        [Test]
        public void StatfulAvatar_DealsDamageFromItsOwnStatsAndLevel()
        {
            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(50, 5, 5, 40, 5, 0), null, BattleAvatar.DefaultId, 30);
            BattleUnit enemy = Unit("enemy", new StatBlock(500, 20, 20, 20, 20, 0), 30);

            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Special, Element.None, 50f), new[] { enemy }), avatar);

            // Level term 14; SpecialAttack 40 vs SpecialDefense 20: (14 * 50 * 2) / 50 + 2 = 30.
            Assert.AreEqual(470, enemy.CurrentHp);
        }

        [Test]
        public void ZeroStatAvatar_DealsTheFloorWhateverThePower()
        {
            BattleUnit avatar = BattleAvatar.Create(null);
            BattleUnit neutral = Unit("neutral", new StatBlock(500, 20, 20, 20, 20, 0), 50);
            BattleUnit weakToFire = Unit("nature", new StatBlock(500, 20, 20, 20, 20, 0), 50, Element.Nature);

            SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.Fire, 1000f), new[] { neutral, weakToFire }), avatar);

            // Zero Attack: base is exactly the +2 offset, then the element multiplier.
            Assert.AreEqual(498, neutral.CurrentHp);
            Assert.AreEqual(496, weakToFire.CurrentHp);
        }

        [Test]
        public void NullArguments_DealNothing()
        {
            BattleUnit unit = Unit("u", new StatBlock(100, 20, 20, 20, 20, 0), 10);
            SkillSO skill = Skill(DamageCategory.Physical, Element.None, 50f);

            Assert.AreEqual(0, DamageFormula.Compute(null, unit, skill, 50f));
            Assert.AreEqual(0, DamageFormula.Compute(unit, null, skill, 50f));
            Assert.AreEqual(0, DamageFormula.Compute(unit, unit, null, 50f));
        }

        /// <summary>
        /// The intent of the level term: between two identical beasts on the roster's shared
        /// <c>medium</c> curve, a Power-40 neutral hit takes a roughly similar share of max HP at
        /// level 1 and level 100. It is only loosely true (about 20% at level 1, 33% at 50 and 35%
        /// at 100), so the band is deliberately loose; tightening it is the balance simulator's job.
        /// </summary>
        [Test]
        public void LevelTerm_KeepsHitShareRoughlyFlatAcrossTheMediumCurve()
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

                SkillEffectApplier.Apply(new SkillActivation(Skill(DamageCategory.Physical, Element.None, 40f), new[] { defender }), attacker);

                shares[i] = (float)(maxHp - defender.CurrentHp) / maxHp;
                Assert.That(shares[i], Is.InRange(0.1f, 0.5f), "level " + levels[i] + " hit share");
            }

            float lowest = Mathf.Min(shares[0], Mathf.Min(shares[1], shares[2]));
            float highest = Mathf.Max(shares[0], Mathf.Max(shares[1], shares[2]));
            Assert.That(highest / lowest, Is.LessThanOrEqualTo(2f), "hit share should stay within a factor of 2 across levels");
        }

        /// <summary>
        /// Pins the worked examples in the design doc ("Damage formula"): Phoenix hitting Golem with a
        /// Power-40 Fire skill (weak against Earth, 0.5x), physical and special, from the authored
        /// roster.
        /// </summary>
        [TestCase(1, 1, 2)]
        [TestCase(50, 8, 12)]
        [TestCase(100, 15, 23)]
        public void Roster_PhoenixIntoGolem_MatchesTheDesignDocExamples(int level, int physical, int special)
        {
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            BattleUnit phoenix = RosterBeast(roster, "phoenix", level);
            BattleUnit golem = RosterBeast(roster, "golem", level);

            Assert.AreEqual(physical, DamageFormula.Compute(phoenix, golem, Skill(DamageCategory.Physical, Element.Fire, 40f), 40f));
            Assert.AreEqual(special, DamageFormula.Compute(phoenix, golem, Skill(DamageCategory.Special, Element.Fire, 40f), 40f));
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
