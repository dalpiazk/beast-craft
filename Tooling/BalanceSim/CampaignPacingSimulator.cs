using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Idle;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode campaign</c>: a Monte Carlo model of the region campaign. Like <c>--mode pacing</c>
    /// no battles are fought — each battle draws whether it was cleared from a clear-chance model —
    /// but everything else is the game's own code: the save (<see cref="PlayerSave"/>), the region
    /// data (<c>regions.json</c>), the node maps (<see cref="NodeMapGenerator"/> through
    /// <see cref="CampaignRules.StartRun"/>), the route rules (<see cref="CampaignRules"/>: loss =
    /// retry the node, Camp, Trade, the boss's seal and bank release), the level cap
    /// (<see cref="LevelCaps"/>, <see cref="LevelCap"/>), beast, bench and avatar XP with the
    /// level-gap falloff (<see cref="BeastProgression"/>, <see cref="AvatarProgression"/>), skill
    /// practice (<see cref="SkillProgression"/>) and drops (<see cref="LootRoller"/> against
    /// <c>drop-tables.json</c>).
    /// <para>
    /// <strong>The player</strong> fields <see cref="FieldedCount"/> beasts (each knocked out in
    /// <see cref="KnockoutChance"/> of battles), keeps <see cref="BenchCount"/> on the bench, and
    /// recruits one more at level 1 when region <see cref="RecruitRegion"/> starts (it joins the
    /// bench). The avatar fights every battle. Regions are played in order, each stage once
    /// (a loss retries the node). Route: an Elite when the fielded team's mean level is at least
    /// the Elite's level, otherwise a Battle, otherwise a Rest, a Shop, an Elite (ties drawn at
    /// random). Camp trains the lowest-level bench beast. The focus skill and its materials follow
    /// <c>--mode pacing</c>'s feeding policy. The economy (gold, gear and look drops, pass and lair
    /// rewards, the Trader at trading posts and at every camp, a greedy shopper) is
    /// <see cref="CampaignEconomyModel"/>'s, on its own random stream. The idle (AFK) rewards (the
    /// game's <c>IdleRewardCalculator</c> at a claim cadence) are <see cref="CampaignIdleModel"/>'s.
    /// </para>
    /// <para>
    /// <strong>Clear chance</strong> by tier (the user's tiered targets, the difficulty the
    /// encounter table is calibrated to for a scouting player at equal level): a generated node (gates
    /// included) clears at its shape's <c>TargetClear</c> in <c>encounter-library.json</c> (squad and
    /// horde 80%, elite 60%, solo 50%), the boss templates at <see cref="BossClear"/> (the target their
    /// <c>DifficultyOverride</c>s are calibrated to). Across a level gap (node level − fielded mean) the
    /// chance moves along <see cref="GapTable"/> (the design's table, whose 0 is 80%) in log-odds,
    /// shifted so gap 0 is the tier's target, interpolated for fractional gaps and clamped at its
    /// ends. A modelling assumption, not measured from the PvE simulation.
    /// </para>
    /// <para>
    /// Deterministic: campaign <c>r</c> of base seed <c>s</c> is seeded
    /// <c>LootRoller.DeriveSeed(s, r)</c>; each stage's map seed is <c>DeriveSeed(campaign, 1000 +
    /// stage index)</c>; every other draw comes from one <see cref="System.Random"/> per campaign in
    /// a fixed order.
    /// </para>
    /// </summary>
    public static class CampaignPacingSimulator
    {
        /// <summary>The region library's path relative to the repo root.</summary>
        public const string RegionsRepoRelativePath = RegionLibraryData.ProjectRelativePath;

        /// <summary>Beasts fielded in every battle.</summary>
        public const int FieldedCount = 3;

        /// <summary>Beasts kept on the bench from the start.</summary>
        public const int BenchCount = 3;

        /// <summary>The recruit (level 1) joins the bench when this region (1-based) starts.</summary>
        public const int RecruitRegion = 5;

        /// <summary>Share of battles each fielded beast is knocked out in (as <c>--mode pacing</c>).</summary>
        public const double KnockoutChance = 0.2;

        /// <summary>
        /// Boss-template clear chance at equal level: the target the templates' <c>DifficultyOverride</c>s
        /// are calibrated to (docs/balance/tuning-log.md). Generated nodes read their shape's
        /// <c>TargetClear</c> (<see cref="World.ShapeClear"/>).
        /// </summary>
        public const double BossClear = 0.50;

        /// <summary>The design's clear chance by gap (node level − team level) from −2 to +4, for an 80% encounter.</summary>
        public static readonly double[] GapTable = { 0.95, 0.90, 0.80, 0.60, 0.40, 0.20, 0.05 };

        /// <summary>The <see cref="GapTable"/> gap of its first entry.</summary>
        public const int GapTableFirst = -2;

        /// <summary>Fielded (and avatar) median within this many levels of the node at every gate and boss.</summary>
        public const int LevelTolerance = 3;

        /// <summary>Bench median this many levels (inclusive) below the fielded team at every boss from <see cref="BenchGateFromRegion"/>.</summary>
        public const int BenchGapMin = 5;

        /// <summary>See <see cref="BenchGapMin"/>.</summary>
        public const int BenchGapMax = 8;

        /// <summary>The first region (1-based) the bench gap gate applies at.</summary>
        public const int BenchGateFromRegion = 3;

        /// <summary>Recruit median within this many levels of the fielded team by the end of <see cref="RecruitRegion"/> + 1.</summary>
        public const int RecruitGapMax = 8;

        /// <summary>Total battles (p50) must fall in this band.</summary>
        public const int TotalBattlesMin = 400;

        /// <summary>See <see cref="TotalBattlesMin"/>.</summary>
        public const int TotalBattlesMax = 600;

        /// <summary>Grind probe: battles fought in each probe.</summary>
        public const int GrindBattles = 25;

        /// <summary>Grind probe: after this region's boss (1-based).</summary>
        public const int GrindAfterRegion = 5;

        /// <summary>Grind probe: most fielded levels 25 battles of the first region may pay.</summary>
        public const double GrindFirstRegionMax = 0.05;

        /// <summary>Grind probe: most fielded levels 25 battles of the probe region's stage 2 (index 1) may pay.</summary>
        public const double GrindSameRegionMax = 0.5;

        /// <summary>A node lost this many times in a row is counted as stuck (and cleared, so the campaign ends).</summary>
        public const int StuckAttempts = 200;

        /// <summary>The focus skill's targets with the economy (the design: L10 ~80 and L15 ~180 within 10%, L20 at least 270).</summary>
        public static readonly PacingSimulator.Gate[] FocusGates =
        {
            new PacingSimulator.Gate(5, 15, 20, "15-20"),
            new PacingSimulator.Gate(10, 72, 88, "~80 (72-88)"),
            new PacingSimulator.Gate(15, 162, 198, "~180 (162-198)"),
            new PacingSimulator.Gate(20, 270, int.MaxValue, "270+"),
        };

        /// <summary>Loads the data, runs the Monte Carlo (twice with <c>--self-check</c>), prints the report. Exit codes as <c>--mode pacing</c>.</summary>
        public static int Run(SimOptions options)
        {
            string libraryPath = SkillLibraryKits.ResolvePath(options.SkillLibraryPath);
            string tablesPath = RosterLoader.ResolveFile(options.DropTablesPath, PacingSimulator.DropTablesRepoRelativePath);
            string regionsPath = RosterLoader.ResolveFile(options.RegionsPath, RegionsRepoRelativePath);
            string encountersPath = RosterLoader.ResolveFile(options.EncounterLibraryPath, EncounterLoader.EncounterLibraryRepoRelativePath);
            foreach (string path in new[] { libraryPath, tablesPath, regionsPath, encountersPath })
            {
                if (path == null || !File.Exists(path))
                {
                    Console.Error.WriteLine("Could not find the skill library, drop tables, regions.json or encounter-library.json; run from inside the repo or pass " +
                                            "--skill-library / --drop-tables / --regions / --encounter-library.");
                    return 2;
                }
            }

            SkillLibraryData library;
            DropTableData tables;
            RegionLibraryData regions;
            EncounterLibraryData encounters;
            try
            {
                JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
                library = JsonSerializer.Deserialize<SkillLibraryData>(File.ReadAllText(libraryPath), json);
                tables = JsonSerializer.Deserialize<DropTableData>(File.ReadAllText(tablesPath), json);
                regions = JsonSerializer.Deserialize<RegionLibraryData>(File.ReadAllText(regionsPath), json);
                encounters = JsonSerializer.Deserialize<EncounterLibraryData>(File.ReadAllText(encountersPath), json);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Could not read the campaign data: " + exception.Message);
                return 2;
            }

            List<string> errors = SkillLibraryValidator.Validate(library);
            errors.AddRange(DropTableValidator.Validate(tables, library.Materials));
            errors.AddRange(RegionLibraryValidator.Validate(regions, encounters));
            if (errors.Count > 0)
            {
                Console.Error.WriteLine("The campaign data is invalid:");
                foreach (string error in errors)
                {
                    Console.Error.WriteLine("  " + error);
                }

                return 2;
            }

            // The mainline campaign (and every table and gate of the report) never sees a post-game
            // region: it plays the regions.json it would play without them.
            World world = new World(new PacingSimulator.Model(library.Materials, DropTableBuilder.Build(tables, DropTableBuilder.TierLookup(library.Materials))),
                                    RegionLibrary.Build(MainlineOnly(regions)), EncounterLibrary.Build(encounters));
            world.Economy = CampaignEconomyModel.World.Load(library, errors);
            if (world.Economy == null)
            {
                Console.Error.WriteLine("The economy data is invalid:");
                foreach (string error in errors)
                {
                    Console.Error.WriteLine("  " + error);
                }

                return 2;
            }

            IdleRewards idle = CampaignIdleModel.Settings.Load(tables, errors);
            if (idle == null)
            {
                Console.Error.WriteLine("The idle reward data is invalid:");
                foreach (string error in errors)
                {
                    Console.Error.WriteLine("  " + error);
                }

                return 2;
            }

            world.Idle = new CampaignIdleModel.Settings
            {
                Content = new IdleContent { Rewards = idle, DropTable = world.Model.Table, Cosmetics = world.Economy.Content.Cosmetics, Regions = world.Regions },
                IdleHoursPerDay = options.IdleHoursPerDay,
                ClaimsPerDay = options.IdleClaimsPerDay,
                BattlesPerDay = options.BattlesPerDay
            };

            List<int> seeds = options.Seeds ?? new List<int> { options.Seed };

            DateTime start = DateTime.UtcNow;
            string report = BuildReport(options, world, seeds, out List<string> misses);
            double seconds = (DateTime.UtcNow - start).TotalSeconds;

            if (options.SelfCheck)
            {
                string second = BuildReport(options, world, seeds, out List<string> _);
                if (!string.Equals(report, second, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-check failed: two campaign runs with identical inputs produced different reports.");
                    return 3;
                }
            }

            Console.Out.Write(report);
            Console.Error.WriteLine("Runtime: " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s for " + seeds.Count * options.PacingRuns + " campaigns.");
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

            if (options.SelfCheck)
            {
                if (misses.Count > 0)
                {
                    Console.Error.WriteLine("Self-check failed: campaign pacing targets missed:");
                    foreach (string miss in misses)
                    {
                        Console.Error.WriteLine("  " + miss);
                    }

                    return 3;
                }

                Console.Error.WriteLine("Self-check passed: two campaign runs identical, every gate met.");
            }

            return 0;
        }

        /// <summary>A copy of <paramref name="data"/> with only its mainline regions (post-game ones, <see cref="RegionData.IsPostGame"/>, dropped).</summary>
        public static RegionLibraryData MainlineOnly(RegionLibraryData data)
        {
            return new RegionLibraryData
            {
                SchemaVersion = data.SchemaVersion,
                StartingLevelCap = data.StartingLevelCap,
                LevelCapMargin = data.LevelCapMargin,
                MapRules = data.MapRules,
                Seals = data.Seals,
                Regions = Array.FindAll(data.Regions ?? new RegionData[0], region => region != null && !region.IsPostGame)
            };
        }

        /// <summary>The fixed inputs of every campaign.</summary>
        public sealed class World
        {
            public World(PacingSimulator.Model model, RegionLibrary regions, EncounterLibrary encounters)
            {
                Model = model;
                Regions = regions;
                Encounters = encounters;
            }

            public PacingSimulator.Model Model { get; }

            public RegionLibrary Regions { get; }

            public EncounterLibrary Encounters { get; }

            /// <summary>The economy's content and the Trader (<see cref="CampaignEconomyModel"/>).</summary>
            public CampaignEconomyModel.World Economy { get; set; }

            /// <summary>The idle rewards' content and cadence (<see cref="CampaignIdleModel"/>).</summary>
            public CampaignIdleModel.Settings Idle { get; set; }

            /// <summary>A material's tier (0 when unknown).</summary>
            public int TierOf(string materialId)
            {
                return Economy.MaterialTiers.TryGetValue(materialId, out int tier) ? tier : 0;
            }

            /// <summary>
            /// The equal-level clear chance of a generated encounter of <paramref name="shapeId"/>: the
            /// library's <c>TargetClear</c> / 100 (the validator requires one on every shape).
            /// </summary>
            public double ShapeClear(string shapeId)
            {
                EncounterShapeData shape = Encounters.GetShape(shapeId);
                if (shape == null)
                {
                    throw new InvalidOperationException("No encounter shape '" + shapeId + "' in the encounter library.");
                }

                return shape.TargetClear / 100.0;
            }

            /// <summary>The equal-level clear chance of <paramref name="node"/>: <see cref="BossClear"/> for a template (the bosses), else its shape's.</summary>
            public double TierClear(MapNode node)
            {
                return !string.IsNullOrEmpty(node.TemplateId) || node.Type == MapNodeType.Boss ? BossClear : ShapeClear(node.ShapeId);
            }

            /// <summary>The drop-table shape a node pays out from: its shape, or its template's.</summary>
            public string DropShape(MapNode node)
            {
                if (!string.IsNullOrEmpty(node.TemplateId))
                {
                    EncounterTemplateData template = Encounters.GetTemplate(node.TemplateId);
                    return template == null ? string.Empty : template.ShapeId;
                }

                return node.ShapeId;
            }
        }

        /// <summary>What one campaign measured.</summary>
        public sealed class Result
        {
            public Result(int regions, int stages)
            {
                BattlesByRegion = new int[regions];
                LossesByRegion = new int[regions];
                ElitesByRegion = new int[regions];
                TopFielded = new double[regions, stages];
                TopAvatar = new int[regions, stages];
                TopLevel = new int[regions, stages];
                EndFielded = new double[regions];
                EndBench = new double[regions];
                EndRecruit = new double[regions];
            }

            public int Battles;
            public int[] BattlesByRegion;
            public int[] LossesByRegion;
            public int[] ElitesByRegion;

            /// <summary>Fielded mean level and the avatar's level on arriving at each stage's Gate or Boss, and that node's level.</summary>
            public double[,] TopFielded;
            public int[,] TopAvatar;
            public int[,] TopLevel;

            /// <summary>At each boss clear (before the seal's release): fielded, bench and recruit mean levels (recruit −1 before it joins).</summary>
            public double[] EndFielded;
            public double[] EndBench;
            public double[] EndRecruit;

            /// <summary>Levels any beast stood above the cap after an award (must stay 0).</summary>
            public int CapViolations;

            /// <summary>Mean banked levels per beast at each seal grant, before the release.</summary>
            public List<double> BankedLevelsAtSeal = new List<double>();

            public long FieldedRawXp;
            public long FieldedCreditedXp;
            public long AvatarRawXp;
            public long AvatarCreditedXp;

            /// <summary>XP earned past the bank limit (lost), all beasts below the max level.</summary>
            public long XpLostToBankLimit;

            public double GrindFirstRegion = -1;
            public double GrindSameRegion = -1;

            public int[] FocusReached = new int[SkillProgressionDefinition.DefaultMaxLevel + 1];
            public int Stuck;

            /// <summary>The economy's measurements (<see cref="CampaignEconomyModel"/>).</summary>
            public CampaignEconomyModel.Result Econ;

            /// <summary>The idle rewards' measurements (<see cref="CampaignIdleModel"/>).</summary>
            public CampaignIdleModel.Result Idle;
        }

        private sealed class Player
        {
            public PlayerSave Save;
            public List<OwnedBeast> Fielded = new List<OwnedBeast>();
            public List<OwnedBeast> Bench = new List<OwnedBeast>();
            public OwnedBeast Recruit;
            public SkillProgress Focus = new SkillProgress("focus");
            public SkillProgress Secondary = new SkillProgress("secondary");
            public SkillProgressionDefinition Definition = new SkillProgressionDefinition();
            public CampaignEconomyModel Economy;
            public CampaignIdleModel Idle;

            /// <summary>Every beast's total XP at the last boss (<see cref="CampaignIdleModel.OnRegionEnd"/>).</summary>
            public long XpAtRegionEnd;

            public double FieldedMean()
            {
                double sum = 0.0;
                foreach (OwnedBeast beast in Fielded)
                {
                    sum += beast.Progress.Level;
                }

                return sum / Fielded.Count;
            }

            /// <summary>The mean level of the original bench (the recruit is reported on its own).</summary>
            public double BenchMean()
            {
                double sum = 0.0;
                int count = 0;
                foreach (OwnedBeast beast in Bench)
                {
                    if (beast != Recruit)
                    {
                        sum += beast.Progress.Level;
                        count++;
                    }
                }

                return sum / count;
            }
        }

        /// <summary>The clear chance of a <paramref name="tierClear"/> encounter <paramref name="gap"/> levels above the team (see the class remarks).</summary>
        public static double ClearChance(double tierClear, double gap)
        {
            double position = Math.Max(0.0, Math.Min(GapTable.Length - 1, gap - GapTableFirst));
            int low = (int)Math.Floor(position);
            int high = Math.Min(GapTable.Length - 1, low + 1);
            double fraction = position - low;
            double logit = ((1.0 - fraction) * Logit(GapTable[low])) + (fraction * Logit(GapTable[high]));
            double shift = Logit(tierClear) - Logit(GapTable[-GapTableFirst]);
            return 1.0 / (1.0 + Math.Exp(-(logit + shift)));
        }

        private static double Logit(double p)
        {
            return Math.Log(p / (1.0 - p));
        }

        /// <summary>Plays one campaign.</summary>
        public static Result Play(World world, int campaignSeed)
        {
            RegionLibrary regions = world.Regions;
            int maxStages = 1;
            foreach (RegionData region in regions.Regions)
            {
                maxStages = Math.Max(maxStages, region.Stages);
            }

            Result result = new Result(regions.Regions.Count, maxStages);
            Random rng = new Random(campaignSeed);
            Player player = new Player { Save = PlayerSave.CreateNew(), Economy = new CampaignEconomyModel(world.Economy, campaignSeed, regions.Regions.Count) };
            result.Econ = player.Economy.Stats;
            for (int i = 0; i < FieldedCount + BenchCount; i++)
            {
                string species = i < FieldedCount ? CampaignEconomyModel.FieldedSpecies[i] : CampaignEconomyModel.BenchSpecies[i - FieldedCount];
                OwnedBeast beast = OwnedBeast.Create("b" + (i + 1), species, 1);
                player.Save.Beasts.Add(beast);
                (i < FieldedCount ? player.Fielded : player.Bench).Add(beast);
            }

            player.Idle = new CampaignIdleModel(world.Idle, player.Save, campaignSeed, regions.Regions.Count, world.Model.Materials);
            result.Idle = player.Idle.Stats;

            int stageIndex = 0;
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                RegionData region = regions.Regions[r];
                if (r + 1 == RecruitRegion)
                {
                    player.Recruit = OwnedBeast.Create("recruit", CampaignEconomyModel.RecruitSpecies, 1);
                    player.Save.Beasts.Add(player.Recruit);
                    player.Bench.Add(player.Recruit);
                }

                for (int stage = 0; stage < region.Stages; stage++, stageIndex++)
                {
                    CampaignResult started = CampaignRules.StartRun(player.Save, regions, region.RegionId, stage, LootRoller.DeriveSeed(campaignSeed, 1000 + stageIndex));
                    if (!started.Success)
                    {
                        throw new InvalidOperationException("Campaign model: " + started.Error);
                    }

                    PlayStage(world, player, result, r, stage, rng);
                }

                if (r + 1 == GrindAfterRegion)
                {
                    GrindProbe(world, player, result, r, rng);
                }
            }

            return result;
        }

        private static void PlayStage(World world, Player player, Result result, int regionIndex, int stage, Random rng)
        {
            PlayerSave save = player.Save;
            RegionLibrary regions = world.Regions;
            while (save.Campaign.HasActiveRun)
            {
                if (player.Idle.ClaimDue(save, player.Fielded, result.Battles, regionIndex, world.TierOf, player.Economy))
                {
                    MaterialSpending.Spend(world.Model, player.Focus, player.Definition, save.Materials, null);
                    MaterialSpending.Spend(world.Model, player.Secondary, player.Definition, save.Materials, MaterialSpending.Reserve(world.Model, player.Focus, player.Definition));
                    CheckCap(player, result, regions);
                }

                MapNode node = Choose(CampaignRules.Choices(save.Campaign.ActiveRun), player.FieldedMean(), rng);
                if (node.Type == MapNodeType.Rest)
                {
                    OwnedBeast lowest = player.Bench[0];
                    foreach (OwnedBeast beast in player.Bench)
                    {
                        lowest = beast.Progress.Level < lowest.Progress.Level ? beast : lowest;
                    }

                    ShopContext camp = CampaignRules.ShopContextFor(save.Campaign.ActiveRun, node);
                    Expect(CampaignRules.Camp(save, regions, node.NodeId, lowest.BeastId));
                    CheckCap(player, result, regions);
                    Shop(world, player, camp, regionIndex);
                    continue;
                }

                if (node.Type == MapNodeType.Shop)
                {
                    ShopContext post = CampaignRules.ShopContextFor(save.Campaign.ActiveRun, node);
                    Expect(CampaignRules.Trade(save, regions, node.NodeId, world.Economy.Shop));
                    Shop(world, player, post, regionIndex);
                    continue;
                }

                if (node.Type == MapNodeType.Gate || node.Type == MapNodeType.Boss)
                {
                    result.TopFielded[regionIndex, stage] = player.FieldedMean();
                    result.TopAvatar[regionIndex, stage] = save.Avatar.Level;
                    result.TopLevel[regionIndex, stage] = node.Level;
                }

                result.ElitesByRegion[regionIndex] += node.Type == MapNodeType.Elite ? 1 : 0;
                for (int attempt = 0; ; attempt++)
                {
                    bool cleared = Fight(world, player, result, regionIndex, node, rng);
                    if (!cleared && attempt + 1 >= StuckAttempts)
                    {
                        result.Stuck++;
                        cleared = true;
                    }

                    if (cleared && node.Type == MapNodeType.Boss)
                    {
                        result.EndFielded[regionIndex] = player.FieldedMean();
                        result.EndBench[regionIndex] = player.BenchMean();
                        result.EndRecruit[regionIndex] = player.Recruit == null ? -1.0 : player.Recruit.Progress.Level;
                        double banked = 0.0;
                        foreach (OwnedBeast beast in save.Beasts)
                        {
                            banked += LevelCap.BankedLevels(beast.Progress);
                        }

                        result.BankedLevelsAtSeal.Add(banked / save.Beasts.Count);
                    }

                    CampaignResult resolved = CampaignRules.ResolveBattle(save, regions, node.NodeId, cleared ? BattleOutcome.PlayerVictory : BattleOutcome.EnemyVictory,
                                                                         world.Economy.Content);
                    Expect(resolved);
                    player.Economy.OnResolved(save, resolved);
                    if (cleared && node.Type == MapNodeType.Boss)
                    {
                        player.Economy.OnRegionEnd(save, regionIndex, node.Level);
                        player.Idle.OnRegionEnd(save, regionIndex, ref player.XpAtRegionEnd);
                    }

                    CheckCap(player, result, regions);
                    if (cleared)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>A Trader visit (a trading post, or the camp's travelling trader), then the focus and secondary skills take any material bought.</summary>
        private static void Shop(World world, Player player, ShopContext context, int regionIndex)
        {
            PlayerSave save = player.Save;
            player.Economy.Visit(save, context, player.Fielded, player.Focus, player.Definition, regionIndex);
            MaterialSpending.Spend(world.Model, player.Focus, player.Definition, save.Materials, null);
            MaterialSpending.Spend(world.Model, player.Secondary, player.Definition, save.Materials, MaterialSpending.Reserve(world.Model, player.Focus, player.Definition));
        }

        /// <summary>Fights (draws) one battle at <paramref name="node"/> and pays it out. Returns whether it was cleared.</summary>
        private static bool Fight(World world, Player player, Result result, int regionIndex, MapNode node, Random rng)
        {
            PlayerSave save = player.Save;
            int cap = CampaignRules.BeastCap(save, world.Regions);
            double chance = ClearChance(world.TierClear(node), node.Level - player.FieldedMean());
            bool cleared = rng.NextDouble() < chance;
            BattleOutcome outcome = cleared ? BattleOutcome.PlayerVictory : BattleOutcome.EnemyVictory;
            int focusUses = PacingSimulator.FocusUsesMin + rng.Next(PacingSimulator.FocusUsesMax - PacingSimulator.FocusUsesMin + 1);
            int secondaryUses = PacingSimulator.FocusUsesMin + rng.Next(PacingSimulator.FocusUsesMax - PacingSimulator.FocusUsesMin + 1);
            player.Economy.BeforeBattle(save, node, regionIndex);

            result.Battles++;
            result.BattlesByRegion[regionIndex]++;
            result.LossesByRegion[regionIndex] += cleared ? 0 : 1;

            SkillProgression.AwardPractice(player.Focus, player.Definition, focusUses);
            SkillProgression.AwardPractice(player.Secondary, player.Definition, secondaryUses);
            if (cleared)
            {
                LootResult loot = LootRoller.RollClear(world.Model.Table, world.DropShape(node), node.Level, save.Materials, rng);
                player.Economy.OnClear(save, world.Model.Table, node, world.DropShape(node), loot.FirstClear, regionIndex);
                player.Idle.OnLoot(loot, regionIndex, world.TierOf);
            }

            MaterialSpending.Spend(world.Model, player.Focus, player.Definition, save.Materials, null);
            MaterialSpending.Spend(world.Model, player.Secondary, player.Definition, save.Materials, MaterialSpending.Reserve(world.Model, player.Focus, player.Definition));
            for (int l = 2; l <= player.Focus.Level && l < result.FocusReached.Length; l++)
            {
                if (result.FocusReached[l] == 0)
                {
                    result.FocusReached[l] = result.Battles;
                }
            }

            foreach (OwnedBeast beast in player.Fielded)
            {
                bool knockedOut = rng.NextDouble() < KnockoutChance;
                result.FieldedRawXp += BeastProgression.BattleXp(outcome, node.Level, knockedOut);
                result.FieldedCreditedXp += BeastProgression.BattleXp(outcome, node.Level, knockedOut, beast.Progress.Level);
                Award(result, beast, BeastProgression.BattleXp(outcome, node.Level, knockedOut, beast.Progress.Level), cap);
            }

            foreach (OwnedBeast beast in player.Bench)
            {
                Award(result, beast, BeastProgression.BenchXp(outcome, node.Level, beast.Progress.Level), cap);
            }

            result.AvatarRawXp += AvatarProgression.BattleXp(outcome, node.Level);
            result.AvatarCreditedXp += AvatarProgression.BattleXp(outcome, node.Level, save.Avatar.Level);
            AvatarProgression.AwardBattle(save.Avatar, outcome, node.Level);
            return cleared;
        }

        /// <summary>Adds <paramref name="xp"/> under <paramref name="cap"/>, counting what the bank limit loses.</summary>
        private static void Award(Result result, OwnedBeast beast, int xp, int cap)
        {
            BeastProgress progress = beast.Progress;
            long before = (long)BeastProgression.TotalXpToReach(progress.Level) + progress.Xp + progress.BankedXp;
            BeastProgression.AddXp(progress, xp, cap);
            long after = (long)BeastProgression.TotalXpToReach(progress.Level) + progress.Xp + progress.BankedXp;
            if (progress.Level < BeastProgression.MaxLevel)
            {
                result.XpLostToBankLimit += Math.Max(0, before + xp - after);
            }
        }

        /// <summary>
        /// After the probe region's boss: the fielded team (copies) fights <see cref="GrindBattles"/>
        /// won battles in the first region's last stage, then as many in the probe region's second
        /// stage, and the mean fielded levels gained are recorded. The campaign itself is untouched.
        /// </summary>
        private static void GrindProbe(World world, Player player, Result result, int regionIndex, Random rng)
        {
            int cap = CampaignRules.BeastCap(player.Save, world.Regions);
            result.GrindFirstRegion = Grind(world, player, world.Regions.Regions[0], world.Regions.Regions[0].Stages - 1, cap, rng);
            result.GrindSameRegion = Grind(world, player, world.Regions.Regions[regionIndex], Math.Min(1, world.Regions.Regions[regionIndex].Stages - 1), cap, rng);
        }

        private static double Grind(World world, Player player, RegionData region, int stage, int cap, Random rng)
        {
            MapRulesData rules = world.Regions.RulesFor(region);
            double gained = 0.0;
            foreach (OwnedBeast fielded in player.Fielded)
            {
                BeastProgress copy = new BeastProgress(fielded.Progress.SpeciesId, fielded.Progress.Level) { Xp = fielded.Progress.Xp, BankedXp = fielded.Progress.BankedXp };
                double start = FractionalLevel(copy);
                for (int b = 0; b < GrindBattles; b++)
                {
                    int level = NodeMapGenerator.RowLevel(region, rules, stage, rng.Next(Math.Max(1, rules.Layers - 1)));
                    BeastProgression.AwardBattle(copy, BattleOutcome.PlayerVictory, level, rng.NextDouble() < KnockoutChance, cap);
                }

                gained += FractionalLevel(copy) - start;
            }

            return gained / player.Fielded.Count;
        }

        private static double FractionalLevel(BeastProgress progress)
        {
            return progress.Level + ((double)(progress.Xp + progress.BankedXp) / BeastProgression.XpToNextLevel(progress.Level));
        }

        private static void CheckCap(Player player, Result result, RegionLibrary regions)
        {
            int cap = CampaignRules.BeastCap(player.Save, regions);
            foreach (OwnedBeast beast in player.Save.Beasts)
            {
                result.CapViolations += Math.Max(0, beast.Progress.Level - cap);
            }
        }

        /// <summary>The route policy (see the class remarks).</summary>
        private static MapNode Choose(List<MapNode> choices, double teamLevel, Random rng)
        {
            List<MapNode> pick = choices.FindAll(n => n.Type == MapNodeType.Gate || n.Type == MapNodeType.Boss);
            if (pick.Count == 0)
            {
                pick = choices.FindAll(n => n.Type == MapNodeType.Elite && teamLevel >= n.Level);
            }

            foreach (MapNodeType type in new[] { MapNodeType.Battle, MapNodeType.Rest, MapNodeType.Shop, MapNodeType.Elite })
            {
                if (pick.Count == 0)
                {
                    pick = choices.FindAll(n => n.Type == type);
                }
            }

            if (pick.Count == 0)
            {
                throw new InvalidOperationException("Campaign model: no node to enter.");
            }

            return pick.Count == 1 ? pick[0] : pick[rng.Next(pick.Count)];
        }

        private static void Expect(CampaignResult result)
        {
            if (!result.Success)
            {
                throw new InvalidOperationException("Campaign model: " + result.Error);
            }
        }

        private static string BuildReport(SimOptions options, World world, List<int> seeds, out List<string> misses)
        {
            List<Result> runs = new List<Result>();
            foreach (int seed in seeds)
            {
                for (int r = 0; r < options.PacingRuns; r++)
                {
                    runs.Add(Play(world, LootRoller.DeriveSeed(seed, r)));
                }
            }

            return CampaignReport.Build(options, world, seeds, runs, out misses);
        }
    }

    /// <summary>
    /// <c>--mode pacing</c>'s feeding policy (<c>PacingSimulator.Spend</c>, kept private there),
    /// repeated here so the campaign model spends materials exactly as the pacing model does.
    /// </summary>
    internal static class MaterialSpending
    {
        /// <summary>Pass a waiting gate with the lowest adequate material, then feed lowest tier first while below the cap (see <c>PacingSimulator</c>).</summary>
        public static void Spend(PacingSimulator.Model model, SkillProgress skill, SkillProgressionDefinition definition, MaterialInventory inventory, int[] reserve)
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                if (skill.Level >= definition.EffectiveMaxLevel && skill.Tier >= definition.TierCount)
                {
                    return;
                }

                int[] keep = reserve ?? Reserve(model, skill, definition);
                if (SkillProgression.IsAwaitingBreakthrough(skill, definition))
                {
                    int required = definition.GetTier(skill.Tier).RequiredMaterialTier;
                    SkillMaterialSO key = FirstAvailable(model, inventory, reserve, required);
                    if (key == null || SkillProgression.TryBreakthrough(skill, definition, key) != SkillBreakthroughResult.Success)
                    {
                        return;
                    }

                    inventory.TryConsume(key.MaterialId, 1);
                    continue;
                }

                if (skill.Level >= definition.EffectiveMaxLevel)
                {
                    return;
                }

                SkillMaterialSO food = FirstAvailable(model, inventory, keep, 0);
                if (food == null)
                {
                    return;
                }

                inventory.TryConsume(food.MaterialId, 1);
                SkillProgression.ApplyMaterial(skill, definition, food);
            }
        }

        /// <summary>One material per tier that an unpassed gate of <paramref name="skill"/> requires (index = tier).</summary>
        public static int[] Reserve(PacingSimulator.Model model, SkillProgress skill, SkillProgressionDefinition definition)
        {
            int[] reserve = new int[model.MaxTier + 1];
            for (int g = skill.Tier; g < definition.TierCount; g++)
            {
                int tier = definition.GetTier(g).RequiredMaterialTier;
                if (tier >= 0 && tier < reserve.Length)
                {
                    reserve[tier]++;
                }
            }

            return reserve;
        }

        private static SkillMaterialSO FirstAvailable(PacingSimulator.Model model, MaterialInventory inventory, int[] reserve, int minTier)
        {
            foreach (SkillMaterialSO material in model.Materials)
            {
                int kept = reserve == null || material.Tier >= reserve.Length ? 0 : reserve[material.Tier];
                if (material.Tier >= minTier && inventory.GetCount(material.MaterialId) > kept)
                {
                    return material;
                }
            }

            return null;
        }
    }

    /// <summary>The Markdown report of <c>--mode campaign</c> and its gates.</summary>
    internal static class CampaignReport
    {
        /// <summary>
        /// The report's clear tiers: squad / horde, elite / gate (generated gates are elites), solo /
        /// boss, each pair merged when its two targets agree (split otherwise).
        /// </summary>
        private static List<KeyValuePair<string, double>> ClearTiers(CampaignPacingSimulator.World world)
        {
            List<KeyValuePair<string, double>> tiers = new List<KeyValuePair<string, double>>();
            AddTier(tiers, "squad", world.ShapeClear("squad"), "horde", world.ShapeClear("horde"));
            tiers.Add(new KeyValuePair<string, double>("elite / gate", world.ShapeClear("elite")));
            AddTier(tiers, "solo", world.ShapeClear("solo"), "boss", CampaignPacingSimulator.BossClear);
            return tiers;
        }

        private static void AddTier(List<KeyValuePair<string, double>> tiers, string first, double firstClear, string second, double secondClear)
        {
            if (firstClear == secondClear)
            {
                tiers.Add(new KeyValuePair<string, double>(first + " / " + second, firstClear));
                return;
            }

            tiers.Add(new KeyValuePair<string, double>(first, firstClear));
            tiers.Add(new KeyValuePair<string, double>(second, secondClear));
        }

        public static string Build(SimOptions options, CampaignPacingSimulator.World world, List<int> seeds, List<CampaignPacingSimulator.Result> runs, out List<string> misses)
        {
            misses = new List<string>();
            RegionLibrary regions = world.Regions;
            MapRulesData rules = regions.Data.MapRules;
            StringBuilder sb = new StringBuilder();
            sb.Append("# Beast Craft campaign pacing report\n\n");
            sb.Append("Generated by `dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign` (see docs/design/progression-and-saves.md, \"Region campaign\").\n");
            sb.Append("A Monte Carlo model of the region campaign, not fought battles: each battle's clear is drawn from a clear-chance model, and\n");
            sb.Append("everything else is the game's code — `regions.json`, `NodeMapGenerator`, `CampaignRules` (retry on loss, Camp, the boss's seal),\n");
            sb.Append("`LevelCaps` / `LevelCap` (cap and bank), `BeastProgression` / `AvatarProgression` (falloff, bench share), `SkillProgression` and\n");
            sb.Append("`LootRoller`. Tunable starting values and DRAFT content, not confirmed balance.\n\n");

            sb.Append("## Configuration\n\n");
            sb.Append("- Campaigns: ").Append(runs.Count).Append(" (").Append(options.PacingRuns).Append(" per base seed; base seed")
              .Append(seeds.Count == 1 ? " " : "s ").Append(SimOptions.Join(seeds)).Append(")\n");
            sb.Append("- Regions: ").Append(regions.Regions.Count).Append(", ").Append(regions.Regions[0].Stages).Append(" stages each; maps of ")
              .Append(rules.Layers).Append(" rows x ").Append(rules.Lanes).Append(" lanes, ").Append(rules.Paths).Append(" paths, rest row ").Append(rules.RestLayer)
              .Append(", node weights ");
            for (int i = 0; i < rules.NodeWeights.Length; i++)
            {
                sb.Append(i == 0 ? string.Empty : ", ").Append(rules.NodeWeights[i].Type).Append(' ').Append(rules.NodeWeights[i].Weight);
            }

            sb.Append("; ").Append(rules.MinElites).Append('-').Append(rules.MaxElites).Append(" elites (+").Append(rules.EliteLevelOffset).Append(" level), at most ")
              .Append(rules.MaxShops).Append(" shop; gates +").Append(rules.GateLevelOffset).Append(" level; bosses at the region's max level\n");
            sb.Append("- Level cap: starting ").Append(regions.StartingLevelCap).Append(", then each boss's seal (");
            for (int i = 0; i < regions.Regions.Count; i++)
            {
                SealData seal = regions.GetSeal(regions.Regions[i].BossRewardSealId);
                sb.Append(i == 0 ? string.Empty : ", ").Append(seal == null ? "-" : seal.LevelCap.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("); bank limit ").Append(LevelCap.BankLevelLimit).Append(" levels\n");
            sb.Append("- Player: ").Append(CampaignPacingSimulator.FieldedCount).Append(" fielded beasts (each knocked out in ")
              .Append(Pct(CampaignPacingSimulator.KnockoutChance * 100.0)).Append(" of battles), ").Append(CampaignPacingSimulator.BenchCount)
              .Append(" on the bench, a level-1 recruit joins the bench when region ").Append(CampaignPacingSimulator.RecruitRegion)
              .Append(" starts; the avatar fights every battle\n");
            sb.Append("- Route: an Elite when the fielded mean level is at least its level, else a Battle, else Rest, Shop, Elite (ties at random);\n");
            sb.Append("  a lost battle is retried at the same node (new battle seed); Camp trains the lowest bench beast, then its travelling trader is\n");
            sb.Append("  visited, as is any trading post taken (the game's `ShopService`; see \"Economy\")\n");
            sb.Append("- Team: fielded ").Append(string.Join(", ", CampaignEconomyModel.FieldedSpecies)).Append("; bench ").Append(string.Join(", ", CampaignEconomyModel.BenchSpecies))
              .Append("; recruit ").Append(CampaignEconomyModel.RecruitSpecies).Append(" (species only matter for skill tomes)\n");
            List<KeyValuePair<string, double>> tiers = ClearTiers(world);
            sb.Append("- Clear chance at equal level: ");
            for (int t = 0; t < tiers.Count; t++)
            {
                sb.Append(t == 0 ? string.Empty : ", ").Append(tiers[t].Key.Replace("elite / gate", "elite and generated gates").Replace("solo / boss", "solo and bosses"))
                  .Append(' ').Append(Pct(tiers[t].Value * 100.0));
            }

            sb.Append("; across a gap (node level - fielded mean) it follows the table below in log-odds, interpolated, clamped at its ends\n");
            sb.Append("- Focus skill fires ").Append(PacingSimulator.FocusUsesMin).Append('-').Append(PacingSimulator.FocusUsesMax)
              .Append(" times per battle, secondary the same; materials spent with `--mode pacing`'s policy\n");
            sb.Append("- Idle rewards: ").Append(world.Idle.Enabled
                                                     ? SimOptions.Format(world.Idle.BattlesPerDay) + " battles a day, " + SimOptions.Format(world.Idle.IdleHoursPerDay) + " hours away, " +
                                                       world.Idle.ClaimsPerDay + " claims a day (see \"Idle rewards\")"
                                                     : "off").Append("\n\n");

            sb.Append("| Gap | -2 | -1 | 0 | +1 | +2 | +3 | +4 |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (KeyValuePair<string, double> tier in tiers)
            {
                sb.Append("| ").Append(tier.Key);
                for (int gap = -2; gap <= 4; gap++)
                {
                    sb.Append(" | ").Append(Pct(100.0 * CampaignPacingSimulator.ClearChance(tier.Value, gap)));
                }

                sb.Append(" |\n");
            }

            // Battles.
            sb.Append("\n## Battles per region\n\n");
            sb.Append("Every battle fought, losses (retries) included.\n\n");
            sb.Append("| Region | Levels | p10 | p50 | p90 | Lost (mean) | Elites taken (mean) |\n| --- | --- | ---: | ---: | ---: | ---: | ---: |\n");
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                RegionData region = regions.Regions[r];
                List<double> battles = runs.ConvertAll(run => (double)run.BattlesByRegion[r]);
                sb.Append("| ").Append(region.RegionId).Append(' ').Append(region.DisplayName).Append(" | ").Append(region.MinLevel).Append('-').Append(region.MaxLevel)
                  .Append(" | ").Append(Int(P(battles, 10))).Append(" | ").Append(Int(P(battles, 50))).Append(" | ").Append(Int(P(battles, 90)))
                  .Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)run.LossesByRegion[r]))))
                  .Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)run.ElitesByRegion[r])))).Append(" |\n");
            }

            List<double> totals = runs.ConvertAll(run => (double)run.Battles);
            double totalP50 = P(totals, 50);
            bool totalOk = totalP50 >= CampaignPacingSimulator.TotalBattlesMin && totalP50 <= CampaignPacingSimulator.TotalBattlesMax;
            if (!totalOk)
            {
                misses.Add("Total battles p50 " + Int(totalP50) + " is outside " + CampaignPacingSimulator.TotalBattlesMin + "-" + CampaignPacingSimulator.TotalBattlesMax + ".");
            }

            sb.Append("| **Total** | 1-100 | ").Append(Int(P(totals, 10))).Append(" | ").Append(Int(totalP50)).Append(" | ").Append(Int(P(totals, 90)))
              .Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)Sum(run.LossesByRegion))))).Append(" | ")
              .Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)Sum(run.ElitesByRegion))))).Append(" |\n\n");
            sb.Append("Target: total p50 ").Append(CampaignPacingSimulator.TotalBattlesMin).Append('-').Append(CampaignPacingSimulator.TotalBattlesMax)
              .Append(": ").Append(totalOk ? "ok" : "**MISS**").Append(".");
            int stuck = 0;
            foreach (CampaignPacingSimulator.Result run in runs)
            {
                stuck += run.Stuck;
            }

            sb.Append(stuck == 0 ? string.Empty : " " + stuck + " node(s) were lost " + CampaignPacingSimulator.StuckAttempts + " times in a row and forced.").Append("\n\n");

            // Levels at gates and bosses.
            sb.Append("## Levels at every gate and boss\n\n");
            sb.Append("On arriving at the stage's Gate (stages 1-3) or the Boss (stage 4), before fighting it. Fielded = the mean level of the three\n");
            sb.Append("fielded beasts. Target: the fielded and avatar medians within ").Append(CampaignPacingSimulator.LevelTolerance).Append(" levels of the node's level.\n\n");
            sb.Append("| Region | Stage | Node | Level | Fielded p10 | Fielded p50 | Fielded p90 | Avatar p50 | Verdict |\n| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |\n");
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                RegionData region = regions.Regions[r];
                for (int s = 0; s < region.Stages; s++)
                {
                    int level = runs[0].TopLevel[r, s];
                    List<double> fielded = runs.ConvertAll(run => run.TopFielded[r, s]);
                    List<double> avatar = runs.ConvertAll(run => (double)run.TopAvatar[r, s]);
                    double f50 = P(fielded, 50);
                    double a50 = P(avatar, 50);
                    bool ok = Math.Abs(f50 - level) <= CampaignPacingSimulator.LevelTolerance && Math.Abs(a50 - level) <= CampaignPacingSimulator.LevelTolerance;
                    if (!ok)
                    {
                        misses.Add(region.RegionId + " stage " + (s + 1) + ": fielded p50 " + Lv(f50) + " / avatar p50 " + Int(a50) + " vs node level " + level + ".");
                    }

                    sb.Append("| ").Append(region.RegionId).Append(" | ").Append(s + 1).Append(" | ").Append(s == region.Stages - 1 ? "Boss" : "Gate").Append(" | ").Append(level)
                      .Append(" | ").Append(Lv(P(fielded, 10))).Append(" | ").Append(Lv(f50)).Append(" | ").Append(Lv(P(fielded, 90))).Append(" | ").Append(Int(a50))
                      .Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
                }
            }

            // Bench and recruit.
            sb.Append("\n## Bench and recruit\n\n");
            sb.Append("At each boss, before its seal's release. The bench earns ").Append(BeastProgression.BenchShareBasePermille / 10)
              .Append("% of a standing fielded beast's XP, +").Append(SimOptions.Format(BeastProgression.BenchSharePerLevelPermille / 10.0))
              .Append("% per level below the enemy (up to 100%), then the falloff; Camp trains the lowest bench beast. Targets: the bench median ")
              .Append(CampaignPacingSimulator.BenchGapMin).Append('-').Append(CampaignPacingSimulator.BenchGapMax).Append(" levels below the fielded team from region ")
              .Append(CampaignPacingSimulator.BenchGateFromRegion).Append("; the recruit (joins at level 1 in region ").Append(CampaignPacingSimulator.RecruitRegion)
              .Append(") within ").Append(CampaignPacingSimulator.RecruitGapMax).Append(" by the end of region ").Append(CampaignPacingSimulator.RecruitRegion + 1).Append(".\n\n");
            sb.Append("| Region | Boss level | Fielded p50 | Bench p50 | Bench gap p50 | Recruit p50 | Recruit gap p50 | Verdict |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                RegionData region = regions.Regions[r];
                List<double> fielded = runs.ConvertAll(run => run.EndFielded[r]);
                List<double> bench = runs.ConvertAll(run => run.EndBench[r]);
                List<double> benchGap = runs.ConvertAll(run => run.EndFielded[r] - run.EndBench[r]);
                bool hasRecruit = runs[0].EndRecruit[r] >= 0.0;
                List<double> recruit = runs.ConvertAll(run => run.EndRecruit[r]);
                List<double> recruitGap = runs.ConvertAll(run => run.EndFielded[r] - run.EndRecruit[r]);
                string verdict = "-";
                if (r + 1 >= CampaignPacingSimulator.BenchGateFromRegion)
                {
                    double gap = P(benchGap, 50);
                    bool ok = gap >= CampaignPacingSimulator.BenchGapMin && gap <= CampaignPacingSimulator.BenchGapMax;
                    if (!ok)
                    {
                        misses.Add(region.RegionId + ": bench gap p50 " + Lv(gap) + " is outside " + CampaignPacingSimulator.BenchGapMin + "-" + CampaignPacingSimulator.BenchGapMax + ".");
                    }

                    verdict = ok ? "ok" : "**MISS**";
                }

                if (r + 1 == CampaignPacingSimulator.RecruitRegion + 1)
                {
                    double gap = P(recruitGap, 50);
                    bool ok = gap <= CampaignPacingSimulator.RecruitGapMax;
                    if (!ok)
                    {
                        misses.Add(region.RegionId + ": recruit gap p50 " + Lv(gap) + " is above " + CampaignPacingSimulator.RecruitGapMax + ".");
                    }

                    verdict += ok ? " (recruit ok)" : " (recruit **MISS**)";
                }

                sb.Append("| ").Append(region.RegionId).Append(" | ").Append(region.MaxLevel).Append(" | ").Append(Lv(P(fielded, 50))).Append(" | ").Append(Lv(P(bench, 50)))
                  .Append(" | ").Append(Lv(P(benchGap, 50))).Append(" | ").Append(hasRecruit ? Lv(P(recruit, 50)) : "-").Append(" | ")
                  .Append(hasRecruit ? Lv(P(recruitGap, 50)) : "-").Append(" | ").Append(verdict).Append(" |\n");
            }

            // Cap and XP.
            sb.Append("\n## Level cap, bank and falloff\n\n");
            long violations = 0;
            List<double> banked = new List<double>();
            double lostToBank = 0.0;
            double fieldedRaw = 0.0;
            double fieldedCredited = 0.0;
            double avatarRaw = 0.0;
            double avatarCredited = 0.0;
            foreach (CampaignPacingSimulator.Result run in runs)
            {
                violations += run.CapViolations;
                banked.AddRange(run.BankedLevelsAtSeal);
                lostToBank += run.XpLostToBankLimit;
                fieldedRaw += run.FieldedRawXp;
                fieldedCredited += run.FieldedCreditedXp;
                avatarRaw += run.AvatarRawXp;
                avatarCredited += run.AvatarCreditedXp;
            }

            if (violations > 0)
            {
                misses.Add("The level cap was exceeded (" + violations + " beast-levels).");
            }

            double bankedP50 = P(banked, 50);
            if (bankedP50 > LevelCap.BankLevelLimit)
            {
                misses.Add("Median banked levels at a seal " + Lv(bankedP50) + " is above the bank limit " + LevelCap.BankLevelLimit + ".");
            }

            sb.Append("- Cap exceeded: ").Append(violations).Append(" beast-levels over all campaigns (target 0): ").Append(violations == 0 ? "ok" : "**MISS**").Append('\n');
            sb.Append("- Banked levels per beast at a seal (before its release): p50 ").Append(Lv(bankedP50)).Append(", p90 ").Append(Lv(P(banked, 90)))
              .Append(", max ").Append(Lv(P(banked, 100))).Append(" (target p50 <= ").Append(LevelCap.BankLevelLimit).Append("): ")
              .Append(bankedP50 <= LevelCap.BankLevelLimit ? "ok" : "**MISS**").Append('\n');
            sb.Append("- XP lost past the bank limit: ").Append(SimOptions.Format(lostToBank / runs.Count)).Append(" per campaign (all beasts)\n");
            sb.Append("- XP lost to the level-gap falloff: fielded ").Append(Pct(fieldedRaw <= 0.0 ? 0.0 : 100.0 * (fieldedRaw - fieldedCredited) / fieldedRaw))
              .Append(" of their battle XP, avatar ").Append(Pct(avatarRaw <= 0.0 ? 0.0 : 100.0 * (avatarRaw - avatarCredited) / avatarRaw)).Append("\n\n");

            // Grind probe.
            RegionData probe = regions.Regions[CampaignPacingSimulator.GrindAfterRegion - 1];
            List<double> grindFirst = runs.ConvertAll(run => run.GrindFirstRegion);
            List<double> grindSame = runs.ConvertAll(run => run.GrindSameRegion);
            bool firstOk = P(grindFirst, 50) < CampaignPacingSimulator.GrindFirstRegionMax;
            bool sameOk = P(grindSame, 50) < CampaignPacingSimulator.GrindSameRegionMax;
            if (!firstOk || !sameOk)
            {
                misses.Add("Grind probe: " + Levels(P(grindFirst, 50)) + " levels in " + regions.Regions[0].RegionId + ", " + Levels(P(grindSame, 50)) + " in " + probe.RegionId + " stage 2.");
            }

            sb.Append("## Grind probe\n\n");
            sb.Append("After ").Append(probe.RegionId).Append("'s boss, the fielded team (copies) wins ").Append(CampaignPacingSimulator.GrindBattles)
              .Append(" battles of old content; mean fielded levels gained (XP and bank as a fraction of the next level):\n\n");
            sb.Append("| Content | p50 | p90 | Target (p50) | Verdict |\n| --- | ---: | ---: | --- | --- |\n");
            sb.Append("| ").Append(regions.Regions[0].RegionId).Append(" stage ").Append(regions.Regions[0].Stages).Append(" | ").Append(Levels(P(grindFirst, 50))).Append(" | ")
              .Append(Levels(P(grindFirst, 90))).Append(" | < ").Append(Levels(CampaignPacingSimulator.GrindFirstRegionMax)).Append(" | ").Append(firstOk ? "ok" : "**MISS**").Append(" |\n");
            sb.Append("| ").Append(probe.RegionId).Append(" stage 2 | ").Append(Levels(P(grindSame, 50))).Append(" | ").Append(Levels(P(grindSame, 90))).Append(" | < ")
              .Append(Levels(CampaignPacingSimulator.GrindSameRegionMax)).Append(" | ").Append(sameOk ? "ok" : "**MISS**").Append(" |\n\n");

            CampaignEconomyModel.Report(sb, regions, runs, P, misses);
            CampaignIdleModel.Report(sb, regions, world.Idle, runs, P, misses);

            // Skill.
            sb.Append("## Focus skill\n\n");
            sb.Append("Battles (losses included) for the focus skill to reach each level, the Trader's gate materials included. Targets (the\n");
            sb.Append("economy design): L5 15-20, L10 ~80 +/-10%, L15 ~180 +/-10%, L20 at least 270.\n\n");
            sb.Append("| Level | Target (p50) | p10 | p50 | p90 | Verdict |\n| ---: | --- | ---: | ---: | ---: | --- |\n");
            foreach (PacingSimulator.Gate gate in CampaignPacingSimulator.FocusGates)
            {
                List<int> reached = runs.ConvertAll(run => run.FocusReached[gate.Level]);
                int p50 = PacingSimulator.Percentile(reached, 50);
                bool ok = p50 >= gate.Min && p50 <= gate.Max;
                if (!ok)
                {
                    misses.Add("Focus skill L" + gate.Level + ": p50 " + (p50 == 0 ? "never" : p50.ToString(CultureInfo.InvariantCulture)) + " is outside " + gate.Min + "-" + gate.Max + ".");
                }

                sb.Append("| ").Append(gate.Level).Append(" | ").Append(gate.Label).Append(" | ").Append(Battles(PacingSimulator.Percentile(reached, 10))).Append(" | ")
                  .Append(Battles(p50)).Append(" | ").Append(Battles(PacingSimulator.Percentile(reached, 90))).Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
            }

            sb.Append("\n## Verdict\n\n");
            sb.Append(misses.Count == 0 ? "Every gate is met.\n" : "Gates missed:\n\n");
            foreach (string miss in misses)
            {
                sb.Append("- ").Append(miss).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>Nearest-rank percentile.</summary>
        private static double P(List<double> values, double p)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }

            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
        }

        private static double Mean(List<double> values)
        {
            double sum = 0.0;
            foreach (double value in values)
            {
                sum += value;
            }

            return values.Count == 0 ? 0.0 : sum / values.Count;
        }

        private static int Sum(int[] values)
        {
            int sum = 0;
            foreach (int value in values)
            {
                sum += value;
            }

            return sum;
        }

        private static string Int(double value)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Lv(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Levels(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Battles(int value)
        {
            return value == 0 ? "never" : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }
    }
}
