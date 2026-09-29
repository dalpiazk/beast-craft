using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Skills;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The content's art references: every species' and enemy's <c>ArtKey</c> and every skill's,
    /// avatar active's and passive's icon key (presentation only) names an entry of the real art
    /// manifest, every VFX sheet is in it, every manifest file exists,
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
            VfxLibraryData data = FieldJson.FromJson<VfxLibraryData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), VfxLibraryData.ProjectRelativePath)));
            List<string> errors = VfxLibraryValidator.Validate(data, Content.KnownSkillIds, Content.Art);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            string folder = Path.GetDirectoryName(GameContent.PathOf(GameContent.FindRoot(), ArtManifestData.ProjectRelativePath));
            foreach (ArtSpriteData sprite in Content.Art.Sprites)
            {
                Assert.IsTrue(File.Exists(Path.Combine(folder, sprite.File)), sprite.Name + ": " + sprite.File);
            }
        }

        [Test]
        public void ArtKey_ReachesTheRuntimeSpecies_ForBeastsAndEnemies()
        {
            Assert.AreEqual("beast/phoenix/illustrated", Content.Battle.GetSpecies("phoenix").ArtKey);
            Assert.AreEqual("enemy/giant/hollow", Content.Enemies.Species("giant", Element.Water).ArtKey);
            Assert.AreEqual("enemy/archer/hollow", Content.Enemies.Get("archer").ArtKey);
        }

        [Test]
        public void EnemyArt_IsKeyedByRegion_WithADefault()
        {
            EnemyData brute = Content.Enemies.Get("brute");

            Assert.AreEqual("enemy/brute/hollow", brute.ArtKey, "the default is the Verdant Hollow art");
            Assert.AreEqual("enemy/brute/hollow", brute.ArtKeyFor("r01"));
            Assert.AreEqual("enemy/brute/hollow", brute.ArtKeyFor("r02"), "a region without a variant draws the default");
            Assert.AreEqual("enemy/brute/hollow", brute.ArtKeyFor(null));

            EnemyData twoRegions = new EnemyData
            {
                EnemyId = "e",
                ArtKey = "enemy/e/hollow",
                RegionArt = new[] { new EnemyRegionArtData { RegionId = "r01", ArtKey = "enemy/e/hollow" }, new EnemyRegionArtData { RegionId = "r02", ArtKey = "enemy/e/ember" } }
            };
            Assert.AreEqual("enemy/e/ember", twoRegions.ArtKeyFor("r02"));
            Assert.AreEqual("enemy/e/hollow", twoRegions.ArtKeyFor("r03"));
        }

        [Test]
        public void TheViewer_ResolvesEnemyArtByRegion_AndFallsBackToTheDefault()
        {
            EnemyData brute = Content.Enemies.Get("brute");
            EnemyRegionArtData[] shipped = brute.RegionArt;
            try
            {
                // A fake second-region variant (the pixel placeholder stands in for its art).
                brute.RegionArt = new[] { shipped[0], new EnemyRegionArtData { RegionId = "r02", ArtKey = "enemy/brute" } };

                Assert.AreEqual("enemy/brute", DemoBattle.ArtKeyOf(Content, "brute", "r02"), "the region's own variant");
                Assert.AreEqual("enemy/brute/hollow", DemoBattle.ArtKeyOf(Content, "brute", "r01"));
                Assert.AreEqual("enemy/brute/hollow", DemoBattle.ArtKeyOf(Content, "brute", "r03"), "no variant: the default");
                Assert.AreEqual("beast/phoenix/illustrated", DemoBattle.ArtKeyOf(Content, "phoenix", "r02"), "a beast keeps its own art");
            }
            finally
            {
                brute.RegionArt = shipped;
            }

            Assert.AreEqual("r02", DemoBattle.RegionOf(Content, "boss_r02_ember_twins"), "a region boss belongs to its region");
            Assert.IsNull(DemoBattle.RegionOf(Content, "squad"), "a generated shape has no region of its own");
            Assert.IsTrue(DemoBattle.IsRegion(Content, DemoBattle.DefaultRegionId));
        }

        [Test]
        public void RegionArt_RegionIds_MustBeRegionsInRegionsJson()
        {
            EnemyLibraryData library = FieldJson.FromJson<EnemyLibraryData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), EnemyLibraryData.ProjectRelativePath)));
            Assert.IsEmpty(EnemyLibraryValidator.Validate(library, null, Content.Regions), "the shipped library names only real regions");

            library.Enemies[0].RegionArt = new[] { new EnemyRegionArtData { RegionId = "r1", ArtKey = library.Enemies[0].ArtKey } };
            List<string> errors = EnemyLibraryValidator.Validate(library, null, Content.Regions);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("'r1' is not a region in regions.json", errors[0]);
            Assert.IsEmpty(EnemyLibraryValidator.Validate(library, null), "without regions.json the id is not cross-checked");
        }

        [Test]
        public void RegionArt_NeedsARegionOnce_AndAKnownKey()
        {
            EnemyData enemy = new EnemyData
            {
                EnemyId = "e",
                ArtKey = "enemy/e",
                RegionArt = new[]
                {
                    new EnemyRegionArtData { RegionId = "r01", ArtKey = "enemy/e" }, new EnemyRegionArtData { RegionId = "r01", ArtKey = "enemy/e" },
                    new EnemyRegionArtData { RegionId = "", ArtKey = "enemy/e" }, new EnemyRegionArtData { RegionId = "r02", ArtKey = "enemy/missing" }
                }
            };
            EnemyLibraryData library = new EnemyLibraryData { Enemies = new[] { enemy } };
            ArtManifestData art = new ArtManifestData { Sprites = new[] { new ArtSpriteData { Name = "e", File = "e.png", ArtKey = "enemy/e" } } };

            List<string> shape = EnemyLibraryValidator.Validate(library, null);
            List<string> keys = ArtReferenceValidator.Validate(null, library, art);

            Assert.IsTrue(shape.Exists(e => e.Contains("'r01' twice")), string.Join("\n", shape));
            Assert.IsTrue(shape.Exists(e => e.Contains("no RegionId")), string.Join("\n", shape));
            Assert.AreEqual(1, keys.Count, string.Join("\n", keys));
            StringAssert.Contains("enemy/missing", keys[0]);
        }

        [Test]
        public void EnemyPixelPlaceholders_StayInTheManifest_AliasesTinted()
        {
            ArtSpriteData archer = Content.Art.FindByArtKey("enemy/archer");
            ArtSpriteData brute = Content.Art.FindByArtKey("enemy/brute");

            Assert.IsNotNull(archer);
            Assert.AreEqual(brute.File, archer.File);
            StringAssert.StartsWith("#", archer.Tint);
            Assert.IsTrue(string.IsNullOrEmpty(brute.Tint));
        }

        [Test]
        public void Validate_ReportsAMissingKey_OnlyWhenKeysAreRequired_AndAnUnknownOne_Always()
        {
            ArtManifestData art = new ArtManifestData
            {
                Sprites = new[] { new ArtSpriteData { Name = "a", File = "a.png", ArtKey = "beast/a" } }
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

        [Test]
        public void EveryShippedSkillActiveAndPassive_HasAnIconKey_InTheManifest()
        {
            List<string> errors = ArtReferenceValidator.Validate(Content.Roster, Content.EnemyLibrary, Content.SkillLibrary, Content.Art, requireKeys: true);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            foreach (SkillData skill in Content.SkillLibrary.BeastSkills)
            {
                Assert.AreEqual("skill/" + skill.SkillId, skill.ArtKey, skill.SkillId);
                Assert.AreEqual("skill", Content.Art.FindByArtKey(skill.ArtKey).Category, skill.SkillId);
            }

            foreach (SkillData skill in Content.SkillLibrary.AvatarActives)
            {
                Assert.AreEqual("skill/" + skill.SkillId, skill.ArtKey, skill.SkillId);
            }

            foreach (PassiveData passive in Content.SkillLibrary.AvatarPassives)
            {
                Assert.AreEqual("skill/" + passive.PassiveId, passive.ArtKey, passive.PassiveId);
            }

            int enemySkills = 0;
            foreach (EnemyData enemy in Content.EnemyLibrary.Enemies)
            {
                foreach (SkillData skill in enemy.Skills)
                {
                    Assert.AreEqual("skill/enemy/" + enemy.EnemyId + "/" + skill.SkillId, skill.ArtKey, enemy.EnemyId + "/" + skill.SkillId);
                    Assert.AreEqual("skill", Content.Art.FindByArtKey(skill.ArtKey).Category, skill.ArtKey);
                    enemySkills++;
                }
            }

            Assert.AreEqual(15, enemySkills);
            Assert.AreEqual("skill/enemy/giant/quake", Content.Enemies.Kit("giant", Element.Earth)[2].ArtKey, "an enemy's runtime skill carries its icon");
        }

        [Test]
        public void EnemyLibraryValidator_RefusesAMalformedSkillIconKey()
        {
            EnemyLibraryData enemies = FieldJson.FromJson<EnemyLibraryData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), EnemyLibraryData.ProjectRelativePath)));
            Assert.IsEmpty(EnemyLibraryValidator.Validate(enemies, Content.Roster, Content.Regions));

            enemies.Enemies[0].Skills[0].ArtKey = "Skill/Enemy/Crush";
            List<string> errors = EnemyLibraryValidator.Validate(enemies, Content.Roster, Content.Regions);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("ArtKey 'Skill/Enemy/Crush'", errors[0]);
        }

        [Test]
        public void Validate_HoldsSkillIconKeys_ToTheManifest_AndRequiresThem_EnemySkillsToo()
        {
            ArtManifestData art = new ArtManifestData
            {
                Sprites = new[] { new ArtSpriteData { Name = "s", File = "s.png", ArtKey = "skill/known" }, new ArtSpriteData { Name = "e", File = "e.png", ArtKey = "enemy/e" } }
            };
            SkillLibraryData skills = new SkillLibraryData
            {
                BeastSkills = new[] { new SkillData { SkillId = "a", ArtKey = "skill/known" }, new SkillData { SkillId = "b" } },
                AvatarActives = new[] { new SkillData { SkillId = "c", ArtKey = "skill/nope" } },
                AvatarPassives = new[] { new PassiveData { PassiveId = "p" }, new PassiveData { PassiveId = "q", ArtKey = "skill/gone" } }
            };
            EnemyLibraryData enemies = new EnemyLibraryData
            {
                Enemies = new[]
                {
                    new EnemyData { EnemyId = "e", ArtKey = "enemy/e", Skills = new[] { new SkillData { SkillId = "bite" }, new SkillData { SkillId = "claw", ArtKey = "skill/claw" } } }
                }
            };

            List<string> loose = ArtReferenceValidator.Validate(null, enemies, skills, art);
            List<string> strict = ArtReferenceValidator.Validate(null, enemies, skills, art, requireKeys: true);

            Assert.AreEqual(3, loose.Count, string.Join("\n", loose));
            Assert.IsTrue(loose.Exists(e => e.Contains("Avatar active 'c'") && e.Contains("skill/nope")));
            Assert.IsTrue(loose.Exists(e => e.Contains("Avatar passive 'q'") && e.Contains("skill/gone")));
            Assert.IsTrue(loose.Exists(e => e.Contains("skill 'claw'") && e.Contains("skill/claw")), "an enemy skill's icon, when set, is checked");
            Assert.AreEqual(6, strict.Count, string.Join("\n", strict));
            Assert.IsTrue(strict.Exists(e => e.Contains("Beast skill 'b'") && e.Contains("no ArtKey")));
            Assert.IsTrue(strict.Exists(e => e.Contains("Avatar passive 'p'") && e.Contains("no ArtKey")));
            Assert.IsTrue(strict.Exists(e => e.Contains("Enemy 'e' skill 'bite'") && e.Contains("no ArtKey")), "enemy skills need an icon too");
        }

        [Test]
        public void SkillIconKey_ReachesTheRuntimeSkillAndPassive_AndIsPresentationOnly()
        {
            SkillData data = Array.Find(Content.SkillLibrary.BeastSkills, s => s.SkillId == "ember_shot");
            SkillSO with = new SkillSO();
            SkillSO without = new SkillSO();
            SkillLibraryBuilder.ApplySkill(data, with);
            string key = data.ArtKey;
            data.ArtKey = null;
            try
            {
                SkillLibraryBuilder.ApplySkill(data, without);
            }
            finally
            {
                data.ArtKey = key;
            }

            Assert.AreEqual("skill/ember_shot", with.ArtKey);
            Assert.IsNull(without.ArtKey);
            without.ArtKey = with.ArtKey;
            Assert.AreEqual(FieldJson.ToJson(with), FieldJson.ToJson(without));

            PassiveSkillSO passive = new PassiveSkillSO();
            SkillLibraryBuilder.ApplyPassive(Content.SkillLibrary.AvatarPassives[0], passive);
            Assert.AreEqual("skill/" + passive.PassiveId, passive.ArtKey);
        }

        [Test]
        public void SkillLibraryValidator_RefusesAMalformedIconKey()
        {
            SkillLibraryData skills = FieldJson.FromJson<SkillLibraryData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), SkillLibraryData.ProjectRelativePath)));
            Assert.IsEmpty(SkillLibraryValidator.Validate(skills, Content.Roster));

            skills.BeastSkills[0].ArtKey = "Skill/Boulder";
            skills.AvatarPassives[0].ArtKey = "skill//keen";
            List<string> errors = SkillLibraryValidator.Validate(skills, Content.Roster);

            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
            Assert.IsTrue(errors.TrueForAll(e => e.Contains("ArtKey")));
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
            BeastRosterData roster = FieldJson.FromJson<BeastRosterData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), BeastRosterData.ProjectRelativePath)));
            roster.Species[0].ArtKey = "Beast/Phoenix";
            Assert.IsTrue(BeastRosterValidator.Validate(roster).Exists(e => e.Contains("ArtKey")));

            EnemyLibraryData enemies = FieldJson.FromJson<EnemyLibraryData>(BeastCraft.Localization.ContentText.ReadFile(GameContent.PathOf(GameContent.FindRoot(), EnemyLibraryData.ProjectRelativePath)));
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

            Assert.AreEqual("beast/golem/illustrated", with.ArtKey);
            Assert.IsNull(without.ArtKey);
            without.ArtKey = with.ArtKey;
            Assert.AreEqual(FieldJson.ToJson(with), FieldJson.ToJson(without));
        }
    }
}
