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
    /// <para>
    /// A <strong>scaling</strong> bond (<c>PerCount</c>) also gets a table by condition count: the
    /// stacks it applies (0 when no teammate receives it: below its <c>MinCount</c>, or an
    /// <c>Others</c> bond on a team made only of its members), the teams, their mean clear rate and
    /// mean excess; and its <strong>per-stack slope</strong>, the least-squares slope of team clear
    /// rate on the applied stacks over every team. (The slope of the excess would be 0 by
    /// construction for a Team-scope stance bond, whose stacks are a sum of memberships the
    /// additive model already fits; the excess by count shows what it misses.)
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
            report.AppendLine("shield scales with its own Defense. **Members** = the beasts that meet the condition; **Team** = every beast; **Others** =");
            report.AppendLine("every beast that does not meet it.");
            report.AppendLine();

            report.AppendLine("#### Bonds and how often they are active");
            report.AppendLine();
            report.AppendLine("| Bond | Condition | Scope | Tiers | Teams active (of " + teams + ") | Per tier / stacks |");
            report.AppendLine("| --- | --- | --- | --- | ---: | --- |");
            foreach (TeamBondSO bond in bonds)
            {
                int active = 0;
                List<string> tierText = new List<string>();
                List<string> tierCounts = new List<string>();
                if (bond.PerCount)
                {
                    int[] perStack = StackCounts(simulator, bond);
                    for (int k = 1; k < perStack.Length; k++)
                    {
                        active += perStack[k];
                        tierCounts.Add("x" + k + " " + perStack[k]);
                    }

                    tierText.Add(bond.Tiers[0].MinCount + "+, per stack (max " + bond.MaxCount + "): " + Report.DescribeEffects(bond.Tiers[0].Effects));
                }
                else
                {
                    int[] perTier = TierCounts(simulator, bond);
                    for (int t = 0; t < bond.Tiers.Count; t++)
                    {
                        active += perTier[t];
                        tierText.Add(bond.Tiers[t].MinCount + "+: " + Report.DescribeEffects(bond.Tiers[t].Effects));
                        tierCounts.Add("t" + (t + 1) + " " + perTier[t]);
                    }
                }

                report.AppendLine("| `" + bond.BondId + "` " + bond.DisplayName + " | " + ConditionText(bond, species) + " | " + bond.Scope + " | " +
                                  string.Join("<br>", tierText) + " | " + active + " (" + Pct(100.0 * active / teams) + ") | " + string.Join(", ", tierCounts) + " |");
            }

            report.AppendLine();
            report.AppendLine("Tier effects are applied as authored (bonds do not level); a later tier replaces an earlier one. A scaling bond has one");
            report.AppendLine("tier applied at magnitude x stacks (stacks = its count, capped; xk = teams at k stacks). **Others** = every beast that is not");
            report.AppendLine("a member. Every beast belongs to its stance's bonds and to one element bond:");
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

            foreach (KitMode mode in options.Modes)
            {
                AppendScaling(report, options, simulator, bonds, new[] { TeamData.Build(options, shapes, cells, mode).Overall.Rate },
                              new List<PveSimulator> { simulator }, species, "#### Scaling bonds by stacks (`" + SimOptions.ModeName(mode) + "`, levels pooled)");
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

            foreach (KitMode mode in options.Modes)
            {
                double[][] rates = new double[n][];
                for (int s = 0; s < n; s++)
                {
                    rates[s] = TeamData.Build(options, catalogs[s].Shapes, cells[s], mode).Overall.Rate;
                }

                AppendScaling(report, options, simulators[0], bonds, rates, simulators, species,
                              "### Scaling bonds by stacks over seeds (`" + SimOptions.ModeName(mode) + "`)");
            }
        }

        /// <summary>
        /// The scaling-bond table (see the class notes) for one mode, overall rates: one
        /// <paramref name="rates"/> entry per seed (one for the single-seed section), averaged.
        /// Nothing when no bond scales.
        /// </summary>
        private static void AppendScaling(StringBuilder report, SimOptions options, PveSimulator simulator, IReadOnlyList<TeamBondSO> bonds, double[][] rates,
                                          List<PveSimulator> simulators, IReadOnlyList<CreatureSpeciesSO> species, string heading)
        {
            List<TeamBondSO> scaling = new List<TeamBondSO>();
            foreach (TeamBondSO bond in bonds)
            {
                if (bond.PerCount)
                {
                    scaling.Add(bond);
                }
            }

            if (scaling.Count == 0)
            {
                return;
            }

            int n = rates.Length;
            report.AppendLine(heading);
            report.AppendLine();
            report.AppendLine("Per scaling bond, the teams by condition count: **stacks** applied (0 = none: below MinCount, or no teammate receives it),");
            report.AppendLine("mean clear rate and mean **excess** over the additive prediction from the members' marginals" +
                              (n > 1 ? ", each averaged over the " + n + " seeds" : string.Empty) + ". **Slope** = least-squares");
            report.AppendLine("points of clear rate per applied stack over every team" + (n > 1 ? " (mean over seeds, SD)" : string.Empty) + ".");
            report.AppendLine();
            report.AppendLine("| Bond | Count | Stacks | Teams | Clear rate | Excess | Slope / stack |");
            report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (TeamBondSO bond in scaling)
            {
                int[] count = new int[simulator.Teams.Count];
                double[] stacks = new double[simulator.Teams.Count];
                int maxCount = 0;
                for (int t = 0; t < count.Length; t++)
                {
                    count[t] = TeamBondResolver.Count(bond, MembersOf(species, simulator.Teams[t]), null);
                    stacks[t] = AppliedStacks(simulator, t, bond);
                    maxCount = Math.Max(maxCount, count[t]);
                }

                double[] slopes = new double[n];
                double[][] residuals = new double[n][];
                for (int s = 0; s < n; s++)
                {
                    residuals[s] = Residuals(rates[s], simulators[s].Teams, species.Count);
                    slopes[s] = Slope(stacks, rates[s]);
                }

                for (int c = 0; c <= maxCount; c++)
                {
                    int teams = 0;
                    double rate = 0.0;
                    double excess = 0.0;
                    HashSet<double> applied = new HashSet<double>();
                    for (int t = 0; t < count.Length; t++)
                    {
                        if (count[t] != c)
                        {
                            continue;
                        }

                        teams++;
                        applied.Add(stacks[t]);
                        for (int s = 0; s < n; s++)
                        {
                            rate += rates[s][t] / n;
                            excess += residuals[s][t] / n;
                        }
                    }

                    if (teams == 0)
                    {
                        continue;
                    }

                    List<string> appliedText = new List<string>();
                    foreach (double a in applied)
                    {
                        appliedText.Add(((int)a).ToString(CultureInfo.InvariantCulture));
                    }

                    appliedText.Sort(string.CompareOrdinal);
                    string slopeText = c == 0 ? SlopeText(slopes) : string.Empty;
                    report.AppendLine("| " + (c == 0 ? "`" + bond.BondId + "`" : string.Empty) + " | " + c + " | " + string.Join("/", appliedText) + " | " + teams + " | " +
                                      Pct(rate / teams) + " | " + SimOptions.Signed(excess / teams) + " | " + slopeText + " |");
                }
            }

            report.AppendLine();
        }

        private static string SlopeText(double[] values)
        {
            double mean = 0.0;
            foreach (double v in values)
            {
                mean += v / values.Length;
            }

            if (values.Length < 2)
            {
                return SimOptions.Signed(mean);
            }

            return SimOptions.Signed(mean) + " (" + SimOptions.Format(Math.Sqrt(TeamReport.Variance(values, true))) + ")";
        }

        /// <summary>The stacks team <paramref name="team"/> actually applies of <paramref name="bond"/>: 0 when inactive or when no teammate receives it.</summary>
        public static int AppliedStacks(PveSimulator simulator, int team, TeamBondSO bond)
        {
            foreach (ActiveTeamBond a in simulator.TeamBonds[team])
            {
                if (a.Bond != bond)
                {
                    continue;
                }

                if (bond.Scope == TeamBondScope.Others && a.Members.Count >= simulator.Teams[team].Length)
                {
                    return 0;
                }

                return a.Stacks;
            }

            return 0;
        }

        private static List<TeamBondMember> MembersOf(IReadOnlyList<CreatureSpeciesSO> species, int[] team)
        {
            List<TeamBondMember> members = new List<TeamBondMember>();
            foreach (int b in team)
            {
                members.Add(TeamBondMember.FromSpecies(species[b]));
            }

            return members;
        }

        /// <summary>Least-squares slope of <paramref name="y"/> on <paramref name="x"/> (0 when x does not vary).</summary>
        public static double Slope(double[] x, double[] y)
        {
            double mx = 0.0;
            double my = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                mx += x[i] / x.Length;
                my += y[i] / x.Length;
            }

            double sxy = 0.0;
            double sxx = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                sxy += (x[i] - mx) * (y[i] - my);
                sxx += (x[i] - mx) * (x[i] - mx);
            }

            return sxx <= 0.0 ? 0.0 : sxy / sxx;
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
            double[] residual = Residuals(rate, teams, speciesCount);
            double sum = 0.0;
            int count = 0;
            for (int t = 0; t < teams.Count; t++)
            {
                if (active[t])
                {
                    sum += residual[t];
                    count++;
                }
            }

            return count == 0 ? 0.0 : sum / count;
        }

        /// <summary>Per team: rate - the additive prediction from its members' marginals (see <see cref="Excess"/>).</summary>
        public static double[] Residuals(double[] rate, List<int[]> teams, int speciesCount)
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

            double[] residual = new double[teams.Count];
            for (int t = 0; t < teams.Count; t++)
            {
                double members = 0.0;
                foreach (int b in teams[t])
                {
                    members += marginal[b];
                }

                double predicted = baseline + ((double)(n - 1) / n * (members - ((double)k / n * total)));
                residual[t] = rate[t] - predicted;
            }

            return residual;
        }

        /// <summary>[stacks] how many teams hold the bond at that many stacks (index 0 unused).</summary>
        private static int[] StackCounts(PveSimulator simulator, TeamBondSO bond)
        {
            int[] counts = new int[Math.Max(1, bond.MaxCount) + 1];
            foreach (List<ActiveTeamBond> active in simulator.TeamBonds)
            {
                foreach (ActiveTeamBond a in active)
                {
                    if (a.Bond == bond && a.Stacks < counts.Length)
                    {
                        counts[a.Stacks]++;
                    }
                }
            }

            return counts;
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
