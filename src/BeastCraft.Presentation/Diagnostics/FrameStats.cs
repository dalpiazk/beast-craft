using System;
using System.Globalization;

namespace BeastCraft.Presentation.Diagnostics
{
    /// <summary>
    /// Frame times over a window of the most recent frames (a ring buffer): the last, the mean, the
    /// worst and a percentile (nearest rank), plus how many went over a budget. The debug frame-time
    /// overlay keeps a short one for its rolling numbers and a long one for the run's summary
    /// (docs/design/performance.md). Pure arithmetic, no clock: the caller measures.
    /// </summary>
    public sealed class FrameStats
    {
        /// <summary>The overlay's rolling window: about four seconds at 60 fps.</summary>
        public const int DefaultWindow = 240;

        /// <summary>The frame budget at 60 fps, in ms (the target).</summary>
        public const double TargetMs = 16.6;

        /// <summary>The frame budget at 30 fps, in ms (the floor).</summary>
        public const double FloorMs = 33.0;

        private readonly double[] _samples;
        private readonly double[] _sorted;
        private int _next;

        public FrameStats(int window = DefaultWindow)
        {
            if (window < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(window));
            }

            _samples = new double[window];
            _sorted = new double[window];
        }

        /// <summary>How many samples the window holds now (at most its size).</summary>
        public int Count { get; private set; }

        /// <summary>Every sample ever added, including those the window has since dropped.</summary>
        public long Total { get; private set; }

        /// <summary>The most recent sample, or 0.</summary>
        public double Last { get; private set; }

        public void Add(double ms)
        {
            if (double.IsNaN(ms) || ms < 0.0)
            {
                ms = 0.0;
            }

            _samples[_next] = ms;
            _next = (_next + 1) % _samples.Length;
            Count = Math.Min(Count + 1, _samples.Length);
            Total++;
            Last = ms;
        }

        public double Mean
        {
            get
            {
                if (Count == 0)
                {
                    return 0.0;
                }

                double sum = 0.0;
                for (int i = 0; i < Count; i++)
                {
                    sum += _samples[i];
                }

                return sum / Count;
            }
        }

        public double Max
        {
            get
            {
                double max = 0.0;
                for (int i = 0; i < Count; i++)
                {
                    max = Math.Max(max, _samples[i]);
                }

                return max;
            }
        }

        /// <summary>The <paramref name="fraction"/> (0-1) percentile of the window, nearest rank; 0 when empty.</summary>
        public double Percentile(double fraction)
        {
            if (Count == 0)
            {
                return 0.0;
            }

            Array.Copy(_samples, _sorted, Count);
            Array.Sort(_sorted, 0, Count);
            double clamped = Math.Max(0.0, Math.Min(1.0, fraction));
            int rank = (int)Math.Ceiling(clamped * Count);
            return _sorted[Math.Max(0, Math.Min(Count - 1, rank - 1))];
        }

        /// <summary>The share (0-1) of the window's samples over <paramref name="budgetMs"/>.</summary>
        public double ShareOver(double budgetMs)
        {
            if (Count == 0)
            {
                return 0.0;
            }

            int over = 0;
            for (int i = 0; i < Count; i++)
            {
                if (_samples[i] > budgetMs)
                {
                    over++;
                }
            }

            return over / (double)Count;
        }

        /// <summary>One line for a log: count, mean, p50, p95, p99, max and the shares over the target and the floor.</summary>
        public string Summary(string label)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1} frames, mean {2:0.00} ms, p50 {3:0.00}, p95 {4:0.00}, p99 {5:0.00}, max {6:0.00}, over {7} ms {8:0.0}%, over {9} ms {10:0.0}%",
                                 label, Count, Mean, Percentile(0.50), Percentile(0.95), Percentile(0.99), Max, TargetMs, ShareOver(TargetMs) * 100.0, FloorMs, ShareOver(FloorMs) * 100.0);
        }
    }
}
