using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The "PvE team bonds" section (and its <c>--seeds</c> counterpart): which bonds each team
    /// activates, and what an active bond is worth in clear rate. A regrouping of battles already
    /// run, like <see cref="TeamReport"/>; nothing at all when bonds are off.
    /// <para>
    /// A bond's <strong>Δ</strong> is the mean clear rate of the teams where it is active minus that
    /// of the teams where it is not. That mixes the bond with its members' own strength (only teams
    /// holding the right beasts can have it), so the <strong>excess</strong> is also shown: the
    /// active teams' mean rate minus what the beasts' marginals predict for them with no interaction
    /// (each team's prediction is baseline + (n - 1) / n x its members' centred marginals, the
    /// additive model the pair synergy table uses). The beasts' marginals already contain the
    /// average value of the bonds they sit in, so the excess is the part of the bond that the
    /// lineup, not the beasts, earns; compare a <c>--bonds off</c> run for the whole effect.
    /// </para>
    /// </summary>
    public static class BondReport
    {
        /// <summary>The single-seed section, after "PvE team composition".</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                         PveSimulator simulator, List<PveCell> cells)
        {
            if (!options.BondsActive || simulator.Teams.Count < 2)
            {
                return;
            }

            IReadOnlyList<TeamBondSO> bonds = options.Library.TeamBonds;
            int teams = simulator.Teams.Count;
            report.AppendLine("### PvE team bonds");
            report.AppendLine();
            report.AppendLine("Team bonds (`TeamBonds` in the skill library) apply at battle start to every player team that meets their condition");
            report.AppendLine("(`--bonds on`, the default; never to enemies). Each recipient applies the reached tier's effects to itself, so a");
            report.AppendLine("shield scales with its own Defense. **Members** = the beasts that meet the condition; **Team** = every beast.");
            report.AppendLine();

            report.AppendLine("#### Bonds and how often they are active");
            report.AppendLine();
            report.AppendLine("| Bond | Condition | Scope | Tiers | Teams active (of " + teams + ") | Per tier |");
            report.AppendLine("| --- | --- | --- | --- | ---: | --- |");
            foreach (TeamBondSO bond in bonds)
            {
                int[] perTier = TierCounts(simulator, bond);
                int active = 0;
                List<string> tierText = new List<string>();
                List<string> tierCounts = new List<string>();
                for (int t = 0; t < bond.Tiers.Count; t++)
                {
                    active += perTier[t];
                    tierText.Add(bond.Tiers[t].MinCount + "+: " + Report.DescribeEffects(bond.Tiers[t].Effects));
                    tierCounts.Add("t" + (t + 1) + " " + perTier[t]);
                }

                report.AppendLine("| `" + bond.BondId + "` " + bond.DisplayName + " | " + ConditionText(bond, species) + " | " + bond.Scope + " | " +
                                  string.Join("<br>", tierText) + " | " + active + " (" + Pct(100.0 * active / teams) + ") | " + string.Join(", ", tierCounts) + " |");
            }

            report.AppendLine();
            report.AppendLine("Tier effects are applied as authored (bonds do not level); a later tier replaces an earlier one. Every beast belongs to");
            report.AppendLine("its stance's bond and to one element bond:");
            report.AppendLine();
            foreach (CreatureSpeciesSO beast in species)
            {
                report.AppendLine("- " + beast.DisplayName + ": " + string.Join(", ", BondsOf(bonds, beast)));
            }

            report.AppendLine();
            int[] histogram = ActiveCountHistogram(simulator);
            StringBuilder header = new StringBuilder("| Active bonds |");
            StringBuilder rule = new StringBuilder("| --- |");
            StringBuilder row = new StringBuilder("| Teams |");
            for (int c = 0; c < histogram.Length; c++)
            {
                header.Append(" " + c + " |");
                rule.Append(" ---: |");
                row.Append(" " + histogram[c] + " |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            report.AppendLine(row.ToString());
            report.AppendLine();

            foreach (KitMode mode in options.Modes)
            {
                TeamData data = TeamData.Build(options, shapes, cells, mode);
                report.AppendLine("#### Bond marginal (`" + SimOptions.ModeName(mode) + "`, levels pooled)");
                report.AppendLine();
                report.AppendLine("Per shape: **Δ** = clear rate of the teams with the bond active minus the teams without / **excess** over the additive");
                report.AppendLine("prediction from the members' marginals (see the class notes in `BondReport.cs`). Points of clear rate.");
                report.AppendLine();
                StringBuilder h = new StringBuilder("| Bond | Teams |");
                StringBuilder r = new StringBuilder("| --- | ---: |");
                foreach (EncounterShape shape in shapes)
                {
                    h.Append(" `" + shape.Id + "` |");
                    r.Append(" ---: |");
                }

                h.Append(" Overall |");
                r.Append(" ---: |");
                report.AppendLine(h.ToString());
                report.AppendLine(r.ToString());
                foreach (TeamBondSO bond in bonds)
                {
                    bool[] active = ActiveMask(simulator, bond);
                    StringBuilder line = new StringBuilder("| `" + bond.BondId + "` | " + CountTrue(active) + " |");
                    for (int e = 0; e <= shapes.Count; e++)
                    {
                        double[] rate = e < shapes.Count ? data.ByShape[e].Rate : data.Overall.Rate;
                        line.Append(" " + Cell(Delta(rate, active), Excess(rate, active, simulator.Teams, species.Count)) + " |");
                    }

                    report.AppendLine(line.ToString());
                }

                report.AppendLine();
            }

            TeamData primary = TeamData.Build(options, shapes, cells, TeamReport.PrimaryMode(options));
            report.AppendLine("#### Clear rate by number of active bonds (`" + SimOptions.ModeName(TeamReport.PrimaryMode(options)) + "`, levels pooled)");
            report.AppendLine();
            StringBuilder bh = new StringBuilder("| Active bonds | Teams |");
            StringBuilder br = new StringBuilder("| ---: | ---: |");
            foreach (EncounterShape shape in shapes)
            {
                bh.Append(" `" + shape.Id + "` |");
                br.Append(" ---: |");
            }

            bh.Append(" Overall |");
            br.Append(" ---: |");
            report.AppendLine(bh.ToString());
            report.AppendLine(br.ToString());
            for (int c = 0; c < histogram.Length; c++)
            {
                if (histogram[c] == 0)
                {
                    continue;
                }

                StringBuilder line = new StringBuilder("| " + c + " | " + histogram[c] + " |");
                for (int e = 0; e <= shapes.Count; e++)
                {
                    double[] rate = e < shapes.Count ? primary.ByShape[e].Rate : primary.Overall.Rate;
                    double sum = 0.0;
                    for (int t = 0; t < teams; t++)
                    {
                        sum += simulator.TeamBonds[t].Count == c ? rate[t] : 0.0;
                    }

                    line.Append(" " + Pct(sum / histogram[c]) + " |");
                }

                report.AppendLine(line.ToString());
            }

            report.AppendLine();
        }

        /// <summary>The <c>--seeds</c> aggregate's bond section: each bond's Δ and excess, averaged over the seeds.</summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int> seeds,
                                           List<EncounterCatalog> catalogs, List<PveSimulator> simulators, List<List<PveCell>> cells)
        {
            if (!options.BondsActive || simulators[0].Teams.Count < 2)
            {
                return;
            }

            IReadOnlyList<TeamBondSO> bonds = options.Library.TeamBonds;
            List<EncounterShape> shapes = catalogs[0].Shapes;
            int n = seeds.Count;
            report.AppendLine("## PvE team bonds over seeds");
            report.AppendLine();
            report.AppendLine("Each seed's bond marginal (as in its \"PvE team bonds\" section), averaged over the seeds: **Δ** = teams with the bond");
            report.AppendLine("active minus teams without; **excess** = over the additive prediction from the members' marginals. Overall also gives");
            report.AppendLine("the sample SD of Δ over seeds. Which teams hold a bond does not depend on the seed.");
            report.AppendLine();
            foreach (KitMode mode in options.Modes)
            {
                TeamData[] perSeed = new TeamData[n];
                for (int s = 0; s < n; s++)
                {
                    perSeed[s] = TeamData.Build(options, catalogs[s].Shapes, cells[s], mode);
                }

                report.AppendLine("### Bond marginal over seeds (`" + SimOptions.ModeName(mode) + "`)");
                report.AppendLine();
                StringBuilder h = new StringBuilder("| Bond | Teams |");
                StringBuilder r = new StringBuilder("| --- | ---: |");
                foreach (EncounterShape shape in shapes)
                {
                    h.Append(" `" + shape.Id + "` Δ / excess |");
                    r.Append(" ---: |");
                }

                h.Append(" Overall Δ (SD) | Overall excess |");
                r.Append(" ---: | ---: |");
                report.AppendLine(h.ToString());
                report.AppendLine(r.ToString());
                foreach (TeamBondSO bond in bonds)
                {
                    bool[] active = ActiveMask(simulators[0], bond);
                    StringBuilder line = new StringBuilder("| `" + bond.BondId + "` | " + CountTrue(active) + " |");
                    for (int e = 0; e <= shapes.Count; e++)
                    {
                        double[] deltas = new double[n];
                        double excess = 0.0;
                        for (int s = 0; s < n; s++)
                        {
                            double[] rate = e < shapes.Count ? perSeed[s].ByShape[e].Rate : perSeed[s].Overall.Rate;
                            deltas[s] = Delta(rate, active);
                            excess += Excess(rate, active, simulators[s].Teams, species.Count) / n;
                        }

                        double mean = 0.0;
                        foreach (double d in deltas)
                        {
                            mean += d / n;
                        }

                        if (e < shapes.Count)
                        {
                            line.Append(" " + Cell(mean, excess) + " |");
                        }
                        else
                        {
                            double sd = n > 1 ? Math.Sqrt(TeamReport.Variance(deltas, true)) : double.NaN;
                            line.Append(" " + SimOptions.Signed(mean) + " (" + (double.IsNaN(sd) ? "-" : SimOptions.Format(sd)) + ") | " + SimOptions.Signed(excess) + " |");
                        }
                    }

                    report.AppendLine(line.ToString());
                }

                report.AppendLine();
            }
        }

        private static string ConditionText(TeamBondSO bond, IReadOnlyList<CreatureSpeciesSO> species)
        {
            switch (bond.Condition)
            {
                case TeamBondCondition.Stance:
                    return bond.Stance + " beasts";
                case TeamBondCondition.Elements:
                    return string.Join(" + ", bond.Elements) + " (distinct elements covered)";
                default:
                    List<string> names = new List<string>();
                    foreach (string id in bond.SpeciesIds)
                    {
                        CreatureSpeciesSO match = null;
                        foreach (CreatureSpeciesSO s in species)
                        {
                            match = s.SpeciesId == id ? s : match;
                        }

                        names.Add(match == null ? id : match.DisplayName);
                    }

                    return string.Join(" + ", names) + " (species fielded)";
            }
        }

        private static List<string> BondsOf(IReadOnlyList<TeamBondSO> bonds, CreatureSpeciesSO beast)
        {
            List<string> names = new List<string>();
            List<TeamBondMember> single = new List<TeamBondMember> { TeamBondMember.FromSpecies(beast) };
            foreach (TeamBondSO bond in bonds)
            {
                List<int> members = new List<int>();
                TeamBondResolver.Count(bond, single, members);
                if (members.Count > 0)
                {
                    names.Add("`" + bond.BondId + "`");
                }
            }

            if (names.Count == 0)
            {
                names.Add("none");
            }

            return names;
        }

        /// <summary>[tier - 1] how many teams have the bond active at that tier.</summary>
        private static int[] TierCounts(PveSimulator simulator, TeamBondSO bond)
        {
            int[] counts = new int[bond.Tiers.Count];
            foreach (List<ActiveTeamBond> active in simulator.TeamBonds)
            {
                foreach (ActiveTeamBond a in active)
                {
                    if (a.Bond == bond)
                    {
                        counts[a.Tier - 1]++;
                    }
                }
            }

            return counts;
        }

        private static bool[] ActiveMask(PveSimulator simulator, TeamBondSO bond)
        {
            bool[] mask = new bool[simulator.Teams.Count];
            for (int t = 0; t < mask.Length; t++)
            {
                foreach (ActiveTeamBond a in simulator.TeamBonds[t])
                {
                    mask[t] |= a.Bond == bond;
                }
            }

            return mask;
        }

        /// <summary>[k] teams with exactly k active bonds, up to the largest k any team has.</summary>
        private static int[] ActiveCountHistogram(PveSimulator simulator)
        {
            int max = 0;
            foreach (List<ActiveTeamBond> active in simulator.TeamBonds)
            {
                max = Math.Max(max, active.Count);
            }

            int[] histogram = new int[max + 1];
            foreach (List<ActiveTeamBond> active in simulator.TeamBonds)
            {
                histogram[active.Count]++;
            }

            return histogram;
        }

        private static int CountTrue(bool[] mask)
        {
            int count = 0;
            foreach (bool b in mask)
            {
                count += b ? 1 : 0;
            }

            return count;
        }

        /// <summary>Mean rate of the masked teams minus the mean of the others (0 when either side is empty).</summary>
        public static double Delta(double[] rate, bool[] active)
        {
            double with = 0.0;
            double without = 0.0;
            int withCount = 0;
            int withoutCount = 0;
            for (int t = 0; t < rate.Length; t++)
            {
                if (active[t])
                {
                    with += rate[t];
                    withCount++;
                }
                else
                {
                    without += rate[t];
                    withoutCount++;
                }
            }

            return withCount == 0 || withoutCount == 0 ? 0.0 : (with / withCount) - (without / withoutCount);
        }

        /// <summary>
        /// Mean over the masked teams of (rate - additive prediction), the prediction being
        /// baseline + (n - 1) / n x (the team's summed beast marginals - k / n x all marginals).
        /// </summary>
        public static double Excess(double[] rate, bool[] active, List<int[]> teams, int speciesCount)
        {
            int n = speciesCount;
            int k = teams[0].Length;
            double baseline = 0.0;
            foreach (double v in rate)
            {
                baseline += v / rate.Length;
            }

            double[] marginal = new double[n];
            double total = 0.0;
            for (int b = 0; b < n; b++)
            {
                bool[] holds = new bool[teams.Count];
                for (int t = 0; t < teams.Count; t++)
                {
                    holds[t] = Array.IndexOf(teams[t], b) >= 0;
                }

                marginal[b] = Delta(rate, holds);
                total += marginal[b];
            }

            double sum = 0.0;
            int count = 0;
            for (int t = 0; t < teams.Count; t++)
            {
                if (!active[t])
                {
                    continue;
                }

                double members = 0.0;
                foreach (int b in teams[t])
                {
                    members += marginal[b];
                }

                double predicted = baseline + ((double)(n - 1) / n * (members - ((double)k / n * total)));
                sum += rate[t] - predicted;
                count++;
            }

            return count == 0 ? 0.0 : sum / count;
        }

        private static string Cell(double delta, double excess)
        {
            return SimOptions.Signed(delta) + " / " + SimOptions.Signed(excess);
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }
    }
}
