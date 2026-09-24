using System;
using System.Collections.Generic;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The "PvE scouted picking" section (and its <c>--seeds</c> counterpart): how much seeing an
    /// encounter and counter-picking a team for it raises the clear rate over the average team,
    /// and which beasts the pickers field. Post-processing of the battles already run
    /// (<see cref="ScoutedPicker"/>); nothing at all with <c>--scouted none</c>.
    /// </summary>
    public static class ScoutingReport
    {
        /// <summary>The single-seed section, after "PvE team bonds".</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                         PveSimulator simulator, List<PveCell> cells)
        {
            if (!ScoutedPicker.Active(options))
            {
                return;
            }

            List<ScoutedCell> scouted = ScoutedPicker.Compute(options, species, simulator, cells);
            report.AppendLine("### PvE scouted picking");
            report.AppendLine();
            AppendIntro(report, options, simulator, shapes);
            foreach (KitMode mode in options.Modes)
            {
                report.AppendLine("#### Clear rate with scouting (`" + SimOptions.ModeName(mode) + "`, levels averaged)");
                report.AppendLine();
                AppendRateHeader(report, options, false);
                for (int e = 0; e <= shapes.Count; e++)
                {
                    List<ScoutedCell> part = Select(scouted, mode, e < shapes.Count ? shapes[e] : null);
                    StringBuilder line = new StringBuilder("| " + (e < shapes.Count ? "`" + shapes[e].Id + "`" : "Overall") + " |");
                    double baseline = Mean(part, shapes, c => c.Baseline);
                    line.Append(" " + Pct(baseline) + " |");
                    for (int k = 0; k < ScoutedPicker.StrategyCount; k++)
                    {
                        if (!ScoutedPicker.Runs(options, k))
                        {
                            continue;
                        }

                        if (k == ScoutedPicker.OracleIndex)
                        {
                            line.Append(" " + RateCell(Mean(part, shapes, c => c.BestFixed), baseline) + " |");
                        }

                        int index = k;
                        line.Append(" " + RateCell(Mean(part, shapes, c => c.Rate[index]), baseline) + " |");
                    }

                    report.AppendLine(line.ToString());
                }

                report.AppendLine();
                if (mode == KitMode.Neutral)
                {
                    report.AppendLine("In `neutral` mode every skill is `None`, so the chart the heuristic reads does not apply: its uplift here is what its");
                    report.AppendLine("picks are worth as lineups alone, the control for the `elemental` figure.");
                    report.AppendLine();
                }

                AppendPickRates(report, options, species, shapes, simulator, scouted, mode);
            }
        }

        /// <summary>The <c>--seeds</c> aggregate's scouting section: each seed's figures, averaged, with the spread of the uplift.</summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int> seeds,
                                           List<EncounterCatalog> catalogs, List<PveSimulator> simulators, List<List<PveCell>> cells)
        {
            if (!ScoutedPicker.Active(options))
            {
                return;
            }

            int n = seeds.Count;
            List<EncounterShape> shapes = catalogs[0].Shapes;
            List<ScoutedCell>[] perSeed = new List<ScoutedCell>[n];
            for (int s = 0; s < n; s++)
            {
                perSeed[s] = ScoutedPicker.Compute(options.ForSeed(seeds[s]), species, simulators[s], cells[s]);
            }

            report.AppendLine("## PvE scouted picking over seeds");
            report.AppendLine();
            report.AppendLine("Each seed's \"PvE scouted picking\" figures, averaged over the seeds: mean clear rate (mean uplift over the seed's");
            report.AppendLine("baseline, the average team). The heuristic column also gives the sample SD of its uplift over seeds. Pick rates are");
            report.AppendLine("the mean share of picks fielding the beast; **0** / **100** = never / always, in every seed.");
            report.AppendLine();
            foreach (KitMode mode in options.Modes)
            {
                report.AppendLine("### Clear rate with scouting over seeds (`" + SimOptions.ModeName(mode) + "`)");
                report.AppendLine();
                AppendRateHeader(report, options, true);
                for (int e = 0; e <= shapes.Count; e++)
                {
                    double baseline = 0.0;
                    double[] rates = new double[ScoutedPicker.StrategyCount];
                    double bestFixed = 0.0;
                    double[] heuristicUplift = new double[n];
                    for (int s = 0; s < n; s++)
                    {
                        List<EncounterShape> seedShapes = catalogs[s].Shapes;
                        List<ScoutedCell> part = Select(perSeed[s], mode, e < shapes.Count ? seedShapes[e] : null);
                        double seedBaseline = Mean(part, seedShapes, c => c.Baseline);
                        baseline += seedBaseline / n;
                        bestFixed += Mean(part, seedShapes, c => c.BestFixed) / n;
                        for (int k = 0; k < ScoutedPicker.StrategyCount; k++)
                        {
                            int index = k;
                            double rate = Mean(part, seedShapes, c => c.Rate[index]);
                            rates[k] += rate / n;
                            if (k == ScoutedPicker.HeuristicIndex)
                            {
                                heuristicUplift[s] = rate - seedBaseline;
                            }
                        }
                    }

                    StringBuilder line = new StringBuilder("| " + (e < shapes.Count ? "`" + shapes[e].Id + "`" : "Overall") + " | " + Pct(baseline) + " |");
                    for (int k = 0; k < ScoutedPicker.StrategyCount; k++)
                    {
                        if (!ScoutedPicker.Runs(options, k))
                        {
                            continue;
                        }

                        if (k == ScoutedPicker.OracleIndex)
                        {
                            line.Append(" " + RateCell(bestFixed, baseline) + " |");
                        }

                        line.Append(" " + RateCell(rates[k], baseline));
                        if (k == ScoutedPicker.HeuristicIndex)
                        {
                            double sd = n > 1 ? Math.Sqrt(TeamReport.Variance(heuristicUplift, true)) : double.NaN;
                            line.Append(" | " + (double.IsNaN(sd) ? "-" : SimOptions.Format(sd)));
                        }

                        line.Append(" |");
                    }

                    report.AppendLine(line.ToString());
                }

                report.AppendLine();
                AppendAggregatePickRates(report, options, species, shapes, catalogs, simulators, perSeed, mode);
            }
        }

        private static void AppendIntro(StringBuilder report, SimOptions options, PveSimulator simulator, List<EncounterShape> shapes)
        {
            report.AppendLine("What seeing the encounter is worth. Before a battle the player sees an `EncounterPreview` (enemy groups with their");
            report.AppendLine("elements, stances and counts; `--scouted-detail " + SimOptions.DetailName(options.ScoutedDetail) + "` here) and picks a team for it. Each strategy");
            report.AppendLine("below fields one of the " + simulator.Teams.Count + " simulated teams per composition, and that team's recorded result against the");
            report.AppendLine("composition is the outcome: a regrouping of battles already run, at the same calibrated difficulty (still aimed at");
            report.AppendLine("the average team). **Baseline** = the mean over every team (the unscouted player); the other columns give the");
            report.AppendLine("clear rate and, in brackets, the uplift over the baseline in points.");
            report.AppendLine();
            if (ScoutedPicker.Runs(options, ScoutedPicker.RandomIndex))
            {
                report.AppendLine("- **Random**: a seeded random team per composition; the no-information control (its gap to the baseline is noise).");
            }

            if (ScoutedPicker.Runs(options, ScoutedPicker.HeuristicIndex))
            {
                report.AppendLine("- **Heuristic**: element counter-pick. Each beast scores, per enemy, " + Number(ScoutedPicker.OffenceWeight) + " x its chart multiplier into");
                report.AppendLine("  the enemy's element - " + Number(ScoutedPicker.DefenceWeight) + " x the enemy's multiplier into it; the best " + options.TeamSize +
                                  " are fielded (ties to roster order), with at");
                report.AppendLine("  least " + options.ScoutedVanguardMin + " Vanguard (`--scouted-vanguard-min`) swapped in for the lowest-scored pick. Stats, kits and bonds are ignored.");
            }

            if (ScoutedPicker.Runs(options, ScoutedPicker.BondAwareIndex))
            {
                report.AppendLine("- **Heuristic + bonds**: the team (with the same Vanguard minimum) maximising its members' heuristic scores plus " +
                                  Number(ScoutedPicker.BondWeight) + " per");
                report.AppendLine("  tier of each bond it activates.");
            }

            if (ScoutedPicker.Runs(options, ScoutedPicker.OracleIndex))
            {
                report.AppendLine(options.Levels.Count > 1
                    ? "- **Best team**: the one lineup with the best clear rate in the shape at the other levels, scored at this level: knowing"
                    : "- **Best team**: the one lineup with the best clear rate in the cell (a single level, so in-sample and inflated): knowing");
                report.AppendLine("  which team is strong, not what it faces. **Oracle**: per composition, the team that did best against it in hindsight");
                report.AppendLine("  (ties to the better cell rate). An upper bound, inflated by damage-roll luck when each team fights each composition " +
                                  options.PveSamples + " time" + (options.PveSamples == 1 ? string.Empty : "s") + ".");
            }

            int battles = (shapes.Count == 0 ? 0 : shapes[0].Compositions.Count) * options.Levels.Count * options.PveSamples;
            report.AppendLine();
            report.AppendLine("Noise: a strategy's shape figure rests on one pick per composition and level, " + battles + " battle" + (battles == 1 ? string.Empty : "s") +
                              " in all, so it carries a");
            report.AppendLine("binomial standard error of about " + SimOptions.Format(StandardError(battles)) + " points (" +
                              SimOptions.Format(StandardError(battles * shapes.Count)) + " overall); the Random column shows the scale. Read one seed's");
            report.AppendLine("figures as indicative and the `--seeds` aggregate as the finding.");
            report.AppendLine();
        }

        /// <summary>The standard error, in points, of a clear rate near 50% over <paramref name="battles"/> battles.</summary>
        private static double StandardError(int battles)
        {
            return battles <= 0 ? 0.0 : 100.0 * Math.Sqrt(0.25 / battles);
        }

        private static void AppendRateHeader(StringBuilder report, SimOptions options, bool aggregate)
        {
            StringBuilder header = new StringBuilder("| Shape | Baseline |");
            StringBuilder rule = new StringBuilder("| --- | ---: |");
            for (int k = 0; k < ScoutedPicker.StrategyCount; k++)
            {
                if (!ScoutedPicker.Runs(options, k))
                {
                    continue;
                }

                if (k == ScoutedPicker.OracleIndex)
                {
                    header.Append(" Best team |");
                    rule.Append(" ---: |");
                }

                header.Append(" " + ScoutedPicker.StrategyNames[k] + " |");
                rule.Append(" ---: |");
                if (aggregate && k == ScoutedPicker.HeuristicIndex)
                {
                    header.Append(" Heuristic uplift SD |");
                    rule.Append(" ---: |");
                }
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
        }

        /// <summary>The strategies with a pick-rate column: the heuristic pickers and the oracle.</summary>
        private static List<int> PickStrategies(SimOptions options)
        {
            List<int> list = new List<int>();
            foreach (int k in new[] { ScoutedPicker.HeuristicIndex, ScoutedPicker.BondAwareIndex, ScoutedPicker.OracleIndex })
            {
                if (ScoutedPicker.Runs(options, k))
                {
                    list.Add(k);
                }
            }

            return list;
        }

        private static string Abbreviation(int k)
        {
            return k == ScoutedPicker.HeuristicIndex ? "H" : k == ScoutedPicker.BondAwareIndex ? "B" : "O";
        }

        private static void AppendPickRates(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                            PveSimulator simulator, List<ScoutedCell> scouted, KitMode mode)
        {
            List<int> strategies = PickStrategies(options);
            if (strategies.Count == 0)
            {
                return;
            }

            // [strategy][shape][beast] percent of picks.
            double[][][] rate = new double[strategies.Count][][];
            for (int k = 0; k < strategies.Count; k++)
            {
                rate[k] = new double[shapes.Count][];
                for (int e = 0; e < shapes.Count; e++)
                {
                    int[] counts = ScoutedPicker.PickCounts(Select(scouted, mode, shapes[e]), strategies[k], simulator.Teams, species.Count, out int picks);
                    rate[k][e] = new double[species.Count];
                    for (int b = 0; b < species.Count; b++)
                    {
                        rate[k][e][b] = picks == 0 ? 0.0 : (100.0 * counts[b]) / picks;
                    }
                }
            }

            AppendPickTable(report, options, species, shapes, strategies, rate, "#### Pick rates (`" + SimOptions.ModeName(mode) + "`, levels pooled)",
                            "Percent of picks (one per composition and level) that field the beast, per shape; each column sums to " + (100 * options.TeamSize) +
                            ".\nThe heuristic pickers see only the composition, so their picks are the same in every mode and level. **0** / **100** =\n" +
                            "never / always fielded.");
            AppendFlags(report, species, shapes, strategies, rate);
        }

        private static void AppendAggregatePickRates(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                                     List<EncounterCatalog> catalogs, List<PveSimulator> simulators, List<ScoutedCell>[] perSeed, KitMode mode)
        {
            List<int> strategies = PickStrategies(options);
            if (strategies.Count == 0)
            {
                return;
            }

            int n = perSeed.Length;
            double[][][] rate = new double[strategies.Count][][];
            for (int k = 0; k < strategies.Count; k++)
            {
                rate[k] = new double[shapes.Count][];
                for (int e = 0; e < shapes.Count; e++)
                {
                    rate[k][e] = new double[species.Count];
                    for (int s = 0; s < n; s++)
                    {
                        int[] counts = ScoutedPicker.PickCounts(Select(perSeed[s], mode, catalogs[s].Shapes[e]), strategies[k], simulators[s].Teams, species.Count,
                                                                out int picks);
                        for (int b = 0; b < species.Count; b++)
                        {
                            rate[k][e][b] += (picks == 0 ? 0.0 : (100.0 * counts[b]) / picks) / n;
                        }
                    }
                }
            }

            AppendPickTable(report, options, species, shapes, strategies, rate, "### Pick rates over seeds (`" + SimOptions.ModeName(mode) + "`)",
                            "Mean percent of picks fielding the beast, per shape; each column sums to " + (100 * options.TeamSize) + ".");
            AppendFlags(report, species, shapes, strategies, rate);
        }

        private static void AppendPickTable(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                            List<int> strategies, double[][][] rate, string heading, string note)
        {
            List<string> abbreviations = new List<string>();
            foreach (int k in strategies)
            {
                abbreviations.Add(Abbreviation(k));
            }

            string legend = string.Join(" / ", abbreviations);
            report.AppendLine(heading);
            report.AppendLine();
            foreach (string line in note.Split('\n'))
            {
                report.AppendLine(line);
            }

            report.AppendLine();
            StringBuilder header = new StringBuilder("| Beast | Element | Stance |");
            StringBuilder rule = new StringBuilder("| --- | --- | --- |");
            foreach (EncounterShape shape in shapes)
            {
                header.Append(" `" + shape.Id + "` " + legend + " |");
                rule.Append(" ---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            for (int b = 0; b < species.Count; b++)
            {
                StringBuilder line = new StringBuilder("| " + species[b].DisplayName + " | " + PvpReport.ElementsOf(species[b]) + " | " + species[b].Stance + " |");
                for (int e = 0; e < shapes.Count; e++)
                {
                    List<string> parts = new List<string>();
                    for (int k = 0; k < strategies.Count; k++)
                    {
                        parts.Add(Share(rate[k][e][b]));
                    }

                    line.Append(" " + string.Join(" / ", parts) + " |");
                }

                report.AppendLine(line.ToString());
            }

            report.AppendLine();
        }

        private static void AppendFlags(StringBuilder report, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes, List<int> strategies,
                                        double[][][] rate)
        {
            for (int k = 0; k < strategies.Count; k++)
            {
                List<string> never = new List<string>();
                List<string> always = new List<string>();
                for (int e = 0; e < shapes.Count; e++)
                {
                    List<string> neverHere = new List<string>();
                    List<string> alwaysHere = new List<string>();
                    for (int b = 0; b < species.Count; b++)
                    {
                        if (rate[k][e][b] < 1e-9)
                        {
                            neverHere.Add(species[b].DisplayName);
                        }
                        else if (rate[k][e][b] > 100.0 - 1e-9)
                        {
                            alwaysHere.Add(species[b].DisplayName);
                        }
                    }

                    if (neverHere.Count > 0)
                    {
                        never.Add("`" + shapes[e].Id + "` " + string.Join(", ", neverHere));
                    }

                    if (alwaysHere.Count > 0)
                    {
                        always.Add("`" + shapes[e].Id + "` " + string.Join(", ", alwaysHere));
                    }
                }

                report.AppendLine("- " + ScoutedPicker.StrategyNames[strategies[k]] + " (" + Abbreviation(strategies[k]) + "): never fielded: " +
                                  (never.Count == 0 ? "none" : string.Join("; ", never)) + ". Always fielded: " +
                                  (always.Count == 0 ? "none" : string.Join("; ", always)) + ".");
            }

            report.AppendLine();
        }

        /// <summary>The cells of <paramref name="mode"/> in <paramref name="shape"/> (every shape when null).</summary>
        private static List<ScoutedCell> Select(List<ScoutedCell> scouted, KitMode mode, EncounterShape shape)
        {
            return scouted.FindAll(c => c.Cell.Mode == mode && (shape == null || c.Cell.Shape == shape));
        }

        /// <summary>Levels averaged within a shape, then shapes averaged (each shape weighs the same, as the marginal tables do).</summary>
        private static double Mean(List<ScoutedCell> part, List<EncounterShape> shapes, Func<ScoutedCell, double> value)
        {
            double total = 0.0;
            int used = 0;
            foreach (EncounterShape shape in shapes)
            {
                List<ScoutedCell> inShape = part.FindAll(c => c.Cell.Shape == shape);
                if (inShape.Count == 0)
                {
                    continue;
                }

                double sum = 0.0;
                foreach (ScoutedCell cell in inShape)
                {
                    sum += value(cell);
                }

                total += sum / inShape.Count;
                used++;
            }

            return used == 0 ? 0.0 : total / used;
        }

        private static string RateCell(double rate, double baseline)
        {
            return Pct(rate) + " (" + SimOptions.Signed(rate - baseline) + ")";
        }

        private static string Share(double share)
        {
            string text = share.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            return share < 1e-9 || share > 100.0 - 1e-9 ? "**" + text + "**" : text;
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
