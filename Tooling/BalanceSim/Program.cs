using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Entry point. Exit codes: 0 success, 1 bad arguments, 2 invalid roster or encounters, 3 a self-check failed.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // stdout carries the report; keep it byte-identical to what --out writes.
            Console.OutputEncoding = new UTF8Encoding(false);
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
            List<CreatureSpeciesSO> species = RosterLoader.Load(rosterPath, errors, out Dictionary<string, GrowthRateCurve> curves);
            if (species == null)
            {
                return Fail("Roster '" + rosterPath + "' is invalid:", errors);
            }

            if (species.Count < 2)
            {
                Console.Error.WriteLine("The roster needs at least two species.");
                return 2;
            }

            List<Encounter> encounters = new List<Encounter>();
            if (options.RunPve)
            {
                if (options.TeamSize > species.Count)
                {
                    Console.Error.WriteLine("--team-size " + options.TeamSize + " is larger than the roster (" + species.Count + " species).");
                    return 1;
                }

                string encountersPath = EncounterLoader.ResolvePath(options.EncountersPath);
                if (encountersPath == null || !File.Exists(encountersPath))
                {
                    Console.Error.WriteLine("Could not find " + EncounterLoader.RepoRelativePath +
                                            "; run from inside the repo or pass --encounters-file <path>.");
                    return 2;
                }

                encounters = EncounterLoader.Load(encountersPath, curves, options, errors);
                if (encounters == null)
                {
                    return Fail("Encounters '" + encountersPath + "' are invalid:", errors);
                }
            }

            Stopwatch clock = Stopwatch.StartNew();
            string report = RunOnce(options, species, encounters, out List<string> problems);
            TimeSpan firstRun = clock.Elapsed;
            if (problems.Count > 0)
            {
                return Fail("Self-check failed:", problems, 3);
            }

            if (options.SelfCheck)
            {
                string second = RunOnce(options, species, encounters, out List<string> _);
                if (!string.Equals(report, second, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-check failed: two runs with identical inputs produced different reports.");
                    return 3;
                }

                List<string> parity = CheckLoopParity(options, species, encounters);
                if (parity.Count > 0)
                {
                    return Fail("Self-check failed: the PvE battle loop disagrees with BattleTurnExecutor.RunBattle:", parity, 3);
                }

                Console.Error.WriteLine("Self-check passed: game counts balanced, two runs identical, PvE loop matches RunBattle.");
            }

            Console.Out.Write(report);
            Console.Error.WriteLine("Runtime: " + firstRun.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                    " s for one full run (" + Environment.ProcessorCount + " logical processors).");

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

        private static int Fail(string heading, List<string> problems, int code = 2)
        {
            Console.Error.WriteLine(heading);
            foreach (string problem in problems)
            {
                Console.Error.WriteLine("  " + problem);
            }

            return code;
        }

        private static string RunOnce(SimOptions options, List<CreatureSpeciesSO> species, List<Encounter> encounters, out List<string> problems)
        {
            problems = new List<string>();
            PveSimulator pve = null;
            List<PveCell> cells = new List<PveCell>();
            List<BattleRecord> pvpRecords = new List<BattleRecord>();

            if (options.RunPve)
            {
                pve = new PveSimulator(options, species);
                foreach (KitMode mode in options.Modes)
                {
                    foreach (Encounter encounter in encounters)
                    {
                        foreach (int level in options.Levels)
                        {
                            PveCell cell = pve.RunCell(mode, level, encounter);
                            if (cell.Battles == null || cell.Battles.Length != pve.Teams.Count)
                            {
                                problems.Add("PvE " + SimOptions.ModeName(mode) + "/" + encounter.Id + "/L" + level + " did not field every team.");
                            }

                            cells.Add(cell);
                        }
                    }
                }
            }

            if (options.RunPvp)
            {
                pvpRecords = Simulator.Run(options, species);
                problems.AddRange(PvpReport.CheckGameCounts(options, species, pvpRecords));
            }

            if (problems.Count > 0)
            {
                return null;
            }

            // LF regardless of platform, so the report is byte-identical on Windows and Linux.
            return Report.Build(options, species, encounters, pve, cells, pvpRecords).Replace("\r\n", "\n");
        }

        /// <summary>
        /// Replays the first and last team of every (mode, encounter, level) at multiplier 1 through
        /// the real <see cref="BattleTurnExecutor.RunBattle"/> and through the simulator's own loop,
        /// and demands the same outcome, round count and final HP for every unit. Run with the
        /// lift-defeated emulation off, since that emulation is the one deliberate departure.
        /// </summary>
        private static List<string> CheckLoopParity(SimOptions options, List<CreatureSpeciesSO> species, List<Encounter> encounters)
        {
            List<string> problems = new List<string>();
            if (!options.RunPve)
            {
                return problems;
            }

            PveSimulator pve = new PveSimulator(options, species);
            foreach (KitMode mode in options.Modes)
            {
                foreach (Encounter encounter in encounters)
                {
                    foreach (int level in options.Levels)
                    {
                        foreach (int team in new[] { 0, pve.Teams.Count - 1 })
                        {
                            PveBattle ours = pve.RunBattle(mode, level, encounter, 1.0, team, false, false, out List<BattleUnit> ourUnits);
                            PveBattle real = pve.RunBattle(mode, level, encounter, 1.0, team, true, false, out List<BattleUnit> realUnits);
                            bool same = ours.Outcome == real.Outcome && ours.Rounds == real.Rounds && ourUnits.Count == realUnits.Count;
                            for (int u = 0; same && u < ourUnits.Count; u++)
                            {
                                same = ourUnits[u].Id == realUnits[u].Id && ourUnits[u].CurrentHp == realUnits[u].CurrentHp &&
                                       ourUnits[u].Position.Equals(realUnits[u].Position);
                            }

                            if (!same)
                            {
                                problems.Add(SimOptions.ModeName(mode) + "/" + encounter.Id + "/L" + level + " team " + team + ": " + ours.Outcome + " in " +
                                             ours.Rounds + " vs RunBattle " + real.Outcome + " in " + real.Rounds + ".");
                            }
                        }
                    }
                }
            }

            return problems;
        }
    }
}
