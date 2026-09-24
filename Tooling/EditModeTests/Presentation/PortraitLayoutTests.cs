using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The portrait battle screen's layout maths (<see cref="PortraitLayout"/>): the 1080x1920
    /// canvas letterboxed onto any screen inside its safe area, the bands stacked without
    /// overlapping, and every arena fitted to the board band.
    /// </summary>
    public class PortraitLayoutTests
    {
        private const int W = PortraitLayout.CanvasWidth;
        private const int H = PortraitLayout.CanvasHeight;

        [Test]
        public void Canvas_Is9By16()
        {
            Assert.AreEqual(9f / 16f, W / (float)H, 1e-6f);
        }

        [Test]
        public void DesktopWindow_540x960_IsHalfScale_NoBars()
        {
            CanvasFit fit = CanvasFit.Of(W, H, 540, 960, SafeInsets.None);

            Assert.AreEqual(0.5f, fit.Scale, 1e-6f);
            Assert.AreEqual(0f, fit.OffsetX, 1e-4f);
            Assert.AreEqual(0f, fit.OffsetY, 1e-4f);
        }

        [Test]
        public void TallPhone_GetsBarsTopAndBottom()
        {
            CanvasFit fit = CanvasFit.Of(W, H, 1080, 2400, SafeInsets.None);

            Assert.AreEqual(1f, fit.Scale, 1e-6f);
            Assert.AreEqual(0f, fit.OffsetX, 1e-4f);
            Assert.AreEqual(240f, fit.OffsetY, 1e-4f);
        }

        [Test]
        public void LandscapeWindow_GetsBarsLeftAndRight()
        {
            CanvasFit fit = CanvasFit.Of(W, H, 1920, 1080, SafeInsets.None);

            Assert.AreEqual(1080f / 1920f, fit.Scale, 1e-6f);
            Assert.AreEqual((1920f - W * fit.Scale) / 2f, fit.OffsetX, 1e-3f);
            Assert.AreEqual(0f, fit.OffsetY, 1e-4f);
        }

        [Test]
        public void SafeInsets_KeepTheCanvasOffTheCutout()
        {
            // A 1080x2400 phone with a 120 px notch on top and a 60 px gesture bar below.
            CanvasFit fit = CanvasFit.Of(W, H, 1080, 2400, new SafeInsets(0, 120, 0, 60));
            Rect screen = fit.Screen(W, H);

            Assert.GreaterOrEqual(screen.Y, 120f);
            Assert.LessOrEqual(screen.Bottom, 2400f - 60f + 1e-3f);
            Assert.AreEqual(1f, fit.Scale, 1e-6f, "the safe area is still taller than 9:16, so nothing shrinks");

            CanvasFit squeezed = CanvasFit.Of(W, H, 1080, 1920, new SafeInsets(0, 100, 0, 0));
            Assert.Less(squeezed.Scale, 1f);
            Assert.GreaterOrEqual(squeezed.Screen(W, H).Y, 100f - 1e-3f);
        }

        [Test]
        public void ToCanvas_InvertsTheFit()
        {
            CanvasFit fit = CanvasFit.Of(W, H, 1440, 3200, new SafeInsets(0, 90, 0, 0));
            Vec2 corner = fit.ToCanvas(fit.OffsetX, fit.OffsetY);
            Vec2 far = fit.ToCanvas(fit.OffsetX + W * fit.Scale, fit.OffsetY + H * fit.Scale);

            Assert.AreEqual(0f, corner.X, 1e-3f);
            Assert.AreEqual(0f, corner.Y, 1e-3f);
            Assert.AreEqual(W, far.X, 1e-2f);
            Assert.AreEqual(H, far.Y, 1e-2f);
        }

        [Test]
        public void Bands_StackTopToBottom_InsideTheCanvas_WithoutOverlap()
        {
            PortraitLayout layout = new PortraitLayout();
            Rect[] bands = { layout.Header, layout.TurnOrder, layout.Board, layout.Toast, layout.SkillStrip, layout.Controls };

            for (int i = 0; i < bands.Length; i++)
            {
                Assert.GreaterOrEqual(bands[i].X, 0f);
                Assert.LessOrEqual(bands[i].Right, W);
                Assert.Greater(bands[i].Height, 0f);
                if (i > 0)
                {
                    Assert.GreaterOrEqual(bands[i].Y, bands[i - 1].Bottom, "band " + i);
                }
            }

            Assert.LessOrEqual(layout.Controls.Bottom, H);
            Assert.Greater(layout.Board.Height, H / 2f, "the board gets over half the screen");
        }

        [Test]
        public void Slots_AndButtons_StayInTheirBands_AndDoNotOverlap()
        {
            PortraitLayout layout = new PortraitLayout();
            for (int i = 0; i < 8; i++)
            {
                Rect slot = layout.TurnOrderSlot(i, 8);
                Assert.AreEqual(slot.Width, slot.Height, 1e-3f, "portrait slots are square");
                Assert.LessOrEqual(slot.Right, layout.TurnOrder.Right + 1e-3f);
                Assert.LessOrEqual(slot.Bottom, layout.TurnOrder.Bottom + 1e-3f);
                if (i > 0)
                {
                    Assert.Greater(slot.X, layout.TurnOrderSlot(i - 1, 8).Right);
                }
            }

            for (int i = 0; i < PortraitLayout.ControlCount; i++)
            {
                Rect button = layout.Control(i);
                Assert.LessOrEqual(button.Right, layout.Controls.Right + 1e-3f);
                Assert.GreaterOrEqual(button.Width, 96f, "a comfortable touch target");
            }

            for (int i = 0; i < 4; i++)
            {
                Assert.LessOrEqual(layout.SkillCard(i, 3).Right, layout.SkillStrip.Right + 1e-3f);
                Assert.GreaterOrEqual(layout.SkillCard(i, 3).Y, layout.SkillStrip.Y);
            }
        }

        [TestCase(ArenaSize.Small)]
        [TestCase(ArenaSize.Medium)]
        [TestCase(ArenaSize.Large)]
        public void EveryArena_FitsTheBoardBand_Centred(ArenaSize size)
        {
            PortraitLayout layout = new PortraitLayout();
            HexGrid grid = new HexGrid(size);
            BoardFit fit = layout.FitBoard(grid.Radius);
            HexLayout hexes = new HexLayout(0, 0);

            foreach (HexCoordinate tile in grid.Tiles)
            {
                Vec2 center = fit.ToCanvas(hexes.Center(tile));
                float halfW = HexLayout.TileWidth / 2f * fit.Scale;
                float halfH = HexLayout.TileHeight / 2f * fit.Scale;
                Assert.IsTrue(layout.Board.Contains(center.X - halfW + 0.01f, center.Y - halfH + 0.01f), size + " " + tile);
                Assert.IsTrue(layout.Board.Contains(center.X + halfW - 0.01f, center.Y + halfH - 0.01f), size + " " + tile);
            }

            Vec2 origin = fit.ToCanvas(Vec2.Zero);
            Assert.AreEqual(layout.Board.Center.X, origin.X, 1e-3f);
            Assert.AreEqual(layout.Board.Center.Y, origin.Y, 1e-3f);
            Assert.Greater(fit.Scale, 2f, "even the large arena (a horde) draws its hexes at over twice the placeholder size");

            Vec2 back = fit.ToBoard(origin.X + 64f, origin.Y - 32f);
            Assert.AreEqual(64f / fit.Scale, back.X, 1e-3f);
            Assert.AreEqual(-32f / fit.Scale, back.Y, 1e-3f);
        }

        [Test]
        public void TheLargeArena_IsDrawnSmallerThanTheMedium()
        {
            PortraitLayout layout = new PortraitLayout();
            Assert.Less(layout.FitBoard(new HexGrid(ArenaSize.Large).Radius).Scale, layout.FitBoard(new HexGrid(ArenaSize.Medium).Radius).Scale);
        }
    }
}
