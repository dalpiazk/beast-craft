using System.Collections.Generic;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    public class StatBlockMoveRangeTests
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
        public void StatType_ExistingValuesAreStable_AndMoveRangeIsAppended()
        {
            Assert.AreEqual(0, (int)StatType.HP);
            Assert.AreEqual(1, (int)StatType.Attack);
            Assert.AreEqual(2, (int)StatType.Defense);
            Assert.AreEqual(3, (int)StatType.SpecialAttack);
            Assert.AreEqual(4, (int)StatType.SpecialDefense);
            Assert.AreEqual(5, (int)StatType.Speed);
            Assert.AreEqual(6, (int)StatType.MoveRange);
        }

        [Test]
        public void Constructor_MoveRangeDefaultsToZero()
        {
            Assert.AreEqual(0, new StatBlock(10, 1, 2, 3, 4, 5).MoveRange);
            Assert.AreEqual(4, new StatBlock(10, 1, 2, 3, 4, 5, 4).MoveRange);
        }

        [Test]
        public void GetStatAndSetStat_RoundTripMoveRange()
        {
            StatBlock block = new StatBlock(10, 1, 2, 3, 4, 5, 3);

            Assert.AreEqual(3, block.GetStat(StatType.MoveRange));

            block.SetStat(StatType.MoveRange, 7);

            Assert.AreEqual(7, block.MoveRange);
            Assert.AreEqual(5, block.Speed);
        }

        [Test]
        public void Addition_SumsMoveRange()
        {
            StatBlock sum = new StatBlock(1, 1, 1, 1, 1, 1, 3) + new StatBlock(0, 0, 0, 0, 0, 0, 2);

            Assert.AreEqual(5, sum.MoveRange);
            Assert.AreEqual(1, sum.Hp);
        }

        [Test]
        public void GetStatAtLevel_MoveRangeIgnoresGrowthCurve()
        {
            // Curve runs 0 -> 1: every scaled stat is 0 at level 1 and full at max level.
            CreatureSpeciesSO species = Species(new StatBlock(100, 50, 0, 0, 0, 0, 3), AnimationCurve.Linear(0f, 0f, 1f, 1f));

            Assert.AreEqual(0, species.GetStatAtLevel(StatType.Attack, 1));
            Assert.AreEqual(3, species.GetStatAtLevel(StatType.MoveRange, 1));
            Assert.AreEqual(3, species.GetStatAtLevel(StatType.MoveRange, 6));
            Assert.AreEqual(3, species.GetStatAtLevel(StatType.MoveRange, 11));
        }

        [Test]
        public void GetStatAtLevel_OtherStatsStillScale()
        {
            // Curve runs 1 -> 2 over levels 1..11, so level 6 is x1.5.
            CreatureSpeciesSO species = Species(new StatBlock(100, 50, 0, 0, 0, 0, 3), AnimationCurve.Linear(0f, 1f, 1f, 2f));

            Assert.AreEqual(100, species.GetStatAtLevel(StatType.HP, 1));
            Assert.AreEqual(150, species.GetStatAtLevel(StatType.HP, 6));
            Assert.AreEqual(100, species.GetStatAtLevel(StatType.Attack, 11));
        }

        [Test]
        public void GetStatAtLevel_MoveRangeWithoutGrowthRate_ReturnsBase()
        {
            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            _created.Add(species);
            species.BaseStats = new StatBlock(10, 0, 0, 0, 0, 0, 2);

            Assert.AreEqual(2, species.GetStatAtLevel(StatType.MoveRange, 50));
        }

        private CreatureSpeciesSO Species(StatBlock baseStats, AnimationCurve curve)
        {
            GrowthRateCurve growth = ScriptableObject.CreateInstance<GrowthRateCurve>();
            growth.Curve = curve;
            growth.MaxLevel = 11;
            _created.Add(growth);

            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            species.BaseStats = baseStats;
            species.GrowthRate = growth;
            _created.Add(species);
            return species;
        }
    }
}
