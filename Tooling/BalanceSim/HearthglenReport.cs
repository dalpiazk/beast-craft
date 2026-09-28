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
    /// pick joins at level 1; each fight pays its winners; the camp trains the newest pick and then
    /// catches every pick up to the leader's level (Hearthglen's camp, <c>CampaignRules.Camp</c>).
    /// </para>
    /// <para>
    /// <strong>Battlefields and elements</strong> as the game fights them: the fights authored
    /// <c>OpenBoard</c> on the open board, the rest on the region's battlefields; an
    /// <c>AdaptiveElements</c> fight (the finale) with every enemy of the element neutral against the
    /// combination's beasts (<see cref="ElementAdaptation"/>), and reported again with the template's
    /// authored elements for comparison.
    /// </para>
    /// <para>
    /// <strong>Targets</strong> (producer decisions, round 2): at least <see cref="RegularTarget"/>% for
    /// every combination in every fight but the finale; the finale's WEAKEST trio at about
    /// <see cref="FinaleFloor"/>% or more (a high mean is fine). <c>--tune</c> also searches each
    /// fight's multiplier: the highest whose weakest combination still clears its target.
    /// </para>
    /// </summary>
    public static class HearthglenReport
    {
        public const double RegularTarget = 90.0;
        public const double FinaleFloor = 60.0;

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

            public readonly Dictionary<string, PveSimulator> Simulators = new Dictionary<string, PveSimulator>(StringComparer.Ordinal);

            /// <summary>A simulator for parties of <paramref name="size"/> on <paramref name="battlefield"/>'s layouts (null = the open board).</summary>
            public PveSimulator For(int size, string battlefield)
            {
                string key = size + "|" + (battlefield ?? "open");
                if (!Simulators.TryGetValue(key, out PveSimulator pve))
                {
                    SimOptions copy = Options.ForSeed(Options.Seed);
                    copy.TeamSize = size;
                    copy.ObstaclesRegion = battlefield;
                    pve = new PveSimulator(copy, Species) { GearFor = null };
                    Simulators.Add(key, pve);
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
            public string Battlefield;
            public bool Adaptive;

            /// <summary>An adaptive fight's rates with the template's authored elements instead (for comparison); null otherwise.</summary>
            public double[] AuthoredRates;

            /// <summary>Per combination, the adapted element (adaptive fights).</summary>
            public Element[] Elements;

            public double Target
            {
                get { return Finale ? FinaleFloor : RegularTarget; }
            }

            public bool Pass
            {
                get { return Min >= Target; }
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
                    int leader = 0;
                    foreach (BeastProgress pick in picks)
                    {
                        leader = Math.Max(leader, pick.Level);
                    }

                    foreach (BeastProgress pick in picks)
                    {
                        if (pick.Level < leader)
                        {
                            pick.Level = leader;
                            pick.Xp = 0;
                        }
                    }
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
            FixedNodeData authored = context.Regions.FixedNode(CampaignProgress.TutorialRegionId, node.NodeId);
            FightResult fight = new FightResult
            {
                Node = node,
                Authored = authored,
                Plan = plan,
                Finale = finale,
                Levels = levels,
                Combinations = context.Combinations[size],
                Battlefield = context.Regions.BattlefieldFor(CampaignProgress.TutorialRegionId, node.NodeId),
                Adaptive = authored != null && authored.AdaptiveElements
            };

            // Per combination, the lineup it fights: the authored one, or (adaptive) its element's copy.
            fight.Elements = new Element[fight.Combinations.Count];
            EncounterShape authoredShape = Copies(context, plan);
            Dictionary<Element, EncounterShape> adapted = new Dictionary<Element, EncounterShape>();
            EncounterShape[] shapes = new EncounterShape[fight.Combinations.Count];
            for (int k = 0; k < shapes.Length; k++)
            {
                if (!fight.Adaptive)
                {
                    shapes[k] = authoredShape;
                    continue;
                }

                List<CreatureSpeciesSO> team = new List<CreatureSpeciesSO>();
                foreach (int s in fight.Combinations[k])
                {
                    team.Add(context.Species[s]);
                }

                Element element = ElementAdaptation.NeutralElement(ElementAdaptation.ElementsOf(team));
                fight.Elements[k] = element;
                if (!adapted.TryGetValue(element, out EncounterShape shape))
                {
                    shape = Copies(context, plan.WithElement(element));
                    adapted.Add(element, shape);
                }

                shapes[k] = shape;
            }

            fight.Rates = Rates(context, size, levels, node.Level, fight.Battlefield, shapes, plan.Multiplier, fight.Combinations);
            Summarize(fight.Rates, out fight.Min, out fight.Mean, out fight.Max);
            if (fight.Adaptive)
            {
                EncounterShape[] plain = new EncounterShape[shapes.Length];
                for (int k = 0; k < plain.Length; k++)
                {
                    plain[k] = authoredShape;
                }

                fight.AuthoredRates = Rates(context, size, levels, node.Level, fight.Battlefield, plain, plan.Multiplier, fight.Combinations);
            }

            if (context.Options.HearthglenTune)
            {
                fight.Suggested = Tune(context, size, levels, node.Level, fight.Battlefield, shapes, fight.Combinations, fight.Target);
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
                string element = plan.Enemies.Count > 0 && plan.Enemies[0].Element != Element.None && plan.ElementScheme == null && IsUniform(plan) ? "-" + plan.Enemies[0].Element : string.Empty;
                Encounter encounter = new Encounter { Id = plan.EncounterId + element + "-" + copy.ToString("00", CultureInfo.InvariantCulture), Arena = plan.Arena };
                for (int i = 0; i < plan.Enemies.Count; i++)
                {
                    encounter.Enemies.Add(factory.Slot(plan.Enemies[i].EnemyId, plan.Enemies[i].Element, i + 1));
                }

                shape.Compositions.Add(encounter);
            }

            return shape;
        }

        /// <summary>Each combination's clear rate (percent) over every copy and sample of its lineup (<paramref name="shapes"/>, per combination), at <paramref name="multiplier"/>.</summary>
        private static double[] Rates(Context context, int size, Levels levels, int level, string battlefield, EncounterShape[] shapes, double multiplier,
                                      List<int[]> combinations)
        {
            PveSimulator pve = context.For(size, battlefield);
            int copies = shapes[0].Compositions.Count;
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

            Dictionary<string, bool[]> ties = new Dictionary<string, bool[]>(StringComparer.Ordinal);
            foreach (EncounterShape shape in shapes)
            {
                foreach (Encounter composition in shape.Compositions)
                {
                    if (!ties.ContainsKey(composition.Id))
                    {
                        ties.Add(composition.Id, pve.PlayersWinTies(KitMode.Elemental, level, composition.Id));
                    }
                }
            }

            int[] cleared = new int[combinations.Count];
            int total = combinations.Count * copies * samples;
            Parallel.For(0, total, i =>
            {
                int k = i / (copies * samples);
                int c = (i / samples) % copies;
                int sample = i % samples;
                int t = teamIndex[k];
                Encounter encounter = shapes[k].Compositions[c];
                PveBattle battle = pve.RunBattle(KitMode.Elemental, level, level, encounter, multiplier, t, sample, false, ties[encounter.Id][t], true, memberLevels[k],
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

        private static double Tune(Context context, int size, Levels levels, int level, string battlefield, EncounterShape[] shapes, List<int[]> combinations, double target)
        {
            double lo = MinMultiplier;
            double hi = MaxMultiplier;
            for (int step = 0; step < TuneSteps; step++)
            {
                double mid = Math.Sqrt(lo * hi);
                Summarize(Rates(context, size, levels, level, battlefield, shapes, mid, combinations), out double min, out double _, out double _);
                bool easyEnough = min >= target;
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

        private static bool IsUniform(EncounterPlan plan)
        {
            foreach (EncounterLineupEnemy enemy in plan.Enemies)
            {
                if (enemy.Element != plan.Enemies[0].Element)
                {
                    return false;
                }
            }

            return true;
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
            report.AppendLine("  the newest pick, then catches every pick up to the leader's level). The library avatar (as calibrated) at its own profile level.");
            report.AppendLine("  No gear, no consumable.");
            report.AppendLine("- Battles: " + options.Compositions + " seeded copies of each lineup x " + options.PveSamples + " battle(s) per combination (seed " +
                              options.Seed.ToString(CultureInfo.InvariantCulture) + "), elemental kits; the `OpenBoard` fights on the open board, the rest on " +
                              context.Battlefield + "'s battlefields (`BattlefieldRegionId`). The finale's enemies take the element neutral against the");
            report.AppendLine("  combination's beasts (`AdaptiveElements`, `ElementAdaptation`).");
            report.AppendLine("- Targets (producer decisions, round 2): at least " + Pct(RegularTarget) + " for every combination in every fight but the finale; the finale's");
            report.AppendLine("  weakest trio at about " + Pct(FinaleFloor) + " or more (a high mean is fine).");
            report.AppendLine();

            report.AppendLine("## Fights");
            report.AppendLine();
            report.AppendLine("Clear rate over the combinations: min, mean and max; below = combinations under the target.");
            report.AppendLine();
            report.AppendLine("| # | Location | Template | Lv | Party (levels) | Multiplier | Target | Min | Mean | Max | Below | Met |" + (options.HearthglenTune ? " Tuned |" : string.Empty));
            report.AppendLine("| ---: | --- | --- | ---: | --- | ---: | --- | ---: | ---: | ---: | ---: | --- |" + (options.HearthglenTune ? " ---: |" : string.Empty));
            foreach (FightResult fight in fights)
            {
                int below = 0;
                foreach (double rate in fight.Rates)
                {
                    below += rate < fight.Target ? 1 : 0;
                }

                report.AppendLine("| " + fight.Node.NodeId + " | " + (fight.Authored?.Name ?? string.Empty) + (fight.Battlefield == null ? " (open board)" : string.Empty) + " | `" +
                                  fight.Node.TemplateId + "` | " + fight.Node.Level + " | " +
                                  fight.Levels.Describe() + " | x" + fight.Plan.Multiplier.ToString("0.000", CultureInfo.InvariantCulture) + " | " +
                                  (fight.Finale ? "weakest >= " : ">= ") + Pct(fight.Target) + " | " + Pct(fight.Min) + " | " + Pct(fight.Mean) + " | " +
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
                if (finale.AuthoredRates != null)
                {
                    Summarize(finale.AuthoredRates, out double amin, out double amean, out double amax);
                    report.AppendLine("Element adaptation: with it (the game) min " + Pct(finale.Min) + ", mean " + Pct(finale.Mean) + ", max " + Pct(finale.Max) +
                                      "; with the template's authored elements instead, min " + Pct(amin) + ", mean " + Pct(amean) + ", max " + Pct(amax) +
                                      " (the same multiplier).");
                    report.AppendLine();
                }

                report.AppendLine("`" + finale.Node.TemplateId + "`: each one-per-stance trio's clear rate from each stance it can start from (pick order), the spread,");
                report.AppendLine("and the element its enemies take.");
                report.AppendLine();
                report.AppendLine("| Trio | From Vanguard | From Ranged | From Skirmisher | Spread | Enemy element |");
                report.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");
                Dictionary<string, double[]> byTrio = new Dictionary<string, double[]>(StringComparer.Ordinal);
                Dictionary<string, Element> elementOf = new Dictionary<string, Element>(StringComparer.Ordinal);
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
                    elementOf[key] = finale.Elements[k];
                }

                trios.Sort(StringComparer.Ordinal);
                foreach (string trio in trios)
                {
                    double[] rates = byTrio[trio];
                    Summarize(rates, out double min, out double _, out double max);
                    report.AppendLine("| " + trio + " | " + Pct(rates[0]) + " | " + Pct(rates[1]) + " | " + Pct(rates[2]) + " | " + SimOptions.Format(max - min) + " | " +
                                      (finale.Adaptive ? elementOf[trio].ToString() : "authored") + " |");
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
