using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="SkillTargetingCriterion.CurrentHp"/>: a skill compares candidates by the HP they
    /// have left, not by the stat block's maximum, honours <see cref="SkillTargetingOrder"/>, breaks
    /// ties on the unit id, and does so identically through <see cref="SkillTargetResolver.ResolveTargets"/>
    /// (range-limited) and <see cref="SkillTargetResolver.PickFocusIgnoringRange"/>.
    /// </summary>
    public class SkillTargetingCurrentHpTests
    {
        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        [Test]
        public void EnumValue_IsAppended_AndExistingValuesKeepTheirNumbers()
        {
            Assert.AreEqual(0, (int)SkillTargetingCriterion.Random);
            Assert.AreEqual(1, (int)SkillTargetingCriterion.Stat);
            Assert.AreEqual(2, (int)SkillTargetingCriterion.Distance);
            Assert.AreEqual(3, (int)SkillTargetingCriterion.CurrentHp);
        }

        [Test]
        public void Lowest_PicksTheUnitWithTheLeastHpLeft_NotTheLowestMaximum()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit fragile = Unit("e1", BattleTeam.Enemy, 60, new HexCoordinate(1, 0));
            BattleUnit wounded = Unit("e2", BattleTeam.Enemy, 200, new HexCoordinate(2, 0));
            wounded.CurrentHp = 40;

            SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, SkillTargetingOrder.Lowest, 5);
            List<BattleUnit> all = new List<BattleUnit> { caster, fragile, wounded };

            Assert.AreSame(wounded, Single(skill, caster, all));
            Assert.AreSame(wounded, SkillTargetResolver.PickFocusIgnoringRange(skill, caster, all, null));

            SkillSO byMaxHp = Skill(SkillTargetingCriterion.Stat, SkillTargetingOrder.Lowest, 5);
            Assert.AreSame(fragile, Single(byMaxHp, caster, all), "Stat/HP still compares the maximum");
        }

        [Test]
        public void Highest_PicksTheUnitWithTheMostHpLeft()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit big = Unit("e1", BattleTeam.Enemy, 300, new HexCoordinate(1, 0));
            BattleUnit small = Unit("e2", BattleTeam.Enemy, 120, new HexCoordinate(2, 0));
            big.CurrentHp = 90;

            SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, SkillTargetingOrder.Highest, 5);
            List<BattleUnit> all = new List<BattleUnit> { caster, big, small };

            Assert.AreSame(small, Single(skill, caster, all));
            Assert.AreSame(small, SkillTargetResolver.PickFocusIgnoringRange(skill, caster, all, null));
        }

        [Test]
        public void Ties_BreakOnTheUnitId_WhateverTheRosterOrder()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit b = Unit("e2", BattleTeam.Enemy, 100, new HexCoordinate(1, 0));
            BattleUnit a = Unit("e1", BattleTeam.Enemy, 150, new HexCoordinate(2, 0));
            a.CurrentHp = 100;

            foreach (SkillTargetingOrder order in new[] { SkillTargetingOrder.Lowest, SkillTargetingOrder.Highest })
            {
                SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, order, 5);
                List<BattleUnit> all = new List<BattleUnit> { b, caster, a };
                Assert.AreSame(a, Single(skill, caster, all), order + ": equal HP goes to the lower id");
                Assert.AreSame(a, SkillTargetResolver.PickFocusIgnoringRange(skill, caster, all, null), order + " (ignoring range)");
            }
        }

        [Test]
        public void RangeLimitedPick_OnlyConsidersUnitsInRange_WhileTheBoardWidePickDoesNot()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit near = Unit("e1", BattleTeam.Enemy, 100, new HexCoordinate(1, 0));
            BattleUnit far = Unit("e2", BattleTeam.Enemy, 100, new HexCoordinate(4, 0));
            far.CurrentHp = 10;

            SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, SkillTargetingOrder.Lowest, 2);
            List<BattleUnit> all = new List<BattleUnit> { caster, near, far };

            Assert.AreSame(near, Single(skill, caster, all), "the weakest is out of range, so the in-range unit is hit");
            Assert.AreSame(far, SkillTargetResolver.PickFocusIgnoringRange(skill, caster, all, null), "the mover chases the weakest");
        }

        [Test]
        public void Pick_TracksDamageAsItIsTaken_AndSkipsTheDefeated()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit a = Unit("e1", BattleTeam.Enemy, 100, new HexCoordinate(1, 0));
            BattleUnit b = Unit("e2", BattleTeam.Enemy, 100, new HexCoordinate(2, 0));
            SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, SkillTargetingOrder.Lowest, 5);
            List<BattleUnit> all = new List<BattleUnit> { caster, a, b };

            Assert.AreSame(a, Single(skill, caster, all), "tie: lower id");

            b.CurrentHp = 70;
            Assert.AreSame(b, Single(skill, caster, all), "b took damage, so it is now the weakest");

            b.CurrentHp = 0;
            b.IsDefeated = true;
            Assert.AreSame(a, Single(skill, caster, all), "a defeated unit is never a candidate");
        }

        [Test]
        public void TargetingStat_IsIgnored()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, 100, new HexCoordinate(0, 0));
            BattleUnit fast = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100, 10, 10, 10, 10, 200), new HexCoordinate(1, 0));
            BattleUnit slow = new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(100, 10, 10, 10, 10, 5), new HexCoordinate(2, 0));
            fast.CurrentHp = 30;

            SkillSO skill = Skill(SkillTargetingCriterion.CurrentHp, SkillTargetingOrder.Lowest, 5);
            skill.TargetingStat = StatType.Speed;

            Assert.AreSame(fast, Single(skill, caster, new List<BattleUnit> { caster, fast, slow }));
        }

        private static BattleUnit Single(SkillSO skill, BattleUnit caster, List<BattleUnit> all)
        {
            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(skill, caster, all, null, null);
            Assert.AreEqual(1, targets.Count);
            return targets[0];
        }

        private static BattleUnit Unit(string id, BattleTeam team, int hp, HexCoordinate position)
        {
            return new BattleUnit(id, team, new StatBlock(hp, 10, 10, 10, 10, 10), position);
        }

        private SkillSO Skill(SkillTargetingCriterion criterion, SkillTargetingOrder order, int range)
        {
            SkillSO skill = new SkillSO();
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.Range = range;
            skill.Cooldown = 1;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = criterion;
            skill.TargetingOrder = order;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            _created.Add(skill);
            return skill;
        }
    }
}
