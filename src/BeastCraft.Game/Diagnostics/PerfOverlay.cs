#if DEBUG || PERF_OVERLAY
using System;
using System.Diagnostics;
using System.Globalization;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Diagnostics
{
    /// <summary>
    /// The frame-time overlay (#64; docs/design/performance.md). Compiled into Debug builds, and into a
    /// Release build only with <c>-p:PerfOverlay=true</c> (to measure optimised code); never into a
    /// shipping build. Off until <c>--perf-overlay</c> or F3. Each frame it records the frame interval
    /// (what the player sees, vsync included) and the CPU time from the frame's first update to the end
    /// of its draw (the headroom), with a rolling p95, the GC counts and the bytes allocated per frame,
    /// the device's draw calls, sprites and texture binds (<see cref="GraphicsDevice.Metrics"/>), and the
    /// art textures' memory. A second, long window feeds the summary printed at exit.
    /// </summary>
    internal sealed class PerfOverlay
    {
        /// <summary>The run summary's window: ten minutes at 60 fps.</summary>
        private const int RunWindow = 36000;

        private readonly FrameStats _frame = new FrameStats();
        private readonly FrameStats _cpu = new FrameStats();
        private readonly FrameStats _runFrame = new FrameStats(RunWindow);
        private readonly FrameStats _runCpu = new FrameStats(RunWindow);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly int _gen0AtStart = GC.CollectionCount(0);
        private readonly int _gen2AtStart = GC.CollectionCount(2);
        private double _cpuStart = -1.0;
        private double _lastDrawEnd = -1.0;
        private long _allocatedAtFrame = GC.GetTotalAllocatedBytes(false);
        private long _allocatedTotal;
        private long _allocatedLast;
        private GraphicsMetrics _metrics;
        private long _peakDraws;
        private long _peakSprites;

        /// <summary>Whether the overlay draws (the numbers are gathered either way once it exists).</summary>
        public bool Visible;

        /// <summary>Art texture bytes, updated by the host now and then (the atlas's own sum).</summary>
        public long TextureBytes;

        private double NowMs
        {
            get { return _clock.Elapsed.TotalMilliseconds; }
        }

        /// <summary>Call at the top of every update: the first one after a draw starts the frame's CPU time.</summary>
        public void OnUpdate()
        {
            if (_cpuStart < 0.0)
            {
                _cpuStart = NowMs;
            }
        }

        /// <summary>Call at the end of every draw, before the frame is presented.</summary>
        public void OnDrawEnd(GraphicsMetrics metrics)
        {
            double now = NowMs;
            if (_cpuStart >= 0.0)
            {
                _cpu.Add(now - _cpuStart);
                _runCpu.Add(now - _cpuStart);
            }

            if (_lastDrawEnd >= 0.0)
            {
                _frame.Add(now - _lastDrawEnd);
                _runFrame.Add(now - _lastDrawEnd);
            }

            long allocated = GC.GetTotalAllocatedBytes(false);
            _allocatedLast = allocated - _allocatedAtFrame;
            _allocatedTotal += _allocatedLast;
            _allocatedAtFrame = allocated;
            _lastDrawEnd = now;
            _cpuStart = -1.0;
            _metrics = metrics;
            _peakDraws = Math.Max(_peakDraws, metrics.DrawCount);
            _peakSprites = Math.Max(_peakSprites, metrics.SpriteCount);
        }

        /// <summary>Draws the numbers in the canvas's top-left corner.</summary>
        public void Draw(SpriteRenderer renderer, ITextRenderer text, Texture2D pixel)
        {
            if (!Visible)
            {
                return;
            }

            string[] lines =
            {
                string.Format(CultureInfo.InvariantCulture, "frame {0:0.0} ms  p95 {1:0.0}  max {2:0.0}", _frame.Last, _frame.Percentile(0.95), _frame.Max),
                string.Format(CultureInfo.InvariantCulture, "cpu {0:0.0} ms  p95 {1:0.0}", _cpu.Last, _cpu.Percentile(0.95)),
                string.Format(CultureInfo.InvariantCulture, "gc0 {0}  gc2 {1}  alloc {2:0.0} KB/frame", GC.CollectionCount(0) - _gen0AtStart, GC.CollectionCount(2) - _gen2AtStart, _allocatedLast / 1024.0),
                string.Format(CultureInfo.InvariantCulture, "draws {0}  sprites {1}  tex binds {2}", _metrics.DrawCount, _metrics.SpriteCount, _metrics.TextureCount),
                string.Format(CultureInfo.InvariantCulture, "art textures {0:0.0} MB", TextureBytes / (1024.0 * 1024.0))
            };
            const float size = 22f;
            float line = text.LineHeight(size);
            renderer.Fill(pixel, new Rectangle(8, 8, 620, (int)(line * lines.Length + 24f)), new Microsoft.Xna.Framework.Color(0, 0, 0, 180));
            for (int i = 0; i < lines.Length; i++)
            {
                text.Draw(renderer, lines[i], new Vector2(20f, 20f + i * line), size, Microsoft.Xna.Framework.Color.White);
            }
        }

        /// <summary>The run so far, for the console at exit.</summary>
        public string Summary()
        {
            long frames = Math.Max(1, _runCpu.Total);
            return _runFrame.Summary("frame interval") + Environment.NewLine + _runCpu.Summary("cpu (update + draw)") + Environment.NewLine +
                   string.Format(CultureInfo.InvariantCulture, "gc0 {0}, gc2 {1}, allocated {2:0.0} KB/frame mean; peak {3} draws and {4} sprites a frame; art textures {5:0.0} MB",
                                 GC.CollectionCount(0) - _gen0AtStart, GC.CollectionCount(2) - _gen2AtStart, _allocatedTotal / 1024.0 / frames, _peakDraws, _peakSprites, TextureBytes / (1024.0 * 1024.0));
        }
    }
}
#endif
