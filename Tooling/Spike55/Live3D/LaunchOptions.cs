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
                    default:
                        Console.Error.WriteLine("Unrecognised argument: " + args[i]);
                        break;
                }
            }
            return o;
        }
    }
}
