using System;
using System.Collections.Generic;
using System.Linq;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Rolling frame-time window -> average fps and 1% low fps (the average fps of the slowest
    /// 1% of frames in the window -- the standard "1% low" stutter metric, not a percentile of fps
    /// values directly, which would under-weight long frames). Also tracks the other on-screen/--bench
    /// stats the task brief asks for: draw calls, triangles, skinning time, a managed+GPU memory estimate.</summary>
    public sealed class StatsTracker
    {
        private readonly List<double> _frameMs = new List<double>();
        private readonly double _windowSeconds;

        public int DrawCallsThisFrame;
        public int TrianglesThisFrame;
        public double SkinningMsThisFrame;
        public double SkinningMsEma; // exponential moving average, smoother for on-screen display

        public StatsTracker(double windowSeconds = 3.0)
        {
            _windowSeconds = windowSeconds;
        }

        public void RecordFrame(double frameMs)
        {
            _frameMs.Add(frameMs);
            double total = 0;
            int cut = _frameMs.Count;
            for (int i = _frameMs.Count - 1; i >= 0; i--)
            {
                total += _frameMs[i];
                if (total / 1000.0 > _windowSeconds)
                { cut = i; break; }
            }
            if (cut > 0)
                _frameMs.RemoveRange(0, cut);

            SkinningMsEma = SkinningMsEma <= 0 ? SkinningMsThisFrame : SkinningMsEma * 0.9 + SkinningMsThisFrame * 0.1;
        }

        public double AverageFps()
        {
            if (_frameMs.Count == 0)
                return 0;
            double meanMs = _frameMs.Average();
            return meanMs > 0 ? 1000.0 / meanMs : 0;
        }

        public double OnePercentLowFps()
        {
            if (_frameMs.Count == 0)
                return 0;
            var sorted = _frameMs.OrderByDescending(x => x).ToList(); // longest frames first
            int n = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
            double worstMean = sorted.Take(n).Average();
            return worstMean > 0 ? 1000.0 / worstMean : 0;
        }

        public int SampleCount => _frameMs.Count;

        public static long EstimateManagedBytes() => GC.GetTotalMemory(false);
    }
}
