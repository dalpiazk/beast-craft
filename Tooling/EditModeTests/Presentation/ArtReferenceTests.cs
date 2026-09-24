using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The content's art references: every species' and enemy's <c>ArtKey</c> (presentation only)
    /// names an entry of the real art manifest, every VFX sheet is in it, every manifest file exists,
    /// and <see cref="ArtReferenceValidator"/>'s rules one by one.
    /// </summary>
    public class ArtReferenceTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [Test]
        public void EveryShippedSpeciesAndEnemy_HasAnArtKey_InTheManifest()
        {
            List<string> errors = ArtReferenceValidator.Validate(Content.Roster, Content.EnemyLibrary, Content.Art, requireKeys: true);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(10, Content.Roster.Species.Length);
            Assert.AreEqual(9, Content.EnemyLibrary.Enemies.Length);
        }

        [Test]
        public void EveryVfxSheet_IsInTheManifest_AndEveryManifestFileExists()
        {
            VfxLibraryData data = FieldJson.FromJson<VfxLibraryData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), VfxLibraryData.ProjectRelativePath)));
            List<string> errors = VfxLibraryValidator.Validate(data, Content.KnownSkillIds, Content.Art);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            string folder = Path.GetDirectoryName(GameContent.PathOf(GameContent.FindRoot(), PixelArtManifestData.ProjectRelativePath));
            foreach (PixelSpriteData sprite in Content.Art.Sprites)
            {
                Assert.IsTrue(File.Exists(Path.Combine(folder, sprite.File)), sprite.Name + ": " + sprite.File);
            }
        }

        [Test]
        public void ArtKey_ReachesTheRuntimeSpecies_ForBeastsAndEnemies()
        {
            Assert.AreEqual("beast/phoenix", Content.Battle.GetSpecies("phoenix").ArtKey);
            Assert.AreEqual("enemy/giant", Content.Enemies.Species("giant", Element.Water).ArtKey);
            Assert.AreEqual("enemy/archer", Content.Enemies.Get("archer").ArtKey);
        }

        [Test]
        public void EnemiesWithoutArtOfTheirOwn_AliasAnotherSprite_WithATint()
        {
            PixelSpriteData archer = Content.Art.FindByArtKey("enemy/archer");
            PixelSpriteData brute = Content.Art.FindByArtKey("enemy/brute");

            Assert.IsNotNull(archer);
            Assert.AreEqual(brute.File, archer.File);
            StringAssert.StartsWith("#", archer.Tint);
            Assert.IsTrue(string.IsNullOrEmpty(brute.Tint));
        }

        [Test]
        public void Validate_ReportsAMissingKey_OnlyWhenKeysAreRequired_AndAnUnknownOne_Always()
        {
            PixelArtManifestData art = new PixelArtManifestData
            {
                Sprites = new[] { new PixelSpriteData { Name = "a", File = "a.png", ArtKey = "beast/a" } }
            };
            BeastRosterData roster = new BeastRosterData
            {
                Species = new[]
                {
                    new SpeciesData { SpeciesId = "a", ArtKey = "beast/a" },
                    new SpeciesData { SpeciesId = "b" },
                    new SpeciesData { SpeciesId = "c", ArtKey = "beast/c" }
                }
            };
            EnemyLibraryData enemies = new EnemyLibraryData { Enemies = new[] { new EnemyData { EnemyId = "e", ArtKey = "enemy/e" } } };

            List<string> loose = ArtReferenceValidator.Validate(roster, enemies, art);
            List<string> strict = ArtReferenceValidator.Validate(roster, enemies, art, requireKeys: true);

            Assert.AreEqual(2, loose.Count, string.Join("\n", loose));
            Assert.IsTrue(loose.Exists(e => e.Contains("'c'") && e.Contains("beast/c")));
            Assert.IsTrue(loose.Exists(e => e.Contains("'e'") && e.Contains("enemy/e")));
            Assert.AreEqual(3, strict.Count);
            Assert.IsTrue(strict.Exists(e => e.Contains("'b'") && e.Contains("no ArtKey")));
        }

        [TestCase("beast/phoenix", true)]
        [TestCase("enemy/frost_wyrm_2", true)]
        [TestCase("single", true)]
        [TestCase("Beast/phoenix", false)]
        [TestCase("beast//phoenix", false)]
        [TestCase("/beast", false)]
        [TestCase("beast/", false)]
        [TestCase("beast phoenix", false)]
        [TestCase("", false)]
        public void IsWellFormed(string key, bool expected)
        {
            Assert.AreEqual(expected, ArtReferenceValidator.IsWellFormed(key));
        }

        [Test]
        public void RosterAndEnemyValidators_RefuseAMalformedArtKey()
        {
            BeastRosterData roster = FieldJson.FromJson<BeastRosterData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), BeastRosterData.ProjectRelativePath)));
            roster.Species[0].ArtKey = "Beast/Phoenix";
            Assert.IsTrue(BeastRosterValidator.Validate(roster).Exists(e => e.Contains("ArtKey")));

            EnemyLibraryData enemies = FieldJson.FromJson<EnemyLibraryData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), EnemyLibraryData.ProjectRelativePath)));
            enemies.Enemies[0].ArtKey = "enemy giant";
            Assert.IsTrue(EnemyLibraryValidator.Validate(enemies, roster).Exists(e => e.Contains("ArtKey")));
        }

        [Test]
        public void ArtKey_IsPresentationOnly_TheBuiltSpeciesIsOtherwiseUnchanged()
        {
            SpeciesData data = Array.Find(Content.Roster.Species, s => s.SpeciesId == "golem");
            CreatureSpeciesSO with = new CreatureSpeciesSO();
            CreatureSpeciesSO without = new CreatureSpeciesSO();
            BeastRosterBuilder.ApplySpecies(data, with, null);
            string key = data.ArtKey;
            data.ArtKey = null;
            try
            {
                BeastRosterBuilder.ApplySpecies(data, without, null);
            }
            finally
            {
                data.ArtKey = key;
            }

            Assert.AreEqual("beast/golem", with.ArtKey);
            Assert.IsNull(without.ArtKey);
            without.ArtKey = with.ArtKey;
            Assert.AreEqual(FieldJson.ToJson(with), FieldJson.ToJson(without));
        }
    }
}
