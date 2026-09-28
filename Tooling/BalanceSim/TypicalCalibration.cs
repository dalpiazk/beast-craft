using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;
using BeastCraft.Encounters;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode typical</c>: the SHIPPING difficulty calibration (producer decision: the tiered targets are
    /// hit by a typical team, not by the scouted optimal pick). Per level band (<see cref="DefaultLevels"/>)
    /// and mainline shape, the multiplier at which the typical team from the owned roster
    /// (<see cref="TypicalTeamModel"/>: the Kinship flow, every trio, the median reasonable team) clears the
    /// shape's <c>TargetClear</c>, at the band's typical gear (<c>--gear typical</c>) and the pacing model's
    /// typical skill level (<see cref="SkillCurve.Typical"/>); the post-game shapes at level 100 (the whole roster
    /// owned); and each region boss template's <c>DifficultyOverride</c> at the boss's level (50%; the post-game
    /// twins 35% Normal / 20% Hard), on the boss region's own battlefields. <c>--write-difficulty</c> writes the
    /// table (elemental cells, the kit mode the game reads; <c>CalibratedOn</c> "typical"), <c>--out</c> the
    /// report with the spread check beside every cell: the weak pick (the roster's lower-quartile reasonable
    /// team), the scouted pick (the game's suggester over what the roster owns) and the strong pick (the scouted
    /// pick with upgraded skills, <see cref="SkillCurve.Upgraded"/>).
    /// <para>
    /// The per-beast balance guard is NOT this: it stays on the full-roster scouted pick at a uniform 50%
    /// (<c>--mode pve --seeds ... --target-clear 50</c>), gearless, as does the committed tuned report.
    /// </para>
    /// </summary>
    public static class TypicalCalibration
    {
        /// <summary>The level bands calibrated when <c>--levels</c> is not given: level 1 and every region boss's level.</summary>
        public static readonly int[] DefaultLevels = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

        /// <summary><c>--typical-samples</c> default: battles per team and composition at each search step.</summary>
        public const int DefaultSamples = 2;

        /// <summary>Seeded copies of a boss lineup (one lineup gives one battle otherwise).</summary>
        public const int BossCopies = NewPlayerReport.BossCopies;

        /// <summary>The post-game boss targets: Normal and Hard.</summary>
        public const double PostGameBossNormal = 35.0;

        public const double PostGameBossHard = 20.0;

        /// <summary>A mainline boss template's target.</summary>
        public const double BossTarget = 50.0;

        public static int Run(SimOptions options, List<CreatureSpeciesSO> species, Dictionary<string, GrowthRateCurve> curves)
        {
            Stopwatch clock = Stopwatch.StartNew();
            List<string> errors = new List<string>();
            EncounterCatalog catalog = EncounterLoader.Load(options, curves, errors);
            if (catalog == null)
            {
                return Fail("Encounters are invalid:", errors);
            }

            JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
            EncounterLibraryData encounterData = NewPlayerReport.Read<EncounterLibraryData>(options.EncounterLibraryPath, EncounterLibraryData.ProjectRelativePath, json, errors);
            EncounterDifficultyData before = NewPlayerReport.Read<EncounterDifficultyData>(options.NewPlayerDifficultyPath, EncounterDifficultyData.ProjectRelativePath, json, errors);
            RegionLibraryData regionData = NewPlayerReport.Read<RegionLibraryData>(options.RegionsPath, RegionLibraryData.ProjectRelativePath, json, errors);
            EnemyLibraryData enemyData = NewPlayerReport.Read<EnemyLibraryData>(options.EnemyLibraryPath, EnemyLibraryData.ProjectRelativePath, json, errors);
            DiscoveryLibraryData discoveryData = NewPlayerReport.Read<DiscoveryLibraryData>(null, DiscoveryLibraryData.ProjectRelativePath, json, errors);
            if (errors.Count > 0)
            {
                return Fail("Could not read the content:", errors);
            }

            if (options.GearKits == null)
            {
                options.GearKits = GearKits.Load(options.GearLibraryPath, species, errors);
                if (options.GearKits == null)
                {
                    return Fail("The gear library is invalid:", errors);
                }
            }

            SkillCurve skills = SkillCurve.Build(options, out string skillError);
            if (skills == null)
            {
                Console.Error.WriteLine(skillError);
                return 2;
            }

            curves.TryGetValue(enemyData.GrowthCurveId ?? string.Empty, out GrowthRateCurve curve);
            Context context = new Context
            {
                Options = options,
                Species = species,
                Skills = skills,
                Library = EncounterLibrary.Build(encounterData, EncounterDifficultyTable.Build(before)),
                Before = EncounterDifficultyTable.Build(before),
                Regions = RegionLibrary.Build(regionData),
                Enemies = EnemyCatalog.Build(enemyData, curve),
                Cache = new SimulatorCache(options, species)
            };

            PveSimulator probe = context.Simulator(1, options.ObstaclesRegion);
            if (probe == null)
            {
                return Fail("The skill library is invalid:", context.Errors);
            }

            context.Model = new TypicalTeamModel(species, probe.Teams, DiscoveryLibrary.Build(discoveryData), context.Regions);

            List<int> levels = new List<int>(options.LevelsGiven ? options.Levels : new List<int>(DefaultLevels));
            levels.Sort();
            List<Row> rows = new List<Row>();
            List<PveCell> cells = new List<PveCell>();
            foreach (int level in levels)
            {
                foreach (EncounterShape shape in catalog.Shapes)
                {
                    Row row = Calibrate(context, shape, level, options.TargetFor(shape), options.ObstaclesRegion, shape.Id);
                    rows.Add(row);
                    cells.Add(new PveCell { Mode = KitMode.Elemental, Level = level, Shape = shape, Multiplier = row.Multiplier });
                }
            }

            List<Row> postGameRows = new List<Row>();
            List<PveCell> postGameCells = new List<PveCell>();
            foreach (EncounterShape shape in catalog.PostGameShapes)
            {
                Row row = Calibrate(context, shape, PostGameCalibration.Level, options.TargetFor(shape), options.ObstaclesRegion, shape.Id);
                postGameRows.Add(row);
                postGameCells.Add(new PveCell { Mode = KitMode.Elemental, Level = PostGameCalibration.Level, Shape = shape, Multiplier = row.Multiplier });
            }

            List<Row> bossRows = new List<Row>();
            foreach (RegionData region in context.Regions.Regions)
            {
                if (!region.IsPostGame)
                {
                    bossRows.Add(Boss(context, region, region.BossTemplateId, region.MaxLevel, BossTarget));
                    continue;
                }

                bossRows.Add(Boss(context, region, region.BossTemplateId, region.MaxLevel, PostGameBossNormal));
                if (region.HardMode != null && !string.IsNullOrEmpty(region.HardMode.BossTemplateId))
                {
                    bossRows.Add(Boss(context, region, region.HardMode.BossTemplateId, region.MaxLevel, PostGameBossHard));
                }
            }

            bossRows.RemoveAll(r => r == null);
            string report = Build(context, catalog, levels, rows, postGameRows, bossRows).Replace("\r\n", "\n");
            Console.Out.Write(report);
            if (!string.IsNullOrEmpty(options.OutPath))
            {
                string outPath = Path.GetFullPath(options.OutPath);
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outPath, report, new UTF8Encoding(false));
                Console.Error.WriteLine("Report written to " + outPath);
            }

            if (!string.IsNullOrEmpty(options.WriteDifficultyPath))
            {
                SimOptions table = options.ForSeed(options.Seed);
                table.Modes = new List<KitMode> { KitMode.Elemental };
                table.Levels = levels;
                table.CalibrateOn = CalibrationTarget.Typical;
                DifficultyWriter.Write(options.WriteDifficultyPath, table, catalog, cells, postGameCells);
                Console.Error.WriteLine("Difficulty table written to " + Path.GetFullPath(options.WriteDifficultyPath));
            }

            Console.Error.WriteLine("Runtime: " + clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            return 0;
        }

        /// <summary>Everything the calibration shares: the content, the model and a simulator per (skill level, battlefield region).</summary>
        private sealed class Context
        {
            public SimOptions Options;
            public List<CreatureSpeciesSO> Species;
            public SkillCurve Skills;
            public EncounterLibrary Library;
            public EncounterDifficultyTable Before;
            public RegionLibrary Regions;
            public EnemyCatalog Enemies;
            public TypicalTeamModel Model;
            public SimulatorCache Cache;

            public List<string> Errors
            {
                get { return Cache.Errors; }
            }

            /// <summary>A simulator at <paramref name="skillLevel"/> on <paramref name="battlefieldRegion"/>'s layouts, in the run's gear.</summary>
            public PveSimulator Simulator(int skillLevel, string battlefieldRegion)
            {
                return Cache.Get(skillLevel, battlefieldRegion, Options.Gear);
            }
        }

        /// <summary>One calibrated cell (a shape at a level, or a boss template): the multiplier and the spread at it.</summary>
        private sealed class Row
        {
            public string Label;
            public string RegionId;
            public int Level;
            public double Target;
            public double Before;
            public double Multiplier;
            public TypicalTeamModel.Band Band;
            public int SkillTypical;
            public int SkillUpgraded;
            public TypicalTeamModel.Measure Measure;
            public double Scouted;
            public double ScoutedMin;
            public double ScoutedMax;
            public double Strong;
            public double StrongMin;
            public double StrongMax;
            public int Evaluations;
            public long Battles;
        }

        private static Row Calibrate(Context context, EncounterShape shape, int level, double target, string battlefield, string label)
        {
            Stopwatch clock = Stopwatch.StartNew();
            int typicalSkill = context.Skills.Typical(level);
            int upgradedSkill = context.Skills.Upgraded(level);
            PveSimulator pve = context.Simulator(typicalSkill, battlefield);
            TypicalTeamModel.Band band = context.Model.BandAt(level);
            int samples = context.Options.TypicalSamples;
            Row row = new Row
            {
                Label = label,
                Level = level,
                Target = target,
                Band = band,
                SkillTypical = typicalSkill,
                SkillUpgraded = upgradedSkill,
                Before = context.Before.HasShape(shape.Id) ? context.Before.Multiplier(shape.Id, level) : double.NaN
            };

            Dictionary<double, TypicalTeamModel.Measure> seen = new Dictionary<double, TypicalTeamModel.Measure>();
            double Rate(double multiplier)
            {
                if (!seen.TryGetValue(multiplier, out TypicalTeamModel.Measure measure))
                {
                    measure = context.Model.Evaluate(pve, band, shape, multiplier, samples);
                    seen.Add(multiplier, measure);
                    row.Evaluations++;
                    row.Battles += measure.Battles;
                }

                return measure.Typical;
            }

            row.Multiplier = Search(target, Rate);
            row.Measure = seen[row.Multiplier];
            row.Scouted = context.Model.EvaluateScouted(pve, context.Options, band, shape, row.Multiplier, samples, out row.ScoutedMin, out row.ScoutedMax);
            PveSimulator upgraded = context.Simulator(upgradedSkill, battlefield);
            row.Strong = context.Model.EvaluateScouted(upgraded, context.Options, band, shape, row.Multiplier, samples, out row.StrongMin, out row.StrongMax);
            Console.Error.WriteLine("Typical " + label + "/L" + level + ": x" + SimOptions.FormatMultiplier(row.Multiplier) + " typical " + SimOptions.Format(row.Measure.Typical) +
                                    "% (target " + SimOptions.Format(target) + "%), weak " + SimOptions.Format(row.Measure.Weak) + "%, scouted " + SimOptions.Format(row.Scouted) +
                                    "%, strong " + SimOptions.Format(row.Strong) + "%; " + band.Entries.Count + " teams, " + row.Evaluations + " steps, " +
                                    clock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            return row;
        }

        private static Row Boss(Context context, RegionData region, string templateId, int level, double target)
        {
            EncounterPlan plan = EncounterPlan.FromTemplate(context.Library, context.Enemies, templateId, level);
            if (plan == null)
            {
                return null;
            }

            EncounterShape shape = NewPlayerReport.TemplateShape(context.Enemies, plan);
            Row row = Calibrate(context, shape, level, target, region.RegionId, templateId);
            row.RegionId = region.RegionId;
            row.Before = plan.Multiplier;
            return row;
        }

        /// <summary>
        /// The multiplier whose rate (<paramref name="rateAt"/>, falling as the multiplier rises) is closest to
        /// <paramref name="target"/>, as <see cref="PveSimulator.RunCell"/> searches: from 1, double or halve until
        /// the target is bracketed (within <see cref="SimOptions.MinMultiplier"/>-<see cref="SimOptions.MaxMultiplier"/>),
        /// then <see cref="SimOptions.CalibrationBisections"/> bisections; the closest evaluated wins (the first on a tie).
        /// </summary>
        public static double Search(double target, Func<double, double> rateAt)
        {
            double best = 1.0;
            double bestGap = double.MaxValue;
            double Evaluate(double m)
            {
                double rate = rateAt(m);
                double gap = Math.Abs(rate - target);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = m;
                }

                return rate;
            }

            double easy = 1.0;
            double hard = 1.0;
            double atOne = Evaluate(1.0);
            if (atOne > target)
            {
                double m = 1.0;
                double rate = atOne;
                while (rate > target && m < SimOptions.MaxMultiplier)
                {
                    easy = m;
                    m *= 2.0;
                    rate = Evaluate(m);
                }

                hard = m;
                if (rate > target)
                {
                    easy = hard;
                }
            }
            else if (atOne < target)
            {
                double m = 1.0;
                double rate = atOne;
                while (rate < target && m > SimOptions.MinMultiplier)
                {
                    hard = m;
                    m /= 2.0;
                    rate = Evaluate(m);
                }

                easy = m;
                if (rate < target)
                {
                    hard = easy;
                }
            }

            for (int step = 0; step < SimOptions.CalibrationBisections && bestGap > 0.0 && hard > easy; step++)
            {
                double middle = (easy + hard) / 2.0;
                if (Evaluate(middle) > target)
                {
                    easy = middle;
                }
                else
                {
                    hard = middle;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ the report

        private static string Build(Context context, EncounterCatalog catalog, List<int> levels, List<Row> rows, List<Row> postGame, List<Row> bosses)
        {
            SimOptions options = context.Options;
            StringBuilder sb = new StringBuilder();
            sb.Append("# Beast Craft typical-team report\n\n");
            sb.Append("Generated by `dotnet run --project Tooling/BalanceSim -c Release -- --mode typical --gear ").Append(options.Gear.ToString().ToLowerInvariant())
              .Append(" --write-difficulty content/data/Encounters/encounter-difficulty.json --out docs/balance/typical-team-report.md`\n");
            sb.Append("(see Tooling/BalanceSim/README.md, \"Typical team\"). The shipping difficulty (`encounter-difficulty.json`) and the region bosses'\n");
            sb.Append("`DifficultyOverride`s, calibrated so a TYPICAL team (a sensible, not optimal pick from the beasts the player owns there) clears\n");
            sb.Append("each kind of fight's target; beside every cell, the spread a weaker and a stronger pick see. This is not the per-beast\n");
            sb.Append("balance guard, which stays on the full-roster scouted pick at 50% (`--mode pve --seeds ... --target-clear 50`).\n\n");

            sb.Append("## Configuration\n\n");
            sb.Append("- Owned roster (the Kinship flow): every one-per-stance trio (").Append(context.Model.Trios.Length)
              .Append(", the Hearthglen trio), plus one beast of every Kinship site whose trial level is at most the band's level (trio k\n");
            sb.Append("  takes offer (k + site) mod 2 of the site's two: both choices, half the trios each). The trio at the band's level (the pacing\n");
            sb.Append("  model's fielded median), each recruit the campaign model's bench gap below it (").Append(string.Join(" / ", Array.ConvertAll(TypicalTeamModel.RecruitLag, l => l.ToString(CultureInfo.InvariantCulture))))
              .Append(" levels in r01 / r02 / ..., the last after).\n");
            sb.Append("- Reasonable teams: three owned beasts covering at least two stances, at least one a Vanguard (no screen for the Ranged\n");
            sb.Append("  without one; a single-stance team gives up the stance system).\n");
            sb.Append("- **Typical** (calibrated): per roster the median reasonable team's clear rate (each team over every composition, at its\n");
            sb.Append("  members' levels, default placement, no scouting), the mean over the rosters (lowest-highest roster). **Weak**: the roster's\n");
            sb.Append("  lower-quartile team. **Best**: the roster's best team (the luckiest of its teams: an upper bound). **Scouted**: the game's\n");
            sb.Append("  `TeamSuggester` over what the roster owns, per composition. **Strong**: the scouted pick with upgraded skills (the focus\n");
            sb.Append("  skill's level on every skill). Placement is the simulator's default in every column (a better placement is not modelled).\n");
            sb.Append("- Skills: every beast and avatar skill at the pacing model's typical team level (`--mode pacing`, \"Team skill level\");\n");
            sb.Append("  the strong pick at the upgraded level. Gear: `--gear ").Append(options.Gear.ToString().ToLowerInvariant())
              .Append("` at the band's level. The library avatar at the band's level.\n");
            sb.Append("- Encounters: ").Append(options.Compositions).Append(" generated compositions per shape (seed ").Append(options.Seed.ToString(CultureInfo.InvariantCulture))
              .Append("), ").Append(options.TypicalSamples).Append(" battle(s) per team and composition at every step; `--obstacles ").Append(options.ObstaclesRegion ?? "none")
              .Append("` for the generated shapes (the table is region-agnostic), each boss on its own region's battlefields.\n");
            sb.Append("- Search: from x1, doubling or halving to bracket the target, then ").Append(SimOptions.CalibrationBisections)
              .Append(" bisections; the closest evaluated multiplier wins. Elemental cells only (the game reads them).\n");
            sb.Append("- Before = the table (or override) this run read; After = this run's.\n\n");

            sb.Append("## Level bands\n\n");
            sb.Append("| Level | Region | Owned | Recruit lag | Reasonable teams per roster | Skill level (typical / upgraded) |\n");
            sb.Append("| ---: | --- | ---: | ---: | --- | --- |\n");
            foreach (int level in levels)
            {
                TypicalTeamModel.Band band = context.Model.BandAt(level);
                band.TeamCounts(out int min, out int max);
                sb.Append("| ").Append(level).Append(" | ").Append(band.Region == null ? "-" : band.Region.RegionId).Append(" | ").Append(3 + band.Sites).Append(" | ")
                  .Append(band.Sites == 0 ? "-" : band.Lag.ToString(CultureInfo.InvariantCulture)).Append(" | ").Append(min == max ? min.ToString(CultureInfo.InvariantCulture) : min + "-" + max)
                  .Append(" | ").Append(context.Skills.Typical(level)).Append(" / ").Append(context.Skills.Upgraded(level)).Append(" |\n");
            }

            sb.Append("\n## Calibrated table (generated shapes)\n\n");
            sb.Append("Clear rates at the After multiplier. Typical is the calibrated number (target); weak / scouted / strong are the spread check.\n\n");
            RowTable(sb, rows, false);

            sb.Append("\n### Spread by shape\n\n");
            sb.Append("Means over the level bands from 10 (at level 1 the roster is the trio alone: one team, so every pick is the same).\n\n");
            sb.Append("| Shape | Target | Typical | Weak | Best | Scouted | Strong | Multiplier, before -> after (mean) |\n");
            sb.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");
            foreach (EncounterShape shape in catalog.Shapes)
            {
                List<Row> mine = rows.FindAll(r => r.Label == shape.Id && r.Level >= 10);
                if (mine.Count == 0)
                {
                    continue;
                }

                double typical = 0.0, weak = 0.0, best = 0.0, scouted = 0.0, strong = 0.0, before = 0.0, after = 0.0;
                foreach (Row r in mine)
                {
                    typical += r.Measure.Typical / mine.Count;
                    weak += r.Measure.Weak / mine.Count;
                    best += r.Measure.Best / mine.Count;
                    scouted += r.Scouted / mine.Count;
                    strong += r.Strong / mine.Count;
                    before += r.Before / mine.Count;
                    after += r.Multiplier / mine.Count;
                }

                sb.Append("| `").Append(shape.Id).Append("` | ").Append(SimOptions.Format(mine[0].Target)).Append("% | ").Append(SimOptions.Format(typical)).Append("% | ")
                  .Append(SimOptions.Format(weak)).Append("% | ").Append(SimOptions.Format(best)).Append("% | ").Append(SimOptions.Format(scouted)).Append("% | ")
                  .Append(SimOptions.Format(strong)).Append("% | x").Append(SimOptions.FormatMultiplier(before)).Append(" -> x").Append(SimOptions.FormatMultiplier(after)).Append(" |\n");
            }

            if (postGame.Count > 0)
            {
                sb.Append("\n## Post-game shapes (level ").Append(PostGameCalibration.Level).Append(")\n\n");
                RowTable(sb, postGame, false);
            }

            if (bosses.Count > 0)
            {
                sb.Append("\n## Region bosses (`DifficultyOverride`)\n\n");
                sb.Append("Each boss template at its region's max level, ").Append(BossCopies).Append(" seeded copies of the lineup, on its region's battlefields;\n");
                sb.Append("target ").Append(SimOptions.Format(BossTarget)).Append("% (the post-game twins ").Append(SimOptions.Format(PostGameBossNormal)).Append("% Normal, ")
                  .Append(SimOptions.Format(PostGameBossHard)).Append("% Hard). After = the override to author in `encounter-library.json`, rounded to three places.\n\n");
                RowTable(sb, bosses, true);
            }

            return sb.ToString();
        }

        private static void RowTable(StringBuilder sb, List<Row> rows, bool bosses)
        {
            sb.Append(bosses ? "| Boss | Region | Level | Target | Before | After | Typical | Weak | Best | Scouted | Strong |\n| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n"
                             : "| Shape | Level | Target | Before | After | Typical | Weak | Best | Scouted | Strong |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (Row r in rows)
            {
                sb.Append("| `").Append(r.Label).Append("` | ");
                if (bosses)
                {
                    sb.Append(r.RegionId).Append(" | ");
                }

                sb.Append(r.Level).Append(" | ").Append(SimOptions.Format(r.Target)).Append("% | ")
                  .Append(double.IsNaN(r.Before) ? "-" : "x" + SimOptions.FormatMultiplier(r.Before)).Append(" | x")
                  .Append(bosses ? r.Multiplier.ToString("0.000", CultureInfo.InvariantCulture) : SimOptions.FormatMultiplier(r.Multiplier)).Append(" | ")
                  .Append(Spread(r.Measure.Typical, r.Measure.TypicalMin, r.Measure.TypicalMax)).Append(" | ").Append(SimOptions.Format(r.Measure.Weak)).Append("% | ")
                  .Append(SimOptions.Format(r.Measure.Best)).Append("% | ").Append(Spread(r.Scouted, r.ScoutedMin, r.ScoutedMax)).Append(" | ")
                  .Append(Spread(r.Strong, r.StrongMin, r.StrongMax)).Append(" |\n");
            }
        }

        private static string Spread(double mean, double min, double max)
        {
            return SimOptions.Format(mean) + "% (" + SimOptions.Format(min) + "-" + SimOptions.Format(max) + ")";
        }

        private static int Fail(string heading, List<string> problems)
        {
            Console.Error.WriteLine(heading);
            foreach (string problem in problems)
            {
                Console.Error.WriteLine("  " + problem);
            }

            return 2;
        }
    }
}
