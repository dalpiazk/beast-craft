using System.Collections.Generic;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The journal UI kit's nine-slice math (<see cref="NineSlicePatch"/>): corners never stretch,
    /// edges stretch along one axis, the centre both, a 3-slice pill (insets zero on one axis) never
    /// splits that axis, and an inset too big for the destination box shrinks instead of overlapping.
    /// </summary>
    public class NineSlicePatchTests
    {
        private const float FrameW = 200f;
        private const float FrameH = 160f;
        private const float L = 40f;
        private const float T = 30f;
        private const float R = 50f;
        private const float B = 20f;

        [Test]
        public void NineCellsAlwaysReturned_CentreIsIndex4()
        {
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, new Rect(0f, 0f, 400f, 300f));

            Assert.AreEqual(9, cells.Count);
        }

        [Test]
        public void Corners_AreNotStretched_SourceAndDestMatch()
        {
            Rect box = new Rect(10f, 20f, 400f, 300f);
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, box);

            // Index 0: top-left corner.
            NineSliceCell topLeft = cells[0];
            Assert.AreEqual(L, topLeft.Source.Width, 1e-4f);
            Assert.AreEqual(T, topLeft.Source.Height, 1e-4f);
            Assert.AreEqual(topLeft.Source.Width, topLeft.Dest.Width, 1e-4f);
            Assert.AreEqual(topLeft.Source.Height, topLeft.Dest.Height, 1e-4f);
            Assert.AreEqual(box.X, topLeft.Dest.X, 1e-4f);
            Assert.AreEqual(box.Y, topLeft.Dest.Y, 1e-4f);

            // Index 8: bottom-right corner, anchored to the box's own bottom-right.
            NineSliceCell bottomRight = cells[8];
            Assert.AreEqual(R, bottomRight.Dest.Width, 1e-4f);
            Assert.AreEqual(B, bottomRight.Dest.Height, 1e-4f);
            Assert.AreEqual(box.Right, bottomRight.Dest.Right, 1e-4f);
            Assert.AreEqual(box.Bottom, bottomRight.Dest.Bottom, 1e-4f);
            Assert.AreEqual(FrameW - R, bottomRight.Source.X, 1e-4f);
            Assert.AreEqual(FrameH - B, bottomRight.Source.Y, 1e-4f);
        }

        [Test]
        public void Edges_StretchAlongOneAxisOnly()
        {
            Rect box = new Rect(0f, 0f, 400f, 300f);
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, box);

            // Index 1: top edge (row 0, col 1) stretches horizontally, fixed source height T.
            NineSliceCell topEdge = cells[1];
            Assert.AreEqual(FrameW - L - R, topEdge.Source.Width, 1e-4f);
            Assert.AreEqual(T, topEdge.Source.Height, 1e-4f);
            Assert.AreEqual(box.Width - L - R, topEdge.Dest.Width, 1e-4f);
            Assert.AreEqual(T, topEdge.Dest.Height, 1e-4f);

            // Index 3: left edge (row 1, col 0) stretches vertically, fixed source width L.
            NineSliceCell leftEdge = cells[3];
            Assert.AreEqual(L, leftEdge.Source.Width, 1e-4f);
            Assert.AreEqual(FrameH - T - B, leftEdge.Source.Height, 1e-4f);
            Assert.AreEqual(L, leftEdge.Dest.Width, 1e-4f);
            Assert.AreEqual(box.Height - T - B, leftEdge.Dest.Height, 1e-4f);
        }

        [Test]
        public void Centre_StretchesBothAxes()
        {
            Rect box = new Rect(0f, 0f, 400f, 300f);
            NineSliceCell centre = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, box)[4];

            Assert.AreEqual(FrameW - L - R, centre.Source.Width, 1e-4f);
            Assert.AreEqual(FrameH - T - B, centre.Source.Height, 1e-4f);
            Assert.AreEqual(box.Width - L - R, centre.Dest.Width, 1e-4f);
            Assert.AreEqual(box.Height - T - B, centre.Dest.Height, 1e-4f);
        }

        [Test]
        public void ZeroOnOneAxis_NeverSplitsThatAxis_A3SlicePill()
        {
            // A slider rail / toggle track: Top = Bottom = 0, so the whole height is one row.
            Rect box = new Rect(5f, 5f, 180f, 32f);
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(120f, 32f, 16f, 0f, 16f, 0f, box);

            // Rows 0 and 2 (indices 0-2 and 6-8) are zero height; only the middle row (3-5) has any.
            Assert.AreEqual(0f, cells[0].Dest.Height, 1e-4f);
            Assert.AreEqual(0f, cells[7].Dest.Height, 1e-4f);
            Assert.AreEqual(box.Height, cells[4].Dest.Height, 1e-4f);
            Assert.AreEqual(32f, cells[4].Source.Height, 1e-4f);

            // The left/right caps keep their full native width, only the centre stretches.
            Assert.AreEqual(16f, cells[3].Dest.Width, 1e-4f);
            Assert.AreEqual(16f, cells[5].Dest.Width, 1e-4f);
            Assert.AreEqual(box.Width - 32f, cells[4].Dest.Width, 1e-4f);
        }

        [Test]
        public void InsetsTooBigForTheBox_ShrinkProportionally_InsteadOfOverlapping()
        {
            // Left + Right (40 + 50 = 90) is more than a 60px-wide box: both must shrink together,
            // in the same 40:50 ratio, so the corners meet exactly at the centre with no overlap and
            // no gap, and the corner is still drawn 1:1 (the source side shrinks by the same amount).
            Rect box = new Rect(0f, 0f, 60f, 200f);
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, box);

            float left = cells[0].Dest.Width;
            float right = cells[2].Dest.Width;
            Assert.AreEqual(60f, left + right, 1e-3f, "the two corners must meet exactly, not overlap");
            Assert.AreEqual(L / R, left / right, 1e-3f, "shrunk in the same ratio as the original insets");
            Assert.AreEqual(left, cells[0].Source.Width, 1e-4f, "the source corner shrinks by the same amount (1:1, never stretched)");
            Assert.AreEqual(0f, cells[1].Dest.Width, 1e-4f, "no room left for a centre column");
        }

        [Test]
        public void TinyBox_NeverProducesNegativeSizes()
        {
            Rect box = new Rect(0f, 0f, 3f, 2f);
            IReadOnlyList<NineSliceCell> cells = NineSlicePatch.Build(FrameW, FrameH, L, T, R, B, box);

            foreach (NineSliceCell cell in cells)
            {
                Assert.GreaterOrEqual(cell.Source.Width, 0f);
                Assert.GreaterOrEqual(cell.Source.Height, 0f);
                Assert.GreaterOrEqual(cell.Dest.Width, 0f);
                Assert.GreaterOrEqual(cell.Dest.Height, 0f);
            }
        }
    }
}
