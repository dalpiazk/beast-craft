using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Every team's clear rate in one scope (one PvE cell, a shape with its levels pooled, or
    /// everything), in points, with the damage-roll noise variance of that rate.
    /// </summary>
    public class TeamScope
    {
        /// <summary>[team] clear rate, percent.</summary>
        public double[] Rate;

        /// <summary>
        /// [team] binomial variance of <see cref="Rate"/> (points squared): p(1 - p) / (N - 1) per
        /// cell, propagated through the averages. An upper bound on the damage-roll noise: the
        /// team's chance differs between compositions, which only lowers the true variance.
        /// </summary>
        public double[] NoiseVar;

        /// <summary>Battles behind each team's rate.</summary>
        public int Battles;

        public static TeamScope FromCell(PveCell cell)
        {
            int teams = cell.TeamCount;
            int[] cleared = new int[teams];
            for (int i = 0; i < cell.Battles.Length; i++)
            {
                cleared[cell.TeamOf(i)] += cell.Battles[i].Cleared ? 1 : 0;
            }

            int per = cell.Shape.Compositions.Count * cell.Samples;
            TeamScope scope = new TeamScope { Rate = new double[teams], NoiseVar = new double[teams], Battles = per };
            for (int t = 0; t < teams; t++)
            {
                double p = per == 0 ? 0.0 : (double)cleared[t] / per;
                scope.Rate[t] = 100.0 * p;
                scope.NoiseVar[t] = 10000.0 * p * (1.0 - p) / Math.Max(1, per - 1);
            }

            return scope;
        }

        /// <summary>The unweighted mean of several scopes (every team has the same battles in each).</summary>
        public static TeamScope Average(List<TeamScope> parts)
        {
            int teams = parts[0].Rate.Length;
            TeamScope scope = new TeamScope { Rate = new double[teams], NoiseVar = new double[teams] };
            foreach (TeamScope part in parts)
            {
                scope.Battles += part.Battles;
                for (int t = 0; t < teams; t++)
                {
                    scope.Rate[t] += part.Rate[t] / parts.Count;
                    scope.NoiseVar[t] += part.NoiseVar[t] / ((double)parts.Count * parts.Count);
                }
            }

            return scope;
        }
    }

    /// <summary>One kit mode's team clear rates: per cell, per shape (levels pooled) and overall.</summary>
    public class TeamData
    {
        public KitMode Mode;

        /// <summary>[shape][level].</summary>
        public TeamScope[][] ByCell;

        /// <summary>[shape], levels pooled.</summary>
        public TeamScope[] ByShape;

        /// <summary>Every shape and level, weighted equally (as the marginals' "Overall").</summary>
        public TeamScope Overall;

        public static TeamData Build(SimOptions options, List<EncounterShape> shapes, List<PveCell> cells, KitMode mode)
        {
            TeamData data = new TeamData { Mode = mode, ByCell = new TeamScope[shapes.Count][], ByShape = new TeamScope[shapes.Count] };
            List<TeamScope> all = new List<TeamScope>();
            for (int e = 0; e < shapes.Count; e++)
            {
                data.ByCell[e] = new TeamScope[options.Levels.Count];
                for (int l = 0; l < options.Levels.Count; l++)
                {
                    data.ByCell[e][l] = TeamScope.FromCell(PveReport.Find(cells, mode, options.Levels[l], shapes[e]));
                }

                data.ByShape[e] = TeamScope.Average(new List<TeamScope>(data.ByCell[e]));
                all.Add(data.ByShape[e]);
            }

            data.Overall = TeamScope.Average(all);
            return data;
        }
    }

    /// <summary>The distribution of team clear rates in one scope.</summary>
    public class TeamSpread
    {
        public double Min;
        public double P10;
        public double Median;
        public double P90;
        public double Max;

        /// <summary>Standard deviation over the teams (population: every team is fielded).</summary>
        public double Sd;

        /// <summary>Root mean binomial noise variance: the SD the teams would show from damage rolls alone.</summary>
        public double NoiseSd;

        /// <summary>sqrt(max(0, Sd^2 - NoiseSd^2)): a lower bound on the spread that is not roll noise.</summary>
        public double RealSd;

        /// <summary>Teams per 10-point bucket: 0-9, 10-19, ..., 90-100.</summary>
        public int[] Histogram = new int[TeamReport.Buckets];

        public static TeamSpread Of(double[] rates, double[] noiseVar)
        {
            List<double> sorted = new List<double>(rates);
            sorted.Sort();
            TeamSpread spread = new TeamSpread
            {
                Min = sorted[0],
                P10 = TeamReport.Percentile(sorted, 0.10),
                Median = TeamReport.Percentile(sorted, 0.50),
                P90 = TeamReport.Percentile(sorted, 0.90),
                Max = sorted[sorted.Count - 1]
            };

            double variance = TeamReport.Variance(rates, false);
            double noise = 0.0;
            if (noiseVar != null)
            {
                foreach (double v in noiseVar)
                {
                    noise += v / noiseVar.Length;
                }
            }

            spread.Sd = Math.Sqrt(variance);
            spread.NoiseSd = Math.Sqrt(noise);
            spread.RealSd = Math.Sqrt(Math.Max(0.0, variance - noise));
            foreach (double rate in rates)
            {
                spread.Histogram[TeamReport.BucketOf(rate)]++;
            }

            return spread;
        }
    }

    /// <summary>One beast pair's clear rate against the additive (no-interaction) prediction.</summary>
    public class PairSynergy
    {
        public int A;
        public int B;
        public int Teams;
        public double Observed;
        public double Additive;

        /// <summary>Binomial standard error of <see cref="Observed"/> (and so of the synergy).</summary>
        public double Se;

        public double Synergy
        {
            get { return Observed - Additive; }
        }
    }

    /// <summary>
    /// Whole-team numbers for the PvE section ("PvE team composition") and the <c>--seeds</c>
    /// aggregate: how far apart the teams' clear rates are, the best and worst lineups, and which
    /// beast pairs do better or worse together than their marginals add up to. A regrouping of
    /// battles already run. Pure and in a fixed order, like the rest of the report.
    /// </summary>
    public static class TeamReport
    {
        public const int Buckets = 10;
        public const int Listed = 5;

        /// <summary>
        /// The additive model's weight on (mA + mB) for a pair: with every k-of-n team fielded, a
        /// beast's marginal is n / (n - 1) times its additive effect, and the teams holding a pair
        /// carry (n - k) / (n - 2) of the pair's effects (the other members share the rest), so the
        /// pair's expected clear rate is baseline + (n - 1)(n - k) / (n (n - 2)) x (mA + mB).
        /// </summary>
        public static double PairWeight(int n, int k)
        {
            return (double)(n - 1) * (n - k) / ((double)n * (n - 2));
        }

        public static bool HasPairs(int n, int k)
        {
            return n >= 3 && k >= 2 && k < n;
        }

        public static KitMode PrimaryMode(SimOptions options)
        {
            return options.Modes.Contains(KitMode.Elemental) ? KitMode.Elemental : options.Modes[0];
        }

        public static double Percentile(List<double> sorted, double q)
        {
            double position = q * (sorted.Count - 1);
            int low = (int)Math.Floor(position);
            int high = Math.Min(sorted.Count - 1, low + 1);
            return sorted[low] + ((position - low) * (sorted[high] - sorted[low]));
        }

        /// <summary>Population (sample = false) or sample variance.</summary>
        public static double Variance(IList<double> values, bool sample)
        {
            double mean = 0.0;
            foreach (double v in values)
            {
                mean += v / values.Count;
            }

            double squares = 0.0;
            foreach (double v in values)
            {
                squares += (v - mean) * (v - mean);
            }

            int divisor = sample ? values.Count - 1 : values.Count;
            return divisor <= 0 ? double.NaN : squares / divisor;
        }

        public static int BucketOf(double rate)
        {
            return Math.Max(0, Math.Min(Buckets - 1, (int)Math.Floor((rate + 1e-9) / 10.0)));
        }

        public static string BucketLabel(int bucket)
        {
            return (bucket * 10) + "-" + (bucket == Buckets - 1 ? 100 : (bucket * 10) + 9);
        }

        public static string TeamName(IReadOnlyList<CreatureSpeciesSO> species, int[] team)
        {
            List<string> names = new List<string>();
            foreach (int b in team)
            {
                names.Add(species[b].DisplayName);
            }

            return string.Join(" + ", names);
        }

        /// <summary>Team indices, highest rate first; ties in team order.</summary>
        public static List<int> Ranked(double[] rates)
        {
            return PveReport.Order(rates.Length, t => rates[t]);
        }

        /// <summary>Every beast pair, in roster order (A &lt; B), against the additive prediction.</summary>
        public static List<PairSynergy> Synergies(int speciesCount, List<int[]> teams, TeamScope scope)
        {
            int n = speciesCount;
            int k = teams[0].Length;
            double baseline = 0.0;
            foreach (double rate in scope.Rate)
            {
                baseline += rate / scope.Rate.Length;
            }

            double[] marginal = new double[n];
            for (int b = 0; b < n; b++)
            {
                double with = 0.0;
                int withCount = 0;
                double without = 0.0;
                int withoutCount = 0;
                for (int t = 0; t < teams.Count; t++)
                {
                    if (Array.IndexOf(teams[t], b) >= 0)
                    {
                        with += scope.Rate[t];
                        withCount++;
                    }
                    else
                    {
                        without += scope.Rate[t];
                        withoutCount++;
                    }
                }

                marginal[b] = withCount == 0 || withoutCount == 0 ? 0.0 : (with / withCount) - (without / withoutCount);
            }

            double weight = PairWeight(n, k);
            List<PairSynergy> pairs = new List<PairSynergy>();
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    PairSynergy pair = new PairSynergy { A = a, B = b, Additive = baseline + (weight * (marginal[a] + marginal[b])) };
                    double sum = 0.0;
                    double noise = 0.0;
                    for (int t = 0; t < teams.Count; t++)
                    {
                        if (Array.IndexOf(teams[t], a) >= 0 && Array.IndexOf(teams[t], b) >= 0)
                        {
                            sum += scope.Rate[t];
                            noise += scope.NoiseVar[t];
                            pair.Teams++;
                        }
                    }

                    pair.Observed = pair.Teams == 0 ? 0.0 : sum / pair.Teams;
                    pair.Se = pair.Teams == 0 ? 0.0 : Math.Sqrt(noise) / pair.Teams;
                    pairs.Add(pair);
                }
            }

            return pairs;
        }

        /// <summary>Indices into a pair list, highest value first; ties in pair order.</summary>
        public static List<int> RankedPairs(int count, Func<int, double> value)
        {
            return PveReport.Order(count, value);
        }

        public static string PairName(IReadOnlyList<CreatureSpeciesSO> species, int a, int b)
        {
            return species[a].DisplayName + " + " + species[b].DisplayName;
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }

        /// <summary>The "Does composition matter?" line for one scope.</summary>
        private static string Verdict(TeamSpread s)
        {
            return "best-to-worst team spread " + SimOptions.Format(s.Max - s.Min) + " points (" + Pct(s.Min) + " … " + Pct(s.Max) + "); p10–p90 " +
                   SimOptions.Format(s.P90 - s.P10) + " points (" + Pct(s.P10) + " … " + Pct(s.P90) + "); team SD " + SimOptions.Format(s.Sd) + " against " +
                   SimOptions.Format(s.NoiseSd) + " from damage rolls alone, so " +
                   (s.RealSd > 0.0 ? "at least " + SimOptions.Format(s.RealSd) + " points of real spread" : "no spread beyond roll noise");
        }

        /// <summary>The single-seed section, after the per-mode marginal tables.</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                         PveSimulator simulator, List<PveCell> cells)
        {
            if (simulator.Teams.Count < 2)
            {
                return;
            }

            List<TeamData> modes = new List<TeamData>();
            foreach (KitMode mode in options.Modes)
            {
                modes.Add(TeamData.Build(options, shapes, cells, mode));
            }

            KitMode primary = PrimaryMode(options);
            TeamData main = modes[options.Modes.IndexOf(primary)];
            int battles = main.ByShape[0].Battles;

            report.AppendLine("### PvE team composition: does the lineup matter?");
            report.AppendLine();
            report.AppendLine("The marginals above judge beasts one at a time; this judges whole teams. Each of the " + simulator.Teams.Count +
                              " teams' clear rate at the calibrated");
            report.AppendLine("difficulty (" + (options.CalibratesOnPick ? "the scouted pick clears about " + options.TargetSummary(shapes) + ", the average team the no-scouting rate"
                                                  : "so the average team clears about " + options.TargetSummary(shapes)) + "): per shape it is " + battles +
                              " battles (compositions x levels x samples), levels");
            report.AppendLine("pooled; overall averages the shapes. **Noise SD** is the spread the teams would show from damage rolls alone");
            report.AppendLine("(binomial, sqrt(p(1 - p) / (N - 1)) per cell, an upper bound since a team's chance differs between compositions), and");
            report.AppendLine("**beyond noise** = sqrt(SD² - noise SD²) is a lower bound on the spread that is the lineup's own. A single seed also");
            report.AppendLine("carries its own composition draw; `--seeds` separates what persists across seeds.");
            report.AppendLine();

            report.AppendLine("#### Does composition matter?");
            report.AppendLine();
            foreach (TeamData data in modes)
            {
                for (int e = 0; e < shapes.Count; e++)
                {
                    report.AppendLine("- `" + SimOptions.ModeName(data.Mode) + "` `" + shapes[e].Id + "`: " +
                                      Verdict(TeamSpread.Of(data.ByShape[e].Rate, data.ByShape[e].NoiseVar)) + ".");
                }

                report.AppendLine("- `" + SimOptions.ModeName(data.Mode) + "` overall: " + Verdict(TeamSpread.Of(data.Overall.Rate, data.Overall.NoiseVar)) + ".");
            }

            report.AppendLine();

            report.AppendLine("#### Team clear-rate spread");
            report.AppendLine();
            report.AppendLine("| Kit mode | Shape | Level | Battles / team | Min | p10 | Median | p90 | Max | Max - min | p10–p90 | SD | Noise SD | Beyond noise |");
            report.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (TeamData data in modes)
            {
                string mode = "`" + SimOptions.ModeName(data.Mode) + "`";
                for (int e = 0; e < shapes.Count; e++)
                {
                    for (int l = 0; l < options.Levels.Count; l++)
                    {
                        SpreadRow(report, mode, "`" + shapes[e].Id + "`", "L" + options.Levels[l], data.ByCell[e][l]);
                    }

                    SpreadRow(report, mode, "`" + shapes[e].Id + "`", "pooled", data.ByShape[e]);
                }

                SpreadRow(report, mode, "overall", "pooled", data.Overall);
            }

            report.AppendLine();
            report.AppendLine("A single level is only " + main.ByCell[0][0].Battles + " battles per team, so its spread is mostly roll noise; read the pooled rows.");
            report.AppendLine();

            report.AppendLine("#### Team clear-rate histogram (levels pooled)");
            report.AppendLine();
            HistogramHeader(report, "| Kit mode | Shape |", "| --- | --- |");
            foreach (TeamData data in modes)
            {
                string mode = "`" + SimOptions.ModeName(data.Mode) + "`";
                for (int e = 0; e < shapes.Count; e++)
                {
                    HistogramRow(report, "| " + mode + " | `" + shapes[e].Id + "` |", TeamSpread.Of(data.ByShape[e].Rate, null));
                }

                HistogramRow(report, "| " + mode + " | overall |", TeamSpread.Of(data.Overall.Rate, null));
            }

            report.AppendLine();
            report.AppendLine("Teams per 10-point bucket of clear rate (" + simulator.Teams.Count + " per row).");
            report.AppendLine();

            string primaryName = "`" + SimOptions.ModeName(primary) + "`";
            report.AppendLine("#### Best and worst lineups (" + primaryName + ", levels pooled)");
            report.AppendLine();
            report.AppendLine("| Shape | Rank | Best team | Clear | Worst team | Clear |");
            report.AppendLine("| --- | ---: | --- | ---: | --- | ---: |");
            for (int e = 0; e <= shapes.Count; e++)
            {
                TeamScope scope = e < shapes.Count ? main.ByShape[e] : main.Overall;
                string label = e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall";
                List<int> ranked = Ranked(scope.Rate);
                for (int r = 0; r < Listed && r < ranked.Count; r++)
                {
                    int best = ranked[r];
                    int worst = ranked[ranked.Count - 1 - r];
                    report.AppendLine("| " + label + " | " + (r + 1) + " | " + TeamName(species, simulator.Teams[best]) + " | " + Pct(scope.Rate[best]) + " | " +
                                      TeamName(species, simulator.Teams[worst]) + " | " + Pct(scope.Rate[worst]) + " |");
                }
            }

            report.AppendLine();
            report.AppendLine("Rank 1 worst is the lowest clear rate. A team's binomial SE is about " +
                              SimOptions.Format(Math.Sqrt(2500.0 / Math.Max(1, battles - 1))) + " points per shape at 50% (" + battles +
                              " battles), so neighbouring ranks are not separated.");
            report.AppendLine();

            if (!HasPairs(species.Count, options.TeamSize))
            {
                return;
            }

            int pairTeams = Synergies(species.Count, simulator.Teams, main.Overall)[0].Teams;
            report.AppendLine("#### Pair synergy (" + primaryName + ", levels pooled)");
            report.AppendLine();
            report.AppendLine("For each of the " + (species.Count * (species.Count - 1) / 2) + " beast pairs: the clear rate of the " + pairTeams +
                              " teams holding both (**observed**) against what the two");
            report.AppendLine("beasts' marginals predict with no interaction (**additive** = baseline + " +
                              PairWeight(species.Count, options.TeamSize).ToString("0.000", CultureInfo.InvariantCulture) + " x (mA + mB); the weight is");
            report.AppendLine("(n - 1)(n - k) / (n (n - 2)) for every k-of-n team fielded, since each marginal already contrasts against teams");
            report.AppendLine("holding the other beast). **Synergy** = observed - additive; SE = the observed rate's binomial standard error.");
            report.AppendLine("Top " + Listed + " each way per shape. With " + (species.Count * (species.Count - 1) / 2) +
                              " pairs, |synergy / SE| up to about 2.5 is expected from noise alone; `*` marks |synergy| > 3 SE.");
            report.AppendLine();
            report.AppendLine("| Shape | Kind | Pair | Teams | Observed | Additive | Synergy | SE |");
            report.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
            for (int e = 0; e <= shapes.Count; e++)
            {
                TeamScope scope = e < shapes.Count ? main.ByShape[e] : main.Overall;
                string label = e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall";
                List<PairSynergy> pairs = Synergies(species.Count, simulator.Teams, scope);
                List<int> ranked = RankedPairs(pairs.Count, i => pairs[i].Synergy);
                for (int r = 0; r < Listed && r < ranked.Count; r++)
                {
                    SynergyRow(report, species, label, "synergy", pairs[ranked[r]]);
                }

                for (int r = 0; r < Listed && r < ranked.Count; r++)
                {
                    SynergyRow(report, species, label, "anti-synergy", pairs[ranked[ranked.Count - 1 - r]]);
                }
            }

            report.AppendLine();
        }

        private static void SpreadRow(StringBuilder report, string mode, string shape, string level, TeamScope scope)
        {
            TeamSpread s = TeamSpread.Of(scope.Rate, scope.NoiseVar);
            report.AppendLine("| " + mode + " | " + shape + " | " + level + " | " + scope.Battles + " | " + Pct(s.Min) + " | " + Pct(s.P10) + " | " + Pct(s.Median) +
                              " | " + Pct(s.P90) + " | " + Pct(s.Max) + " | " + SimOptions.Format(s.Max - s.Min) + " | " + SimOptions.Format(s.P90 - s.P10) + " | " +
                              SimOptions.Format(s.Sd) + " | " + SimOptions.Format(s.NoiseSd) + " | " + SimOptions.Format(s.RealSd) + " |");
        }

        public static void HistogramHeader(StringBuilder report, string header, string rule)
        {
            StringBuilder h = new StringBuilder(header);
            StringBuilder r = new StringBuilder(rule);
            for (int bucket = 0; bucket < Buckets; bucket++)
            {
                h.Append(" " + BucketLabel(bucket) + " |");
                r.Append(" ---: |");
            }

            report.AppendLine(h.ToString());
            report.AppendLine(r.ToString());
        }

        public static void HistogramRow(StringBuilder report, string prefix, TeamSpread spread)
        {
            StringBuilder row = new StringBuilder(prefix);
            foreach (int count in spread.Histogram)
            {
                row.Append(" " + count + " |");
            }

            report.AppendLine(row.ToString());
        }

        private static void SynergyRow(StringBuilder report, IReadOnlyList<CreatureSpeciesSO> species, string shape, string kind, PairSynergy pair)
        {
            string mark = pair.Se > 0.0 && Math.Abs(pair.Synergy) > 3.0 * pair.Se ? " *" : string.Empty;
            report.AppendLine("| " + shape + " | " + kind + " | " + PairName(species, pair.A, pair.B) + " | " + pair.Teams + " | " + Pct(pair.Observed) + " | " +
                              Pct(pair.Additive) + " | " + SimOptions.Signed(pair.Synergy) + mark + " | " + SimOptions.Format(pair.Se) + " |");
        }

        /// <summary>
        /// The <c>--seeds</c> aggregate's team section: team clear rates averaged over seeds, the
        /// spread that persists across seeds, best and worst lineups on the means, and pair
        /// synergies with their seed-to-seed spread.
        /// </summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int> seeds,
                                           List<EncounterCatalog> catalogs, List<PveSimulator> simulators, List<List<PveCell>> cells)
        {
            List<EncounterShape> shapes = catalogs[0].Shapes;
            List<int[]> teams = simulators[0].Teams;
            if (teams.Count < 2)
            {
                return;
            }

            int n = seeds.Count;
            KitMode primary = PrimaryMode(options);
            report.AppendLine("## PvE team composition over seeds");
            report.AppendLine();
            report.AppendLine("Each team's clear rate (levels pooled, as in each seed's \"PvE team composition\" section), averaged over the seeds.");
            report.AppendLine("**Per-seed SD** = the teams' spread within one seed (root mean over seeds); **seed-to-seed SD** = how much one team's rate");
            report.AppendLine("moves between seeds (root mean over teams; damage rolls and each seed's composition draw); **persistent SD** =");
            report.AppendLine("sqrt(per-seed SD² - seed-to-seed SD²), the spread that is the lineup's own. Min … max, percentiles and the histogram are");
            report.AppendLine("of the seed means, which still carry seed-to-seed SD / sqrt(" + n + ") of noise. **Superseded** as the measure of how much");
            report.AppendLine("the lineup matters by the composition panel (`--panel`, \"PvE composition panel over seeds\"): the persistent SD still folds");
            report.AppendLine("each seed's composition draw into the lineup's spread, where the panel holds the compositions fixed.");
            report.AppendLine();

            // [mode][seed].
            List<TeamData[]> byMode = new List<TeamData[]>();
            foreach (KitMode mode in options.Modes)
            {
                TeamData[] perSeed = new TeamData[n];
                for (int s = 0; s < n; s++)
                {
                    perSeed[s] = TeamData.Build(options, catalogs[s].Shapes, cells[s], mode);
                }

                byMode.Add(perSeed);
            }

            report.AppendLine("### Does composition matter? (seed means)");
            report.AppendLine();
            for (int m = 0; m < options.Modes.Count; m++)
            {
                for (int e = 0; e <= shapes.Count; e++)
                {
                    SeedStats stats = SeedStats.Of(byMode[m], e, n);
                    TeamSpread s = TeamSpread.Of(stats.Mean, null);
                    report.AppendLine("- `" + SimOptions.ModeName(options.Modes[m]) + "` " + (e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall") +
                                      ": persistent team SD " + SimOptions.Format(stats.PersistentSd) + " points (per-seed SD " + SimOptions.Format(stats.PerSeedSd) +
                                      ", seed-to-seed " + Sd(stats.SeedToSeedSd) + "); seed means " + Pct(s.Min) + " … " + Pct(s.Max) + " (" +
                                      SimOptions.Format(s.Max - s.Min) + " points), p10–p90 " + Pct(s.P10) + " … " + Pct(s.P90) + " (" +
                                      SimOptions.Format(s.P90 - s.P10) + " points), median " + Pct(s.Median) + ".");
                }
            }

            report.AppendLine();
            report.AppendLine("### Team clear-rate spread over seeds");
            report.AppendLine();
            report.AppendLine("| Kit mode | Shape | Min | p10 | Median | p90 | Max | SD of means | Per-seed SD | Seed-to-seed SD | Binomial SD | Persistent SD |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (int m = 0; m < options.Modes.Count; m++)
            {
                for (int e = 0; e <= shapes.Count; e++)
                {
                    SeedStats stats = SeedStats.Of(byMode[m], e, n);
                    TeamSpread s = TeamSpread.Of(stats.Mean, null);
                    report.AppendLine("| `" + SimOptions.ModeName(options.Modes[m]) + "` | " + (e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall") + " | " +
                                      Pct(s.Min) + " | " + Pct(s.P10) + " | " + Pct(s.Median) + " | " + Pct(s.P90) + " | " + Pct(s.Max) + " | " +
                                      SimOptions.Format(s.Sd) + " | " + SimOptions.Format(stats.PerSeedSd) + " | " + Sd(stats.SeedToSeedSd) + " | " +
                                      SimOptions.Format(stats.BinomialSd) + " | " + SimOptions.Format(stats.PersistentSd) + " |");
                }
            }

            report.AppendLine();
            report.AppendLine("Binomial SD = the damage-roll noise one seed's rates would show (root mean of the per-seed binomial variances).");
            report.AppendLine();
            HistogramHeader(report, "| Kit mode | Shape |", "| --- | --- |");
            for (int m = 0; m < options.Modes.Count; m++)
            {
                for (int e = 0; e <= shapes.Count; e++)
                {
                    SeedStats stats = SeedStats.Of(byMode[m], e, n);
                    HistogramRow(report, "| `" + SimOptions.ModeName(options.Modes[m]) + "` | " + (e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall") + " |",
                                 TeamSpread.Of(stats.Mean, null));
                }
            }

            report.AppendLine();
            report.AppendLine("Teams per 10-point bucket of their seed-mean clear rate.");
            report.AppendLine();

            TeamData[] main = byMode[options.Modes.IndexOf(primary)];
            string primaryName = "`" + SimOptions.ModeName(primary) + "`";
            report.AppendLine("### Best and worst lineups over seeds (" + primaryName + ", seed means)");
            report.AppendLine();
            report.AppendLine("| Shape | Rank | Best team | Mean (SD) | Worst team | Mean (SD) |");
            report.AppendLine("| --- | ---: | --- | ---: | --- | ---: |");
            for (int e = 0; e <= shapes.Count; e++)
            {
                SeedStats stats = SeedStats.Of(main, e, n);
                List<int> ranked = Ranked(stats.Mean);
                string label = e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall";
                for (int r = 0; r < Listed && r < ranked.Count; r++)
                {
                    int best = ranked[r];
                    int worst = ranked[ranked.Count - 1 - r];
                    report.AppendLine("| " + label + " | " + (r + 1) + " | " + TeamName(species, teams[best]) + " | " + Pct(stats.Mean[best]) + " (" +
                                      Sd(stats.TeamSd[best]) + ") | " + TeamName(species, teams[worst]) + " | " + Pct(stats.Mean[worst]) + " (" +
                                      Sd(stats.TeamSd[worst]) + ") |");
                }
            }

            report.AppendLine();

            if (!HasPairs(species.Count, options.TeamSize))
            {
                return;
            }

            report.AppendLine("### Pair synergy over seeds (" + primaryName + ")");
            report.AppendLine();
            report.AppendLine("Each seed's pair synergy (observed - additive, as in its report), then the mean, the sample SD over seeds and each");
            report.AppendLine("seed's value. **Noise** = the larger of SD / sqrt(" + n + ") and the binomial SE of the mean; `*` marks |mean| > 2 x noise");
            report.AppendLine("(of " + (species.Count * (species.Count - 1) / 2) + " pairs, about " +
                              (0.05 * species.Count * (species.Count - 1) / 2).ToString("0", CultureInfo.InvariantCulture) +
                              " per shape pass that by chance, so trust a pair that is marked in several shapes or overall).");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Shape | Kind | Pair | Teams | Mean synergy | SD over seeds | Noise |");
            StringBuilder rule = new StringBuilder("| --- | --- | --- | ---: | ---: | ---: | ---: |");
            foreach (int seed in seeds)
            {
                header.Append(" " + seed.ToString(CultureInfo.InvariantCulture) + " |");
                rule.Append(" ---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            for (int e = 0; e <= shapes.Count; e++)
            {
                List<PairSynergy>[] perSeed = new List<PairSynergy>[n];
                for (int s = 0; s < n; s++)
                {
                    perSeed[s] = Synergies(species.Count, teams, e < shapes.Count ? main[s].ByShape[e] : main[s].Overall);
                }

                int count = perSeed[0].Count;
                double[] mean = new double[count];
                double[] sd = new double[count];
                double[] noise = new double[count];
                for (int i = 0; i < count; i++)
                {
                    double[] values = new double[n];
                    double se2 = 0.0;
                    for (int s = 0; s < n; s++)
                    {
                        values[s] = perSeed[s][i].Synergy;
                        mean[i] += values[s] / n;
                        se2 += perSeed[s][i].Se * perSeed[s][i].Se / n;
                    }

                    sd[i] = n > 1 ? Math.Sqrt(Variance(values, true)) : double.NaN;
                    double binomial = Math.Sqrt(se2 / n);
                    noise[i] = double.IsNaN(sd[i]) ? binomial : Math.Max(binomial, sd[i] / Math.Sqrt(n));
                }

                List<int> ranked = RankedPairs(count, i => mean[i]);
                string label = e < shapes.Count ? "`" + shapes[e].Id + "`" : "overall";
                for (int r = 0; r < 2 * Listed && r < ranked.Count; r++)
                {
                    bool positive = r < Listed;
                    int i = positive ? ranked[r] : ranked[ranked.Count - 1 - (r - Listed)];
                    PairSynergy pair = perSeed[0][i];
                    StringBuilder row = new StringBuilder("| " + label + " | " + (positive ? "synergy" : "anti-synergy") + " | " + PairName(species, pair.A, pair.B) +
                                                          " | " + pair.Teams + " | " + SimOptions.Signed(mean[i]) +
                                                          (Math.Abs(mean[i]) > 2.0 * noise[i] ? " *" : string.Empty) + " | " + Sd(sd[i]) + " | " +
                                                          SimOptions.Format(noise[i]) + " |");
                    for (int s = 0; s < n; s++)
                    {
                        row.Append(" " + SimOptions.Signed(perSeed[s][i].Synergy) + " |");
                    }

                    report.AppendLine(row.ToString());
                }
            }

            report.AppendLine();
        }

        private static string Sd(double value)
        {
            return double.IsNaN(value) ? "-" : SimOptions.Format(value);
        }

        /// <summary>One scope's team rates over seeds (scope index = shape, or the shape count for overall).</summary>
        private class SeedStats
        {
            public double[] Mean;
            public double[] TeamSd;
            public double PerSeedSd;
            public double SeedToSeedSd;
            public double BinomialSd;
            public double PersistentSd;

            public static SeedStats Of(TeamData[] perSeed, int scopeIndex, int n)
            {
                TeamScope[] scopes = new TeamScope[n];
                for (int s = 0; s < n; s++)
                {
                    scopes[s] = scopeIndex < perSeed[s].ByShape.Length ? perSeed[s].ByShape[scopeIndex] : perSeed[s].Overall;
                }

                int teams = scopes[0].Rate.Length;
                SeedStats stats = new SeedStats { Mean = new double[teams], TeamSd = new double[teams] };
                double perSeedVar = 0.0;
                double binomial = 0.0;
                for (int s = 0; s < n; s++)
                {
                    perSeedVar += Variance(scopes[s].Rate, false) / n;
                    foreach (double v in scopes[s].NoiseVar)
                    {
                        binomial += v / ((double)n * teams);
                    }
                }

                double seedVar = 0.0;
                for (int t = 0; t < teams; t++)
                {
                    double[] values = new double[n];
                    for (int s = 0; s < n; s++)
                    {
                        values[s] = scopes[s].Rate[t];
                        stats.Mean[t] += values[s] / n;
                    }

                    double v = n > 1 ? Variance(values, true) : double.NaN;
                    stats.TeamSd[t] = Math.Sqrt(v);
                    seedVar += v / teams;
                }

                stats.PerSeedSd = Math.Sqrt(perSeedVar);
                stats.SeedToSeedSd = Math.Sqrt(seedVar);
                stats.BinomialSd = Math.Sqrt(binomial);
                // One seed: fall back to the binomial noise.
                double noise = double.IsNaN(seedVar) ? binomial : seedVar;
                stats.PersistentSd = Math.Sqrt(Math.Max(0.0, perSeedVar - noise));
                return stats;
            }
        }
    }
}
