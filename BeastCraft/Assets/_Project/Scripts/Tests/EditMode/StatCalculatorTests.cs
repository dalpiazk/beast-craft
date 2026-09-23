using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    public class StatCalculatorTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void NoGear_ReturnsBaseAtLevel()
        {
            // Curve 1 -> 2 over levels 1..11: level 6 is x1.5. Move range is not scaled.
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 10, 30, 40, 8, 3));

            StatBlock stats = StatCalculator.ComputeStats(species, 6, null);

            Assert.AreEqual(150, stats.Hp);
            Assert.AreEqual(30, stats.Attack);
            Assert.AreEqual(15, stats.Defense);
            Assert.AreEqual(45, stats.SpecialAttack);
            Assert.AreEqual(60, stats.SpecialDefense);
            Assert.AreEqual(12, stats.Speed);
            Assert.AreEqual(3, stats.MoveRange);
        }

        [Test]
        public void Flat_IsAddedBeforePercent()
        {
            // (20 + 10) * 1.5 = 45. Percent first would give 20 * 1.5 + 10 = 40.
            StatBlock stats = StatCalculator.ComputeStats(
                new StatBlock(10, 20, 0, 0, 0, 0),
                new[] { Mod(StatType.Attack, 0, 0.5f), Mod(StatType.Attack, 10, 0f) });

            Assert.AreEqual(45, stats.Attack);
        }

        [Test]
        public void Percents_AreSummedNotCompounded()
        {
            // 100 * (1 + 0.1 + 0.1) = 120, not 100 * 1.1 * 1.1 = 121.
            StatBlock stats = StatCalculator.ComputeStats(
                new StatBlock(10, 100, 0, 0, 0, 0),
                new[] { Mod(StatType.Attack, 0, 0.1f), Mod(StatType.Attack, 0, 0.1f) });

            Assert.AreEqual(120, stats.Attack);
        }

        [Test]
        public void Flats_AreSummedPerAxis()
        {
            StatBlock stats = StatCalculator.ComputeStats(
                new StatBlock(10, 5, 5, 0, 0, 0, 2),
                new[] { Mod(StatType.Defense, 3, 0f), Mod(StatType.Defense, 4, 0f), Mod(StatType.MoveRange, 1, 0f) });

            Assert.AreEqual(12, stats.Defense);
            Assert.AreEqual(5, stats.Attack);
            Assert.AreEqual(3, stats.MoveRange);
        }

        [Test]
        public void Result_IsRoundedToNearest()
        {
            // 7 * 1.1 = 7.7, rounded to 8.
            StatBlock stats = StatCalculator.ComputeStats(new StatBlock(10, 7, 0, 0, 0, 0), new[] { Mod(StatType.Attack, 0, 0.1f) });

            Assert.AreEqual(8, stats.Attack);
        }

        [Test]
        public void NegativeResult_IsClampedToZero_AndHpToOne()
        {
            StatBlock stats = StatCalculator.ComputeStats(
                new StatBlock(10, 5, 0, 0, 0, 0, 2),
                new[] { Mod(StatType.Attack, -20, 0f), Mod(StatType.HP, -50, 0f), Mod(StatType.MoveRange, 0, -2f) });

            Assert.AreEqual(0, stats.Attack);
            Assert.AreEqual(1, stats.Hp);
            Assert.AreEqual(0, stats.MoveRange);
        }

        [Test]
        public void ZeroBase_HpIsFloorOne()
        {
            Assert.AreEqual(1, StatCalculator.ComputeStats(default(StatBlock), null).Hp);
        }

        [Test]
        public void GearAboveLevel_IsIgnored()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 0, 0, 0, 0, 3));
            GearSO ok = Gear(5, Mod(StatType.Attack, 5, 0f));
            GearSO tooHigh = Gear(7, Mod(StatType.Attack, 100, 0f), Mod(StatType.MoveRange, 2, 0f));

            StatBlock stats = StatCalculator.ComputeStats(species, 6, new[] { ok, tooHigh });

            // (20 * 1.5 = 30) + 5 from the level-5 item only.
            Assert.AreEqual(35, stats.Attack);
            Assert.AreEqual(3, stats.MoveRange);
        }

        [Test]
        public void GearAtExactlyMinimumLevel_Applies()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 0, 0, 0, 0, 3));

            StatBlock stats = StatCalculator.ComputeStats(species, 6, new[] { Gear(6, Mod(StatType.MoveRange, 1, 0f)) });

            Assert.AreEqual(4, stats.MoveRange);
        }

        [Test]
        public void NullGearAndNullModifiers_AreSkipped()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 0, 0, 0, 0, 3));
            GearSO gear = Gear(1, Mod(StatType.Attack, 2, 0f));
            gear.Modifiers.Add(null);
            GearSO noList = Gear(1);
            noList.Modifiers = null;

            StatBlock stats = StatCalculator.ComputeStats(species, 1, new[] { null, gear, noList });

            Assert.AreEqual(22, stats.Attack);
        }

        [Test]
        public void GearAcrossPieces_IsCombinedInOnePass()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 10, 0, 0, 0, 0, 3));
            GearSO a = Gear(1, Mod(StatType.Attack, 10, 0.25f));
            GearSO b = Gear(1, Mod(StatType.Attack, 0, 0.25f));

            // Level 1 is x1: (10 + 10) * (1 + 0.5) = 30, whichever piece is listed first.
            StatBlock stats = StatCalculator.ComputeStats(species, 1, new[] { b, a });

            Assert.AreEqual(30, stats.Attack);
        }

        [Test]
        public void ComputeStats_DoesNotMutateInputs()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 10, 0, 0, 0, 0, 3));
            StatModifier modifier = Mod(StatType.Attack, 5, 0.5f);
            GearSO gear = Gear(1, modifier);

            StatCalculator.ComputeStats(species, 11, new[] { gear });

            Assert.AreEqual(10, species.BaseStats.Attack);
            Assert.AreEqual(5, modifier.FlatBonus);
            Assert.AreEqual(1, gear.Modifiers.Count);
        }

        [Test]
        public void NullSpecies_YieldsFlooredZeroBlock()
        {
            StatBlock stats = StatCalculator.ComputeStats((CreatureSpeciesSO)null, 5, null);

            Assert.AreEqual(1, stats.Hp);
            Assert.AreEqual(0, stats.Attack);
            Assert.AreEqual(0, stats.MoveRange);
        }

        [Test]
        public void CollectModifiers_FiltersByLevelAndNulls()
        {
            StatModifier keep = Mod(StatType.Speed, 1, 0f);
            GearSO ok = Gear(3, keep);
            ok.Modifiers.Add(null);

            List<StatModifier> modifiers = StatCalculator.CollectModifiers(new[] { ok, Gear(4, Mod(StatType.Speed, 9, 0f)), null }, 3);

            Assert.AreEqual(1, modifiers.Count);
            Assert.AreSame(keep, modifiers[0]);
            Assert.AreEqual(0, StatCalculator.CollectModifiers(null, 3).Count);
        }

        private CreatureSpeciesSO Species(StatBlock baseStats)
        {
            GrowthRateCurve growth = ScriptableObject.CreateInstance<GrowthRateCurve>();
            growth.Curve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
            growth.MaxLevel = 11;
            _created.Add(growth);

            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            species.BaseStats = baseStats;
            species.GrowthRate = growth;
            _created.Add(species);
            return species;
        }

        private GearSO Gear(int minimumLevel, params StatModifier[] modifiers)
        {
            GearSO gear = ScriptableObject.CreateInstance<GearSO>();
            gear.MinimumLevel = minimumLevel;
            gear.Modifiers.AddRange(modifiers);
            _created.Add(gear);
            return gear;
        }

        private static StatModifier Mod(StatType stat, int flat, float percent)
        {
            return new StatModifier { Stat = stat, FlatBonus = flat, PercentBonus = percent };
        }
    }
}
