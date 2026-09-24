using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="BattleTurnExecutor"/>'s two board rules: defeated units leave the grid the moment
    /// they fall, and a unit whose target is out of reach this turn makes a partial approach.
    /// <para>
    /// Most tests run on a "corridor": a Medium board (radius 5) with every tile off the
    /// <c>R == 0</c> row blocked by terrain, so the only walkable tiles are <c>(-5, 0)</c> to
    /// <c>(5, 0)</c> in a straight line and every route is forced. Every skill aims at the nearest
    /// eligible unit and has cooldown 1, so it is offered every turn. Units have 10 in every combat
    /// stat at level 1, so a power-10 hit deals a couple of points: HP 1 dies to anything, HP 500
    /// survives everything here.
    /// </para>
    /// </summary>
    public class BattleTurnExecutorMovementTests
    {
        private const int Sturdy = 500;
        private const int Fragile = 1;

        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Defeated units leave the grid.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void DefeatedUnit_TileIsFreed_AndReusableInTheSameTurn()
        {
            HexGrid grid = Corridor();
            // Slot 0 (range 1) kills the adjacent blocker; slot 1 (range 2) then wants the unit
            // behind it, and the first in-range tile is the one the blocker just died on.
            BattleUnit attacker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 3, At(-2), Skill(1), Skill(2)));
            BattleUnit blocker = Place(grid, Unit("e1", BattleTeam.Enemy, Fragile, 0, At(-1)));
            BattleUnit behind = Place(grid, Unit("e2", BattleTeam.Enemy, Sturdy, 0, At(1)));
            List<BattleUnit> all = new List<BattleUnit> { attacker, blocker, behind };

            BattleTurnResult result = Turn(attacker, all, grid);

            Assert.IsTrue(blocker.IsDefeated);
            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.IsTrue(result.SkillOutcomes[1].Fired);
            Assert.AreEqual(1, result.SkillOutcomes[1].MovementSpent);
            Assert.AreEqual(At(-1), attacker.Position);
            Assert.AreEqual("a", grid.GetOccupant(At(-1)));
            Assert.Less(behind.CurrentHp, Sturdy);
        }

        [Test]
        public void DefeatedUnit_KeepsItsPosition_ButIsNoLongerOnTheGrid()
        {
            HexGrid grid = Corridor();
            BattleUnit attacker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 0, At(0), Skill(1)));
            BattleUnit victim = Place(grid, Unit("e1", BattleTeam.Enemy, Fragile, 0, At(1)));

            Turn(attacker, new List<BattleUnit> { attacker, victim }, grid);

            Assert.IsTrue(victim.IsDefeated);
            Assert.AreEqual(At(1), victim.Position);
            Assert.IsNull(grid.GetOccupant(At(1)));
            Assert.IsFalse(grid.TryGetPosition("e1", out HexCoordinate _));
            Assert.IsTrue(grid.IsPassable(At(1), "a"));
        }

        [Test]
        public void DefeatedUnit_OpensAChoke_ForLaterTurns()
        {
            HexGrid grid = Corridor();
            // The runner's only route runs through its own ally, so it cannot go anywhere until the
            // enemy's ranged hit kills that ally.
            BattleUnit runner = Place(grid, Unit("p1", BattleTeam.Player, Sturdy, 5, At(-4), Skill(1)));
            BattleUnit plug = Place(grid, Unit("p2", BattleTeam.Player, Fragile, 0, At(-1)));
            BattleUnit enemy = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(2), Skill(3)));
            List<BattleUnit> all = new List<BattleUnit> { runner, plug, enemy };

            BattleTurnResult blocked = Turn(runner, all, grid);

            Assert.AreEqual(BattleSkillStatus.Unreachable, blocked.SkillOutcomes[0].Status);
            Assert.AreEqual(At(-4), runner.Position);
            Assert.AreEqual(0, blocked.MovementSpent);

            Turn(enemy, all, grid);

            Assert.IsTrue(plug.IsDefeated);
            Assert.IsNull(grid.GetOccupant(At(-1)));

            BattleTurnResult through = Turn(runner, all, grid);

            Assert.IsTrue(through.SkillOutcomes[0].Fired);
            Assert.AreEqual(5, through.MovementSpent);
            Assert.AreEqual(At(1), runner.Position);
            Assert.Less(enemy.CurrentHp, Sturdy);
        }

        [Test]
        public void AvatarActivation_ThatDefeatsAUnit_FreesItsTile()
        {
            HexGrid grid = Corridor();
            BattleUnit beast = Place(grid, Unit("p1", BattleTeam.Player, Sturdy, 0, At(-4)));
            BattleUnit victim = Place(grid, Unit("e1", BattleTeam.Enemy, Fragile, 0, At(3)));
            BattleUnit avatar = Unit("avatar", BattleTeam.Player, Sturdy, 0, HexCoordinate.Zero,
                                     Skill(0, SkillTargetShape.AllEnemies));
            List<BattleUnit> roster = new List<BattleUnit> { beast, victim };

            // A player beast's turn no longer ticks the avatar: it casts on its own turns.
            BattleTurnResult beastTurn = BattleTurnExecutor.ExecuteTurn(beast, roster, grid, new System.Random(1), avatar);
            Assert.AreEqual(0, beastTurn.AvatarActivations.Count);
            Assert.IsFalse(victim.IsDefeated);

            BattleTurnResult result = BattleTurnExecutor.ExecuteAvatarTurn(avatar, roster, grid, new System.Random(1), null);

            Assert.AreSame(avatar, result.Unit);
            Assert.AreEqual(1, result.AvatarActivations.Count);
            Assert.AreEqual(0, result.SkillOutcomes.Count);
            Assert.AreEqual(0, result.MovementSpent);
            Assert.IsTrue(victim.IsDefeated);
            Assert.IsNull(grid.GetOccupant(At(3)));
        }

        [Test]
        public void ExecuteTurn_HandedTheAvatar_RunsTheAvatarsOwnTurn()
        {
            HexGrid grid = Corridor();
            BattleUnit beast = Place(grid, Unit("p1", BattleTeam.Player, Sturdy, 0, At(-4)));
            BattleUnit victim = Place(grid, Unit("e1", BattleTeam.Enemy, Fragile, 0, At(3)));
            BattleUnit avatar = Unit("avatar", BattleTeam.Player, Sturdy, 0, HexCoordinate.Zero,
                                     Skill(0, SkillTargetShape.AllEnemies));

            BattleTurnResult result = BattleTurnExecutor.ExecuteTurn(avatar, new List<BattleUnit> { beast, victim }, grid, new System.Random(1), avatar);

            Assert.AreSame(avatar, result.Unit);
            Assert.AreEqual(1, result.AvatarActivations.Count);
            Assert.AreEqual(0, result.SkillOutcomes.Count, "not a beast's turn: its loadout is not fired as a beast's");
            Assert.IsTrue(victim.IsDefeated);
            Assert.IsNull(grid.GetOccupant(At(3)));
        }

        [Test]
        public void NullGrid_DefeatStillLands_AndNothingThrows()
        {
            BattleUnit attacker = Unit("a", BattleTeam.Player, Sturdy, 3, At(0), Skill(1));
            BattleUnit victim = Unit("e1", BattleTeam.Enemy, Fragile, 0, At(1));

            BattleTurnResult result = Turn(attacker, new List<BattleUnit> { attacker, victim }, null);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.IsTrue(victim.IsDefeated);
            Assert.AreEqual(At(1), victim.Position);
        }

        // ---------------------------------------------------------------------------------------
        // Partial approach.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PartialApproach_WalksExactlyTheBudget_AndDoesNotFire()
        {
            HexGrid grid = Corridor();
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 2, At(-5), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(3)));

            BattleTurnResult result = Turn(walker, new List<BattleUnit> { walker, target }, grid);

            BattleSkillOutcome outcome = result.SkillOutcomes[0];
            Assert.AreEqual(BattleSkillStatus.OutOfMovement, outcome.Status);
            Assert.IsFalse(outcome.Fired);
            Assert.IsNull(outcome.Activation);
            Assert.AreEqual(2, outcome.MovementSpent);
            Assert.AreEqual(2, result.MovementSpent);
            Assert.AreEqual(0, result.MovementRemaining);
            Assert.AreEqual(At(-5), result.StartPosition);
            Assert.AreEqual(At(-3), result.EndPosition);
            Assert.AreEqual(At(-3), walker.Position);
            Assert.AreEqual("a", grid.GetOccupant(At(-3)));
            Assert.IsNull(grid.GetOccupant(At(-5)));
            Assert.AreEqual(0, walker.Skills.RemainingCooldown(0), "a held slot keeps its 0");
            Assert.AreEqual(Sturdy, target.CurrentHp);
        }

        [Test]
        public void PartialApproach_FiresOnALaterTurn_OnceInRange()
        {
            HexGrid grid = Corridor();
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 2, At(-3), Skill(1, cooldown: 2)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(2)));
            List<BattleUnit> all = new List<BattleUnit> { walker, target };

            // Cooldown 2: the first tick only takes it to 1, so turn 1 offers nothing and moves nothing.
            BattleTurnResult idle = Turn(walker, all, grid);
            Assert.AreEqual(0, idle.SkillOutcomes.Count);
            Assert.AreEqual(At(-3), walker.Position);

            BattleTurnResult first = Turn(walker, all, grid);
            Assert.AreEqual(BattleSkillStatus.OutOfMovement, first.SkillOutcomes[0].Status);
            Assert.AreEqual(At(-1), walker.Position);

            BattleTurnResult second = Turn(walker, all, grid);
            Assert.IsTrue(second.SkillOutcomes[0].Fired);
            Assert.AreEqual(2, second.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(At(1), walker.Position);
            Assert.AreEqual(2, walker.Skills.RemainingCooldown(0), "firing re-arms the slot");
            Assert.Less(target.CurrentHp, Sturdy);
        }

        [Test]
        public void FullyBlocked_DoesNotMove()
        {
            HexGrid grid = Corridor();
            grid.SetBlocked(At(-2), true);
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 4, At(-4), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(2)));

            BattleTurnResult result = Turn(walker, new List<BattleUnit> { walker, target }, grid);

            Assert.AreEqual(BattleSkillStatus.Unreachable, result.SkillOutcomes[0].Status);
            Assert.AreEqual(0, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(0, result.MovementSpent);
            Assert.AreEqual(At(-4), walker.Position);
            Assert.AreEqual("a", grid.GetOccupant(At(-4)));
            Assert.AreEqual(0, walker.Skills.RemainingCooldown(0));
        }

        [Test]
        public void SharedBudget_PartialApproachFirst_ThenALaterSlotFiresFromTheNewTile()
        {
            HexGrid grid = Corridor();
            // Slot 0 (range 1) needs 8 steps: it advances the whole budget of 3 and holds. Slot 1
            // (range 6) is then 6 away from the new tile, so it fires without moving.
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 3, At(-5), Skill(1), Skill(6)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(4)));

            BattleTurnResult result = Turn(walker, new List<BattleUnit> { walker, target }, grid);

            Assert.AreEqual(BattleSkillStatus.OutOfMovement, result.SkillOutcomes[0].Status);
            Assert.AreEqual(3, result.SkillOutcomes[0].MovementSpent);
            Assert.IsTrue(result.SkillOutcomes[1].Fired);
            Assert.AreEqual(0, result.SkillOutcomes[1].MovementSpent);
            Assert.AreEqual(3, result.MovementSpent);
            Assert.AreEqual(At(-2), walker.Position);
            Assert.AreEqual(0, walker.Skills.RemainingCooldown(0));
            Assert.AreEqual(1, walker.Skills.RemainingCooldown(1));
        }

        [Test]
        public void SharedBudget_SpentByAnEarlierSlot_LeavesALaterSlotHeldInPlace()
        {
            HexGrid grid = Corridor();
            // Slot 0 (range 6) needs exactly the 3-step budget and fires; slot 1 (range 1) then has
            // nothing left, so it holds with no movement.
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, 3, At(-5), Skill(6), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(4)));

            BattleTurnResult result = Turn(walker, new List<BattleUnit> { walker, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(3, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(BattleSkillStatus.OutOfMovement, result.SkillOutcomes[1].Status);
            Assert.AreEqual(0, result.SkillOutcomes[1].MovementSpent);
            Assert.AreEqual(3, result.MovementSpent);
            Assert.AreEqual(At(-2), walker.Position);
        }

        [Test]
        public void PartialApproach_OnAnOpenBoard_ClosesByTheBudget_Deterministically()
        {
            // On an open board the cheapest route is a straight run, so every step of a partial
            // approach is a step closer; and the same board must always give the same tile.
            BattleUnit first = OpenBoardWalker(2, out HexGrid firstGrid, out List<BattleUnit> firstAll);
            BattleUnit second = OpenBoardWalker(2, out HexGrid secondGrid, out List<BattleUnit> secondAll);
            HexCoordinate start = first.Position;
            int before = start.Distance(firstAll[1].Position);

            BattleTurnResult result = Turn(first, firstAll, firstGrid);
            Turn(second, secondAll, secondGrid);

            Assert.AreEqual(BattleSkillStatus.OutOfMovement, result.SkillOutcomes[0].Status);
            Assert.AreEqual(2, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(2, start.Distance(first.Position));
            Assert.AreEqual(before - 2, first.Position.Distance(firstAll[1].Position));
            Assert.AreEqual(first.Position, second.Position);
        }

        [Test]
        public void NullGrid_OutOfRangeTarget_DoesNotMove()
        {
            BattleUnit walker = Unit("a", BattleTeam.Player, Sturdy, 3, At(-5), Skill(1));
            BattleUnit target = Unit("e1", BattleTeam.Enemy, Sturdy, 0, At(5));

            BattleTurnResult result = Turn(walker, new List<BattleUnit> { walker, target }, null);

            Assert.AreEqual(BattleSkillStatus.Unreachable, result.SkillOutcomes[0].Status);
            Assert.AreEqual(0, result.MovementSpent);
            Assert.AreEqual(At(-5), walker.Position);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        private BattleUnit OpenBoardWalker(int moveRange, out HexGrid grid, out List<BattleUnit> all)
        {
            grid = new HexGrid(ArenaSize.Medium);
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, Sturdy, moveRange, new HexCoordinate(-2, 4), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, Sturdy, 0, new HexCoordinate(3, -4)));
            all = new List<BattleUnit> { walker, target };
            return walker;
        }

        private static BattleTurnResult Turn(BattleUnit unit, List<BattleUnit> all, HexGrid grid)
        {
            return BattleTurnExecutor.ExecuteTurn(unit, all, grid, new System.Random(1), null);
        }

        private static HexCoordinate At(int q)
        {
            return new HexCoordinate(q, 0);
        }

        /// <summary>A Medium board whose only walkable tiles are the <c>R == 0</c> row.</summary>
        private static HexGrid Corridor()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            foreach (HexCoordinate tile in new List<HexCoordinate>(grid.Tiles))
            {
                if (tile.R != 0)
                {
                    grid.SetBlocked(tile, true);
                }
            }

            return grid;
        }

        private static BattleUnit Place(HexGrid grid, BattleUnit unit)
        {
            Assert.IsTrue(grid.TryPlaceUnit(unit.Id, unit.Position), "test setup: could not place " + unit.Id);
            return unit;
        }

        private static BattleUnit Unit(string id, BattleTeam team, int hp, int moveRange, HexCoordinate position, params SkillSO[] skills)
        {
            return new BattleUnit(id, team, new StatBlock(hp, 10, 10, 10, 10, 10, moveRange), position, new SkillLoadout(skills));
        }

        private SkillSO Skill(int range, SkillTargetShape shape = SkillTargetShape.SingleTarget, int cooldown = 1)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            skill.TargetShape = shape;
            skill.Range = range;
            skill.Cooldown = cooldown;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            _created.Add(skill);
            return skill;
        }
    }
}
