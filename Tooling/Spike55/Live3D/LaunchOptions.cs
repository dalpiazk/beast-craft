using System;
using System.Globalization;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Parsed command-line options -- see Program.cs's doc comment for the two headless modes.</summary>
    public sealed class LaunchOptions
    {
        public bool BenchMode;
        public int BenchBeasts = 1;
        public double BenchSeconds = 10.0;
        public string BenchOutPath = "result.json";

        public bool ScreenshotMode;
        public string ScreenshotPath;
        public int ScreenshotBeasts = 1;
        public bool ScreenshotCrestOn = true;
        public int ScreenshotTint = 0;
        public bool ScreenshotHideStats = false;

        // Fifth pass (swarm via VAT): --battle sets up a fixed-composition scene (BattleGriffins
        // GPU-skinned beasts + BattleSwarm VAT swarmlings on an 11x15 arena, "game scale" per the task
        // brief) instead of the fourth pass's single-species Tab-cycling stress test. BattleGriffins=0
        // gives "swarm alone" for the fifth-pass bench comparison the task brief asks for.
        public bool Battle;
        public int BattleGriffins = 3;
        public int BattleSwarm = 24;

        // A bench-only frame-rate cap (MonoGame's own fixed-time-step throttle, not vsync) -- the
        // research-backed 30fps battle target (docs/spikes/055-3d-mini-spike.md section 2.9) measured
        // alongside the uncapped throughput number, not instead of it.
        public int FpsCap = 0;

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--bench":
                        o.BenchMode = true;
                        o.BenchBeasts = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--seconds":
                        o.BenchSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--out":
                        o.BenchOutPath = args[++i];
                        break;
                    case "--screenshot":
                        o.ScreenshotMode = true;
                        o.ScreenshotPath = args[++i];
                        break;
                    case "--beasts":
                        o.ScreenshotBeasts = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--crest":
                        o.ScreenshotCrestOn = args[++i] != "off";
                        break;
                    case "--tint":
                        o.ScreenshotTint = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--hide-stats":
                        o.ScreenshotHideStats = true;
                        break;
                    case "--battle":
                        o.Battle = true;
                        break;
                    case "--griffins":
                        o.BattleGriffins = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--swarm":
                        o.BattleSwarm = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--fps-cap":
                        o.FpsCap = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    default:
                        Console.Error.WriteLine("Unrecognised argument: " + args[i]);
                        break;
                }
            }
            return o;
        }
    }
}
