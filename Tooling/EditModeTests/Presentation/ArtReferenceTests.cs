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
            VfxLibraryData data = FieldJson.FromJson<VfxLibraryData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), VfxLibraryData.ProjectRelativePath)));
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
            Assert.AreEqual("enemy/giant", Content.Enemies.Species("giant", Element.Water).ArtKey);
            Assert.AreEqual("enemy/archer", Content.Enemies.Get("archer").ArtKey);
        }

        [Test]
        public void EnemiesWithoutArtOfTheirOwn_AliasAnotherSprite_WithATint()
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
        }

        [Test]
        public void Validate_HoldsSkillIconKeys_ToTheManifest_AndRequiresThemOnlyForTheSkillLibrary()
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
            Assert.AreEqual(5, strict.Count, string.Join("\n", strict));
            Assert.IsTrue(strict.Exists(e => e.Contains("Beast skill 'b'") && e.Contains("no ArtKey")));
            Assert.IsTrue(strict.Exists(e => e.Contains("Avatar passive 'p'") && e.Contains("no ArtKey")));
            Assert.IsFalse(strict.Exists(e => e.Contains("'bite'")), "enemy-library skills never need an icon");
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
            SkillLibraryData skills = FieldJson.FromJson<SkillLibraryData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), SkillLibraryData.ProjectRelativePath)));
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

            Assert.AreEqual("beast/golem/illustrated", with.ArtKey);
            Assert.IsNull(without.ArtKey);
            without.ArtKey = with.ArtKey;
            Assert.AreEqual(FieldJson.ToJson(with), FieldJson.ToJson(without));
        }
    }
}
