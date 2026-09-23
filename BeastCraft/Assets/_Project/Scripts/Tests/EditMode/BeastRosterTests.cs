using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Checks the authored starter roster (<c>Data/Creatures/beast-roster.json</c>) straight from the
    /// JSON, so it runs without the imported assets existing. Structural rules come from
    /// <see cref="BeastRosterValidator"/>; the roster-shape and first-draft balance guidelines
    /// (ten beasts, one per element, the stat budget band, the move-range band) live here, where
    /// the balance pass can deliberately move them.
    /// </summary>
    public class BeastRosterTests
    {
        private const int SpeciesCount = 10;
        private const int SixStatBudget = 600;
        private const float BudgetTolerance = 0.05f;
        private const int MinMoveRange = 2;
        private const int MaxMoveRange = 5;

        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Roster_PassesStructuralValidation()
        {
            List<string> errors = BeastRosterValidator.Validate(LoadRoster());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Roster_HasTheTenApprovedSpeciesIds()
        {
            // SpeciesIds are persisted in save data and must never be renamed after ship; this pins them.
            string[] expected = { "phoenix", "leviathan", "golem", "griffin", "thunderbird", "frost_wyrm", "treant", "tarasque", "kirin", "basilisk" };
            BeastRosterData roster = LoadRoster();

            Assert.AreEqual(SpeciesCount, roster.Species.Length);
            CollectionAssert.AreEquivalent(expected, Array.ConvertAll(roster.Species, s => s.SpeciesId));
        }

        [Test]
        public void Roster_UsesEveryElementExceptNoneExactlyOnce()
        {
            Dictionary<Element, int> uses = new Dictionary<Element, int>();
            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.AreEqual(1, species.Elements.Length, species.SpeciesId + " should have a single element.");
                Assert.IsTrue(BeastRosterValidator.TryParseElement(species.Elements[0], out Element element), species.Elements[0]);
                uses[element] = uses.TryGetValue(element, out int count) ? count + 1 : 1;
            }

            foreach (Element element in (Element[])Enum.GetValues(typeof(Element)))
            {
                int expected = element == Element.None ? 0 : 1;
                Assert.AreEqual(expected, uses.TryGetValue(element, out int count) ? count : 0, element.ToString());
            }
        }

        [Test]
        public void Roster_SixStatTotalsStayWithinTheSharedBudgetBand()
        {
            int min = Mathf.RoundToInt(SixStatBudget * (1f - BudgetTolerance));
            int max = Mathf.RoundToInt(SixStatBudget * (1f + BudgetTolerance));

            foreach (SpeciesData species in LoadRoster().Species)
            {
                StatBlock s = species.BaseStats;
                int total = s.Hp + s.Attack + s.Defense + s.SpecialAttack + s.SpecialDefense + s.Speed;
                Assert.That(total, Is.InRange(min, max), species.SpeciesId + " six-stat total");
            }
        }

        [Test]
        public void Roster_MoveRangeStaysInBand()
        {
            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.That(species.BaseStats.MoveRange, Is.InRange(MinMoveRange, MaxMoveRange), species.SpeciesId);
            }
        }

        [Test]
        public void Roster_AllSpeciesShareTheMediumCurveForNow()
        {
            // User decision: one shared curve until the balance simulator differentiates them.
            // fast and slow stay defined (and validated below) but unused.
            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.AreEqual("medium", species.GrowthCurveId, species.SpeciesId);
            }
        }

        [Test]
        public void Curves_StartAboveZeroAtLevelOneAndReachOneAtMaxLevel()
        {
            BeastRosterData roster = LoadRoster();
            Assert.AreEqual(3, roster.GrowthCurves.Length);

            foreach (GrowthCurveData curve in roster.GrowthCurves)
            {
                float first = BeastRosterValidator.ScaleAtLevel(curve, 1);
                Assert.That(first, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.25f), curve.CurveId + " level 1");
                Assert.AreEqual(1f, BeastRosterValidator.ScaleAtLevel(curve, curve.MaxLevel), 0.0001f, curve.CurveId + " max level");
            }
        }

        [Test]
        public void Curves_AreLinearBetweenAuthoredKeys()
        {
            // Guards the linear tangents ToAnimationCurve sets: with Unity's default flat tangents the
            // midpoint of a two-key curve would not sit on the straight line.
            GrowthCurveData medium = Array.Find(LoadRoster().GrowthCurves, c => c.CurveId == "medium");
            Assert.IsNotNull(medium);

            float expected = 0.15f + (0.85f * 0.5f);
            Assert.AreEqual(expected, medium.ToAnimationCurve().Evaluate(0.5f), 0.0001f);
        }

        [Test]
        public void Species_EveryStatIsUsableAtLevelOneAndFullAtMaxLevel()
        {
            BeastRosterData roster = LoadRoster();

            foreach (SpeciesData data in roster.Species)
            {
                GrowthCurveData curveData = Array.Find(roster.GrowthCurves, c => c.CurveId == data.GrowthCurveId);
                CreatureSpeciesSO species = BuildSpecies(data, curveData);

                foreach (StatType stat in (StatType[])Enum.GetValues(typeof(StatType)))
                {
                    Assert.That(species.GetStatAtLevel(stat, 1), Is.GreaterThanOrEqualTo(1), data.SpeciesId + " " + stat + " at level 1");
                    Assert.AreEqual(data.BaseStats.GetStat(stat), species.GetStatAtLevel(stat, curveData.MaxLevel), data.SpeciesId + " " + stat + " at max level");
                }
            }
        }

        [Test]
        public void Validator_RejectsBadReferencesElementsAndCurves()
        {
            BeastRosterData roster = LoadRoster();
            roster.Species[0].Elements = new[] { "Plasma" };
            roster.Species[1].GrowthCurveId = "glacial";
            roster.Species[2].SpeciesId = roster.Species[3].SpeciesId;
            roster.GrowthCurves[0].Keys[0].Scale = 0f;

            List<string> errors = BeastRosterValidator.Validate(roster);

            Assert.IsTrue(errors.Exists(e => e.Contains("'Plasma' is not an Element name")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("'glacial' does not match")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate SpeciesId")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("level-1 scale")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_ParsesElementNamesOnly()
        {
            Assert.IsTrue(BeastRosterValidator.TryParseElement("Lightning", out Element element));
            Assert.AreEqual(Element.Lightning, element);
            Assert.IsFalse(BeastRosterValidator.TryParseElement("lightning", out _));
            Assert.IsFalse(BeastRosterValidator.TryParseElement("5", out _));
        }

        private CreatureSpeciesSO BuildSpecies(SpeciesData data, GrowthCurveData curveData)
        {
            GrowthRateCurve curve = ScriptableObject.CreateInstance<GrowthRateCurve>();
            curve.Curve = curveData.ToAnimationCurve();
            curve.MaxLevel = curveData.MaxLevel;
            _created.Add(curve);

            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            species.SpeciesId = data.SpeciesId;
            species.BaseStats = data.BaseStats;
            species.GrowthRate = curve;
            _created.Add(species);
            return species;
        }

        /// <summary>
        /// Reads the roster JSON. In the Unity Editor the working directory is the project folder, so
        /// the project-relative path resolves directly; other runners find it by walking up from
        /// their working or base directory.
        /// </summary>
        private static BeastRosterData LoadRoster()
        {
            string path = FindRosterFile();
            Assert.IsNotNull(path, "Could not find " + BeastRosterData.ProjectRelativePath);

            BeastRosterData roster = JsonUtility.FromJson<BeastRosterData>(File.ReadAllText(path));
            Assert.IsNotNull(roster);
            return roster;
        }

        private static string FindRosterFile()
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

            foreach (string start in starts)
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string[] candidates =
                    {
                        Path.Combine(dir.FullName, BeastRosterData.ProjectRelativePath),
                        Path.Combine(dir.FullName, "BeastCraft", BeastRosterData.ProjectRelativePath)
                    };

                    foreach (string candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }
    }
}
