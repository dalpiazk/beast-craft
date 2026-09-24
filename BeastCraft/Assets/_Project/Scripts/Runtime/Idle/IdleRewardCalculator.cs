using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Idle
{
    /// <summary>
    /// What <see cref="IdleRewardCalculator"/> reads: the idle rates, the drop tables (the material
    /// cell and the battle-drop look chance's pool), the cosmetic library (null = no look rolls) and
    /// the region library (the progress level and the beast level cap). Plain holder.
    /// </summary>
    public sealed class IdleContent
    {
        /// <summary>The idle rates (<c>idle-rewards.json</c>). Required.</summary>
        public IdleRewards Rewards { get; set; }

        /// <summary>The drop tables (<c>drop-tables.json</c>); null = no idle materials.</summary>
        public DropTable DropTable { get; set; }

        /// <summary>The cosmetic library; null = no idle look roll and no milestone check.</summary>
        public CosmeticLibrary Cosmetics { get; set; }

        /// <summary>The region library (<c>regions.json</c>). Required: without it nothing is cleared, so nothing is paid.</summary>
        public RegionLibrary Regions { get; set; }
    }

    /// <summary>
    /// The idle (AFK) rewards: time away from the game pays gold, beast XP, skill materials and a
    /// very rare look, at rates set by how far the player has got (<see cref="CampaignRules.ProgressLevel"/>:
    /// the level of the highest cleared map location). See <c>docs/design/progression-and-saves.md</c>,
    /// "Idle rewards", and the lead / user decisions there.
    /// <para>
    /// <strong>Time.</strong> There is no trusted server clock (the game is offline-first). A claim
    /// compares the wall clock (UTC) and a monotonic clock (time since boot, which the player cannot
    /// set) with their readings at the last claim (<see cref="IdleState"/>) and credits
    /// <see cref="Elapsed"/>: the wall-clock time, never more than the monotonic clock says really
    /// passed. A clock set back or forward is silently clamped to the real elapsed time (lead / user
    /// decision: no message, no penalty, no bonus). Then at most <see cref="IdleRewards.CapHours"/>
    /// hours are paid; time beyond the cap is not banked (the UI shows "capped",
    /// <see cref="IdleClaimPreview.Capped"/>).
    /// </para>
    /// <para>
    /// <strong>What an hour pays</strong> (the band of the progress level; nothing below level 1, i.e.
    /// before the first clear):
    /// </para>
    /// <list type="bullet">
    /// <item><b>Gold</b>: <c>floor(hours × GoldPerHour)</c>, into the <see cref="Wallet"/>. No
    /// first-clear bonus, no reward modifiers.</item>
    /// <item><b>Materials</b>: <c>hours × MaterialRollsPerHour</c> rolls (stochastically rounded) of
    /// the drop tables' cell for (<see cref="IdleRewards.Shape"/>, progress level), every chance
    /// scaled by the band's <c>MaterialChanceMultiplier</c> (<see cref="LootRoller.RollScaled"/>):
    /// <strong>no pity and no first-clear credit</strong> — the pity counters and cleared cells are
    /// never touched.</item>
    /// <item><b>Beast XP</b>: <c>floor(hours × XpPerHour)</c> to every beast of the party, cut by the
    /// level-gap falloff on its level against the progress level (<see cref="LevelGapXp"/>), so idle
    /// never out-levels the content; every other beast (the bench) earns the bench share of it
    /// (<see cref="BeastProgression.BenchSharePermille"/>, the same catch-up rule as battles), then
    /// the falloff. All of it is added under the beast level cap
    /// (<see cref="BeastProgression.AddXp(BeastProgress, int, int)"/>): at the cap it banks, at most
    /// <see cref="LevelCap.BankLevelLimit"/> levels' worth, and more is not granted
    /// (<see cref="IdleClaimResult.XpLostAtCap"/>; the UI shows "banked at cap"). The avatar earns no
    /// idle XP.</item>
    /// <item><b>A look</b>: one roll per claim with the band's <c>CosmeticChancePer10k</c> scaled by
    /// <c>hours / CapHours</c> (so claiming often gains nothing), from the same pool as battle drops
    /// (<see cref="CosmeticRules.PickDrop"/>: the progress level's region's <c>drop</c> looks not yet
    /// owned). Milestone looks the idle XP reaches are unlocked too, as after a battle.</item>
    /// </list>
    /// <para>
    /// <strong>Deterministic.</strong> Claim <c>n</c>'s rolls come from
    /// <c>LootRoller.DeriveSeed(IdleState.IdleSeed, n)</c>, materials on its stream
    /// <see cref="MaterialStream"/> and the look on <see cref="CosmeticStream"/>; the same save, clock
    /// readings and party always claim the same rewards. Non-throwing; a refused claim changes nothing.
    /// </para>
    /// </summary>
    public static class IdleRewardCalculator
    {
        /// <summary>The <c>LootRoller.DeriveSeed(claim seed, …)</c> stream of a claim's material rolls.</summary>
        public const int MaterialStream = 0;

        /// <summary>The stream of a claim's look roll.</summary>
        public const int CosmeticStream = PostBattleAward.CosmeticStream;

        /// <summary>The stream an unset <see cref="IdleState.IdleSeed"/> is drawn on, from the first claim's clock.</summary>
        public const int SeedStream = 0x49444C45;

        /// <summary>A wall clock ahead of the monotonic clock by more than this (ms) counts as a clamp (NTP corrections stay under it).</summary>
        public const long ClockSlackMs = 120000;

        private const double MsPerHour = 3600000.0;

        /// <summary>
        /// Claims the idle rewards at <paramref name="nowUtc"/> (the wall clock; a local time is
        /// converted, an unspecified one read as UTC) and <paramref name="nowMonotonic"/> (time since the
        /// device booted — e.g. Android's <c>elapsedRealtime</c>, iOS's continuous time; negative = not
        /// available, the wall clock alone is used). The first claim of a save only starts the clock.
        /// <paramref name="partyBeastIds"/> is the current party (unknown or repeated ids are ignored);
        /// every other beast is on the bench. See the class remarks.
        /// </summary>
        public static IdleClaimResult Claim(PlayerSave save, IdleContent content, DateTime nowUtc, TimeSpan nowMonotonic, IEnumerable<string> partyBeastIds)
        {
            if (save == null || content == null || content.Rewards == null)
            {
                return IdleClaimResult.Refused("No save, or no idle content (the rates are required).");
            }

            save.EnsureInitialized();
            IdleState state = save.Idle;
            long nowTicks = UtcTicks(nowUtc);
            long nowMonoMs = nowMonotonic < TimeSpan.Zero ? -1 : (long)nowMonotonic.TotalMilliseconds;
            IdleClaimResult result = new IdleClaimResult();
            result.ProgressLevel = CampaignRules.ProgressLevel(save, content.Regions);
            result.BeastLevelCap = CampaignRules.BeastCap(save, content.Regions);

            if (!state.HasStarted)
            {
                if (state.IdleSeed == 0)
                {
                    state.IdleSeed = SeedFrom(nowTicks);
                }

                Anchor(state, nowTicks, nowMonoMs);
                result.Started = true;
                return result;
            }

            long elapsedMs = Elapsed(state, nowTicks, nowMonoMs, out bool clamped);
            result.ClockClamped = clamped;
            result.ElapsedHours = elapsedMs / MsPerHour;
            result.Capped = result.ElapsedHours > content.Rewards.CapHours;
            result.Hours = Math.Min(result.ElapsedHours, content.Rewards.CapHours);

            IdleBand band = content.Rewards.BandFor(result.ProgressLevel);
            int claimSeed = LootRoller.DeriveSeed(state.IdleSeed, state.ClaimIndex);
            if (band != null && result.Hours > 0.0)
            {
                Pay(save, content, band, result, claimSeed, partyBeastIds);
            }

            Anchor(state, nowTicks, nowMonoMs);
            state.ClaimIndex++;
            state.ClockClamps += clamped ? 1 : 0;
            return result;
        }

        /// <summary>
        /// What a claim now would credit, without claiming: the hours (capped), whether the cap is
        /// reached, the progress level and the gold and pre-falloff XP per party beast it would pay.
        /// Never changes the save.
        /// </summary>
        public static IdleClaimPreview Preview(PlayerSave save, IdleContent content, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            IdleClaimPreview preview = new IdleClaimPreview();
            if (save == null || content == null || content.Rewards == null)
            {
                return preview;
            }

            preview.CapHours = content.Rewards.CapHours;
            preview.ProgressLevel = CampaignRules.ProgressLevel(save, content.Regions);
            if (save.Idle == null || !save.Idle.HasStarted)
            {
                return preview;
            }

            long nowMonoMs = nowMonotonic < TimeSpan.Zero ? -1 : (long)nowMonotonic.TotalMilliseconds;
            double elapsed = Elapsed(save.Idle, UtcTicks(nowUtc), nowMonoMs, out bool _) / MsPerHour;
            preview.Capped = elapsed >= content.Rewards.CapHours;
            preview.Hours = Math.Min(elapsed, content.Rewards.CapHours);
            IdleBand band = content.Rewards.BandFor(preview.ProgressLevel);
            if (band != null)
            {
                preview.Gold = (int)Math.Floor(preview.Hours * band.GoldPerHour);
                preview.XpPerPartyBeast = (int)Math.Floor(preview.Hours * band.XpPerHour);
            }

            return preview;
        }

        /// <summary>
        /// The real idle time since the last claim, in milliseconds, from the wall clock
        /// (<paramref name="nowTicks"/>, UTC ticks) and the monotonic clock (<paramref name="nowMonoMs"/>,
        /// −1 = not available):
        /// <list type="bullet">
        /// <item>Both clocks from the same boot (the monotonic reading has not gone back): the wall-clock
        /// time, but never more than the monotonic time and, when the wall clock went back, the
        /// monotonic time. <paramref name="clamped"/> when the wall clock was replaced (went back, or
        /// ran ahead by more than <see cref="ClockSlackMs"/>).</item>
        /// <item>The device rebooted in between (the monotonic reading went back): at least the time
        /// since boot really passed, so the wall-clock time, or the time since boot when that is more
        /// (<paramref name="clamped"/>). A clock set forward across a reboot cannot be told apart
        /// offline; the cap bounds it.</item>
        /// <item>No monotonic reading (now or at the last claim): the wall-clock time, or 0 when it went
        /// back (<paramref name="clamped"/>).</item>
        /// </list>
        /// Never negative.
        /// </summary>
        public static long Elapsed(IdleState state, long nowTicks, long nowMonoMs, out bool clamped)
        {
            clamped = false;
            if (state == null || !state.HasStarted)
            {
                return 0;
            }

            long deltaUtcMs = (nowTicks - state.LastClaimUtcTicks) / TimeSpan.TicksPerMillisecond;
            if (nowMonoMs < 0 || state.LastClaimMonotonicMs < 0)
            {
                clamped = deltaUtcMs < 0;
                return Math.Max(0, deltaUtcMs);
            }

            long deltaMonoMs = nowMonoMs - state.LastClaimMonotonicMs;
            if (deltaMonoMs >= 0)
            {
                if (deltaUtcMs < 0)
                {
                    clamped = true;
                    return deltaMonoMs;
                }

                clamped = deltaUtcMs > deltaMonoMs + ClockSlackMs;
                return Math.Min(deltaUtcMs, deltaMonoMs);
            }

            if (deltaUtcMs < nowMonoMs)
            {
                clamped = true;
                return nowMonoMs;
            }

            return deltaUtcMs;
        }

        private static void Pay(PlayerSave save, IdleContent content, IdleBand band, IdleClaimResult result, int claimSeed, IEnumerable<string> partyBeastIds)
        {
            IdleRewards rewards = content.Rewards;
            double hours = result.Hours;
            int level = result.ProgressLevel;

            result.GoldGained = Wallet.Add(save, (int)Math.Min(int.MaxValue, Math.Floor(hours * band.GoldPerHour)));

            if (content.DropTable != null && band.MaterialChanceMultiplier > 0.0 && rewards.MaterialRollsPerHour > 0.0)
            {
                Random rng = new Random(LootRoller.DeriveSeed(claimSeed, MaterialStream));
                double expected = hours * rewards.MaterialRollsPerHour;
                int rolls = (int)Math.Floor(expected);
                rolls += rng.NextDouble() < expected - rolls ? 1 : 0;
                result.MaterialRolls = rolls;
                result.Loot = LootRoller.RollScaled(content.DropTable, rewards.Shape, level, band.MaterialChanceMultiplier, rolls, save.Materials, rng);
            }

            AwardXp(save, band, result, partyBeastIds);

            if (content.Cosmetics != null)
            {
                long threshold = (long)Math.Round(band.CosmeticChancePer10k * 100.0 * hours / rewards.CapHours);
                Random rng = new Random(LootRoller.DeriveSeed(claimSeed, CosmeticStream));
                if (threshold > 0 && rng.Next(1000000) < threshold)
                {
                    CosmeticOption look = CosmeticRules.PickDrop(content.Cosmetics, save, level, rng);
                    if (look != null && CosmeticRules.Unlock(save, content.Cosmetics, look.Key))
                    {
                        result.CosmeticDropped = look.Key;
                        result.CosmeticsUnlocked.Add(look.Key);
                    }
                }

                result.CosmeticsUnlocked.AddRange(CosmeticRules.UnlockMilestones(save, content.Cosmetics));
            }
        }

        private static void AwardXp(PlayerSave save, IdleBand band, IdleClaimResult result, IEnumerable<string> partyBeastIds)
        {
            HashSet<string> party = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in partyBeastIds ?? new string[0])
            {
                if (save.FindBeast(id) != null)
                {
                    party.Add(id);
                }
            }

            int xp = (int)Math.Min(int.MaxValue, Math.Floor(result.Hours * band.XpPerHour));
            int level = result.ProgressLevel;
            foreach (OwnedBeast beast in save.Beasts)
            {
                if (string.IsNullOrEmpty(beast.BeastId) || result.XpOffered.ContainsKey(beast.BeastId))
                {
                    continue;
                }

                BeastProgress progress = beast.Progress;
                int own = progress.Level;
                bool benched = !party.Contains(beast.BeastId);
                int share = benched ? (int)((long)xp * BeastProgression.BenchSharePermille(level, own) / 1000) : xp;
                int gap = LevelGapXp.Gap(own, level);
                int offered = LevelGapXp.Apply(share, gap);

                long before = Total(progress);
                int bankBefore = Math.Max(0, progress.BankedXp);
                int gained = BeastProgression.AddXp(progress, offered, result.BeastLevelCap);
                long accepted = Total(progress) - before;

                result.XpOffered[beast.BeastId] = offered;
                result.FalloffPercent[beast.BeastId] = LevelGapXp.Percent(gap);
                result.LevelsGained[beast.BeastId] = gained;
                result.XpBanked[beast.BeastId] = Math.Max(0, progress.BankedXp - bankBefore);
                result.XpLostAtCap[beast.BeastId] = (int)Math.Max(0, offered - accepted);
                if (benched)
                {
                    result.BenchBeastIds.Add(beast.BeastId);
                }
                else
                {
                    result.PartyBeastIds.Add(beast.BeastId);
                }
            }
        }

        private static long Total(BeastProgress progress)
        {
            return (long)BeastProgression.TotalXpToReach(progress.Level) + Math.Max(0, progress.Xp) + Math.Max(0, progress.BankedXp);
        }

        private static void Anchor(IdleState state, long nowTicks, long nowMonoMs)
        {
            state.LastClaimUtcTicks = nowTicks;
            state.LastClaimMonotonicMs = nowMonoMs;
        }

        private static long UtcTicks(DateTime now)
        {
            DateTime utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now;
            return Math.Max(1, utc.Ticks);
        }

        private static int SeedFrom(long ticks)
        {
            int seed = LootRoller.DeriveSeed((int)(ticks ^ (ticks >> 32)), SeedStream);
            return seed == 0 ? 1 : seed;
        }
    }

    /// <summary>What <see cref="IdleRewardCalculator.Claim"/> did.</summary>
    public sealed class IdleClaimResult
    {
        /// <summary>False when refused (no save or no rates): nothing changed.</summary>
        public bool Success { get; private set; } = true;

        /// <summary>Why the claim was refused, or null.</summary>
        public string Error { get; private set; }

        /// <summary>This was the save's first claim: it only started the clock and paid nothing.</summary>
        public bool Started { get; internal set; }

        /// <summary>The real idle time since the last claim (after the clock checks), in hours.</summary>
        public double ElapsedHours { get; internal set; }

        /// <summary>The hours paid: <see cref="ElapsedHours"/>, at most the cap.</summary>
        public double Hours { get; internal set; }

        /// <summary>Whether the idle time reached past the cap (the excess was not banked).</summary>
        public bool Capped { get; internal set; }

        /// <summary>Whether the wall clock was replaced by the real elapsed time (silent; see <see cref="IdleState.ClockClamps"/>).</summary>
        public bool ClockClamped { get; internal set; }

        /// <summary>The progress level the rates were read at (0 = nothing cleared, nothing paid).</summary>
        public int ProgressLevel { get; internal set; }

        /// <summary>The beast level cap the XP was added under.</summary>
        public int BeastLevelCap { get; internal set; }

        /// <summary>Gold added to the wallet.</summary>
        public int GoldGained { get; internal set; }

        /// <summary>How many material rolls were made.</summary>
        public int MaterialRolls { get; internal set; }

        /// <summary>The materials granted (never a first clear, never pity), or an empty result.</summary>
        public LootResult Loot { get; internal set; } = new LootResult();

        /// <summary>The XP each beast was offered (after the bench share and the falloff), by beast id.</summary>
        public Dictionary<string, int> XpOffered { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The falloff percent each beast's XP was cut to, by beast id.</summary>
        public Dictionary<string, int> FalloffPercent { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Levels gained, by beast id.</summary>
        public Dictionary<string, int> LevelsGained { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>XP that went into the level-cap bank, by beast id ("banked at cap").</summary>
        public Dictionary<string, int> XpBanked { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>XP offered but not granted because the bank was full, by beast id.</summary>
        public Dictionary<string, int> XpLostAtCap { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The party beasts paid, in save order.</summary>
        public List<string> PartyBeastIds { get; } = new List<string>();

        /// <summary>The bench beasts paid, in save order.</summary>
        public List<string> BenchBeastIds { get; } = new List<string>();

        /// <summary>The look the idle roll unlocked (<c>"categoryId/optionId"</c>), or null.</summary>
        public string CosmeticDropped { get; internal set; }

        /// <summary>Every look unlocked by the claim: the idle drop, then milestones.</summary>
        public List<string> CosmeticsUnlocked { get; } = new List<string>();

        internal static IdleClaimResult Refused(string error)
        {
            return new IdleClaimResult { Success = false, Error = error };
        }
    }

    /// <summary>What <see cref="IdleRewardCalculator.Preview"/> reports (for the idle screen).</summary>
    public sealed class IdleClaimPreview
    {
        /// <summary>The hours a claim now would pay (at most <see cref="CapHours"/>).</summary>
        public double Hours { get; internal set; }

        /// <summary>The accumulation cap in hours.</summary>
        public int CapHours { get; internal set; }

        /// <summary>Whether the cap is reached (the UI shows "capped": more time adds nothing).</summary>
        public bool Capped { get; internal set; }

        /// <summary>The progress level the rates are read at.</summary>
        public int ProgressLevel { get; internal set; }

        /// <summary>The gold a claim now would pay.</summary>
        public int Gold { get; internal set; }

        /// <summary>The XP a claim now would offer each party beast before the falloff and the cap.</summary>
        public int XpPerPartyBeast { get; internal set; }
    }
}
