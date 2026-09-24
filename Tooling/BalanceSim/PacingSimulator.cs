using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Progression;
using BeastCraft.Skills;
using UnityEngine;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--mode pacing</c>: a Monte Carlo campaign model of skill progression and the material
    /// economy. No battles are fought: each simulated battle draws its encounter shape, whether it
    /// was cleared and how often the focus skill fired, then pays out exactly what the game would —
    /// practice XP through <see cref="SkillProgression.AwardPractice"/> and drops through
    /// <see cref="LootRoller.RollClear"/> against <c>drop-tables.json</c> — and a feeding policy
    /// spends the materials. The report gives the p10 / p50 / p90 battle counts for the focus skill
    /// to reach levels 5, 10, 15 and 20 and checks the medians against <see cref="Gates"/>. It also
    /// levels the avatar through <see cref="AvatarProgression.AwardBattle"/> and checks its median
    /// level stays within <see cref="AvatarLevelTolerance"/> of the encounter level.
    /// <para>
    /// <strong>Campaign model</strong> (the constants below; README.md, "Pacing"): battle
    /// <c>i</c> (0-based) is fought at encounter level <c>min(100, 1 + i / BattlesPerLevel)</c>;
    /// its shape is drawn by <see cref="ShapeWeights"/>; it is cleared with probability
    /// <see cref="ClearChance"/>; the focus skill fires a uniform
    /// <see cref="FocusUsesMin"/>-<see cref="FocusUsesMax"/> times (practice is credited win or
    /// lose, drops only on a clear), and a secondary skill the same.
    /// </para>
    /// <para>
    /// <strong>Feeding policy</strong> (a dedicated player pushing one signature skill): after each
    /// battle the focus skill passes any gate it is waiting at with the lowest adequate material
    /// held, then is fed materials lowest tier first while it is below its level cap, keeping one
    /// material of each tier a later gate of its own will need. Whatever it cannot use (it is gated
    /// or maxed) spills to the secondary skill under the same rules, never touching the focus
    /// skill's reserve.
    /// </para>
    /// <para>
    /// Deterministic: campaign <c>r</c> of base seed <c>s</c> is seeded
    /// <c>LootRoller.DeriveSeed(s, r)</c> and each of its battles <c>DeriveSeed(campaign, i)</c>,
    /// drawn in a fixed order (shape, clear, focus uses, secondary uses, then the loot rolls).
    /// </para>
    /// </summary>
    public static class PacingSimulator
    {
        /// <summary>The drop tables' path relative to the repo root.</summary>
        public const string DropTablesRepoRelativePath = "BeastCraft/" + DropTableData.ProjectRelativePath;

        /// <summary>Battles per campaign (<c>--battles</c>): a 400-600-battle playthrough, levels 1-100 at <see cref="BattlesPerLevel"/>.</summary>
        public const int DefaultBattles = 500;

        /// <summary>Monte Carlo campaigns per base seed (<c>--runs</c>).</summary>
        public const int DefaultRuns = 1000;

        /// <summary>Encounter level rises by one every this many battles, from 1 to 100.</summary>
        public const int BattlesPerLevel = 5;

        /// <summary>
        /// Share of battles cleared. The PvE report calibrates to 50% as a stress point; a campaign
        /// player wins most fights (a loss is a retry), and a loss still pays practice.
        /// </summary>
        public const double ClearChance = 0.8;

        /// <summary>
        /// The focus skill's fires per battle, uniform in [min, max]. From the PvE report: a cleared
        /// battle lasts 5-19 units of normalized time and a beast takes about 0.3-0.7 turns per unit,
        /// so 3-10 turns; a cooldown-1 signature skill fires on most of them. Well under
        /// <see cref="SkillProgression.PracticeUseCapPerAward"/> (20), which only a long fight with a
        /// cooldown-0 skill reaches.
        /// </summary>
        public const int FocusUsesMin = 3;

        /// <summary>See <see cref="FocusUsesMin"/>.</summary>
        public const int FocusUsesMax = 9;

        /// <summary>Encounter shapes and their weights in the campaign mix (the generated PvE shapes).</summary>
        public static readonly KeyValuePair<string, int>[] ShapeWeights =
        {
            new KeyValuePair<string, int>("solo", 15),
            new KeyValuePair<string, int>("elite", 20),
            new KeyValuePair<string, int>("squad", 35),
            new KeyValuePair<string, int>("horde", 30),
        };

        /// <summary>A pacing target: the focus skill's median battles to reach a level must fall in [Min, Max].</summary>
        public sealed class Gate
        {
            public Gate(int level, int min, int max, string label)
            {
                Level = level;
                Min = min;
                Max = max;
                Label = label;
            }

            public int Level { get; }
            public int Min { get; }
            public int Max { get; }
            public string Label { get; }
        }

        /// <summary>The pacing targets (design: stage 5 economy). The median (p50) must land in each band.</summary>
        public static readonly Gate[] Gates =
        {
            new Gate(5, 15, 20, "15-20"),
            new Gate(10, 70, 90, "~80 (70-90)"),
            new Gate(15, 160, 200, "~180 (160-200)"),
            new Gate(20, 295, 325, "~300-320 (295-325)"),
        };

        /// <summary>
        /// Avatar pacing target: at every 50-battle checkpoint the avatar's median level is within this
        /// many levels of the encounter level (it levels alongside the content, neither outgrowing nor
        /// falling behind it).
        /// </summary>
        public const int AvatarLevelTolerance = 3;

        /// <summary>
        /// Loads the skill library's materials and the drop tables, runs the Monte Carlo (twice with
        /// <c>--self-check</c>), prints the report and returns an exit code: 0, 2 on unreadable or
        /// invalid data, 3 when <c>--self-check</c> finds the two runs differ or a median misses its target.
        /// </summary>
        public static int Run(SimOptions options)
        {
            string libraryPath = SkillLibraryKits.ResolvePath(options.SkillLibraryPath);
            string tablesPath = RosterLoader.ResolveFile(options.DropTablesPath, DropTablesRepoRelativePath);
            if (libraryPath == null || !File.Exists(libraryPath) || tablesPath == null || !File.Exists(tablesPath))
            {
                Console.Error.WriteLine("Could not find " + SkillLibraryKits.RepoRelativePath + " or " + DropTablesRepoRelativePath +
                                        "; run from inside the repo or pass --skill-library / --drop-tables.");
                return 2;
            }

            SkillLibraryData library;
            DropTableData tables;
            try
            {
                JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
                library = JsonSerializer.Deserialize<SkillLibraryData>(File.ReadAllText(libraryPath), json);
                tables = JsonSerializer.Deserialize<DropTableData>(File.ReadAllText(tablesPath), json);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Could not read the skill library or drop tables: " + exception.Message);
                return 2;
            }

            List<string> errors = SkillLibraryValidator.Validate(library);
            errors.AddRange(DropTableValidator.Validate(tables, library.Materials));
            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Drop tables '" + tablesPath + "' (or the skill library) are invalid:");
                foreach (string error in errors)
                {
                    Console.Error.WriteLine("  " + error);
                }

                return 2;
            }

            Model model = new Model(library.Materials, DropTableBuilder.Build(tables, DropTableBuilder.TierLookup(library.Materials)));
            List<int> seeds = options.Seeds ?? new List<int> { options.Seed };

            DateTime start = DateTime.UtcNow;
            string report = BuildReport(options, model, seeds, out List<string> misses);
            double seconds = (DateTime.UtcNow - start).TotalSeconds;

            if (options.SelfCheck)
            {
                string second = BuildReport(options, model, seeds, out List<string> _);
                if (!string.Equals(report, second, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Self-check failed: two pacing runs with identical inputs produced different reports.");
                    return 3;
                }
            }

            Console.Out.Write(report);
            Console.Error.WriteLine("Runtime: " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s for " + seeds.Count * options.PacingRuns +
                                    " campaigns of " + options.PacingBattles + " battles.");
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
                    Console.Error.WriteLine("Self-check failed: pacing targets missed:");
                    foreach (string miss in misses)
                    {
                        Console.Error.WriteLine("  " + miss);
                    }

                    return 3;
                }

                Console.Error.WriteLine("Self-check passed: two pacing runs identical, every median inside its target.");
            }

            return 0;
        }

        /// <summary>The fixed inputs of every campaign: the materials (as the game builds them) and the drop table.</summary>
        public sealed class Model
        {
            public Model(SkillMaterialData[] materials, DropTable table)
            {
                Table = table;
                foreach (SkillMaterialData data in materials)
                {
                    SkillMaterialSO material = ScriptableObject.CreateInstance<SkillMaterialSO>();
                    SkillLibraryBuilder.ApplyMaterial(data, material);
                    Materials.Add(material);
                }

                // Lowest tier first: the order materials are spent and fed in.
                Materials.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : string.CompareOrdinal(a.MaterialId, b.MaterialId));
            }

            public DropTable Table { get; }

            public List<SkillMaterialSO> Materials { get; } = new List<SkillMaterialSO>();

            public int MaxTier
            {
                get { return Materials.Count == 0 ? 0 : Materials[Materials.Count - 1].Tier; }
            }
        }

        /// <summary>One campaign's outcome.</summary>
        public sealed class Campaign
        {
            /// <summary>1-based battle count at which the focus skill first reached each level (index = level); 0 = never.</summary>
            public int[] FocusReached = new int[SkillProgressionDefinition.DefaultMaxLevel + 1];

            /// <summary>The focus skill's level after each battle.</summary>
            public int[] FocusLevelAfter;

            /// <summary>The avatar's level after each battle.</summary>
            public int[] AvatarLevelAfter;

            /// <summary>Materials gained per tier (index = tier).</summary>
            public int[] GainedByTier;

            /// <summary>1-based battle of the first gain of each tier; 0 = never.</summary>
            public int[] FirstOfTier;

            /// <summary>XP the focus skill got from practice and from materials, up to reaching level 20 (or the campaign's end).</summary>
            public long PracticeXp;
            public long MaterialXp;

            public int SecondaryLevel;
            public int SecondaryTier;
            public int Clears;
        }

        /// <summary>Plays one campaign.</summary>
        public static Campaign Play(Model model, int campaignSeed, int battles)
        {
            SkillProgressionDefinition definition = new SkillProgressionDefinition();
            SkillProgress focus = new SkillProgress("focus");
            SkillProgress secondary = new SkillProgress("secondary");
            MaterialInventory inventory = new MaterialInventory();
            AvatarProgress avatar = new AvatarProgress();
            Campaign campaign = new Campaign
            {
                FocusLevelAfter = new int[battles],
                AvatarLevelAfter = new int[battles],
                GainedByTier = new int[model.MaxTier + 1],
                FirstOfTier = new int[model.MaxTier + 1]
            };
            campaign.FocusReached[1] = 0;

            int totalWeight = 0;
            foreach (KeyValuePair<string, int> shape in ShapeWeights)
            {
                totalWeight += shape.Value;
            }

            for (int i = 0; i < battles; i++)
            {
                System.Random rng = new System.Random(LootRoller.DeriveSeed(campaignSeed, i));
                int level = Math.Min(DropTableValidator.MaxEncounterLevel, 1 + (i / BattlesPerLevel));
                string shape = PickShape(rng.Next(totalWeight));
                bool cleared = rng.NextDouble() < ClearChance;
                int focusUses = FocusUsesMin + rng.Next(FocusUsesMax - FocusUsesMin + 1);
                int secondaryUses = FocusUsesMin + rng.Next(FocusUsesMax - FocusUsesMin + 1);

                bool focusDone = focus.Level >= definition.EffectiveMaxLevel;
                int before = TotalXp(focus);
                SkillProgression.AwardPractice(focus, definition, focusUses);
                if (!focusDone)
                {
                    campaign.PracticeXp += TotalXp(focus) - before;
                }

                SkillProgression.AwardPractice(secondary, definition, secondaryUses);
                AvatarProgression.AwardBattle(avatar, cleared ? BattleOutcome.PlayerVictory : BattleOutcome.EnemyVictory, level);
                campaign.AvatarLevelAfter[i] = avatar.Level;

                if (cleared)
                {
                    campaign.Clears++;
                    LootResult loot = LootRoller.RollClear(model.Table, shape, level, inventory, rng);
                    foreach (MaterialStack stack in loot.Drops)
                    {
                        int tier = TierOf(model, stack.MaterialId);
                        campaign.GainedByTier[tier] += stack.Quantity;
                        if (campaign.FirstOfTier[tier] == 0)
                        {
                            campaign.FirstOfTier[tier] = i + 1;
                        }
                    }
                }

                before = TotalXp(focus);
                Spend(model, focus, definition, inventory, null);
                if (!focusDone)
                {
                    campaign.MaterialXp += Math.Max(0, TotalXp(focus) - before);
                }

                Spend(model, secondary, definition, inventory, Reserve(model, focus, definition));

                campaign.FocusLevelAfter[i] = focus.Level;
                for (int l = 2; l <= focus.Level && l < campaign.FocusReached.Length; l++)
                {
                    if (campaign.FocusReached[l] == 0)
                    {
                        campaign.FocusReached[l] = i + 1;
                    }
                }
            }

            campaign.SecondaryLevel = secondary.Level;
            campaign.SecondaryTier = secondary.Tier;
            return campaign;
        }

        /// <summary>
        /// Spends held materials on <paramref name="skill"/>: pass the gate it waits at with the lowest
        /// adequate tier, then feed lowest tier first while it is below its cap. With a
        /// <paramref name="reserve"/> (another skill's), never takes a material below the count
        /// reserved for its tier; without one, keeps one material per tier its own later gates need.
        /// </summary>
        private static void Spend(Model model, SkillProgress skill, SkillProgressionDefinition definition, MaterialInventory inventory, int[] reserve)
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

        /// <summary>The lowest-tier material of at least <paramref name="minTier"/> held above its reserve.</summary>
        private static SkillMaterialSO FirstAvailable(Model model, MaterialInventory inventory, int[] reserve, int minTier)
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

        /// <summary>One material per tier that an unpassed gate of <paramref name="skill"/> requires (index = tier).</summary>
        private static int[] Reserve(Model model, SkillProgress skill, SkillProgressionDefinition definition)
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

        private static int TotalXp(SkillProgress skill)
        {
            return SkillProgression.TotalXpToReach(skill.Level) + skill.Xp;
        }

        private static int TierOf(Model model, string materialId)
        {
            foreach (SkillMaterialSO material in model.Materials)
            {
                if (material.MaterialId == materialId)
                {
                    return material.Tier;
                }
            }

            return 0;
        }

        private static string PickShape(int roll)
        {
            foreach (KeyValuePair<string, int> shape in ShapeWeights)
            {
                if (roll < shape.Value)
                {
                    return shape.Key;
                }

                roll -= shape.Value;
            }

            return ShapeWeights[ShapeWeights.Length - 1].Key;
        }

        /// <summary>Nearest-rank percentile of battle counts, where 0 (never reached) sorts last.</summary>
        public static int Percentile(List<int> values, double p)
        {
            List<int> sorted = new List<int>(values.Count);
            foreach (int v in values)
            {
                sorted.Add(v == 0 ? int.MaxValue : v);
            }

            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            int index = Math.Min(sorted.Count - 1, Math.Max(0, rank - 1));
            return sorted[index] == int.MaxValue ? 0 : sorted[index];
        }

        private static string BuildReport(SimOptions options, Model model, List<int> seeds, out List<string> misses)
        {
            List<Campaign> campaigns = new List<Campaign>();
            foreach (int seed in seeds)
            {
                for (int r = 0; r < options.PacingRuns; r++)
                {
                    campaigns.Add(Play(model, LootRoller.DeriveSeed(seed, r), options.PacingBattles));
                }
            }

            misses = new List<string>();
            StringBuilder sb = new StringBuilder();
            sb.Append("# Beast Craft skill-progression pacing report\n\n");
            sb.Append("Generated by `dotnet run --project Tooling/BalanceSim -c Release -- --mode pacing` (see Tooling/BalanceSim/README.md, \"Pacing\").\n");
            sb.Append("A Monte Carlo campaign model, not fought battles: practice XP and drops go through the real `SkillProgression` and\n");
            sb.Append("`LootRoller` against `drop-tables.json`. Tunable starting values, not confirmed balance.\n\n");

            sb.Append("## Configuration\n\n");
            sb.Append("- Campaigns: ").Append(campaigns.Count).Append(" (").Append(options.PacingRuns).Append(" per base seed; base seed")
              .Append(seeds.Count == 1 ? " " : "s ").Append(SimOptions.Join(seeds)).Append("), ").Append(options.PacingBattles).Append(" battles each\n");
            sb.Append("- Encounter level: 1 + battle / ").Append(BattlesPerLevel).Append(" (level 100 from battle ").Append(99 * BattlesPerLevel + 1).Append(")\n");
            sb.Append("- Shape mix: ");
            for (int i = 0; i < ShapeWeights.Length; i++)
            {
                sb.Append(i == 0 ? string.Empty : ", ").Append('`').Append(ShapeWeights[i].Key).Append("` ").Append(ShapeWeights[i].Value);
            }

            sb.Append("; clear chance ").Append(Pct(ClearChance * 100.0)).Append(" (drops on clears only; practice either way)\n");
            sb.Append("- Focus skill fires ").Append(FocusUsesMin).Append('-').Append(FocusUsesMax).Append(" times per battle (uniform) at ")
              .Append(SkillProgression.PracticeXpPerUse).Append(" XP per use; the secondary skill the same\n");
            sb.Append("- Materials: ");
            for (int i = 0; i < model.Materials.Count; i++)
            {
                SkillMaterialSO m = model.Materials[i];
                sb.Append(i == 0 ? string.Empty : ", ").Append('`').Append(m.MaterialId).Append("` tier ").Append(m.Tier).Append(" = ").Append(m.XpValue).Append(" XP");
            }

            sb.Append("\n- Policy: pass a waiting gate with the lowest adequate material; feed lowest tier first while below the cap, keeping one\n");
            sb.Append("  material per tier a later gate needs; spill the rest to the secondary skill.\n\n");

            sb.Append("## Battles for the focus skill to reach each level\n\n");
            sb.Append("| Level | Cumulative XP | Practice only (mean uses) | Target (p50) | p10 | p50 | p90 | Never reached | Verdict |\n");
            sb.Append("| ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | --- |\n");
            double meanUses = (FocusUsesMin + FocusUsesMax) / 2.0;
            foreach (Gate gate in Gates)
            {
                List<int> reached = campaigns.ConvertAll(c => c.FocusReached[gate.Level]);
                int p10 = Percentile(reached, 10);
                int p50 = Percentile(reached, 50);
                int p90 = Percentile(reached, 90);
                int never = reached.FindAll(v => v == 0).Count;
                bool ok = p50 >= gate.Min && p50 <= gate.Max;
                if (!ok)
                {
                    misses.Add("L" + gate.Level + ": p50 " + Battles(p50) + " is outside " + gate.Min + "-" + gate.Max + ".");
                }

                int xp = SkillProgression.TotalXpToReach(gate.Level);
                sb.Append("| ").Append(gate.Level).Append(" | ").Append(xp.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(Math.Ceiling(xp / (meanUses * SkillProgression.PracticeXpPerUse)).ToString(CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(gate.Label).Append(" | ").Append(Battles(p10)).Append(" | ").Append(Battles(p50)).Append(" | ").Append(Battles(p90))
                  .Append(" | ").Append(Pct(100.0 * never / campaigns.Count)).Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
            }

            sb.Append("\nPractice only ignores the gates (a skill cannot pass one without a material). Never reached = campaigns that end first.\n\n");

            sb.Append("## Focus skill level by battle\n\n");
            sb.Append("| Battle | Encounter level | p10 | p50 | p90 |\n| ---: | ---: | ---: | ---: | ---: |\n");
            for (int b = 50; b <= options.PacingBattles; b += 50)
            {
                List<int> levels = campaigns.ConvertAll(c => c.FocusLevelAfter[b - 1]);
                sb.Append("| ").Append(b).Append(" | ").Append(Math.Min(100, 1 + ((b - 1) / BattlesPerLevel))).Append(" | ")
                  .Append(LevelPercentile(levels, 10)).Append(" | ").Append(LevelPercentile(levels, 50)).Append(" | ").Append(LevelPercentile(levels, 90)).Append(" |\n");
            }

            sb.Append("\n## Avatar level\n\n");
            sb.Append("`AvatarProgression.AwardBattle` after every battle (").Append(AvatarProgression.ParticipationXp).Append(" XP win or lose, + ")
              .Append(AvatarProgression.ClearBaseXp).Append(" + ").Append(AvatarProgression.ClearXpPerEnemyLevel).Append(" x encounter level on a clear; a level costs ")
              .Append(AvatarProgression.XpCurveBase).Append(" + ").Append(AvatarProgression.XpCurvePerLevel).Append(" x level). Target: median within ")
              .Append(AvatarLevelTolerance).Append(" levels of the encounter level at every checkpoint.\n\n");
            sb.Append("| Battle | Encounter level | p10 | p50 | p90 | Verdict |\n| ---: | ---: | ---: | ---: | ---: | --- |\n");
            for (int b = 50; b <= options.PacingBattles; b += 50)
            {
                List<int> levels = campaigns.ConvertAll(c => c.AvatarLevelAfter[b - 1]);
                int encounter = Math.Min(100, 1 + ((b - 1) / BattlesPerLevel));
                int p50 = LevelPercentile(levels, 50);
                bool ok = Math.Abs(p50 - encounter) <= AvatarLevelTolerance;
                if (!ok)
                {
                    misses.Add("Avatar at battle " + b + ": median level " + p50 + " vs encounter level " + encounter + ".");
                }

                sb.Append("| ").Append(b).Append(" | ").Append(encounter).Append(" | ").Append(LevelPercentile(levels, 10)).Append(" | ").Append(p50)
                  .Append(" | ").Append(LevelPercentile(levels, 90)).Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
            }

            sb.Append("\n## Materials\n\n");
            sb.Append("| Material | Tier | Gained per campaign (mean) | First gained, battle p10 / p50 / p90 |\n| --- | ---: | ---: | --- |\n");
            foreach (SkillMaterialSO m in model.Materials)
            {
                double mean = 0.0;
                foreach (Campaign c in campaigns)
                {
                    mean += c.GainedByTier[m.Tier];
                }

                List<int> first = campaigns.ConvertAll(c => c.FirstOfTier[m.Tier]);
                sb.Append("| `").Append(m.MaterialId).Append("` | ").Append(m.Tier).Append(" | ").Append(SimOptions.Format(mean / campaigns.Count)).Append(" | ")
                  .Append(Battles(Percentile(first, 10))).Append(" / ").Append(Battles(Percentile(first, 50))).Append(" / ").Append(Battles(Percentile(first, 90))).Append(" |\n");
            }

            double practice = 0.0;
            double material = 0.0;
            List<int> secondary = new List<int>();
            foreach (Campaign c in campaigns)
            {
                practice += c.PracticeXp;
                material += c.MaterialXp;
                secondary.Add(c.SecondaryLevel);
            }

            sb.Append("\nFocus skill XP up to level 20 (or the campaign's end): ").Append(Pct(100.0 * practice / Math.Max(1.0, practice + material)))
              .Append(" practice, ").Append(Pct(100.0 * material / Math.Max(1.0, practice + material))).Append(" materials (gate materials are spent, not fed, and add no XP).\n");
            sb.Append("Secondary skill (spillover only) at the campaign's end: level p10 / p50 / p90 = ").Append(LevelPercentile(secondary, 10)).Append(" / ")
              .Append(LevelPercentile(secondary, 50)).Append(" / ").Append(LevelPercentile(secondary, 90)).Append(".\n\n");

            sb.Append("## Verdict\n\n");
            sb.Append(misses.Count == 0 ? "Every median is inside its target.\n" : "Targets missed:\n\n");
            foreach (string miss in misses)
            {
                sb.Append("- ").Append(miss).Append('\n');
            }

            return sb.ToString();
        }

        private static int LevelPercentile(List<int> levels, double p)
        {
            List<int> sorted = new List<int>(levels);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
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
