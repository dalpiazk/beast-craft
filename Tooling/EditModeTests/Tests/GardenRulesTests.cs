using System;
using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Wildgarden (docs/design/grove.md): planting and growth through <c>OfflineClock</c>
    /// (<see cref="GardenRules.Plant"/>, <see cref="GardenRules.GrowthProgress"/>,
    /// <see cref="GardenRules.IsReady"/>), the deterministic cross-pollination matrix
    /// (<see cref="GardenRules.Harvest"/> — a lone plot's self-pair, always the same result —
    /// and <see cref="GardenRules.HarvestPair"/> — two different ready plots' fixed hybrid), the
    /// herbarium and the generic Grove item deposit, and deterministic crafting
    /// (<see cref="GardenRules.Craft"/>). No combat power anywhere. Also the authored
    /// <c>garden-library.json</c> against <see cref="GardenLibraryValidator"/>.
    /// </summary>
    public class GardenRulesTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan M0 = TimeSpan.FromHours(3);

        internal static GardenLibraryData LoadGarden()
        {
            return EncounterContentTests.Load<GardenLibraryData>(GardenLibraryData.ProjectRelativePath);
        }

        private static GardenLibraryData SyntheticData()
        {
            return new GardenLibraryData
            {
                SchemaVersion = 1,
                PlotCount = 3,
                Seeds = new[]
                {
                    new SeedSpeciesData { SeedId = "alpha", DisplayName = "Alpha", GrowthHours = 4, Source = "starter", UnlockId = "", ArtKey = "a" },
                    new SeedSpeciesData { SeedId = "beta", DisplayName = "Beta", GrowthHours = 6, Source = "starter", UnlockId = "", ArtKey = "a" },
                    new SeedSpeciesData { SeedId = "gamma", DisplayName = "Gamma", GrowthHours = 8, Source = "shrine", UnlockId = "grove_seed_gamma", ArtKey = "a" }
                },
                CrossPollinations = new[]
                {
                    new CrossPollinationData { ParentA = "alpha", ParentB = "alpha", ResultVarietyId = "alpha_pure" },
                    new CrossPollinationData { ParentA = "beta", ParentB = "beta", ResultVarietyId = "beta_pure" },
                    new CrossPollinationData { ParentA = "gamma", ParentB = "gamma", ResultVarietyId = "gamma_pure" },
                    new CrossPollinationData { ParentA = "alpha", ParentB = "beta", ResultVarietyId = "alpha_beta_hybrid" }
                },
                Varieties = new[]
                {
                    new VarietyData { VarietyId = "alpha_pure", DisplayName = "Alpha Pure", ArtKey = "a", HerbariumEntry = "e" },
                    new VarietyData { VarietyId = "beta_pure", DisplayName = "Beta Pure", ArtKey = "a", HerbariumEntry = "e" },
                    new VarietyData { VarietyId = "gamma_pure", DisplayName = "Gamma Pure", ArtKey = "a", HerbariumEntry = "e" },
                    new VarietyData { VarietyId = "alpha_beta_hybrid", DisplayName = "Alpha-Beta", ArtKey = "a", HerbariumEntry = "e" }
                },
                Recipes = new[]
                {
                    new RecipeData
                    {
                        RecipeId = "recipe_dye", Output = "dye", OutputId = "dye_test",
                        Inputs = new[] { new RecipeInputData { VarietyId = "alpha_pure", Count = 2 } }
                    },
                    new RecipeData
                    {
                        RecipeId = "recipe_dupe", Output = "dye", OutputId = "dye_dupe",
                        // The same VarietyId split across two input rows (2 + 1 = 3 total): a content
                        // author should never write this (the validator now rejects it), but the rule
                        // itself must still sum the rows correctly rather than checking/consuming each
                        // in isolation.
                        Inputs = new[]
                        {
                            new RecipeInputData { VarietyId = "alpha_pure", Count = 2 },
                            new RecipeInputData { VarietyId = "alpha_pure", Count = 1 }
                        }
                    }
                }
            };
        }

        private static PlayerSave FreshSave()
        {
            return PlayerSave.CreateNew();
        }

        // ---- Content ----

        [Test]
        public void AuthoredLibrary_IsValid_AndHas36Varieties()
        {
            GardenLibraryData data = LoadGarden();
            DiscoveryLibraryData discovery = DiscoveryTests.ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath);
            GroveLibraryData grove = GroveRulesTests.LoadGrove();

            List<string> errors = GardenLibraryValidator.Validate(data, discovery, grove, null);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(12, data.Seeds.Length);
            Assert.AreEqual(36, data.Varieties.Length);
            Assert.AreEqual(36, data.CrossPollinations.Length);
        }

        // ---- Plant / growth ----

        [Test]
        public void Plant_RefusesAnOutOfRangePlot_AnOccupiedOne_OrAnUnlockedSeed()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());

            Assert.IsFalse(GardenRules.Plant(save, library, -1, "alpha", T0, M0).Success);
            Assert.IsFalse(GardenRules.Plant(save, library, 3, "alpha", T0, M0).Success, "PlotCount is 3 (0-2)");
            Assert.IsFalse(GardenRules.Plant(save, library, 0, "gamma", T0, M0).Success, "gamma needs its shrine unlock");

            Assert.IsTrue(GardenRules.Plant(save, library, 0, "alpha", T0, M0).Success);
            Assert.IsFalse(GardenRules.Plant(save, library, 0, "beta", T0, M0).Success, "plot 0 is occupied");
        }

        [Test]
        public void GrowthProgress_ReachesReadyExactlyAtGrowthHours_CheckedThroughOfflineClock()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());
            GardenRules.Plant(save, library, 0, "alpha", T0, M0); // GrowthHours 4
            PlotState plot = save.Garden.FindPlot(0);

            Assert.IsFalse(GardenRules.IsReady(library, plot, T0 + TimeSpan.FromHours(3), M0 + TimeSpan.FromHours(3)));
            Assert.IsTrue(GardenRules.IsReady(library, plot, T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(4)));

            // wall clock pushed far forward (well past ClockSlackMs) is clamped to the monotonic time.
            Assert.IsFalse(GardenRules.IsReady(library, plot, T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(1)));
        }

        // ---- Harvest ----

        [Test]
        public void Harvest_ALonePlot_AlwaysYieldsItsSelfPairVariety_Deterministically()
        {
            GardenLibrary library = GardenLibrary.Build(SyntheticData());

            for (int i = 0; i < 3; i++)
            {
                PlayerSave save = FreshSave();
                GardenRules.Plant(save, library, 0, "alpha", T0, M0);
                GardenHarvestResult result = GardenRules.Harvest(save, library, 0, T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(4));

                Assert.IsTrue(result.Success);
                Assert.AreEqual("alpha_pure", result.VarietyId);
                Assert.IsTrue(result.Discovered);
                Assert.AreEqual(1, save.Grove.Items.GetCount("alpha_pure"));
                Assert.IsNull(save.Garden.FindPlot(0), "the plot empties after harvest");
            }
        }

        [Test]
        public void Harvest_RefusesANotYetReadyPlot_OrAnEmptyOne()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());
            GardenRules.Plant(save, library, 0, "alpha", T0, M0);

            Assert.IsFalse(GardenRules.Harvest(save, library, 0, T0 + TimeSpan.FromHours(1), M0 + TimeSpan.FromHours(1)).Success);
            Assert.IsFalse(GardenRules.Harvest(save, library, 1, T0, M0).Success);
        }

        [Test]
        public void HarvestPair_TwoDifferentReadyPlots_AlwaysYieldsTheMatrixsFixedHybrid()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());
            GardenRules.Plant(save, library, 0, "alpha", T0, M0);
            GardenRules.Plant(save, library, 1, "beta", T0, M0);
            DateTime ready = T0 + TimeSpan.FromHours(6);
            TimeSpan readyM = M0 + TimeSpan.FromHours(6);

            GardenHarvestResult result = GardenRules.HarvestPair(save, library, 0, 1, ready, readyM);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("alpha_beta_hybrid", result.VarietyId);
            Assert.IsNull(save.Garden.FindPlot(0));
            Assert.IsNull(save.Garden.FindPlot(1));
        }

        [Test]
        public void HarvestPair_RefusesTheSamePlotTwice_OrAPairTheMatrixDoesNotDefine()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());
            GardenRules.Plant(save, library, 0, "alpha", T0, M0);
            GardenRules.Plant(save, library, 1, "gamma", T0, M0); // no alpha+gamma entry in the synthetic matrix
            DateTime ready = T0 + TimeSpan.FromHours(8);
            TimeSpan readyM = M0 + TimeSpan.FromHours(8);

            Assert.IsFalse(GardenRules.HarvestPair(save, library, 0, 0, ready, readyM).Success);
            Assert.IsFalse(GardenRules.HarvestPair(save, library, 0, 1, ready, readyM).Success);
        }

        [Test]
        public void Harvest_DiscoveredIsTrueOnlyTheFirstTime()
        {
            GardenLibrary library = GardenLibrary.Build(SyntheticData());
            PlayerSave save = FreshSave();
            GardenRules.Plant(save, library, 0, "alpha", T0, M0);
            GardenHarvestResult first = GardenRules.Harvest(save, library, 0, T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(4));
            Assert.IsTrue(first.Discovered);

            GardenRules.Plant(save, library, 0, "alpha", T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(4));
            GardenHarvestResult second = GardenRules.Harvest(save, library, 0, T0 + TimeSpan.FromHours(8), M0 + TimeSpan.FromHours(8));
            Assert.IsFalse(second.Discovered);
            Assert.AreEqual(2, save.Grove.Items.GetCount("alpha_pure"));
        }

        // ---- Craft ----

        [Test]
        public void Craft_RefusesWithoutEnoughInputs_ThenConsumesAndProducesOnSuccess()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());

            Assert.IsFalse(GardenRules.Craft(save, library, "recipe_dye").Success);

            save.Grove.Items.Add("alpha_pure", 2);
            Assert.IsTrue(GardenRules.Craft(save, library, "recipe_dye").Success);
            Assert.AreEqual(0, save.Grove.Items.GetCount("alpha_pure"));
            Assert.AreEqual(1, save.Grove.Items.GetCount("dye_test"));
        }

        [Test]
        public void Craft_UnknownRecipe_Refuses()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());

            Assert.IsFalse(GardenRules.Craft(save, library, "no_such_recipe").Success);
        }

        [Test]
        public void Craft_SumsARepeatedInputVarietyAcrossRows_BeforeCheckingAndConsuming()
        {
            PlayerSave save = FreshSave();
            GardenLibrary library = GardenLibrary.Build(SyntheticData());

            // recipe_dupe needs 3 alpha_pure total (2 + 1 rows). Holding only 2 must refuse and change
            // nothing (the old per-row check would have let this through with 2 held).
            save.Grove.Items.Add("alpha_pure", 2);
            Assert.IsFalse(GardenRules.Craft(save, library, "recipe_dupe").Success);
            Assert.AreEqual(2, save.Grove.Items.GetCount("alpha_pure"), "a refused craft must not consume anything");
            Assert.AreEqual(0, save.Grove.Items.GetCount("dye_dupe"));

            save.Grove.Items.Add("alpha_pure", 1);
            Assert.IsTrue(GardenRules.Craft(save, library, "recipe_dupe").Success);
            Assert.AreEqual(0, save.Grove.Items.GetCount("alpha_pure"), "all 3 required are consumed, not just one row's worth");
            Assert.AreEqual(1, save.Grove.Items.GetCount("dye_dupe"));
        }

        // ---- Content: no recipe may repeat a VarietyId across its own inputs ----

        [Test]
        public void ValidateRecipes_RejectsARepeatedVarietyIdWithinOneRecipe()
        {
            GardenLibraryData data = SyntheticData();
            List<string> errors = GardenLibraryValidator.Validate(data, null, null, null);

            Assert.IsTrue(errors.Exists(e => e.Contains("recipe_dupe") && e.Contains("repeated")),
                          "recipe_dupe's two alpha_pure rows must be flagged:\n" + string.Join("\n", errors));
        }
    }
}
