using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Obstacles in battle: <see cref="BattleSession"/> blocks the picked layout's cells before
    /// anyone is placed, so nobody spawns, walks or is knocked onto one; paths detour round them;
    /// skills pass over them (area effects cover them harmlessly, single-target and line skills reach
    /// past them); and every authored template and boss seats on every shipped layout. A battle with
    /// no region, or a region without layouts, is exactly the open-board battle.
    /// </summary>
    public class ObstacleBattleTests
    {
        private readonly List<SkillSO> _created = new List<SkillSO>();

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        private static HexCellData Cell(int column, int row)
        {
            HexCoordinate hex = HexGrid.FromOffset(column, row);
            return new HexCellData { Q = hex.Q, R = hex.R };
        }

        /// <summary>Two layouts per arena for r01, valid (the validator passes them).</summary>
        private static BattleLayoutData Fixture()
        {
            return new BattleLayoutData
            {
                SchemaVersion = 1,
                Layouts = new[]
                {
                    Entry("a", "Small", Cell(-2, 0), Cell(2, 0)), Entry("b", "Small", Cell(0, -1), Cell(-1, 1)),
                    Entry("a", "Medium", Cell(-3, -1), Cell(2, -1), Cell(-3, 1), Cell(2, 1)), Entry("b", "Medium", Cell(-4, -2), Cell(-3, -1), Cell(2, 1), Cell(3, 2)),
                    Entry("a", "Large", Cell(-4, -2), Cell(3, -2), Cell(-4, 2), Cell(3, 2), Cell(-1, -3), Cell(0, 3)),
                    Entry("b", "Large", Cell(-2, -1), Cell(2, 1), Cell(-3, 1), Cell(1, -1), Cell(0, 0))
                }
            };
        }

        private static BattleLayoutEntryData Entry(string id, string arena, params HexCellData[] cells)
        {
            return new BattleLayoutEntryData { RegionId = "r01", ArtKey = "backdrop/r01/" + id + "/" + arena.ToLowerInvariant(), Arena = arena, Cells = cells };
        }

        /// <summary>Runs <paramref name="body"/> with the shared content's layouts swapped for <paramref name="layouts"/>.</summary>
        private static void WithLayouts(BattleLayoutData layouts, System.Action body)
        {
            BattleLayoutData shipped = Content.Battle.Layouts;
            Content.Battle.Layouts = layouts;
            try
            {
                body();
            }
            finally
            {
                Content.Battle.Layouts = shipped;
            }
        }

        [Test]
        public void Fixture_IsAValidLayoutSet()
        {
            List<string> errors = ObstacleLayoutValidator.Validate(Fixture(), Content.Regions, null, null);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [TestCase("squad", ArenaSize.Small)]
        [TestCase("squad", ArenaSize.Medium)]
        [TestCase("horde", ArenaSize.Large)]
        [TestCase("boss_r01_hollow_warden", null)]
        public void Session_BlocksThePickedLayout_AndNoUnitEverStandsOnAnObstacle(string encounter, ArenaSize? arena)
        {
            WithLayouts(Fixture(), () =>
            {
                HashSet<string> picked = new HashSet<string>();
                for (int seed = 1; seed <= 16; seed++)
                {
                    BattleSetup setup = DemoBattle.Create(Content, seed, out _, out string error, null, encounter, 30, 30, arena, null, "r01");
                    Assert.IsNull(error, error);
                    BattleSessionRun run = BattleSession.Begin(setup);
                    Assert.IsNotNull(run.Battle, run.Result.Error);

                    BattleLayoutEntryData layout = run.Result.Layout;
                    Assert.IsNotNull(layout, "an r01 battle stands on one of r01's layouts");
                    Assert.AreEqual(run.Grid.Size.ToString(), layout.Arena);
                    Assert.AreSame(BattleLayouts.Pick(Content.Battle.Layouts, "r01", run.Grid.Size, seed), layout, "the seeded pick");
                    Assert.AreEqual("r01", run.Result.RegionId);
                    picked.Add(layout.ArtKey);

                    HashSet<HexCoordinate> cells = new HashSet<HexCoordinate>(layout.Coordinates());
                    foreach (HexCoordinate tile in run.Grid.Tiles)
                    {
                        Assert.AreEqual(cells.Contains(tile), run.Grid.IsBlocked(tile), tile.ToString());
                    }

                    AssertNobodyOnAnObstacle(run, "at deployment");
                    for (int turn = 0; turn < 400 && run.Step() != null; turn++)
                    {
                        AssertNobodyOnAnObstacle(run, "after turn " + (turn + 1));
                    }
                }

                Assert.AreEqual(2, picked.Count, "both layouts come up over the seeds");
            });
        }

        [Test]
        public void Session_WithoutARegionOrLayouts_IsTheOpenBoardBattle()
        {
            BattleSessionResult open = BattleSession.Run(DemoBattle.Create(Content, 7, out _, out _, null, "squad", 30, 30));
            WithLayouts(Fixture(), () =>
            {
                BattleSessionResult noRegion = BattleSession.Run(DemoBattle.Create(Content, 7, out _, out _, null, "squad", 30, 30));
                BattleSessionResult otherRegion = BattleSession.Run(DemoBattle.Create(Content, 7, out _, out _, null, "squad", 30, 30, null, null, "r02"));

                foreach (BattleSessionResult result in new[] { noRegion, otherRegion })
                {
                    Assert.IsNull(result.Layout);
                    Assert.AreEqual(open.Outcome, result.Outcome);
                    Assert.AreEqual(open.Battle.Turns.Count, result.Battle.Turns.Count, "the same battle, turn for turn");
                    Assert.AreEqual(open.Battle.ElapsedTicks, result.Battle.ElapsedTicks);
                    foreach (HexCoordinate tile in result.Grid.Tiles)
                    {
                        Assert.IsFalse(result.Grid.IsBlocked(tile));
                    }
                }
            });
        }

        [Test]
        public void Knockback_StopsAtTheLastFreeHex_BeforeAnObstacle()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleLayouts.Apply(grid, Entry("k", "Medium", Cell(3, 0), Cell(-3, 0)));
            BattleUnit caster = Place(grid, Unit("p1", BattleTeam.Player, 0, new HexCoordinate(0, 0)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, 0, new HexCoordinate(1, 0)));

            SkillSO knock = new SkillSO();
            knock.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Knockback, Magnitude = 4 });
            _created.Add(knock);
            SkillEffectApplier.Apply(new SkillActivation(knock, new[] { target }), caster, null, grid);

            Assert.AreEqual(new HexCoordinate(2, 0), target.Position, "pushed two hexes, stopped by the rock at (3, 0)");
            Assert.AreEqual("e1", grid.GetOccupant(new HexCoordinate(2, 0)));
            Assert.IsNull(grid.GetOccupant(new HexCoordinate(3, 0)));
        }

        [Test]
        public void Movement_DetoursRoundAnObstacle()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            grid.SetBlocked(new HexCoordinate(0, 0), true);

            IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, new HexCoordinate(0, 1), new HexCoordinate(0, -1), "walker");
            Assert.AreEqual(4, path.Count, "two steps round the rock instead of the blocked straight line");
            CollectionAssert.DoesNotContain(path, new HexCoordinate(0, 0));

            // A melee attacker walks round it and still lands its hit.
            BattleUnit walker = Place(grid, Unit("a", BattleTeam.Player, 4, new HexCoordinate(0, 2), Skill(1)));
            BattleUnit target = Place(grid, Unit("e1", BattleTeam.Enemy, 0, new HexCoordinate(0, -2)));
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(walker, new List<BattleUnit> { walker, target }, grid, new System.Random(1), null);

            Assert.IsTrue(turn.SkillOutcomes[0].Fired, "it reached its target");
            Assert.AreEqual(1, walker.Position.Distance(target.Position));
            Assert.AreNotEqual(new HexCoordinate(0, 0), walker.Position);
            Assert.Less(target.CurrentHp, 500);
        }

        [Test]
        public void Skills_PassOverObstacles_AndAnAreaCoversThemHarmlessly()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            grid.SetBlocked(new HexCoordinate(0, 0), true);
            BattleUnit caster = Place(grid, Unit("p1", BattleTeam.Player, 0, new HexCoordinate(0, 1)));
            BattleUnit beyond = Place(grid, Unit("e1", BattleTeam.Enemy, 0, new HexCoordinate(0, -1)));
            List<BattleUnit> all = new List<BattleUnit> { caster, beyond };

            IReadOnlyList<BattleUnit> single = SkillTargetResolver.ResolveTargets(Skill(2), caster, all, grid, new System.Random(1));
            CollectionAssert.AreEqual(new[] { beyond }, single, "no line of sight: a range-2 shot reaches over the rock");

            IReadOnlyList<BattleUnit> area = SkillTargetResolver.ResolveTargets(Skill(2, SkillTargetShape.AreaBurst), caster, all, grid, new System.Random(1));
            CollectionAssert.AreEqual(new[] { beyond }, area, "the burst covers the rock's hex and hits the foe past it");

            IReadOnlyList<BattleUnit> line = SkillTargetResolver.ResolveTargets(Skill(3, SkillTargetShape.Line), caster, all, grid, new System.Random(1));
            CollectionAssert.Contains(line, beyond, "a line runs through the rock's hex");
            Assert.IsTrue(grid.IsBlocked(new HexCoordinate(0, 0)), "and the rock stays");
        }

        [Test]
        public void EveryTemplateAndBoss_SeatsOnEveryShippedLayout_AndEveryShapeStillPacks()
        {
            List<string> errors = ObstacleLayoutValidator.Validate(Content.Layouts, Content.Regions, EncounterLibraryDataOf(), Content.EnemyLibrary);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            Assert.AreEqual(18, Content.Layouts.Layouts.Length, "the six Hollow paintings on the three arenas");
            foreach (BattleLayoutEntryData layout in Content.Layouts.Layouts)
            {
                ObstacleLayoutValidator.TryParseArena(layout.Arena, out ArenaSize arena);
                HexGrid grid = new HexGrid(arena);
                BattleLayouts.Apply(grid, layout);
                foreach (EncounterTemplateData template in EncounterLibraryDataOf().Templates)
                {
                    if (EncounterLibrary.ParseArena(template.Arena) != arena)
                    {
                        continue;
                    }

                    EncounterPlan plan = EncounterPlan.FromTemplate(Content.Encounters, Content.Enemies, template.EncounterId, 50);
                    List<UnitFootprint> footprints = new List<UnitFootprint>();
                    foreach (EncounterLineupEnemy enemy in plan.Enemies)
                    {
                        footprints.Add(Content.Enemies.Species(enemy.EnemyId, enemy.Element).Footprint);
                    }

                    Assert.IsTrue(EncounterFit.Fits(grid, footprints), template.EncounterId + " on " + layout.ArtKey);
                }

                // Obstacles never touch a deployment zone, so every lineup packs exactly as on the open board.
                foreach (BattleTeam team in new[] { BattleTeam.Enemy, BattleTeam.Player })
                {
                    CollectionAssert.AreEqual(DeploymentPacker.FrontOrder(new HexGrid(arena), team), DeploymentPacker.FrontOrder(grid, team), layout.ArtKey);
                }
            }

        }

        private static EncounterLibraryData EncounterLibraryDataOf()
        {
            return FieldJson.FromJson<EncounterLibraryData>(System.IO.File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), EncounterLibraryData.ProjectRelativePath)));
        }

        private static void AssertNobodyOnAnObstacle(BattleSessionRun run, string when)
        {
            foreach (BattleUnit unit in run.Units)
            {
                if (unit.IsDefeated)
                {
                    continue;
                }

                foreach (HexCoordinate tile in Footprints.Tiles(unit.Position, unit.Footprint))
                {
                    Assert.IsFalse(run.Grid.IsBlocked(tile), unit.Id + " stands on an obstacle at " + tile + " " + when);
                }
            }
        }

        private static BattleUnit Place(HexGrid grid, BattleUnit unit)
        {
            Assert.IsTrue(grid.TryPlaceUnit(unit.Id, unit.Position), "test setup: could not place " + unit.Id);
            return unit;
        }

        private static BattleUnit Unit(string id, BattleTeam team, int moveRange, HexCoordinate position, params SkillSO[] skills)
        {
            return new BattleUnit(id, team, new StatBlock(500, 10, 10, 10, 10, 10, moveRange), position, new SkillLoadout(skills));
        }

        private SkillSO Skill(int range, SkillTargetShape shape = SkillTargetShape.SingleTarget)
        {
            SkillSO skill = new SkillSO();
            skill.TargetShape = shape;
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
