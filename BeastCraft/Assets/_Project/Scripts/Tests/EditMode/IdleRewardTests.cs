using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Idle;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The idle (AFK) rewards: save schema 5 (<see cref="IdleState"/>, <see cref="SaveMigrations.AddIdle"/>),
    /// <see cref="IdleRewardCalculator"/> (the clock checks, the 8-hour cap, gold, XP under the falloff,
    /// the bench share and the level cap, materials without pity or first-clear credit, the look roll
    /// from the battle-drop pool, determinism), <see cref="CampaignRules.ProgressLevel"/>,
    /// <see cref="LootRoller.RollScaled"/> and <see cref="IdleRewardsValidator"/> against the authored
    /// <c>idle-rewards.json</c>.
    /// </summary>
    public class IdleRewardTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan M0 = TimeSpan.FromHours(3);

        private IdleContent _content;
        private SkillLibraryData _library;

        [SetUp]
        public void SetUp()
        {
            _library = SkillLibraryTests.LoadLibrary();
            _content = new IdleContent
            {
                Rewards = IdleRewardsBuilder.Build(LoadRewards()),
                DropTable = DropTableBuilder.Build(DropTableTests.LoadTables(), DropTableBuilder.TierLookup(_library.Materials)),
                Cosmetics = CosmeticLibrary.Build(CosmeticTests.LoadCosmetics()),
                Regions = RegionLibrary.Build(CampaignMapTests.LoadRegions())
            };
        }

        internal static IdleRewardsData LoadRewards()
        {
            return EncounterContentTests.Load<IdleRewardsData>(IdleRewardsData.ProjectRelativePath);
        }

        /// <summary>A save past r01's boss (seal: cap 22) with r02's first two stages cleared: progress level 15. Party b1-b3, bench b4.</summary>
        private static PlayerSave ProgressedSave()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.FindRegion("r01").StagesCleared = 3;
            save.Campaign.FindRegion("r01").BossCleared = true;
            save.Campaign.AddSeal("seal_r01");
            save.Campaign.Unlock("r02");
            save.Campaign.FindRegion("r02").StagesCleared = 2;
            save.Beasts.Add(OwnedBeast.Create("b1", "griffin", 15));
            save.Beasts.Add(OwnedBeast.Create("b2", "phoenix", 15));
            save.Beasts.Add(OwnedBeast.Create("b3", "golem", 14));
            save.Beasts.Add(OwnedBeast.Create("b4", "kirin", 5));
            return save;
        }

        private static readonly string[] Party = { "b1", "b2", "b3" };

        /// <summary>Starts the clock at (T0, M0).</summary>
        private IdleClaimResult Start(PlayerSave save)
        {
            IdleClaimResult started = IdleRewardCalculator.Claim(save, _content, T0, M0, Party);
            Assert.IsTrue(started.Success, started.Error);
            Assert.IsTrue(started.Started);
            return started;
        }

        private IdleClaimResult ClaimAfter(PlayerSave save, TimeSpan utc, TimeSpan mono)
        {
            IdleClaimResult result = IdleRewardCalculator.Claim(save, _content, T0 + utc, M0 + mono, Party);
            Assert.IsTrue(result.Success, result.Error);
            return result;
        }

        // ---- Save schema 5 ----

        [Test]
        public void Migration_V4ToV5_AddsAStoppedIdleClock_AndRoundTrips()
        {
            const string v4 = "{\"SchemaVersion\":4," +
                              "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"griffin\",\"Level\":14,\"Xp\":5,\"BankedXp\":0}," +
                              "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]," +
                              "\"Appearance\":{\"OptionEntries\":[],\"ColorEntries\":[]}}]," +
                              "\"Avatar\":{\"Level\":9,\"Xp\":3}," +
                              "\"Materials\":{\"Materials\":[{\"MaterialId\":\"essence_shard\",\"Quantity\":2}],\"ClearedCells\":[],\"Pity\":[]}," +
                              "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]," +
                              "\"Campaign\":{\"Seals\":[],\"Regions\":[{\"RegionId\":\"r01\",\"StagesCleared\":2,\"BossCleared\":false}],\"CurrentRegionId\":\"r01\"," +
                              "\"ActiveRun\":{\"RegionId\":\"\",\"Stage\":0,\"Seed\":0,\"Nodes\":[],\"CurrentNodeId\":-1,\"Cleared\":[],\"Attempts\":0,\"NodeAttempts\":0}}," +
                              "\"Gold\":77,\"Consumables\":[],\"Shops\":[],\"Cosmetics\":{\"Unlocked\":[]}," +
                              "\"AvatarAppearance\":{\"OptionEntries\":[],\"ColorEntries\":[]}}";
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), null, null, PlayerSave.CurrentSchemaVersion, null, null);

            SaveLoadResult migrated = serializer.Deserialize(v4);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(4, migrated.SourceVersion);
            Assert.IsEmpty(migrated.Issues, string.Join("\n", migrated.Issues));
            PlayerSave save = migrated.Save;
            Assert.AreEqual(5, save.SchemaVersion);
            Assert.IsNotNull(save.Idle);
            Assert.IsFalse(save.Idle.HasStarted, "a migrated save's clock starts at its first claim");
            Assert.AreEqual(0, save.Idle.ClaimIndex);
            Assert.AreEqual(77, save.Gold, "nothing else moves");
            Assert.AreEqual(14, save.FindBeast("b1").Progress.Level);
            Assert.AreEqual(2, save.Materials.GetCount("essence_shard"));

            IdleClaimResult first = IdleRewardCalculator.Claim(save, _content, T0, M0, new[] { "b1" });
            Assert.IsTrue(first.Started);
            Assert.AreEqual(77, save.Gold, "the first claim only starts the clock");
            IdleRewardCalculator.Claim(save, _content, T0.AddHours(2), M0 + TimeSpan.FromHours(2), new[] { "b1" });

            string v5 = serializer.Serialize(save);
            SaveLoadResult reloaded = serializer.Deserialize(v5);
            Assert.IsTrue(reloaded.Success, reloaded.Error);
            Assert.IsFalse(reloaded.Migrated);
            Assert.IsEmpty(reloaded.Issues, string.Join("\n", reloaded.Issues));
            Assert.AreEqual(v5, serializer.Serialize(reloaded.Save));
            Assert.AreEqual(save.Idle.LastClaimUtcTicks, reloaded.Save.Idle.LastClaimUtcTicks);
            Assert.AreEqual(save.Idle.LastClaimMonotonicMs, reloaded.Save.Idle.LastClaimMonotonicMs);
            Assert.AreEqual(save.Idle.IdleSeed, reloaded.Save.Idle.IdleSeed);
            Assert.AreEqual(1, reloaded.Save.Idle.ClaimIndex);
        }

        [Test]
        public void EnsureInitialized_RepairsANullIdleClock()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Idle = null;

            Assert.AreEqual(1, save.EnsureInitialized());
            Assert.IsNotNull(save.Idle);
            Assert.AreEqual(0, save.EnsureInitialized());
        }

        [Test]
        public void Validate_ReportsBadIdleClockValues()
        {
            PlayerSave save = PlayerSave.CreateNew();
            Assert.IsEmpty(SaveValidator.Validate(save, null));

            save.Idle.LastClaimUtcTicks = -5;
            save.Idle.LastClaimMonotonicMs = -2;
            save.Idle.ClaimIndex = -1;
            save.Idle.ClockClamps = -1;
            List<SaveIssue> issues = SaveValidator.Validate(save, null);
            foreach (string path in new[] { "Idle.LastClaimUtcTicks", "Idle.LastClaimMonotonicMs", "Idle.ClaimIndex", "Idle.ClockClamps" })
            {
                Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidValue && i.Path == path), path + ":\n" + string.Join("\n", issues));
            }

            save = PlayerSave.CreateNew();
            save.Idle.LastClaimUtcTicks = T0.Ticks;
            Assert.IsTrue(SaveValidator.Validate(save, null).Exists(i => i.Path == "Idle.IdleSeed"), "a started clock needs a seed");
        }

        // ---- Progress level ----

        [Test]
        public void ProgressLevel_IsTheHighestClearedLocation()
        {
            RegionLibrary regions = _content.Regions;
            PlayerSave save = PlayerSave.CreateNew();
            Assert.AreEqual(0, CampaignRules.ProgressLevel(save, regions), "nothing cleared");
            Assert.AreEqual(0, CampaignRules.ProgressLevel(save, null));
            Assert.AreEqual(0, CampaignRules.ProgressLevel(null, regions));

            save.Campaign.FindRegion("r01").StagesCleared = 1;
            Assert.AreEqual(3, CampaignRules.ProgressLevel(save, regions), "r01's first pass is level 3");

            save.Campaign.FindRegion("r01").BossCleared = true;
            Assert.AreEqual(10, CampaignRules.ProgressLevel(save, regions), "r01's lair: its max level");

            save.Campaign.Unlock("r02");
            save.Campaign.FindRegion("r02").StagesCleared = 2;
            Assert.AreEqual(15, CampaignRules.ProgressLevel(save, regions));

            CampaignResult started = CampaignRules.StartRun(save, regions, "r02", 2, 99);
            Assert.IsTrue(started.Success, started.Error);
            MapRun run = save.Campaign.ActiveRun;
            MapNode deep = run.Nodes.Find(n => n.IsBattle && n.Level > 15 && n.Type != MapNodeType.Gate);
            Assert.IsNotNull(deep);
            run.Cleared.Add(deep.NodeId);
            Assert.AreEqual(deep.Level, CampaignRules.ProgressLevel(save, regions), "a cleared battle location of the expedition counts");
        }

        // ---- The clock ----

        [Test]
        public void FirstClaim_StartsTheClock_AndPaysNothing()
        {
            PlayerSave save = ProgressedSave();

            IdleClaimResult started = Start(save);

            Assert.AreEqual(0, started.GoldGained);
            Assert.AreEqual(0.0, started.Hours);
            Assert.AreEqual(0, save.Gold);
            Assert.AreEqual(T0.Ticks, save.Idle.LastClaimUtcTicks);
            Assert.AreEqual((long)M0.TotalMilliseconds, save.Idle.LastClaimMonotonicMs);
            Assert.AreNotEqual(0, save.Idle.IdleSeed, "the seed is drawn from the first claim's clock");
            Assert.AreEqual(0, save.Idle.ClaimIndex);
        }

        [Test]
        public void Claim_PaysAtMostTheCap_AndDoesNotBankTheExcess()
        {
            PlayerSave save = ProgressedSave();
            Start(save);
            int cap = _content.Rewards.CapHours;
            Assert.AreEqual(8, cap, "lead / user decision: an 8-hour accumulation cap");
            IdleBand band = _content.Rewards.BandFor(15);

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(30), TimeSpan.FromHours(30));

            Assert.AreEqual(15, result.ProgressLevel);
            Assert.AreEqual(30.0, result.ElapsedHours, 1e-9);
            Assert.AreEqual(cap, result.Hours, 1e-9);
            Assert.IsTrue(result.Capped);
            Assert.IsFalse(result.ClockClamped);
            Assert.AreEqual(cap * band.GoldPerHour, result.GoldGained);
            Assert.AreEqual(cap * band.GoldPerHour, save.Gold);
            Assert.AreEqual(1, save.Idle.ClaimIndex);

            IdleClaimResult next = ClaimAfter(save, TimeSpan.FromHours(31), TimeSpan.FromHours(31));
            Assert.AreEqual(1.0, next.Hours, 1e-9, "the 22 hours past the cap were not banked");
            Assert.IsFalse(next.Capped);
            Assert.AreEqual(band.GoldPerHour, next.GoldGained);

            IdleClaimPreview preview = IdleRewardCalculator.Preview(save, _content, T0.AddHours(40), M0 + TimeSpan.FromHours(40));
            Assert.IsTrue(preview.Capped);
            Assert.AreEqual(cap, preview.Hours, 1e-9);
            Assert.AreEqual(cap * band.GoldPerHour, preview.Gold);
            Assert.AreEqual(2, save.Idle.ClaimIndex, "a preview changes nothing");
        }

        [TestCase(-60.0, false)]
        [TestCase(0.0, true)]
        [TestCase(60.0, true)]
        public void Capped_MeansTheCapIsReached_ForClaimAndPreviewAlike(double offsetSeconds, bool capped)
        {
            PlayerSave save = ProgressedSave();
            Start(save);
            int cap = _content.Rewards.CapHours;
            TimeSpan at = TimeSpan.FromHours(cap) + TimeSpan.FromSeconds(offsetSeconds);

            IdleClaimPreview preview = IdleRewardCalculator.Preview(save, _content, T0 + at, M0 + at);
            IdleClaimResult claim = ClaimAfter(save, at, at);

            Assert.AreEqual(capped, preview.Capped, "preview");
            Assert.AreEqual(capped, claim.Capped, "claim");
            double paid = Math.Min(at.TotalHours, cap);
            Assert.AreEqual(paid, preview.Hours, 1e-9);
            Assert.AreEqual(paid, claim.Hours, 1e-9);
        }

        [Test]
        public void ClockSetBack_IsClampedSilentlyToTheRealElapsedTime()
        {
            PlayerSave save = ProgressedSave();
            Start(save);
            IdleBand band = _content.Rewards.BandFor(15);

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(-5), TimeSpan.FromHours(2));

            Assert.IsTrue(result.Success);
            Assert.IsTrue(result.ClockClamped);
            Assert.AreEqual(2.0, result.Hours, 1e-9, "no penalty: the two hours that really passed are paid");
            Assert.AreEqual(2 * band.GoldPerHour, result.GoldGained);
            Assert.AreEqual(1, save.Idle.ClockClamps);
            Assert.AreEqual((T0 - TimeSpan.FromHours(5)).Ticks, save.Idle.LastClaimUtcTicks, "re-anchored at the clock as it reads now");

            IdleClaimResult restored = ClaimAfter(save, TimeSpan.FromHours(1), TimeSpan.FromHours(3));
            Assert.IsTrue(restored.ClockClamped, "the clock put right again jumps 6 hours in 1 real hour");
            Assert.AreEqual(1.0, restored.Hours, 1e-9, "no bonus either");
        }

        [Test]
        public void ClockSetForward_IsClampedSilentlyToTheRealElapsedTime()
        {
            PlayerSave save = ProgressedSave();
            Start(save);

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(48), TimeSpan.FromHours(1));

            Assert.IsTrue(result.ClockClamped);
            Assert.AreEqual(1.0, result.Hours, 1e-9);
            Assert.IsFalse(result.Capped);

            IdleClaimResult drift = ClaimAfter(save, TimeSpan.FromHours(50) + TimeSpan.FromSeconds(30), TimeSpan.FromHours(3));
            Assert.IsFalse(drift.ClockClamped, "a wall clock a few seconds ahead of the monotonic one is ordinary drift");
            Assert.AreEqual(2.0, drift.Hours, 1e-9, "still never more than the monotonic time");
        }

        [Test]
        public void Reboot_TrustsTheWallClock_ButNeverLessThanTheTimeSinceBoot()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Idle.IdleSeed = 7;
            save.Idle.LastClaimUtcTicks = T0.Ticks;
            save.Idle.LastClaimMonotonicMs = (long)TimeSpan.FromHours(50).TotalMilliseconds;

            long later = IdleRewardCalculator.Elapsed(save.Idle, T0.AddHours(3).Ticks, (long)TimeSpan.FromHours(1).TotalMilliseconds, out bool clamped);
            Assert.AreEqual(TimeSpan.FromHours(3).TotalMilliseconds, later);
            Assert.IsFalse(clamped);

            long rolledBack = IdleRewardCalculator.Elapsed(save.Idle, T0.AddHours(-2).Ticks, (long)TimeSpan.FromHours(1).TotalMilliseconds, out clamped);
            Assert.AreEqual(TimeSpan.FromHours(1).TotalMilliseconds, rolledBack, "at least the time since boot really passed");
            Assert.IsTrue(clamped);

            long noMonotonic = IdleRewardCalculator.Elapsed(save.Idle, T0.AddHours(-2).Ticks, -1, out clamped);
            Assert.AreEqual(0, noMonotonic, "no monotonic clock and a clock set back: nothing provable");
            Assert.IsTrue(clamped);
        }

        [Test]
        public void Claim_WithNothingCleared_PaysNothing_ButKeepsTheClock()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", "griffin", 1));
            Start(save);

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(5), TimeSpan.FromHours(5));

            Assert.AreEqual(0, result.ProgressLevel);
            Assert.AreEqual(5.0, result.Hours, 1e-9);
            Assert.AreEqual(0, result.GoldGained);
            Assert.AreEqual(0, save.FindBeast("b1").Progress.Xp);
            Assert.AreEqual((T0 + TimeSpan.FromHours(5)).Ticks, save.Idle.LastClaimUtcTicks);
        }

        [Test]
        public void Claim_IsRefusedWithoutASaveOrRates()
        {
            Assert.IsFalse(IdleRewardCalculator.Claim(null, _content, T0, M0, Party).Success);
            Assert.IsFalse(IdleRewardCalculator.Claim(PlayerSave.CreateNew(), null, T0, M0, Party).Success);
            PlayerSave save = PlayerSave.CreateNew();
            IdleClaimResult refused = IdleRewardCalculator.Claim(save, new IdleContent(), T0, M0, Party);
            Assert.IsFalse(refused.Success);
            Assert.IsNotNull(refused.Error);
            Assert.IsFalse(save.Idle.HasStarted, "a refused claim changes nothing");
        }

        // ---- XP ----

        [Test]
        public void Xp_FallsOffAboveTheProgressLevel_AndBenchCatchesUp()
        {
            PlayerSave save = ProgressedSave();
            save.FindBeast("b2").Progress.Level = 17;
            save.FindBeast("b3").Progress.Level = 20;
            Start(save);
            int xp = 4 * _content.Rewards.BandFor(15).XpPerHour;
            Assert.Greater(xp, 0);

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(4), TimeSpan.FromHours(4));

            CollectionAssert.AreEqual(new[] { "b1", "b2", "b3" }, result.PartyBeastIds);
            CollectionAssert.AreEqual(new[] { "b4" }, result.BenchBeastIds);
            Assert.AreEqual(xp, result.XpOffered["b1"], "at the progress level: all of it");
            Assert.AreEqual(xp * 25 / 100, result.XpOffered["b2"], "two levels above: the falloff's 25%");
            Assert.AreEqual(25, result.FalloffPercent["b2"]);
            Assert.AreEqual(0, result.XpOffered["b3"], "five above: nothing — idle cannot out-level the content");
            Assert.AreEqual(xp, save.FindBeast("b1").Progress.Xp);
            Assert.AreEqual(0, save.FindBeast("b3").Progress.Xp);
            Assert.AreEqual(xp * BeastProgression.BenchSharePermille(15, 5) / 1000, result.XpOffered["b4"], "the bench: the battles' catch-up share");
            Assert.AreEqual(1000, BeastProgression.BenchSharePermille(15, 5), "ten below: the full share");
        }

        [Test]
        public void Xp_AvatarEarnsThePartyRate_ThroughItsFalloff_WithNoCap()
        {
            PlayerSave save = ProgressedSave();
            save.Avatar.Level = 15;
            Start(save);
            int xp = 8 * _content.Rewards.BandFor(15).XpPerHour;

            IdleClaimResult atLevel = ClaimAfter(save, TimeSpan.FromHours(8), TimeSpan.FromHours(8));

            Assert.AreEqual(xp, atLevel.AvatarXpGained, "at the progress level: the party's rate");
            Assert.AreEqual(100, atLevel.AvatarFalloffPercent);
            Assert.AreEqual(xp, save.Avatar.Xp);

            save.Avatar.Level = 16;
            save.Avatar.Xp = 0;
            IdleClaimResult above = ClaimAfter(save, TimeSpan.FromHours(16), TimeSpan.FromHours(16));
            Assert.AreEqual(xp * 60 / 100, above.AvatarXpGained, "one level above: the falloff's 60%");
            Assert.AreEqual(60, above.AvatarFalloffPercent);

            save.Avatar.Level = 30;
            save.Avatar.Xp = 0;
            IdleClaimResult far = ClaimAfter(save, TimeSpan.FromHours(24), TimeSpan.FromHours(24));
            Assert.AreEqual(0, far.AvatarXpGained, "far above the content: nothing");

            PlayerSave capped = ProgressedSave();
            int cap = CampaignRules.BeastCap(capped, _content.Regions);
            capped.Campaign.Unlock("r03");
            capped.Campaign.FindRegion("r03").StagesCleared = 3;
            Assert.Greater(CampaignRules.ProgressLevel(capped, _content.Regions), cap + 2);
            capped.Avatar.Level = cap;
            Start(capped);
            IdleClaimResult noCap = ClaimAfter(capped, TimeSpan.FromHours(8), TimeSpan.FromHours(8));
            Assert.Greater(noCap.AvatarXpGained, 0);
            for (int i = 2; i <= 60; i++)
            {
                ClaimAfter(capped, TimeSpan.FromHours(8 * i), TimeSpan.FromHours(8 * i));
            }

            Assert.Greater(capped.Avatar.Level, cap, "the avatar has no level cap (lead decision)");
        }

        [Test]
        public void Xp_BenchAtTheProgressLevel_EarnsTheBaseShare()
        {
            PlayerSave save = ProgressedSave();
            save.FindBeast("b4").Progress.Level = 15;
            Start(save);
            int xp = 8 * _content.Rewards.BandFor(15).XpPerHour;

            IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(8), TimeSpan.FromHours(8));

            Assert.AreEqual(xp * BeastProgression.BenchShareBasePermille / 1000, result.XpOffered["b4"]);
        }

        [Test]
        public void Xp_RespectsTheLevelCapAndItsBank()
        {
            PlayerSave save = ProgressedSave();
            int cap = CampaignRules.BeastCap(save, _content.Regions);
            Assert.AreEqual(22, cap, "seal_r01");
            save.Campaign.FindRegion("r02").BossCleared = true;
            Assert.AreEqual(20, CampaignRules.ProgressLevel(save, _content.Regions), "r02's lair cleared but its seal not yet granted");
            save.FindBeast("b1").Progress.Level = 20;
            save.FindBeast("b2").Progress.Level = cap - 1;
            BeastProgress b2 = save.FindBeast("b2").Progress;
            b2.Xp = BeastProgression.XpToNextLevel(b2.Level) - 1;
            Start(save);

            for (int i = 1; i <= 400; i++)
            {
                IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(8 * i), TimeSpan.FromHours(8 * i));
                Assert.AreEqual(cap, result.BeastLevelCap);
                foreach (OwnedBeast beast in save.Beasts)
                {
                    Assert.LessOrEqual(beast.Progress.Level, cap, beast.BeastId + " never passes the cap");
                    Assert.LessOrEqual(LevelCap.BankedLevels(beast.Progress), LevelCap.BankLevelLimit, beast.BeastId + "'s bank holds at most its limit");
                }
            }

            Assert.AreEqual(cap, b2.Level, "a beast reaches the cap, then only banks");
            Assert.AreEqual(LevelCap.BankLevelLimit, LevelCap.BankedLevels(b2), "400 full claims fill the bank to its limit, no further");

            b2.Level = cap;
            b2.Xp = BeastProgression.XpToNextLevel(cap) - 1;
            b2.BankedXp = BeastProgression.TotalXpToReach(cap + LevelCap.BankLevelLimit) - BeastProgression.TotalXpToReach(cap) - b2.Xp;
            save.Campaign.FindRegion("r02").BossCleared = false;
            save.Campaign.FindRegion("r02").StagesCleared = 3;
            save.Campaign.Unlock("r03");
            save.Campaign.FindRegion("r03").StagesCleared = 3;
            Assert.Greater(CampaignRules.ProgressLevel(save, _content.Regions), cap, "the progress level runs past the cap (a seal still missing)");
            IdleClaimResult full = ClaimAfter(save, TimeSpan.FromHours(8 * 401), TimeSpan.FromHours(8 * 401));
            Assert.Greater(full.XpOffered["b2"], 0);
            Assert.AreEqual(full.XpOffered["b2"], full.XpLostAtCap["b2"], "a full bank: \"banked at cap\", nothing more granted");
            Assert.AreEqual(cap, b2.Level);
        }

        // ---- Materials, gold, looks ----

        [Test]
        public void Materials_NeverCreditAFirstClearOrPity()
        {
            PlayerSave save = ProgressedSave();
            save.Materials.SetPity("squad", 1, 3);
            Start(save);
            int gained = 0;

            for (int i = 1; i <= 40; i++)
            {
                IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(8 * i), TimeSpan.FromHours(8 * i));
                Assert.IsFalse(result.Loot.FirstClear);
                Assert.IsEmpty(result.Loot.PityForcedTiers);
                Assert.AreEqual(8, result.MaterialRolls);
                foreach (MaterialStack stack in result.Loot.Drops)
                {
                    gained += stack.Quantity;
                }
            }

            Assert.Greater(gained, 0, "320 idle rolls of the squad cell drop something");
            Assert.IsEmpty(save.Materials.ClearedCells, "no cell is ever marked cleared");
            Assert.AreEqual(1, save.Materials.Pity.Count);
            Assert.AreEqual(3, save.Materials.GetPity("squad", 1), "pity counters are never touched");
            Assert.AreEqual(gained, save.Materials.GetCount("essence_shard") + save.Materials.GetCount("essence_crystal") + save.Materials.GetCount("essence_core"));
        }

        [Test]
        public void RollScaled_ScalesTheChances_AndKeepsDrawCountsFixed()
        {
            DropTable table = _content.DropTable;
            MaterialInventory none = new MaterialInventory();
            Assert.IsEmpty(LootRoller.RollScaled(table, "squad", 15, 0.0, 500, none, new Random(1)).Drops);
            Assert.IsEmpty(LootRoller.RollScaled(table, "nope", 15, 1.0, 500, none, new Random(1)).Drops);
            Assert.IsEmpty(LootRoller.RollScaled(null, "squad", 15, 1.0, 500, none, new Random(1)).Drops);

            MaterialInventory full = new MaterialInventory();
            MaterialInventory quarter = new MaterialInventory();
            int fullShards = LootRoller.RollScaled(table, "squad", 15, 1.0, 4000, full, new Random(3)).QuantityOf("essence_shard");
            int quarterShards = LootRoller.RollScaled(table, "squad", 15, 0.25, 4000, quarter, new Random(3)).QuantityOf("essence_shard");
            Assert.AreEqual(4000 * 0.20, fullShards, 4000 * 0.03, "x1: the clear's 20%");
            Assert.AreEqual(4000 * 0.05, quarterShards, 4000 * 0.02, "x0.25: 5%");
            Assert.AreEqual(fullShards, full.GetCount("essence_shard"));
            Assert.IsEmpty(full.ClearedCells);
            Assert.IsEmpty(full.Pity);

            Random a = new Random(9);
            Random b = new Random(9);
            LootRoller.RollScaled(table, "squad", 15, 0.0, 10, new MaterialInventory(), a);
            LootRoller.RollScaled(table, "squad", 15, 1.0, 10, new MaterialInventory(), b);
            Assert.AreEqual(a.Next(), b.Next(), "both draws are taken on a miss too");
        }

        [Test]
        public void Claim_IsDeterministic_ForTheSameSeedAndClock()
        {
            PlayerSave first = ProgressedSave();
            PlayerSave second = ProgressedSave();
            Start(first);
            Start(second);
            Assert.AreEqual(first.Idle.IdleSeed, second.Idle.IdleSeed);
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), null);

            for (int i = 1; i <= 12; i++)
            {
                TimeSpan at = TimeSpan.FromHours(7.5 * i);
                IdleClaimResult a = ClaimAfter(first, at, at);
                IdleClaimResult b = ClaimAfter(second, at, at);
                Assert.AreEqual(a.GoldGained, b.GoldGained);
                Assert.AreEqual(a.MaterialRolls, b.MaterialRolls);
                Assert.AreEqual(a.Loot.Drops.Count, b.Loot.Drops.Count);
                CollectionAssert.AreEquivalent(a.XpOffered, b.XpOffered);
            }

            Assert.AreEqual(serializer.Serialize(first), serializer.Serialize(second));

            PlayerSave reseeded = ProgressedSave();
            reseeded.Idle.IdleSeed = first.Idle.IdleSeed + 1;
            Start(reseeded);
            Assert.AreEqual(first.Idle.IdleSeed + 1, reseeded.Idle.IdleSeed, "a seed already set is kept");
        }

        [Test]
        public void LookRoll_DrawsFromTheBattleDropPool()
        {
            IdleRewardsData data = LoadRewards();
            foreach (IdleBandData band in data.Bands)
            {
                Assert.Greater(band.CosmeticChancePer10k, 0, "every band rolls for a look");
                Assert.LessOrEqual(band.CosmeticChancePer10k, 10, "a very low chance per claim (at most 0.1%)");
                band.CosmeticChancePer10k = IdleRewardsData.MaxCosmeticChancePer10k;
            }

            _content.Rewards = IdleRewardsBuilder.Build(data);
            List<CosmeticOption> pool = _content.Cosmetics.Pool(CosmeticLibrary.SourceDrop, CosmeticLibrary.RegionOfLevel(15));
            Assert.IsNotEmpty(pool);
            int hits = 0;
            for (int seed = 1; seed <= 1500; seed++)
            {
                PlayerSave save = ProgressedSave();
                save.Idle.IdleSeed = seed;
                Start(save);
                IdleClaimResult result = ClaimAfter(save, TimeSpan.FromHours(8), TimeSpan.FromHours(8));
                if (result.CosmeticDropped == null)
                {
                    continue;
                }

                hits++;
                CosmeticOption look = _content.Cosmetics.GetOption(result.CosmeticDropped);
                Assert.AreEqual(CosmeticLibrary.SourceDrop, look.Source, "the same pool as battle drops: no idle-exclusive looks");
                Assert.IsTrue(pool.Exists(o => o.Key == look.Key), "the progress level's region's drop pool");
                Assert.IsTrue(save.Cosmetics.Has(look.Key));
            }

            Assert.Greater(hits, 3, "1% per full claim over 1500 claims");
            Assert.Less(hits, 40);
        }

        [Test]
        public void PickDrop_IsWhatABattleDropDrawsOnAHit()
        {
            CosmeticLibrary library = _content.Cosmetics;
            PlayerSave save = PlayerSave.CreateNew();
            for (int seed = 0; seed < 200; seed++)
            {
                Random battle = new Random(seed);
                CosmeticOption dropped = CosmeticRules.RollDrop(_content.DropTable, library, save, "elite", 35, battle);
                if (dropped == null)
                {
                    continue;
                }

                Random replay = new Random(seed);
                replay.Next(1000);
                Assert.AreEqual(dropped.Key, CosmeticRules.PickDrop(library, save, 35, replay).Key);
            }

            Assert.IsNull(CosmeticRules.PickDrop(null, save, 35, new Random(1)));
        }

        // ---- The authored rates and the validator ----

        [Test]
        public void AuthoredRates_PassValidation_AndRiseWithProgress()
        {
            IdleRewardsData data = LoadRewards();
            List<string> errors = IdleRewardsValidator.Validate(data, DropTableTests.LoadTables());

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(8, data.CapHours);
            Assert.AreEqual(1, data.Bands[0].MinProgressLevel);
            Assert.AreEqual(BeastProgression.MaxLevel, data.Bands[data.Bands.Length - 1].MaxProgressLevel);
            for (int i = 1; i < data.Bands.Length; i++)
            {
                Assert.Greater(data.Bands[i].GoldPerHour, data.Bands[i - 1].GoldPerHour, "gold steps up with progress");
                Assert.GreaterOrEqual(data.Bands[i].XpPerHour, data.Bands[i - 1].XpPerHour);
            }

            IdleRewards rewards = IdleRewardsBuilder.Build(data);
            Assert.IsNull(rewards.BandFor(0));
            Assert.AreEqual(1, rewards.BandFor(1).MinProgressLevel);
            Assert.AreSame(rewards.Bands[rewards.Bands.Count - 1], rewards.BandFor(150));
        }

        [Test]
        public void Validator_ReportsEveryProblem()
        {
            Assert.IsNotEmpty(IdleRewardsValidator.Validate(null));
            AssertError(d => d.SchemaVersion = 9, "SchemaVersion");
            AssertError(d => d.CapHours = 0, "CapHours");
            AssertError(d => d.CapHours = 25, "CapHours");
            AssertError(d => d.Shape = "", "Shape is empty");
            AssertError(d => d.Shape = "swarm", "not a drop-table shape");
            AssertError(d => d.MaterialRollsPerHour = -1f, "MaterialRollsPerHour");
            AssertError(d => d.Bands = new IdleBandData[0], "Bands is empty");
            AssertError(d => d.Bands[0].MinProgressLevel = 2, "expected 1");
            AssertError(d => d.Bands[1].MinProgressLevel = d.Bands[0].MaxProgressLevel, "contiguous");
            AssertError(d => d.Bands[d.Bands.Length - 1].MaxProgressLevel = 99, "must cover progress levels");
            AssertError(d => d.Bands[0].MaxProgressLevel = 0, "ends at 0");
            AssertError(d => d.Bands[0].GoldPerHour = -1, "negative rate");
            AssertError(d => d.Bands[3].XpPerHour = 0, "never fall");
            AssertError(d => d.Bands[3].GoldPerHour = 0, "never fall");
            AssertError(d => d.Bands[0].MaterialChanceMultiplier = 1.5f, "MaterialChanceMultiplier");
            AssertError(d => d.Bands[0].MaterialChanceMultiplier = float.NaN, "MaterialChanceMultiplier");
            AssertError(d => d.Bands[0].CosmeticChancePer10k = 101, "at most 1%");
            AssertError(d => d.Bands[0].CosmeticChancePer10k = -1, "CosmeticChancePer10k");
            AssertError(d => d.Bands[2] = null, "is null");
        }

        private static void AssertError(Action<IdleRewardsData> breakIt, string fragment)
        {
            IdleRewardsData data = LoadRewards();
            breakIt(data);
            List<string> errors = IdleRewardsValidator.Validate(data, DropTableTests.LoadTables());
            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }
    }
}
