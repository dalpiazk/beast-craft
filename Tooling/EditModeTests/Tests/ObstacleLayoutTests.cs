using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Battlefield obstacle layouts (<c>battle-layouts.json</c>): <see cref="ObstacleLayoutValidator"/>'s
    /// rules one by one over hand-built layouts, the seeded per-battle pick
    /// (<see cref="BattleLayouts.Pick"/>), the obstructed <see cref="EncounterFit"/> overload, and the
    /// region reaching the encounter setup.
    /// </summary>
    public class ObstacleLayoutTests
    {
        private static readonly RegionLibraryData Regions = new RegionLibraryData { Regions = new[] { new RegionData { RegionId = "r01" }, new RegionData { RegionId = "r02" } } };

        /// <summary>A cell by its offset column and row (HexGrid's rectangle), as axial data.</summary>
        private static HexCellData Cell(int column, int row)
        {
            HexCoordinate hex = HexGrid.FromOffset(column, row);
            return new HexCellData { Q = hex.Q, R = hex.R };
        }

        private static BattleLayoutEntryData Layout(string arena, params HexCellData[] cells)
        {
            return new BattleLayoutEntryData { RegionId = "r01", ArtKey = "backdrop/r01/test/" + arena.ToLowerInvariant(), Arena = arena, Cells = cells };
        }

        private static List<string> Errors(params BattleLayoutEntryData[] layouts)
        {
            return ObstacleLayoutValidator.Validate(new BattleLayoutData { SchemaVersion = 1, Layouts = layouts }, Regions, null, null);
        }

        private static void AssertRejected(BattleLayoutEntryData layout, string expected)
        {
            List<string> errors = Errors(layout);
            Assert.IsTrue(errors.Exists(e => e.Contains(expected)), "expected '" + expected + "' in:\n" + string.Join("\n", errors));
        }

        /// <summary>Four cells in Medium's neutral band, two a side each way.</summary>
        private static BattleLayoutEntryData GoodMedium()
        {
            return Layout("Medium", Cell(-3, -1), Cell(2, -1), Cell(-3, 1), Cell(2, 1));
        }

        [Test]
        public void Validator_AcceptsABalancedNeutralBandLayout_OnEveryArena()
        {
            Assert.IsEmpty(Errors(GoodMedium()), string.Join("\n", Errors(GoodMedium())));
            Assert.IsEmpty(Errors(Layout("Small")), "a Small board may have none");
            Assert.IsEmpty(Errors(Layout("Small", Cell(-2, 0), Cell(2, 0))));
            Assert.IsEmpty(Errors(Layout("Large", Cell(-4, -2), Cell(3, -2), Cell(-4, 2), Cell(3, 2), Cell(-1, -3), Cell(0, 3))));
        }

        [Test]
        public void Validator_RefusesCellsInADeploymentZone_OffTheBoard_OrTwice()
        {
            AssertRejected(Layout("Medium", Cell(-3, -3), Cell(2, 1)), "deployment zone");
            AssertRejected(Layout("Medium", Cell(-3, 1), Cell(2, 3)), "deployment zone");
            AssertRejected(Layout("Medium", Cell(-3, 0), Cell(9, 0)), "off the Medium board");
            AssertRejected(Layout("Medium", Cell(-3, 0), Cell(-3, 0), Cell(2, 0)), "listed twice");
        }

        [Test]
        public void Validator_HoldsTheDensityCaps()
        {
            Assert.AreEqual(0, ObstacleLayoutValidator.MinCells(ArenaSize.Small));
            Assert.AreEqual(2, ObstacleLayoutValidator.MaxCells(ArenaSize.Small));
            Assert.AreEqual(2, ObstacleLayoutValidator.MinCells(ArenaSize.Medium));
            Assert.AreEqual(5, ObstacleLayoutValidator.MaxCells(ArenaSize.Medium));
            Assert.AreEqual(3, ObstacleLayoutValidator.MinCells(ArenaSize.Large));
            Assert.AreEqual(8, ObstacleLayoutValidator.MaxCells(ArenaSize.Large));

            AssertRejected(Layout("Small", Cell(-2, -1), Cell(2, 1), Cell(0, 0)), "3 obstacles; a Small layout has 0-2");
            AssertRejected(Layout("Medium", Cell(-3, 0)), "1 obstacles; a Medium layout has 2-5");
            AssertRejected(Layout("Large", Cell(-4, 0), Cell(3, 0)), "a Large layout has 3-8");
        }

        [Test]
        public void Validator_HoldsBalancedCountFairness()
        {
            AssertRejected(Layout("Medium", Cell(-3, -1), Cell(2, -1), Cell(0, -2)), "3 obstacles on the enemy half and 0 on the player half");
            AssertRejected(Layout("Medium", Cell(-4, -1), Cell(-3, 1), Cell(-2, 0)), "3 obstacles left of the centre line and 0 right");
            Assert.IsEmpty(Errors(Layout("Medium", Cell(-3, -1), Cell(2, 1), Cell(0, -2))), "a difference of one is fair");
        }

        [Test]
        public void Validator_RefusesAWalledOffPocket()
        {
            // The first tile of Medium's centre row has three neighbours on the board.
            AssertRejected(Layout("Medium", Cell(-4, -1), Cell(-4, 1), Cell(-3, 0), Cell(2, -1), Cell(2, 1)), "wall off 1 of the 83 free tiles");
        }

        [Test]
        public void Validator_RefusesALayoutThatTrapsAGiantOrAChampion()
        {
            // Five stones across Medium's neutral band leave one-hex gaps: a seven-hex giant cannot squeeze through.
            AssertRejected(Layout("Medium", Cell(-2, -2), Cell(1, -1), Cell(-2, 1), Cell(2, 2), Cell(0, 0)), "a Hex7 enemy deployed in its zone can no longer walk");
            // Two stumps either side of Small's centre row wall the three-hex champion in.
            AssertRejected(Layout("Small", Cell(-1, 0), Cell(1, 0)), "a Triangle enemy deployed in its zone can no longer walk");

            HexGrid open = new HexGrid(ArenaSize.Medium);
            Assert.IsTrue(ObstacleLayoutValidator.LargeUnitCrosses(open, UnitFootprint.Hex7));
            Assert.IsTrue(ObstacleLayoutValidator.LargeUnitCrosses(new HexGrid(ArenaSize.Small), UnitFootprint.Hex7), "a giant never deploys on Small: nothing to trap");
        }

        [Test]
        public void Validator_ChecksTheEntryFields()
        {
            BattleLayoutEntryData unknownRegion = GoodMedium();
            unknownRegion.RegionId = "r99";
            AssertRejected(unknownRegion, "RegionId 'r99' is not a region");

            BattleLayoutEntryData badArena = GoodMedium();
            badArena.Arena = "Huge";
            AssertRejected(badArena, "Arena 'Huge'");

            BattleLayoutEntryData badKey = GoodMedium();
            badKey.ArtKey = "Backdrop/R01";
            AssertRejected(badKey, "ArtKey is not lowercase");

            List<string> twice = Errors(GoodMedium(), GoodMedium());
            Assert.IsTrue(twice.Exists(e => e.Contains("listed twice (a painted backdrop has one layout)")), string.Join("\n", twice));
            Assert.IsNotEmpty(ObstacleLayoutValidator.Validate(new BattleLayoutData { SchemaVersion = 2 }, Regions, null, null));
            Assert.IsNotEmpty(ObstacleLayoutValidator.Validate(null, Regions, null, null));
        }

        [Test]
        public void Validator_RefusesALayoutATemplateNoLongerFits()
        {
            EncounterLibraryData library = new EncounterLibraryData
            {
                Templates = new[] { new EncounterTemplateData { EncounterId = "ten", Arena = "Small", Groups = new[] { new EncounterGroupData { EnemyId = "brute", Count = 10 } } } }
            };
            EnemyLibraryData enemies = new EnemyLibraryData { Enemies = new[] { new EnemyData { EnemyId = "brute" } } };

            BattleLayoutData open = new BattleLayoutData { SchemaVersion = 1, Layouts = new[] { Layout("Small", Cell(-2, 0), Cell(2, 0)) } };
            Assert.IsEmpty(ObstacleLayoutValidator.Validate(open, Regions, library, enemies), "a neutral-band layout never costs a deployment tile");

            // A rock in the enemy zone: ten brutes need all ten zone tiles.
            BattleLayoutData blocked = new BattleLayoutData { SchemaVersion = 1, Layouts = new[] { Layout("Small", Cell(0, -3), Cell(0, 0)) } };
            List<string> errors = ObstacleLayoutValidator.Validate(blocked, Regions, library, enemies);
            Assert.IsTrue(errors.Exists(e => e.Contains("template 'ten' (10 enemies) no longer fits")), string.Join("\n", errors));
        }

        [Test]
        public void Side_SplitsARowAtTheBoardsVerticalCentreLine()
        {
            HexGrid medium = new HexGrid(ArenaSize.Medium);
            for (int column = medium.MinColumn; column <= medium.MaxColumn; column++)
            {
                int expected = column < 0 ? -1 : 1;
                Assert.AreEqual(expected, ObstacleLayoutValidator.Side(medium, HexGrid.FromOffset(column, 0)), "even row, column " + column);
                Assert.AreEqual(expected, ObstacleLayoutValidator.Side(medium, HexGrid.FromOffset(column, 1)), "odd row, column " + column);
            }
        }

        [Test]
        public void Pick_IsSeeded_OnItsOwnStream_AndCoversEveryCandidate()
        {
            BattleLayoutData data = new BattleLayoutData
            {
                SchemaVersion = 1,
                Layouts = new[]
                {
                    Layout("Medium", Cell(-3, 0), Cell(2, 0)), new BattleLayoutEntryData { RegionId = "r01", ArtKey = "b/two", Arena = "Medium", Cells = new[] { Cell(0, 1), Cell(0, -1) } },
                    Layout("Large", Cell(-4, 0), Cell(3, 0), Cell(0, 1))
                }
            };

            Assert.AreEqual(2, BattleLayouts.Candidates(data, "r01", ArenaSize.Medium).Count);
            Assert.IsNull(BattleLayouts.Pick(data, "r02", ArenaSize.Medium, 7), "a region without layouts: open board");
            Assert.IsNull(BattleLayouts.Pick(data, null, ArenaSize.Medium, 7));
            Assert.IsNull(BattleLayouts.Pick(data, "r01", ArenaSize.Small, 7), "an arena without layouts: open board");
            Assert.IsNull(BattleLayouts.Pick(null, "r01", ArenaSize.Medium, 7));

            HashSet<string> picked = new HashSet<string>();
            for (int seed = 0; seed < 64; seed++)
            {
                BattleLayoutEntryData a = BattleLayouts.Pick(data, "r01", ArenaSize.Medium, seed);
                Assert.AreSame(a, BattleLayouts.Pick(data, "r01", ArenaSize.Medium, seed), "the same seed, the same layout");
                int expected = Progression.LootRoller.DeriveSeed(seed, BattleLayouts.ObstacleStream) % 2;
                Assert.AreSame(data.Layouts[expected], a);
                picked.Add(a.ArtKey);
            }

            Assert.AreEqual(2, picked.Count, "every candidate comes up");

            HexGrid grid = new HexGrid(ArenaSize.Large);
            Assert.AreEqual(3, BattleLayouts.Apply(grid, data.Layouts[2]));
            Assert.IsTrue(grid.IsBlocked(HexGrid.FromOffset(-4, 0)));
            Assert.IsFalse(grid.IsBlocked(HexGrid.FromOffset(-3, 0)));
            Assert.AreEqual(0, BattleLayouts.Apply(grid, null));
        }

        [Test]
        public void EncounterFit_OnAnOpenGrid_AgreesWithTheArenaOverload()
        {
            List<UnitFootprint[]> lineups = new List<UnitFootprint[]>
            {
                new[] { UnitFootprint.Hex7, UnitFootprint.Single, UnitFootprint.Single },
                new[] { UnitFootprint.Triangle, UnitFootprint.Triangle, UnitFootprint.Single },
                new[] { UnitFootprint.Hex7, UnitFootprint.Hex7 },
                new UnitFootprint[24]
            };
            foreach (ArenaSize arena in new[] { ArenaSize.Small, ArenaSize.Medium, ArenaSize.Large })
            {
                foreach (UnitFootprint[] lineup in lineups)
                {
                    Assert.AreEqual(EncounterFit.Fits(arena, lineup), EncounterFit.Fits(new HexGrid(arena), lineup), arena + " " + lineup.Length);
                }
            }

            HexGrid obstructed = new HexGrid(ArenaSize.Small);
            obstructed.SetBlocked(HexGrid.FromOffset(0, -2), true);
            Assert.IsTrue(EncounterFit.Fits(ArenaSize.Small, new UnitFootprint[10]));
            Assert.IsFalse(EncounterFit.Fits(obstructed, new UnitFootprint[10]), "a blocked zone tile costs a seat");
        }

        [Test]
        public void RegionId_ReachesTheEncounterSetup()
        {
            GameContent content = VfxLibraryTests.Content;
            EncounterPlan plan = EncounterPlan.Generate(content.Encounters, content.Enemies, "squad", 20, 5);
            Assert.IsNull(plan.ToSetup().RegionId);
            Assert.AreEqual("r01", plan.ToSetup("r01").RegionId);

            BattleSetup demo = DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out string error, regionId: "r01");
            Assert.IsNull(error);
            Assert.AreEqual("r01", demo.Encounter.RegionId);
            Assert.IsNull(DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out _).Encounter.RegionId, "no region: the open board");
            BattleSetup lineup = DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out _, lineup: new[] { "brute:Fire" }, regionId: "r02");
            Assert.AreEqual("r02", lineup.Encounter.RegionId);
        }
    }
}
