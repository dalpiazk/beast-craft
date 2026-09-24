using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Skills;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// One beast's numbers in one (mode, level, shape) cell, or averaged over several. Every
    /// sample of every team against every composition counts as its own battle.
    /// </summary>
    public class BeastMetrics
    {
        /// <summary>Clear rate of teams containing the beast minus teams without it, in points.</summary>
        public double Marginal;

        /// <summary>
        /// <see cref="Marginal"/> x <see cref="PveReport.NormalizationFactor"/> of its cell: the marginal
        /// rescaled to what it would be at a 50% cell clear rate, so cells calibrated off 50% for
        /// the average team (a scouted-pick calibration) weigh like the rest. Averaged like the raw one.
        /// </summary>
        public double NormalizedMarginal;
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
        /// time): about sqrt(Speed / 100) while it stands (the ATB gauge fills with the square root
        /// of Speed), less when it falls early. NaN with no time.
        /// </summary>
        public double TurnsPerTime = double.NaN;
    }

    /// <summary>
    /// Turns <see cref="PveCell"/>s into the report's primary "PvE" section. Pure: output depends only
    /// on its inputs, so two runs are byte-identical. Every average is taken in a fixed order.
    /// </summary>
    public static class PveReport
    {
        /// <summary>A physical share further than this from 50% is flagged as a kit parity miss.</summary>
        public const double ParityTolerance = 5.0;

        /// <summary>Per-beast metrics for one cell, over its balance battles (<see cref="PveCell.BalanceBattles"/>: the level-gap mix when it is on).</summary>
        public static BeastMetrics[] Compute(PveCell cell, List<int[]> teams, int speciesCount)
        {
            return Compute(cell, cell.BalanceBattles, teams, speciesCount);
        }

        /// <summary>Per-beast metrics for one cell over <paramref name="battles"/> (the cell's layout: <see cref="PveCell.Battles"/> or <see cref="PveCell.MixBattles"/>).</summary>
        public static BeastMetrics[] Compute(PveCell cell, PveBattle[] battles, List<int[]> teams, int speciesCount)
        {
            double normalization = NormalizationFactor(battles);
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

                for (int i = 0; i < battles.Length; i++)
                {
                    PveBattle battle = battles[i];
                    int member = Array.IndexOf(teams[cell.TeamOf(i)], b);
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
                m.NormalizedMarginal = m.Marginal * normalization;
                metrics[b] = m;
            }

            return metrics;
        }

        /// <summary>
        /// <c>0.25 / (p (1 - p))</c> for the mean clear rate p over every battle of
        /// <paramref name="battles"/> (a cell's every-team run: at gap 0 the no-scouting rate,
        /// <see cref="PveCell.ClearRate"/>): a beast's marginal scales with the binomial variance
        /// p (1 - p), largest at 50%, so multiplying by this rescales it to a 50% cell. 1 at p = 50%;
        /// 0 when p is 0 or 100% (every marginal is 0 there anyway).
        /// </summary>
        public static double NormalizationFactor(PveBattle[] battles)
        {
            int cleared = 0;
            foreach (PveBattle battle in battles)
            {
                cleared += battle.Cleared ? 1 : 0;
            }

            double p = battles.Length == 0 ? 0.0 : (double)cleared / battles.Length;
            return p <= 0.0 || p >= 1.0 ? 0.0 : 0.25 / (p * (1.0 - p));
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
                average.NormalizedMarginal += m.NormalizedMarginal;
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
            average.NormalizedMarginal /= n;
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

            /// <summary>[shape][level][beast].</summary>
            public BeastMetrics[][][] ByCell;

            /// <summary>[shape][beast], levels averaged.</summary>
            public BeastMetrics[][] ByShape;

            /// <summary>[beast], every shape and level averaged.</summary>
            public BeastMetrics[] Overall;

            /// <summary>[shape] beast indices, best marginal first.</summary>
            public List<int>[] Ranking;

            public List<int> OverallOrder;

            /// <summary>With the level-gap mix on: the same summary over the gap-0 battles (null otherwise).</summary>
            public ModeSummary Gap0;
        }

        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, EncounterCatalog catalog,
                                         PveSimulator simulator, List<PveCell> cells)
        {
            List<EncounterShape> shapes = catalog.Shapes;
            List<ModeSummary> summaries = new List<ModeSummary>();
            foreach (KitMode mode in options.Modes)
            {
                ModeSummary summary = Summarize(options, species, shapes, simulator, cells, mode, false);
                summary.Gap0 = options.GapMixActive ? Summarize(options, species, shapes, simulator, cells, mode, true) : null;
                summaries.Add(summary);
            }

            bool generated = catalog.Set == EncounterSet.Generated;
            report.AppendLine("## PvE: team vs encounter (primary)");
            report.AppendLine();
            report.AppendLine("The expected shape of the game: the player fields a team of beasts against enemies that are not roster beasts,");
            report.AppendLine("from one huge creature to a couple of dozen small ones. " +
                              (generated
                                  ? "Encounters are generated: for each shape (solo giant, elite, squad, horde) the"
                                  : "Encounters are the fixed set (`--encounter-set fixed`): every"));
            if (options.CalibratesOnPick)
            {
                if (generated)
                {
                    report.AppendLine("simulator draws " + options.Compositions + " random compositions of mixed enemy types with varied elements; every team fights every");
                    report.AppendLine("composition. The player is assumed to scout: each shape's difficulty is calibrated so the team the " + PickerName(options));
                    report.AppendLine("fields against each composition clears it about " + options.TargetSummary(shapes) +
                                      " of the time (`--calibrate-on " + SimOptions.CalibrationName(options.EffectiveCalibrateOn) + "`), and each beast");
                }
                else
                {
                    report.AppendLine("team is fielded against every encounter. The player is assumed to scout: each encounter's difficulty is calibrated");
                    report.AppendLine("so the team the " + PickerName(options) + " fields against it clears it about " + options.TargetSummary(shapes) +
                                      " of the time (`--calibrate-on " + SimOptions.CalibrationName(options.EffectiveCalibrateOn) + "`), and each beast");
                }

                report.AppendLine("is judged by how much it moves the clear rate of every team it is in. The average team clears less than that (the");
                report.AppendLine("no-scouting rate, see \"Calibrated difficulty\"), where marginals shrink, so they are also given normalized to a 50% cell.");
            }
            else if (generated)
            {
                report.AppendLine("simulator draws " + options.Compositions + " random compositions of mixed enemy types with varied elements; every team fights every");
                report.AppendLine("composition, each shape's difficulty is calibrated so the average team clears its compositions about " +
                                  options.TargetSummary(shapes) + " of the");
                report.AppendLine("time (where a beast's presence moves the outcome most), and each beast is judged by how much it moves its team's");
                report.AppendLine("clear rate.");
            }
            else
            {
                report.AppendLine("team is fielded against every encounter, each encounter's difficulty is calibrated so the average team clears it");
                report.AppendLine("about " + options.TargetSummary(shapes) + " of the time (where a beast's presence moves the outcome most), and each beast is judged");
                report.AppendLine("by how much it moves its team's clear rate.");
            }

            report.AppendLine();

            AppendConfiguration(report, options, simulator, catalog);
            if (generated)
            {
                AppendTypes(report, catalog);
                AppendShapes(report, catalog);
                AppendCompositions(report, options, catalog, cells);
                AppendElementDistribution(report, catalog);
            }
            else
            {
                AppendFixedEncounters(report, catalog);
            }

            AppendCalibration(report, options, species, simulator, cells);
            AppendGapMix(report, options, shapes, cells);
            LevelGapReport.AppendSection(report, options, cells);
            AvatarValueReport.AppendSection(report, options, species, simulator, cells);
            if (options.KitSource == KitSource.Standard)
            {
                // The parity table measures the standard kit's Strike / Shot / Blast balance; library kits differ by design.
                AppendParity(report, options, species, simulator, shapes, cells);
            }

            AppendRolls(report, species, simulator, cells);
            AppendFlags(report, options, species, simulator, shapes, cells, summaries);

            foreach (ModeSummary summary in summaries)
            {
                AppendMode(report, options, species, shapes, summary);
                if (summary.Mode == KitMode.Elemental)
                {
                    AppendElementMatchups(report, options, species, simulator, cells);
                }
            }

            TeamReport.AppendSection(report, options, species, shapes, simulator, cells);
            PanelReport.AppendSection(report, options, species, shapes, simulator);
            BondReport.AppendSection(report, options, species, shapes, simulator, cells);
            ScoutingReport.AppendSection(report, options, species, shapes, simulator, cells);
        }

        /// <summary>One kit mode's per-beast metrics over the balance battles, or with <paramref name="gap0"/> over the gap-0 battles.</summary>
        private static ModeSummary Summarize(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                             PveSimulator simulator, List<PveCell> cells, KitMode mode, bool gap0)
        {
            ModeSummary summary = new ModeSummary
            {
                Mode = mode,
                ByCell = new BeastMetrics[shapes.Count][][],
                ByShape = new BeastMetrics[shapes.Count][],
                Overall = new BeastMetrics[species.Count],
                Ranking = new List<int>[shapes.Count]
            };

            for (int e = 0; e < shapes.Count; e++)
            {
                summary.ByCell[e] = new BeastMetrics[options.Levels.Count][];
                summary.ByShape[e] = new BeastMetrics[species.Count];
                for (int l = 0; l < options.Levels.Count; l++)
                {
                    PveCell cell = Find(cells, mode, options.Levels[l], shapes[e]);
                    summary.ByCell[e][l] = Compute(cell, gap0 ? cell.Battles : cell.BalanceBattles, simulator.Teams, species.Count);
                }
            }

            for (int b = 0; b < species.Count; b++)
            {
                List<BeastMetrics> all = new List<BeastMetrics>();
                for (int e = 0; e < shapes.Count; e++)
                {
                    List<BeastMetrics> levels = new List<BeastMetrics>();
                    for (int l = 0; l < options.Levels.Count; l++)
                    {
                        levels.Add(summary.ByCell[e][l][b]);
                        all.Add(summary.ByCell[e][l][b]);
                    }

                    summary.ByShape[e][b] = Average(levels);
                }

                summary.Overall[b] = Average(all);
            }

            for (int e = 0; e < shapes.Count; e++)
            {
                BeastMetrics[] row = summary.ByShape[e];
                summary.Ranking[e] = Order(species.Count, b => row[b].Marginal);
            }

            summary.OverallOrder = Order(species.Count, b => summary.Overall[b].Marginal);
            return summary;
        }

        /// <summary>
        /// One kit mode's marginal clear rates exactly as the "Marginal clear rate by shape" table
        /// shows them: <paramref name="byShape"/>[shape][beast] (levels averaged) and
        /// <paramref name="overall"/>[beast], and the normalized overall marginal
        /// (<see cref="BeastMetrics.NormalizedMarginal"/>) <paramref name="normalizedOverall"/>[beast].
        /// Used by <see cref="SeedAggregate"/>. Over the balance battles (the level-gap mix when it is on),
        /// or with <paramref name="gap0"/> over the gap-0 battles.
        /// </summary>
        public static void Marginals(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes, PveSimulator simulator,
                                     List<PveCell> cells, KitMode mode, bool gap0, out double[][] byShape, out double[] overall, out double[] normalizedOverall)
        {
            ModeSummary summary = Summarize(options, species, shapes, simulator, cells, mode, gap0);
            byShape = new double[shapes.Count][];
            for (int e = 0; e < shapes.Count; e++)
            {
                byShape[e] = new double[species.Count];
                for (int b = 0; b < species.Count; b++)
                {
                    byShape[e][b] = summary.ByShape[e][b].Marginal;
                }
            }

            overall = new double[species.Count];
            normalizedOverall = new double[species.Count];
            for (int b = 0; b < species.Count; b++)
            {
                overall[b] = summary.Overall[b].Marginal;
                normalizedOverall[b] = summary.Overall[b].NormalizedMarginal;
            }
        }

        /// <summary>Beast indices, highest value first; ties in roster order.</summary>
        public static List<int> Order(int count, Func<int, double> value)
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

        public static PveCell Find(List<PveCell> cells, KitMode mode, int level, EncounterShape shape)
        {
            foreach (PveCell cell in cells)
            {
                if (cell.Mode == mode && cell.Level == level && cell.Shape == shape)
                {
                    return cell;
                }
            }

            throw new InvalidOperationException("Missing PvE cell " + mode + "/" + level + "/" + shape.Id + ".");
        }

        private static void AppendConfiguration(StringBuilder report, SimOptions options, PveSimulator simulator, EncounterCatalog catalog)
        {
            bool generated = catalog.Set == EncounterSet.Generated;
            int compositions = catalog.AllCompositions.Count;
            report.AppendLine("### PvE configuration");
            report.AppendLine();
            report.AppendLine("- Teams: every combination of " + options.TeamSize + " distinct beasts (" + simulator.Teams.Count + " teams, format " +
                              SimOptions.FormatForTeamSize(options.TeamSize) + "); each beast is in " + TeamsWith(simulator) + " of them");
            if (generated)
            {
                report.AppendLine("- Encounters: generated (`--encounter-set generated`, the default): " + catalog.Shapes.Count + " shapes x " +
                                  options.Compositions + " compositions (`--compositions`) = " + compositions + " compositions, drawn from the");
                report.AppendLine("  enemy library `" + EncounterLoader.EnemyLibraryRepoRelativePath + "` per shape of `" + EncounterLoader.EncounterLibraryRepoRelativePath +
                                  "` (game content) by a generator seeded from");
                report.AppendLine("  `--seed` alone. Each draw picks a shape variant, a count per slot and each unit's type, and is kept only inside");
                report.AppendLine("  the shape's threat budget and with enough distinct types; each composition's element scheme is drawn too (one");
                report.AppendLine("  element for the whole team " + SchemeShare(catalog, ElementScheme.Uniform) + "%, one per type " + SchemeShare(catalog, ElementScheme.PerType) +
                                  "%, one per unit " + SchemeShare(catalog, ElementScheme.PerUnit) + "%, none " + SchemeShare(catalog, ElementScheme.None) +
                                  "%), with elements dealt from a shuffled");
                report.AppendLine("  deck of the ten so every element is dealt before any repeats. Enemy elements: " +
                                  (options.EnemyElementOverride.HasValue ? "all overridden to `" + options.EnemyElementOverride.Value + "`" : "as generated") +
                                  " (enemy kits are `None` in `neutral` mode).");
            }
            else
            {
                report.AppendLine("- Encounters: the fixed set (`--encounter-set fixed`) in `" + EncounterLoader.FixedRepoRelativePath + "`" +
                                  " — simulator fixtures, not game content; enemy elements: " +
                                  (options.EnemyElementOverride.HasValue ? "all overridden to `" + options.EnemyElementOverride.Value + "`" : "as authored") +
                                  " (enemy kits are `None` in `neutral` mode, so an elementless encounter reads the same in both modes)");
            }

            int perCell = compositions == 0 ? 0 : simulator.Teams.Count * simulator.Samples;
            if (options.CalibratesOnPick)
            {
                int perStep = catalog.Shapes.Count == 0 ? 0 : catalog.Shapes[0].Compositions.Count * options.CalibrateSamples;
                report.AppendLine("- Samples: damage variance (" + DamageFormula.VarianceMinPercent + "-" + DamageFormula.VarianceMaxPercent +
                                  "%) and crits make a battle random. At each calibration step the picked team fights each " +
                                  (generated ? "composition" : "encounter"));
                report.AppendLine("  " + options.CalibrateSamples + " times with distinct seeds (`--calibrate-samples`; " + perStep + " battles per step" +
                                  (generated ? " for a shape of " + catalog.Shapes[0].Compositions.Count + " compositions" : string.Empty) +
                                  "); at the chosen multiplier every team");
                report.AppendLine("  fights every " + (generated ? "composition " : "encounter ") + simulator.Samples + " time(s) (`--samples`; " + perCell + " battles per " +
                                  (generated ? "composition" : "encounter") + "). Seeds exclude the multiplier, so calibration");
                report.AppendLine("  compares multipliers on the same rolls, and the picked team's first sample is its battle in the all-teams run.");
            }
            else
            {
                report.AppendLine("- Samples: damage variance (" + DamageFormula.VarianceMinPercent + "-" + DamageFormula.VarianceMaxPercent +
                                  "%) and crits make a battle random, so every team fights every " + (generated ? "composition " : "encounter ") +
                                  simulator.Samples + " time(s) per calibration step");
                report.AppendLine("  with distinct seeds (`--samples`; " + perCell + " battles per " + (generated ? "composition" : "encounter") +
                                  " per evaluation). Seeds exclude the multiplier, so calibration compares");
                report.AppendLine("  multipliers on the same rolls; the clear rate is over every battle of the " + (generated ? "shape" : "encounter") + ".");
            }

            if (simulator.CalibrationTeams != null)
            {
                report.AppendLine("- Calibration sample (`--calibrate-sample " + simulator.CalibrationTeams.Length + "`): the multiplier search evaluated a seeded subset of " +
                                  simulator.CalibrationTeams.Length + " of the " + simulator.Teams.Count + " teams;");
                report.AppendLine("  the chosen multiplier was then run once with every team, and every number below comes from that full run (the");
                report.AppendLine("  \"Evaluations\" column counts it). Multipliers, and so every number, differ slightly from a run without the option.");
            }
            report.AppendLine("- Levels: " + SimOptions.Join(options.Levels) + " (beasts and enemies at the same level); kit modes: " +
                              PvpReport.ModeList(options.Modes) + "; no gear; no avatar");
            report.AppendLine("- Placement: each side takes the front-most tiles of its own deployment zone (front row first, then outward from");
            report.AppendLine("  the centre line); enemies front-to-back in composition order (Vanguard, then Skirmisher, then Ranged types), the");
            report.AppendLine("  team through `PlacementValidator.TryPlaceAll`. Which team member gets which slot (and unit id, the initiative-tie");
            report.AppendLine("  and target-tie break within the team) is a fixed seeded shuffle per team. Large enemies (the giant and the colossus");
            report.AppendLine("  cover 7 tiles, the champion 3) take the front-most anchor where their whole footprint fits the zone");
            report.AppendLine("  (`DeploymentPacker`); every range to or from them is measured between nearest tiles.");
            report.AppendLine("- Initiative ties between the sides: `TurnManager` breaks equally full, equally fast gauges on the ordinal unit id,");
            report.AppendLine("  so each battle prefixes one side's ids so that it wins cross-side ties; against every composition, at every kit");
            report.AppendLine("  mode and level, exactly half the teams win them (a seeded shuffle of the team indices). The prefix is side-wide,");
            report.AppendLine("  so ties within a side are unchanged.");
            report.AppendLine("- Difficulty: HP, Atk, Def, SpA and SpD of every enemy are scaled by one multiplier per (kit mode, " +
                              (generated ? "shape" : "encounter") + ", level)" + (generated ? ", shared by all" : string.Empty));
            report.AppendLine("  " + (generated ? "of the shape's compositions; " : string.Empty) + "Speed and Move are not. Calibration starts at x1, doubles or halves until the " +
                              "target (" + options.TargetSummary(catalog.Shapes) + ") is bracketed");
            report.AppendLine("  (x" + SimOptions.FormatMultiplier(SimOptions.MinMultiplier) + " to x" + SimOptions.FormatMultiplier(SimOptions.MaxMultiplier) +
                              "), then bisects " + SimOptions.CalibrationBisections + " times; the evaluated multiplier whose " +
                              (options.CalibratesOnPick ? "picked-team " : string.Empty) + "clear rate is closest to the");
            report.AppendLine("  target wins (first evaluated on a tie). Every metric below is measured at that multiplier" +
                              (options.CalibratesOnPick ? ", over every team." : "."));
            report.AppendLine("- Movement rules are the Runtime's own (`BattleTurnExecutor`), with no simulator-side emulation: a defeated unit");
            report.AppendLine("  leaves the grid the moment it falls, and a unit that cannot reach range this turn makes a partial approach");
            report.AppendLine("  (walks its remaining move toward the target and holds the skill).");
            report.AppendLine("- Combat stances are the Runtime's too (`CombatStance`, from the roster and the enemies): a Vanguard approaches");
            report.AppendLine("  as above and prefers stop tiles that screen its Ranged / Skirmisher allies; a Ranged unit never walks into melee");
            report.AppendLine("  (so Ranged beasts carry Shot, range 3, instead of Strike); Ranged and Skirmisher units prefer stop tiles with");
            report.AppendLine("  fewer adjacent enemies and spend leftover movement backing away, keeping the nearest enemy within their longest reach.");
            report.AppendLine("- Turn order: the Runtime's ATB gauge (`TurnManager`): every unit fills a gauge by round(" + TurnManager.FillScale +
                              " x sqrt(Speed)) per tick and acts at " + TurnManager.ActionThreshold + ",");
            report.AppendLine("  so turns grow with the square root of Speed (four times the Speed is twice the turns). Battle time is normalized:");
            report.AppendLine("  1.0 = one turn of a Speed-" + TurnManager.ReferenceSpeed + " unit. Speed scales with level, so the");
            report.AppendLine("  same fight reads longer at low levels; compare times within a level, not across levels.");
            report.AppendLine("- Max time: " + options.MaxTime + " (a battle reaching it is a stalemate and counts as not cleared); base seed: " + options.Seed);
            report.AppendLine("- Metrics: **marginal** = clear rate of teams containing the beast minus teams without it (points; the primary");
            report.AppendLine("  number). **Dmg share** / **Taken share** = the beast's share of its team's damage dealt / taken (HP actually");
            report.AppendLine("  removed, so overkill is not counted), averaged over its battles. **Survival** = standing at the end. **Time to");
            report.AppendLine("  clear** = mean length of the clears it took part in (normalized time). **Turns / time** = the beast's turns per");
            report.AppendLine("  unit of time over its battles (sqrt(Speed / 100) while standing). \"Overall\" averages every " + (generated ? "shape" : "encounter") +
                              " and level equally.");
            if (options.CalibratesOnPick)
            {
                report.AppendLine("  **Normalized** marginal = marginal x 0.25 / (p (1 - p)) per cell, p = the cell's no-scouting clear rate: a");
                report.AppendLine("  marginal scales with p (1 - p), so this is the marginal the cell would show at 50%, then averaged the same way. The");
                report.AppendLine("  flags and the multi-seed balance guard (+/-" + SimOptions.Format(SimOptions.GuardElemental) + " `elemental`, +/-" +
                                  SimOptions.Format(SimOptions.GuardNeutral) + " `neutral`) read the normalized overall.");
            }

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

        private static void AppendTypes(StringBuilder report, EncounterCatalog catalog)
        {
            report.AppendLine("### Enemy types (game content: `enemy-library.json`)");
            report.AppendLine();
            report.AppendLine("Base stats are max-level values scaled by the roster's growth curve, like a beast's, before the difficulty");
            report.AppendLine("multiplier (Move and Crit are exempt from both). Threat is the type's weight in a shape's budget. Kit entries are");
            report.AppendLine("category, shape, range, power, cooldown and whom the skill aims at (`nearest`, or `lowest current HP` = the beast");
            report.AppendLine("with the least HP left). A large enemy's size (tiles covered) follows its name; its ranges count from its nearest tile.");
            report.AppendLine();
            report.AppendLine("| Type | Role | Threat | Stance | HP | Atk | Def | SpA | SpD | Spe | Move | Crit | Kit |");
            report.AppendLine("| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
            foreach (EnemyData type in catalog.Types)
            {
                StatBlock s = type.BaseStats;
                report.AppendLine("| " + type.DisplayName + SizeText(type) + " | " + type.Role + " | " + Number(type.Threat) + " | " + StanceOf(type) + " | " + s.Hp + " | " + s.Attack +
                                  " | " + s.Defense + " | " + s.SpecialAttack + " | " + s.SpecialDefense + " | " + s.Speed + " | " + s.MoveRange + " | " +
                                  s.CritChance + "% | " + KitText(type.Skills) + " |");
            }

            report.AppendLine();
        }

        private static void AppendShapes(StringBuilder report, EncounterCatalog catalog)
        {
            report.AppendLine("### Encounter shapes");
            report.AppendLine();
            report.AppendLine("| Shape | Arena | Threat budget | Min types | Variants (weight: slots, count range from types) |");
            report.AppendLine("| --- | --- | --- | ---: | --- |");
            foreach (EncounterShape shape in catalog.Shapes)
            {
                List<string> variants = new List<string>();
                foreach (EncounterVariantData variant in shape.Data.Variants)
                {
                    List<string> slots = new List<string>();
                    foreach (EncounterSlotData slot in variant.Slots)
                    {
                        slots.Add((slot.Min == slot.Max ? slot.Min.ToString(CultureInfo.InvariantCulture) : slot.Min + "-" + slot.Max) + " from " +
                                  string.Join("/", slot.Types));
                    }

                    variants.Add(variant.Label + " (" + variant.Weight + ": " + string.Join(" + ", slots) + ")");
                }

                report.AppendLine("| `" + shape.Id + "` | " + shape.Arena + " | " + Number(shape.Data.ThreatMin) + "-" + Number(shape.Data.ThreatMax) +
                                  " | " + shape.Data.MinDistinctTypes + " | " + string.Join("; ", variants) + " |");
            }

            report.AppendLine();
        }

        /// <summary>
        /// Every generated composition, with its clear rate at its shape's calibrated difficulty
        /// (levels averaged), so the threat budget can be checked: a composition far from its
        /// shape's target is easier or harder than its threat says.
        /// </summary>
        private static void AppendCompositions(StringBuilder report, SimOptions options, EncounterCatalog catalog, List<PveCell> cells)
        {
            report.AppendLine("### Generated compositions");
            report.AppendLine();
            report.AppendLine("Enemies front-to-back. Elements per type, in unit order. Dominant = the element carrying at least " +
                              SimOptions.Format(100.0 * SimOptions.DominantElementShare) + "% of the");
            report.AppendLine("composition's threat (`-` = none does). Clear = the composition's clear rate at its shape's calibrated difficulty,");
            report.AppendLine("levels averaged, per kit mode.");
            report.AppendLine();

            StringBuilder header = new StringBuilder("| Composition | Enemies | Scheme | Elements | Dominant | Threat |");
            StringBuilder rule = new StringBuilder("| --- | --- | --- | --- | --- | ---: |");
            foreach (KitMode mode in options.Modes)
            {
                header.Append(" Clear `" + SimOptions.ModeName(mode) + "` |");
                rule.Append(" ---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            foreach (EncounterShape shape in catalog.Shapes)
            {
                for (int c = 0; c < shape.Compositions.Count; c++)
                {
                    Encounter encounter = shape.Compositions[c];
                    Element? dominant = DominantElement(encounter);
                    StringBuilder row = new StringBuilder("| `" + encounter.Id + "` | " + EnemiesText(encounter) + " | " + encounter.ElementScheme + " | " +
                                                          ElementsText(encounter) + " | " + (dominant.HasValue ? dominant.Value.ToString() : "-") + " | " +
                                                          Number(encounter.Threat) + " |");
                    foreach (KitMode mode in options.Modes)
                    {
                        double sum = 0.0;
                        foreach (int level in options.Levels)
                        {
                            sum += CompositionClearRate(Find(cells, mode, level, shape), c);
                        }

                        row.Append(" " + SimOptions.Format(sum / options.Levels.Count) + "% |");
                    }

                    report.AppendLine(row.ToString());
                }
            }

            report.AppendLine();
        }

        private static void AppendElementDistribution(StringBuilder report, EncounterCatalog catalog)
        {
            int[] units = new int[(int)Element.Dark + 1];
            int[] compositions = new int[(int)Element.Dark + 1];
            Dictionary<string, int> schemes = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> schemeOrder = new List<string>();
            List<Encounter> all = catalog.AllCompositions;
            foreach (Encounter encounter in all)
            {
                bool[] seen = new bool[units.Length];
                foreach (EnemySlot slot in encounter.Enemies)
                {
                    units[(int)slot.Element]++;
                    seen[(int)slot.Element] = true;
                }

                for (int e = 0; e < seen.Length; e++)
                {
                    compositions[e] += seen[e] ? 1 : 0;
                }

                if (!schemes.ContainsKey(encounter.ElementScheme))
                {
                    schemes[encounter.ElementScheme] = 0;
                    schemeOrder.Add(encounter.ElementScheme);
                }

                schemes[encounter.ElementScheme]++;
            }

            report.AppendLine("### Enemy element distribution (all compositions)");
            report.AppendLine();
            List<string> schemeParts = new List<string>();
            foreach (string scheme in schemeOrder)
            {
                schemeParts.Add(scheme + " " + schemes[scheme]);
            }

            report.AppendLine("Element schemes over " + all.Count + " compositions: " + string.Join(", ", schemeParts) + ".");
            report.AppendLine();
            report.AppendLine("| Element | Units | Compositions with it |");
            report.AppendLine("| --- | ---: | ---: |");
            for (int e = 0; e < units.Length; e++)
            {
                report.AppendLine("| " + (Element)e + " | " + units[e] + " | " + compositions[e] + " |");
            }

            report.AppendLine();
        }

        /// <summary>" (7 tiles)" after a large enemy's name; nothing for a one-tile enemy.</summary>
        private static string SizeText(EnemyData type)
        {
            EnemyLibraryValidator.TryParseFootprint(type.Footprint, out UnitFootprint footprint);
            int tiles = Footprints.TileCount(footprint);
            return tiles == 1 ? string.Empty : " (" + tiles + " tiles)";
        }

        private static CombatStance StanceOf(EnemyData type)
        {
            BeastRosterValidator.TryParseStance(type.Stance, out CombatStance stance);
            return stance;
        }

        /// <summary>
        /// A scheme's share of the generated set's draws, in percent: its weight over the total (the
        /// shipped weights add up to 100, so this is the weight itself).
        /// </summary>
        private static string SchemeShare(EncounterCatalog catalog, ElementScheme scheme)
        {
            int total = 0;
            int weight = 0;
            foreach (SchemeWeightData entry in catalog.SchemeWeights)
            {
                total += entry.Weight;
                if (string.Equals(entry.Scheme, scheme.ToString(), StringComparison.Ordinal))
                {
                    weight = entry.Weight;
                }
            }

            double share = total == 0 ? 0.0 : 100.0 * weight / total;
            return share == Math.Floor(share) ? ((int)share).ToString(CultureInfo.InvariantCulture) : SimOptions.Format(share);
        }

        private static void AppendFixedEncounters(StringBuilder report, EncounterCatalog catalog)
        {
            report.AppendLine("### Encounters (simulator fixtures, not game content)");
            report.AppendLine();
            report.AppendLine("Base stats are max-level values scaled by the roster's growth curve, like a beast's, before the difficulty");
            report.AppendLine("multiplier (Move and Crit are exempt from both). Kit entries are category, shape, range, power, cooldown and whom the");
            report.AppendLine("skill aims at (`nearest`, or `lowest current HP` = the beast with the least HP left). A large enemy's size (tiles");
            report.AppendLine("covered) follows its name; its ranges count from its nearest tile.");
            report.AppendLine();
            report.AppendLine("| Encounter | Arena | Enemy | Count | Stance | Elements | HP | Atk | Def | SpA | SpD | Spe | Move | Crit | Kit |");
            report.AppendLine("| --- | --- | --- | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

            foreach (EncounterShape shape in catalog.Shapes)
            {
                Encounter encounter = shape.Compositions[0];
                foreach (FixedGroupData group in encounter.FixedData.Groups)
                {
                    List<string> elements = new List<string>();
                    foreach (EnemySlot slot in encounter.Enemies)
                    {
                        if (slot.TypeId == group.EnemyId)
                        {
                            elements.Add(slot.Element.ToString());
                        }
                    }

                    StatBlock s = group.BaseStats;
                    report.AppendLine("| `" + encounter.Id + "` | " + encounter.Arena + " | " + group.DisplayName + SizeText(group) + " | " + group.Count + " | " +
                                      StanceOf(group) + " | " + Compress(elements) + " | " + s.Hp + " | " + s.Attack + " | " + s.Defense + " | " + s.SpecialAttack + " | " +
                                      s.SpecialDefense + " | " + s.Speed + " | " + s.MoveRange + " | " + s.CritChance + "% | " + KitText(group.Skills) + " |");
                }
            }

            report.AppendLine();
        }

        /// <summary>
        /// An enemy kit as category, shape, range, power (its first damage effect's magnitude),
        /// cooldown and, for a single-target skill, whom it aims at.
        /// </summary>
        private static string KitText(SkillData[] skills)
        {
            List<string> kit = new List<string>();
            foreach (SkillData skill in skills)
            {
                SkillTargetShape shape = SkillLibraryValidator.ParseOr(skill.TargetShape, SkillTargetShape.SingleTarget);
                kit.Add(skill.SkillId + " (" + SkillLibraryValidator.ParseOr(skill.Category, DamageCategory.Physical) + ", " + shape + ", r" + skill.Range + ", p" +
                        DamagePower(skill).ToString(CultureInfo.InvariantCulture) + ", cd" + skill.Cooldown +
                        (shape == SkillTargetShape.SingleTarget ? ", " + TargetingLabel(skill) : string.Empty) + ")");
            }

            return string.Join("; ", kit);
        }

        /// <summary>The magnitude of a skill's first Damage effect (0 when it has none).</summary>
        private static float DamagePower(SkillData skill)
        {
            foreach (EffectData effect in skill.Effects ?? new EffectData[0])
            {
                if (effect != null && SkillLibraryValidator.ParseOr(effect.EffectType, SkillEffectType.Damage) == SkillEffectType.Damage)
                {
                    return effect.Magnitude;
                }
            }

            return 0f;
        }

        /// <summary>Whom an enemy skill aims at: "nearest", "farthest", "lowest current HP", or a stat extreme such as "lowest HP" (maximum).</summary>
        private static string TargetingLabel(SkillData skill)
        {
            bool lowest = SkillLibraryValidator.ParseOr(skill.TargetingOrder, SkillTargetingOrder.Lowest) == SkillTargetingOrder.Lowest;
            switch (SkillLibraryValidator.ParseOr(skill.TargetingCriterion, SkillTargetingCriterion.Distance))
            {
                case SkillTargetingCriterion.Distance:
                    return lowest ? "nearest" : "farthest";
                case SkillTargetingCriterion.CurrentHp:
                    return (lowest ? "lowest" : "highest") + " current HP";
                default:
                    return (lowest ? "lowest " : "highest ") + "max " + SkillLibraryValidator.ParseOr(skill.TargetingStat, StatType.HP);
            }
        }

        /// <summary>"Giant x1, Archer x2" in placement order.</summary>
        private static string EnemiesText(Encounter encounter)
        {
            List<string> names = new List<string>();
            foreach (EnemySlot slot in encounter.Enemies)
            {
                names.Add(slot.GroupDisplayName);
            }

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
                parts.Add(name + " x" + counts[name]);
            }

            return string.Join(", ", parts);
        }

        /// <summary>"Giant: Fire; Archer: Water, Dark x2" in placement order.</summary>
        private static string ElementsText(Encounter encounter)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<string>> byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (EnemySlot slot in encounter.Enemies)
            {
                if (!byType.ContainsKey(slot.GroupDisplayName))
                {
                    byType[slot.GroupDisplayName] = new List<string>();
                    order.Add(slot.GroupDisplayName);
                }

                byType[slot.GroupDisplayName].Add(slot.Element.ToString());
            }

            List<string> parts = new List<string>();
            foreach (string type in order)
            {
                parts.Add(type + ": " + Compress(byType[type]));
            }

            return string.Join("; ", parts);
        }

        /// <summary>
        /// The element carrying at least <see cref="SimOptions.DominantElementShare"/> of the
        /// composition's threat (fixed encounters weigh every unit 1), or null when none does or
        /// that element is <c>None</c>.
        /// </summary>
        public static Element? DominantElement(Encounter encounter)
        {
            double[] weight = new double[(int)Element.Dark + 1];
            double total = 0.0;
            foreach (EnemySlot slot in encounter.Enemies)
            {
                double w = slot.Threat > 0.0 ? slot.Threat : 1.0;
                weight[(int)slot.Element] += w;
                total += w;
            }

            for (int e = 1; e < weight.Length; e++)
            {
                if (total > 0.0 && weight[e] / total >= SimOptions.DominantElementShare - 1e-9)
                {
                    return (Element)e;
                }
            }

            return null;
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

        private static double CompositionClearRate(PveCell cell, int composition)
        {
            int per = cell.TeamCount * cell.Samples;
            int cleared = 0;
            for (int i = composition * per; i < (composition + 1) * per; i++)
            {
                cleared += cell.Battles[i].Cleared ? 1 : 0;
            }

            return per == 0 ? 0.0 : (100.0 * cleared) / per;
        }

        private static void AppendCalibration(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                              List<PveCell> cells)
        {
            report.AppendLine("### Calibrated difficulty");
            report.AppendLine();
            if (options.CalibratesOnPick)
            {
                AppendPickCalibration(report, options, species, simulator, cells);
                return;
            }

            report.AppendLine("| Kit mode | Shape | Level | Multiplier | Clear rate | Composition clear range | Evaluations | Battles | Avg time | Stalemates |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: |");

            foreach (PveCell cell in cells)
            {
                double time = 0.0;
                int stalemates = 0;
                foreach (PveBattle battle in cell.Battles)
                {
                    time += battle.Time;
                    stalemates += battle.Outcome == BattleOutcome.Stalemate ? 1 : 0;
                }

                double low = double.MaxValue;
                double high = double.MinValue;
                for (int c = 0; c < cell.Shape.Compositions.Count; c++)
                {
                    double rate = CompositionClearRate(cell, c);
                    low = Math.Min(low, rate);
                    high = Math.Max(high, rate);
                }

                string miss = Math.Abs(cell.ClearRate - options.TargetFor(cell.Shape)) > SimOptions.CalibrationTolerance ? " !" : string.Empty;
                report.AppendLine("| `" + SimOptions.ModeName(cell.Mode) + "` | `" + cell.Shape.Id + "` | " + cell.Level + " | x" +
                                  SimOptions.FormatMultiplier(cell.Multiplier) + " | " + SimOptions.Format(cell.ClearRate) + "%" + miss + " | " +
                                  SimOptions.Format(low) + "-" + SimOptions.Format(high) + "% | " + cell.Evaluations.Count + " | " + cell.Battles.Length + " | " +
                                  SimOptions.Format(time / cell.Battles.Length) + " | " + stalemates + " |");
            }

            report.AppendLine();
            report.AppendLine("`!` = the closest clear rate calibration found is more than " + SimOptions.Format(SimOptions.CalibrationTolerance) +
                              " points off target (a step in the");
            report.AppendLine("clear-rate curve that no multiplier splits). Composition clear range = lowest and highest clear rate of a single");
            report.AppendLine("composition at the shape's multiplier.");
            report.AppendLine();
        }

        /// <summary>"Calibrated difficulty" for a scouted-pick calibration: the calibrated (picked-team) rate beside the no-scouting one.</summary>
        private static void AppendPickCalibration(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                                  List<PveCell> cells)
        {
            report.AppendLine("| Kit mode | Shape | Level | Multiplier | Calibrated on | Target | Scouted clear | No-scouting clear | Gap | Composition clear range | Evaluations | Battles | Avg time | Stalemates |");
            report.AppendLine("| --- | --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: |");
            foreach (PveCell cell in cells)
            {
                double time = 0.0;
                int stalemates = 0;
                foreach (PveBattle battle in cell.Battles)
                {
                    time += battle.Time;
                    stalemates += battle.Outcome == BattleOutcome.Stalemate ? 1 : 0;
                }

                double low = double.MaxValue;
                double high = double.MinValue;
                for (int c = 0; c < cell.Shape.Compositions.Count; c++)
                {
                    double rate = CompositionClearRate(cell, c);
                    low = Math.Min(low, rate);
                    high = Math.Max(high, rate);
                }

                string miss = Math.Abs(cell.CalibratedRate - options.TargetFor(cell.Shape)) > SimOptions.CalibrationTolerance ? " !" : string.Empty;
                report.AppendLine("| `" + SimOptions.ModeName(cell.Mode) + "` | `" + cell.Shape.Id + "` | " + cell.Level + " | x" +
                                  SimOptions.FormatMultiplier(cell.Multiplier) + " | " + SimOptions.CalibrationName(cell.CalibratedOn) + " | " +
                                  SimOptions.Format(options.TargetFor(cell.Shape)) + "% | " +
                                  SimOptions.Format(cell.ScoutedClearRate) + "%" + miss + " | " + SimOptions.Format(cell.ClearRate) + "% | " +
                                  SimOptions.Signed(cell.ScoutedClearRate - cell.ClearRate) + " | " + SimOptions.Format(low) + "-" + SimOptions.Format(high) + "% | " +
                                  cell.Evaluations.Count + " | " + cell.Battles.Length + " | " + SimOptions.Format(time / cell.Battles.Length) + " | " + stalemates + " |");
            }

            report.AppendLine();
            int samples = cells.Count == 0 ? 0 : cells[0].CalibrationSamples;
            report.AppendLine("**Scouted clear** = the clear rate of the team the " + PickerName(options) + " fields against each composition, over " + samples +
                              " battles per composition");
            report.AppendLine("(`--calibrate-samples`): the number the calibration aims at the target. **No-scouting clear** = the mean over every");
            report.AppendLine("team and composition at the same multiplier (the player who brings any team without looking); **Gap** = scouted minus no");
            report.AppendLine("scouting. `!` = the closest scouted rate calibration found is more than " + SimOptions.Format(SimOptions.CalibrationTolerance) +
                              " points off target (a step in the picked teams'");
            report.AppendLine("clear-rate curve that no multiplier splits; the few picked teams react to one stat rounding alike, most at level 1).");
            report.AppendLine("Evaluations counts the search steps (picked teams only) plus the final every-team run; Battles, Avg time, Stalemates and");
            report.AppendLine("the composition clear range (lowest and highest clear rate of a single composition over every team) are that final run's.");
            report.AppendLine();

            report.AppendLine("#### Difficulty by shape (levels averaged)");
            report.AppendLine();
            report.AppendLine("Each shape is calibrated to its own **target** (`TargetClear` in `encounter-library.json`, tiered by the kind of fight;");
            report.AppendLine("`--target-clear` overrides it). **Scouted** = the calibrated rate (the " + PickerName(options) + "'s team); **no scouting** = the");
            report.AppendLine("average team; **heuristic (no bonds)** = the team the plain element counter-pick fields, ignoring bonds (its first battle");
            report.AppendLine("per composition in the every-team run, so noisier); **no avatar** = the scouted team's battles replayed without the avatar");
            report.AppendLine("(`--avatar-value`; - without it).");
            report.AppendLine();
            report.AppendLine("| Kit mode | Shape | Target | Multiplier | Scouted clear | No-scouting clear | Heuristic (no bonds) | No avatar | Gap |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (KitMode mode in options.Modes)
            {
                foreach (EncounterShape shape in ScoutedPicker.ShapesOf(cells))
                {
                    List<PveCell> part = cells.FindAll(c => c.Mode == mode && c.Shape == shape);
                    int[] heuristicPicks = ScoutedPicker.PicksFor(options, species, simulator.Teams, simulator.TeamBonds, shape, ScoutedPicker.HeuristicIndex);
                    double multiplier = 0.0;
                    double scouted = 0.0;
                    double none = 0.0;
                    double heuristic = 0.0;
                    double noAvatar = 0.0;
                    foreach (PveCell cell in part)
                    {
                        multiplier += cell.Multiplier / part.Count;
                        scouted += cell.ScoutedClearRate / part.Count;
                        none += cell.ClearRate / part.Count;
                        noAvatar += cell.NoAvatarScoutedClearRate / part.Count;
                        int cleared = 0;
                        for (int c = 0; c < shape.Compositions.Count; c++)
                        {
                            cleared += cell.Battles[simulator.BattleIndex(c, heuristicPicks[c], 0)].Cleared ? 1 : 0;
                        }

                        heuristic += 100.0 * cleared / shape.Compositions.Count / part.Count;
                    }

                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | `" + shape.Id + "` | " + SimOptions.Format(options.TargetFor(shape)) + "% | x" +
                                      SimOptions.FormatMultiplier(multiplier) + " | " + SimOptions.Format(scouted) + "% | " + SimOptions.Format(none) + "% | " +
                                      SimOptions.Format(heuristic) + "% | " + (double.IsNaN(noAvatar) ? "-" : SimOptions.Format(noAvatar) + "%") + " | " +
                                      SimOptions.Signed(scouted - none) + " |");
                }
            }

            report.AppendLine();
        }

        /// <summary>The picker a scouted-pick calibration aims at the target, in words.</summary>
        public static string PickerName(SimOptions options)
        {
            return options.EffectiveCalibrateOn == CalibrationTarget.Bonds
                ? "bond-aware scouted picker (heuristic + bonds)"
                : "element counter-pick heuristic";
        }

        /// <summary>Single-target fire totals for the parity table.</summary>
        private class FireTotals
        {
            public long Physical;
            public long Special;
            public double PhysicalPower;
            public long Burst;
            public long BurstTargets;

            public double SpecialPower
            {
                get { return Special * (double)SimOptions.BlastPower; }
            }

            public double PhysicalShare
            {
                get { return PhysicalPower + SpecialPower == 0.0 ? double.NaN : (100.0 * PhysicalPower) / (PhysicalPower + SpecialPower); }
            }
        }

        private static void AddFires(FireTotals totals, PveCell cell, PveSimulator simulator, IReadOnlyList<CreatureSpeciesSO> species, CombatStance? stance)
        {
            for (int i = 0; i < cell.Battles.Length; i++)
            {
                PveBattle battle = cell.Battles[i];
                int[] team = simulator.Teams[cell.TeamOf(i)];
                for (int m = 0; m < team.Length; m++)
                {
                    CombatStance memberStance = species[team[m]].Stance;
                    if (stance.HasValue && memberStance != stance.Value)
                    {
                        continue;
                    }

                    totals.Physical += battle.MemberPhysicalFires[m];
                    totals.Special += battle.MemberSpecialFires[m];
                    totals.PhysicalPower += battle.MemberPhysicalFires[m] * (double)Kit.PhysicalSinglePower(memberStance);
                }

                if (!stance.HasValue)
                {
                    totals.Burst += battle.BurstFires;
                    totals.BurstTargets += battle.BurstTargets;
                }
            }
        }

        private static List<CombatStance> StancesIn(IReadOnlyList<CreatureSpeciesSO> species)
        {
            List<CombatStance> stances = new List<CombatStance>();
            foreach (CombatStance stance in new[] { CombatStance.Vanguard, CombatStance.Skirmisher, CombatStance.Ranged })
            {
                foreach (CreatureSpeciesSO beast in species)
                {
                    if (beast.Stance == stance)
                    {
                        stances.Add(stance);
                        break;
                    }
                }
            }

            return stances;
        }

        private static FireTotals StanceTotals(List<PveCell> cells, KitMode mode, PveSimulator simulator, IReadOnlyList<CreatureSpeciesSO> species, CombatStance? stance)
        {
            FireTotals totals = new FireTotals();
            foreach (PveCell cell in cells)
            {
                if (cell.Mode == mode)
                {
                    AddFires(totals, cell, simulator, species, stance);
                }
            }

            return totals;
        }

        private static void AppendParity(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                         List<EncounterShape> shapes, List<PveCell> cells)
        {
            report.AppendLine("### Kit parity (beast skill fires at calibrated difficulty, all levels)");
            report.AppendLine();
            report.AppendLine("The physical single-target skill is Strike (range 1, power " + Number(SimOptions.StrikePower) + ") for Vanguard and Skirmisher beasts and");
            report.AppendLine("Shot (range 3, power " + Number(SimOptions.ShotPower) + ") for Ranged beasts, which never walk into melee; Blast (special, range 3, power " +
                              Number(SimOptions.BlastPower) + ") is");
            report.AppendLine("shared. **Physical share** = physical fires x their power as a share of all single-target power delivered; 50%");
            report.AppendLine("means `Attack` and `SpecialAttack` weigh the same. Burst uses count once per pair of halves; targets = enemies");
            report.AppendLine("inside the radius when the first half fires.");
            report.AppendLine();
            report.AppendLine("By stance (every shape and level):");
            report.AppendLine();
            report.AppendLine("| Kit mode | Stance | Physical fires | Blast fires | Physical / Blast | Physical share |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
            foreach (KitMode mode in options.Modes)
            {
                foreach (CombatStance stance in StancesIn(species))
                {
                    FireTotals t = StanceTotals(cells, mode, simulator, species, stance);
                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | " + stance + " (" + (stance == CombatStance.Ranged ? "Shot" : "Strike") + ") | " +
                                      t.Physical + " | " + t.Special + " | " + Ratio(t.Physical, t.Special) + " | " + Share(t.PhysicalShare) + " |");
                }

                FireTotals all = StanceTotals(cells, mode, simulator, species, null);
                report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | all | " + all.Physical + " | " + all.Special + " | " + Ratio(all.Physical, all.Special) +
                                  " | " + Share(all.PhysicalShare) + " |");
            }

            report.AppendLine();
            report.AppendLine("By shape (every stance and level):");
            report.AppendLine();
            report.AppendLine("| Kit mode | Shape | Physical fires | Blast fires | Physical / Blast | Physical share | Burst uses | Avg targets per Burst |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (KitMode mode in options.Modes)
            {
                foreach (EncounterShape shape in shapes)
                {
                    FireTotals t = new FireTotals();
                    foreach (int level in options.Levels)
                    {
                        AddFires(t, Find(cells, mode, level, shape), simulator, species, null);
                    }

                    report.AppendLine("| `" + SimOptions.ModeName(mode) + "` | `" + shape.Id + "` | " + t.Physical + " | " + t.Special + " | " + Ratio(t.Physical, t.Special) +
                                      " | " + Share(t.PhysicalShare) + " | " + t.Burst + " | " +
                                      (t.Burst == 0 ? "-" : ((double)t.BurstTargets / t.Burst).ToString("0.00", CultureInfo.InvariantCulture)) + " |");
                }
            }

            report.AppendLine();
        }

        private static string Ratio(long a, long b)
        {
            return b == 0 ? "-" : ((double)a / b).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Share(double share)
        {
            return double.IsNaN(share) ? "-" : SimOptions.Format(share) + "%";
        }

        /// <summary>
        /// Per beast, over every PvE battle at calibrated difficulty (both kit modes, every
        /// shape, composition, level and sample): how often its hits crit against its authored
        /// chance, and the average random multiplier its hits got. Expected = <c>1 + (CritMultiplier - 1) *
        /// chance</c>, since the variance roll averages 100%.
        /// </summary>
        private static void AppendRolls(StringBuilder report, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator, List<PveCell> cells)
        {
            long[] hits = new long[species.Count];
            long[] crits = new long[species.Count];
            double[] multiplier = new double[species.Count];

            foreach (PveCell cell in cells)
            {
                for (int i = 0; i < cell.Battles.Length; i++)
                {
                    PveBattle battle = cell.Battles[i];
                    int[] team = simulator.Teams[cell.TeamOf(i)];
                    for (int m = 0; m < team.Length; m++)
                    {
                        hits[team[m]] += battle.MemberHits[m];
                        crits[team[m]] += battle.MemberCrits[m];
                        multiplier[team[m]] += battle.MemberRollMultiplier[m];
                    }
                }
            }

            report.AppendLine("### Critical hits and damage rolls (beast kit, calibrated difficulty, all modes, levels and samples)");
            report.AppendLine();
            report.AppendLine("Each damage effect that lands rolls crit (x" + DamageFormula.CritMultiplier.ToString("0.0#", CultureInfo.InvariantCulture) +
                              ", chance = the beast's `CritChance`) then variance (uniform " + DamageFormula.VarianceMinPercent + "-" +
                              DamageFormula.VarianceMaxPercent + "%). **Avg roll** = mean");
            report.AppendLine("of `(crit ? " + DamageFormula.CritMultiplier.ToString("0.0#", CultureInfo.InvariantCulture) +
                              " : 1) x variance` over the beast's hits; expected = `1 + " +
                              (DamageFormula.CritMultiplier - 1f).ToString("0.0#", CultureInfo.InvariantCulture) + " x chance`.");
            report.AppendLine();
            report.AppendLine("| Beast | Crit chance | Hits | Crits | Observed crit rate | Avg roll | Expected avg roll |");
            report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            for (int b = 0; b < species.Count; b++)
            {
                int chance = DamageFormula.ClampCritChance(species[b].BaseStats.CritChance);
                double expected = 1.0 + ((DamageFormula.CritMultiplier - 1.0) * chance / 100.0);
                report.AppendLine("| " + species[b].DisplayName + " | " + chance + "% | " + hits[b] + " | " + crits[b] + " | " +
                                  (hits[b] == 0 ? "-" : SimOptions.Format((100.0 * crits[b]) / hits[b]) + "%") + " | " +
                                  (hits[b] == 0 ? "-" : (multiplier[b] / hits[b]).ToString("0.000", CultureInfo.InvariantCulture)) + " | " +
                                  expected.ToString("0.000", CultureInfo.InvariantCulture) + " |");
            }

            report.AppendLine();
        }

        /// <summary>
        /// "Level-gap mix" (<c>--gap-mix</c>, on by default): what the balance sections are judged over,
        /// and per kit mode and shape (levels averaged) the no-scouting clear rate at gap 0 against the
        /// mix, then per gap the no-scouting rate of the battles the mix dealt it. Nothing with the mix off.
        /// </summary>
        private static void AppendGapMix(StringBuilder report, SimOptions options, List<EncounterShape> shapes, List<PveCell> cells)
        {
            if (!options.GapMixActive)
            {
                return;
            }

            report.AppendLine("### Level-gap mix");
            report.AppendLine();
            report.AppendLine("The player does not always fight at their own level, so the balance sections (per-beast marginals, niches, flags,");
            report.AppendLine("element matchups, team composition and bonds) are judged over a gameplay mix of level gaps (`--gap-mix`; enemy level");
            report.AppendLine("minus team level, positive = the team is under-levelled): " + options.GapMixText + ". Each every-team battle of a cell is");
            report.AppendLine("dealt one gap in those proportions (a seeded deal per composition; the gap-0 ones are the calibration's own battles) and");
            report.AppendLine("fought at the cell's calibrated multiplier; where a gap would put the enemies outside levels 1-" + SimOptions.MaxLevel +
                              ", the team moves instead.");
            report.AppendLine("The calibration, the difficulty table, scouting and the plumbing checks stay at gap 0; the marginal tables give the gap-0");
            report.AppendLine("overall beside the mix. No-scouting clear rate (every team), levels averaged:");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Kit mode | Shape | Gap 0 | Mix |");
            StringBuilder rule = new StringBuilder("| --- | --- | ---: | ---: |");
            foreach (int gap in options.GapMixGaps)
            {
                header.Append(" " + (gap > 0 ? "+" : string.Empty) + gap.ToString(CultureInfo.InvariantCulture) + " |");
                rule.Append(" ---: |");
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            foreach (KitMode mode in options.Modes)
            {
                for (int e = 0; e < shapes.Count; e++)
                {
                    double gap0 = 0.0;
                    double mix = 0.0;
                    long[] byGap = new long[options.GapMixGaps.Count];
                    long[] clearedByGap = new long[options.GapMixGaps.Count];
                    int parts = 0;
                    foreach (int level in options.Levels)
                    {
                        PveCell cell = Find(cells, mode, level, shapes[e]);
                        gap0 += Rate(cell.Battles);
                        mix += Rate(cell.MixBattles);
                        parts++;
                        for (int i = 0; i < cell.MixBattles.Length; i++)
                        {
                            int k = options.GapMixGaps.IndexOf(cell.GapMixGaps[i]);
                            byGap[k]++;
                            clearedByGap[k] += cell.MixBattles[i].Cleared ? 1 : 0;
                        }
                    }

                    StringBuilder row = new StringBuilder("| `" + SimOptions.ModeName(mode) + "` | `" + shapes[e].Id + "` | " + SimOptions.Format(gap0 / parts) + "% | " +
                                                          SimOptions.Format(mix / parts) + "% |");
                    for (int k = 0; k < byGap.Length; k++)
                    {
                        row.Append(" " + (byGap[k] == 0 ? "-" : SimOptions.Format((100.0 * clearedByGap[k]) / byGap[k]) + "%") + " |");
                    }

                    report.AppendLine(row.ToString());
                }
            }

            report.AppendLine();
            report.AppendLine("Per-gap columns pool the battles the mix dealt that gap over the shape's levels (" +
                              SimOptions.Format(100.0 * Min(options.GapMixWeights)) + "% of each cell for the rarest gap), so they");
            report.AppendLine("are noisy; `--level-gap` measures single gaps properly.");
            report.AppendLine();
        }

        private static double Min(List<double> values)
        {
            double min = double.MaxValue;
            foreach (double value in values)
            {
                min = Math.Min(min, value);
            }

            return min;
        }

        private static double Rate(PveBattle[] battles)
        {
            int cleared = 0;
            foreach (PveBattle battle in battles)
            {
                cleared += battle.Cleared ? 1 : 0;
            }

            return battles.Length == 0 ? 0.0 : (100.0 * cleared) / battles.Length;
        }

        private static void AppendFlags(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                        List<EncounterShape> shapes, List<PveCell> cells, List<ModeSummary> summaries)
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
                    double marginal = options.CalibratesOnPick ? summary.Overall[b].NormalizedMarginal : summary.Overall[b].Marginal;
                    if (Math.Abs(marginal) > options.MarginalThreshold)
                    {
                        report.AppendLine("- " + mode + " " + species[b].DisplayName + ": overall " + (options.CalibratesOnPick ? "normalized " : string.Empty) +
                                          "marginal " + SimOptions.Signed(marginal) +
                                          " points (" + (marginal > 0 ? "HIGH" : "LOW") + ", outside +/-" + SimOptions.Format(options.MarginalThreshold) + ")");
                        any = true;
                    }
                }

                if (shapes.Count > 1)
                {
                    for (int b = 0; b < species.Count; b++)
                    {
                        bool bottomEverywhere = true;
                        bool topEverywhere = true;
                        for (int e = 0; e < shapes.Count; e++)
                        {
                            int rank = summary.Ranking[e].IndexOf(b);
                            bottomEverywhere &= rank >= species.Count - band;
                            topEverywhere &= rank < band;
                        }

                        if (bottomEverywhere)
                        {
                            report.AppendLine("- " + mode + " " + species[b].DisplayName + ": NO NICHE (bottom " + band + " in every shape)");
                            any = true;
                        }

                        if (topEverywhere)
                        {
                            report.AppendLine("- " + mode + " " + species[b].DisplayName + ": NO WEAKNESS (top " + band + " in every shape)");
                            any = true;
                        }
                    }
                }

                foreach (CombatStance stance in StancesIn(species))
                {
                    double share = StanceTotals(cells, summary.Mode, simulator, species, stance).PhysicalShare;
                    if (!double.IsNaN(share) && Math.Abs(share - 50.0) > ParityTolerance)
                    {
                        report.AppendLine("- " + mode + " kit parity: " + stance + " physical share " + SimOptions.Format(share) + "% (outside 50 +/- " +
                                          SimOptions.Format(ParityTolerance) + ")");
                        any = true;
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

                string where = "`" + SimOptions.ModeName(cell.Mode) + "` `" + cell.Shape.Id + "` L" + cell.Level;
                if (stalemates > 0)
                {
                    report.AppendLine("- " + where + ": " + stalemates + " of " + cell.Battles.Length + " battles stalemated at the calibrated difficulty");
                    any = true;
                }

                if (Math.Abs(cell.CalibratedRate - options.TargetFor(cell.Shape)) > SimOptions.CalibrationTolerance)
                {
                    report.AppendLine("- " + where + ": calibration miss, closest " + (options.CalibratesOnPick ? "scouted " : string.Empty) + "clear rate " +
                                      SimOptions.Format(cell.CalibratedRate) + "%");
                    any = true;
                }
            }

            if (!any)
            {
                report.AppendLine("None.");
            }

            report.AppendLine();
        }

        private static void AppendMode(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<EncounterShape> shapes,
                                       ModeSummary summary)
        {
            string name = SimOptions.ModeName(summary.Mode);
            report.AppendLine("### PvE mode: `" + name + "`");
            report.AppendLine();

            // Marginal by shape.
            report.AppendLine("#### Marginal clear rate by shape (levels averaged)");
            report.AppendLine();
            StringBuilder header = new StringBuilder("| Beast | Element | Stance |");
            StringBuilder rule = new StringBuilder("| --- | --- | --- |");
            foreach (EncounterShape shape in shapes)
            {
                header.Append(" `" + shape.Id + "` |");
                rule.Append(" ---: |");
            }

            header.Append(" Overall |");
            rule.Append(" ---: |");
            if (options.CalibratesOnPick)
            {
                header.Append(" Overall normalized |");
                rule.Append(" ---: |");
            }

            if (summary.Gap0 != null)
            {
                header.Append(" Gap 0 overall |" + (options.CalibratesOnPick ? " Gap 0 normalized |" : string.Empty));
                rule.Append(" ---: |" + (options.CalibratesOnPick ? " ---: |" : string.Empty));
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());

            foreach (int b in summary.OverallOrder)
            {
                StringBuilder row = new StringBuilder("| " + species[b].DisplayName + " | " + PvpReport.ElementsOf(species[b]) + " | " + species[b].Stance + " |");
                for (int e = 0; e < shapes.Count; e++)
                {
                    row.Append(" " + Marked(options, summary.ByShape[e][b].Marginal) + " (" + (summary.Ranking[e].IndexOf(b) + 1) + ") |");
                }

                row.Append(" " + Marked(options, summary.Overall[b].Marginal) + " |");
                if (options.CalibratesOnPick)
                {
                    row.Append(" " + Marked(options, summary.Overall[b].NormalizedMarginal) + " |");
                }

                if (summary.Gap0 != null)
                {
                    row.Append(" " + Marked(options, summary.Gap0.Overall[b].Marginal) + " |" +
                               (options.CalibratesOnPick ? " " + Marked(options, summary.Gap0.Overall[b].NormalizedMarginal) + " |" : string.Empty));
                }

                report.AppendLine(row.ToString());
            }

            report.AppendLine();
            report.AppendLine("Points of clear rate; (n) = rank within that shape. Sorted by overall. **Bold** = above +" +
                              SimOptions.Format(options.MarginalThreshold) + ", _italic_ = below -" + SimOptions.Format(options.MarginalThreshold) + "." +
                              (options.CalibratesOnPick ? " Overall normalized = the per-cell normalized marginals (see \"PvE configuration\"), averaged." : string.Empty) +
                              (summary.Gap0 == null ? string.Empty
                                  : " Every column but the gap-0 ones is over the level-gap mix (see \"Level-gap mix\"); gap 0 = the equal-level battles alone."));
            report.AppendLine();

            // Marginal by shape and level.
            report.AppendLine("#### Marginal clear rate by shape and level");
            report.AppendLine();
            header = new StringBuilder("| Beast |");
            rule = new StringBuilder("| --- |");
            foreach (EncounterShape shape in shapes)
            {
                foreach (int level in options.Levels)
                {
                    header.Append(" `" + shape.Id + "` L" + level + " |");
                    rule.Append(" ---: |");
                }
            }

            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            foreach (int b in summary.OverallOrder)
            {
                StringBuilder row = new StringBuilder("| " + species[b].DisplayName + " |");
                for (int e = 0; e < shapes.Count; e++)
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
            report.AppendLine("#### Ranking per shape (niches)");
            report.AppendLine();
            header = new StringBuilder("| Rank |");
            rule = new StringBuilder("| ---: |");
            foreach (EncounterShape shape in shapes)
            {
                header.Append(" `" + shape.Id + "` |");
                rule.Append(" --- |");
            }

            header.Append(" Overall |");
            rule.Append(" --- |");
            report.AppendLine(header.ToString());
            report.AppendLine(rule.ToString());
            for (int r = 0; r < species.Count; r++)
            {
                StringBuilder row = new StringBuilder("| " + (r + 1) + " |");
                for (int e = 0; e < shapes.Count; e++)
                {
                    int b = summary.Ranking[e][r];
                    row.Append(" " + species[b].DisplayName + " " + SimOptions.Signed(summary.ByShape[e][b].Marginal) + " |");
                }

                int o = summary.OverallOrder[r];
                row.Append(" " + species[o].DisplayName + " " + SimOptions.Signed(summary.Overall[o].Marginal) + " |");
                report.AppendLine(row.ToString());
            }

            report.AppendLine();

            // Role metrics.
            for (int e = 0; e < shapes.Count; e++)
            {
                report.AppendLine("#### Role metrics: `" + shapes[e].Id + "` (levels averaged)");
                report.AppendLine();
                report.AppendLine("| Beast | Marginal | Clear with | Clear without | Dmg share | Taken share | Survival | Time to clear | Turns / time |");
                report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                foreach (int b in summary.Ranking[e])
                {
                    BeastMetrics m = summary.ByShape[e][b];
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

        /// <summary>Clear counts for one beast in one matchup bucket.</summary>
        private class MatchupTally
        {
            public long With;
            public long WithCleared;
            public long Without;
            public long WithoutCleared;
            public int Compositions;

            public string Marginal
            {
                get
                {
                    if (With == 0 || Without == 0)
                    {
                        return "-";
                    }

                    return SimOptions.Signed((100.0 * WithCleared / With) - (100.0 * WithoutCleared / Without));
                }
            }
        }

        /// <summary>
        /// Elemental mode only: each beast's marginal clear rate split by how its kit element fares
        /// against the composition's dominant element (strong = super-effective, weak = resisted),
        /// pooled over every shape and level. Cheap: a regrouping of battles already run.
        /// </summary>
        private static void AppendElementMatchups(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                                  List<PveCell> cells)
        {
            const int Strong = 0;
            const int Neutral = 1;
            const int Weak = 2;
            const int Mixed = 3;
            MatchupTally[][] tallies = new MatchupTally[species.Count][];
            for (int b = 0; b < species.Count; b++)
            {
                tallies[b] = new MatchupTally[4];
                for (int k = 0; k < 4; k++)
                {
                    tallies[b][k] = new MatchupTally();
                }
            }

            bool anyCell = false;
            foreach (PveCell cell in cells)
            {
                if (cell.Mode != KitMode.Elemental)
                {
                    continue;
                }

                anyCell = true;
                int[][] bucket = new int[cell.Shape.Compositions.Count][];
                for (int c = 0; c < cell.Shape.Compositions.Count; c++)
                {
                    Element? dominant = DominantElement(cell.Shape.Compositions[c]);
                    bucket[c] = new int[species.Count];
                    for (int b = 0; b < species.Count; b++)
                    {
                        bucket[c][b] = Bucket(species[b], dominant, Strong, Neutral, Weak, Mixed);
                        if (cell.Level == options.Levels[0])
                        {
                            tallies[b][bucket[c][b]].Compositions++;
                        }
                    }
                }

                PveBattle[] battles = cell.BalanceBattles;
                for (int i = 0; i < battles.Length; i++)
                {
                    PveBattle battle = battles[i];
                    int[] team = simulator.Teams[cell.TeamOf(i)];
                    int c = cell.CompositionOf(i);
                    for (int b = 0; b < species.Count; b++)
                    {
                        MatchupTally t = tallies[b][bucket[c][b]];
                        if (Array.IndexOf(team, b) >= 0)
                        {
                            t.With++;
                            t.WithCleared += battle.Cleared ? 1 : 0;
                        }
                        else
                        {
                            t.Without++;
                            t.WithoutCleared += battle.Cleared ? 1 : 0;
                        }
                    }
                }
            }

            if (!anyCell)
            {
                return;
            }

            report.AppendLine("#### Element matchups (`elemental`, every shape and level pooled)");
            report.AppendLine();
            report.AppendLine("Each beast's marginal clear rate over the compositions whose dominant element (at least " +
                              SimOptions.Format(100.0 * SimOptions.DominantElementShare) + "% of the threat) its kit");
            report.AppendLine("element hits super-effectively (strong), neutrally, or resisted (weak), and over the compositions with no dominant");
            report.AppendLine("element or a `None` one (mixed). (n) = compositions in the bucket per level and kit mode. Small buckets are noisy.");
            report.AppendLine();
            report.AppendLine("| Beast | Element | Strong | Neutral | Weak | Mixed / none |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
            for (int b = 0; b < species.Count; b++)
            {
                StringBuilder row = new StringBuilder("| " + species[b].DisplayName + " | " + PvpReport.ElementsOf(species[b]) + " |");
                for (int k = 0; k < 4; k++)
                {
                    row.Append(" " + tallies[b][k].Marginal + " (" + tallies[b][k].Compositions + ") |");
                }

                report.AppendLine(row.ToString());
            }

            report.AppendLine();
        }

        private static int Bucket(CreatureSpeciesSO beast, Element? dominant, int strong, int neutral, int weak, int mixed)
        {
            if (!dominant.HasValue)
            {
                return mixed;
            }

            Element attack = beast.Elements != null && beast.Elements.Length > 0 ? beast.Elements[0] : Element.None;
            float multiplier = ElementChart.GetMultiplier(attack, dominant.Value);
            return multiplier > 1f ? strong : multiplier < 1f ? weak : neutral;
        }

        public static string Marked(SimOptions options, double marginal)
        {
            string text = SimOptions.Signed(marginal);
            if (marginal > options.MarginalThreshold)
            {
                return "**" + text + "**";
            }

            return marginal < -options.MarginalThreshold ? "_" + text + "_" : text;
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
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
