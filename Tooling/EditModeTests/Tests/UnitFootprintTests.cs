using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Creatures;
using NUnit.Framework;
using Random = System.Random;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Multi-hex units (<see cref="UnitFootprint"/>): the grid's all-or-nothing footprint
    /// placement, <see cref="FootprintMath"/> distances, targeting against and from large units,
    /// movement of and toward large units, knockback, the deployment packer, the footprint-aware
    /// pathfinder, and the roster rule that beasts are always one tile. The one-tile behaviour is
    /// pinned by every other battle fixture, which run unchanged.
    /// <para>
    /// Conventions: a giant is <see cref="UnitFootprint.Hex7"/> anchored on its centre, a champion
    /// <see cref="UnitFootprint.Triangle"/> (anchor, <c>(1, 0)</c>, <c>(1, -1)</c>). Units have 10
    /// in every combat stat at level 1 and HP 500, so nothing here dies unless a test says so.
    /// </para>
    /// </summary>
    public class UnitFootprintTests
    {
        private const int Sturdy = 500;

        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Footprint tables.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Footprints_AnchorFirst_ThenAxialOrder()
        {
            CollectionAssert.AreEqual(new[] { HexCoordinate.Zero }, Footprints.Offsets(UnitFootprint.Single));
            CollectionAssert.AreEqual(new[] { HexCoordinate.Zero, new HexCoordinate(1, 0), new HexCoordinate(1, -1) }, Footprints.Offsets(UnitFootprint.Triangle));

            List<HexCoordinate> hex7 = new List<HexCoordinate> { HexCoordinate.Zero };
            hex7.AddRange(HexCoordinate.AxialDirections);
            CollectionAssert.AreEqual(hex7, Footprints.Offsets(UnitFootprint.Hex7));
            Assert.AreEqual(1, Footprints.TileCount(UnitFootprint.Single));
            Assert.AreEqual(3, Footprints.TileCount(UnitFootprint.Triangle));
            Assert.AreEqual(7, Footprints.TileCount(UnitFootprint.Hex7));
        }

        // ---------------------------------------------------------------------------------------
        // Grid.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Grid_PlacesAHex7_OnAllSevenTiles_AndReportsItOnEveryOne()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);

            Assert.IsTrue(grid.TryPlaceUnit("g", new HexCoordinate(1, -1), UnitFootprint.Hex7));

            foreach (HexCoordinate tile in Footprints.Tiles(new HexCoordinate(1, -1), UnitFootprint.Hex7))
            {
                Assert.AreEqual("g", grid.GetOccupant(tile), tile.ToString());
                Assert.IsTrue(grid.IsPassable(tile, "g"), "a unit never obstructs itself");
                Assert.IsFalse(grid.IsPassable(tile, "other"));
            }

            Assert.AreEqual(7, CountTiles(grid, "g"));
            Assert.IsTrue(grid.TryGetPosition("g", out HexCoordinate anchor));
            Assert.AreEqual(new HexCoordinate(1, -1), anchor, "the grid records the anchor");
            Assert.AreEqual(UnitFootprint.Hex7, grid.GetFootprint("g"));
            Assert.AreEqual(UnitFootprint.Single, grid.GetFootprint("nobody"));
        }

        [Test]
        public void Grid_FootprintPlacement_IsAllOrNothing()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            grid.SetBlocked(new HexCoordinate(1, 0), true);
            Assert.IsTrue(grid.TryPlaceUnit("b", new HexCoordinate(-1, 1)));

            Assert.IsFalse(grid.TryPlaceUnit("g", HexCoordinate.Zero, UnitFootprint.Hex7), "one blocked tile refuses the whole footprint");
            grid.SetBlocked(new HexCoordinate(1, 0), false);
            Assert.IsFalse(grid.TryPlaceUnit("g", HexCoordinate.Zero, UnitFootprint.Hex7), "one occupied tile refuses the whole footprint");
            Assert.IsFalse(grid.TryPlaceUnit("g", new HexCoordinate(5, 0), UnitFootprint.Hex7), "a footprint hanging off the board is refused");
            Assert.AreEqual(0, CountTiles(grid, "g"), "a refused placement changes nothing");
            Assert.IsFalse(grid.TryGetPosition("g", out _));

            Assert.IsTrue(grid.RemoveUnit("b"));
            Assert.IsTrue(grid.TryPlaceUnit("g", HexCoordinate.Zero, UnitFootprint.Hex7));
            Assert.AreEqual(7, CountTiles(grid, "g"));
        }

        [Test]
        public void Grid_MovingALargeUnit_TranslatesItsFootprint_WithoutLeftovers_AndTheTwoArgumentMoveKeepsIt()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            Assert.IsTrue(grid.TryPlaceUnit("g", HexCoordinate.Zero, UnitFootprint.Hex7));

            // One step east overlaps six of its own tiles: legal, as a unit never obstructs itself.
            Assert.IsTrue(grid.TryPlaceUnit("g", new HexCoordinate(1, 0)));
            Assert.AreEqual(UnitFootprint.Hex7, grid.GetFootprint("g"));
            Assert.AreEqual(7, CountTiles(grid, "g"));
            Assert.IsNull(grid.GetOccupant(new HexCoordinate(-1, 0)), "the tile it left behind is free");
            Assert.IsNull(grid.GetOccupant(new HexCoordinate(-1, 1)));
            Assert.AreEqual("g", grid.GetOccupant(new HexCoordinate(2, -1)));

            Assert.IsTrue(grid.TryPlaceUnit("c", new HexCoordinate(-3, 2), UnitFootprint.Triangle));
            Assert.IsTrue(grid.TryPlaceUnit("c", new HexCoordinate(-3, 1)));
            Assert.AreEqual(3, CountTiles(grid, "c"));
            Assert.AreEqual("c", grid.GetOccupant(new HexCoordinate(-2, 0)));
            Assert.IsNull(grid.GetOccupant(new HexCoordinate(-3, 2)));
        }

        [Test]
        public void Grid_RemoveUnit_AndLiftingTheDefeated_FreeEveryTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            Assert.IsTrue(grid.TryPlaceUnit("g", HexCoordinate.Zero, UnitFootprint.Hex7));
            Assert.IsTrue(grid.RemoveUnit("g"));
            Assert.AreEqual(0, CountTiles(grid, "g"));
            Assert.AreEqual(UnitFootprint.Single, grid.GetFootprint("g"));

            // A defeated giant is lifted as the next turn opens, all seven tiles at once.
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, 0, HexCoordinate.Zero));
            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 3)));
            giant.CurrentHp = 0;
            giant.IsDefeated = true;
            BattleTurnExecutor.ExecuteTurn(beast, new List<BattleUnit> { giant, beast }, grid, new Random(1), null);

            Assert.AreEqual(0, CountTiles(grid, "g"));
            Assert.IsTrue(grid.TryPlaceUnit("x", new HexCoordinate(0, 1)), "its old tiles are free for anyone");
        }

        [Test]
        public void Grid_SingleFootprintOverload_IsTheTwoArgumentPlacement()
        {
            // The one-tile rule never looked at terrain; the Single overload keeps that exactly.
            HexGrid grid = new HexGrid(ArenaSize.Small);
            grid.SetBlocked(new HexCoordinate(1, 0), true);
            Assert.IsTrue(grid.TryPlaceUnit("a", new HexCoordinate(1, 0), UnitFootprint.Single));
            Assert.IsFalse(grid.TryPlaceUnit("b", new HexCoordinate(1, 0), UnitFootprint.Single), "occupied");
            Assert.IsFalse(grid.TryPlaceUnit("b", new HexCoordinate(9, 0), UnitFootprint.Single), "off the board");
            Assert.IsFalse(grid.TryPlaceUnit(null, HexCoordinate.Zero, UnitFootprint.Single));
            Assert.IsTrue(grid.TryPlaceUnit("a", HexCoordinate.Zero, UnitFootprint.Single), "a one-tile move");
            Assert.IsNull(grid.GetOccupant(new HexCoordinate(1, 0)));
        }

        [Test]
        public void Grid_CanStand_AndFitsDeploymentZone_CheckTheWholeFootprint()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            Assert.IsTrue(grid.TryPlaceUnit("b", new HexCoordinate(1, 0)));

            Assert.IsFalse(grid.CanStand(HexCoordinate.Zero, UnitFootprint.Hex7, "g"));
            Assert.IsTrue(grid.CanStand(HexCoordinate.Zero, UnitFootprint.Hex7, "b"), "a unit's own tile never obstructs it");
            Assert.IsTrue(grid.CanStand(new HexCoordinate(-2, 0), UnitFootprint.Hex7, "g"));
            Assert.AreEqual(grid.IsPassable(new HexCoordinate(1, 0), "g"), grid.CanStand(new HexCoordinate(1, 0), UnitFootprint.Single, "g"));

            // Medium enemy zone: rows -3..-5. A Hex7 needs three rows, so only the middle one.
            Assert.IsFalse(grid.FitsDeploymentZone(new HexCoordinate(2, -3), UnitFootprint.Hex7, BattleTeam.Enemy));
            Assert.IsTrue(grid.FitsDeploymentZone(new HexCoordinate(2, -4), UnitFootprint.Hex7, BattleTeam.Enemy));
            Assert.IsTrue(grid.FitsDeploymentZone(new HexCoordinate(1, -3), UnitFootprint.Triangle, BattleTeam.Enemy));
            Assert.IsFalse(grid.FitsDeploymentZone(new HexCoordinate(0, -5), UnitFootprint.Triangle, BattleTeam.Enemy), "(1, -6) is off the board");
        }

        [Test]
        public void Grid_EveryUnitCoversExactlyItsFootprint_ThroughRandomMoves()
        {
            Random rng = new Random(7);
            UnitFootprint[] sizes = { UnitFootprint.Single, UnitFootprint.Triangle, UnitFootprint.Hex7 };

            for (int round = 0; round < 200; round++)
            {
                HexGrid grid = new HexGrid(ArenaSize.Large);
                string[] ids = { "a", "b", "c", "d", "e", "f" };
                UnitFootprint[] footprints = new UnitFootprint[ids.Length];
                for (int i = 0; i < ids.Length; i++)
                {
                    footprints[i] = sizes[rng.Next(sizes.Length)];
                }

                for (int op = 0; op < 60; op++)
                {
                    int u = rng.Next(ids.Length);
                    HexCoordinate tile = new HexCoordinate(rng.Next(-8, 9), rng.Next(-8, 9));
                    if (rng.Next(6) == 0)
                    {
                        grid.RemoveUnit(ids[u]);
                    }
                    else if (rng.Next(2) == 0)
                    {
                        grid.TryPlaceUnit(ids[u], tile, footprints[u]);
                    }
                    else if (grid.TryGetPosition(ids[u], out _))
                    {
                        grid.TryPlaceUnit(ids[u], tile);
                    }

                    for (int i = 0; i < ids.Length; i++)
                    {
                        bool placed = grid.TryGetPosition(ids[i], out HexCoordinate anchor);
                        Assert.AreEqual(placed ? Footprints.TileCount(footprints[i]) : 0, CountTiles(grid, ids[i]), "round " + round + " op " + op);
                        if (placed)
                        {
                            foreach (HexCoordinate covered in Footprints.Tiles(anchor, footprints[i]))
                            {
                                Assert.AreEqual(ids[i], grid.GetOccupant(covered));
                            }
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // FootprintMath.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void FootprintMath_Single_IsHexDistance_EverywhereOnALargeBoard()
        {
            List<HexCoordinate> tiles = new List<HexCoordinate>(new HexGrid(ArenaSize.Large).Tiles);
            foreach (HexCoordinate a in tiles)
            {
                foreach (HexCoordinate b in tiles)
                {
                    int expected = a.Distance(b);
                    Assert.AreEqual(expected, FootprintMath.DistanceTo(a, b, UnitFootprint.Single));
                    Assert.AreEqual(expected, FootprintMath.UnitDistance(a, UnitFootprint.Single, b, UnitFootprint.Single));
                }
            }
        }

        [Test]
        public void FootprintMath_ClosedForms_MatchTheBruteForceMinimum()
        {
            List<HexCoordinate> tiles = new List<HexCoordinate>(new HexGrid(ArenaSize.Large).Tiles);
            UnitFootprint[] sizes = { UnitFootprint.Single, UnitFootprint.Triangle, UnitFootprint.Hex7 };
            HexCoordinate[] anchors = { HexCoordinate.Zero, new HexCoordinate(3, -2), new HexCoordinate(-5, 1) };

            foreach (UnitFootprint size in sizes)
            {
                foreach (HexCoordinate anchor in anchors)
                {
                    foreach (HexCoordinate tile in tiles)
                    {
                        Assert.AreEqual(BruteDistance(new List<HexCoordinate> { tile }, Footprints.Tiles(anchor, size)), FootprintMath.DistanceTo(tile, anchor, size),
                                        size + " " + anchor + " " + tile);
                    }

                    foreach (UnitFootprint other in sizes)
                    {
                        foreach (HexCoordinate tile in tiles)
                        {
                            int expected = BruteDistance(Footprints.Tiles(tile, other), Footprints.Tiles(anchor, size));
                            Assert.AreEqual(expected, FootprintMath.UnitDistance(tile, other, anchor, size));
                            Assert.AreEqual(expected, FootprintMath.UnitDistance(anchor, size, tile, other), "symmetric");
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Targeting.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SingleTarget_ReachesAGiant_ToItsNearestTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, 0, HexCoordinate.Zero));
            BattleUnit near = Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 2));
            BattleUnit far = Unit("c", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 3));
            List<BattleUnit> all = new List<BattleUnit> { giant, near, far };

            SkillSO melee = Skill(1);
            CollectionAssert.AreEqual(new[] { giant }, SkillTargetResolver.ResolveTargets(melee, near, all, grid, null), "centre distance 2 = Range + 1");
            CollectionAssert.IsEmpty(SkillTargetResolver.ResolveTargets(melee, far, all, grid, null));
            CollectionAssert.AreEqual(new[] { giant }, SkillTargetResolver.ResolveTargets(Skill(2), far, all, grid, null));

            // And the giant's own range-1 skill reaches the one next to its ring, not the one beyond.
            CollectionAssert.AreEqual(new[] { near }, SkillTargetResolver.ResolveTargets(melee, giant, all, grid, null));
        }

        [Test]
        public void AreaBurst_OverSeveralGiantTiles_HitsItOnce()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit caster = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(1, 1)));
            SkillSO burst = Area(SkillTargetShape.AreaBurst, 1);

            // The radius-1 disc around (1, 1) covers two giant tiles, (1, 0) and (0, 1): one hit.
            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(burst, caster, new List<BattleUnit> { giant, caster }, grid, null);
            CollectionAssert.AreEqual(new[] { giant }, targets);

            SkillSO wide = Area(SkillTargetShape.AreaBurst, 3);
            CollectionAssert.AreEqual(new[] { giant }, SkillTargetResolver.ResolveTargets(wide, caster, new List<BattleUnit> { giant, caster }, grid, null),
                                      "all seven tiles covered: still one hit");
        }

        [Test]
        public void LineAndCross_HitALargeUnit_OnAnyOfItsTiles()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit champion = Place(grid, Large("c", BattleTeam.Enemy, UnitFootprint.Triangle, new HexCoordinate(1, -3)));
            BattleUnit caster = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(2, 0)));
            List<BattleUnit> all = new List<BattleUnit> { champion, caster };

            // (2, 0) straight up the (0, -1) axis passes (2, -3), a champion tile but not its anchor.
            CollectionAssert.AreEqual(new[] { champion }, SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.Line, 3), caster, all, grid, null));
            CollectionAssert.AreEqual(new[] { champion }, SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.Cross, 3), caster, all, grid, null));
            CollectionAssert.IsEmpty(SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.Cross, 2), caster, all, grid, null));
        }

        [Test]
        public void LargeCaster_AreaBurst_IsTheDiscOfRangePlusOne_AndItsLineAndCrossRadiateFromItsTiles()
        {
            HexGrid grid = new HexGrid(ArenaSize.Large);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            List<BattleUnit> all = new List<BattleUnit> { giant };
            foreach (HexCoordinate tile in grid.GetTilesInRange(HexCoordinate.Zero, 4))
            {
                if (tile.Distance(HexCoordinate.Zero) >= 2)
                {
                    all.Add(Place(grid, Unit("p" + (tile.Q + 10) + "_" + (tile.R + 10), BattleTeam.Player, CombatStance.Vanguard, 0, tile)));
                }
            }

            foreach (BattleUnit hit in SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.AreaBurst, 1), giant, all, grid, null))
            {
                Assert.AreEqual(2, hit.Position.Distance(HexCoordinate.Zero), hit.Id);
            }

            Assert.AreEqual(12, SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.AreaBurst, 1), giant, all, grid, null).Count, "the radius-2 ring");
            Assert.AreEqual(12 + 18, SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.AreaBurst, 2), giant, all, grid, null).Count, "the radius-3 disc");

            // Cross range 1 from all seven tiles: exactly the ring at 2 (every ring tile is next to
            // some footprint tile along an axis), and never the giant's own tiles.
            Assert.AreEqual(12, SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.Cross, 1), giant, all, grid, null).Count);

            // A line fires from the giant's tile nearest the focus, so range 1 reaches the ring.
            IReadOnlyList<BattleUnit> line = SkillTargetResolver.ResolveTargets(Area(SkillTargetShape.Line, 1), giant, all, grid, null);
            Assert.AreEqual(1, line.Count);
            Assert.AreEqual(2, line[0].Position.Distance(HexCoordinate.Zero));
        }

        [Test]
        public void LargeCaster_AllySideCrossAndLine_NeverHitTheCasterThroughItsOwnTiles()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit friend = Place(grid, Unit("f", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(2, 0)));
            List<BattleUnit> all = new List<BattleUnit> { giant, friend };

            SkillSO cross = Area(SkillTargetShape.Cross, 2);
            cross.TargetSide = SkillTargetSide.Ally;
            CollectionAssert.AreEqual(new[] { friend }, SkillTargetResolver.ResolveTargets(cross, giant, all, grid, null));

            SkillSO burst = Area(SkillTargetShape.AreaBurst, 1);
            burst.TargetSide = SkillTargetSide.Ally;
            CollectionAssert.AreEquivalent(new[] { giant, friend }, SkillTargetResolver.ResolveTargets(burst, giant, all, grid, null),
                                           "a burst includes its caster, as a one-tile burst does");
        }

        // ---------------------------------------------------------------------------------------
        // Movement.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Beast_ClosingOnAGiant_StopsOnItsRing_AndFires()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 4, new HexCoordinate(0, 5), Skill(1)));

            BattleTurnResult result = Turn(beast, new List<BattleUnit> { giant, beast }, grid);

            Assert.AreEqual(BattleSkillStatus.Fired, result.SkillOutcomes[0].Status);
            Assert.AreEqual(3, result.MovementSpent, "centre distance 5 to 2: three steps, not four");
            Assert.AreEqual(1, FootprintMath.UnitDistance(beast, giant));
            Assert.Less(giant.CurrentHp, Sturdy);
        }

        [Test]
        public void Beast_WalksAroundToAnotherRingTile_WhenTheNearSideIsWalled()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));

            // Wall off every ring tile but (2, -2), on the far north-east side.
            foreach (HexCoordinate tile in grid.GetTilesInRange(HexCoordinate.Zero, 2))
            {
                if (tile.Distance(HexCoordinate.Zero) == 2 && tile != new HexCoordinate(2, -2))
                {
                    grid.SetBlocked(tile, true);
                }
            }

            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 10, new HexCoordinate(0, 4), Skill(1)));

            BattleTurnResult result = Turn(beast, new List<BattleUnit> { giant, beast }, grid);

            Assert.AreEqual(BattleSkillStatus.Fired, result.SkillOutcomes[0].Status);
            Assert.AreEqual(new HexCoordinate(2, -2), beast.Position);
        }

        [Test]
        public void Giant_ApproachesWithItsWholeFootprint_PartiallyWhenShortOfMove_ThenFires()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, 3, new HexCoordinate(0, -3), Skill(1)));
            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 4)));
            List<BattleUnit> all = new List<BattleUnit> { giant, beast };

            // Footprint to beast: centre distance 7, so 6; range 1 needs 5 anchor steps. Move 3.
            BattleTurnResult first = Turn(giant, all, grid);
            Assert.AreEqual(BattleSkillStatus.OutOfMovement, first.SkillOutcomes[0].Status);
            Assert.AreEqual(3, first.MovementSpent);
            Assert.AreEqual(3, FootprintMath.UnitDistance(giant, beast));
            AssertCovers(grid, giant);

            BattleTurnResult second = Turn(giant, all, grid);
            Assert.AreEqual(BattleSkillStatus.Fired, second.SkillOutcomes[0].Status);
            Assert.AreEqual(2, second.MovementSpent);
            Assert.AreEqual(1, FootprintMath.UnitDistance(giant, beast));
            AssertCovers(grid, giant);
            Assert.Less(beast.CurrentHp, Sturdy);
        }

        [Test]
        public void Giant_RoutesOnlyWhereItsFootprintFits()
        {
            // A three-row channel (R = -1, 0, 1): a Hex7 can travel only along its middle row, and
            // a one-tile pillar at (2, 1) forces it to wait rather than squeeze past.
            HexGrid grid = new HexGrid(ArenaSize.Large);
            foreach (HexCoordinate tile in new List<HexCoordinate>(grid.Tiles))
            {
                if (Math.Abs(tile.R) > 1)
                {
                    grid.SetBlocked(tile, true);
                }
            }

            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, 4, new HexCoordinate(-4, 0), Skill(1)));
            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(5, 1)));

            BattleTurnResult turn = Turn(giant, new List<BattleUnit> { giant, beast }, grid);
            Assert.AreEqual(4, turn.MovementSpent);
            Assert.AreEqual(new HexCoordinate(0, 0), giant.Position, "straight along the middle row");
            AssertCovers(grid, giant);

            IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, giant.Position, new HexCoordinate(3, 0), "g", UnitFootprint.Hex7);
            Assert.AreEqual(4, path.Count);
            foreach (HexCoordinate anchor in path)
            {
                Assert.IsTrue(grid.CanStand(anchor, UnitFootprint.Hex7, "g"), anchor.ToString());
            }

            grid.SetBlocked(new HexCoordinate(2, 1), true);
            CollectionAssert.IsEmpty(HexPathfinder.FindPath(grid, giant.Position, new HexCoordinate(3, 0), "g", UnitFootprint.Hex7),
                                     "a one-tile pillar in the channel stops a seven-tile unit");
            Assert.IsNotEmpty(HexPathfinder.FindPath(grid, new HexCoordinate(4, 1), new HexCoordinate(1, 1), "x"), "but not a one-tile one");
        }

        [Test]
        public void Ranged_StandsOffFromAGiant_AtRangeFromItsNearestTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit archer = Place(grid, Unit("a", BattleTeam.Player, CombatStance.Ranged, 3, new HexCoordinate(0, 5), Skill(3)));

            BattleTurnResult result = Turn(archer, new List<BattleUnit> { giant, archer }, grid);

            Assert.AreEqual(BattleSkillStatus.Fired, result.SkillOutcomes[0].Status);
            Assert.AreEqual(3, FootprintMath.UnitDistance(archer, giant), "at the edge of its reach, measured to the footprint");
            Assert.AreEqual(4, archer.Position.Distance(HexCoordinate.Zero));
        }

        [Test]
        public void Skirmisher_CountsAGiantOnce_WhenWeighingCrowdedStopTiles()
        {
            // Two in-range ring tiles at the same cost from (4, -1): (2, 0), the plain rule's stop,
            // touches one giant tile and the bystander at (1, 1); (2, -1) touches two giant tiles.
            // Counting units, (2, -1) has one enemy next to it and (2, 0) two, so it steps to
            // (2, -1). (Counting tiles would tie them at two and keep the plain stop.) The
            // bystander is as far as the giant and sorts after it, so the giant is the focus.
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit bystander = Place(grid, Unit("x", BattleTeam.Enemy, CombatStance.Vanguard, 0, new HexCoordinate(1, 1)));
            BattleUnit skirmisher = Place(grid, Unit("s", BattleTeam.Player, CombatStance.Skirmisher, 2, new HexCoordinate(4, -1), Skill(1)));

            BattleTurnResult result = Turn(skirmisher, new List<BattleUnit> { giant, bystander, skirmisher }, grid);

            Assert.AreEqual(BattleSkillStatus.Fired, result.SkillOutcomes[0].Status);
            Assert.AreEqual(new HexCoordinate(2, -1), skirmisher.Position);
        }

        // ---------------------------------------------------------------------------------------
        // Knockback.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Knockback_AGiantIsImmovable()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit caster = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 2)));

            Knock(caster, giant, 3, grid);

            Assert.AreEqual(HexCoordinate.Zero, giant.Position);
            AssertCovers(grid, giant);
        }

        [Test]
        public void Knockback_AChampionMovesOneTileAtMost_AndOnlyWhereItFits()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit champion = Place(grid, Large("c", BattleTeam.Enemy, UnitFootprint.Triangle, HexCoordinate.Zero));
            BattleUnit caster = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(0, 1)));

            // From (0, 1) to its nearest tile (0, 0): straight up the (0, -1) axis, one tile only.
            Knock(caster, champion, 3, grid);
            Assert.AreEqual(new HexCoordinate(0, -1), champion.Position);
            AssertCovers(grid, champion);

            grid.SetBlocked(new HexCoordinate(1, -3), true);
            Knock(caster, champion, 3, grid);
            Assert.AreEqual(new HexCoordinate(0, -1), champion.Position, "(0, -2) would put a tile on the blocked (1, -3)");
            AssertCovers(grid, champion);
        }

        [Test]
        public void Knockback_ByAGiant_PushesAOneTileTargetOffItsNearestFace()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            BattleUnit giant = Place(grid, Large("g", BattleTeam.Enemy, UnitFootprint.Hex7, HexCoordinate.Zero));
            BattleUnit beast = Place(grid, Unit("b", BattleTeam.Player, CombatStance.Vanguard, 0, new HexCoordinate(2, -1)));

            // (2, -1) touches giant tiles (1, 0) and (1, -1); the first in footprint order, (1, 0),
            // pushes along (1, -1). From the centre the push would tie between (1, 0) and (1, -1)
            // and go along (1, 0), to (4, -1): the nearest-tile rule decides.
            Knock(giant, beast, 2, grid);
            Assert.AreEqual(new HexCoordinate(4, -3), beast.Position);
        }

        // ---------------------------------------------------------------------------------------
        // Deployment packing.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Packer_OneTileUnits_TakeTheFrontOrder()
        {
            HexGrid grid = new HexGrid(ArenaSize.Large);
            List<UnitFootprint> singles = new List<UnitFootprint>();
            for (int i = 0; i < 24; i++)
            {
                singles.Add(UnitFootprint.Single);
            }

            List<HexCoordinate> anchors = new List<HexCoordinate>();
            List<HexCoordinate> covered = new List<HexCoordinate>();
            Assert.IsTrue(DeploymentPacker.TryPack(grid, BattleTeam.Enemy, singles, anchors, covered));
            CollectionAssert.AreEqual(DeploymentPacker.FrontOrder(grid, BattleTeam.Enemy).GetRange(0, 24), anchors);
            CollectionAssert.AreEqual(anchors, covered);
        }

        [Test]
        public void Packer_SeatsTheBossCentred_ThenTheEscortAroundIt()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            List<HexCoordinate> anchors = new List<HexCoordinate>();
            List<HexCoordinate> covered = new List<HexCoordinate>();

            Assert.IsTrue(DeploymentPacker.TryPack(grid, BattleTeam.Enemy, new[] { UnitFootprint.Hex7, UnitFootprint.Single, UnitFootprint.Single }, anchors, covered));
            Assert.AreEqual(new HexCoordinate(2, -4), anchors[0], "the middle row of the zone, centred");
            Assert.AreEqual(9, covered.Count);
            Assert.AreEqual(9, new HashSet<HexCoordinate>(covered).Count);

            Assert.IsTrue(DeploymentPacker.TryPack(grid, BattleTeam.Enemy, new[] { UnitFootprint.Triangle, UnitFootprint.Triangle, UnitFootprint.Single }, anchors, covered));
            Assert.AreEqual(new HexCoordinate(1, -3), anchors[0]);
            Assert.AreEqual(new HexCoordinate(3, -3), anchors[1]);
            Assert.AreEqual(7, new HashSet<HexCoordinate>(covered).Count);
        }

        [Test]
        public void Packer_RefusesAGiantOnASmallBoard_AndAnOverfullZone()
        {
            List<HexCoordinate> anchors = new List<HexCoordinate>();
            List<HexCoordinate> covered = new List<HexCoordinate>();

            Assert.IsFalse(DeploymentPacker.TryPack(new HexGrid(ArenaSize.Small), BattleTeam.Enemy, new[] { UnitFootprint.Hex7 }, anchors, covered),
                           "a Small zone is two rows deep");
            Assert.IsTrue(DeploymentPacker.TryPack(new HexGrid(ArenaSize.Small), BattleTeam.Enemy, new[] { UnitFootprint.Triangle }, anchors, covered));

            // Medium's 24-tile zone, eight wide: two giants seat side by side, a third does not.
            Assert.IsTrue(DeploymentPacker.TryPack(new HexGrid(ArenaSize.Medium), BattleTeam.Enemy, new[] { UnitFootprint.Hex7, UnitFootprint.Hex7 }, anchors, covered));
            Assert.IsFalse(DeploymentPacker.TryPack(new HexGrid(ArenaSize.Medium), BattleTeam.Enemy, new[] { UnitFootprint.Hex7, UnitFootprint.Hex7, UnitFootprint.Hex7 }, anchors, covered));
            Assert.AreEqual(2, anchors.Count, "the first two fitted; the third is where it failed");

            UnitFootprint[] overfull = new UnitFootprint[25];
            Assert.IsFalse(DeploymentPacker.TryPack(new HexGrid(ArenaSize.Medium), BattleTeam.Enemy, overfull, anchors, covered));
        }

        // ---------------------------------------------------------------------------------------
        // Pathfinder.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Pathfinder_SingleOverload_IsTheOneTileSearch_OnRandomBoards()
        {
            Random rng = new Random(4242);
            ArenaSize[] sizes = { ArenaSize.Small, ArenaSize.Medium, ArenaSize.Large };

            for (int round = 0; round < 300; round++)
            {
                HexGrid grid = RandomBoard(rng, sizes[rng.Next(sizes.Length)]);
                int radius = Span(grid);

                for (int q = 0; q < 20; q++)
                {
                    HexCoordinate start = new HexCoordinate(rng.Next(-radius, radius + 1), rng.Next(-radius, radius + 1));
                    HexCoordinate goal = new HexCoordinate(rng.Next(-radius, radius + 1), rng.Next(-radius, radius + 1));
                    string mover = "u" + rng.Next(34);
                    if (grid.TryGetPosition(mover, out HexCoordinate at))
                    {
                        start = at;
                    }

                    CollectionAssert.AreEqual(HexPathfinder.FindPath(grid, start, goal, mover), HexPathfinder.FindPath(grid, start, goal, mover, UnitFootprint.Single));
                }
            }
        }

        [Test]
        public void Pathfinder_LargeFootprint_FindsAShortestRouteOfAnchorsWhereItFits()
        {
            Random rng = new Random(99);
            UnitFootprint[] sizes = { UnitFootprint.Triangle, UnitFootprint.Hex7 };

            for (int round = 0; round < 150; round++)
            {
                HexGrid grid = new HexGrid(ArenaSize.Large);
                for (int b = 0; b < 30; b++)
                {
                    grid.SetBlocked(new HexCoordinate(rng.Next(-7, 8), rng.Next(-7, 8)), true);
                }

                UnitFootprint size = sizes[rng.Next(sizes.Length)];
                HexCoordinate start = new HexCoordinate(rng.Next(-5, 6), rng.Next(-5, 6));
                if (!grid.TryPlaceUnit("m", start, size))
                {
                    continue;
                }

                Dictionary<HexCoordinate, int> reference = BreadthFirst(grid, start, size);

                for (int q = 0; q < 10; q++)
                {
                    HexCoordinate goal = new HexCoordinate(rng.Next(-7, 8), rng.Next(-7, 8));
                    IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, start, goal, "m", size);

                    if (!grid.IsInBounds(goal) || !reference.TryGetValue(goal, out int steps))
                    {
                        CollectionAssert.IsEmpty(path, "unreachable " + goal);
                        continue;
                    }

                    Assert.AreEqual(steps + 1, path.Count, "shortest");
                    Assert.AreEqual(start, path[0]);
                    for (int i = 1; i < path.Count; i++)
                    {
                        Assert.AreEqual(1, path[i].Distance(path[i - 1]));
                        Assert.IsTrue(grid.CanStand(path[i], size, "m"));
                    }
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Species and roster.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Factory_CopiesTheSpeciesFootprint_DefaultSingle()
        {
            CreatureSpeciesSO species = new CreatureSpeciesSO();
            Assert.AreEqual(UnitFootprint.Single, BattleUnitFactory.CreateBeast("a", BattleTeam.Player, species, 1, null, HexCoordinate.Zero).Footprint);
            species.Footprint = UnitFootprint.Hex7;
            Assert.AreEqual(UnitFootprint.Hex7, BattleUnitFactory.CreateBeast("g", BattleTeam.Enemy, species, 1, null, HexCoordinate.Zero).Footprint);
            Assert.AreEqual(UnitFootprint.Single, BattleUnitFactory.CreateBeast("n", BattleTeam.Enemy, null, 1, null, HexCoordinate.Zero).Footprint);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        /// <summary>The largest |Q| or |R| of any tile on the board, so random coordinates in +/- this cover it (and some off-board tiles too).</summary>
        private static int Span(HexGrid grid)
        {
            int span = 0;
            foreach (HexCoordinate tile in grid.Tiles)
            {
                span = Math.Max(span, Math.Max(Math.Abs(tile.Q), Math.Abs(tile.R)));
            }

            return span;
        }

        private static HexGrid RandomBoard(Random rng, ArenaSize size)
        {
            HexGrid grid = new HexGrid(size);
            int radius = Span(grid);
            int ops = rng.Next(0, 120);
            for (int op = 0; op < ops; op++)
            {
                HexCoordinate tile = new HexCoordinate(rng.Next(-radius, radius + 1), rng.Next(-radius, radius + 1));
                if (rng.Next(3) == 0)
                {
                    grid.SetBlocked(tile, true);
                }
                else
                {
                    grid.TryPlaceUnit("u" + rng.Next(30), tile);
                }
            }

            return grid;
        }

        /// <summary>Plain breadth-first anchor distances for a footprint, the reference the A* must match in length.</summary>
        private static Dictionary<HexCoordinate, int> BreadthFirst(HexGrid grid, HexCoordinate start, UnitFootprint size)
        {
            Dictionary<HexCoordinate, int> cost = new Dictionary<HexCoordinate, int> { { start, 0 } };
            Queue<HexCoordinate> queue = new Queue<HexCoordinate>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                HexCoordinate current = queue.Dequeue();
                foreach (HexCoordinate next in current.Neighbors())
                {
                    if (!cost.ContainsKey(next) && grid.CanStand(next, size, "m"))
                    {
                        cost[next] = cost[current] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            return cost;
        }

        private static int BruteDistance(List<HexCoordinate> a, List<HexCoordinate> b)
        {
            int best = int.MaxValue;
            foreach (HexCoordinate x in a)
            {
                foreach (HexCoordinate y in b)
                {
                    best = Math.Min(best, x.Distance(y));
                }
            }

            return best;
        }

        private static int CountTiles(HexGrid grid, string id)
        {
            int count = 0;
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (grid.GetOccupant(tile) == id)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertCovers(HexGrid grid, BattleUnit unit)
        {
            Assert.IsTrue(grid.TryGetPosition(unit.Id, out HexCoordinate anchor));
            Assert.AreEqual(unit.Position, anchor, "grid and unit agree on the anchor");
            Assert.AreEqual(Footprints.TileCount(unit.Footprint), CountTiles(grid, unit.Id));
            foreach (HexCoordinate tile in Footprints.Tiles(anchor, unit.Footprint))
            {
                Assert.AreEqual(unit.Id, grid.GetOccupant(tile));
            }
        }

        private void Knock(BattleUnit caster, BattleUnit target, int hexes, HexGrid grid)
        {
            SkillSO skill = new SkillSO();
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Knockback, Magnitude = hexes });
            _created.Add(skill);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster, null, grid);
        }

        private static BattleTurnResult Turn(BattleUnit unit, List<BattleUnit> all, HexGrid grid)
        {
            return BattleTurnExecutor.ExecuteTurn(unit, all, grid, new Random(1), null);
        }

        private static BattleUnit Place(HexGrid grid, BattleUnit unit)
        {
            Assert.IsTrue(grid.TryPlaceUnit(unit.Id, unit.Position, unit.Footprint), "test setup: could not place " + unit.Id);
            return unit;
        }

        private static BattleUnit Unit(string id, BattleTeam team, CombatStance stance, int moveRange, HexCoordinate position, params SkillSO[] skills)
        {
            return new BattleUnit(id, team, new StatBlock(Sturdy, 10, 10, 10, 10, 10, moveRange), position, new SkillLoadout(skills), null, 1, stance);
        }

        private static BattleUnit Large(string id, BattleTeam team, UnitFootprint footprint, HexCoordinate position)
        {
            return Large(id, team, footprint, 0, position);
        }

        private static BattleUnit Large(string id, BattleTeam team, UnitFootprint footprint, int moveRange, HexCoordinate position, params SkillSO[] skills)
        {
            return new BattleUnit(id, team, new StatBlock(Sturdy, 10, 10, 10, 10, 10, moveRange), position, new SkillLoadout(skills), null, 1,
                                  CombatStance.Vanguard, 0, footprint);
        }

        private SkillSO Skill(int range)
        {
            SkillSO skill = Area(SkillTargetShape.SingleTarget, range);
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            return skill;
        }

        private SkillSO Area(SkillTargetShape shape, int range)
        {
            SkillSO skill = new SkillSO();
            skill.TargetShape = shape;
            skill.Range = range;
            skill.Cooldown = 1;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            _created.Add(skill);
            return skill;
        }
    }
}
