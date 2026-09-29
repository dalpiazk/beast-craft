using BeastCraft.Presentation.Diagnostics;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>The frame-time overlay's arithmetic (#64): a rolling window, nearest-rank percentiles, shares over a budget.</summary>
    public class FrameStatsTests
    {
        [Test]
        public void Empty_ReadsZero()
        {
            FrameStats stats = new FrameStats(8);

            Assert.AreEqual(0, stats.Count);
            Assert.AreEqual(0.0, stats.Mean);
            Assert.AreEqual(0.0, stats.Max);
            Assert.AreEqual(0.0, stats.Percentile(0.95));
            Assert.AreEqual(0.0, stats.ShareOver(FrameStats.TargetMs));
        }

        [Test]
        public void Percentiles_AreNearestRank()
        {
            FrameStats stats = new FrameStats(100);
            for (int i = 100; i >= 1; i--)
            {
                stats.Add(i);
            }

            Assert.AreEqual(50.0, stats.Percentile(0.50));
            Assert.AreEqual(95.0, stats.Percentile(0.95));
            Assert.AreEqual(99.0, stats.Percentile(0.99));
            Assert.AreEqual(100.0, stats.Percentile(1.0));
            Assert.AreEqual(1.0, stats.Percentile(0.0));
            Assert.AreEqual(50.5, stats.Mean, 1e-9);
            Assert.AreEqual(1.0, stats.Last);
        }

        [Test]
        public void TheWindow_KeepsOnlyTheMostRecentFrames()
        {
            FrameStats stats = new FrameStats(4);
            stats.Add(100.0);
            for (int i = 0; i < 4; i++)
            {
                stats.Add(10.0);
            }

            Assert.AreEqual(4, stats.Count);
            Assert.AreEqual(5, stats.Total);
            Assert.AreEqual(10.0, stats.Max, "the 100 ms frame has left the window");
            Assert.AreEqual(10.0, stats.Percentile(0.95));
        }

        [Test]
        public void ShareOver_CountsFramesOverTheBudget()
        {
            FrameStats stats = new FrameStats(10);
            double[] frames = { 10, 12, 16, 17, 20, 30, 34, 40, 8, 9 };
            foreach (double ms in frames)
            {
                stats.Add(ms);
            }

            Assert.AreEqual(0.5, stats.ShareOver(FrameStats.TargetMs), 1e-9);
            Assert.AreEqual(0.2, stats.ShareOver(FrameStats.FloorMs), 1e-9);
            StringAssert.Contains("p95 40.00", stats.Summary("frame"));
        }

        [Test]
        public void BadSamples_ReadAsZero()
        {
            FrameStats stats = new FrameStats(4);
            stats.Add(double.NaN);
            stats.Add(-3.0);

            Assert.AreEqual(0.0, stats.Max);
            Assert.AreEqual(2, stats.Count);
        }
    }
}
