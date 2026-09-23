using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Entry point. Exit codes: 0 success, 1 bad arguments, 2 invalid roster, 3 a self-check failed.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            SimOptions options = SimOptions.Parse(args, out string error);
            if (options == null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
                Console.Error.Write(SimOptions.Usage);
                return 1;
            }

            if (options.ShowHelp)
            {
                Console.Out.Write(SimOptions.Usage);
                return 0;
            }

            string rosterPath = RosterLoader.ResolvePath(options.RosterPath);
            if (rosterPath == null || !File.Exists(rosterPath))
            {
                Console.Error.WriteLine("Could not find " + RosterLoader.RepoRelativePath +
                                        "; run from inside the repo or pass --roster <path>.");
                return 2;
            }

            List<string> errors = new List<string>();
            List<CreatureSpeciesSO> species = RosterLoader.Load(rosterPath, errors);
            if (species == null)
            {
                Console.Error.WriteLine("Roster '" + rosterPath + "' is invalid:");
                foreach (string problem in errors)
                {
                    Console.Error.WriteLine("  " + problem);
                }

                return 2;
            }

            if (species.Count < 2)
            {
                Console.Error.WriteLine("The roster needs at least two species to run a round-robin.");
                return 2;
            }

            string report = RunOnce(options, species, out List<string> countProblems);
            if (countProblems.Count > 0)
            {
                Console.Error.WriteLine("Self-check failed: the round-robin is unbalanced.");
                foreach (string problem in countProblems)
                {
                    Console.Error.WriteLine("  " + problem);
                }

                return 3;
            }

            if (options.SelfCheck)
            {
                string second = RunOnce(options, species, out List<string> _);
                if (!string.Equals(report, second, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-check failed: two runs with identical inputs produced different reports.");
                    return 3;
                }

                Console.Error.WriteLine("Self-check passed: every beast played the same number of games and two runs were identical.");
            }

            Console.Out.Write(report);

            if (!string.IsNullOrEmpty(options.OutPath))
            {
                string outPath = Path.GetFullPath(options.OutPath);
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outPath, report, new UTF8Encoding(false));
                Console.Error.WriteLine("Report written to " + outPath);
            }

            return 0;
        }

        private static string RunOnce(SimOptions options, List<CreatureSpeciesSO> species, out List<string> countProblems)
        {
            List<BattleRecord> records = Simulator.Run(options, species);
            countProblems = ReportBuilder.CheckGameCounts(options, species, records);

            // LF regardless of platform, so the report is byte-identical on Windows and Linux.
            return ReportBuilder.Build(options, species, records).Replace("\r\n", "\n");
        }
    }
}
