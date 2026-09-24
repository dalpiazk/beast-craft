using BeastCraft.Encounters;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="EncounterLibrarySO"/> (what the encounter importer fills) and the encounter DTOs'
    /// JsonUtility round trip: arrays, doubles and the template override survive, so the asset holds
    /// exactly what the JSON says.
    /// </summary>
    public class EncounterLibrarySOTests
    {
        private EncounterLibrarySO _asset;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<EncounterLibrarySO>();
            _asset.Enemies = EncounterContentTests.LoadEnemyLibrary();
            _asset.Encounters = EncounterContentTests.LoadEncounterLibrary();
            _asset.Difficulty = EncounterPlanTests.LoadDifficulty();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
        }

        [Test]
        public void CatalogAndLibrary_AreBuiltOnce_UntilReset()
        {
            EnemyCatalog catalog = _asset.Catalog;
            EncounterLibrary library = _asset.Library;

            Assert.AreSame(catalog, _asset.Catalog);
            Assert.AreSame(library, _asset.Library);
            Assert.AreEqual(_asset.Enemies.Enemies.Length, catalog.Enemies.Count);
            Assert.IsNotNull(library.Difficulty);
            Assert.IsTrue(library.Difficulty.HasShape("solo"));

            _asset.ResetRuntimeCaches();

            Assert.AreNotSame(catalog, _asset.Catalog);
            Assert.AreNotSame(library, _asset.Library);
        }

        [Test]
        public void EncounterDtos_SurviveAJsonUtilityRoundTrip()
        {
            EncounterLibraryData data = EncounterContentTests.LoadEncounterLibrary();
            data.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "example_round_trip",
                    ShapeId = "solo",
                    Arena = "Large",
                    Groups = new[] { new EncounterGroupData { EnemyId = "giant", Count = 1, Elements = new[] { "Metal" } } },
                    DifficultyOverride = 1.25
                }
            };

            EncounterLibraryData copy = JsonUtility.FromJson<EncounterLibraryData>(JsonUtility.ToJson(data));

            Assert.AreEqual(data.DifficultyScale, copy.DifficultyScale);
            Assert.AreEqual(data.SchemeWeights.Length, copy.SchemeWeights.Length);
            Assert.AreEqual(data.Shapes.Length, copy.Shapes.Length);
            for (int i = 0; i < data.Shapes.Length; i++)
            {
                Assert.AreEqual(data.Shapes[i].ThreatMin, copy.Shapes[i].ThreatMin);
                Assert.AreEqual(data.Shapes[i].ThreatMax, copy.Shapes[i].ThreatMax);
                Assert.AreEqual(data.Shapes[i].Variants.Length, copy.Shapes[i].Variants.Length);
            }

            Assert.AreEqual(1.25, copy.Templates[0].DifficultyOverride);
            Assert.AreEqual("Metal", copy.Templates[0].Groups[0].Elements[0]);

            EnemyLibraryData enemies = EncounterContentTests.LoadEnemyLibrary();
            EnemyLibraryData enemiesCopy = JsonUtility.FromJson<EnemyLibraryData>(JsonUtility.ToJson(enemies));
            Assert.AreEqual(enemies.Enemies.Length, enemiesCopy.Enemies.Length);
            for (int i = 0; i < enemies.Enemies.Length; i++)
            {
                Assert.AreEqual(enemies.Enemies[i].Threat, enemiesCopy.Enemies[i].Threat);
                Assert.AreEqual(enemies.Enemies[i].BaseStats, enemiesCopy.Enemies[i].BaseStats);
                Assert.AreEqual(enemies.Enemies[i].Skills.Length, enemiesCopy.Enemies[i].Skills.Length);
            }

            EncounterDifficultyData difficulty = EncounterPlanTests.LoadDifficulty();
            EncounterDifficultyData difficultyCopy = JsonUtility.FromJson<EncounterDifficultyData>(JsonUtility.ToJson(difficulty));
            Assert.AreEqual(difficulty.Cells.Length, difficultyCopy.Cells.Length);
            Assert.AreEqual(difficulty.Cells[0].Multiplier, difficultyCopy.Cells[0].Multiplier);
        }
    }
}
