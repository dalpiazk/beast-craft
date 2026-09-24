using System;

namespace BeastCraft.Desktop
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            SpikeOptions options = SpikeOptions.Parse(args, out string error);
            if (options == null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine("Usage: BeastCraft.Desktop [--screenshot PATH [--turns N] [--skill ID] [--at MS] [--scale K]] [--seed S] [--level L] [--enemy-level L] [--content DIR]");
                return 2;
            }

            using (SpikeGame game = new SpikeGame(options))
            {
                game.Run();
                if (game.FailureMessage != null)
                {
                    Console.Error.WriteLine(game.FailureMessage);
                    return 1;
                }
            }

            return 0;
        }
    }
}
