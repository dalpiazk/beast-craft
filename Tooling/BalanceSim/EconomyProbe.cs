using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--economy-probe</c>: what gear and consumables are worth in battle, in levels-equivalent
    /// (LE). Every PvE cell is replayed at its calibrated multiplier by every team against every
    /// composition (no scouting), gearless and without consumables (the base), with each variant,
    /// and with the team one level above the enemies (enemies one level below; at level 1, one
    /// level above and the sign flipped). A variant's LE is its clear-rate gain over the base
    /// divided by the one-level gain, pooled over the level's cells (both kit modes, every shape).
    /// Each variant's battles use the base's seeds, so the difference is the variant's alone.
    /// Budget targets (economy design): three commons ~0.7 LE, three rares ~1.2, three epics ~1.8;
    /// one consumable at most ~0.3.
    /// </summary>
    internal static class EconomyProbe
    {
        public static string Build(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<PveCell> cells)
        {
            List<string> names = new List<string>();
            List<Action<PveSimulator>> variants = new List<Action<PveSimulator>>();
            foreach (GearProfile profile in new[] { GearProfile.Common, GearProfile.Rare, GearProfile.Epic, GearProfile.Typical })
            {
                GearProfile p = profile;
                names.Add("gear " + p.ToString().ToLowerInvariant());
                variants.Add(sim => sim.GearFor = (s, l) => options.GearKits.For(s, l, p));
            }

            AddConsumableVariants(options, names, variants);

            List<int> levels = new List<int>();
            foreach (PveCell cell in cells)
            {
                if (!levels.Contains(cell.Level))
                {
                    levels.Add(cell.Level);
                }
            }

            // Per level: pooled base, one-level and variant clear counts.
            Dictionary<int, double> baseRate = new Dictionary<int, double>();
            Dictionary<int, double> levelGain = new Dictionary<int, double>();
            double[,] gain = new double[variants.Count, levels.Count];
            PveSimulator plain = new PveSimulator(options, species) { GearFor = null };
            PveSimulator[] sims = new PveSimulator[variants.Count];
            for (int v = 0; v < variants.Count; v++)
            {
                sims[v] = new PveSimulator(options, species) { GearFor = null };
                variants[v](sims[v]);
            }

            int[] teams = new int[plain.Teams.Count];
            for (int t = 0; t < teams.Length; t++)
            {
                teams[t] = t;
            }

            for (int li = 0; li < levels.Count; li++)
            {
                int level = levels[li];
                double baseSum = 0.0;
                double upSum = 0.0;
                double[] variantSum = new double[variants.Count];
                int count = 0;
                foreach (PveCell cell in cells)
                {
                    if (cell.Level != level)
                    {
                        continue;
                    }

                    double b = Rate(plain.RunTeams(cell.Mode, level, cell.Shape, cell.Multiplier, teams));
                    bool down = level > 1;
                    double shifted = Rate(plain.RunTeams(cell.Mode, level, cell.Shape, cell.Multiplier, teams, down ? -1 : 1));
                    baseSum += b;
                    upSum += down ? shifted - b : b - shifted;
                    for (int v = 0; v < variants.Count; v++)
                    {
                        variantSum[v] += Rate(sims[v].RunTeams(cell.Mode, level, cell.Shape, cell.Multiplier, teams)) - b;
                    }

                    count++;
                }

                baseRate[level] = count == 0 ? 0.0 : baseSum / count;
                levelGain[level] = count == 0 ? 0.0 : upSum / count;
                for (int v = 0; v < variants.Count; v++)
                {
                    gain[v, li] = count == 0 ? 0.0 : variantSum[v] / count;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("\n## PvE economy probe\n\n");
            sb.Append("Every cell replayed at its calibrated multiplier by every team against every composition (no scouting), with the\n");
            sb.Append("gear profile (`--gear`, GearKits) or the one consumable fielded, against the gearless, consumable-free base on the\n");
            sb.Append("same seeds. LE = the clear-rate gain / the gain of the team being one level above the enemies, pooled over the\n");
            sb.Append("level's cells. Targets (economy design): three commons ~0.7 LE, three rares ~1.2, three epics ~1.8, one\n");
            sb.Append("consumable at most ~0.3.\n\n");
            sb.Append("| Variant |");
            foreach (int level in levels)
            {
                sb.Append(" L").Append(level).Append(" pts | L").Append(level).Append(" LE |");
            }

            sb.Append(" Mean LE |\n| --- |");
            foreach (int _ in levels)
            {
                sb.Append(" ---: | ---: |");
            }

            sb.Append(" ---: |\n| (base clear rate) |");
            foreach (int level in levels)
            {
                sb.Append(' ').Append(Pct(baseRate[level])).Append("% | |");
            }

            sb.Append(" |\n| (one level up) |");
            foreach (int level in levels)
            {
                sb.Append(" +").Append(Pct(levelGain[level])).Append(" | 1.00 |");
            }

            sb.Append(" 1.00 |\n");
            for (int v = 0; v < variants.Count; v++)
            {
                sb.Append("| ").Append(names[v]).Append(" |");
                double sum = 0.0;
                for (int li = 0; li < levels.Count; li++)
                {
                    double le = levelGain[levels[li]] <= 0.0 ? 0.0 : gain[v, li] / levelGain[levels[li]];
                    sum += le;
                    sb.Append(' ').Append(gain[v, li] >= 0.0 ? "+" : string.Empty).Append(Pct(gain[v, li])).Append(" | ").Append(le.ToString("0.00", CultureInfo.InvariantCulture)).Append(" |");
                }

                sb.Append(' ').Append((sum / Math.Max(1, levels.Count)).ToString("0.00", CultureInfo.InvariantCulture)).Append(" |\n");
            }

            return sb.ToString();
        }

        /// <summary>Adds one variant per consumable (see the consumable library); none yet.</summary>
        private static void AddConsumableVariants(SimOptions options, List<string> names, List<Action<PveSimulator>> variants)
        {
        }

        private static double Rate(PveBattle[] battles)
        {
            int won = 0;
            foreach (PveBattle battle in battles)
            {
                won += battle.Outcome == BattleOutcome.PlayerVictory ? 1 : 0;
            }

            return battles.Length == 0 ? 0.0 : (double)won / battles.Length;
        }

        private static string Pct(double fraction)
        {
            return (fraction * 100.0).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
