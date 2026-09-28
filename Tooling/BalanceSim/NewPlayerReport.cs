using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BeastCraft.Battle;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;
using BeastCraft.Encounters;
using BeastCraft.Save;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode newplayer</c>: how a NEW player fares in the first regions, with and without the
    /// early-region easing (<c>regions.json</c> <c>StageEasing</c> of <c>EasingShapeScales</c>), and the first-node
    /// experience. Fought with the PvE simulator's own battles (the calibration's code path), so a
    /// rate here and a calibrated rate are the same kind of number.
    /// <para>
    /// <strong>The new-player profile.</strong> Three starters, one per stance (every such trio of
    /// the roster is fielded: min, mean and max over them), all three fielded (the only pick three
    /// beasts allow), no scouting, beside the library avatar as calibrated; team and avatar at the
    /// node's level (the campaign pacing model: the fielded median sits at the node level at every
    /// gate and boss); gear per <see cref="GearFor"/>: none through r01-r02, typical from r03. The
    /// calibration assumes typical gear and a scouted pick from the whole roster.
    /// </para>
    /// <para>
    /// <strong>Per stage</strong> of r01-r03, each shape a stage draws (its battle shapes and the
    /// elite shape, the elite one level up) at the stage's middle row level, at the shipping table's
    /// multiplier (<c>encounter-difficulty.json</c>, <see cref="EncounterLibrary.Multiplier"/>): the
    /// clear rate at scale 1 (off), at the authored stage scale (on), and the scale the trio mean
    /// needs to reach its shape's target (a bisection on the scale; lower = easier).
    /// </para>
    /// <para>
    /// <strong>First node.</strong> For map seeds 1..<c>--map-seeds</c>, a fresh save's first r01
    /// expedition (<see cref="CampaignRules.StartRun(PlayerSave, RegionLibrary, string, int)"/>) and its "Next
    /// battle" node (the lowest-level reachable battle, a plain battle before a den, then the furthest
    /// left: the map's <c>Recommended</c>), planned as the game plans it
    /// (<see cref="CampaignRules.PlanFor(MapRun, MapNode, EncounterLibrary, EnemyCatalog, RegionLibrary)"/>),
    /// fought by every trio with and without the easing.
    /// </para>
    /// <para>
    /// <strong>Roster growth</strong> (the Kinship flow, <c>discovery.json</c>): the same stages of r01-r03
    /// fought by the player who owns the trio plus the Kinship recruits reached by then (each site's first
    /// offered beast, <see cref="KinshipRules.Offer(KinshipSiteData, ICollection{string}, IReadOnlyList{string})"/>:
    /// four beasts in r01, five in r02, six in r03), picking three per composition with the scouted
    /// heuristic over what it owns (the preview is free), the recruits fighting
    /// <see cref="RecruitLag"/> levels below the trio (the campaign model's bench gap at the region's
    /// boss). <strong>Kinship trials</strong>: each site's trial at its middle level against every trio
    /// with the recruits of the sites before it, at the template's own multiplier, the weakest trio at
    /// least <see cref="TrialFloor"/>%; <c>--tune</c> bisects each template's multiplier for it.
    /// </para>
    /// </summary>
    public static class NewPlayerReport
    {
        /// <summary><c>--map-seeds</c> default: the first-node section's map seeds (1..n).</summary>
        public const int DefaultMapSeeds = 60;

        /// <summary>The regions the per-stage section covers: the eased ones and the first one after.</summary>
        public static readonly string[] Regions = { "r01", "r02", "r03" };

        /// <summary>The first region the new-player profile wears typical gear in.</summary>
        public const string TypicalGearFrom = "r03";

        /// <summary>The map row a stage's representative battle level is read at (rows 0-9 are its battles, 10 the gate).</summary>
        public const int MiddleRow = 5;

        /// <summary>Scale bisection: the easiest scale searched and the number of halvings.</summary>
        public const double MinScale = 0.30;

        public const int ScaleBisections = 7;

        /// <summary>Levels below the trio a Kinship recruit fights at, by region (r01, r02, r03, ...): the campaign model's bench gap at the region's boss.</summary>
        public static readonly int[] RecruitLag = { 3, 4, 5, 5, 5, 5 };

        /// <summary>
        /// A Kinship trial's floor, percent: the WEAKEST trio (with the recruits it has by then) clears it at
        /// least this often (a loss just retries, so no roster may be walled; the mean runs higher).
        /// </summary>
        public const double TrialFloor = 50.0;

        /// <summary>The map row a Kinship trial's measured level is read at (sites sit above rows 2-6).</summary>
        public const int TrialRow = 4;

        public static int Run(SimOptions options, List<CreatureSpeciesSO> species, Dictionary<string, GrowthRateCurve> curves)
        {
            List<string> errors = new List<string>();
            if (options.TeamSize != 3)
            {
                Console.Error.WriteLine("--mode newplayer fields one beast per stance: --team-size must be 3.");
                return 1;
            }

            EncounterCatalog catalog = EncounterLoader.Load(options, curves, errors);
            if (catalog == null)
            {
                return Fail("Encounters are invalid:", errors);
            }

            JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
            EncounterLibraryData encounterData = Read<EncounterLibraryData>(options.EncounterLibraryPath, EncounterLibraryData.ProjectRelativePath, json, errors);
            EncounterDifficultyData difficultyData = Read<EncounterDifficultyData>(options.NewPlayerDifficultyPath, EncounterDifficultyData.ProjectRelativePath, json, errors);
            RegionLibraryData regionData = Read<RegionLibraryData>(options.RegionsPath, RegionLibraryData.ProjectRelativePath, json, errors);
            EnemyLibraryData enemyData = Read<EnemyLibraryData>(options.EnemyLibraryPath, EnemyLibraryData.ProjectRelativePath, json, errors);
            DiscoveryLibraryData discoveryData = Read<DiscoveryLibraryData>(null, DiscoveryLibraryData.ProjectRelativePath, json, errors);
            if (errors.Count > 0)
            {
                return Fail("Could not read the content:", errors);
            }

            errors.AddRange(RegionLibraryValidator.Validate(regionData, encounterData));
            errors.AddRange(EncounterDifficultyTable.Validate(difficultyData, encounterData));
            if (errors.Count > 0)
            {
                return Fail("The content is invalid:", errors);
            }

            if (options.GearKits == null)
            {
                options.GearKits = GearKits.Load(options.GearLibraryPath, species, errors);
                if (options.GearKits == null)
                {
                    return Fail("The gear library is invalid:", errors);
                }
            }

            curves.TryGetValue(enemyData.GrowthCurveId ?? string.Empty, out GrowthRateCurve curve);
            EnemyCatalog enemies = EnemyCatalog.Build(enemyData, curve);
            EncounterLibrary library = EncounterLibrary.Build(encounterData, EncounterDifficultyTable.Build(difficultyData));
            RegionLibrary regions = RegionLibrary.Build(regionData);

            Context context = new Context
            {
                Options = options,
                Species = species,
                Catalog = catalog,
                Library = library,
                Regions = regions,
                Enemies = enemies,
                Discovery = DiscoveryLibrary.Build(discoveryData)
            };

            PveSimulator probe = new PveSimulator(options, species);
            context.Trios = Trios(probe.Teams, species);

            StringBuilder report = new StringBuilder();
            Header(report, context);
            if (!options.NewPlayerKinshipOnly)
            {
                PerStage(report, context);
                FirstNode(report, context);
            }

            RosterGrowth(report, context);
            KinshipTrials(report, context);

            string text = report.ToString().Replace("\r\n", "\n");
            Console.Out.Write(text);
            if (!string.IsNullOrEmpty(options.OutPath))
            {
                string outPath = Path.GetFullPath(options.OutPath);
                string directory = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outPath, text, new UTF8Encoding(false));
                Console.Error.WriteLine("Report written to " + outPath);
            }

            return 0;
        }

        private sealed class Context
        {
            public SimOptions Options;
            public List<CreatureSpeciesSO> Species;
            public EncounterCatalog Catalog;
            public EncounterLibrary Library;
            public RegionLibrary Regions;
            public EnemyCatalog Enemies;
            public int[] Trios;
            public DiscoveryLibrary Discovery;
            public readonly Dictionary<string, PveSimulator> Simulators = new Dictionary<string, PveSimulator>(StringComparer.Ordinal);

            /// <summary>A simulator fighting on <paramref name="regionId"/>'s battlefields (as the game does) in the profile's gear there.</summary>
            public PveSimulator For(string regionId)
            {
                if (!Simulators.TryGetValue(regionId, out PveSimulator pve))
                {
                    SimOptions copy = Options.ForSeed(Options.Seed);
                    copy.ObstaclesRegion = regionId;
                    pve = new PveSimulator(copy, Species);
                    GearProfile profile = GearFor(regionId);
                    GearKits kits = Options.GearKits;
                    pve.GearFor = profile == GearProfile.None ? null : (s, l) => kits.For(s, l, profile);
                    Simulators.Add(regionId, pve);
                }

                return pve;
            }
        }

        /// <summary>The new-player profile's gear in <paramref name="regionId"/>: none before <see cref="TypicalGearFrom"/>, typical from it on.</summary>
        public static GearProfile GearFor(string regionId)
        {
            return string.CompareOrdinal(regionId, TypicalGearFrom) >= 0 ? GearProfile.Typical : GearProfile.None;
        }

        /// <summary>The indices of <paramref name="teams"/> that field exactly one beast of each stance.</summary>
        public static int[] Trios(List<int[]> teams, IReadOnlyList<CreatureSpeciesSO> species)
        {
            List<int> trios = new List<int>();
            for (int t = 0; t < teams.Count; t++)
            {
                HashSet<CombatStance> stances = new HashSet<CombatStance>();
                foreach (int s in teams[t])
                {
                    stances.Add(species[s].Stance);
                }

                if (teams[t].Length == 3 && stances.Count == 3)
                {
                    trios.Add(t);
                }
            }

            return trios.ToArray();
        }

        /// <summary>Per-trio clear rates (percent) of <paramref name="shape"/> at <paramref name="multiplier"/>, and their mean.</summary>
        private static double[] Rates(Context context, PveSimulator pve, int level, EncounterShape shape, double multiplier, out double mean)
        {
            // --start-level: the team (and the avatar) never below it, the enemies at the node's level.
            int team = Math.Max(level, context.Options.NewPlayerStartLevel);
            PveBattle[] battles = pve.RunTeams(KitMode.Elemental, team, shape, multiplier, context.Trios, level - team);
            int count = context.Trios.Length;
            int samples = pve.Samples;
            int[] cleared = new int[count];
            int[] fought = new int[count];
            for (int i = 0; i < battles.Length; i++)
            {
                int k = (i / samples) % count;
                fought[k]++;
                cleared[k] += battles[i].Cleared ? 1 : 0;
            }

            double[] rates = new double[count];
            mean = 0.0;
            for (int k = 0; k < count; k++)
            {
                rates[k] = fought[k] == 0 ? 0.0 : (100.0 * cleared[k]) / fought[k];
                mean += rates[k] / count;
            }

            return rates;
        }

        private static string Spread(double[] rates, double mean)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double rate in rates)
            {
                min = Math.Min(min, rate);
                max = Math.Max(max, rate);
            }

            return SimOptions.Format(mean) + "% (" + SimOptions.Format(min) + "-" + SimOptions.Format(max) + ")";
        }

        private static void Header(StringBuilder report, Context context)
        {
            SimOptions options = context.Options;
            report.AppendLine("# Beast Craft new-player report");
            report.AppendLine();
            report.AppendLine("Generated by `dotnet run --project Tooling/BalanceSim -c Release -- --mode newplayer --compositions " + options.Compositions + " --samples " +
                              options.PveSamples + " --map-seeds " + options.NewPlayerMapSeeds + "`");
            report.AppendLine("(see Tooling/BalanceSim/README.md, \"New-player easing\"). The early-region easing (`regions.json` `StageEasing` x `EasingShapeScales`)");
            report.AppendLine("against the calibrated table (`encounter-difficulty.json`, which assumes typical gear and a scouted pick from the whole roster).");
            report.AppendLine();
            report.AppendLine("## Configuration");
            report.AppendLine();
            List<string> names = new List<string>();
            foreach (int t in context.Trios)
            {
                names.Add(TeamName(context.For(Regions[0]).Teams[t], context.Species));
            }

            report.AppendLine("- New-player profile: three starters, one per stance, all fielded, no scouting; every one-per-stance trio of the roster (" +
                              context.Trios.Length + "): min, mean and max over them. Team and avatar (the library avatar, as calibrated) at the node's");
            report.AppendLine("  level. Gear: none in r01-r02, typical (`--gear typical`) from " + TypicalGearFrom + ".");
            report.AppendLine("- Encounters: " + options.Compositions + " generated compositions per shape (seed " + options.Seed.ToString(CultureInfo.InvariantCulture) + "), " +
                              options.PveSamples + " battle(s) per trio and composition; each region's own battlefields (`--obstacles` per region, as the game).");
            report.AppendLine("- Per stage: the level of the stage's middle row (" + MiddleRow + "), the `elite` shape one level up (dens and generated passes);");
            report.AppendLine("  required = the scale the trio mean needs to reach the shape's target (bisection from x" + SimOptions.Format(MinScale) + ", " + ScaleBisections +
                              " halvings; `<` = not reached, `1.00+` = already there unscaled).");
            report.AppendLine("- Trios: " + string.Join(", ", names) + ".");
            if (options.NewPlayerStartLevel > 0)
            {
                report.AppendLine("- `--start-level " + options.NewPlayerStartLevel + "`: the trio and the avatar never fight below level " + options.NewPlayerStartLevel +
                                  " (a player arriving from Hearthglen); the enemies stay at the node's level.");
            }

            report.AppendLine();
        }

        private static string TeamName(int[] team, IReadOnlyList<CreatureSpeciesSO> species)
        {
            List<string> parts = new List<string>();
            foreach (int s in team)
            {
                parts.Add(species[s].SpeciesId);
            }

            return string.Join("/", parts);
        }

        private static void PerStage(StringBuilder report, Context context)
        {
            report.AppendLine("## Clear rate per stage (new-player profile)");
            report.AppendLine();
            report.AppendLine("Trio mean (lowest-highest trio). Off = the calibrated multiplier; on = times the stage's authored scale for the shape.");
            report.AppendLine();
            report.AppendLine("| Region | Stage | Shape | Level | Target | Table | Scale | Off | On | Required |");
            report.AppendLine("| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (string regionId in Regions)
            {
                RegionData region = context.Regions.GetRegion(regionId);
                if (region == null)
                {
                    continue;
                }

                MapRulesData rules = context.Regions.RulesFor(region);
                PveSimulator pve = context.For(regionId);
                for (int stage = 0; stage < region.Stages; stage++)
                {
                    int rowLevel = NodeMapGenerator.RowLevel(region, rules, stage, MiddleRow);
                    List<string> shapeIds = new List<string>();
                    foreach (ShapeWeightData weight in region.ShapeWeights ?? new ShapeWeightData[0])
                    {
                        shapeIds.Add(weight.ShapeId);
                    }

                    shapeIds.Add(rules.EliteShapeId);
                    foreach (string shapeId in shapeIds)
                    {
                        EncounterShape shape = context.Catalog.Shapes.Find(s => s.Id == shapeId);
                        if (shape == null)
                        {
                            continue;
                        }

                        int level = shapeId == rules.EliteShapeId ? rowLevel + rules.EliteLevelOffset : rowLevel;
                        double scale = context.Regions.DifficultyScaleFor(regionId, stage, shapeId);
                        double target = context.Options.TargetFor(shape);
                        double multiplier = context.Library.Multiplier(shapeId, level);
                        double[] off = Rates(context, pve, level, shape, multiplier, out double offMean);
                        string on = "-";
                        if (scale != 1.0)
                        {
                            double[] eased = Rates(context, pve, level, shape, multiplier * scale, out double onMean);
                            on = Spread(eased, onMean);
                        }

                        string required = RequiredScale(context, pve, level, shape, multiplier, target, offMean);
                        report.AppendLine("| " + regionId + " | " + (stage + 1) + " | `" + shapeId + "` | " + level + " | " + SimOptions.Format(target) + "% | " +
                                          "x" + SimOptions.FormatMultiplier(multiplier) + " | x" + scale.ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                                          Spread(off, offMean) + " | " + (scale != 1.0 ? on : "(= off)") + " | " + required + " |");
                    }

                    if (stage == region.Stages - 1)
                    {
                        BossRow(report, context, pve, region, context.Regions.DifficultyScaleFor(regionId, stage, RegionLibraryData.EasingBossId));
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("The boss row is the region's boss template at its max level and its own `DifficultyOverride` (target 50%), fought " +
                              BossCopies + " times over per trio and");
            report.AppendLine("sample (seeded copies of the one lineup).");
            report.AppendLine();
        }

        /// <summary>Seeded copies of a boss lineup per trio and sample in the boss row (one lineup gives one battle otherwise).</summary>
        public const int BossCopies = 16;

        private static void BossRow(StringBuilder report, Context context, PveSimulator pve, RegionData region, double scale)
        {
            EncounterPlan plan = EncounterPlan.FromTemplate(context.Library, context.Enemies, region.BossTemplateId, region.MaxLevel);
            if (plan == null)
            {
                return;
            }

            EnemyFactory factory = new EnemyFactory(context.Enemies);
            EncounterShape shape = new EncounterShape { Id = plan.EncounterId, Arena = plan.Arena };
            for (int copy = 1; copy <= BossCopies; copy++)
            {
                Encounter encounter = new Encounter { Id = plan.EncounterId + "-" + copy.ToString("00", CultureInfo.InvariantCulture), Arena = plan.Arena };
                for (int i = 0; i < plan.Enemies.Count; i++)
                {
                    encounter.Enemies.Add(factory.Slot(plan.Enemies[i].EnemyId, plan.Enemies[i].Element, i + 1));
                }

                shape.Compositions.Add(encounter);
            }

            const double target = SimOptions.DefaultTargetClearRate;
            double[] off = Rates(context, pve, plan.Level, shape, plan.Multiplier, out double offMean);
            string on = "(= off)";
            if (scale != 1.0)
            {
                double[] eased = Rates(context, pve, plan.Level, shape, plan.Multiplier * scale, out double onMean);
                on = Spread(eased, onMean);
            }

            string required = RequiredScale(context, pve, plan.Level, shape, plan.Multiplier, target, offMean);
            report.AppendLine("| " + region.RegionId + " | " + region.Stages + " | boss `" + plan.EncounterId + "` | " + plan.Level + " | " + SimOptions.Format(target) + "% | " +
                              "x" + SimOptions.FormatMultiplier(plan.Multiplier) + " | x" + scale.ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                              Spread(off, offMean) + " | " + on + " | " + required + " |");
        }

        private static string RequiredScale(Context context, PveSimulator pve, int level, EncounterShape shape, double multiplier, double target, double atOne)
        {
            if (atOne >= target)
            {
                return "1.00+";
            }

            Rates(context, pve, level, shape, multiplier * MinScale, out double easiest);
            if (easiest < target)
            {
                return "<" + MinScale.ToString("0.00", CultureInfo.InvariantCulture);
            }

            double lo = MinScale;
            double hi = 1.0;
            for (int step = 0; step < ScaleBisections; step++)
            {
                double mid = (lo + hi) / 2.0;
                Rates(context, pve, level, shape, multiplier * mid, out double rate);
                if (rate >= target)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return "x" + lo.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static void FirstNode(StringBuilder report, Context context)
        {
            int seeds = context.Options.NewPlayerMapSeeds;
            Dictionary<string, EncounterShape> byKey = new Dictionary<string, EncounterShape>(StringComparer.Ordinal);
            Dictionary<string, EncounterPlan> planOf = new Dictionary<string, EncounterPlan>(StringComparer.Ordinal);
            List<string> keys = new List<string>();
            EnemyFactory factory = new EnemyFactory(context.Enemies);
            string regionId = Regions[0];

            for (int seed = 1; seed <= seeds; seed++)
            {
                PlayerSave save = PlayerSave.CreateNew();
                CampaignResult started = CampaignRules.StartRun(save, context.Regions, regionId, seed);
                MapRun run = save.Campaign.ActiveRun;
                if (started == null || !save.Campaign.HasActiveRun)
                {
                    continue;
                }

                MapNode best = null;
                foreach (MapNode node in CampaignRules.Choices(run))
                {
                    if (node.IsBattle && (best == null || Rank(node).CompareTo(Rank(best)) < 0))
                    {
                        best = node;
                    }
                }

                EncounterPlan plan = best == null ? null : CampaignRules.PlanFor(run, best, context.Library, context.Enemies, context.Regions);
                if (plan == null)
                {
                    continue;
                }

                string key = plan.ShapeId + "|" + plan.Level.ToString(CultureInfo.InvariantCulture) + "|" + plan.Multiplier.ToString("R", CultureInfo.InvariantCulture);
                if (!byKey.TryGetValue(key, out EncounterShape shape))
                {
                    shape = new EncounterShape
                    {
                        Id = plan.ShapeId,
                        Arena = plan.Arena,
                        Data = context.Library.GetShape(plan.ShapeId)
                    };
                    byKey.Add(key, shape);
                    planOf.Add(key, plan);
                    keys.Add(key);
                }

                Encounter encounter = new Encounter
                {
                    Id = "first-node-" + seed.ToString("000", CultureInfo.InvariantCulture),
                    Arena = plan.Arena
                };
                for (int i = 0; i < plan.Enemies.Count; i++)
                {
                    encounter.Enemies.Add(factory.Slot(plan.Enemies[i].EnemyId, plan.Enemies[i].Element, i + 1));
                }

                shape.Compositions.Add(encounter);
            }

            report.AppendLine("## First node (new-player profile)");
            report.AppendLine();
            report.AppendLine("Map seeds 1-" + seeds + ": each fresh save's first " + regionId + " expedition and its \"Next battle\" node (lowest level, a plain");
            report.AppendLine("battle before a den, then furthest left), planned by `CampaignRules.PlanFor` with the run (eased), fought by every trio,");
            report.AppendLine(context.Options.PveSamples + " battle(s) each. Win rate = trio mean (lowest-highest trio); starter = the game's current fielded starters");
            report.AppendLine("(griffin/phoenix/golem, one per stance).");
            report.AppendLine();
            report.AppendLine("| Shape | Level | Seeds | Target | Multiplier (eased) | Scale | Win, eased | Win, no easing | Starter trio, eased |");
            report.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            keys.Sort(StringComparer.Ordinal);
            PveSimulator pve = context.For(regionId);
            int starter = Array.FindIndex(context.Trios, t => TeamName(pve.Teams[t], context.Species) == StarterName(pve.Teams[t], context.Species));
            foreach (string key in keys)
            {
                EncounterShape shape = byKey[key];
                EncounterPlan plan = planOf[key];
                double target = context.Options.TargetFor(shape);
                double[] eased = Rates(context, pve, plan.Level, shape, plan.Multiplier, out double easedMean);
                double[] plain = Rates(context, pve, plan.Level, shape, plan.Multiplier / plan.DifficultyScale, out double plainMean);
                report.AppendLine("| `" + shape.Id + "` | " + plan.Level + " | " + shape.Compositions.Count + " | " + SimOptions.Format(target) + "% | " +
                                  "x" + SimOptions.FormatMultiplier(plan.Multiplier) + " | x" + plan.DifficultyScale.ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                                  Spread(eased, easedMean) + " | " + Spread(plain, plainMean) + " | " +
                                  (starter < 0 ? "-" : SimOptions.Format(eased[starter]) + "%") + " |");
            }

            report.AppendLine();
        }

        /// <summary>The game's starter trio as a team name when <paramref name="team"/> is it (the species of <c>CampaignEconomyModel.FieldedSpecies</c>), else null.</summary>
        private static string StarterName(int[] team, IReadOnlyList<CreatureSpeciesSO> species)
        {
            HashSet<string> starters = new HashSet<string>(CampaignEconomyModel.FieldedSpecies, StringComparer.Ordinal);
            foreach (int s in team)
            {
                if (!starters.Contains(species[s].SpeciesId))
                {
                    return null;
                }
            }

            return TeamName(team, species);
        }

        // ------------------------------------------------------------------ Kinship: roster growth and trials

        /// <summary>
        /// The species indices trio <paramref name="k"/> owns on stage <paramref name="stage"/> of
        /// <paramref name="regionId"/>: its three, plus the first offered beast of every Kinship site
        /// before that point (a site of an earlier region, or of this region at this stage or before).
        /// With <paramref name="beforeSite"/>, only the sites before that one (a trial is fought by the
        /// roster that reached it).
        /// </summary>
        private static List<int> Owned(Context context, int k, string regionId, int stage, KinshipSiteData beforeSite = null)
        {
            List<string> order = new List<string>();
            foreach (CreatureSpeciesSO species in context.Species)
            {
                order.Add(species.SpeciesId);
            }

            HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (int s in context.For(Regions[0]).Teams[context.Trios[k]])
            {
                owned.Add(context.Species[s].SpeciesId);
            }

            foreach (KinshipSiteData site in context.Discovery.Sites)
            {
                if (site == beforeSite)
                {
                    break;
                }

                int cmp = string.CompareOrdinal(site.RegionId, regionId);
                if (beforeSite == null && (cmp > 0 || (cmp == 0 && site.Stage > stage)))
                {
                    break;
                }

                List<string> offer = KinshipRules.Offer(site, owned, order);
                if (offer.Count > 0)
                {
                    owned.Add(offer[0]);
                }
            }

            List<int> indices = new List<int>();
            for (int s = 0; s < context.Species.Count; s++)
            {
                if (owned.Contains(context.Species[s].SpeciesId))
                {
                    indices.Add(s);
                }
            }

            return indices;
        }

        /// <summary>Per-trio clear rates when each trio fields the scouted heuristic's three of what it owns (<paramref name="owned"/>), recruits <paramref name="lag"/> levels down.</summary>
        private static double[] RatesOwned(Context context, PveSimulator pve, int level, EncounterShape shape, double multiplier, Func<int, List<int>> owned, int lag,
                                           out double mean)
        {
            int teamLevel = Math.Max(level, context.Options.NewPlayerStartLevel);
            int count = context.Trios.Length;
            int compositions = shape.Compositions.Count;
            int samples = pve.Samples;
            Dictionary<string, int> teamIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int t = 0; t < pve.Teams.Count; t++)
            {
                int[] sorted = (int[])pve.Teams[t].Clone();
                Array.Sort(sorted);
                teamIndex[string.Join(",", sorted)] = t;
            }

            int[,] picks = new int[count, compositions];
            bool[][] ties = new bool[compositions][];
            for (int c = 0; c < compositions; c++)
            {
                ties[c] = pve.PlayersWinTies(KitMode.Elemental, teamLevel, shape.Compositions[c].Id);
            }

            int[][] memberLevels = new int[count * compositions][];
            for (int k = 0; k < count; k++)
            {
                List<int> mine = owned(k);
                HashSet<int> trio = new HashSet<int>(pve.Teams[context.Trios[k]]);
                for (int c = 0; c < compositions; c++)
                {
                    double[] scores = ScoutedPicker.Scores(ScoutedPicker.Preview(shape.Compositions[c], context.Options.ScoutedDetail), context.Species);
                    for (int s = 0; s < scores.Length; s++)
                    {
                        // As the game's suggester weighs a lagging candidate (TeamSuggester.LevelWeight a level below the best).
                        scores[s] = !mine.Contains(s) ? -1e9 : trio.Contains(s) ? scores[s] : scores[s] - (TeamSuggester.LevelWeight * lag);
                    }

                    int[] team = ScoutedPicker.Pick(scores, context.Species, 3, context.Options.ScoutedVanguardMin);
                    int t = teamIndex[string.Join(",", team)];
                    picks[k, c] = t;
                    int[] members = pve.Teams[t];
                    int[] memberLevel = new int[members.Length];
                    for (int m = 0; m < members.Length; m++)
                    {
                        memberLevel[m] = trio.Contains(members[m]) ? teamLevel : Math.Max(1, teamLevel - lag);
                    }

                    memberLevels[(k * compositions) + c] = memberLevel;
                }
            }

            PveBattle[] battles = new PveBattle[count * compositions * samples];
            Parallel.For(0, battles.Length, i =>
            {
                int k = i / (compositions * samples);
                int c = (i / samples) % compositions;
                int t = picks[k, c];
                battles[i] = pve.RunBattle(KitMode.Elemental, teamLevel, level, shape.Compositions[c], multiplier, t, i % samples, false, ties[c][t], true,
                                           memberLevels[(k * compositions) + c], 0, out _);
            });

            double[] rates = new double[count];
            mean = 0.0;
            for (int k = 0; k < count; k++)
            {
                int cleared = 0;
                for (int j = 0; j < compositions * samples; j++)
                {
                    cleared += battles[(k * compositions * samples) + j].Cleared ? 1 : 0;
                }

                rates[k] = (100.0 * cleared) / (compositions * samples);
                mean += rates[k] / count;
            }

            return rates;
        }

        /// <summary>The scale (x the multiplier) the trio mean needs to reach <paramref name="target"/>: bisection as <see cref="RequiredScale"/>.</summary>
        private static string Required(Func<double, double> meanAt, double target, double atOne)
        {
            if (atOne >= target)
            {
                return "1.00+";
            }

            if (meanAt(MinScale) < target)
            {
                return "<" + MinScale.ToString("0.00", CultureInfo.InvariantCulture);
            }

            double lo = MinScale;
            double hi = 1.0;
            for (int step = 0; step < ScaleBisections; step++)
            {
                double mid = (lo + hi) / 2.0;
                if (meanAt(mid) >= target)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return "x" + lo.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static void RosterGrowth(StringBuilder report, Context context)
        {
            report.AppendLine("## Roster growth: the Kinship recruits (new-player profile)");
            report.AppendLine();
            report.AppendLine("The per-stage table again, for the player who owns the trio plus the Kinship recruits reached by then (each site's first");
            report.AppendLine("offered beast: four beasts in r01, five in r02, six in r03), fielding the scouted heuristic's three of them per composition (the");
            report.AppendLine("preview is free, the heuristic weighing a lagging recruit as the game's suggester does); a recruit fights " +
                              string.Join(" / ", Array.ConvertAll(RecruitLag, l => l.ToString(CultureInfo.InvariantCulture))) + " levels below the trio in");
            report.AppendLine("r01 / r02 / ... (the campaign model's bench gap at the boss). Trio = the trio-only rate at the authored scale (the \"On\" column");
            report.AppendLine("above); Grown = with the recruits, at the authored scale; Caught up = the same with the recruits at the trio's level (an upper");
            report.AppendLine("bound); Grown off = Grown unscaled; Required = the scale Grown's mean needs for the shape's target.");
            report.AppendLine();
            report.AppendLine("| Region | Stage | Owned | Shape | Level | Target | Scale | Trio | Grown | Caught up | Grown off | Required |");
            report.AppendLine("| --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            for (int r = 0; r < Regions.Length; r++)
            {
                string regionId = Regions[r];
                RegionData region = context.Regions.GetRegion(regionId);
                if (region == null)
                {
                    continue;
                }

                MapRulesData rules = context.Regions.RulesFor(region);
                PveSimulator pve = context.For(regionId);
                int lag = RecruitLag[Math.Min(r, RecruitLag.Length - 1)];
                for (int stage = 0; stage < region.Stages; stage++)
                {
                    int rowLevel = NodeMapGenerator.RowLevel(region, rules, stage, MiddleRow);
                    int ownedCount = Owned(context, 0, regionId, stage).Count;
                    int stageCopy = stage;
                    List<string> shapeIds = new List<string>();
                    foreach (ShapeWeightData weight in region.ShapeWeights ?? new ShapeWeightData[0])
                    {
                        shapeIds.Add(weight.ShapeId);
                    }

                    shapeIds.Add(rules.EliteShapeId);
                    foreach (string shapeId in shapeIds)
                    {
                        EncounterShape shape = context.Catalog.Shapes.Find(s => s.Id == shapeId);
                        if (shape == null)
                        {
                            continue;
                        }

                        int level = shapeId == rules.EliteShapeId ? rowLevel + rules.EliteLevelOffset : rowLevel;
                        double scale = context.Regions.DifficultyScaleFor(regionId, stage, shapeId);
                        double target = context.Options.TargetFor(shape);
                        double multiplier = context.Library.Multiplier(shapeId, level);
                        Func<int, List<int>> owned = k => Owned(context, k, regionId, stageCopy);
                        double[] trio = Rates(context, pve, level, shape, multiplier * scale, out double trioMean);
                        double[] grown = RatesOwned(context, pve, level, shape, multiplier * scale, owned, lag, out double grownMean);
                        double[] caughtUp = RatesOwned(context, pve, level, shape, multiplier * scale, owned, 0, out double caughtUpMean);
                        double[] grownOff = scale == 1.0 ? grown : RatesOwned(context, pve, level, shape, multiplier, owned, lag, out _);
                        double grownOffMean = 0.0;
                        foreach (double rate in grownOff)
                        {
                            grownOffMean += rate / grownOff.Length;
                        }

                        string required = Required(x =>
                        {
                            RatesOwned(context, pve, level, shape, multiplier * x, owned, lag, out double m);
                            return m;
                        }, target, grownOffMean);
                        report.AppendLine("| " + regionId + " | " + (stage + 1) + " | " + ownedCount + " | `" + shapeId + "` | " + level + " | " + SimOptions.Format(target) + "% | x" +
                                          scale.ToString("0.00", CultureInfo.InvariantCulture) + " | " + Spread(trio, trioMean) + " | " + Spread(grown, grownMean) + " | " +
                                          Spread(caughtUp, caughtUpMean) + " | " + Spread(grownOff, grownOffMean) + " | " + required + " |");
                    }
                }
            }

            report.AppendLine();
        }

        private static void KinshipTrials(StringBuilder report, Context context)
        {
            report.AppendLine("## Kinship trials (new-player profile)");
            report.AppendLine();
            report.AppendLine("Each site's trial (its template at its own `DifficultyOverride`, never eased) at the level of its middle row (" + TrialRow + ") plus its");
            report.AppendLine("offset, " + BossCopies + " seeded copies of the lineup, fought by every trio with the recruits of the sites before it (the scouted");
            report.AppendLine("heuristic's three; recruits lagging as above), the team at the row's level (the campaign model's fielded mean). Target: the");
            report.AppendLine("weakest trio at least " + SimOptions.Format(TrialFloor) + "% (a loss just retries, so no roster may be walled; the mean runs higher, and the campaign");
            report.AppendLine("model's `TrialClear` follows it)." + (context.Options.HearthglenTune ? " Tuned = the highest multiplier whose weakest trio meets the floor (`--tune`)." : string.Empty));
            report.AppendLine();
            report.AppendLine("| Site | Region | Stage | Level | Owned | Multiplier | Clear | Tuned |");
            report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            EnemyFactory factory = new EnemyFactory(context.Enemies);
            foreach (KinshipSiteData site in context.Discovery.Sites)
            {
                RegionData region = context.Regions.GetRegion(site.RegionId);
                MapRulesData rules = context.Regions.RulesFor(region);
                int row = NodeMapGenerator.RowLevel(region, rules, site.Stage, TrialRow);
                int level = row + site.LevelOffset;
                EncounterPlan plan = EncounterPlan.FromTemplate(context.Library, context.Enemies, site.TemplateId, level);
                if (plan == null)
                {
                    continue;
                }

                EncounterShape shape = new EncounterShape { Id = plan.EncounterId, Arena = plan.Arena };
                for (int copy = 1; copy <= BossCopies; copy++)
                {
                    Encounter encounter = new Encounter { Id = plan.EncounterId + "-" + copy.ToString("00", CultureInfo.InvariantCulture), Arena = plan.Arena };
                    for (int i = 0; i < plan.Enemies.Count; i++)
                    {
                        encounter.Enemies.Add(factory.Slot(plan.Enemies[i].EnemyId, plan.Enemies[i].Element, i + 1));
                    }

                    shape.Compositions.Add(encounter);
                }

                int regionIndex = Math.Max(0, Array.IndexOf(RegionOrder, site.RegionId));
                int lag = RecruitLag[Math.Min(regionIndex, RecruitLag.Length - 1)];
                PveSimulator pve = context.For(site.RegionId);
                KinshipSiteData before = site;
                Func<int, List<int>> owned = k => Owned(context, k, site.RegionId, site.Stage, before);
                double[] rates = RatesOwned(context, pve, level, shape, plan.Multiplier, owned, lag, out double mean);
                string tuned = "-";
                if (context.Options.HearthglenTune)
                {
                    double lo = 0.2;
                    double hi = 3.0;
                    for (int step = 0; step < 9; step++)
                    {
                        double mid = (lo + hi) / 2.0;
                        double[] probe = RatesOwned(context, pve, level, shape, mid, owned, lag, out double _);
                        if (Min(probe) >= TrialFloor)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }

                    tuned = "x" + lo.ToString("0.000", CultureInfo.InvariantCulture);
                }

                report.AppendLine("| " + site.SiteId + " | " + site.RegionId + " | " + (site.Stage + 1) + " | " + level + " | " + Owned(context, 0, site.RegionId, site.Stage, before).Count +
                                  " | x" + SimOptions.FormatMultiplier(plan.Multiplier) + " | " + Spread(rates, mean) + " | " + tuned + " |");
            }

            report.AppendLine();
        }

        private static double Min(double[] values)
        {
            double min = double.MaxValue;
            foreach (double value in values)
            {
                min = Math.Min(min, value);
            }

            return min;
        }

        /// <summary>The Kinship regions in order (a site's lag index).</summary>
        private static readonly string[] RegionOrder = { "r01", "r02", "r03", "r04", "r05", "r06" };

        private static (int, int, float) Rank(MapNode node)
        {
            return (node.Level, node.Type == MapNodeType.Battle ? 0 : 1, node.X);
        }

        private static T Read<T>(string explicitPath, string repoRelativePath, JsonSerializerOptions json, List<string> errors) where T : class
        {
            string path = RosterLoader.ResolveFile(explicitPath, repoRelativePath);
            if (path == null || !File.Exists(path))
            {
                errors.Add("Could not find " + repoRelativePath + "; run from inside the repo.");
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), json);
            }
            catch (Exception exception)
            {
                errors.Add("Could not read '" + path + "': " + exception.Message);
                return null;
            }
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
