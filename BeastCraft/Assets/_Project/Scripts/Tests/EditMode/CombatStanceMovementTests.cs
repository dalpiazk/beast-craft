using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="BattleTurnExecutor"/>'s combat stances: a Ranged unit never walks into melee,
    /// Ranged and Skirmisher units prefer uncrowded stop tiles and retreat with leftover movement
    /// (never beyond their longest reach), and a Vanguard ignores crowding, screens its fragile
    /// allies and never retreats. The plain-rule behaviour of a Vanguard with no fragile allies is
    /// covered by <see cref="BattleTurnExecutorMovementTests"/>, whose units are all Vanguards.
    /// <para>
    /// Same conventions as that fixture: a "corridor" is a Medium board whose only walkable tiles
    /// are the <c>R == 0</c> row, every skill aims at the nearest eligible unit with cooldown 1,
    /// and units have 10 in every combat stat at level 1, so HP 500 survives everything here.
    /// Enemies have move 0 and no skills: they are only there to be approached or fled.
    /// </para>
    /// </summary>
    public class CombatStanceMovementTests
    {
        private const int Sturdy = 500;

        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Ranged: never walks into melee.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Ranged_MeleeSlot_HoldsWithoutWalking_WhenNoTargetIsAdjacent()
        {
            HexGrid grid = Corridor();
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 4, At(-5), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(0)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, grid);

            Assert.AreEqual(BattleSkillStatus.HeldByStance, result.SkillOutcomes[0].Status);
            Assert.AreEqual(0, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(0, result.MovementSpent, "the enemy is beyond its reach, so no retreat either");
            Assert.AreEqual(At(-5), archer.Position);
            Assert.AreEqual(0, archer.Skills.RemainingCooldown(0), "a held slot keeps its 0");
            Assert.AreEqual(Sturdy, target.CurrentHp);
        }

        [Test]
        public void Vanguard_SameMeleeSlot_WalksIn()
        {
            HexGrid grid = Corridor();
            BattleUnit brawler = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Vanguard, 4, At(-5), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(0)));

            BattleTurnResult result = Turn(brawler, new List<BattleUnit> { brawler, target }, grid);

            Assert.AreEqual(BattleSkillStatus.Fired, result.SkillOutcomes[0].Status);
            Assert.AreEqual(At(-1), brawler.Position);
        }

        [Test]
        public void Ranged_HeldMeleeSlot_LeavesTheBudgetToALaterSlot()
        {
            HexGrid grid = Corridor();
            // Slot 0 (melee) holds for free; slot 1 (range 3) then needs 4 steps and has all 3.
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, At(-5), Skill(1), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(2)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, grid);

            Assert.AreEqual(BattleSkillStatus.HeldByStance, result.SkillOutcomes[0].Status);
            Assert.AreEqual(BattleSkillStatus.OutOfMovement, result.SkillOutcomes[1].Status);
            Assert.AreEqual(3, result.SkillOutcomes[1].MovementSpent);
            Assert.AreEqual(At(-2), archer.Position);
            Assert.AreEqual(0, result.RetreatSteps);
        }

        [Test]
        public void Ranged_MeleeSlot_FiresOnAnAdjacentTarget_ThenRetreatsToTheEdgeOfItsReach()
        {
            HexGrid grid = Corridor();
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, At(0), Skill(3), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(1)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.IsTrue(result.SkillOutcomes[1].Fired, "a melee slot fires when the enemy is already adjacent");
            // Leftover 3: -3 would be distance 4, beyond the longest reach (3), so it stops at -2.
            Assert.AreEqual(At(-2), archer.Position);
            Assert.AreEqual(2, result.RetreatSteps);
            Assert.AreEqual(2, result.MovementSpent);
            Assert.AreEqual(3, archer.Position.Distance(target.Position));
        }

        // ---------------------------------------------------------------------------------------
        // Ranged: approach distance.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Ranged_Approach_StopsAtTheFarthestInRangeTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 5, new HexCoordinate(0, 4), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(0, -4)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(5, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(3, archer.Position.Distance(target.Position), "stops at the edge of range, not closer");
            Assert.AreEqual(0, result.RetreatSteps, "no budget left to retreat with");
        }

        [Test]
        public void Ranged_AlreadyInRange_DoesNotCloseIn_AndBacksOffToItsFullReach()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, new HexCoordinate(0, 1), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(0, -1)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(0, result.SkillOutcomes[0].MovementSpent);
            Assert.AreEqual(1, result.RetreatSteps, "one step already reaches distance 3, and further would leave its reach");
            Assert.AreEqual(3, archer.Position.Distance(target.Position));
        }

        // ---------------------------------------------------------------------------------------
        // Retreat with leftover budget.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Skirmisher_ClosesToMelee_Fires_ThenRetreatsWithinItsLongestReach()
        {
            HexGrid grid = Corridor();
            // Strike (range 1) walks one step in; Blast (range 3) fires from there; the leftover 3
            // buys a retreat to distance 3 (two steps back) and no further.
            BattleUnit skirmisher = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Skirmisher, 4, At(-2), Skill(1), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(0)));

            BattleTurnResult result = Turn(skirmisher, new List<BattleUnit> { skirmisher, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(1, result.SkillOutcomes[0].MovementSpent);
            Assert.IsTrue(result.SkillOutcomes[1].Fired);
            Assert.AreEqual(2, result.RetreatSteps);
            Assert.AreEqual(3, result.MovementSpent);
            Assert.AreEqual(At(-3), skirmisher.Position);
            Assert.AreEqual(At(-3), result.EndPosition);
        }

        [Test]
        public void Skirmisher_WithOnlyMelee_StaysAdjacent_BecauseItsReachIsOne()
        {
            HexGrid grid = Corridor();
            BattleUnit skirmisher = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Skirmisher, 4, At(-2), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(0)));

            BattleTurnResult result = Turn(skirmisher, new List<BattleUnit> { skirmisher, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(0, result.RetreatSteps);
            Assert.AreEqual(At(-1), skirmisher.Position);
        }

        [Test]
        public void Vanguard_NeverRetreats()
        {
            HexGrid grid = Corridor();
            BattleUnit brawler = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Vanguard, 4, At(-2), Skill(1), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(0)));

            BattleTurnResult result = Turn(brawler, new List<BattleUnit> { brawler, target }, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.IsTrue(result.SkillOutcomes[1].Fired);
            Assert.AreEqual(0, result.RetreatSteps);
            Assert.AreEqual(1, result.MovementSpent);
            Assert.AreEqual(3, result.MovementRemaining, "a Vanguard's leftover is discarded");
            Assert.AreEqual(At(-1), brawler.Position);
        }

        [Test]
        public void Retreat_NullGrid_DoesNotMove()
        {
            BattleUnit archer = Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, At(0), Skill(3));
            BattleUnit target = Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, At(1));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target }, null);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(0, result.RetreatSteps);
            Assert.AreEqual(At(0), archer.Position);
        }

        [Test]
        public void Retreat_IsDeterministic()
        {
            HexCoordinate first = RetreatOnOpenBoard();
            HexCoordinate second = RetreatOnOpenBoard();

            Assert.AreEqual(first, second);
        }

        // ---------------------------------------------------------------------------------------
        // Anti-surround and screening.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Skirmisher_PrefersTheLessCrowdedOfTwoEquallyShortApproaches()
        {
            // From (3,-1), (1,0) and (1,-1) are both 2 steps and both next to e1 at (0,0). The plain
            // rule takes (1,0), which e2 at (0,1) also touches; the Skirmisher takes (1,-1).
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit skirmisher = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Skirmisher, 2, new HexCoordinate(3, -1), Skill(1)));
            List<BattleUnit> all = CrowdedTargets(grid, skirmisher);

            BattleTurnResult result = Turn(skirmisher, all, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(2, result.MovementSpent);
            Assert.AreEqual(new HexCoordinate(1, -1), skirmisher.Position);
        }

        [Test]
        public void Vanguard_IgnoresCrowding()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit brawler = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Vanguard, 2, new HexCoordinate(3, -1), Skill(1)));
            List<BattleUnit> all = CrowdedTargets(grid, brawler);

            BattleTurnResult result = Turn(brawler, all, grid);

            Assert.IsTrue(result.SkillOutcomes[0].Fired);
            Assert.AreEqual(new HexCoordinate(1, 0), brawler.Position);
        }

        [Test]
        public void Vanguard_AmongEquallyShortApproaches_StandsNearestItsFragileAlly()
        {
            // Same geometry without e2. A Ranged ally at (2,-3) is 2 from (1,-1) and 3 from (1,0).
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit brawler = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Vanguard, 2, new HexCoordinate(3, -1), Skill(1)));
            BattleUnit ally = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Ranged, 0, new HexCoordinate(2, -3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, HexCoordinate.Zero));

            Turn(brawler, new List<BattleUnit> { brawler, ally, target }, grid);

            Assert.AreEqual(new HexCoordinate(1, -1), brawler.Position);
        }

        [Test]
        public void Vanguard_WithOnlyVanguardAllies_KeepsThePlainRulesPick()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit brawler = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Vanguard, 2, new HexCoordinate(3, -1), Skill(1)));
            BattleUnit ally = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(2, -3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, HexCoordinate.Zero));

            Turn(brawler, new List<BattleUnit> { brawler, ally, target }, grid);

            Assert.AreEqual(new HexCoordinate(1, 0), brawler.Position);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        /// <summary>e1 at the origin (the nearest enemy) and e2 at (0,1), touching the plain rule's stop tile.</summary>
        private List<BattleUnit> CrowdedTargets(HexGrid grid, BattleUnit mover)
        {
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, HexCoordinate.Zero));
            BattleUnit bystander = Place(grid, Unit("e2", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(0, 1)));
            return new List<BattleUnit> { mover, target, bystander };
        }

        private HexCoordinate RetreatOnOpenBoard()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, new HexCoordinate(0, 1), Skill(3)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(0, -1)));
            BattleUnit other = Place(grid, Unit("e2", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(2, -1)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { archer, target, other }, grid);
            Assert.Greater(result.RetreatSteps, 0);
            return archer.Position;
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

        private static BattleUnit Unit(string id, BattleTeam team, CombatStance stance, int moveRange, HexCoordinate position, params SkillSO[] skills)
        {
            return new BattleUnit(id, team, new StatBlock(Sturdy, 10, 10, 10, 10, 10, moveRange), position, new SkillLoadout(skills), null, 1, stance);
        }

        private SkillSO Skill(int range)
        {
            SkillSO skill = new SkillSO();
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.Range = range;
            skill.Cooldown = 1;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            _created.Add(skill);
            return skill;
        }
    }
}
