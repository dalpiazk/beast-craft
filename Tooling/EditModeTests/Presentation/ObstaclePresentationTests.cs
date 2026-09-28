using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The viewer and the battle agree on the battlefield: a battle in r01 draws the backdrop its
    /// layout's obstacles are painted on; the movement preview (<see cref="MovementPreview.Reach"/>)
    /// never offers an obstacle or an occupied tile; and a walk is animated along a route round the
    /// obstacles (<see cref="MovementPreview.WalkPath"/>, <see cref="TurnAnimation"/>), never over one.
    /// </summary>
    public class ObstaclePresentationTests
    {
        private static readonly HexLayout Layout = new HexLayout(0, 0);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [Test]
        public void AnR01Battle_DrawsTheBackdropItsObstaclesArePaintedOn()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (string encounter in new[] { "squad", "horde" })
            {
                for (int seed = 1; seed <= 24; seed++)
                {
                    BattleSessionRun run = BattleSession.Begin(DemoBattle.Create(Content, seed, out _, out _, null, encounter, 30, 30, null, null, "r01"));
                    BattleLayoutEntryData layout = run.Result.Layout;
                    Assert.IsNotNull(layout, encounter + " seed " + seed);
                    Assert.IsNotNull(Content.BattleArt.BackdropByArtKey(layout.ArtKey), layout.ArtKey);
                    Assert.AreEqual(run.Grid.Size.ToString(), Content.BattleArt.BackdropByArtKey(layout.ArtKey).Arena);
                    seen.Add(layout.ArtKey);
                }
            }

            Assert.Greater(seen.Count, 8, "the seeds spread over the six paintings of each arena");
        }

        [Test]
        public void MovementPreview_NeverOffersAnObstacleOrAnOccupiedTile()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            grid.SetBlocked(new HexCoordinate(1, 0), true);
            grid.SetBlocked(new HexCoordinate(0, 1), true);
            Assert.IsTrue(grid.TryPlaceUnit("mover", HexCoordinate.Zero));
            Assert.IsTrue(grid.TryPlaceUnit("friend", new HexCoordinate(-1, 0)));

            List<HexCoordinate> reach = MovementPreview.Reach(grid, HexCoordinate.Zero, UnitFootprint.Single, "mover", 2);
            CollectionAssert.DoesNotContain(reach, new HexCoordinate(1, 0), "an obstacle");
            CollectionAssert.DoesNotContain(reach, new HexCoordinate(0, 1), "an obstacle");
            CollectionAssert.DoesNotContain(reach, new HexCoordinate(-1, 0), "another unit");
            CollectionAssert.DoesNotContain(reach, HexCoordinate.Zero, "not its own tile");
            CollectionAssert.Contains(reach, new HexCoordinate(2, -1), "round the rock");
            CollectionAssert.DoesNotContain(reach, new HexCoordinate(1, 1), "two steps as the crow flies, but walled off by the two rocks");
            foreach (HexCoordinate tile in reach)
            {
                Assert.IsTrue(grid.CanStand(tile, UnitFootprint.Single, "mover"));
                Assert.LessOrEqual(tile.Distance(HexCoordinate.Zero), 2);
            }

            Assert.IsEmpty(MovementPreview.Reach(grid, HexCoordinate.Zero, UnitFootprint.Single, "mover", 0));
        }

        [Test]
        public void WalkPath_GoesRoundObstacles_IgnoringUnits()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            grid.SetBlocked(new HexCoordinate(0, 0), true);
            Assert.IsTrue(grid.TryPlaceUnit("someone", new HexCoordinate(1, 0)));

            List<HexCoordinate> path = MovementPreview.WalkPath(grid, new HexCoordinate(0, 1), new HexCoordinate(0, -1), UnitFootprint.Single);
            Assert.AreEqual(new HexCoordinate(0, 1), path[0]);
            Assert.AreEqual(new HexCoordinate(0, -1), path[path.Count - 1]);
            Assert.AreEqual(4, path.Count);
            CollectionAssert.DoesNotContain(path, new HexCoordinate(0, 0));
            CollectionAssert.AreEqual(new[] { HexCoordinate.Zero, HexCoordinate.Zero }, MovementPreview.WalkPath(null, HexCoordinate.Zero, HexCoordinate.Zero, UnitFootprint.Single));
        }

        [Test]
        public void TurnAnimation_WalksRoundTheObstacles_NeverOverOne()
        {
            int walks = 0;
            foreach (string encounter in new[] { "horde", "squad" })
            {
                for (int seed = 1; seed <= 4; seed++)
                {
                    BattlePlayback playback = new BattlePlayback(BattleSession.Begin(DemoBattle.Create(Content, seed, out _, out _, null, encounter, 30, 30, null, null, "r01")));
                    for (PlayedTurn turn = playback.Advance(); turn != null; turn = playback.Advance())
                    {
                        TurnAnimation animation = new TurnAnimation(turn, Layout, Content.Vfx, seed, null, playback.Grid);
                        UnitSnapshot walker = turn.Before[turn.Turn.Unit.Id];
                        if (animation.MoveMs == 0 || walker.Footprint != UnitFootprint.Single)
                        {
                            continue;
                        }

                        walks++;
                        for (int ms = 0; ms < animation.MoveMs; ms += 8)
                        {
                            Vec2 at = animation.UnitCenter(walker.Id, ms);
                            HexCoordinate under = Layout.TileAt(at.X, at.Y);
                            Assert.IsFalse(playback.Grid.IsBlocked(under) && playback.Grid.IsInBounds(under),
                                           "turn " + turn.Index + " of " + encounter + " seed " + seed + ": the walk crosses the obstacle at " + under + " at " + ms + " ms");
                        }
                    }
                }
            }

            Assert.Greater(walks, 50, "plenty of walks sampled");
        }
    }
}
