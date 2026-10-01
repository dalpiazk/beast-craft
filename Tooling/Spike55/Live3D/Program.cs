using System;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>
    /// Entry point. Recognised arguments (all optional; with none, opens the interactive window):
    ///   --bench N --seconds S --out result.json   Headless: spawns N beasts, runs S seconds with a
    ///                                              fixed-step clock (no window presentation wait),
    ///                                              writes stats to result.json, then exits.
    ///   --screenshot path.png [--beasts N]         Opens the window, waits a few frames for the scene
    ///                                              to settle, writes one PNG, then exits.
    /// See Tooling/Spike55/README.md for the full CLI and what each stat means.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var options = LaunchOptions.Parse(args);
            using (var game = new Game1(options))
            {
                game.Run();
            }
        }
    }
}
