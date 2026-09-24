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
    /// Entry point. Exit codes: 0 success, 1 bad arguments, 2 invalid roster, skill library or encounters, 3 a self-check failed.
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

            if (options.RunPacing)
            {
                return PacingSimulator.Run(options);
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

            if (options.NeedsLibrary)
            {
                string libraryPath = SkillLibraryKits.ResolvePath(options.SkillLibraryPath);
                if (libraryPath == null || !File.Exists(libraryPath))
                {
                    Console.Error.WriteLine("Could not find " + SkillLibraryKits.RepoRelativePath +
                                            "; run from inside the repo or pass --skill-library <path>.");
                    return 2;
                }

                options.Library = SkillLibraryKits.Load(libraryPath, rosterPath, options.SkillLevel, errors);
                if (options.Library == null)
                {
                    return Fail("Skill library '" + libraryPath + "' is invalid:", errors);
                }
            }

            if (options.RunPve && options.TeamSize > species.Count)
            {
                Console.Error.WriteLine("--team-size " + options.TeamSize + " is larger than the roster (" + species.Count + " species).");
                return 1;
            }

            if (options.Seeds != null)
            {
                return RunSeeds(options, species, curves);
            }

            int code = RunSeed(options, species, curves, out SeedRun run);
            if (code != 0)
            {
                return code;
            }

            Console.Out.Write(run.Report);
            Console.Error.WriteLine("Runtime: " + run.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                                    " s for one full run (" + Environment.ProcessorCount + " logical processors).");

            if (!string.IsNullOrEmpty(options.OutPath))
            {
                WriteReport(options.OutPath, run.Report);
            }

            return 0;
        }

        /// <summary>One seed's finished run: its report and the PvE data behind it.</summary>
        private class SeedRun
        {
            public int Seed;
            public string Report;
            public double Seconds;
            public EncounterCatalog Encounters;
            public PveSimulator Pve;
            public List<PveCell> Cells;
        }

        /// <summary>
        /// Loads the encounters for <paramref name="options"/>' seed (the generated compositions
        /// depend on it), runs everything once, and with <c>--self-check</c> runs it again and
        /// replays sample battles through <see cref="BattleTurnExecutor.RunBattle"/>. Returns an exit
        /// code (0 on success) and prints any failure itself.
        /// </summary>
        private static int RunSeed(SimOptions options, List<CreatureSpeciesSO> species, Dictionary<string, GrowthRateCurve> curves, out SeedRun run)
        {
            run = null;
            EncounterCatalog encounters = new EncounterCatalog();
            if (options.RunPve)
            {
                string encountersPath = EncounterLoader.ResolvePath(options.EncountersPath);
                if (encountersPath == null || !File.Exists(encountersPath))
                {
                    Console.Error.WriteLine("Could not find " + EncounterLoader.RepoRelativePath +
                                            "; run from inside the repo or pass --encounters-file <path>.");
                    return 2;
                }

                List<string> errors = new List<string>();
                encounters = EncounterLoader.Load(encountersPath, curves, options, errors);
                if (encounters == null)
                {
                    return Fail("Encounters '" + encountersPath + "' are invalid:", errors);
                }
            }

            Stopwatch clock = Stopwatch.StartNew();
            string report = RunOnce(options, species, encounters, out List<string> problems, out PveSimulator pve, out List<PveCell> cells);
            TimeSpan firstRun = clock.Elapsed;
            if (problems.Count > 0)
            {
                return Fail("Self-check failed:", problems, 3);
            }

            if (options.SelfCheck)
            {
                string second = RunOnce(options, species, encounters, out List<string> _, out PveSimulator _, out List<PveCell> _);
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

            run = new SeedRun
            {
                Seed = options.Seed,
                Report = report,
                Seconds = firstRun.TotalSeconds,
                Encounters = encounters,
                Pve = pve,
                Cells = cells
            };
            return 0;
        }

        /// <summary>
        /// <c>--seeds</c>: every seed in one process (the roster, library and compiled code are
        /// shared), each run exactly as <c>--seed</c> would run it. stdout (and <c>--out</c>) get
        /// the multi-seed aggregate; with <c>--out</c>, each seed's full report is also written
        /// beside it as <c>&lt;name&gt;.seed&lt;n&gt;&lt;ext&gt;</c>, byte-identical to a
        /// <c>--seed n --out</c> run.
        /// </summary>
        private static int RunSeeds(SimOptions options, List<CreatureSpeciesSO> species, Dictionary<string, GrowthRateCurve> curves)
        {
            Stopwatch clock = Stopwatch.StartNew();
            List<SeedRun> runs = new List<SeedRun>();
            foreach (int seed in options.Seeds)
            {
                int code = RunSeed(options.ForSeed(seed), species, curves, out SeedRun run);
                if (code != 0)
                {
                    Console.Error.WriteLine("Seed " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + " failed.");
                    return code;
                }

                Console.Error.WriteLine("Seed " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " +
                                        run.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s.");
                if (!string.IsNullOrEmpty(options.OutPath))
                {
                    WriteReport(SeedReportPath(options.OutPath, seed), run.Report);
                }

                runs.Add(run);
            }

            string aggregate = SeedAggregate.Build(options, species, runs.ConvertAll(r => r.Seed), runs.ConvertAll(r => r.Encounters),
                                                   runs.ConvertAll(r => r.Pve), runs.ConvertAll(r => r.Cells)).Replace("\r\n", "\n");
            Console.Out.Write(aggregate);
            Console.Error.WriteLine("Runtime: " + clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s for " +
                                    runs.Count + " seeds (" + Environment.ProcessorCount + " logical processors).");
            if (!string.IsNullOrEmpty(options.OutPath))
            {
                WriteReport(options.OutPath, aggregate);
            }

            return 0;
        }

        /// <summary><c>out/report.md</c> and seed 777 give <c>out/report.seed777.md</c>.</summary>
        public static string SeedReportPath(string outPath, int seed)
        {
            string full = Path.GetFullPath(outPath);
            string name = Path.GetFileNameWithoutExtension(full) + ".seed" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                          Path.GetExtension(full);
            return Path.Combine(Path.GetDirectoryName(full) ?? string.Empty, name);
        }

        private static void WriteReport(string path, string report)
        {
            string outPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outPath, report, new UTF8Encoding(false));
            Console.Error.WriteLine("Report written to " + outPath);
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

        private static string RunOnce(SimOptions options, List<CreatureSpeciesSO> species, EncounterCatalog encounters, out List<string> problems,
                                      out PveSimulator pveOut, out List<PveCell> cellsOut)
        {
            problems = new List<string>();
            Stopwatch clock = Stopwatch.StartNew();
            PveSimulator pve = null;
            List<PveCell> cells = new List<PveCell>();
            List<BattleRecord> pvpRecords = new List<BattleRecord>();

            if (options.RunPve)
            {
                pve = new PveSimulator(options, species);
                foreach (KitMode mode in options.Modes)
                {
                    foreach (EncounterShape shape in encounters.Shapes)
                    {
                        foreach (int level in options.Levels)
                        {
                            PveCell cell = pve.RunCell(mode, level, shape);
                            if (options.Timings)
                            {
                                PrintCellTimings(cell);
                            }

                            if (cell.Battles == null || cell.Battles.Length != shape.Compositions.Count * pve.Teams.Count * pve.Samples)
                            {
                                problems.Add("PvE " + SimOptions.ModeName(mode) + "/" + shape.Id + "/L" + level + " did not field every team against every composition.");
                            }

                            cells.Add(cell);
                        }
                    }
                }
            }

            if (problems.Count == 0 && ScoutedPicker.Active(options))
            {
                problems.AddRange(ScoutedPicker.Check(options, species, pve, cells));
            }

            if (problems.Count == 0 && options.RunPve)
            {
                // A calibration miss is flagged in the report; only --self-check treats it as a failure.
                problems.AddRange(ScoutedPicker.CheckCalibration(options, species, pve, cells, options.SelfCheck));
            }

            TimeSpan pveTime = clock.Elapsed;
            if (options.RunPvp)
            {
                pvpRecords = Simulator.Run(options, species);
                problems.AddRange(PvpReport.CheckGameCounts(options, species, pvpRecords));
            }

            TimeSpan pvpTime = clock.Elapsed - pveTime;
            pveOut = pve;
            cellsOut = cells;
            if (problems.Count > 0)
            {
                return null;
            }

            // LF regardless of platform, so the report is byte-identical on Windows and Linux.
            string report = Report.Build(options, species, encounters, pve, cells, pvpRecords).Replace("\r\n", "\n");
            if (options.Timings)
            {
                TimeSpan reportTime = clock.Elapsed - pveTime - pvpTime;
                Console.Error.WriteLine("[timings] PvE " + Seconds(pveTime) + " s, PvP " + Seconds(pvpTime) + " s (" + pvpRecords.Count + " games), report " +
                                        Seconds(reportTime) + " s; GC gen0/1/2 " + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" +
                                        GC.CollectionCount(2) + ", " + (GC.GetTotalAllocatedBytes() / (1024 * 1024)) + " MB allocated so far.");
            }

            return report;
        }

        /// <summary><c>--timings</c>: one stderr line per PvE cell (evaluations, the time each took, battles per second).</summary>
        private static void PrintCellTimings(PveCell cell)
        {
            double total = 0.0;
            long battles = 0;
            StringBuilder steps = new StringBuilder();
            foreach (CalibrationPoint point in cell.Evaluations)
            {
                total += point.Seconds;
                battles += point.Reused ? 0 : point.Battles;
                steps.Append(steps.Length == 0 ? string.Empty : ", ").Append(SimOptions.FormatMultiplier(point.Multiplier)).Append(" -> ").Append(SimOptions.Format(point.ClearRate))
                     .Append("% ").Append(point.Reused ? "reused" : Seconds(TimeSpan.FromSeconds(point.Seconds)) + " s").Append(point.Final ? " (all teams)" : string.Empty);
            }

            double rate = total > 0.0 ? battles / total : 0.0;
            Console.Error.WriteLine("[timings] " + SimOptions.ModeName(cell.Mode) + "/" + cell.Shape.Id + "/L" + cell.Level + ": " + cell.Evaluations.Count +
                                    " evaluations, " + battles + " battles run, " + Seconds(TimeSpan.FromSeconds(total)) + " s, " +
                                    rate.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " battles/s (" + steps + ").");
        }

        private static string Seconds(TimeSpan span)
        {
            return span.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Replays the first and last team against every composition of every shape, at every mode and level, at multiplier 1 through
        /// the real <see cref="BattleTurnExecutor.RunBattle"/> and through the simulator's own loop,
        /// and demands the same outcome, battle time, turn count, final HP and position for every unit. The
        /// simulator's loop adds no rules of its own and both are seeded alike (sample 0), so with
        /// damage variance and crits drawn from that seed the two must still agree exactly.
        /// </summary>
        private static List<string> CheckLoopParity(SimOptions options, List<CreatureSpeciesSO> species, EncounterCatalog encounters)
        {
            List<string> problems = new List<string>();
            if (!options.RunPve)
            {
                return problems;
            }

            PveSimulator pve = new PveSimulator(options, species);
            List<KitMode> modes = new List<KitMode>();
            List<Encounter> fights = new List<Encounter>();
            List<int> levels = new List<int>();
            List<int> teams = new List<int>();
            foreach (KitMode mode in options.Modes)
            {
                foreach (Encounter encounter in encounters.AllCompositions)
                {
                    foreach (int level in options.Levels)
                    {
                        foreach (int team in new[] { 0, pve.Teams.Count - 1 })
                        {
                            modes.Add(mode);
                            fights.Add(encounter);
                            levels.Add(level);
                            teams.Add(team);
                        }
                    }
                }
            }

            // Every replay is independent; they run in parallel and report in the order above.
            string[] found = new string[modes.Count];
            System.Threading.Tasks.Parallel.For(0, modes.Count, i =>
            {
                KitMode mode = modes[i];
                Encounter encounter = fights[i];
                int level = levels[i];
                int team = teams[i];
                bool[] ties = pve.PlayersWinTies(mode, level, encounter.Id);
                PveBattle ours = pve.RunBattle(mode, level, encounter, 1.0, team, 0, false, ties[team], out List<BattleUnit> ourUnits);
                PveBattle real = pve.RunBattle(mode, level, encounter, 1.0, team, 0, true, ties[team], out List<BattleUnit> realUnits);
                bool same = ours.Outcome == real.Outcome && ours.ElapsedTicks == real.ElapsedTicks && ours.Actions == real.Actions &&
                            ourUnits.Count == realUnits.Count;
                for (int u = 0; same && u < ourUnits.Count; u++)
                {
                    same = ourUnits[u].Id == realUnits[u].Id && ourUnits[u].CurrentHp == realUnits[u].CurrentHp &&
                           ourUnits[u].Position.Equals(realUnits[u].Position);
                }

                if (!same)
                {
                    found[i] = SimOptions.ModeName(mode) + "/" + encounter.Id + "/L" + level + " team " + team + ": " + ours.Outcome + " in " +
                               ours.ElapsedTicks + " ticks / " + ours.Actions + " turns vs RunBattle " + real.Outcome + " in " +
                               real.ElapsedTicks + " ticks / " + real.Actions + " turns.";
                }
            });

            foreach (string problem in found)
            {
                if (problem != null)
                {
                    problems.Add(problem);
                }
            }

            return problems;
        }
    }
}
