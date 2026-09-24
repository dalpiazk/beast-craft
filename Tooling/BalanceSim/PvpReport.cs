using System;
using System.Collections.Generic;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Aggregates the 1v1 round-robin's <see cref="BattleRecord"/>s into the report's secondary
    /// "PvP" section. Pure: the output depends
    /// only on its inputs (no timestamps, machine paths or culture-sensitive formatting), which is
    /// what makes two runs byte-identical.
    /// </summary>
    public static class PvpReport
    {
        private class Tally
        {
            public int Games;
            public int Wins;
            public int Losses;
            public int MutualDefeats;
            public int Stalemates;
            public double WinnerHpSum;

            public double WinRate
            {
                get { return Games == 0 ? 0.0 : (100.0 * Wins) / Games; }
            }

            public double AverageWinnerHp
            {
                get { return Wins == 0 ? 0.0 : (100.0 * WinnerHpSum) / Wins; }
            }
        }

        /// <summary>
        /// Checks the round-robin is balanced — every species played the same number of games at
        /// every level in every mode — and returns a description of each violation.
        /// </summary>
        public static List<string> CheckGameCounts(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records)
        {
            List<string> problems = new List<string>();
            int expected = 2 * (species.Count - 1) * options.Samples;

            foreach (KitMode mode in options.Modes)
            {
                foreach (int level in options.Levels)
                {
                    int[] games = new int[species.Count];
                    foreach (BattleRecord record in records)
                    {
                        if (record.Mode == mode && record.Level == level)
                        {
                            games[record.PlayerIndex]++;
                            games[record.EnemyIndex]++;
                        }
                    }

                    for (int i = 0; i < species.Count; i++)
                    {
                        if (games[i] != expected)
                        {
                            problems.Add(species[i].SpeciesId + " played " + games[i] + " games in " + SimOptions.ModeName(mode) +
                                         " mode at level " + level + "; expected " + expected + ".");
                        }
                    }
                }
            }

            return problems;
        }

        /// <summary>Appends the whole PvP section (heading, configuration, flags, one block per kit mode).</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records)
        {
            int gamesPerBeast = 2 * (species.Count - 1) * options.Samples;

            report.AppendLine("## PvP: 1v1 round-robin (secondary)");
            report.AppendLine();
            report.AppendLine("Kept for reference: the game is expected to be PvE, so a beast's 1v1 record against the other roster beasts is a");
            report.AppendLine("secondary signal. Same standard kit as the PvE section.");
            report.AppendLine();

            AppendConfiguration(report, options, species, records.Count, gamesPerBeast);
            AppendFlagSummary(report, options, species, records);

            foreach (KitMode mode in options.Modes)
            {
                AppendMode(report, options, species, records, mode);
            }
        }

        private static void AppendConfiguration(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, int battles, int gamesPerBeast)
        {
            report.AppendLine("### PvP configuration");
            report.AppendLine();
            report.AppendLine("- Levels: " + SimOptions.Join(options.Levels) + "; kit modes: " + ModeList(options.Modes) + "; no gear; no avatar");
            report.AppendLine("- Arena: " + SimOptions.PvpArena + ", 1v1, mirrored central start tiles in each deployment zone");
            report.AppendLine("- Round-robin: every pair of distinct species, played twice per level and mode with sides swapped, each game " +
                              options.Samples + " times with distinct seeds (damage variance and crits make battles random) (" +
                              gamesPerBeast + " games per beast per level per mode; mirror matches skipped); " + battles + " battles total");
            report.AppendLine("- Win rate = wins / games; mutual defeats and stalemates count as games but not wins. Flags: win rate above " +
                              SimOptions.Format(SimOptions.HighWinRate) + "% or below " + SimOptions.Format(SimOptions.LowWinRate) +
                              "%, a swing of more than " + SimOptions.Format(SimOptions.MaxLevelSwing) + " points across levels, and any stalemate.");
            report.AppendLine("- Turn order: the Runtime's ATB gauge (`TurnManager`: each unit acts every " + TurnManager.ActionThreshold +
                              " / round(" + TurnManager.FillScale + " x sqrt(Speed)) ticks, so turns grow with the square root of Speed). Battle length is normalized time, 1.0 = one turn of a Speed-" +
                              TurnManager.ReferenceSpeed +
                              " unit, so it reads longer at low levels, where Speed is lower; max time " + options.MaxTime + ".");
            report.AppendLine();
        }

        private static void AppendFlagSummary(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records)
        {
            report.AppendLine("### PvP flagged outliers");
            report.AppendLine();

            bool any = false;
            foreach (KitMode mode in options.Modes)
            {
                Tally[,] byLevel = TallyByLevel(options, species, records, mode);
                Tally[] overall = TallyOverall(options, species, records, mode);

                for (int i = 0; i < species.Count; i++)
                {
                    List<string> flags = FlagsFor(options, byLevel, overall, i);
                    if (flags.Count > 0)
                    {
                        report.AppendLine("- `" + SimOptions.ModeName(mode) + "` " + species[i].DisplayName + ": " + string.Join("; ", flags));
                        any = true;
                    }
                }

                int stalemates = 0;
                foreach (BattleRecord record in records)
                {
                    if (record.Mode == mode && record.Outcome == BattleOutcome.Stalemate)
                    {
                        stalemates++;
                    }
                }

                if (stalemates > 0)
                {
                    report.AppendLine("- `" + SimOptions.ModeName(mode) + "`: " + stalemates + " stalemated battle(s) — see that mode's stalemate list");
                    any = true;
                }
            }

            if (!any)
            {
                report.AppendLine("None.");
            }

            report.AppendLine();
        }

        private static List<string> FlagsFor(SimOptions options, Tally[,] byLevel, Tally[] overall, int beast)
        {
            List<string> flags = new List<string>();
            List<string> high = new List<string>();
            List<string> low = new List<string>();
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int l = 0; l < options.Levels.Count; l++)
            {
                double rate = byLevel[beast, l].WinRate;
                string label = "L" + options.Levels[l] + " " + SimOptions.Format(rate) + "%";
                if (rate > SimOptions.HighWinRate)
                {
                    high.Add(label);
                }
                else if (rate < SimOptions.LowWinRate)
                {
                    low.Add(label);
                }

                min = Math.Min(min, rate);
                max = Math.Max(max, rate);
            }

            double overallRate = overall[beast].WinRate;
            if (overallRate > SimOptions.HighWinRate)
            {
                flags.Add("overall HIGH " + SimOptions.Format(overallRate) + "%");
            }
            else if (overallRate < SimOptions.LowWinRate)
            {
                flags.Add("overall LOW " + SimOptions.Format(overallRate) + "%");
            }

            if (high.Count > 0)
            {
                flags.Add("high at " + string.Join(", ", high));
            }

            if (low.Count > 0)
            {
                flags.Add("low at " + string.Join(", ", low));
            }

            if (options.Levels.Count > 1 && (max - min) > SimOptions.MaxLevelSwing)
            {
                flags.Add("swings " + SimOptions.Format(max - min) + " points across levels");
            }

            return flags;
        }

        private static void AppendMode(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records, KitMode mode)
        {
            Tally[,] byLevel = TallyByLevel(options, species, records, mode);
            Tally[] overall = TallyOverall(options, species, records, mode);
            string name = SimOptions.ModeName(mode);

            report.AppendLine("### PvP mode: `" + name + "`");
            report.AppendLine();

            // Win rate by level.
            report.AppendLine("#### Win rate by level");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Beast | Element |");
            StringBuilder rule = new StringBuilder("| --- | --- |");
            foreach (int level in options.Levels)
            {
                header.Append(" L" + level + " |");
                rule.Append(" ---: |");
            }

            header.Append(" Overall | W-L-D-S | Swing | Avg HP left on win |");
            rule.Append(" ---: | --- | ---: | ---: |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            List<int> order = SortByOverall(species, overall);
            foreach (int i in order)
            {
                StringBuilder row = new StringBuilder("| " + species[i].DisplayName + " | " + ElementsOf(species[i]) + " |");
                double min = double.MaxValue;
                double max = double.MinValue;

                for (int l = 0; l < options.Levels.Count; l++)
                {
                    double rate = byLevel[i, l].WinRate;
                    row.Append(" " + Marked(rate) + " |");
                    min = Math.Min(min, rate);
                    max = Math.Max(max, rate);
                }

                Tally total = overall[i];
                double swing = options.Levels.Count > 1 ? max - min : 0.0;
                string swingText = SimOptions.Format(swing) + (swing > SimOptions.MaxLevelSwing ? " !" : string.Empty);
                row.Append(" " + Marked(total.WinRate) + " | " + total.Wins + "-" + total.Losses + "-" + total.MutualDefeats + "-" + total.Stalemates +
                           " | " + swingText + " | " + SimOptions.Format(total.AverageWinnerHp) + "% |");
                report.AppendLine(row.ToString());
            }

            report.AppendLine();
            report.AppendLine("Sorted by overall win rate. **Bold** = above " + SimOptions.Format(SimOptions.HighWinRate) + "%, _italic_ = below " +
                              SimOptions.Format(SimOptions.LowWinRate) + "%, `!` = level swing above " + SimOptions.Format(SimOptions.MaxLevelSwing) +
                              " points. W-L-D-S = wins, losses, mutual defeats, stalemates across all levels.");
            report.AppendLine();

            AppendBattleLength(report, options, records, mode);
            AppendMatrix(report, options, species, records, mode);
            AppendStalemates(report, species, records, mode);
        }

        private static void AppendBattleLength(StringBuilder report, SimOptions options, List<BattleRecord> records, KitMode mode)
        {
            report.AppendLine("#### Battle length by level");
            report.AppendLine();
            report.AppendLine("| Level | Battles | Avg time | Min | Max | Avg turns | Stalemates | Mutual defeats |");
            report.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (int level in options.Levels)
            {
                int battles = 0;
                double timeSum = 0.0;
                double min = double.MaxValue;
                double max = 0.0;
                long actionSum = 0;
                int stalemates = 0;
                int mutual = 0;

                foreach (BattleRecord record in records)
                {
                    if (record.Mode != mode || record.Level != level)
                    {
                        continue;
                    }

                    battles++;
                    timeSum += record.Time;
                    actionSum += record.Actions;
                    min = Math.Min(min, record.Time);
                    max = Math.Max(max, record.Time);
                    stalemates += record.Outcome == BattleOutcome.Stalemate ? 1 : 0;
                    mutual += record.Outcome == BattleOutcome.MutualDefeat ? 1 : 0;
                }

                double average = battles == 0 ? 0.0 : timeSum / battles;
                double turns = battles == 0 ? 0.0 : (double)actionSum / battles;
                report.AppendLine("| " + level + " | " + battles + " | " + SimOptions.Format(average) + " | " + SimOptions.Format(battles == 0 ? 0.0 : min) +
                                  " | " + SimOptions.Format(max) + " | " + SimOptions.Format(turns) + " | " + stalemates + " | " + mutual + " |");
            }

            report.AppendLine();
        }

        private static void AppendMatrix(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records, KitMode mode)
        {
            int count = species.Count;
            int[,] wins = new int[count, count];
            int[,] other = new int[count, count];

            foreach (BattleRecord record in records)
            {
                if (record.Mode != mode || record.Level != options.MatrixLevel)
                {
                    continue;
                }

                if (record.WinnerIndex >= 0)
                {
                    int loser = record.WinnerIndex == record.PlayerIndex ? record.EnemyIndex : record.PlayerIndex;
                    wins[record.WinnerIndex, loser]++;
                }
                else
                {
                    other[record.PlayerIndex, record.EnemyIndex]++;
                    other[record.EnemyIndex, record.PlayerIndex]++;
                }
            }

            report.AppendLine("#### Win matrix at level " + options.MatrixLevel);
            report.AppendLine();
            report.AppendLine("Row vs column: row's wins-losses over the two side-swapped games x " + options.Samples +
                              " samples; `+N` = N games that were a stalemate or mutual defeat.");
            report.AppendLine();

            StringBuilder header = new StringBuilder("| vs |");
            StringBuilder rule = new StringBuilder("| --- |");
            for (int j = 0; j < count; j++)
            {
                header.Append(" " + species[j].DisplayName + " |");
                rule.Append(" :---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            for (int i = 0; i < count; i++)
            {
                StringBuilder row = new StringBuilder("| **" + species[i].DisplayName + "** |");
                for (int j = 0; j < count; j++)
                {
                    if (i == j)
                    {
                        row.Append(" — |");
                        continue;
                    }

                    string cell = wins[i, j] + "-" + wins[j, i];
                    if (other[i, j] > 0)
                    {
                        cell += " +" + other[i, j];
                    }

                    row.Append(" " + cell + " |");
                }

                report.AppendLine(row.ToString());
            }

            report.AppendLine();
        }

        private static void AppendStalemates(StringBuilder report, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records, KitMode mode)
        {
            report.AppendLine("#### Stalemated matchups");
            report.AppendLine();

            bool any = false;
            foreach (BattleRecord record in records)
            {
                if (record.Mode == mode && record.Outcome == BattleOutcome.Stalemate)
                {
                    report.AppendLine("- L" + record.Level + ": " + species[record.PlayerIndex].DisplayName + " (player) vs " +
                                      species[record.EnemyIndex].DisplayName + " (enemy), sample " + record.Sample + ", " + SimOptions.Format(record.Time) + " time");
                    any = true;
                }
            }

            if (!any)
            {
                report.AppendLine("None.");
            }

            report.AppendLine();
        }

        private static Tally[,] TallyByLevel(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records, KitMode mode)
        {
            Tally[,] tallies = new Tally[species.Count, options.Levels.Count];
            for (int i = 0; i < species.Count; i++)
            {
                for (int l = 0; l < options.Levels.Count; l++)
                {
                    tallies[i, l] = new Tally();
                }
            }

            foreach (BattleRecord record in records)
            {
                if (record.Mode != mode)
                {
                    continue;
                }

                int l = options.Levels.IndexOf(record.Level);
                Count(tallies[record.PlayerIndex, l], record, record.PlayerIndex);
                Count(tallies[record.EnemyIndex, l], record, record.EnemyIndex);
            }

            return tallies;
        }

        private static Tally[] TallyOverall(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<BattleRecord> records, KitMode mode)
        {
            Tally[] tallies = new Tally[species.Count];
            for (int i = 0; i < species.Count; i++)
            {
                tallies[i] = new Tally();
            }

            foreach (BattleRecord record in records)
            {
                if (record.Mode != mode || !options.Levels.Contains(record.Level))
                {
                    continue;
                }

                Count(tallies[record.PlayerIndex], record, record.PlayerIndex);
                Count(tallies[record.EnemyIndex], record, record.EnemyIndex);
            }

            return tallies;
        }

        private static void Count(Tally tally, BattleRecord record, int beast)
        {
            tally.Games++;
            switch (record.Outcome)
            {
                case BattleOutcome.Stalemate:
                    tally.Stalemates++;
                    break;
                case BattleOutcome.MutualDefeat:
                    tally.MutualDefeats++;
                    break;
                default:
                    if (record.WinnerIndex == beast)
                    {
                        tally.Wins++;
                        tally.WinnerHpSum += record.WinnerHpFraction;
                    }
                    else
                    {
                        tally.Losses++;
                    }

                    break;
            }
        }

        private static List<int> SortByOverall(IReadOnlyList<CreatureSpeciesSO> species, Tally[] overall)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < species.Count; i++)
            {
                order.Add(i);
            }

            // Stable and total: win rate descending, then roster order.
            order.Sort((x, y) =>
            {
                int byRate = overall[y].Wins.CompareTo(overall[x].Wins);
                return byRate != 0 ? byRate : x.CompareTo(y);
            });

            return order;
        }

        private static string Marked(double rate)
        {
            string text = SimOptions.Format(rate) + "%";
            if (rate > SimOptions.HighWinRate)
            {
                return "**" + text + "**";
            }

            return rate < SimOptions.LowWinRate ? "_" + text + "_" : text;
        }

        public static string ElementsOf(CreatureSpeciesSO beast)
        {
            if (beast.Elements == null || beast.Elements.Length == 0)
            {
                return "None";
            }

            List<string> names = new List<string>();
            foreach (Element element in beast.Elements)
            {
                names.Add(element.ToString());
            }

            return string.Join("/", names);
        }

        public static string ModeList(List<KitMode> modes)
        {
            List<string> names = new List<string>();
            foreach (KitMode mode in modes)
            {
                names.Add("`" + SimOptions.ModeName(mode) + "`");
            }

            return string.Join(", ", names);
        }
    }
}
