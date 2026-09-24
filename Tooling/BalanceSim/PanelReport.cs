using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>The composition panel's variance decomposition of one <see cref="PanelCell"/> (percent points).</summary>
    public class PanelStats
    {
        /// <summary>The mean clear rate over every team and composition.</summary>
        public double Mean;

        /// <summary>
        /// The team main-effect SD: sqrt(Var(team means) - noise²), where a team's mean is over the
        /// panel's compositions and noise² is the damage-roll variance of that mean (each cell's
        /// unbiased binomial variance p(1 - p) / (S - 1), averaged and divided by K). How much the
        /// lineup matters whatever it faces.
        /// </summary>
        public double MainSd;

        /// <summary>
        /// The team x composition interaction SD: sqrt((MS_int - MS_err) / S) from a two-way ANOVA
        /// with S replicates per cell. How much a lineup's clear rate depends on which composition it
        /// faces beyond the team and composition means: the value of counter-picking.
        /// </summary>
        public double InteractionSd;

        /// <summary>The roll noise SD of one team's panel mean (the noise² above, square-rooted).</summary>
        public double NoiseSd;

        /// <summary>[team] the team's clear rate over the panel, percent.</summary>
        public double[] TeamRate;

        public static PanelStats Of(PanelCell cell)
        {
            int teams = cell.TeamCount;
            int k = cell.Compositions;
            int s = cell.Samples;
            double[] teamMean = new double[teams];
            double[] compMean = new double[k];
            double grand = 0.0;
            double withinVar = 0.0;

            for (int t = 0; t < teams; t++)
            {
                for (int c = 0; c < k; c++)
                {
                    double p = cell.Rate(t, c);
                    teamMean[t] += p / k;
                    compMean[c] += p / teams;
                    grand += p / (teams * (double)k);
                    withinVar += p * (1.0 - p);
                }
            }

            double varTeams = 0.0;
            for (int t = 0; t < teams; t++)
            {
                varTeams += (teamMean[t] - grand) * (teamMean[t] - grand) / teams;
            }

            double ssInt = 0.0;
            for (int t = 0; t < teams; t++)
            {
                for (int c = 0; c < k; c++)
                {
                    double residual = cell.Rate(t, c) - teamMean[t] - compMean[c] + grand;
                    ssInt += s * residual * residual;
                }
            }

            // Within a cell the S outcomes are 0/1 with mean p: their sum of squares is S p (1 - p).
            double ssErr = s * withinVar;
            double msInt = teams > 1 && k > 1 ? ssInt / ((teams - 1.0) * (k - 1.0)) : 0.0;
            double msErr = s > 1 ? ssErr / (teams * (double)k * (s - 1.0)) : 0.0;
            double noise = s > 1 ? withinVar / (teams * (double)k) / (s - 1.0) / k : 0.0;

            PanelStats stats = new PanelStats
            {
                Mean = 100.0 * grand,
                MainSd = 100.0 * Math.Sqrt(Math.Max(0.0, varTeams - noise)),
                InteractionSd = 100.0 * Math.Sqrt(Math.Max(0.0, (msInt - msErr) / s)),
                NoiseSd = 100.0 * Math.Sqrt(noise),
                TeamRate = new double[teams]
            };

            for (int t = 0; t < teams; t++)
            {
                stats.TeamRate[t] = 100.0 * teamMean[t];
            }

            return stats;
        }

        /// <summary>sqrt of the mean of squares: the pooled SD of several shapes' SDs.</summary>
        public static double Rms(IList<double> values)
        {
            double sum = 0.0;
            foreach (double v in values)
            {
                sum += v * v / values.Count;
            }

            return Math.Sqrt(sum);
        }
    }

    /// <summary>
    /// <c>--panel KxS</c>: the "PvE composition panel" section and its <c>--seeds</c> aggregate.
    /// Every team fights the same fixed panel of K compositions per shape (drawn from
    /// <see cref="SimOptions.PanelSeed"/>, never <c>--seed</c>) S times each, at the
    /// <c>--panel-level</c> cell's calibrated multiplier; the variance of the clear rates is split
    /// into a team main effect, a team x composition interaction and roll noise. It supersedes the
    /// multi-seed "persistent SD", which mixed the per-seed composition draw into the lineup's own
    /// spread. Pure and in a fixed order, like the rest of the report.
    /// </summary>
    public static class PanelReport
    {
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                         PveSimulator simulator)
        {
            if (!options.PanelActive || simulator == null || simulator.PanelCells.Count == 0)
            {
                return;
            }

            PanelCell first = simulator.PanelCells[0];
            report.AppendLine("### PvE composition panel");
            report.AppendLine();
            report.AppendLine("A fixed panel of " + options.PanelCompositions + " compositions per shape, drawn from a constant seed (" + SimOptions.PanelSeed +
                              ", never `--seed`, so every run and every seed of a");
            report.AppendLine("`--seeds` run measures the same lineups): all " + first.TeamCount + " teams fight every panel composition " + options.PanelSamples +
                              " times at level " + options.PanelLevel + " and that cell's calibrated");
            report.AppendLine("multiplier (`--panel " + options.PanelCompositions + "x" + options.PanelSamples + " --panel-level " + options.PanelLevel +
                              "`). The clear rates' variance is split three ways. **Team main-effect SD** = sqrt(Var(team means) -");
            report.AppendLine("noise²): how much the lineup matters whatever it faces (noise² = the roll variance of a team's panel mean, each cell's");
            report.AppendLine("p(1 - p) / (S - 1) averaged and divided by K). **Interaction SD** = sqrt((MS_int - MS_err) / S) from a two-way ANOVA");
            report.AppendLine("with S replicates: how much a lineup's rate depends on *which* composition it faces beyond both means, the value of");
            report.AppendLine("counter-picking. This supersedes the `--seeds` \"persistent SD\", which folded each seed's composition draw into the lineup's");
            report.AppendLine("own spread. **Pooled** is the root mean square over the shapes.");
            report.AppendLine();
            report.AppendLine("| Kit mode | Shape | Multiplier | Battles | Mean clear | Team main-effect SD | Interaction SD | Noise SD (team mean) |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (KitMode mode in options.Modes)
            {
                List<double> mains = new List<double>();
                List<double> inters = new List<double>();
                double mean = 0.0;
                int count = 0;
                foreach (PanelCell cell in CellsOf(simulator, mode))
                {
                    PanelStats stats = PanelStats.Of(cell);
                    mains.Add(stats.MainSd);
                    inters.Add(stats.InteractionSd);
                    mean += stats.Mean;
                    count++;
                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | `" + cell.Shape.Id + "` | " + SimOptions.FormatMultiplier(cell.Multiplier) + " | " +
                                      (cell.TeamCount * cell.Compositions * cell.Samples) + " | " + Pct(stats.Mean) + " | " + SimOptions.Format(stats.MainSd) + " | " +
                                      SimOptions.Format(stats.InteractionSd) + " | " + SimOptions.Format(stats.NoiseSd) + " |");
                }

                if (count > 0)
                {
                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | pooled |  |  | " + Pct(mean / count) + " | **" + SimOptions.Format(PanelStats.Rms(mains)) + "** | **" +
                                      SimOptions.Format(PanelStats.Rms(inters)) + "** |  |");
                }
            }

            report.AppendLine();
            AppendStanceMix(report, options, species, shapes, simulator);
            AppendBonds(report, options, species, shapes, simulator);
        }

        private static List<PanelCell> CellsOf(PveSimulator simulator, KitMode mode)
        {
            return simulator.PanelCells.FindAll(c => c.Mode == mode);
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }

        /// <summary>Clear rate by the team's stance mix (Vanguards, Ranged, Skirmishers), per shape, over the panel.</summary>
        private static void AppendStanceMix(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                            PveSimulator simulator)
        {
            List<int[]> teams = simulator.Teams;
            Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();
            List<int[]> keys = new List<int[]>();
            for (int t = 0; t < teams.Count; t++)
            {
                int[] mix = new int[3];
                foreach (int b in teams[t])
                {
                    mix[species[b].Stance == CombatStance.Vanguard ? 0 : species[b].Stance == CombatStance.Ranged ? 1 : 2]++;
                }

                string key = mix[0] + "/" + mix[1] + "/" + mix[2];
                if (!groups.TryGetValue(key, out List<int> members))
                {
                    members = new List<int>();
                    groups.Add(key, members);
                    keys.Add(mix);
                }

                members.Add(t);
            }

            // Most Vanguards first, then most Ranged.
            keys.Sort((a, b) => a[0] != b[0] ? b[0].CompareTo(a[0]) : b[1].CompareTo(a[1]));

            report.AppendLine("#### Clear rate by stance mix (panel)");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Kit mode | Vanguard / Ranged / Skirmisher | Teams |");
            StringBuilder rule = new StringBuilder("| --- | --- | ---: |");
            foreach (EncounterShape shape in shapes)
            {
                header.Append(" `" + shape.Id + "` |");
                rule.Append(" ---: |");
            }

            header.Append(" Mean |");
            rule.Append(" ---: |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            foreach (KitMode mode in options.Modes)
            {
                List<PanelCell> cells = CellsOf(simulator, mode);
                List<PanelStats> stats = cells.ConvertAll(PanelStats.Of);
                foreach (int[] mix in keys)
                {
                    List<int> members = groups[mix[0] + "/" + mix[1] + "/" + mix[2]];
                    StringBuilder row = new StringBuilder("| `" + SimOptions.ModeName(mode) + "` | " + mix[0] + " / " + mix[1] + " / " + mix[2] + " | " + members.Count + " |");
                    double overall = 0.0;
                    foreach (PanelStats shapeStats in stats)
                    {
                        double sum = 0.0;
                        foreach (int t in members)
                        {
                            sum += shapeStats.TeamRate[t];
                        }

                        row.Append(" " + Pct(sum / members.Count) + " |");
                        overall += sum / members.Count / stats.Count;
                    }

                    row.Append(" " + Pct(overall) + " |");
                    report.AppendLine(row.ToString());
                }
            }

            report.AppendLine();
        }

        /// <summary>
        /// Per library bond: the panel clear rate of the teams it is active for against the additive
        /// prediction from the beasts' panel marginals (<see cref="Excess"/>), per shape and pooled,
        /// and its reactions per battle.
        /// </summary>
        private static void AppendBonds(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                        PveSimulator simulator)
        {
            if (!options.BondsActive)
            {
                return;
            }

            IReadOnlyList<TeamBondSO> bonds = options.Library.TeamBonds;
            report.AppendLine("#### Team bonds on the panel");
            report.AppendLine();
            report.AppendLine("**Excess** = the mean panel clear rate of the teams the bond is active for minus what the beasts' own panel marginals");
            report.AppendLine("predict for those teams with no interaction (the additive model of \"PvE team bonds\"). A bond that only rides on");
            report.AppendLine("who its members are shows ~0; one that makes its lineup better than its parts shows its value. Part of any bond's");
            report.AppendLine("effect is absorbed into its members' marginals, so excess is a lower bound. **Reactions / battle** = its reactions fired per battle of an active team.");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Kit mode | Bond | Teams |");
            StringBuilder rule = new StringBuilder("| --- | --- | ---: |");
            foreach (EncounterShape shape in shapes)
            {
                header.Append(" `" + shape.Id + "` |");
                rule.Append(" ---: |");
            }

            header.Append(" Pooled excess | Reactions / battle |");
            rule.Append(" ---: | ---: |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            foreach (KitMode mode in options.Modes)
            {
                List<PanelCell> cells = CellsOf(simulator, mode);
                List<PanelStats> stats = cells.ConvertAll(PanelStats.Of);
                double[] pooled = new double[simulator.Teams.Count];
                foreach (PanelStats shapeStats in stats)
                {
                    for (int t = 0; t < pooled.Length; t++)
                    {
                        pooled[t] += shapeStats.TeamRate[t] / stats.Count;
                    }
                }

                for (int b = 0; b < bonds.Count; b++)
                {
                    List<int> active = ActiveTeams(simulator, bonds[b]);
                    if (active.Count == 0)
                    {
                        continue;
                    }

                    StringBuilder row = new StringBuilder("| `" + SimOptions.ModeName(mode) + "` | " + bonds[b].BondId + " | " + active.Count + " |");
                    foreach (PanelStats shapeStats in stats)
                    {
                        row.Append(" " + SimOptions.Signed(Excess(species.Count, simulator.Teams, shapeStats.TeamRate, active)) + " |");
                    }

                    long reactions = 0;
                    long battles = 0;
                    foreach (PanelCell cell in cells)
                    {
                        foreach (int t in active)
                        {
                            reactions += cell.Reactions[t][b];
                            battles += cell.Compositions * cell.Samples;
                        }
                    }

                    bool reacts = bonds[b].Tiers.Exists(tier => tier != null && tier.HasReaction);
                    row.Append(" **" + SimOptions.Signed(Excess(species.Count, simulator.Teams, pooled, active)) + "** | " +
                               (battles == 0 || !reacts ? "-" : ((double)reactions / battles).ToString("0.00", CultureInfo.InvariantCulture)) + " |");
                    report.AppendLine(row.ToString());
                }
            }

            report.AppendLine();
        }

        /// <summary>The teams (indices) a bond is active for.</summary>
        public static List<int> ActiveTeams(PveSimulator simulator, TeamBondSO bond)
        {
            List<int> active = new List<int>();
            for (int t = 0; t < simulator.Teams.Count; t++)
            {
                if (simulator.TeamBonds[t].Exists(a => a.Bond == bond))
                {
                    active.Add(t);
                }
            }

            return active;
        }

        /// <summary>
        /// The mean over <paramref name="subset"/> of each team's rate minus the additive prediction
        /// from the beasts' marginals (<see cref="BondReport.Excess"/>).
        /// </summary>
        public static double Excess(int speciesCount, List<int[]> teams, double[] rates, List<int> subset)
        {
            bool[] mask = new bool[teams.Count];
            foreach (int t in subset)
            {
                mask[t] = true;
            }

            return BondReport.Excess(rates, mask, teams, speciesCount);
        }

        /// <summary>The <c>--seeds</c> aggregate: each seed's panel SDs, their mean and range, per kit mode and shape.</summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, List<int> seeds, List<PveSimulator> simulators)
        {
            if (!options.PanelActive || simulators.Count == 0 || simulators[0] == null || simulators[0].PanelCells.Count == 0)
            {
                return;
            }

            report.AppendLine("## PvE composition panel over seeds");
            report.AppendLine();
            report.AppendLine("Each seed's \"PvE composition panel\" (the same " + options.PanelCompositions + " x " + options.PanelSamples +
                              " panel every seed; only the calibrated multiplier and the rolls differ): the team main-effect SD");
            report.AppendLine("and the interaction SD per shape, their mean over the seeds and each seed's value. Pooled = root mean square over shapes.");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Kit mode | Shape | Main-effect SD (mean) | Interaction SD (mean) |");
            StringBuilder rule = new StringBuilder("| --- | --- | ---: | ---: |");
            foreach (int seed in seeds)
            {
                header.Append(" " + seed.ToString(CultureInfo.InvariantCulture) + " main / int |");
                rule.Append(" ---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            foreach (KitMode mode in options.Modes)
            {
                List<List<PanelCell>> perSeed = simulators.ConvertAll(s => CellsOf(s, mode));
                int shapeCount = perSeed[0].Count;
                for (int e = 0; e <= shapeCount; e++)
                {
                    double main = 0.0;
                    double inter = 0.0;
                    StringBuilder values = new StringBuilder();
                    foreach (List<PanelCell> cells in perSeed)
                    {
                        double m;
                        double i;
                        if (e < shapeCount)
                        {
                            PanelStats stats = PanelStats.Of(cells[e]);
                            m = stats.MainSd;
                            i = stats.InteractionSd;
                        }
                        else
                        {
                            List<PanelStats> all = cells.ConvertAll(PanelStats.Of);
                            m = PanelStats.Rms(all.ConvertAll(x => x.MainSd));
                            i = PanelStats.Rms(all.ConvertAll(x => x.InteractionSd));
                        }

                        main += m / perSeed.Count;
                        inter += i / perSeed.Count;
                        values.Append(" " + SimOptions.Format(m) + " / " + SimOptions.Format(i) + " |");
                    }

                    string shape = e < shapeCount ? "`" + perSeed[0][e].Shape.Id + "`" : "pooled";
                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | " + shape + " | " + SimOptions.Format(main) + " | " + SimOptions.Format(inter) + " |" + values);
                }
            }

            report.AppendLine();
        }
    }
}
