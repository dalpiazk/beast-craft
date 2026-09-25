using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode campaign</c>'s "Post-game" section: every post-game region
    /// (<see cref="RegionData.IsPostGame"/>) played from its first stage to its boss on each
    /// <see cref="RunDifficulty"/>, by a team already at the level cap (the post-game regions are
    /// flat level 100, so every battle is at level gap 0). Same clear-chance model and route policy as
    /// the mainline campaign (<see cref="CampaignPacingSimulator.ClearChance"/>: a generated node at
    /// its post-game shape's <c>TargetClear</c>, the boss at <see cref="BossClear"/> of the
    /// difficulty; an Elite whenever one is offered, since the team is never below it; a loss
    /// retries the node), and the game's own <see cref="CampaignRules"/> and map generator with the
    /// full <c>regions.json</c> (the Hard maps from the region's <c>HardMode</c>). No XP, loot or
    /// economy: at the cap nothing levels, and Hard pays Normal's loot.
    /// <para>
    /// Seeded apart from the mainline campaigns, so it can never move a mainline number: run
    /// <c>r</c> of base seed <c>s</c> is <c>DeriveSeed(DeriveSeed(s, </c><see cref="Stream"/><c>), r)</c>,
    /// the same for Normal and Hard (common random numbers: the same maps, harder ids), each stage's
    /// map <c>DeriveSeed(run, 1000 + stage)</c>, every other draw one <see cref="Random"/> per run.
    /// The mainline tables never include a post-game region (the campaign plays
    /// <see cref="CampaignPacingSimulator.MainlineOnly"/>); this section is appended after them.
    /// </para>
    /// </summary>
    public static class PostGamePacing
    {
        /// <summary>The <see cref="LootRoller.DeriveSeed"/> stream every post-game run is derived from ("POST").</summary>
        public const int Stream = 0x504F5354;

        /// <summary>
        /// The post-game boss's clear chance at equal level per difficulty: the targets its two
        /// templates' <c>DifficultyOverride</c>s are calibrated to (Normal about 35%, Hard about 20%;
        /// docs/balance/tuning-log.md).
        /// </summary>
        public static double BossClear(RunDifficulty difficulty)
        {
            return difficulty == RunDifficulty.Hard ? 0.20 : 0.35;
        }

        private static readonly RunDifficulty[] Difficulties = { RunDifficulty.Normal, RunDifficulty.Hard };

        private const int Tiers = 3;

        private static readonly string[] TierNames = { "Battle (squad / horde)", "Elite and Gate", "Boss" };

        /// <summary>One post-game region run to its boss once.</summary>
        private sealed class Run
        {
            public int Battles;
            public int BossAttempts;
            public int Elites;
            public int Stuck;
            public readonly int[] Fought = new int[Tiers];
            public readonly int[] Won = new int[Tiers];
        }

        /// <summary>The section's text ("" when the library has no post-game region), starting with a blank line.</summary>
        public static string Build(SimOptions options, RegionLibraryData regions, EncounterLibrary encounters, List<int> seeds)
        {
            RegionLibrary library = RegionLibrary.Build(regions);
            List<RegionData> postGame = library.PostGameRegions();
            if (postGame.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("\n## Post-game\n\n");
            sb.Append("The post-game regions, outside every table and gate above: each played from its first stage through its boss, ")
              .Append(seeds.Count * options.PacingRuns).Append(" times per difficulty,\n")
              .Append("by a team at the level cap (every node is level 100, so every battle is at gap 0), on Normal and on Hard (the player's choice when an expedition\n")
              .Append("starts). Seeded apart from the campaigns above (stream ").Append(Stream.ToString(CultureInfo.InvariantCulture))
              .Append("); Normal and Hard share each run's seeds, so they walk the same maps with harder ids.\n")
              .Append("Clear chance: a generated node at its post-game shape's `TargetClear`, the boss at ")
              .Append(Pct(100.0 * BossClear(RunDifficulty.Normal))).Append(" (Normal) or ").Append(Pct(100.0 * BossClear(RunDifficulty.Hard)))
              .Append(" (Hard); an Elite is taken whenever offered; a loss retries the node.\n")
              .Append("No XP, loot or economy is modelled: nothing levels at the cap, and Hard pays Normal's loot (its extra reward is looks).\n");

            foreach (RegionData region in postGame)
            {
                foreach (RunDifficulty difficulty in Difficulties)
                {
                    List<Run> runs = new List<Run>();
                    foreach (int seed in seeds)
                    {
                        int stream = LootRoller.DeriveSeed(seed, Stream);
                        for (int r = 0; r < options.PacingRuns; r++)
                        {
                            runs.Add(Play(library, encounters, region, difficulty, LootRoller.DeriveSeed(stream, r)));
                        }
                    }

                    Append(sb, library, encounters, region, difficulty, runs);
                }
            }

            return sb.ToString();
        }

        private static Run Play(RegionLibrary library, EncounterLibrary encounters, RegionData region, RunDifficulty difficulty, int seed)
        {
            Run result = new Run();
            Random rng = new Random(seed);
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.Unlock(region.RegionId);
            save.Beasts.Add(OwnedBeast.Create("b1", "griffin", BeastProgression.MaxLevel));
            for (int stage = 0; stage < region.Stages; stage++)
            {
                CampaignResult started = CampaignRules.StartRun(save, library, region.RegionId, stage, LootRoller.DeriveSeed(seed, 1000 + stage), difficulty);
                if (!started.Success)
                {
                    throw new InvalidOperationException("Post-game model: " + started.Error);
                }

                while (save.Campaign.HasActiveRun)
                {
                    MapNode node = CampaignPacingSimulator.Choose(CampaignRules.Choices(save.Campaign.ActiveRun), BeastProgression.MaxLevel, rng);
                    if (node.Type == MapNodeType.Rest)
                    {
                        Expect(CampaignRules.Camp(save, library, node.NodeId, "b1"));
                        continue;
                    }

                    if (node.Type == MapNodeType.Shop)
                    {
                        Expect(CampaignRules.Trade(save, library, node.NodeId, null));
                        continue;
                    }

                    int tier = node.Type == MapNodeType.Boss ? 2 : node.Type == MapNodeType.Battle ? 0 : 1;
                    result.Elites += node.Type == MapNodeType.Elite ? 1 : 0;
                    double chance = CampaignPacingSimulator.ClearChance(TierClear(encounters, node, difficulty), 0.0);
                    for (int attempt = 0; ; attempt++)
                    {
                        bool cleared = rng.NextDouble() < chance;
                        result.Battles++;
                        result.Fought[tier]++;
                        result.Won[tier] += cleared ? 1 : 0;
                        result.BossAttempts += node.Type == MapNodeType.Boss ? 1 : 0;
                        if (!cleared && attempt + 1 >= CampaignPacingSimulator.StuckAttempts)
                        {
                            result.Stuck++;
                            cleared = true;
                        }

                        Expect(CampaignRules.ResolveBattle(save, library, node.NodeId, cleared ? BattleOutcome.PlayerVictory : BattleOutcome.EnemyVictory));
                        if (cleared)
                        {
                            break;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>A node's equal-level clear chance: the boss's per difficulty, else its shape's <c>TargetClear</c>.</summary>
        private static double TierClear(EncounterLibrary encounters, MapNode node, RunDifficulty difficulty)
        {
            if (node.Type == MapNodeType.Boss)
            {
                return BossClear(difficulty);
            }

            EncounterShapeData shape = encounters.GetShape(node.ShapeId);
            if (shape == null)
            {
                throw new InvalidOperationException("Post-game model: no encounter shape '" + node.ShapeId + "'.");
            }

            return shape.TargetClear / 100.0;
        }

        private static void Append(StringBuilder sb, RegionLibrary library, EncounterLibrary encounters, RegionData region, RunDifficulty difficulty, List<Run> runs)
        {
            RegionData fielded = library.RegionFor(region, difficulty);
            MapRulesData rules = library.RulesFor(region, difficulty);
            List<string> shapes = new List<string>();
            foreach (ShapeWeightData weight in fielded.ShapeWeights)
            {
                shapes.Add("`" + weight.ShapeId + "` " + Pct(encounters.GetShape(weight.ShapeId).TargetClear));
            }

            sb.Append("\n### ").Append(region.RegionId).Append(' ').Append(region.DisplayName).Append(", ").Append(difficulty).Append("\n\n");
            sb.Append("Level ").Append(region.MaxLevel).Append(", ").Append(region.Stages).Append(" stages; Battle shapes ").Append(string.Join(", ", shapes))
              .Append("; Elites and Gates `").Append(rules.EliteShapeId).Append("` ").Append(Pct(encounters.GetShape(rules.EliteShapeId).TargetClear))
              .Append("; boss `").Append(fielded.BossTemplateId).Append("` ").Append(Pct(100.0 * BossClear(difficulty))).Append(".\n\n");

            sb.Append("| Encounters | Fought (mean) | Cleared (mean) | Clear rate |\n");
            sb.Append("| --- | ---: | ---: | ---: |\n");
            int allFought = 0;
            int allWon = 0;
            for (int t = 0; t < Tiers; t++)
            {
                int fought = 0;
                int won = 0;
                foreach (Run run in runs)
                {
                    fought += run.Fought[t];
                    won += run.Won[t];
                }

                allFought += fought;
                allWon += won;
                sb.Append("| ").Append(TierNames[t]).Append(" | ").Append(F1((double)fought / runs.Count)).Append(" | ").Append(F1((double)won / runs.Count))
                  .Append(" | ").Append(Pct(fought == 0 ? 0.0 : 100.0 * won / fought)).Append(" |\n");
            }

            sb.Append("| **All** | ").Append(F1((double)allFought / runs.Count)).Append(" | ").Append(F1((double)allWon / runs.Count)).Append(" | ")
              .Append(Pct(allFought == 0 ? 0.0 : 100.0 * allWon / allFought)).Append(" |\n\n");

            List<double> battles = runs.ConvertAll(run => (double)run.Battles);
            List<double> boss = runs.ConvertAll(run => (double)run.BossAttempts);
            int stuck = 0;
            double elites = 0.0;
            foreach (Run run in runs)
            {
                stuck += run.Stuck;
                elites += run.Elites;
            }

            sb.Append("| To clear the region | p10 | p50 | p90 | Mean |\n");
            sb.Append("| --- | ---: | ---: | ---: | ---: |\n");
            sb.Append("| Battles (losses included) | ").Append(Int(P(battles, 10))).Append(" | ").Append(Int(P(battles, 50))).Append(" | ").Append(Int(P(battles, 90)))
              .Append(" | ").Append(F1(Mean(battles))).Append(" |\n");
            sb.Append("| Boss attempts | ").Append(Int(P(boss, 10))).Append(" | ").Append(Int(P(boss, 50))).Append(" | ").Append(Int(P(boss, 90))).Append(" | ")
              .Append(F1(Mean(boss))).Append(" |\n\n");
            sb.Append("Elites taken: ").Append(F1(elites / runs.Count)).Append(" per region (").Append(F1(elites / runs.Count / Math.Max(1, region.Stages)))
              .Append(" per stage); nodes still lost after ").Append(CampaignPacingSimulator.StuckAttempts).Append(" tries: ").Append(stuck).Append(".\n");
        }

        private static void Expect(CampaignResult result)
        {
            if (!result.Success)
            {
                throw new InvalidOperationException("Post-game model: " + result.Error);
            }
        }

        /// <summary>Nearest-rank percentile.</summary>
        private static double P(List<double> values, double p)
        {
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            return sorted.Count == 0 ? 0.0 : sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
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

        private static string Int(double value)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string F1(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Pct(double value)
        {
            return SimOptions.Format(value) + "%";
        }
    }
}
