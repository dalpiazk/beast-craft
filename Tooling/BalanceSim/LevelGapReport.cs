using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// "PvE level gap" (<c>--level-gap</c>): every cell's clear rate with the enemies some levels
    /// above (or below) the team, at the multiplier calibrated at equal levels, scouted and not,
    /// against the level-gap targets (<see cref="SimOptions.LevelGapNearLow"/> and friends). The
    /// same tables over several seeds for <c>--seeds</c>. Pure: output depends only on the cells.
    /// </summary>
    public static class LevelGapReport
    {
        /// <summary>The single-seed section; nothing without <c>--level-gap</c>.</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, List<PveCell> cells)
        {
            if (options.LevelGaps == null)
            {
                return;
            }

            report.AppendLine("### PvE level gap");
            report.AppendLine();
            Append(report, options, new List<List<PveCell>> { cells }, "####");
        }

        /// <summary>The <c>--seeds</c> section: every number is the mean over seeds of the single-seed one.</summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, List<int> seeds, List<List<PveCell>> cells)
        {
            if (options.LevelGaps == null)
            {
                return;
            }

            report.AppendLine("## PvE level gap over seeds");
            report.AppendLine();
            report.AppendLine("Every number is the mean over the " + seeds.Count + " seeds (" + SimOptions.Join(seeds) + ") of that seed's \"PvE level gap\" number.");
            report.AppendLine();
            Append(report, options, cells, "###");
        }

        /// <summary>The target a scouted rate at <paramref name="gap"/> is held to, in words; null when the gap has none.</summary>
        public static string TargetText(SimOptions options, int gap)
        {
            if (gap == 0)
            {
                return SimOptions.Format(options.TargetClearRate - SimOptions.LevelGapEvenTolerance) + "-" +
                       SimOptions.Format(options.TargetClearRate + SimOptions.LevelGapEvenTolerance) + "%";
            }

            if (gap >= SimOptions.LevelGapNearMin && gap <= SimOptions.LevelGapNearMax)
            {
                return SimOptions.Format(SimOptions.LevelGapNearLow) + "-" + SimOptions.Format(SimOptions.LevelGapNearHigh) + "%";
            }

            return gap >= SimOptions.LevelGapFarMin ? "under " + SimOptions.Format(SimOptions.LevelGapFarHigh) + "%" : null;
        }

        /// <summary>Whether a scouted rate at <paramref name="gap"/> meets its target; true when the gap has none.</summary>
        public static bool MeetsTarget(SimOptions options, int gap, double scouted)
        {
            if (gap == 0)
            {
                return Math.Abs(scouted - options.TargetClearRate) <= SimOptions.LevelGapEvenTolerance;
            }

            if (gap >= SimOptions.LevelGapNearMin && gap <= SimOptions.LevelGapNearMax)
            {
                return scouted >= SimOptions.LevelGapNearLow && scouted <= SimOptions.LevelGapNearHigh;
            }

            return gap < SimOptions.LevelGapFarMin || scouted < SimOptions.LevelGapFarHigh;
        }

        private static void Append(StringBuilder report, SimOptions options, List<List<PveCell>> seeds, string heading)
        {
            List<int> gaps = options.LevelGaps;
            List<PveCell> first = seeds[0];
            bool scouted = options.CalibratesOnPick;
            int samples = first.Count == 0 ? 0 : first[0].CalibrationSamples;
            int teams = first.Count == 0 ? 0 : first[0].TeamCount;
            int noScoutTeams = Math.Min(options.LevelGapTeams, teams);

            report.AppendLine("Every cell replayed with the enemies `g` levels above the team (negative = below), at the difficulty multiplier");
            report.AppendLine("calibrated at equal levels (`--level-gap`). The team's beasts" + (options.AvatarLevel > 0 ? string.Empty : " and the avatar") +
                              " stay at the row's level; the enemies' stats follow their growth curve");
            report.AppendLine("to their own level, and every hit (damage over time included) carries the damage formula's level-difference multiplier");
            report.AppendLine("`clamp(1 + k d + q d |d|, 1 - cap, 1 + cap)`, d = attacker level - defender level, k = " +
                              DamageFormula.LevelDifferencePerLevel.ToString("0.###", CultureInfo.InvariantCulture) + ", q = " +
                              DamageFormula.LevelDifferenceConvex.ToString("0.####", CultureInfo.InvariantCulture) + ", cap = " +
                              DamageFormula.LevelDifferenceCap.ToString("0.###", CultureInfo.InvariantCulture) + ". Each battle's seed");
            report.AppendLine("ignores the gap, so gap 0 is the calibration itself and every gap replays the same damage-roll streams.");
            report.AppendLine();
            report.AppendLine("Cell = " + (scouted
                                  ? "**scouted** clear % (the team the " + PveReport.PickerName(options) + " fields, " + samples + " battles per composition) "
                                  : string.Empty) +
                              "(**no scouting** %: " + noScoutTeams + " of " + teams + " teams (`--level-gap-teams`) x every composition).");
            report.AppendLine("— = the enemies would be outside 1-" + SimOptions.MaxLevel + ". Targets for the scouted rate: gap 0 " + TargetText(options, 0) + "; +" + SimOptions.LevelGapNearMin + " to +" +
                              SimOptions.LevelGapNearMax + " " + TargetText(options, SimOptions.LevelGapNearMin) + "; +" + SimOptions.LevelGapFarMin + " and beyond " +
                              TargetText(options, SimOptions.LevelGapFarMin) + ". `!` = outside its target" + (scouted ? "." : " (not flagged: `--calibrate-on mean` has no scouted rate)."));
            report.AppendLine("**All shapes** = the mean of the shape rows.");
            report.AppendLine();

            List<string> shapeIds = new List<string>();
            foreach (PveCell cell in first)
            {
                if (!shapeIds.Contains(cell.Shape.Id))
                {
                    shapeIds.Add(cell.Shape.Id);
                }
            }

            foreach (KitMode mode in options.Modes)
            {
                report.AppendLine(heading + " Level gap: `" + SimOptions.ModeName(mode) + "`");
                report.AppendLine();
                StringBuilder header = new StringBuilder("| Shape | Level |");
                StringBuilder rule = new StringBuilder("| --- | ---: |");
                foreach (int gap in gaps)
                {
                    header.Append(' ').Append(gap > 0 ? "+" + gap.ToString(CultureInfo.InvariantCulture) : gap.ToString(CultureInfo.InvariantCulture)).Append(" |");
                    rule.Append(" ---: |");
                }

                report.AppendLine(header.ToString());
                report.AppendLine(rule.ToString());

                int met = 0;
                int flagged = 0;
                List<string> misses = new List<string>();
                foreach (string shapeId in shapeIds)
                {
                    foreach (int level in options.Levels)
                    {
                        StringBuilder row = new StringBuilder("| `" + shapeId + "` | " + level + " |");
                        for (int g = 0; g < gaps.Count; g++)
                        {
                            Mean(seeds, mode, new List<string> { shapeId }, level, g, out double s, out double n, out bool inRange);
                            row.Append(' ').Append(CellText(options, gaps[g], inRange, s, n, scouted, ref met, ref flagged)).Append(" |");
                        }

                        report.AppendLine(row.ToString());
                    }
                }

                int allMet = 0;
                int allFlagged = 0;
                foreach (int level in options.Levels)
                {
                    StringBuilder row = new StringBuilder("| **All shapes** | " + level + " |");
                    for (int g = 0; g < gaps.Count; g++)
                    {
                        Mean(seeds, mode, shapeIds, level, g, out double s, out double n, out bool inRange);
                        int before = allMet;
                        int beforeFlagged = allFlagged;
                        row.Append(' ').Append(CellText(options, gaps[g], inRange, s, n, scouted, ref allMet, ref allFlagged)).Append(" |");
                        if (allFlagged > beforeFlagged && allMet == before)
                        {
                            misses.Add("L" + level + " " + (gaps[g] > 0 ? "+" : string.Empty) + gaps[g] + ": " + SimOptions.Format(s) + "% (target " + TargetText(options, gaps[g]) + ")");
                        }
                    }

                    report.AppendLine(row.ToString());
                }

                report.AppendLine();
                if (scouted)
                {
                    report.AppendLine("Targets met: " + allMet + " of " + allFlagged + " **All shapes** cells with a target; " + met + " of " + flagged +
                                      " shape cells." + (misses.Count == 0 ? string.Empty : " Missed (all shapes): " + string.Join("; ", misses) + "."));
                    report.AppendLine();
                }
            }
        }

        private static string CellText(SimOptions options, int gap, bool inRange, double scoutedRate, double noScoutRate, bool scouted, ref int met, ref int flagged)
        {
            if (!inRange)
            {
                return "—";
            }

            string none = "(" + SimOptions.Format(noScoutRate) + ")";
            if (!scouted || double.IsNaN(scoutedRate))
            {
                return none;
            }

            string mark = string.Empty;
            if (TargetText(options, gap) != null)
            {
                flagged++;
                if (MeetsTarget(options, gap, scoutedRate))
                {
                    met++;
                }
                else
                {
                    mark = " !";
                }
            }

            return SimOptions.Format(scoutedRate) + " " + none + mark;
        }

        /// <summary>
        /// The mean over seeds, then over <paramref name="shapeIds"/>, of the scouted and no-scouting
        /// rates at gap index <paramref name="gapIndex"/>; <paramref name="inRange"/> false (and NaN
        /// rates) when no such cell was run in range.
        /// </summary>
        private static void Mean(List<List<PveCell>> seeds, KitMode mode, List<string> shapeIds, int level, int gapIndex, out double scouted, out double noScout,
                                 out bool inRange)
        {
            double scoutedSum = 0.0;
            double noScoutSum = 0.0;
            int count = 0;
            foreach (List<PveCell> cells in seeds)
            {
                foreach (PveCell cell in cells)
                {
                    if (cell.Mode != mode || cell.Level != level || !shapeIds.Contains(cell.Shape.Id) || cell.LevelGaps == null)
                    {
                        continue;
                    }

                    LevelGapPoint point = cell.LevelGaps[gapIndex];
                    if (!point.InRange)
                    {
                        continue;
                    }

                    scoutedSum += point.ScoutedClearRate;
                    noScoutSum += point.NoScoutClearRate;
                    count++;
                }
            }

            inRange = count > 0;
            scouted = inRange ? scoutedSum / count : double.NaN;
            noScout = inRange ? noScoutSum / count : double.NaN;
        }
    }
}
