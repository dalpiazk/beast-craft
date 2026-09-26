using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The auto camera (<see cref="CameraRig"/>, <see cref="TurnCamera"/>): the fit-all view is the
    /// old fixed board fit, framing zooms onto what it is given with padding, within the zoom limits
    /// and clamped to the arena (the largest arena, hordes of two dozen units and multi-hex units
    /// included), easing is smooth and deterministic, and a real turn's camera starts where it was,
    /// frames the actor and its targets, and is a pure function of the turn clock.
    /// </summary>
    public class CameraTests
    {
        private const float Tolerance = 0.01f;
        private static readonly PortraitLayout Screen = new PortraitLayout();
        private static readonly HexLayout Layout = new HexLayout(0, 0);

        [TestCase(ArenaSize.Small)]
        [TestCase(ArenaSize.Medium)]
        [TestCase(ArenaSize.Large)]
        public void FitAll_IsTheFixedBoardFit(ArenaSize size)
        {
            HexGrid grid = new HexGrid(size);
            CameraRig rig = new CameraRig(grid.Width, grid.Height, Screen.Board);
            BoardFit fixedFit = Screen.FitBoard(grid.Width, grid.Height);
            BoardFit fit = rig.Fit(rig.FitAll);

            Assert.AreEqual(fixedFit.Scale, fit.Scale, Tolerance);
            Assert.AreEqual(fixedFit.OriginX, fit.OriginX, Tolerance);
            Assert.AreEqual(fixedFit.OriginY, fit.OriginY, Tolerance);
            Assert.AreEqual(1f, rig.FitAll.Zoom);
            Assert.IsTrue(Inside(rig.Bounds, rig.Visible(rig.FitAll)), "fit-all shows the whole arena");
        }

        [Test]
        public void Frame_OneUnitMidArena_ZoomsToTheLimit_CentredOnIt()
        {
            CameraRig rig = LargeRig();
            Rect unit = CameraRig.UnitBox(Layout.Center(new HexCoordinate(1, 0)), 1f);

            CameraView view = rig.Frame(new[] { unit });

            Assert.AreEqual(rig.MaxZoomFor, view.Zoom, Tolerance);
            Assert.AreEqual(unit.Center.X, view.Center.X, Tolerance);
            Assert.AreEqual(unit.Center.Y, view.Center.Y, Tolerance);
            Assert.IsTrue(Inside(Pad(unit, rig.Settings.Padding), rig.Visible(view)), "the unit and its padding are in view");
        }

        [Test]
        public void Frame_TwoUnitsApart_FitsBothWithPadding_BetweenTheLimits()
        {
            CameraRig rig = LargeRig();
            Rect a = CameraRig.UnitBox(Layout.Center(new HexCoordinate(-5, 2)), 1f);
            Rect b = CameraRig.UnitBox(Layout.Center(new HexCoordinate(5, -2)), 1f);

            CameraView view = rig.Frame(new[] { a, b });

            Assert.Greater(view.Zoom, 1f);
            Assert.Less(view.Zoom, rig.MaxZoomFor);
            Assert.IsTrue(Inside(Pad(a, rig.Settings.Padding), rig.Visible(view)));
            Assert.IsTrue(Inside(Pad(b, rig.Settings.Padding), rig.Visible(view)));
        }

        [Test]
        public void Frame_AHordeOfTwoDozenAcrossTheLargestArena_ShowsThemAll()
        {
            CameraRig rig = LargeRig();
            HexGrid grid = new HexGrid(ArenaSize.Large);
            List<Rect> horde = new List<Rect>();
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (horde.Count < 24 && (tile.Q + 2 * tile.R) % 5 == 0)
                {
                    horde.Add(CameraRig.UnitBox(Layout.Center(tile), 1f));
                }
            }

            // The arena's far left and right edges (an even row's first tile, an odd row's last)
            // and its top and bottom rows.
            horde.Add(CameraRig.UnitBox(Layout.Center(HexGrid.FromOffset(grid.MaxColumn, 1)), 1f));
            horde.Add(CameraRig.UnitBox(Layout.Center(HexGrid.FromOffset(grid.MinColumn, 0)), 1f));
            horde.Add(CameraRig.UnitBox(Layout.Center(HexGrid.FromOffset(0, grid.MinRow)), 1f));
            horde.Add(CameraRig.UnitBox(Layout.Center(HexGrid.FromOffset(0, grid.MaxRow)), 1f));

            CameraView view = rig.Frame(horde);

            Assert.GreaterOrEqual(horde.Count, 24);
            Assert.Less(view.Zoom, 1.1f, "a horde spread over the arena needs about the whole of it (the headroom is all it can trim)");
            Rect seen = rig.Visible(view);
            foreach (Rect unit in horde)
            {
                Assert.IsTrue(Inside(Clip(unit, rig.Bounds), seen), unit.ToString());
            }
        }

        [Test]
        public void Frame_AUnitInTheCorner_IsClampedToTheArena()
        {
            CameraRig rig = LargeRig();
            HexGrid grid = new HexGrid(ArenaSize.Large);
            Rect corner = CameraRig.UnitBox(Layout.Center(HexGrid.FromOffset(grid.MaxColumn, grid.MinRow)), 1f);

            CameraView view = rig.Frame(new[] { corner });
            Rect seen = rig.Visible(view);

            Assert.AreEqual(rig.MaxZoomFor, view.Zoom, Tolerance);
            Assert.IsTrue(Inside(seen, rig.Bounds), "never shows past the arena: " + seen + " in " + rig.Bounds);
            Assert.IsTrue(Inside(Clip(corner, rig.Bounds), seen), "the unit is still in view");
            Assert.Greater(view.Center.Y, corner.Center.Y + 1f, "the centre moved in (down, off the top row) to keep the view inside");
        }

        [Test]
        public void Clamp_KeepsZoomInItsLimits_AndCentresWhatIsWiderThanTheArena()
        {
            CameraRig rig = new CameraRig(8, 11, Screen.Board);

            Assert.AreEqual(1f, rig.Clamp(new CameraView(new Vec2(500f, -500f), 0.2f)).Zoom);
            Assert.AreEqual(rig.MaxZoomFor, rig.Clamp(new CameraView(Vec2.Zero, 99f)).Zoom);
            CameraView far = rig.Clamp(new CameraView(new Vec2(500f, -500f), 1f));
            Assert.AreEqual(rig.Bounds.Center.X, far.Center.X, Tolerance, "at fit-all there is no room to pan");
            Assert.AreEqual(rig.Bounds.Center.Y, far.Center.Y, Tolerance);
        }

        [TestCase(ArenaSize.Small)]
        [TestCase(ArenaSize.Medium)]
        [TestCase(ArenaSize.Large)]
        public void MaxZoom_IsCappedByAbsoluteScale_AndNeverBelowFitAll(ArenaSize size)
        {
            HexGrid grid = new HexGrid(size);
            CameraRig rig = new CameraRig(grid.Width, grid.Height, Screen.Board);

            Assert.GreaterOrEqual(rig.MaxZoomFor, 1f);
            Assert.LessOrEqual(rig.MaxZoomFor, rig.Settings.MaxZoom + Tolerance);
            Assert.LessOrEqual(rig.FitScale * rig.MaxZoomFor, Math.Max(rig.FitScale, rig.Settings.MaxScale) + Tolerance);
        }

        [Test]
        public void MultiHexUnits_TakeTwiceTheRoom_SoTheCameraStaysFurtherOut()
        {
            CameraRig rig = new CameraRig(11, 15, new Rect(0f, 0f, 300f, 300f), new CameraSettings { MaxZoom = 10f, MaxScale = 100f });
            Vec2 feet = Layout.FootprintCenter(new HexCoordinate(0, 0), UnitFootprint.Triangle);
            Rect small = CameraRig.UnitBox(feet, 1f);
            Rect big = CameraRig.UnitBox(feet, 2f);

            Assert.AreEqual(2f * small.Width, big.Width, Tolerance);
            Assert.AreEqual(2f * small.Height, big.Height, Tolerance);
            Assert.Less(rig.Frame(new[] { big }).Zoom, rig.Frame(new[] { small }).Zoom);
        }

        [Test]
        public void Ease_StartsAndEndsExactly_IsSmooth_AndDeterministic()
        {
            CameraRig rig = LargeRig();
            CameraView from = rig.FitAll;
            CameraView to = rig.Frame(new[] { CameraRig.UnitBox(Layout.Center(new HexCoordinate(2, 1)), 1f) });

            Assert.AreEqual(from, rig.Ease(from, to, 0, 400));
            Assert.AreEqual(to, rig.Ease(from, to, 400, 400));
            Assert.AreEqual(to, rig.Ease(from, to, 5000, 400));
            Assert.AreEqual(to, rig.Ease(from, to, 10, 0), "a zero-length move is a cut");
            Assert.AreEqual(0.5f, CameraRig.Smoothstep(0.5f), Tolerance);
            Assert.Less(CameraRig.Smoothstep(0.1f), 0.1f, "starts gently");
            Assert.Greater(CameraRig.Smoothstep(0.9f), 0.9f, "stops gently");

            float last = from.Zoom;
            for (int ms = 0; ms <= 400; ms += 20)
            {
                CameraView view = rig.Ease(from, to, ms, 400);
                Assert.GreaterOrEqual(view.Zoom, last - Tolerance, "zooms in monotonically");
                Assert.AreEqual(view, rig.Ease(from, to, ms, 400), "the same time gives the same view");
                last = view.Zoom;
            }
        }

        [Test]
        public void TurnCamera_OverRealTurns_StartsWhereItWas_FramesTheAction_AndIsAPureFunctionOfTime()
        {
            GameContent content = VfxLibraryTests.Content;
            BattleSetup setup = DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out string error, null, "boss_r08_deepwild_heart");
            Assert.IsNotNull(setup, error);
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(setup));
            CameraRig rig = new CameraRig(playback.Grid.Width, playback.Grid.Height, Screen.Board);
            Assert.AreEqual(ArenaSize.Large, playback.Grid.Size, "the largest arena");

            CameraView now = rig.FitAll;
            int framedBeats = 0;
            for (int i = 0; i < 40; i++)
            {
                PlayedTurn turn = playback.Advance();
                if (turn == null)
                {
                    break;
                }

                TurnAnimation animation = new TurnAnimation(turn, Layout, content.Vfx, DemoBattle.DefaultSeed);
                TurnCamera camera = new TurnCamera(animation, Layout, rig, now);
                TurnCamera again = new TurnCamera(animation, Layout, rig, now);

                Assert.AreEqual(rig.Clamp(now), camera.Start);
                Assert.AreEqual(camera.Start, camera.Sample(0), "it starts from where the camera was");
                Assert.Greater(camera.Shots.Count, 0);
                for (int s = 1; s < camera.Shots.Count; s++)
                {
                    Assert.Greater(camera.Shots[s].StartMs, camera.Shots[s - 1].StartMs);
                }

                CameraView previous = camera.Sample(0);
                for (int ms = 0; ms <= animation.DurationMs; ms += 10)
                {
                    CameraView view = camera.Sample(ms);
                    Assert.AreEqual(again.Sample(ms), view, "deterministic");
                    Assert.IsTrue(ClampedTo(rig, view), "clamped to the arena at " + ms + " ms");
                    Assert.Less(Distance(previous.Center, view.Center), 40f, "no cut within a turn");
                    previous = view;
                }

                // Once a beat's framing has settled, its caster and targets are in view.
                foreach (ScheduledBeat beat in animation.Beats)
                {
                    int settled = Math.Max(0, beat.StartMs - rig.Settings.LeadMs) + rig.Settings.EaseMs;
                    if (settled >= beat.EndMs || NextShotBefore(camera, beat, settled))
                    {
                        continue;
                    }

                    Rect seen = rig.Visible(camera.Sample(settled));
                    foreach (BeatTarget target in beat.Beat.Targets)
                    {
                        UnitSnapshot unit = turn.After[target.UnitId];
                        Vec2 feet = Layout.FootprintCenter(unit.Position, unit.Footprint);
                        Assert.IsTrue(seen.Contains(feet.X, feet.Y), target.UnitId + " in view at " + settled + " ms");
                    }

                    framedBeats++;
                }

                now = camera.Sample(animation.DurationMs);
            }

            Assert.Greater(framedBeats, 5);
        }

        [Test]
        public void TurnCamera_WithoutBeats_FramesTheActor()
        {
            GameContent content = VfxLibraryTests.Content;
            BattleSetup setup = DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out string error);
            Assert.IsNotNull(setup, error);
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(setup));
            CameraRig rig = new CameraRig(playback.Grid.Width, playback.Grid.Height, Screen.Board);

            PlayedTurn turn = playback.Advance();
            TurnAnimation animation = new TurnAnimation(turn, Layout, null, 1);
            TurnCamera camera = new TurnCamera(animation, Layout, rig, rig.FitAll);
            UnitSnapshot actor = turn.After[turn.Turn.Unit.Id];
            Vec2 feet = Layout.FootprintCenter(actor.Position, actor.Footprint);

            Assert.IsTrue(rig.Visible(camera.Sample(animation.DurationMs)).Contains(feet.X, feet.Y));
            Assert.AreEqual(rig.FitAll, camera.Sample(-5), "before the turn: where it started");
        }

        private static CameraRig LargeRig()
        {
            HexGrid grid = new HexGrid(ArenaSize.Large);
            return new CameraRig(grid.Width, grid.Height, Screen.Board);
        }

        private static bool NextShotBefore(TurnCamera camera, ScheduledBeat beat, int ms)
        {
            int own = -1;
            for (int s = 0; s < camera.Shots.Count; s++)
            {
                if (camera.Shots[s].StartMs <= Math.Max(0, beat.StartMs))
                {
                    own = s;
                }
            }

            return own + 1 < camera.Shots.Count && camera.Shots[own + 1].StartMs <= ms;
        }

        /// <summary>On each axis the view shows only the arena, or (showing more than it) is centred on it.</summary>
        private static bool ClampedTo(CameraRig rig, CameraView view)
        {
            Rect seen = rig.Visible(view);
            Rect bounds = rig.Bounds;
            bool x = seen.Width >= bounds.Width ? Math.Abs(seen.Center.X - bounds.Center.X) <= Tolerance
                                                : seen.X >= bounds.X - Tolerance && seen.Right <= bounds.Right + Tolerance;
            bool y = seen.Height >= bounds.Height ? Math.Abs(seen.Center.Y - bounds.Center.Y) <= Tolerance
                                                  : seen.Y >= bounds.Y - Tolerance && seen.Bottom <= bounds.Bottom + Tolerance;
            return x && y && view.Zoom >= 1f && view.Zoom <= rig.MaxZoomFor + Tolerance;
        }

        private static float Distance(Vec2 a, Vec2 b)
        {
            return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private static Rect Pad(Rect r, float pad)
        {
            return new Rect(r.X - pad, r.Y - pad, r.Width + 2f * pad, r.Height + 2f * pad);
        }

        private static Rect Clip(Rect r, Rect to)
        {
            float x = Math.Max(r.X, to.X);
            float y = Math.Max(r.Y, to.Y);
            return new Rect(x, y, Math.Max(0f, Math.Min(r.Right, to.Right) - x), Math.Max(0f, Math.Min(r.Bottom, to.Bottom) - y));
        }

        private static bool Inside(Rect inner, Rect outer)
        {
            return inner.X >= outer.X - Tolerance && inner.Y >= outer.Y - Tolerance && inner.Right <= outer.Right + Tolerance &&
                   inner.Bottom <= outer.Bottom + Tolerance;
        }
    }
}
