using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Two opt-in diagnostic sections. "PvE avatar value" (<c>--avatar-value</c>): what the avatar is
    /// worth, in points of the scouted clear rate (the picked teams with the avatar, at the calibrated
    /// multiplier, minus the same battles without it), and its direct share of the team's output
    /// (damage, healing and shield soak; see <see cref="PveBattle.AvatarDamage"/>). "PvE beast turns"
    /// (<c>--turn-detail</c>): each beast's no-fire turns and its damage to large enemies (bosses)
    /// versus the rest. Both also over seeds for <c>--seeds</c>. Pure: output depends only on the cells.
    /// </summary>
    public static class AvatarValueReport
    {
        /// <summary>The single-seed sections; nothing without the options.</summary>
        public static void AppendSection(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator,
                                         List<PveCell> cells)
        {
            if (options.AvatarValue)
            {
                report.AppendLine("### PvE avatar value");
                report.AppendLine();
                AppendValue(report, options, new List<List<PveCell>> { cells }, "####");
            }

            if (options.TurnDetail)
            {
                report.AppendLine("### PvE beast turns");
                report.AppendLine();
                AppendTurns(report, options, species, new List<PveSimulator> { simulator }, new List<List<PveCell>> { cells }, "####");
            }
        }

        /// <summary>The <c>--seeds</c> sections: avatar value = mean over seeds; beast turns = pooled over seeds.</summary>
        public static void AppendAggregate(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int> seeds,
                                           List<PveSimulator> simulators, List<List<PveCell>> cells)
        {
            if (options.AvatarValue)
            {
                report.AppendLine("## PvE avatar value over seeds");
                report.AppendLine();
                report.AppendLine("Every number is the mean over the " + seeds.Count + " seeds (" + SimOptions.Join(seeds) + ") of that seed's \"PvE avatar value\" number.");
                report.AppendLine();
                AppendValue(report, options, cells, "###");
            }

            if (options.TurnDetail)
            {
                report.AppendLine("## PvE beast turns over seeds");
                report.AppendLine();
                report.AppendLine("Pooled over the " + seeds.Count + " seeds (" + SimOptions.Join(seeds) + ").");
                report.AppendLine();
                AppendTurns(report, options, species, simulators, cells, "###");
            }
        }

        private sealed class ValueCell
        {
            public double With;
            public double Without;
            public double AvatarTurns;
            public double Share;
            public double DamageShare;
            public double HealShare;
            public double ShieldShare;
            public double PassiveShare;
        }

        private static ValueCell Measure(PveCell cell)
        {
            if (cell.PickedBattles == null || double.IsNaN(cell.NoAvatarScoutedClearRate))
            {
                return null;
            }

            long avatarDamage = 0;
            long avatarHeal = 0;
            long avatarShield = 0;
            long avatarPassive = 0;
            long total = 0;
            long turns = 0;
            foreach (PveBattle battle in cell.PickedBattles)
            {
                avatarDamage += battle.AvatarDamage;
                avatarHeal += battle.AvatarHeal;
                avatarShield += battle.AvatarShieldAbsorbed;
                avatarPassive += battle.AvatarPassiveDamage + battle.AvatarPassiveShieldAbsorbed;
                total += battle.AvatarDamage + battle.AvatarHeal + battle.AvatarShieldAbsorbed + battle.BeastDamage + battle.BeastHeal + battle.BeastShieldAbsorbed;
                turns += battle.AvatarTurns;
            }

            double denominator = total > 0 ? total : 1;
            return new ValueCell
            {
                With = cell.ScoutedClearRate,
                Without = cell.NoAvatarScoutedClearRate,
                AvatarTurns = cell.PickedBattles.Length == 0 ? 0.0 : (double)turns / cell.PickedBattles.Length,
                Share = 100.0 * (avatarDamage + avatarHeal + avatarShield) / denominator,
                DamageShare = 100.0 * avatarDamage / denominator,
                HealShare = 100.0 * avatarHeal / denominator,
                ShieldShare = 100.0 * avatarShield / denominator,
                PassiveShare = 100.0 * avatarPassive / denominator
            };
        }

        private static ValueCell Mean(List<List<PveCell>> seeds, KitMode mode, List<string> shapeIds, List<int> levels)
        {
            ValueCell sum = new ValueCell();
            int count = 0;
            foreach (List<PveCell> cells in seeds)
            {
                foreach (PveCell cell in cells)
                {
                    if (cell.Mode != mode || !levels.Contains(cell.Level) || !shapeIds.Contains(cell.Shape.Id))
                    {
                        continue;
                    }

                    ValueCell value = Measure(cell);
                    if (value == null)
                    {
                        continue;
                    }

                    sum.With += value.With;
                    sum.Without += value.Without;
                    sum.AvatarTurns += value.AvatarTurns;
                    sum.Share += value.Share;
                    sum.DamageShare += value.DamageShare;
                    sum.HealShare += value.HealShare;
                    sum.ShieldShare += value.ShieldShare;
                    sum.PassiveShare += value.PassiveShare;
                    count++;
                }
            }

            if (count == 0)
            {
                return null;
            }

            return new ValueCell
            {
                With = sum.With / count,
                Without = sum.Without / count,
                AvatarTurns = sum.AvatarTurns / count,
                Share = sum.Share / count,
                DamageShare = sum.DamageShare / count,
                HealShare = sum.HealShare / count,
                ShieldShare = sum.ShieldShare / count,
                PassiveShare = sum.PassiveShare / count
            };
        }

        private static List<string> ShapeIds(List<List<PveCell>> seeds)
        {
            List<string> ids = new List<string>();
            foreach (PveCell cell in seeds[0])
            {
                if (!ids.Contains(cell.Shape.Id))
                {
                    ids.Add(cell.Shape.Id);
                }
            }

            return ids;
        }

        private static void AppendValue(StringBuilder report, SimOptions options, List<List<PveCell>> seeds, string heading)
        {
            List<string> shapeIds = ShapeIds(seeds);
            int samples = seeds[0].Count == 0 ? 0 : seeds[0][0].CalibrationSamples;
            report.AppendLine("The team the " + PveReport.PickerName(options) + " fields against each composition, " + samples +
                              " battles per composition at the calibrated multiplier, with the avatar (the calibrated");
            report.AppendLine("scouted rate) and replayed without it (`--avatar-value`; same seeds, same enemies). **Value** = with - without, points of clear rate.");
            report.AppendLine("**Direct share** = the avatar's damage + healing + shield soak as a percent of the team's (beasts and avatar) total over");
            report.AppendLine("the with-avatar battles: damage is enemy HP taken (the avatar's own turns and its passives' hits), healing is team HP restored,");
            report.AppendLine("shield soak is damage a shield absorbed, credited to the shield's caster (damage over time soaked by a shield is not counted).");
            report.AppendLine("**From passives** = the part of the direct share its passives produced (passive shields' soak, passive hits).");
            report.AppendLine("Auras and buffs count only through what the beasts then do, so the share is the avatar's *direct* output, not its value.");
            report.AppendLine();

            foreach (KitMode mode in options.Modes)
            {
                report.AppendLine(heading + " Avatar value: `" + SimOptions.ModeName(mode) + "`");
                report.AppendLine();
                report.AppendLine("| Shape | Level | With avatar % | Without % | Value (pts) | Avatar turns / battle | Direct share % | damage % | heal % | shield % | from passives % |");
                report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                foreach (string shapeId in shapeIds)
                {
                    foreach (int level in options.Levels)
                    {
                        AppendValueRow(report, "`" + shapeId + "`", level.ToString(CultureInfo.InvariantCulture),
                                       Mean(seeds, mode, new List<string> { shapeId }, new List<int> { level }));
                    }
                }

                foreach (string shapeId in shapeIds)
                {
                    AppendValueRow(report, "`" + shapeId + "`", "all", Mean(seeds, mode, new List<string> { shapeId }, options.Levels));
                }

                foreach (int level in options.Levels)
                {
                    AppendValueRow(report, "**All shapes**", level.ToString(CultureInfo.InvariantCulture), Mean(seeds, mode, shapeIds, new List<int> { level }));
                }

                AppendValueRow(report, "**All shapes**", "**all**", Mean(seeds, mode, shapeIds, options.Levels));
                report.AppendLine();
            }
        }

        private static void AppendValueRow(StringBuilder report, string shape, string level, ValueCell value)
        {
            if (value == null)
            {
                return;
            }

            report.AppendLine("| " + shape + " | " + level + " | " + SimOptions.Format(value.With) + " | " + SimOptions.Format(value.Without) + " | " +
                              Signed(value.With - value.Without) + " | " + value.AvatarTurns.ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                              SimOptions.Format(value.Share) + " | " + SimOptions.Format(value.DamageShare) + " | " + SimOptions.Format(value.HealShare) + " | " +
                              SimOptions.Format(value.ShieldShare) + " | " + SimOptions.Format(value.PassiveShare) + " |");
        }

        private static string Signed(double value)
        {
            string text = SimOptions.Format(value);
            return value > 0.0 && !text.StartsWith("-", StringComparison.Ordinal) && text != "0.0" ? "+" + text : text;
        }

        private static void AppendTurns(StringBuilder report, SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<PveSimulator> simulators,
                                        List<List<PveCell>> seeds, string heading)
        {
            List<string> shapeIds = ShapeIds(seeds);
            report.AppendLine("Every team's battles at the calibrated multiplier (the no-scouting run), levels pooled; per beast, over the battles it fought in.");
            report.AppendLine("**No fire** = its turns on which no skill fired (stunned turns not counted), split into **held** (a skill held by the beast's");
            report.AppendLine("stance: a Skirmisher or Ranged beast that would have had to step next to an enemy) and **out of reach** (no target reachable");
            report.AppendLine("this turn); the rest of no-fire turns had every skill on cooldown or no candidate. **To bosses** = the share of the enemy HP");
            report.AppendLine("it took on its own turns that came off a large enemy (giant, champion); `—` in shapes without one.");
            report.AppendLine();

            foreach (KitMode mode in options.Modes)
            {
                foreach (string shapeId in shapeIds)
                {
                    report.AppendLine(heading + " Beast turns: `" + SimOptions.ModeName(mode) + "` `" + shapeId + "`");
                    report.AppendLine();
                    report.AppendLine("| Beast | Turns / battle | No fire % | held % | out of reach % | stunned % | Damage / battle | To bosses % |");
                    report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                    for (int b = 0; b < species.Count; b++)
                    {
                        long battles = 0;
                        long turns = 0;
                        long noFire = 0;
                        long held = 0;
                        long unreachable = 0;
                        long stunned = 0;
                        long boss = 0;
                        long escort = 0;
                        for (int s = 0; s < seeds.Count; s++)
                        {
                            PveSimulator simulator = simulators[s];
                            foreach (PveCell cell in seeds[s])
                            {
                                if (cell.Mode != mode || cell.Shape.Id != shapeId || cell.Battles == null)
                                {
                                    continue;
                                }

                                for (int i = 0; i < cell.Battles.Length; i++)
                                {
                                    PveBattle battle = cell.Battles[i];
                                    if (battle.MemberNoFireTurns == null)
                                    {
                                        continue;
                                    }

                                    int member = Array.IndexOf(simulator.Teams[cell.TeamOf(i)], b);
                                    if (member < 0)
                                    {
                                        continue;
                                    }

                                    battles++;
                                    turns += battle.MemberActions[member];
                                    noFire += battle.MemberNoFireTurns[member];
                                    held += battle.MemberHeldTurns[member];
                                    unreachable += battle.MemberUnreachableTurns[member];
                                    stunned += battle.MemberStunnedTurns[member];
                                    boss += battle.MemberBossDamage[member];
                                    escort += battle.MemberEscortDamage[member];
                                }
                            }
                        }

                        if (battles == 0)
                        {
                            continue;
                        }

                        double t = turns > 0 ? turns : 1;
                        report.AppendLine("| " + species[b].DisplayName + " | " + ((double)turns / battles).ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                                          SimOptions.Format(100.0 * noFire / t) + " | " + SimOptions.Format(100.0 * held / t) + " | " +
                                          SimOptions.Format(100.0 * unreachable / t) + " | " + SimOptions.Format(100.0 * stunned / t) + " | " +
                                          ((double)(boss + escort) / battles).ToString("0", CultureInfo.InvariantCulture) + " | " +
                                          (boss == 0 ? "—" : SimOptions.Format(100.0 * boss / (boss + escort))) + " |");
                    }

                    report.AppendLine();
                }
            }
        }
    }
}
