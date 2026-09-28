using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Tutorial;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode hearthglen</c>: Hearthglen (r00, the tutorial region) fight by fight, against every
    /// legal pick. Each fixed fight (<c>regions.json</c> <c>TutorialRegions</c> <c>FixedNodes</c>) is its
    /// template at its node's level and its own <c>DifficultyOverride</c> (never the calibrated table,
    /// never the easing), fought with the PvE simulator's own battles on the region's battlefields
    /// (Hearthglen borrows Verdant Hollow's), beside the library avatar, no gear, no consumable.
    /// <para>
    /// <strong>Who fights.</strong> Every beast owned at that point is fielded (the party is never
    /// full in Hearthglen): the 1st pick alone until the first trial, then the (1st, 2nd) pair, then
    /// the trio. Every legal combination in pick order (<see cref="StarterPicks"/>): the 10 solo picks,
    /// every (1st, 2nd) pair with the 2nd of the next stance, every trio in pick order — so each
    /// one-per-stance trio three times, once per stance it can start from, since the picks join at
    /// different times and so stand at different levels.
    /// </para>
    /// <para>
    /// <strong>Levels.</strong> The level profile a player who wins every fight once reaches, by the
    /// Core's own XP rules (<see cref="BeastProgression"/>, <see cref="AvatarProgression"/>): every
    /// pick joins at level 1; each fight pays its winners; the camp trains the newest pick.
    /// </para>
    /// <para>
    /// <strong>Targets</strong> (producer decision): at least <see cref="RegularTarget"/>% for every
    /// combination in every fight but the finale; the finale (the last fight) about
    /// <see cref="FinaleTarget"/>% for every trio (within <see cref="FinaleBand"/> points), so the
    /// stance a player starts from does not matter. <c>--tune</c> also searches each fight's
    /// multiplier for its target (the regular fights: the highest multiplier whose weakest
    /// combination still clears <see cref="RegularTarget"/>%; the finale: the mean at
    /// <see cref="FinaleTarget"/>%).
    /// </para>
    /// </summary>
    public static class HearthglenReport
    {
        public const double RegularTarget = 90.0;
        public const double FinaleTarget = 70.0;
        public const double FinaleBand = 10.0;

        /// <summary>Tuning: the multipliers searched and the bisection steps.</summary>
        public const double MinMultiplier = 0.1;

        public const double MaxMultiplier = 3.0;
        public const int TuneSteps = 9;

        public static int Run(SimOptions options, List<CreatureSpeciesSO> species, Dictionary<string, GrowthRateCurve> curves)
        {
            List<string> errors = new List<string>();
            JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
            EncounterLibraryData encounterData = Read<EncounterLibraryData>(options.EncounterLibraryPath, EncounterLibraryData.ProjectRelativePath, json, errors);
            EncounterDifficultyData difficultyData = Read<EncounterDifficultyData>(null, EncounterDifficultyData.ProjectRelativePath, json, errors);
            RegionLibraryData regionData = Read<RegionLibraryData>(options.RegionsPath, RegionLibraryData.ProjectRelativePath, json, errors);
            EnemyLibraryData enemyData = Read<EnemyLibraryData>(options.EnemyLibraryPath, EnemyLibraryData.ProjectRelativePath, json, errors);
            if (errors.Count > 0)
            {
                return Fail("Could not read the content:", errors);
            }

            errors.AddRange(RegionLibraryValidator.Validate(regionData, encounterData));
            if (errors.Count > 0)
            {
                return Fail("The content is invalid:", errors);
            }

            curves.TryGetValue(enemyData.GrowthCurveId ?? string.Empty, out GrowthRateCurve curve);
            Context context = new Context
            {
                Options = options,
                Species = species,
                Enemies = EnemyCatalog.Build(enemyData, curve),
                Library = EncounterLibrary.Build(encounterData, EncounterDifficultyTable.Build(difficultyData)),
                Regions = RegionLibrary.Build(regionData)
            };

            RegionData hearthglen = context.Regions.Tutorial;
            if (hearthglen == null)
            {
                Console.Error.WriteLine("regions.json has no tutorial region (" + CampaignProgress.TutorialRegionId + ").");
                return 2;
            }

            context.Nodes = CampaignRules.FixedMap(hearthglen, 1);
            context.Profile = LevelProfile(context.Nodes, context.Regions.StartingLevelCap);
            context.Battlefield = context.Regions.BattlefieldRegionOf(hearthglen.RegionId);
            BuildCombinations(context);

            List<FightResult> fights = new List<FightResult>();
            int last = context.Nodes.FindLastIndex(n => n.IsBattle);
            foreach (MapNode node in context.Nodes)
            {
                if (!node.IsBattle)
                {
                    continue;
                }

                FightResult fight = Fight(context, node, node.NodeId == last);
                fights.Add(fight);
                Console.Error.WriteLine(node.TemplateId + ": min " + SimOptions.Format(fight.Min) + "%, mean " + SimOptions.Format(fight.Mean) + "%, max " +
                                        SimOptions.Format(fight.Max) + "%" + (fight.Suggested > 0 ? ", suggested x" + fight.Suggested.ToString("0.000", CultureInfo.InvariantCulture) : string.Empty));
            }

            string text = Write(context, hearthglen, fights).Replace("\r\n", "\n");
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

            bool pass = fights.TrueForAll(f => f.Pass);
            Console.Error.WriteLine(pass ? "Every Hearthglen fight meets its target." : "Some Hearthglen fights miss their target (see the report).");
            return 0;
        }

        private sealed class Context
        {
            public SimOptions Options;
            public List<CreatureSpeciesSO> Species;
            public EnemyCatalog Enemies;
            public EncounterLibrary Library;
            public RegionLibrary Regions;
            public List<MapNode> Nodes;
            public string Battlefield;
            public Dictionary<int, Levels> Profile;

            /// <summary>Per party size (1-3): every legal combination, as roster indices in pick order.</summary>
            public readonly Dictionary<int, List<int[]>> Combinations = new Dictionary<int, List<int[]>>();

            public readonly Dictionary<int, PveSimulator> Simulators = new Dictionary<int, PveSimulator>();

            public PveSimulator For(int size)
            {
                if (!Simulators.TryGetValue(size, out PveSimulator pve))
                {
                    SimOptions copy = Options.ForSeed(Options.Seed);
                    copy.TeamSize = size;
                    copy.ObstaclesRegion = Battlefield;
                    pve = new PveSimulator(copy, Species) { GearFor = null };
                    Simulators.Add(size, pve);
                }

                return pve;
            }
        }

        /// <summary>The picks' and the avatar's levels at one fight.</summary>
        private sealed class Levels
        {
            public int[] Picks;
            public int Avatar;

            public string Describe()
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < Picks.Length; i++)
                {
                    parts.Add(Ordinal(i) + " Lv " + Picks[i]);
                }

                return string.Join(", ", parts) + "; avatar Lv " + Avatar;
            }
        }

        private sealed class FightResult
        {
            public MapNode Node;
            public FixedNodeData Authored;
            public EncounterPlan Plan;
            public bool Finale;
            public Levels Levels;
            public List<int[]> Combinations;
            public double[] Rates;
            public double Min;
            public double Mean;
            public double Max;
            public double Suggested;

            public bool Pass
            {
                get
                {
                    return Finale ? Math.Abs(Mean - FinaleTarget) <= FinaleBand / 2.0 && Min >= FinaleTarget - FinaleBand && Max <= FinaleTarget + FinaleBand
                               : Min >= RegularTarget;
                }
            }
        }

        /// <summary>
        /// The levels a player who wins every fight once stands at, fight by fight: every pick joins at
        /// level 1; every fight pays its winners (every owned beast is fielded; no bench) and the
        /// avatar; the camp trains the newest pick.
        /// </summary>
        private static Dictionary<int, Levels> LevelProfile(List<MapNode> nodes, int cap)
        {
            Dictionary<int, Levels> profile = new Dictionary<int, Levels>();
            List<BeastProgress> picks = new List<BeastProgress> { new BeastProgress("first", StarterPicks.JoinLevel) };
            AvatarProgress avatar = new AvatarProgress();
            foreach (MapNode node in nodes)
            {
                if (node.IsBattle)
                {
                    profile[node.NodeId] = new Levels { Picks = picks.ConvertAll(p => p.Level).ToArray(), Avatar = avatar.Level };
                    foreach (BeastProgress pick in picks)
                    {
                        BeastProgression.AwardBattle(pick, BattleOutcome.PlayerVictory, node.Level, false, cap);
                    }

                    AvatarProgression.AwardBattle(avatar, BattleOutcome.PlayerVictory, node.Level);
                    if (node.Type == MapNodeType.Trial && picks.Count < StarterPicks.PickCount)
                    {
                        picks.Add(new BeastProgress("next", StarterPicks.JoinLevel));
                    }
                }
                else if (node.Type == MapNodeType.Rest)
                {
                    BeastProgression.AwardBattle(picks[picks.Count - 1], BattleOutcome.PlayerVictory, node.Level, false, cap);
                }
            }

            return profile;
        }

        private static void BuildCombinations(Context context)
        {
            List<CreatureSpeciesSO> roster = context.Species;
            for (int size = 1; size <= StarterPicks.PickCount; size++)
            {
                context.Combinations[size] = new List<int[]>();
            }

            for (int a = 0; a < roster.Count; a++)
            {
                context.Combinations[1].Add(new[] { a });
                for (int b = 0; b < roster.Count; b++)
                {
                    if (!StarterPicks.IsLegalSequence(new[] { roster[a].SpeciesId, roster[b].SpeciesId }, roster, out string _))
                    {
                        continue;
                    }

                    context.Combinations[2].Add(new[] { a, b });
                    for (int c = 0; c < roster.Count; c++)
                    {
                        if (StarterPicks.IsLegalSequence(new[] { roster[a].SpeciesId, roster[b].SpeciesId, roster[c].SpeciesId }, roster, out string _))
                        {
                            context.Combinations[3].Add(new[] { a, b, c });
                        }
                    }
                }
            }
        }

        private static FightResult Fight(Context context, MapNode node, bool finale)
        {
            Levels levels = context.Profile[node.NodeId];
            int size = levels.Picks.Length;
            EncounterPlan plan = EncounterPlan.FromTemplate(context.Library, context.Enemies, node.TemplateId, node.Level);
            FightResult fight = new FightResult
            {
                Node = node,
                Authored = context.Regions.FixedNode(CampaignProgress.TutorialRegionId, node.NodeId),
                Plan = plan,
                Finale = finale,
                Levels = levels,
                Combinations = context.Combinations[size]
            };

            EncounterShape shape = Copies(context, plan);
            fight.Rates = Rates(context, size, levels, node.Level, shape, plan.Multiplier, fight.Combinations);
            Summarize(fight.Rates, out fight.Min, out fight.Mean, out fight.Max);
            if (context.Options.HearthglenTune)
            {
                fight.Suggested = Tune(context, size, levels, node.Level, shape, fight.Combinations, finale);
            }

            return fight;
        }

        /// <summary>The template's lineup as <c>--compositions</c> seeded copies (one lineup is one battle otherwise).</summary>
        private static EncounterShape Copies(Context context, EncounterPlan plan)
        {
            EnemyFactory factory = new EnemyFactory(context.Enemies);
            EncounterShape shape = new EncounterShape { Id = plan.EncounterId, Arena = plan.Arena };
            for (int copy = 1; copy <= Math.Max(1, context.Options.Compositions); copy++)
            {
                Encounter encounter = new Encounter { Id = plan.EncounterId + "-" + copy.ToString("00", CultureInfo.InvariantCulture), Arena = plan.Arena };
                for (int i = 0; i < plan.Enemies.Count; i++)
                {
                    encounter.Enemies.Add(factory.Slot(plan.Enemies[i].EnemyId, plan.Enemies[i].Element, i + 1));
                }

                shape.Compositions.Add(encounter);
            }

            return shape;
        }

        /// <summary>Each combination's clear rate (percent) over every copy and sample, at <paramref name="multiplier"/>.</summary>
        private static double[] Rates(Context context, int size, Levels levels, int level, EncounterShape shape, double multiplier, List<int[]> combinations)
        {
            PveSimulator pve = context.For(size);
            int copies = shape.Compositions.Count;
            int samples = pve.Samples;
            int[] teamIndex = new int[combinations.Count];
            int[][] memberLevels = new int[combinations.Count][];
            for (int k = 0; k < combinations.Count; k++)
            {
                int[] order = combinations[k];
                int[] sorted = (int[])order.Clone();
                Array.Sort(sorted);
                teamIndex[k] = pve.Teams.FindIndex(t => SameMembers(t, sorted));
                memberLevels[k] = new int[sorted.Length];
                int[] team = pve.Teams[teamIndex[k]];
                for (int m = 0; m < team.Length; m++)
                {
                    memberLevels[k][m] = levels.Picks[Array.IndexOf(order, team[m])];
                }
            }

            bool[][] ties = new bool[copies][];
            for (int c = 0; c < copies; c++)
            {
                ties[c] = pve.PlayersWinTies(KitMode.Elemental, level, shape.Compositions[c].Id);
            }

            int[] cleared = new int[combinations.Count];
            int total = combinations.Count * copies * samples;
            Parallel.For(0, total, i =>
            {
                int k = i / (copies * samples);
                int c = (i / samples) % copies;
                int sample = i % samples;
                int t = teamIndex[k];
                PveBattle battle = pve.RunBattle(KitMode.Elemental, level, level, shape.Compositions[c], multiplier, t, sample, false, ties[c][t], true, memberLevels[k],
                                                 levels.Avatar, out _);
                if (battle.Cleared)
                {
                    System.Threading.Interlocked.Increment(ref cleared[k]);
                }
            });

            double[] rates = new double[combinations.Count];
            for (int k = 0; k < rates.Length; k++)
            {
                rates[k] = 100.0 * cleared[k] / (copies * samples);
            }

            return rates;
        }

        private static double Tune(Context context, int size, Levels levels, int level, EncounterShape shape, List<int[]> combinations, bool finale)
        {
            double lo = MinMultiplier;
            double hi = MaxMultiplier;
            for (int step = 0; step < TuneSteps; step++)
            {
                double mid = Math.Sqrt(lo * hi);
                Summarize(Rates(context, size, levels, level, shape, mid, combinations), out double min, out double mean, out double _);
                bool easyEnough = finale ? mean >= FinaleTarget : min >= RegularTarget;
                if (easyEnough)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        private static void Summarize(double[] rates, out double min, out double mean, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            mean = 0.0;
            foreach (double rate in rates)
            {
                min = Math.Min(min, rate);
                max = Math.Max(max, rate);
                mean += rate / rates.Length;
            }
        }

        private static bool SameMembers(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string Ordinal(int index)
        {
            return index == 0 ? "1st" : index == 1 ? "2nd" : "3rd";
        }

        private static string Name(Context context, int[] order)
        {
            List<string> parts = new List<string>();
            foreach (int s in order)
            {
                parts.Add(context.Species[s].SpeciesId);
            }

            return string.Join(" > ", parts);
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }

        private static string Write(Context context, RegionData hearthglen, List<FightResult> fights)
        {
            SimOptions options = context.Options;
            StringBuilder report = new StringBuilder();
            report.AppendLine("# Beast Craft Hearthglen report");
            report.AppendLine();
            report.AppendLine("Generated by `dotnet run --project Tooling/BalanceSim -c Release -- --mode hearthglen --compositions " + options.Compositions + " --samples " +
                              options.PveSamples + "`");
            report.AppendLine("(see Tooling/BalanceSim/README.md, \"Hearthglen\"). Hearthglen (" + hearthglen.RegionId + ", the tutorial region) fight by fight against every");
            report.AppendLine("legal pick: its fixed templates at their own `DifficultyOverride` (never the calibrated table, never the easing).");
            report.AppendLine();
            report.AppendLine("## Configuration");
            report.AppendLine();
            report.AppendLine("- Picks (`StarterPicks`): the 1st any of the " + context.Species.Count + " beasts; the 2nd any beast of the next stance in the cycle");
            report.AppendLine("  Vanguard > Ranged > Skirmisher > Vanguard; the 3rd any beast of the remaining stance. Every owned beast is fielded (the party is");
            report.AppendLine("  never full here), so the fights before the first trial are fought by the " + context.Combinations[1].Count + " solo picks, those before the second by");
            report.AppendLine("  the " + context.Combinations[2].Count + " legal (1st, 2nd) pairs, and the rest by the " + context.Combinations[3].Count +
                              " trios in pick order (each one-per-stance trio three times: once per stance it can start from).");
            report.AppendLine("- Levels: every pick joins at level 1; the profile below is a player who wins every fight once (the Core's XP rules; the camp trains");
            report.AppendLine("  the newest pick). The library avatar (as calibrated) at its own profile level. No gear, no consumable.");
            report.AppendLine("- Battles: " + options.Compositions + " seeded copies of each lineup x " + options.PveSamples + " battle(s) per combination (seed " +
                              options.Seed.ToString(CultureInfo.InvariantCulture) + "), on " + context.Battlefield + "'s battlefields (`BattlefieldRegionId`), elemental kits.");
            report.AppendLine("- Targets (producer decision): at least " + Pct(RegularTarget) + " for every combination in every fight but the finale; the finale about " +
                              Pct(FinaleTarget) + " for every trio (mean within +/-" + SimOptions.Format(FinaleBand / 2.0) + ", every trio within +/-" + SimOptions.Format(FinaleBand) +
                              " points), so stance order does not matter.");
            report.AppendLine();

            report.AppendLine("## Fights");
            report.AppendLine();
            report.AppendLine("Clear rate over the combinations: min, mean and max; below = combinations under the target (the finale: outside its band).");
            report.AppendLine();
            report.AppendLine("| # | Location | Template | Lv | Party (levels) | Multiplier | Target | Min | Mean | Max | Below | Met |" + (options.HearthglenTune ? " Tuned |" : string.Empty));
            report.AppendLine("| ---: | --- | --- | ---: | --- | ---: | --- | ---: | ---: | ---: | ---: | --- |" + (options.HearthglenTune ? " ---: |" : string.Empty));
            foreach (FightResult fight in fights)
            {
                int below = 0;
                foreach (double rate in fight.Rates)
                {
                    below += fight.Finale ? (Math.Abs(rate - FinaleTarget) > FinaleBand ? 1 : 0) : (rate < RegularTarget ? 1 : 0);
                }

                report.AppendLine("| " + fight.Node.NodeId + " | " + (fight.Authored?.Name ?? string.Empty) + " | `" + fight.Node.TemplateId + "` | " + fight.Node.Level + " | " +
                                  fight.Levels.Describe() + " | x" + fight.Plan.Multiplier.ToString("0.000", CultureInfo.InvariantCulture) + " | " +
                                  (fight.Finale ? "~" + Pct(FinaleTarget) : ">= " + Pct(RegularTarget)) + " | " + Pct(fight.Min) + " | " + Pct(fight.Mean) + " | " +
                                  Pct(fight.Max) + " | " + below + "/" + fight.Rates.Length + " | " + (fight.Pass ? "yes" : "**no**") + " |" +
                                  (options.HearthglenTune ? " x" + fight.Suggested.ToString("0.000", CultureInfo.InvariantCulture) + " |" : string.Empty));
            }

            report.AppendLine();
            report.AppendLine("## Weakest combinations per fight");
            report.AppendLine();
            foreach (FightResult fight in fights)
            {
                List<int> order = new List<int>();
                for (int k = 0; k < fight.Rates.Length; k++)
                {
                    order.Add(k);
                }

                order.Sort((a, b) => fight.Rates[a] != fight.Rates[b] ? fight.Rates[a].CompareTo(fight.Rates[b]) : a.CompareTo(b));
                List<string> worst = new List<string>();
                for (int i = 0; i < Math.Min(5, order.Count); i++)
                {
                    worst.Add(Name(context, fight.Combinations[order[i]]) + " " + Pct(fight.Rates[order[i]]));
                }

                report.AppendLine("- `" + fight.Node.TemplateId + "`: " + string.Join("; ", worst) + ".");
            }

            FightResult finale = fights.Find(f => f.Finale);
            if (finale != null)
            {
                report.AppendLine();
                report.AppendLine("## Finale by trio");
                report.AppendLine();
                report.AppendLine("`" + finale.Node.TemplateId + "`: each one-per-stance trio's clear rate from each stance it can start from (pick order), and the spread.");
                report.AppendLine();
                report.AppendLine("| Trio | From Vanguard | From Ranged | From Skirmisher | Spread |");
                report.AppendLine("| --- | ---: | ---: | ---: | ---: |");
                Dictionary<string, double[]> byTrio = new Dictionary<string, double[]>(StringComparer.Ordinal);
                List<string> trios = new List<string>();
                for (int k = 0; k < finale.Combinations.Count; k++)
                {
                    int[] order = finale.Combinations[k];
                    List<string> names = new List<string>();
                    foreach (int s in order)
                    {
                        names.Add(context.Species[s].SpeciesId);
                    }

                    names.Sort(StringComparer.Ordinal);
                    string key = string.Join("/", names);
                    if (!byTrio.TryGetValue(key, out double[] rates))
                    {
                        rates = new[] { double.NaN, double.NaN, double.NaN };
                        byTrio.Add(key, rates);
                        trios.Add(key);
                    }

                    rates[Array.IndexOf(StarterPicks.StanceCycle, context.Species[order[0]].Stance)] = finale.Rates[k];
                }

                trios.Sort(StringComparer.Ordinal);
                foreach (string trio in trios)
                {
                    double[] rates = byTrio[trio];
                    Summarize(rates, out double min, out double _, out double max);
                    report.AppendLine("| " + trio + " | " + Pct(rates[0]) + " | " + Pct(rates[1]) + " | " + Pct(rates[2]) + " | " + SimOptions.Format(max - min) + " |");
                }
            }

            report.AppendLine();
            return report.ToString();
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
