using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Idle;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The idle (AFK) rewards inside <c>--mode campaign</c> (<see cref="CampaignPacingSimulator"/>):
    /// the player plays <see cref="Settings.BattlesPerDay"/> battles a day and is away
    /// <see cref="Settings.IdleHoursPerDay"/> hours a day, claiming <see cref="Settings.ClaimsPerDay"/>
    /// times a day, evenly spaced: a claim every <c>BattlesPerDay / ClaimsPerDay</c> battles, each
    /// crediting <c>IdleHoursPerDay / ClaimsPerDay</c> hours of real idle time (both clocks advance
    /// together; the game's <see cref="IdleRewardCalculator.Claim"/> pays at most the 8-hour cap). The
    /// claim is the game's own code on the model's save — the progress level, the party (the
    /// fielded beasts) and bench, the level cap and bank, the falloff, the drop tables and the
    /// cosmetic pool — and it feeds back into the campaign (levels, gold, materials).
    /// <para>
    /// Measured against the lead / user ceilings over a whole campaign: idle at most
    /// <see cref="GoldShareMax"/> of all gold (clears, gear sales and idle), at most
    /// <see cref="MaterialShareMax"/> of all skill materials (by XP value, clears and idle) and at most
    /// <see cref="XpShareMax"/> of all beast XP credited (battles, camps and idle). Deterministic: every
    /// idle roll comes from the save's <see cref="IdleState.IdleSeed"/>, set to
    /// <c>DeriveSeed(campaign seed, </c><see cref="Stream"/><c>)</c>, so idle never moves the campaign's
    /// own random draws.
    /// </para>
    /// </summary>
    public sealed class CampaignIdleModel
    {
        /// <summary>The <c>LootRoller.DeriveSeed(campaign seed, …)</c> stream the save's idle seed is drawn on.</summary>
        public const int Stream = 0x49444C;

        /// <summary>Hours a day the player is away (two 8-hour claims: the full cap twice a day).</summary>
        public const double DefaultIdleHoursPerDay = 16.0;

        /// <summary>Claims a day (morning and evening).</summary>
        public const int DefaultClaimsPerDay = 2;

        /// <summary>Battles fought a day (the campaign's ~545 battles over about three weeks).</summary>
        public const double DefaultBattlesPerDay = 25.0;

        /// <summary>The ceiling on idle's share of all gold (lead / user decision).</summary>
        public const double GoldShareMax = 0.15;

        /// <summary>The ceiling on idle's share of all skill materials, by XP value (lead / user decision).</summary>
        public const double MaterialShareMax = 0.15;

        /// <summary>The ceiling on idle's share of all beast XP (lead / user decision).</summary>
        public const double XpShareMax = 0.10;

        /// <summary>The idle clocks' start (any fixed time: the model reads only differences).</summary>
        public static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Settings _settings;
        private readonly Result _result;
        private readonly Dictionary<string, int> _materialValue;
        private double _hours;
        private int _claims;

        public CampaignIdleModel(Settings settings, PlayerSave save, int campaignSeed, int regions, List<SkillMaterialSO> materials)
        {
            _settings = settings;
            _result = new Result(regions);
            _materialValue = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (SkillMaterialSO material in materials)
            {
                _materialValue[material.MaterialId] = material.XpValue;
            }

            if (settings.Enabled)
            {
                save.Idle.IdleSeed = LootRoller.DeriveSeed(campaignSeed, Stream);
                IdleRewardCalculator.Claim(save, settings.Content, Epoch, TimeSpan.Zero, null);
            }
        }

        /// <summary>The idle cadence and content (null content or 0 hours = no idle rewards).</summary>
        public sealed class Settings
        {
            public IdleContent Content;
            public double IdleHoursPerDay = DefaultIdleHoursPerDay;
            public int ClaimsPerDay = DefaultClaimsPerDay;
            public double BattlesPerDay = DefaultBattlesPerDay;

            public bool Enabled
            {
                get { return Content != null && IdleHoursPerDay > 0.0 && ClaimsPerDay > 0; }
            }

            /// <summary>Battles between two claims.</summary>
            public double BattlesPerClaim
            {
                get { return BattlesPerDay / ClaimsPerDay; }
            }

            /// <summary>Real idle hours each claim covers (before the cap).</summary>
            public double HoursPerClaim
            {
                get { return IdleHoursPerDay / ClaimsPerDay; }
            }

            /// <summary>Loads and validates <c>idle-rewards.json</c> against the drop tables; null with <paramref name="errors"/> filled on failure.</summary>
            public static IdleRewards Load(DropTableData tables, List<string> errors)
            {
                string path = RosterLoader.ResolveFile(null, IdleRewardsData.ProjectRelativePath);
                if (path == null || !File.Exists(path))
                {
                    errors.Add("Could not find " + IdleRewardsData.ProjectRelativePath + ".");
                    return null;
                }

                IdleRewardsData data;
                try
                {
                    data = JsonSerializer.Deserialize<IdleRewardsData>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
                }
                catch (Exception exception)
                {
                    errors.Add("Could not read " + path + ": " + exception.Message);
                    return null;
                }

                errors.AddRange(IdleRewardsValidator.Validate(data, tables));
                return errors.Count > 0 ? null : IdleRewardsBuilder.Build(data);
            }
        }

        /// <summary>What one campaign's idle rewards measured (and the active income they are compared with).</summary>
        public sealed class Result
        {
            public Result(int regions)
            {
                IdleGoldByRegion = new double[regions];
                IdleXpByRegion = new double[regions];
                TotalXpByRegion = new double[regions];
                IdleMaterialValueByRegion = new double[regions];
                ActiveMaterialValueByRegion = new double[regions];
                ClaimsByRegion = new int[regions];
            }

            public double[] IdleGoldByRegion;
            public double[] IdleXpByRegion;

            /// <summary>All beast XP credited in the region (battles, camps and idle), from the save's totals.</summary>
            public double[] TotalXpByRegion;

            public double[] IdleMaterialValueByRegion;
            public double[] ActiveMaterialValueByRegion;
            public int[] ClaimsByRegion;
            public int[] IdleMaterialsByTier = new int[4];
            public int[] ActiveMaterialsByTier = new int[4];
            public int CappedClaims;
            public int Looks;
            public double Hours;

            /// <summary>Avatar XP from idle claims, and the avatar's total XP at the last boss.</summary>
            public long IdleAvatarXp;
            public long AvatarTotalXp;
        }

        public Result Stats
        {
            get { return _result; }
        }

        /// <summary>
        /// Claims every idle payout due by <paramref name="battles"/> battles fought. Milestone looks the
        /// idle XP reaches are counted in <paramref name="economy"/>'s look sources; the idle look drop
        /// only here. Returns whether any was claimed.
        /// </summary>
        public bool ClaimDue(PlayerSave save, List<OwnedBeast> party, int battles, int regionIndex, Func<string, int> tierOf, CampaignEconomyModel economy)
        {
            if (!_settings.Enabled)
            {
                return false;
            }

            bool claimed = false;
            while (battles >= Math.Ceiling((_claims + 1) * _settings.BattlesPerClaim))
            {
                _claims++;
                _hours += _settings.HoursPerClaim;
                TimeSpan at = TimeSpan.FromHours(_hours);
                long before = TotalXp(save);
                IdleClaimResult claim = IdleRewardCalculator.Claim(save, _settings.Content, Epoch + at, at, party.ConvertAll(b => b.BeastId));
                if (!claim.Success)
                {
                    throw new InvalidOperationException("Idle model: " + claim.Error);
                }

                _result.IdleXpByRegion[regionIndex] += TotalXp(save) - before;
                _result.IdleGoldByRegion[regionIndex] += claim.GoldGained;
                _result.ClaimsByRegion[regionIndex]++;
                _result.CappedClaims += claim.Capped ? 1 : 0;
                _result.Hours += claim.Hours;
                _result.IdleAvatarXp += claim.AvatarXpGained;
                _result.Looks += claim.CosmeticDropped == null ? 0 : 1;
                economy.OnMilestoneLooks(claim.CosmeticsUnlocked.FindAll(key => key != claim.CosmeticDropped));
                foreach (MaterialStack stack in claim.Loot.Drops)
                {
                    _result.IdleMaterialValueByRegion[regionIndex] += Value(stack);
                    AddTier(_result.IdleMaterialsByTier, tierOf(stack.MaterialId), stack.Quantity);
                }

                claimed = true;
            }

            return claimed;
        }

        /// <summary>A clear's material drops (the active side of the materials share).</summary>
        public void OnLoot(LootResult loot, int regionIndex, Func<string, int> tierOf)
        {
            foreach (MaterialStack stack in loot.Drops)
            {
                _result.ActiveMaterialValueByRegion[regionIndex] += Value(stack);
                AddTier(_result.ActiveMaterialsByTier, tierOf(stack.MaterialId), stack.Quantity);
            }
        }

        /// <summary>At a region's boss: every beast XP credited in the region, from the save's totals.</summary>
        public void OnRegionEnd(PlayerSave save, int regionIndex, ref long xpAtLastRegionEnd)
        {
            long now = TotalXp(save);
            _result.TotalXpByRegion[regionIndex] = now - xpAtLastRegionEnd;
            xpAtLastRegionEnd = now;
            _result.AvatarTotalXp = AvatarProgression.TotalXpToReach(save.Avatar.Level) + Math.Max(0, save.Avatar.Xp);
        }

        /// <summary>Every beast's XP from level 1: levels, XP toward the next and the bank.</summary>
        public static long TotalXp(PlayerSave save)
        {
            long total = 0;
            foreach (OwnedBeast beast in save.Beasts)
            {
                total += BeastProgression.TotalXpToReach(beast.Progress.Level) + Math.Max(0, beast.Progress.Xp) + Math.Max(0, beast.Progress.BankedXp);
            }

            return total;
        }

        private double Value(MaterialStack stack)
        {
            return (_materialValue.TryGetValue(stack.MaterialId, out int value) ? value : 0) * (double)stack.Quantity;
        }

        private static void AddTier(int[] tiers, int tier, int quantity)
        {
            if (tier >= 0 && tier < tiers.Length)
            {
                tiers[tier] += quantity;
            }
        }

        /// <summary>The report's idle section and its gates.</summary>
        public static void Report(StringBuilder sb, RegionLibrary regions, Settings settings, List<CampaignPacingSimulator.Result> runs, Func<List<double>, double, double> p,
                                  List<string> misses)
        {
            sb.Append("## Idle rewards\n\n");
            if (!settings.Enabled)
            {
                sb.Append("Off (`--idle-hours-per-day 0`).\n\n");
                return;
            }

            IdleRewards rewards = settings.Content.Rewards;
            sb.Append("The game's `IdleRewardCalculator` (`idle-rewards.json`) on the model's save: the player fights ").Append(SimOptions.Format(settings.BattlesPerDay))
              .Append(" battles a day and is away ").Append(SimOptions.Format(settings.IdleHoursPerDay)).Append(" hours a day, claiming ").Append(settings.ClaimsPerDay)
              .Append(" times a day (a claim every ").Append(SimOptions.Format(settings.BattlesPerClaim)).Append(" battles, each ")
              .Append(SimOptions.Format(settings.HoursPerClaim)).Append(" hours idle, paid up to the ").Append(rewards.CapHours).Append("-hour cap;\n");
            sb.Append("`--battles-per-day`, `--idle-hours-per-day`, `--idle-claims-per-day`). A claim pays at the progress level (the highest cleared location):\n");
            sb.Append("gold; XP to the fielded beasts (the party) and the bench share to the rest, through the level-gap falloff and under the level cap;\n");
            sb.Append(SimOptions.Format(rewards.MaterialRollsPerHour)).Append(" roll(s) an hour of the `").Append(rewards.Shape)
              .Append("` drop cell with scaled chances (no pity, no first-clear credit); a rare look from the battle-drop pool.\n");
            sb.Append("The avatar earns the party's rate through its own falloff (no cap). Idle income feeds the campaign (levels, the purse, the focus skill's materials).\n\n");

            sb.Append("| Progress levels | Gold / hour | XP / hour | Material chance | Look chance / full claim |\n| --- | ---: | ---: | ---: | ---: |\n");
            foreach (IdleBand band in rewards.Bands)
            {
                sb.Append("| ").Append(band.MinProgressLevel).Append('-').Append(band.MaxProgressLevel).Append(" | ").Append(band.GoldPerHour).Append(" | ").Append(band.XpPerHour)
                  .Append(" | x").Append(band.MaterialChanceMultiplier.ToString("0.00", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append((band.CosmeticChancePer10k / 100.0).ToString("0.00", CultureInfo.InvariantCulture)).Append("% |\n");
            }

            sb.Append("\nIdle's share of each region's income (p50): gold of the gold from clears, materials by XP value of the materials from clears, XP of all\n");
            sb.Append("beast XP credited (battles, camps and idle).\n\n");
            sb.Append("| Region | Claims (mean) | Idle gold p50 | Gold share | Material share | XP share |\n| --- | ---: | ---: | ---: | ---: | ---: |\n");
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                int region = r;
                List<double> gold = runs.ConvertAll(run => run.Idle.IdleGoldByRegion[region]);
                List<double> goldShare = runs.ConvertAll(run => Share(run.Idle.IdleGoldByRegion[region], run.Econ.GoldByRegion[region] + run.Idle.IdleGoldByRegion[region]));
                List<double> materialShare = runs.ConvertAll(run => Share(run.Idle.IdleMaterialValueByRegion[region],
                                                                          run.Idle.ActiveMaterialValueByRegion[region] + run.Idle.IdleMaterialValueByRegion[region]));
                List<double> xpShare = runs.ConvertAll(run => Share(run.Idle.IdleXpByRegion[region], run.Idle.TotalXpByRegion[region]));
                sb.Append("| ").Append(regions.Regions[r].RegionId).Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)run.Idle.ClaimsByRegion[region]))))
                  .Append(" | ").Append(Int(p(gold, 50))).Append(" | ").Append(Pct(p(goldShare, 50))).Append(" | ").Append(Pct(p(materialShare, 50))).Append(" | ")
                  .Append(Pct(p(xpShare, 50))).Append(" |\n");
            }

            List<double> goldTotal = runs.ConvertAll(run => Sum(run.Idle.IdleGoldByRegion));
            List<double> goldCampaign = runs.ConvertAll(run => Share(Sum(run.Idle.IdleGoldByRegion), Sum(run.Econ.GoldByRegion) + run.Econ.GoldFromSales + Sum(run.Idle.IdleGoldByRegion)));
            List<double> materialCampaign = runs.ConvertAll(run => Share(Sum(run.Idle.IdleMaterialValueByRegion),
                                                                         Sum(run.Idle.IdleMaterialValueByRegion) + Sum(run.Idle.ActiveMaterialValueByRegion)));
            List<double> xpCampaign = runs.ConvertAll(run => Share(Sum(run.Idle.IdleXpByRegion), Sum(run.Idle.TotalXpByRegion)));
            double g50 = p(goldCampaign, 50);
            double m50 = p(materialCampaign, 50);
            double x50 = p(xpCampaign, 50);
            bool goldOk = g50 <= GoldShareMax;
            bool materialOk = m50 <= MaterialShareMax;
            bool xpOk = x50 <= XpShareMax;
            if (!goldOk)
            {
                misses.Add("Idle gold is " + Pct(g50) + " of all gold (p50), above " + Pct(GoldShareMax) + ".");
            }

            if (!materialOk)
            {
                misses.Add("Idle materials are " + Pct(m50) + " of all materials by value (p50), above " + Pct(MaterialShareMax) + ".");
            }

            if (!xpOk)
            {
                misses.Add("Idle XP is " + Pct(x50) + " of all beast XP (p50), above " + Pct(XpShareMax) + ".");
            }

            sb.Append("\n| Over the campaign | Ceiling | p10 | p50 | p90 | Verdict |\n| --- | --- | ---: | ---: | ---: | --- |\n");
            Row(sb, "Idle gold / all gold (clears, gear sales, idle)", GoldShareMax, goldCampaign, p, goldOk);
            Row(sb, "Idle materials / all materials (by XP value)", MaterialShareMax, materialCampaign, p, materialOk);
            Row(sb, "Idle beast XP / all beast XP", XpShareMax, xpCampaign, p, xpOk);

            double claims = Mean(runs.ConvertAll(run => (double)Sum(run.Idle.ClaimsByRegion)));
            double capped = Mean(runs.ConvertAll(run => (double)run.Idle.CappedClaims));
            double[] idleTiers = new double[4];
            double[] activeTiers = new double[4];
            foreach (CampaignPacingSimulator.Result run in runs)
            {
                for (int t = 1; t < 4; t++)
                {
                    idleTiers[t] += run.Idle.IdleMaterialsByTier[t];
                    activeTiers[t] += run.Idle.ActiveMaterialsByTier[t];
                }
            }

            sb.Append("\nPer campaign (means): ").Append(SimOptions.Format(claims)).Append(" claims (").Append(SimOptions.Format(capped)).Append(" reached the cap), ")
              .Append(SimOptions.Format(Mean(runs.ConvertAll(run => run.Idle.Hours)))).Append(" idle hours paid, ").Append(Int(p(goldTotal, 50))).Append(" gold (p50), materials by tier ")
              .Append(SimOptions.Format(idleTiers[1] / runs.Count)).Append(" / ").Append(SimOptions.Format(idleTiers[2] / runs.Count)).Append(" / ")
              .Append(SimOptions.Format(idleTiers[3] / runs.Count)).Append(" (clears: ").Append(SimOptions.Format(activeTiers[1] / runs.Count)).Append(" / ")
              .Append(SimOptions.Format(activeTiers[2] / runs.Count)).Append(" / ").Append(SimOptions.Format(activeTiers[3] / runs.Count)).Append("), ")
              .Append(Mean(runs.ConvertAll(run => (double)run.Idle.Looks)).ToString("0.00", CultureInfo.InvariantCulture)).Append(" idle look drops; idle avatar XP ")
              .Append(Pct(p(runs.ConvertAll(run => Share(run.Idle.IdleAvatarXp, run.Idle.AvatarTotalXp)), 50))).Append(" of the avatar's XP (p50; not gated).\n\n");
        }

        private static void Row(StringBuilder sb, string label, double ceiling, List<double> values, Func<List<double>, double, double> p, bool ok)
        {
            sb.Append("| ").Append(label).Append(" | at most ").Append(Pct(ceiling)).Append(" | ").Append(Pct(p(values, 10))).Append(" | ").Append(Pct(p(values, 50)))
              .Append(" | ").Append(Pct(p(values, 90))).Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
        }

        private static double Share(double part, double whole)
        {
            return whole <= 0.0 ? 0.0 : part / whole;
        }

        private static double Mean(List<double> values)
        {
            double sum = 0.0;
            foreach (double v in values)
            {
                sum += v;
            }

            return values.Count == 0 ? 0.0 : sum / values.Count;
        }

        private static double Sum(double[] values)
        {
            double sum = 0.0;
            foreach (double v in values)
            {
                sum += v;
            }

            return sum;
        }

        private static int Sum(int[] values)
        {
            int sum = 0;
            foreach (int v in values)
            {
                sum += v;
            }

            return sum;
        }

        private static string Int(double value)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Pct(double share)
        {
            return (100.0 * share).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
