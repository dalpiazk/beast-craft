using System;
using BeastCraft.Game;

namespace BeastCraft.Desktop
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            ViewerOptions options = ViewerOptions.Parse(args, out string error);
            if (options == null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine("Usage: BeastCraft.Desktop [--screenshot PATH [--turns N] [--skill ID] [--at MS] [--scale K]] [--select-skill N] [--speed S] [--safe-inset L,T,R,B] [--seed S] [--level L] [--enemy-level L] [--encounter ID] [--team A,B,...] [--content DIR] [--effects full|reduced|minimal] [--no-shake] [--no-flashes] [--show-settings] [--glossary TERM]");
                return 2;
            }

            using (BattleViewerGame game = new BattleViewerGame(options, ViewerHost.Desktop()))
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
