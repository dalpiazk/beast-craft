using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>One beast's numbers in one (mode, level, encounter) cell, or averaged over several.</summary>
    public class BeastMetrics
    {
        /// <summary>Clear rate of teams containing the beast minus teams without it, in points.</summary>
        public double Marginal;
        public double ClearWith;
        public double ClearWithout;

        /// <summary>Mean share (percent) of its team's damage dealt / damage taken.</summary>
        public double DamageShare;
        public double TakenShare;

        /// <summary>Percent of its battles it was still standing at the end.</summary>
        public double Survival;

        /// <summary>Mean normalized time of the clears it took part in; NaN when it took part in none.</summary>
        public double TimeToClear = double.NaN;

        /// <summary>
        /// Its turns per unit of normalized time over all its battles (total turns / total battle
        /// time): about Speed / 100 while it stands, less when it falls early. NaN with no time.
        /// </summary>
        public double TurnsPerTime = double.NaN;
    }

    /// <summary>
    /// Turns <see cref="PveCell"/>s into the report's primary "PvE" section. Pure: output depends only
    /// on its inputs, so two runs are byte-identical. Every average is taken in a fixed order.
    /// </summary>
    public static class PveReport
    {
        /// <summary>Per-beast metrics for one cell.</summary>
        public static BeastMetrics[] Compute(PveCell cell, List<int[]> teams, int speciesCount)
        {
            BeastMetrics[] metrics = new BeastMetrics[speciesCount];
            for (int b = 0; b < speciesCount; b++)
            {
                int with = 0;
                int withCleared = 0;
                int without = 0;
                int withoutCleared = 0;
                double shareSum = 0.0;
                int shareCount = 0;
                double takenSum = 0.0;
                int takenCount = 0;
                int alive = 0;
                long clearTicks = 0;
                long turns = 0;
                long battleTicks = 0;

                for (int t = 0; t < teams.Count; t++)
                {
                    PveBattle battle = cell.Battles[t];
                    int member = Array.IndexOf(teams[t], b);
                    if (member < 0)
                    {
                        without++;
                        withoutCleared += battle.Cleared ? 1 : 0;
                        continue;
                    }

                    with++;
                    alive += battle.Alive[member] ? 1 : 0;
                    turns += battle.MemberActions[member];
                    battleTicks += battle.ElapsedTicks;
                    if (battle.Cleared)
                    {
                        withCleared++;
                        clearTicks += battle.ElapsedTicks;
                    }

                    int dealt = Sum(battle.DamageDealt);
                    if (dealt > 0)
                    {
                        shareSum += (100.0 * battle.DamageDealt[member]) / dealt;
                        shareCount++;
                    }

                    int taken = Sum(battle.DamageTaken);
                    if (taken > 0)
                    {
                        takenSum += (100.0 * battle.DamageTaken[member]) / taken;
                        takenCount++;
                    }
                }

                BeastMetrics m = new BeastMetrics
                {
                    ClearWith = with == 0 ? 0.0 : (100.0 * withCleared) / with,
                    ClearWithout = without == 0 ? 0.0 : (100.0 * withoutCleared) / without,
                    DamageShare = shareCount == 0 ? 0.0 : shareSum / shareCount,
                    TakenShare = takenCount == 0 ? 0.0 : takenSum / takenCount,
                    Survival = with == 0 ? 0.0 : (100.0 * alive) / with,
                    TimeToClear = withCleared == 0 ? double.NaN : (double)clearTicks / TurnManager.TicksPerTimeUnit / withCleared,
                    TurnsPerTime = battleTicks == 0 ? double.NaN : (double)turns * TurnManager.TicksPerTimeUnit / battleTicks
                };
                m.Marginal = without == 0 ? 0.0 : m.ClearWith - m.ClearWithout;
                metrics[b] = m;
            }

            return metrics;
        }

        /// <summary>Unweighted mean of several cells' metrics (NaN times and rates skipped).</summary>
        public static BeastMetrics Average(List<BeastMetrics> items)
        {
            BeastMetrics average = new BeastMetrics();
            double time = 0.0;
            int timeCount = 0;
            double rate = 0.0;
            int rateCount = 0;
            foreach (BeastMetrics m in items)
            {
                average.Marginal += m.Marginal;
                average.ClearWith += m.ClearWith;
                average.ClearWithout += m.ClearWithout;
                average.DamageShare += m.DamageShare;
                average.TakenShare += m.TakenShare;
                average.Survival += m.Survival;
                if (!double.IsNaN(m.TimeToClear))
                {
                    time += m.TimeToClear;
                    timeCount++;
                }

                if (!double.IsNaN(m.TurnsPerTime))
                {
                    rate += m.TurnsPerTime;
                    rateCount++;
                }
            }

            int n = Math.Max(1, items.Count);
            average.Marginal /= n;
            average.ClearWith /= n;
            average.ClearWithout /= n;
            average.DamageShare /= n;
            average.TakenShare /= n;
            average.Survival /= n;
            average.TimeToClear = timeCount == 0 ? double.NaN : time / timeCount;
            average.TurnsPerTime = rateCount == 0 ? double.NaN : rate / rateCount;
            return average;
        }

        /// <summary>Everything the section needs for one kit mode, computed once.</summary>
        private class ModeSummary
        {
            public KitMode Mode;

            /// <summary>[encounter][level][beast].</summary>
            public BeastMetrics[][][] ByCell;

            /// <summary>[encounter][beast], levels averaged.</summary>
            public BeastMetrics[][] ByEncounter;

            /// <summary>[beast], every encounter and level averaged.</summary>
            public BeastMetrics[] Overall;

            /// <summary>[encounter] beast indices, best marginal first.</summary>
            public List<int>[] Ranking;

            public List<int> OverallOrder;
        }

        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<Encounter> encounters,
                                         PveSimulator simulator, List<PveCell> cells)
        {
            List<ModeSummary> summaries = new List<ModeSummary>();
            foreach (KitMode mode in options.Modes)
            {
                summaries.Add(Summarize(options, species, encounters, simulator, cells, mode));
            }

            report.AppendLine("## PvE: team vs encounter (primary)");
            report.AppendLine();
            report.AppendLine("The expected shape of the game: the player fields a team of beasts against enemies that are not roster beasts,");
            report.AppendLine("from one huge creature to a couple of dozen small ones. Every team is fielded against every encounter, each");
            report.AppendLine("encounter's difficulty is calibrated so the average team clears it about " + SimOptions.Format(options.TargetClearRate) +
                              "% of the time (where a beast's");
            report.AppendLine("presence moves the outcome most), and each beast is judged by how much it moves its team's clear rate.");
            report.AppendLine();

            AppendConfiguration(report, options, simulator);
            AppendEncounters(report, encounters);
            AppendCalibration(report, options, cells);
            AppendParity(report, options, encounters, cells);
            AppendFlags(report, options, species, encounters, cells, summaries);

            foreach (ModeSummary summary in summaries)
            {
                AppendMode(report, options, species, encounters, summary);
            }
        }

        private static ModeSummary Summarize(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<Encounter> encounters,
                                             PveSimulator simulator, List<PveCell> cells, KitMode mode)
        {
            ModeSummary summary = new ModeSummary
            {
                Mode = mode,
                ByCell = new BeastMetrics[encounters.Count][][],
                ByEncounter = new BeastMetrics[encounters.Count][],
                Overall = new BeastMetrics[species.Count],
                Ranking = new List<int>[encounters.Count]
            };

            for (int e = 0; e < encounters.Count; e++)
            {
                summary.ByCell[e] = new BeastMetrics[options.Levels.Count][];
                for (int l = 0; l < options.Levels.Count; l++)
                {
                    PveCell cell = Find(cells, mode, options.Levels[l], encounters[e]);
                    summary.ByCell[e][l] = Compute(cell, simulator.Teams, species.Count);
                }
            }

            for (int b = 0; b < species.Count; b++)
            {
                List<BeastMetrics> all = new List<BeastMetrics>();
                for (int e = 0; e < encounters.Count; e++)
                {
                    List<BeastMetrics> levels = new List<BeastMetrics>();
                    for (int l = 0; l < options.Levels.Count; l++)
                    {
                        levels.Add(summary.ByCell[e][l][b]);
                        all.Add(summary.ByCell[e][l][b]);
                    }

                    if (summary.ByEncounter[e] == null)
                    {
                        summary.ByEncounter[e] = new BeastMetrics[species.Count];
                    }

                    summary.ByEncounter[e][b] = Average(levels);
                }

                summary.Overall[b] = Average(all);
            }

            for (int e = 0; e < encounters.Count; e++)
            {
                BeastMetrics[] row = summary.ByEncounter[e];
                summary.Ranking[e] = Order(species.Count, b => row[b].Marginal);
            }

            summary.OverallOrder = Order(species.Count, b => summary.Overall[b].Marginal);
            return summary;
        }

        /// <summary>Beast indices, highest value first; ties in roster order.</summary>
        private static List<int> Order(int count, Func<int, double> value)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < count; i++)
            {
                order.Add(i);
            }

            order.Sort((x, y) =>
            {
                int byValue = value(y).CompareTo(value(x));
                return byValue != 0 ? byValue : x.CompareTo(y);
            });
            return order;
        }

        public static PveCell Find(List<PveCell> cells, KitMode mode, int level, Encounter encounter)
        {
            foreach (PveCell cell in cells)
            {
                if (cell.Mode == mode && cell.Level == level && cell.Encounter == encounter)
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Missing PvE cell " + mode + "/" + level + "/" + encounter.Id + ".");
        }

        private static void AppendConfiguration(StringBuilder report, SimOptions options, PveSimulator simulator)
        {
            report.AppendLine("### PvE configuration");
            report.AppendLine();
            report.AppendLine("- Teams: every combination of " + options.TeamSize + " distinct beasts (" + simulator.Teams.Count + " teams, format " +
                              SimOptions.FormatForTeamSize(options.TeamSize) + "); each beast is in " + TeamsWith(simulator) + " of them");
            report.AppendLine("- Levels: " + SimOptions.Join(options.Levels) + " (beasts and enemies at the same level); kit modes: " +
                              PvpReport.ModeList(options.Modes) + "; no gear; no avatar");
            report.AppendLine("- Encounters: `" + EncounterLoader.RepoRelativePath + "`" +
                              " — simulator fixtures, not game content; enemy elements: " +
                              (options.EnemyElementOverride.HasValue ? "all overridden to `" + options.EnemyElementOverride.Value + "`" : "as authored") +
                              " (enemy kits are `None` in `neutral` mode, so an elementless encounter reads the same in both modes)");
            report.AppendLine("- Placement: each side takes the front-most tiles of its own deployment zone (front row first, then outward from");
            report.AppendLine("  the centre line); enemies in fixture order, the team through `PlacementValidator.TryPlaceAll`. Which team member");
            report.AppendLine("  gets which slot (and unit id, the initiative-tie and target-tie break within the team) is a fixed seeded shuffle per team.");
            report.AppendLine("- Initiative ties between the sides: `TurnManager` breaks equally full, equally fast gauges on the ordinal unit id,");
            report.AppendLine("  so each battle prefixes one side's ids so that it wins cross-side ties; in every (kit mode, encounter, level) cell");
            report.AppendLine("  exactly half the teams win them (a seeded shuffle of the team indices). The prefix is side-wide, so ties within a");
            report.AppendLine("  side are unchanged.");
            report.AppendLine("- Difficulty: HP, Atk, Def, SpA and SpD of every enemy are scaled by one multiplier per (kit mode, encounter,");
            report.AppendLine("  level); Speed and Move are not. Calibration starts at x1, doubles or halves until the " +
                              SimOptions.Format(options.TargetClearRate) + "% target is bracketed");
            report.AppendLine("  (x" + SimOptions.FormatMultiplier(SimOptions.MinMultiplier) + " to x" + SimOptions.FormatMultiplier(SimOptions.MaxMultiplier) +
                              "), then bisects " + SimOptions.CalibrationBisections + " times; the evaluated multiplier whose clear rate is closest to the");
            report.AppendLine("  target wins (first evaluated on a tie). Every metric below is measured at that multiplier.");
            report.AppendLine("- Movement rules are the Runtime's own (`BattleTurnExecutor`), with no simulator-side emulation: a defeated unit");
            report.AppendLine("  leaves the grid the moment it falls, and a unit that cannot reach range this turn makes a partial approach");
            report.AppendLine("  (walks its remaining move toward the target and holds the skill).");
            report.AppendLine("- Turn order: the Runtime's ATB gauge (`TurnManager`): every unit fills a gauge by its Speed and acts at " +
                              TurnManager.ActionThreshold + ", so twice the");
            report.AppendLine("  Speed is twice the turns. Battle time is normalized: 1.0 = one turn of a Speed-" + TurnManager.ReferenceSpeed +
                              " unit. Speed scales with level, so the");
            report.AppendLine("  same fight reads longer at low levels; compare times within a level, not across levels.");
            report.AppendLine("- Max time: " + options.MaxTime + " (a battle reaching it is a stalemate and counts as not cleared); base seed: " + options.Seed);
            report.AppendLine("- Metrics: **marginal** = clear rate of teams containing the beast minus teams without it (points; the primary");
            report.AppendLine("  number). **Dmg share** / **Taken share** = the beast's share of its team's damage dealt / taken (HP actually");
            report.AppendLine("  removed, so overkill is not counted), averaged over its teams. **Survival** = standing at the end. **Time to");
            report.AppendLine("  clear** = mean length of the clears it took part in (normalized time). **Turns / time** = the beast's turns per");
            report.AppendLine("  unit of time over its battles (Speed / 100 while standing). \"Overall\" averages every encounter and level equally.");
            report.AppendLine();
        }

        private static int TeamsWith(PveSimulator simulator)
        {
            int count = 0;
            foreach (int[] team in simulator.Teams)
            {
                count += Array.IndexOf(team, 0) >= 0 ? 1 : 0;
            }

            return count;
        }

        private static void AppendEncounters(StringBuilder report, List<Encounter> encounters)
        {
            report.AppendLine("### Encounters (simulator fixtures, not game content)");
            report.AppendLine();
            report.AppendLine("Base stats are max-level values scaled by the roster's growth curve, like a beast's, before the difficulty");
            report.AppendLine("multiplier. Kit entries are category, shape, range, power, cooldown; every enemy skill aims at the nearest beast.");
            report.AppendLine();
            report.AppendLine("| Encounter | Arena | Enemy | Count | Elements | HP | Atk | Def | SpA | SpD | Spe | Move | Kit |");
            report.AppendLine("| --- | --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

            foreach (Encounter encounter in encounters)
            {
                foreach (EnemyGroupData group in encounter.Data.Groups)
                {
                    List<string> elements = new List<string>();
                    foreach (EnemySlot slot in encounter.Enemies)
                    {
                        if (slot.GroupDisplayName == group.DisplayName)
                        {
                            elements.Add(slot.Element.ToString());
                        }
                    }

                    List<string> kit = new List<string>();
                    foreach (EnemySkillData skill in group.Skills)
                    {
                        kit.Add(skill.SkillId + " (" + skill.ParsedCategory + ", " + skill.ParsedShape + ", r" + skill.Range + ", p" +
                                skill.Power.ToString(CultureInfo.InvariantCulture) + ", cd" + skill.Cooldown + ")");
                    }

                    StatBlock s = group.BaseStats;
                    report.AppendLine("| `" + encounter.Id + "` | " + encounter.Data.ParsedArena + " | " + group.DisplayName + " | " + group.Count + " | " +
                                      Compress(elements) + " | " + s.Hp + " | " + s.Attack + " | " + s.Defense + " | " + s.SpecialAttack + " | " +
                                      s.SpecialDefense + " | " + s.Speed + " | " + s.MoveRange + " | " + string.Join("; ", kit) + " |");
                }
            }

            report.AppendLine();
        }

        /// <summary>"Fire x3, Water x2" in first-seen order.</summary>
        private static string Compress(List<string> names)
        {
            List<string> order = new List<string>();
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (!counts.ContainsKey(name))
                {
                    counts[name] = 0;
                    order.Add(name);
                }

                counts[name]++;
            }

            List<string> parts = new List<string>();
            foreach (string name in order)
            {
                parts.Add(counts[name] == 1 ? name : name + " x" + counts[name]);
            }

            return string.Join(", ", parts);
        }

        private static void AppendCalibration(StringBuilder report, SimOptions options, List<PveCell> cells)
        {
            report.AppendLine("### Calibrated difficulty");
            report.AppendLine();
            report.AppendLine("| Kit mode | Encounter | Level | Multiplier | Clear rate | Evaluations | Avg time | Stalemates | Lead enemy HP / Atk / Def |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

            foreach (PveCell cell in cells)
            {
                double time = 0.0;
                int stalemates = 0;
                foreach (PveBattle battle in cell.Battles)
                {
                    time += battle.Time;
                    stalemates += battle.Outcome == BattleOutcome.Stalemate ? 1 : 0;
                }

                EnemySlot lead = cell.Encounter.Enemies[0];
                StatBlock stats = PveSimulator.Scale(StatCalculator.ComputeStats(lead.Species, cell.Level, null), cell.Multiplier);
                string miss = Math.Abs(cell.ClearRate - options.TargetClearRate) > SimOptions.CalibrationTolerance ? " !" : string.Empty;

                report.AppendLine("| `" + SimOptions.ModeName(cell.Mode) + "` | `" + cell.Encounter.Id + "` | " + cell.Level + " | x" +
                                  SimOptions.FormatMultiplier(cell.Multiplier) + " | " + SimOptions.Format(cell.ClearRate) + "%" + miss + " | " +
                                  cell.Evaluations.Count + " | " + SimOptions.Format(time / cell.Battles.Length) + " | " + stalemates + " | " +
                                  stats.Hp + " / " + stats.Attack + " / " + stats.Defense + " |");
            }

            report.AppendLine();
            report.AppendLine("`!` = the closest clear rate calibration found is more than " + SimOptions.Format(SimOptions.CalibrationTolerance) +
                              " points off target (a step in the");
            report.AppendLine("clear-rate curve that no multiplier splits). Lead enemy = the encounter's first enemy, at that level and multiplier.");
            report.AppendLine();
        }

        private static void AppendParity(StringBuilder report, SimOptions options, List<Encounter> encounters, List<PveCell> cells)
        {
            report.AppendLine("### Kit parity (beast skill fires at calibrated difficulty, all levels)");
            report.AppendLine();
            report.AppendLine("Strike (Physical, range 1) fires less often than Blast (Special, range 3): it needs the beast to reach a free");
            report.AppendLine("tile next to its target. Strike's higher power compensates; **physical share** = Strike fires x Strike power as a");
            report.AppendLine("share of all single-target power delivered, and 50% means `Attack` and `SpecialAttack` weigh the same. Burst");
            report.AppendLine("uses count once per pair of halves; targets = enemies inside the radius when the first half fires.");
            report.AppendLine();
            report.AppendLine("| Kit mode | Encounter | Strike fires | Blast fires | Strike / Blast | Physical share | Burst uses | Avg targets per Burst |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (KitMode mode in options.Modes)
            {
                foreach (Encounter encounter in encounters)
                {
                    long strike = 0;
                    long blast = 0;
                    long burst = 0;
                    long burstTargets = 0;
                    foreach (int level in options.Levels)
                    {
                        foreach (PveBattle battle in Find(cells, mode, level, encounter).Battles)
                        {
                            strike += battle.StrikeFires;
                            blast += battle.BlastFires;
                            burst += battle.BurstFires;
                            burstTargets += battle.BurstTargets;
                        }
                    }

                    double physical = (double)strike * SimOptions.StrikePower;
                    double special = (double)blast * SimOptions.BlastPower;
                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | `" + encounter.Id + "` | " + strike + " | " + blast + " | " +
                                      (blast == 0 ? "-" : ((double)strike / blast).ToString("0.00", CultureInfo.InvariantCulture)) + " | " +
                                      (physical + special == 0.0 ? "-" : SimOptions.Format((100.0 * physical) / (physical + special)) + "%") + " | " + burst + " | " +
                                      (burst == 0 ? "-" : ((double)burstTargets / burst).ToString("0.00", CultureInfo.InvariantCulture)) + " |");
                }
            }

            report.AppendLine();
        }

        private static void AppendFlags(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<Encounter> encounters,
                                        List<PveCell> cells, List<ModeSummary> summaries)
        {
            report.AppendLine("### PvE flagged outliers");
            report.AppendLine();
            bool any = false;
            int band = Math.Min(SimOptions.NicheBand, species.Count);

            foreach (ModeSummary summary in summaries)
            {
                string mode = "`" + SimOptions.ModeName(summary.Mode) + "`";
                foreach (int b in summary.OverallOrder)
                {
                    double marginal = summary.Overall[b].Marginal;
                    if (Math.Abs(marginal) > options.MarginalThreshold)
                    {
                        report.AppendLine("- " + mode + " " + species[b].DisplayName + ": overall marginal " + SimOptions.Signed(marginal) +
                                          " points (" + (marginal > 0 ? "HIGH" : "LOW") + ", outside +/-" + SimOptions.Format(options.MarginalThreshold) + ")");
                        any = true;
                    }
                }

                if (encounters.Count > 1)
                {
                    for (int b = 0; b < species.Count; b++)
                    {
                        bool bottomEverywhere = true;
                        bool topEverywhere = true;
                        for (int e = 0; e < encounters.Count; e++)
                        {
                            int rank = summary.Ranking[e].IndexOf(b);
                            bottomEverywhere &= rank >= species.Count - band;
                            topEverywhere &= rank < band;
                        }

                        if (bottomEverywhere)
                        {
                            report.AppendLine("- " + mode + " " + species[b].DisplayName + ": NO NICHE (bottom " + band + " in every encounter)");
                            any = true;
                        }

                        if (topEverywhere)
                        {
                            report.AppendLine("- " + mode + " " + species[b].DisplayName + ": NO WEAKNESS (top " + band + " in every encounter)");
                            any = true;
                        }
                    }
                }
            }

            foreach (PveCell cell in cells)
            {
                int stalemates = 0;
                foreach (PveBattle battle in cell.Battles)
                {
                    stalemates += battle.Outcome == BattleOutcome.Stalemate ? 1 : 0;
                }

                string where = "`" + SimOptions.ModeName(cell.Mode) + "` `" + cell.Encounter.Id + "` L" + cell.Level;
                if (stalemates > 0)
                {
                    report.AppendLine("- " + where + ": " + stalemates + " of " + cell.Battles.Length + " battles stalemated at the calibrated difficulty");
                    any = true;
                }

                if (Math.Abs(cell.ClearRate - options.TargetClearRate) > SimOptions.CalibrationTolerance)
                {
                    report.AppendLine("- " + where + ": calibration miss, closest clear rate " + SimOptions.Format(cell.ClearRate) + "%");
                    any = true;
                }
            }

            if (!any)
            {
                report.AppendLine("None.");
            }

            report.AppendLine();
        }

        private static void AppendMode(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<Encounter> encounters,
                                       ModeSummary summary)
        {
            string name = SimOptions.ModeName(summary.Mode);
            report.AppendLine("### PvE mode: `" + name + "`");
            report.AppendLine();

            // Marginal by encounter.
            report.AppendLine("#### Marginal clear rate by encounter (levels averaged)");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Beast | Element |");
            StringBuilder rule = new StringBuilder("| --- | --- |");
            foreach (Encounter encounter in encounters)
            {
                header.Append(" `" + encounter.Id + "` |");
                rule.Append(" ---: |");
            }

            header.Append(" Overall |");
            rule.Append(" ---: |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            foreach (int b in summary.OverallOrder)
            {
                StringBuilder row = new StringBuilder("| " + species[b].DisplayName + " | " + PvpReport.ElementsOf(species[b]) + " |");
                for (int e = 0; e < encounters.Count; e++)
                {
                    row.Append(" " + Marked(options, summary.ByEncounter[e][b].Marginal) + " (" + (summary.Ranking[e].IndexOf(b) + 1) + ") |");
                }

                row.Append(" " + Marked(options, summary.Overall[b].Marginal) + " |");
                report.AppendLine(row.ToString());
            }

            report.AppendLine();
            report.AppendLine("Points of clear rate; (n) = rank within that encounter. Sorted by overall. **Bold** = above +" +
                              SimOptions.Format(options.MarginalThreshold) + ", _italic_ = below -" + SimOptions.Format(options.MarginalThreshold) + ".");
            report.AppendLine();

            // Marginal by encounter and level.
            report.AppendLine("#### Marginal clear rate by encounter and level");
            report.AppendLine();
            header = new StringBuilder("| Beast |");
            rule = new StringBuilder("| --- |");
            foreach (Encounter encounter in encounters)
            {
                foreach (int level in options.Levels)
                {
                    header.Append(" `" + encounter.Id + "` L" + level + " |");
                    rule.Append(" ---: |");
                }
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            foreach (int b in summary.OverallOrder)
            {
                StringBuilder row = new StringBuilder("| " + species[b].DisplayName + " |");
                for (int e = 0; e < encounters.Count; e++)
                {
                    for (int l = 0; l < options.Levels.Count; l++)
                    {
                        row.Append(" " + Marked(options, summary.ByCell[e][l][b].Marginal) + " |");
                    }
                }

                report.AppendLine(row.ToString());
            }

            report.AppendLine();

            // Niches.
            report.AppendLine("#### Ranking per encounter (niches)");
            report.AppendLine();
            header = new StringBuilder("| Rank |");
            rule = new StringBuilder("| ---: |");
            foreach (Encounter encounter in encounters)
            {
                header.Append(" `" + encounter.Id + "` |");
                rule.Append(" --- |");
            }

            header.Append(" Overall |");
            rule.Append(" --- |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            for (int r = 0; r < species.Count; r++)
            {
                StringBuilder row = new StringBuilder("| " + (r + 1) + " |");
                for (int e = 0; e < encounters.Count; e++)
                {
                    int b = summary.Ranking[e][r];
                    row.Append(" " + species[b].DisplayName + " " + SimOptions.Signed(summary.ByEncounter[e][b].Marginal) + " |");
                }

                int o = summary.OverallOrder[r];
                row.Append(" " + species[o].DisplayName + " " + SimOptions.Signed(summary.Overall[o].Marginal) + " |");
                report.AppendLine(row.ToString());
            }

            report.AppendLine();

            // Role metrics.
            for (int e = 0; e < encounters.Count; e++)
            {
                report.AppendLine("#### Role metrics: `" + encounters[e].Id + "` (levels averaged)");
                report.AppendLine();
                report.AppendLine("| Beast | Marginal | Clear with | Clear without | Dmg share | Taken share | Survival | Time to clear | Turns / time |");
                report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                foreach (int b in summary.Ranking[e])
                {
                    BeastMetrics m = summary.ByEncounter[e][b];
                    report.AppendLine("| " + species[b].DisplayName + " | " + Marked(options, m.Marginal) + " | " + SimOptions.Format(m.ClearWith) + "% | " +
                                      SimOptions.Format(m.ClearWithout) + "% | " + SimOptions.Format(m.DamageShare) + "% | " +
                                      SimOptions.Format(m.TakenShare) + "% | " + SimOptions.Format(m.Survival) + "% | " +
                                      (double.IsNaN(m.TimeToClear) ? "-" : SimOptions.Format(m.TimeToClear)) + " | " +
                                      (double.IsNaN(m.TurnsPerTime) ? "-" : m.TurnsPerTime.ToString("0.00", CultureInfo.InvariantCulture)) + " |");
                }

                report.AppendLine();
            }

            report.AppendLine("An even split is " + SimOptions.Format(100.0 / options.TeamSize) + "% for both shares.");
            report.AppendLine();
        }

        private static string Marked(SimOptions options, double marginal)
        {
            string text = SimOptions.Signed(marginal);
            if (marginal > options.MarginalThreshold)
            {
                return "**" + text + "**";
            }

            return marginal < -options.MarginalThreshold ? "_" + text + "_" : text;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }
    }
}
