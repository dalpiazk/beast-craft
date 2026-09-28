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
                Console.Error.WriteLine("Usage: BeastCraft.Desktop [--screen NAME [--screenshot PATH]] [--walkthrough DIR] [--save-dir DIR] [--map-seed N] [--scale K] [--safe-inset L,T,R,B] [--content DIR] [--effects full|reduced|minimal] [--no-shake] [--no-flashes]");
                Console.Error.WriteLine("   or (the battle demo): BeastCraft.Desktop [--screenshot PATH [--turns N] [--skill ID] [--at MS]] [--select-skill N] [--speed S] [--seed S] [--level L] [--enemy-level L] [--encounter ID] [--arena SIZE] [--team A,B,...] [--lineup E:EL,...] [--region ID] [--show-settings] [--glossary TERM]");
                return 2;
            }

            using (BeastCraftGame game = new BeastCraftGame(options, ViewerHost.Desktop()))
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
