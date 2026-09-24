using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    public class MoveRangeBuffTests
    {
        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        [Test]
        public void Buff_RaisesMoveRange_AndRevertsOnExpiry()
        {
            BattleUnit unit = Unit(3);

            Fire(Skill(SkillEffectType.BuffStat, 2f, 1), unit);

            Assert.AreEqual(5, unit.MoveRange);
            Assert.AreEqual(1, unit.ActiveStatModifiers.Count);

            SkillEffectApplier.TickModifiers(unit);

            Assert.AreEqual(3, unit.MoveRange);
            Assert.AreEqual(0, unit.ActiveStatModifiers.Count);
        }

        [Test]
        public void Debuff_ClampsMoveRangeAtZero_AndRevertsOnlyWhatItTook()
        {
            BattleUnit unit = Unit(2);

            Fire(Skill(SkillEffectType.DebuffStat, 5f, 1), unit);

            Assert.AreEqual(0, unit.MoveRange);

            SkillEffectApplier.TickModifiers(unit);

            Assert.AreEqual(2, unit.MoveRange);
        }

        [Test]
        public void InstantBuff_IsPermanent()
        {
            BattleUnit unit = Unit(3);

            Fire(Skill(SkillEffectType.BuffStat, 1f, 0), unit);
            SkillEffectApplier.TickModifiers(unit);

            Assert.AreEqual(4, unit.MoveRange);
        }

        [Test]
        public void ExpiringBuff_DoesNotCountTowardThatTurnsBudget()
        {
            BattleUnit unit = Unit(3);
            Fire(Skill(SkillEffectType.BuffStat, 2f, 1), unit);

            BattleTurnResult result = BattleTurnExecutor.ExecuteTurn(unit, new[] { unit }, null, new System.Random(1), null);

            Assert.AreEqual(3, result.MovementBudget);
        }

        [Test]
        public void ActiveBuff_CountsTowardBudget()
        {
            BattleUnit unit = Unit(3);
            Fire(Skill(SkillEffectType.BuffStat, 2f, 2), unit);

            BattleTurnResult result = BattleTurnExecutor.ExecuteTurn(unit, new[] { unit }, null, new System.Random(1), null);

            Assert.AreEqual(5, result.MovementBudget);
        }

        private SkillSO Skill(SkillEffectType type, float magnitude, int duration)
        {
            SkillSO skill = new SkillSO();
            skill.Effects.Add(new SkillEffect
            {
                EffectType = type,
                AffectedStat = StatType.MoveRange,
                Magnitude = magnitude,
                DurationTurns = duration
            });
            _created.Add(skill);
            return skill;
        }

        private static BattleUnit Unit(int moveRange)
        {
            return new BattleUnit("u", BattleTeam.Player, new StatBlock(10, 0, 0, 0, 0, 0, moveRange), HexCoordinate.Zero);
        }

        private static void Fire(SkillSO skill, BattleUnit target)
        {
            BattleUnit caster = new BattleUnit("caster", BattleTeam.Player, new StatBlock(10, 0, 0, 0, 0, 0), HexCoordinate.Zero);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster);
        }
    }
}
