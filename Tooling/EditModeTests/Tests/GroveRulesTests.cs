using System;
using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Grove;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Grove (docs/design/grove.md): habitat and decor unlocks (<see cref="GroveRules.RefreshUnlocks"/>,
    /// shrine and start/starter sources), decor placement (slot bounds, scope, single placement),
    /// affinity (<see cref="GroveRules.Feed"/>/<see cref="GroveRules.Play"/>: daily cooldown through
    /// <c>OfflineClock</c>, no decay, tier-up rewards) and gifts (<see cref="GroveRules.RefreshGifts"/>:
    /// the cap, the seeded roll and its pity guarantee). No combat power anywhere. Also the authored
    /// <c>grove-library.json</c> against <see cref="GroveLibraryValidator"/>.
    /// </summary>
    public class GroveRulesTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan M0 = TimeSpan.FromHours(3);

        internal static GroveLibraryData LoadGrove()
        {
            return EncounterContentTests.Load<GroveLibraryData>(GroveLibraryData.ProjectRelativePath);
        }

        private static PlayerSave SaveWithBeast(string beastId = "b1", string speciesId = "phoenix")
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create(beastId, speciesId, 10));
            return save;
        }

        private static GroveLibraryData SyntheticData()
        {
            return new GroveLibraryData
            {
                SchemaVersion = 1,
                FeedXp = 10,
                PlayXp = 15,
                DailyCooldownHours = 20,
                GiftHoursByTier = new[] { 6, 5, 4, 3, 2 },
                Habitats = new[]
                {
                    new HabitatData { HabitatId = "glade", DisplayName = "Glade", UnlockSource = "start", UnlockId = "", SlotCount = 2, ArtKey = "a" },
                    new HabitatData { HabitatId = "pool", DisplayName = "Pool", UnlockSource = "shrine", UnlockId = "grove_habitat_pool", SlotCount = 4, ArtKey = "a" }
                },
                Decor = new[]
                {
                    new DecorData { DecorId = "stone", DisplayName = "Stone", HabitatScope = "", Source = "starter", UnlockId = "", ArtKey = "a", SortOrder = 1 },
                    new DecorData { DecorId = "pool_only", DisplayName = "Pool Deco", HabitatScope = "pool", Source = "starter", UnlockId = "", ArtKey = "a", SortOrder = 2 },
                    new DecorData { DecorId = "shrine_deco", DisplayName = "Shrine Deco", HabitatScope = "", Source = "shrine", UnlockId = "grove_decor_shrine_deco", ArtKey = "a", SortOrder = 3 },
                    new DecorData { DecorId = "tier_prize", DisplayName = "Tier Prize", HabitatScope = "", Source = "affinity", UnlockId = "", ArtKey = "a", SortOrder = 4 }
                },
                AffinityTiers = new[]
                {
                    new AffinityTierData { SpeciesId = "phoenix", Tier = 1, XpThreshold = 10, RewardKind = "none", RewardId = "" },
                    new AffinityTierData { SpeciesId = "phoenix", Tier = 2, XpThreshold = 25, RewardKind = "decor", RewardId = "tier_prize" },
                    new AffinityTierData { SpeciesId = "phoenix", Tier = 3, XpThreshold = 50, RewardKind = "lore", RewardId = "lore_a" },
                    new AffinityTierData { SpeciesId = "phoenix", Tier = 4, XpThreshold = 80, RewardKind = "none", RewardId = "" },
                    new AffinityTierData { SpeciesId = "phoenix", Tier = 5, XpThreshold = 120, RewardKind = "idle_anim", RewardId = "" }
                },
                GiftTables = new[]
                {
                    new GiftTableData
                    {
                        SpeciesId = "default",
                        PityAt = 3,
                        Entries = new[]
                        {
                            new GiftEntryData { ItemKind = "decor", ItemId = "stone", Weight = 95, IsCommon = true },
                            new GiftEntryData { ItemKind = "lore", ItemId = "lore_a", Weight = 5, IsCommon = false }
                        }
                    }
                },
                Lore = new[] { new GroveLoreEntryData { LoreId = "lore_a", Title = "A Tale", Text = "Something warm." } }
            };
        }

        // ---- Content ----

        [Test]
        public void AuthoredLibrary_IsValid()
        {
            GroveLibraryData data = LoadGrove();
            List<string> species = new List<string> { "phoenix", "leviathan", "golem", "griffin", "thunderbird", "frost_wyrm", "treant", "tarasque", "kirin", "basilisk" };
            List<string> errors = GroveLibraryValidator.Validate(data, DiscoveryTests.ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath), species, null);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(3, data.Habitats.Length);
            Assert.AreEqual(50, data.AffinityTiers.Length);
        }

        [Test]
        public void EveryShrineGroveUnlockId_IsClaimedByExactlyOneHabitatOrDecorOrSeed()
        {
            DiscoveryLibraryData discovery = DiscoveryTests.ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath);
            GroveLibraryData grove = LoadGrove();
            Garden.GardenLibraryData garden = GardenRulesTests.LoadGarden();

            Dictionary<string, int> claims = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ShrineData shrine in discovery.Shrines)
            {
                claims[shrine.GroveUnlockId] = 0;
            }

            foreach (HabitatData habitat in grove.Habitats)
            {
                if (habitat.UnlockSource == "shrine")
                {
                    claims[habitat.UnlockId] = claims[habitat.UnlockId] + 1;
                }
            }

            foreach (DecorData decor in grove.Decor)
            {
                if (decor.Source == "shrine")
                {
                    claims[decor.UnlockId] = claims[decor.UnlockId] + 1;
                }
            }

            foreach (Garden.SeedSpeciesData seed in garden.Seeds)
            {
                if (seed.Source == "shrine")
                {
                    claims[seed.UnlockId] = claims[seed.UnlockId] + 1;
                }
            }

            foreach (KeyValuePair<string, int> claim in claims)
            {
                Assert.AreEqual(1, claim.Value, "shrine unlock '" + claim.Key + "' must be claimed exactly once");
            }
        }

        // ---- Unlocks ----

        [Test]
        public void RefreshUnlocks_GrantsStartHabitatsAndStarterDecor_ButNotShrineOnesWithoutTheUnlock()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            int granted = GroveRules.RefreshUnlocks(save, library);

            CollectionAssert.Contains(save.Grove.HabitatsUnlocked, "glade");
            CollectionAssert.DoesNotContain(save.Grove.HabitatsUnlocked, "pool");
            CollectionAssert.Contains(save.Grove.UnlockedDecorIds, "stone");
            CollectionAssert.DoesNotContain(save.Grove.UnlockedDecorIds, "shrine_deco");
            Assert.Greater(granted, 0);
        }

        [Test]
        public void RefreshUnlocks_GrantsShrineHabitat_OnceItsGroveUnlockIdIsHeld()
        {
            PlayerSave save = SaveWithBeast();
            save.Discovery.GroveUnlockIds.Add("grove_habitat_pool");
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            GroveRules.RefreshUnlocks(save, library);

            CollectionAssert.Contains(save.Grove.HabitatsUnlocked, "pool");
        }

        [Test]
        public void RefreshUnlocks_IsIdempotent()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());
            GroveRules.RefreshUnlocks(save, library);
            int before = save.Grove.HabitatsUnlocked.Count + save.Grove.UnlockedDecorIds.Count;

            int grantedAgain = GroveRules.RefreshUnlocks(save, library);

            Assert.AreEqual(0, grantedAgain);
            Assert.AreEqual(before, save.Grove.HabitatsUnlocked.Count + save.Grove.UnlockedDecorIds.Count);
        }

        // ---- Decor placement ----

        [Test]
        public void PlaceDecor_RefusesWhenHabitatLocked_DecorNotOwned_WrongScope_AlreadyPlaced_OrSlotsFull()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());
            GroveRules.RefreshUnlocks(save, library);

            Assert.IsFalse(GroveRules.PlaceDecor(save, library, "pool", "stone", 0, 0, 0).Success, "habitat not unlocked");
            Assert.IsFalse(GroveRules.PlaceDecor(save, library, "glade", "shrine_deco", 0, 0, 0).Success, "decor not owned");

            save.Grove.UnlockedDecorIds.Add("pool_only");
            save.Discovery.GroveUnlockIds.Add("grove_habitat_pool");
            GroveRules.RefreshUnlocks(save, library);
            Assert.IsFalse(GroveRules.PlaceDecor(save, library, "glade", "pool_only", 0, 0, 0).Success, "wrong habitat scope");

            Assert.IsTrue(GroveRules.PlaceDecor(save, library, "glade", "stone", 1, 2, 0).Success);
            Assert.IsFalse(GroveRules.PlaceDecor(save, library, "glade", "stone", 0, 0, 0).Success, "already placed");

            // glade has 2 slots: stone fills 1; fill the second, then refuse a third.
            save.Grove.UnlockedDecorIds.Add("tier_prize");
            Assert.IsTrue(GroveRules.PlaceDecor(save, library, "glade", "tier_prize", 0, 0, 0).Success);
            save.Grove.UnlockedDecorIds.Add("pool_only");
            Assert.IsFalse(GroveRules.PlaceDecor(save, library, "glade", "pool_only", 0, 0, 0).Success, "no scope match but also full");
        }

        [Test]
        public void RemoveDecor_FreesTheSlot_ButKeepsItOwned()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());
            GroveRules.RefreshUnlocks(save, library);
            GroveRules.PlaceDecor(save, library, "glade", "stone", 0, 0, 0);

            Assert.IsTrue(GroveRules.RemoveDecor(save, "stone"));
            Assert.IsFalse(save.Grove.IsPlaced("stone"));
            Assert.IsTrue(GroveRules.IsDecorUnlocked(save, "stone"));
        }

        // ---- Affinity ----

        [Test]
        public void Feed_GrantsXp_ThenRefusesUntilTheCooldownPasses()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            GroveActionResult first = GroveRules.Feed(save, library, "b1", T0, M0);
            Assert.IsTrue(first.Success);
            Assert.AreEqual(10, first.XpGained);

            GroveActionResult again = GroveRules.Feed(save, library, "b1", T0 + TimeSpan.FromHours(1), M0 + TimeSpan.FromHours(1));
            Assert.IsFalse(again.Success);

            GroveActionResult tomorrow = GroveRules.Feed(save, library, "b1", T0 + TimeSpan.FromHours(21), M0 + TimeSpan.FromHours(21));
            Assert.IsTrue(tomorrow.Success);
        }

        [Test]
        public void Feed_AndPlay_HaveIndependentCooldowns()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            Assert.IsTrue(GroveRules.Feed(save, library, "b1", T0, M0).Success);
            Assert.IsTrue(GroveRules.Play(save, library, "b1", T0, M0).Success);
        }

        [Test]
        public void TierUp_AppliesTheTiersReward_DecorThenLoreThenIdleAnimAtTier5()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            GroveActionResult r1 = GroveRules.Feed(save, library, "b1", T0, M0); // 10 xp, tier 1 (threshold 10)
            Assert.IsTrue(r1.TierUp);
            Assert.AreEqual(1, r1.Tier);

            GroveActionResult r2 = GroveRules.Play(save, library, "b1", T0, M0); // +15 = 25 xp, tier 2 (threshold 25): decor
            Assert.IsTrue(r2.TierUp);
            Assert.AreEqual(2, r2.Tier);
            Assert.AreEqual("decor", r2.RewardKind);
            CollectionAssert.Contains(save.Grove.UnlockedDecorIds, "tier_prize");

            GroveActionResult r3 = GroveRules.Feed(save, library, "b1", T0 + TimeSpan.FromHours(21), M0 + TimeSpan.FromHours(21)); // +10 = 35, still tier 2
            Assert.IsFalse(r3.TierUp);

            GroveActionResult r4 = GroveRules.Play(save, library, "b1", T0 + TimeSpan.FromHours(21), M0 + TimeSpan.FromHours(21)); // +15 = 50, tier 3: lore
            Assert.IsTrue(r4.TierUp);
            Assert.AreEqual("lore", r4.RewardKind);
            CollectionAssert.Contains(save.Grove.LoreIds, "lore_a");

            BeastAffinityState state = save.Grove.FindAffinity("b1");
            Assert.AreEqual(3, state.Tier);
            Assert.AreEqual(0, state.Xp - 50); // sanity: exactly 50 so far
        }

        [Test]
        public void NoDecay_XpAndTierNeverDropOverTime()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());
            GroveRules.Feed(save, library, "b1", T0, M0);
            int xpAfterFeed = save.Grove.FindAffinity("b1").Xp;

            GroveRules.Feed(save, library, "b1", T0 + TimeSpan.FromDays(90), M0 + TimeSpan.FromDays(90));

            Assert.GreaterOrEqual(save.Grove.FindAffinity("b1").Xp, xpAfterFeed);
        }

        // ---- Gifts ----

        [Test]
        public void RefreshGifts_FirstCall_OnlyStartsTheClock_GrantsNothing()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());

            int generated = GroveRules.RefreshGifts(save, library, "b1", T0, M0);

            Assert.AreEqual(0, generated);
            Assert.AreEqual(0, save.Grove.FindAffinity("b1").PendingGifts.Count);
        }

        [Test]
        public void RefreshGifts_RollsOneGiftPerElapsedInterval_CappedAtThree_AndNeverLosesTime()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData()); // tier 0 -> GiftHoursForTier(1) clamps to index 0 = 6h
            GroveRules.RefreshGifts(save, library, "b1", T0, M0); // starts the clock

            // 20 hours later: floor(20/6) = 3 gifts, exactly the cap.
            int generated = GroveRules.RefreshGifts(save, library, "b1", T0 + TimeSpan.FromHours(20), M0 + TimeSpan.FromHours(20));
            Assert.AreEqual(3, generated);
            Assert.AreEqual(GroveRules.MaxPendingGifts, save.Grove.FindAffinity("b1").PendingGifts.Count);

            // more time passes while capped: nothing new generates (no free slot).
            int whileCapped = GroveRules.RefreshGifts(save, library, "b1", T0 + TimeSpan.FromHours(40), M0 + TimeSpan.FromHours(40));
            Assert.AreEqual(0, whileCapped);

            // collect one: the banked time (well over an interval) immediately fills the freed slot.
            GiftInstance collected = GroveRules.CollectGift(save, "b1");
            Assert.IsNotNull(collected);
            int afterFreeing = GroveRules.RefreshGifts(save, library, "b1", T0 + TimeSpan.FromHours(40), M0 + TimeSpan.FromHours(40));
            Assert.AreEqual(1, afterFreeing, "banked elapsed time is never lost, just gated by the cap");
            Assert.AreEqual(GroveRules.MaxPendingGifts, save.Grove.FindAffinity("b1").PendingGifts.Count);
        }

        [Test]
        public void RollGift_Deterministic_SameSeedAndIndexAlwaysRollsTheSameEntry()
        {
            GiftTableData table = SyntheticData().GiftTables[0];
            BeastAffinityState a = new BeastAffinityState { BeastId = "x" };
            BeastAffinityState b = new BeastAffinityState { BeastId = "x" };

            GiftInstance first = GroveRules.RollGift(table, a);
            GiftInstance second = GroveRules.RollGift(table, b);

            Assert.AreEqual(first.ItemKind, second.ItemKind);
            Assert.AreEqual(first.ItemId, second.ItemId);
            Assert.AreEqual(a.GiftSeed, b.GiftSeed);
        }

        [Test]
        public void RollGift_PityGuaranteesANonCommonEntry_WithinPityAtRolls()
        {
            GiftTableData table = SyntheticData().GiftTables[0]; // PityAt 3, "stone" common weight 95, "lore_a" non-common weight 5
            BeastAffinityState state = new BeastAffinityState { BeastId = "pity-test" };

            bool sawNonCommon = false;
            for (int i = 0; i < 3; i++)
            {
                GiftInstance gift = GroveRules.RollGift(table, state);
                state.GiftIndex++;
                if (gift.ItemId == "lore_a")
                {
                    sawNonCommon = true;
                }
            }

            Assert.IsTrue(sawNonCommon, "pity must force a non-common entry within PityAt rolls");
        }

        [Test]
        public void CollectAllGifts_AppliesEveryReward_AndEmptiesThePendingList()
        {
            PlayerSave save = SaveWithBeast();
            GroveLibrary library = GroveLibrary.Build(SyntheticData());
            BeastAffinityState state = save.Grove.AffinityOf("b1");
            state.PendingGifts.Add(new GiftInstance { ItemKind = "decor", ItemId = "stone" });
            state.PendingGifts.Add(new GiftInstance { ItemKind = "lore", ItemId = "lore_a" });

            List<GiftInstance> collected = new List<GiftInstance>();
            int count = GroveRules.CollectAllGifts(save, "b1", collected);

            Assert.AreEqual(2, count);
            Assert.AreEqual(0, state.PendingGifts.Count);
            CollectionAssert.Contains(save.Grove.UnlockedDecorIds, "stone");
            CollectionAssert.Contains(save.Grove.LoreIds, "lore_a");
        }
    }
}
