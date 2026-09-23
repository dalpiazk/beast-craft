using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    public class BattleUnitFactoryTests
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
        public void CreateBeast_AssemblesStatsFromSpeciesLevelAndGear()
        {
            CreatureSpeciesSO species = Species(new StatBlock(100, 20, 0, 0, 0, 0, 3));
            GearSO boots = Gear(1, new StatModifier { Stat = StatType.MoveRange, FlatBonus = 1 });
            HexCoordinate position = new HexCoordinate(1, -1);

            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Player, species, 6, new[] { boots }, position);

            Assert.AreEqual("b1", unit.Id);
            Assert.AreEqual(BattleTeam.Player, unit.Team);
            Assert.AreEqual(position, unit.Position);
            Assert.AreEqual(150, unit.Stats.Hp);
            Assert.AreEqual(30, unit.Stats.Attack);
            Assert.AreEqual(4, unit.Stats.MoveRange);
            Assert.AreEqual(4, unit.MoveRange);
            Assert.AreEqual(150, unit.CurrentHp);
            Assert.AreEqual(6, unit.Level);
            Assert.AreEqual(0, unit.ActiveStatModifiers.Count);
            Assert.IsFalse(unit.IsDefeated);
            Assert.IsNotNull(unit.Skills);
        }

        [Test]
        public void CreateBeast_CopiesSpeciesElements()
        {
            CreatureSpeciesSO species = Species(new StatBlock(10, 0, 0, 0, 0, 0, 2));
            species.Elements = new[] { Element.Fire, Element.Metal };

            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Enemy, species, 1, null, HexCoordinate.Zero);
            species.Elements[0] = Element.Water;

            Assert.AreEqual(2, unit.Elements.Count);
            Assert.AreEqual(Element.Fire, unit.Elements[0]);
            Assert.AreEqual(Element.Metal, unit.Elements[1]);
        }

        [Test]
        public void CreateBeast_KeepsProvidedLoadout()
        {
            SkillLoadout loadout = new SkillLoadout(null);
            CreatureSpeciesSO species = Species(new StatBlock(10, 0, 0, 0, 0, 0));

            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Enemy, species, 1, null, HexCoordinate.Zero, loadout);

            Assert.AreSame(loadout, unit.Skills);
        }

        [Test]
        public void CreateBeast_NullSpecies_DoesNotThrow()
        {
            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Enemy, null, 1, null, HexCoordinate.Zero);

            Assert.AreEqual(1, unit.Stats.Hp);
            Assert.AreEqual(0, unit.MoveRange);
            Assert.AreEqual(0, unit.Elements.Count);
        }

        [Test]
        public void ExecuteTurn_BudgetComesFromMoveRangeStat()
        {
            CreatureSpeciesSO species = Species(new StatBlock(10, 0, 0, 0, 0, 0, 3));
            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Enemy, species, 1, null, HexCoordinate.Zero);

            BattleTurnResult result = BattleTurnExecutor.ExecuteTurn(unit, new[] { unit }, null, new System.Random(1), null);

            Assert.AreEqual(3, result.MovementBudget);
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
    }
}
