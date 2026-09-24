using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The status engine: chance and resistance, taunt, stun, shield, damage over time and
    /// knockback, through <see cref="SkillEffectApplier"/>, <see cref="StatusEffects"/>,
    /// <see cref="SkillTargetResolver"/> and <see cref="BattleTurnExecutor"/>.
    /// </summary>
    public class StatusEffectTests
    {
        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ------------------------------------------------------------------ chance and resistance

        [Test]
        public void EffectiveChance_HostileIsReducedByResist_AllyIsNot()
        {
            SkillEffect effect = new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Taunt, Chance = 85 };
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            BattleUnit boss = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, statusResist: 50);
            BattleUnit ally = Unit("p2", BattleTeam.Player, HexCoordinate.Zero, statusResist: 50);

            Assert.AreEqual(42, SkillEffectApplier.GetEffectiveChance(effect, caster, boss), "85 * (100 - 50) / 100, truncated");
            Assert.AreEqual(85, SkillEffectApplier.GetEffectiveChance(effect, caster, ally), "an ally's resistance never applies");
        }

        [Test]
        public void EffectiveChance_UnsetOrOutOfRangeChanceReadsAsAlways()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero);

            Assert.AreEqual(100, SkillEffectApplier.GetEffectiveChance(new SkillEffect { Chance = 0 }, caster, target), "Unity zero-fill must not mean never");
            Assert.AreEqual(100, SkillEffectApplier.GetEffectiveChance(new SkillEffect { Chance = 150 }, caster, target));
            Assert.AreEqual(100, SkillEffectApplier.GetEffectiveChance(new SkillEffect(), caster, target), "the default");
        }

        [Test]
        public void StatusResist_IsClampedIntoPercentRange()
        {
            Assert.AreEqual(100, Unit("a", BattleTeam.Enemy, HexCoordinate.Zero, statusResist: 250).StatusResist);
            Assert.AreEqual(0, Unit("b", BattleTeam.Enemy, HexCoordinate.Zero, statusResist: -5).StatusResist);
        }

        [Test]
        public void RollChance_Certain_DrawsNothing_Uncertain_DrawsOnce()
        {
            System.Random rng = new System.Random(42);
            System.Random reference = new System.Random(42);

            Assert.IsTrue(SkillEffectApplier.RollChance(100, rng));
            Assert.AreEqual(reference.Next(), rng.Next(), "a certain effect takes no draw");

            rng = new System.Random(7);
            reference = new System.Random(7);
            bool expected = reference.Next(100) < 60;

            Assert.AreEqual(expected, SkillEffectApplier.RollChance(60, rng));
            Assert.AreEqual(reference.Next(), rng.Next(), "exactly one draw for an uncertain effect");
        }

        [Test]
        public void RollChance_NullRng_OnlyCertainEffectsLand()
        {
            Assert.IsTrue(SkillEffectApplier.RollChance(100, null));
            Assert.IsFalse(SkillEffectApplier.RollChance(99, null));
        }

        [Test]
        public void ChanceRoll_ComesAfterTheSameTargetsEarlierDamageRolls()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 10000);
            SkillSO skill = Skill(
                new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f },
                new SkillEffect { EffectType = SkillEffectType.DebuffStat, AffectedStat = StatType.Speed, Magnitude = 1f, DurationTurns = 2, Chance = 50 });

            System.Random rng = new System.Random(3);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster, rng);

            System.Random reference = new System.Random(3);
            reference.Next(100);
            reference.Next(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent + 1);
            bool lands = reference.Next(100) < 50;

            Assert.AreEqual(lands ? 9 : 10, target.Stats.Speed, "crit, variance, then the chance draw");
            Assert.AreEqual(reference.Next(), rng.Next(), "three draws in all");
        }

        [Test]
        public void DefaultChance_OnUnresistingTarget_TakesNoDraw()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero);
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.DebuffStat, AffectedStat = StatType.Speed, Magnitude = 1f });

            System.Random rng = new System.Random(11);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster, rng);

            Assert.AreEqual(9, target.Stats.Speed);
            Assert.AreEqual(new System.Random(11).Next(), rng.Next(), "existing content must replay exactly");
        }

        [Test]
        public void FullResistance_BlocksHostileStatus_ButDrawsOnce()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            BattleUnit boss = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, statusResist: 100);
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Stun, DurationTurns = 1 });

            System.Random rng = new System.Random(5);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { boss }), caster, rng);

            Assert.IsFalse(StatusEffects.IsStunned(boss));
            System.Random reference = new System.Random(5);
            reference.Next(100);
            Assert.AreEqual(reference.Next(), rng.Next());
        }

        [Test]
        public void SeededChance_IsReproducible()
        {
            int landed1 = CountLandings(99);
            int landed2 = CountLandings(99);

            Assert.AreEqual(landed1, landed2);
            Assert.Greater(landed1, 0);
            Assert.Less(landed1, 200);
        }

        // ------------------------------------------------------------------ taunt

        [Test]
        public void Taunt_ForcesTheRangeLimitedPick()
        {
            BattleUnit taunted = Unit("e1", BattleTeam.Enemy, new HexCoordinate(0, 0));
            BattleUnit near = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            BattleUnit taunter = Unit("p2", BattleTeam.Player, new HexCoordinate(3, 0));
            List<BattleUnit> all = new List<BattleUnit> { taunted, near, taunter };
            SkillSO strike = Picking(4);

            Assert.AreSame(near, SkillTargetResolver.ResolveTargets(strike, taunted, all, null, null)[0], "untaunted: nearest");

            Taunt(taunter, taunted);

            Assert.AreSame(taunter, StatusEffects.GetTaunter(taunted));
            Assert.AreSame(taunter, SkillTargetResolver.ResolveTargets(strike, taunted, all, null, null)[0]);
        }

        [Test]
        public void Taunt_OutOfRange_FallsBackInRange_ButIsTheFocusToApproach()
        {
            BattleUnit taunted = Unit("e1", BattleTeam.Enemy, new HexCoordinate(0, 0));
            BattleUnit near = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            BattleUnit taunter = Unit("p2", BattleTeam.Player, new HexCoordinate(4, 0));
            List<BattleUnit> all = new List<BattleUnit> { taunted, near, taunter };
            SkillSO strike = Picking(1);

            Taunt(taunter, taunted);

            Assert.AreSame(near, SkillTargetResolver.ResolveTargets(strike, taunted, all, null, null)[0]);
            Assert.AreSame(taunter, SkillTargetResolver.PickFocusIgnoringRange(strike, taunted, all, null));
        }

        [Test]
        public void Taunt_MakesTheExecutorApproachTheTaunter()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit taunted = Place(grid, Unit("e1", BattleTeam.Enemy, new HexCoordinate(-2, 0), moveRange: 6, skill: Picking(1)));
            BattleUnit near = Place(grid, Unit("p1", BattleTeam.Player, new HexCoordinate(-1, 0), hp: 500));
            BattleUnit taunter = Place(grid, Unit("p2", BattleTeam.Player, new HexCoordinate(3, 0), hp: 500));
            List<BattleUnit> all = new List<BattleUnit> { taunted, near, taunter };

            Taunt(taunter, taunted);
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(taunted, all, grid, null, null);

            Assert.AreEqual(1, turn.SkillOutcomes.Count);
            Assert.AreEqual(BattleSkillStatus.Fired, turn.SkillOutcomes[0].Status);
            Assert.AreSame(taunter, turn.SkillOutcomes[0].Activation.Targets[0]);
            Assert.AreEqual(1, taunted.Position.Distance(taunter.Position));
            Assert.AreEqual(500, near.CurrentHp, "the adjacent unit was ignored");
            Assert.Less(taunter.CurrentHp, 500);
        }

        [Test]
        public void Taunt_LatestWins_AndAFallenTaunterForcesNothing()
        {
            BattleUnit taunted = Unit("e1", BattleTeam.Enemy, new HexCoordinate(0, 0));
            BattleUnit first = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            BattleUnit second = Unit("p2", BattleTeam.Player, new HexCoordinate(2, 0));

            Taunt(first, taunted);
            Taunt(second, taunted);

            Assert.AreSame(second, StatusEffects.GetTaunter(taunted));
            Assert.AreEqual(1, StatusEffects.Count(taunted, StatusType.Taunt));

            second.IsDefeated = true;

            Assert.IsNull(StatusEffects.GetTaunter(taunted));
            List<BattleUnit> all = new List<BattleUnit> { taunted, first, second };
            Assert.AreSame(first, SkillTargetResolver.ResolveTargets(Picking(4), taunted, all, null, null)[0]);
        }

        [Test]
        public void Taunt_DoesNotRedirectAllySideSkills()
        {
            BattleUnit taunted = Unit("e1", BattleTeam.Enemy, new HexCoordinate(0, 0));
            BattleUnit taunter = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            List<BattleUnit> all = new List<BattleUnit> { taunted, taunter };
            SkillSO heal = Picking(4);
            heal.TargetSide = SkillTargetSide.Ally;

            Taunt(taunter, taunted);

            Assert.AreSame(taunted, SkillTargetResolver.ResolveTargets(heal, taunted, all, null, null)[0]);
        }

        [Test]
        public void Taunt_ExpiresAfterItsDurationInTheTauntedUnitsTurns()
        {
            BattleUnit taunted = Unit("e1", BattleTeam.Enemy, new HexCoordinate(0, 0));
            BattleUnit taunter = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            List<BattleUnit> all = new List<BattleUnit> { taunted, taunter };

            Taunt(taunter, taunted, 1);
            BattleTurnExecutor.ExecuteTurn(taunted, all, null, null, null);

            Assert.IsNull(StatusEffects.GetTaunter(taunted));
        }

        // ------------------------------------------------------------------ stun

        [Test]
        public void Stun_SkipsTheTurn_NoMoveNoSkills_CooldownsDoNotTick()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            SkillSO slow = Picking(1);
            slow.Cooldown = 2;
            BattleUnit unit = Place(grid, Unit("e1", BattleTeam.Enemy, new HexCoordinate(-3, 0), moveRange: 4, skill: slow));
            BattleUnit target = Place(grid, Unit("p1", BattleTeam.Player, new HexCoordinate(3, 0)));
            List<BattleUnit> all = new List<BattleUnit> { unit, target };

            ApplyStatus(target, unit, StatusType.Stun, 1);
            Assert.AreEqual(2, unit.Skills.RemainingCooldown(0));

            BattleTurnResult stunned = BattleTurnExecutor.ExecuteTurn(unit, all, grid, null, null);

            Assert.IsTrue(stunned.Stunned);
            Assert.AreEqual(0, stunned.SkillOutcomes.Count);
            Assert.AreEqual(0, stunned.MovementSpent);
            Assert.AreEqual(new HexCoordinate(-3, 0), unit.Position);
            Assert.AreEqual(2, unit.Skills.RemainingCooldown(0), "a stun delays the rotation");
            Assert.IsFalse(StatusEffects.IsStunned(unit), "a one-turn stun is gone after the turn it skipped");

            BattleTurnResult next = BattleTurnExecutor.ExecuteTurn(unit, all, grid, null, null);

            Assert.IsFalse(next.Stunned);
            Assert.AreEqual(1, unit.Skills.RemainingCooldown(0), "ticking again once the stun is over");
        }

        [Test]
        public void Stun_ModifiersStillTick_AndATwoTurnStunSkipsTwoTurns()
        {
            BattleUnit unit = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, skill: Picking(1));
            BattleUnit caster = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0));
            List<BattleUnit> all = new List<BattleUnit> { unit, caster };

            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.DebuffStat, AffectedStat = StatType.Speed, Magnitude = 3f, DurationTurns = 1 }), caster,
                 unit);
            ApplyStatus(caster, unit, StatusType.Stun, 2);

            Assert.IsTrue(BattleTurnExecutor.ExecuteTurn(unit, all, null, null, null).Stunned);
            Assert.AreEqual(10, unit.Stats.Speed, "the debuff expired on the stunned turn");
            Assert.IsTrue(BattleTurnExecutor.ExecuteTurn(unit, all, null, null, null).Stunned);
            Assert.IsFalse(BattleTurnExecutor.ExecuteTurn(unit, all, null, null, null).Stunned);
        }

        // ------------------------------------------------------------------ shield

        [Test]
        public void Shield_IsAPercentOfTheCastersDefense_AndAbsorbsBeforeHp()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, defense: 50);
            BattleUnit ally = Unit("p2", BattleTeam.Player, HexCoordinate.Zero, hp: 100);
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, attack: 100);

            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 40f, DurationTurns = 2 }), caster,
                 ally);
            Assert.AreEqual(20, StatusEffects.ShieldPoints(ally), "40% of Defense 50");

            SkillSO hit = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f });
            int damage = DamageFormula.Compute(enemy, ally, hit, 50f);
            SkillActivation activation = new SkillActivation(hit, new[] { ally });
            SkillEffectApplier.Apply(activation, enemy);

            Assert.Greater(damage, 20, "test setup: the hit breaks the shield");
            Assert.AreEqual(100 - (damage - 20), ally.CurrentHp);
            Assert.AreEqual(20, activation.Hits[0].Absorbed);
            Assert.AreEqual(0, StatusEffects.ShieldPoints(ally));
            Assert.AreEqual(0, StatusEffects.Count(ally, StatusType.Shield), "a broken shield is removed");
        }

        [Test]
        public void Shield_SmallHitLeavesHpUntouched()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, defense: 200);
            BattleUnit ally = Unit("p2", BattleTeam.Player, HexCoordinate.Zero, hp: 100);
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, attack: 10);

            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 100f, DurationTurns = 2 }), caster,
                 ally);
            SkillSO hit = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f });
            int damage = DamageFormula.Compute(enemy, ally, hit, 50f);
            SkillEffectApplier.Apply(new SkillActivation(hit, new[] { ally }), enemy);

            Assert.AreEqual(100, ally.CurrentHp);
            Assert.AreEqual(200 - damage, StatusEffects.ShieldPoints(ally));
        }

        [Test]
        public void Shield_LargestWins()
        {
            BattleUnit weak = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, defense: 10);
            BattleUnit strong = Unit("p3", BattleTeam.Player, HexCoordinate.Zero, defense: 30);
            BattleUnit ally = Unit("p2", BattleTeam.Player, HexCoordinate.Zero);
            SkillSO shield = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 100f, DurationTurns = 2 });

            Fire(shield, strong, ally);
            Fire(shield, weak, ally);
            Assert.AreEqual(30, StatusEffects.ShieldPoints(ally), "a smaller shield does not replace a larger one");
            Assert.AreEqual(1, StatusEffects.Count(ally, StatusType.Shield));

            BattleUnit stronger = Unit("p4", BattleTeam.Player, HexCoordinate.Zero, defense: 45);
            Fire(shield, stronger, ally);
            Assert.AreEqual(45, StatusEffects.ShieldPoints(ally));
            Assert.AreEqual(1, StatusEffects.Count(ally, StatusType.Shield));
        }

        [Test]
        public void Shield_ExpiresAfterItsDuration_ASelfShieldKeepsTheTurnItWasCastIn()
        {
            BattleUnit unit = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, defense: 50);
            SkillSO shield = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 100f, DurationTurns = 1 });

            Fire(shield, unit, unit);
            StatusEffects.EndTurn(unit);
            Assert.AreEqual(50, StatusEffects.ShieldPoints(unit), "cast during its own turn: that turn does not count");

            StatusEffects.BeginTurn(unit, out bool _);
            Assert.AreEqual(50, StatusEffects.ShieldPoints(unit), "in force for its one turn");
            StatusEffects.EndTurn(unit);
            Assert.AreEqual(0, StatusEffects.ShieldPoints(unit));
        }

        [Test]
        public void Shield_ScalesWithSkillLevel()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, defense: 100);
            BattleUnit ally = Unit("p2", BattleTeam.Player, HexCoordinate.Zero);
            SkillSO shield = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 20f, DurationTurns = 2 });
            SkillInstance leveled = new SkillInstance(shield, 5);

            SkillEffectApplier.Apply(new SkillActivation(leveled, new[] { ally }), caster);

            Assert.AreEqual((int)(leveled.ScaleMagnitude(20f) * 100.0 / 100.0), StatusEffects.ShieldPoints(ally));
            Assert.Greater(StatusEffects.ShieldPoints(ally), 20);
        }

        // ------------------------------------------------------------------ damage over time

        [Test]
        public void DamageOverTime_IsSnapshotted_AndDealtAtTheAffectedUnitsTurnStart()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 500, defense: 100);
            SkillSO burn = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 50f, DurationTurns = 2 });

            Fire(burn, caster, target);
            Assert.AreEqual(500, target.CurrentHp, "nothing on application");

            StatBlock boosted = caster.Stats;
            boosted.Attack = 1000;
            caster.Stats = boosted;

            List<BattleUnit> all = new List<BattleUnit> { caster, target };
            BattleTurnResult first = BattleTurnExecutor.ExecuteTurn(target, all, null, null, null);

            Assert.AreEqual(25, first.StatusDamage, "50% power, A 100 vs D 100, fixed when applied");
            Assert.AreEqual(475, target.CurrentHp);

            BattleTurnExecutor.ExecuteTurn(target, all, null, null, null);
            Assert.AreEqual(450, target.CurrentHp);

            BattleTurnResult third = BattleTurnExecutor.ExecuteTurn(target, all, null, null, null);
            Assert.AreEqual(0, third.StatusDamage, "a two-turn burn deals twice");
            Assert.AreEqual(450, target.CurrentHp);
        }

        [Test]
        public void DamageOverTime_StacksUpToTheCap_EachStackDeals()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 500, defense: 100);
            SkillSO poison = Skill(new SkillEffect
            {
                EffectType = SkillEffectType.ApplyStatus,
                Status = StatusType.DamageOverTime,
                Magnitude = 50f,
                DurationTurns = 3,
                MaxStacks = 3
            });

            for (int i = 0; i < 4; i++)
            {
                Fire(poison, caster, target);
            }

            Assert.AreEqual(3, StatusEffects.Count(target, StatusType.DamageOverTime));
            Assert.AreEqual(75, StatusEffects.BeginTurn(target, out bool _));
        }

        [Test]
        public void DamageOverTime_DefaultCapRefreshes()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 500, defense: 100);
            SkillSO burn = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 50f, DurationTurns = 3 });

            Fire(burn, caster, target);
            StatusEffects.BeginTurn(target, out bool _);
            StatusEffects.EndTurn(target);
            Fire(burn, caster, target);

            Assert.AreEqual(1, StatusEffects.Count(target, StatusType.DamageOverTime));
            Assert.AreEqual(3, target.Statuses[0].RemainingTurns, "refreshed to a full duration");
        }

        [Test]
        public void DamageOverTime_ThatDefeatsTheUnit_EndsItsTurnBeforeItActs()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, new HexCoordinate(1, 0), attack: 100, hp: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 500, defense: 100, skill: Picking(1));
            SkillSO burn = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 50f, DurationTurns = 2 });

            Fire(burn, caster, target);
            target.CurrentHp = 10;
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(target, new List<BattleUnit> { caster, target }, null, null, null);

            Assert.IsTrue(target.IsDefeated);
            Assert.AreEqual(10, turn.StatusDamage);
            Assert.AreEqual(0, turn.SkillOutcomes.Count);
            Assert.AreEqual(100, caster.CurrentHp);
        }

        [Test]
        public void DamageOverTime_IsSoakedByAShield()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero, hp: 500, defense: 100);
            BattleUnit shielder = Unit("e2", BattleTeam.Enemy, HexCoordinate.Zero, defense: 10);

            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 50f, DurationTurns = 2 }),
                 caster, target);
            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 100f, DurationTurns = 2 }), shielder,
                 target);

            Assert.AreEqual(15, StatusEffects.BeginTurn(target, out bool _), "25 less the 10-point shield");
            Assert.AreEqual(485, target.CurrentHp);
        }

        // ------------------------------------------------------------------ knockback

        [Test]
        public void Knockback_PushesDirectlyAway()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit caster = Place(grid, Unit("p1", BattleTeam.Player, new HexCoordinate(0, 0)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, new HexCoordinate(1, 0)));

            Knock(caster, target, 2, grid);

            Assert.AreEqual(new HexCoordinate(3, 0), target.Position);
            Assert.IsTrue(grid.TryGetPosition("e1", out HexCoordinate onGrid));
            Assert.AreEqual(new HexCoordinate(3, 0), onGrid);
        }

        [Test]
        public void Knockback_StopsAtABlockedOrOccupiedTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit caster = Place(grid, Unit("p1", BattleTeam.Player, new HexCoordinate(0, 0)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, new HexCoordinate(1, 0)));
            grid.SetBlocked(new HexCoordinate(3, 0), true);

            Knock(caster, target, 3, grid);
            Assert.AreEqual(new HexCoordinate(2, 0), target.Position);

            Place(grid, Unit("e2", BattleTeam.Enemy, new HexCoordinate(-2, 0)));
            BattleUnit other = Place(grid, Unit("e3", BattleTeam.Enemy, new HexCoordinate(-1, 0)));
            Knock(caster, other, 2, grid);
            Assert.AreEqual(new HexCoordinate(-1, 0), other.Position, "an occupied tile stops it dead");
        }

        [Test]
        public void Knockback_StopsAtTheBoardEdge_AndDoesNothingWithoutAGrid()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit caster = Place(grid, Unit("p1", BattleTeam.Player, new HexCoordinate(3, 0)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, new HexCoordinate(4, 0)));

            Knock(caster, target, 5, grid);
            Assert.AreEqual(new HexCoordinate(5, 0), target.Position, "Medium radius 5");

            BattleUnit loose = Unit("e2", BattleTeam.Enemy, new HexCoordinate(1, 0));
            Knock(Unit("p2", BattleTeam.Player, HexCoordinate.Zero), loose, 2, null);
            Assert.AreEqual(new HexCoordinate(1, 0), loose.Position);
        }

        [Test]
        public void AwayDirection_IsTheBestAlignedAxis_TiesToTheEarlierAxis()
        {
            Assert.AreEqual(new HexCoordinate(1, 0), StatusEffects.AwayDirection(HexCoordinate.Zero, new HexCoordinate(3, -1)));
            Assert.AreEqual(new HexCoordinate(1, 0), StatusEffects.AwayDirection(HexCoordinate.Zero, new HexCoordinate(2, -1)), "exact bisector");
            Assert.AreEqual(new HexCoordinate(0, 1), StatusEffects.AwayDirection(HexCoordinate.Zero, new HexCoordinate(0, 2)));
            Assert.AreEqual(new HexCoordinate(-1, 1), StatusEffects.AwayDirection(new HexCoordinate(1, 0), new HexCoordinate(-1, 2)));
            Assert.IsNull(StatusEffects.AwayDirection(HexCoordinate.Zero, HexCoordinate.Zero));
        }

        // ------------------------------------------------------------------ helpers

        private int CountLandings(int seed)
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, HexCoordinate.Zero);
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Stun, DurationTurns = 1, Chance = 40 });
            System.Random rng = new System.Random(seed);
            int landed = 0;

            for (int i = 0; i < 200; i++)
            {
                BattleUnit target = Unit("e1", BattleTeam.Enemy, HexCoordinate.Zero);
                SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster, rng);
                landed += StatusEffects.IsStunned(target) ? 1 : 0;
            }

            return landed;
        }

        private void Taunt(BattleUnit taunter, BattleUnit target, int duration = 3)
        {
            ApplyStatus(taunter, target, StatusType.Taunt, duration);
        }

        private void ApplyStatus(BattleUnit caster, BattleUnit target, StatusType status, int duration)
        {
            Fire(Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = status, DurationTurns = duration }), caster, target);
        }

        private void Knock(BattleUnit caster, BattleUnit target, int hexes, HexGrid grid)
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Knockback, Magnitude = hexes });
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster, null, grid);
        }

        private static void Fire(SkillSO skill, BattleUnit caster, BattleUnit target)
        {
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster);
        }

        private SkillSO Skill(params SkillEffect[] effects)
        {
            SkillSO skill = new SkillSO();
            skill.Effects.AddRange(effects);
            _created.Add(skill);
            return skill;
        }

        private SkillSO Picking(int range)
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f });
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            skill.Range = range;
            skill.Cooldown = 1;
            return skill;
        }

        private static BattleUnit Place(HexGrid grid, BattleUnit unit)
        {
            Assert.IsTrue(grid.TryPlaceUnit(unit.Id, unit.Position), "test setup: could not place " + unit.Id);
            return unit;
        }

        private static BattleUnit Unit(string id, BattleTeam team, HexCoordinate position, int hp = 100, int attack = 10, int defense = 10,
                                       int moveRange = 3, int statusResist = 0, SkillSO skill = null)
        {
            StatBlock stats = new StatBlock(hp, attack, defense, 10, 10, 10, moveRange);
            SkillLoadout loadout = new SkillLoadout(skill == null ? null : new[] { skill });
            return new BattleUnit(id, team, stats, position, loadout, null, 1, CombatStance.Vanguard, statusResist);
        }
    }
}
