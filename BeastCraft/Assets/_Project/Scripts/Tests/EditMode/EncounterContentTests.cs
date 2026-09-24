using System.Collections.Generic;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Skills;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The authored encounter content, straight from the JSON (so it runs without imported assets):
    /// <c>Data/Encounters/enemy-library.json</c> and <c>encounter-library.json</c> pass their
    /// validators against the real roster and drop tables, and <see cref="EnemyCatalog"/> builds
    /// every enemy's kit as authored. Balance is the simulator's business, not these tests'.
    /// </summary>
    public class EncounterContentTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void AuthoredEnemyLibrary_PassesValidationAgainstTheRoster()
        {
            List<string> errors = EnemyLibraryValidator.Validate(LoadEnemyLibrary(), BeastRosterTests.LoadRoster());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void AuthoredEncounterLibrary_PassesValidationAgainstTheEnemiesAndTheDropTables()
        {
            List<string> errors = EncounterLibraryValidator.Validate(LoadEncounterLibrary(), LoadEnemyLibrary(), DropTableTests.LoadTables());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void AuthoredEncounterLibrary_ShapeIdsAreExactlyTheDropTableShapes()
        {
            List<string> shapes = new List<string>();
            foreach (EncounterShapeData shape in LoadEncounterLibrary().Shapes)
            {
                shapes.Add(shape.ShapeId);
            }

            CollectionAssert.AreEquivalent(DropTableTests.LoadTables().Shapes, shapes);
        }

        [Test]
        public void AuthoredEnemyLibrary_CatalogKitsAreTheAuthoredSkills()
        {
            EnemyLibraryData library = LoadEnemyLibrary();
            EnemyCatalog catalog = EnemyCatalog.Build(library, null);

            foreach (EnemyData enemy in library.Enemies)
            {
                foreach (Element element in new[] { Element.None, Element.Fire, Element.Dark })
                {
                    IReadOnlyList<SkillSO> kit = catalog.Kit(enemy.EnemyId, element);
                    CreatureSpeciesSO species = catalog.Species(enemy.EnemyId, element);
                    _created.AddRange(kit);
                    _created.Add(species);

                    Assert.AreEqual(enemy.EnemyId, species.SpeciesId);
                    Assert.AreEqual(enemy.BaseStats, species.BaseStats);
                    Assert.AreEqual(enemy.Skills.Length, kit.Count, enemy.EnemyId);
                    for (int i = 0; i < kit.Count; i++)
                    {
                        SkillData data = enemy.Skills[i];
                        Assert.AreEqual(data.SkillId, kit[i].SkillId);
                        Assert.AreEqual(element, kit[i].Element);
                        Assert.AreEqual(data.Range, kit[i].Range);
                        Assert.AreEqual(data.Cooldown, kit[i].Cooldown);
                        Assert.AreEqual(SkillLibraryValidator.ParseOr(data.Category, DamageCategory.Physical), kit[i].Category);
                        Assert.AreEqual(SkillLibraryValidator.ParseOr(data.TargetShape, SkillTargetShape.SingleTarget), kit[i].TargetShape);
                        Assert.AreEqual(SkillLibraryValidator.ParseOr(data.TargetingCriterion, SkillTargetingCriterion.Distance), kit[i].TargetingCriterion);
                        Assert.AreEqual(data.Effects[0].Magnitude, kit[i].Effects[0].Magnitude);
                        Assert.AreEqual(SkillEffectType.Damage, kit[i].Effects[0].EffectType);
                    }
                }
            }
        }

        internal static EnemyLibraryData LoadEnemyLibrary()
        {
            return Load<EnemyLibraryData>(EnemyLibraryData.ProjectRelativePath);
        }

        internal static EncounterLibraryData LoadEncounterLibrary()
        {
            return Load<EncounterLibraryData>(EncounterLibraryData.ProjectRelativePath);
        }

        internal static T Load<T>(string projectRelativePath) where T : class
        {
            string path = DropTableTests.FindFile(projectRelativePath);
            Assert.IsNotNull(path, "Could not find " + projectRelativePath);

            T data = JsonUtility.FromJson<T>(File.ReadAllText(path));
            Assert.IsNotNull(data);
            return data;
        }
    }
}
