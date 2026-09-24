using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Battle;
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
    /// (ten beasts, one per element, the stat budget band, the move-range band, the crit-chance
    /// band, the speed band and order) live here, where the balance pass can deliberately move them. The six-stat budget is
    /// the six combat stats only: <c>MoveRange</c> and <c>CritChance</c> sit outside it.
    /// </summary>
    public class BeastRosterTests
    {
        private const int SpeciesCount = 10;
        private const int SixStatBudget = 600;
        private const float BudgetTolerance = 0.05f;
        private const int MinMoveRange = 2;
        private const int MaxMoveRange = 5;
        private const int MinCritChance = 0;
        private const int MaxCritChance = 25;
        private const double MinTurnRateSpread = 1.10;
        private const double MaxTurnRateSpread = 1.15;

        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
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
        public void Roster_TurnRateSpreadStaysInBand()
        {
            // User decision (authored-kits retune): the fastest beast gets 10-15% more turns than the
            // slowest. Turns come from the ATB fill rate, which grows with sqrt(Speed), so this is
            // checked on TurnManager.FillRateForSpeed of the base Speeds, not on Speed itself (a
            // 10-15% turn spread is a roughly 21-32% base Speed spread). Every species shares the
            // growth curve, so the ratio holds at every level up to rounding.
            int slowest = int.MaxValue;
            int fastest = int.MinValue;
            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.That(species.BaseStats.Speed, Is.GreaterThan(0), species.SpeciesId);
                slowest = Math.Min(slowest, TurnManager.FillRateForSpeed(species.BaseStats.Speed));
                fastest = Math.Max(fastest, TurnManager.FillRateForSpeed(species.BaseStats.Speed));
            }

            double ratio = (double)fastest / slowest;
            Assert.That(ratio, Is.InRange(MinTurnRateSpread, MaxTurnRateSpread), "fastest fill rate " + fastest + " / slowest " + slowest);
        }

        [Test]
        public void Roster_BaseSpeedsKeepTheApprovedOrder()
        {
            // The archetypes' speed order, fastest first (ties allowed): Thunderbird, Griffin, Basilisk,
            // Phoenix, Kirin, Frost Wyrm, Tarasque, Leviathan, Treant, and Golem strictly the slowest.
            string[] order = { "thunderbird", "griffin", "basilisk", "phoenix", "kirin", "frost_wyrm", "tarasque", "leviathan", "treant", "golem" };
            BeastRosterData roster = LoadRoster();
            int[] speeds = Array.ConvertAll(order, id => Array.Find(roster.Species, s => s.SpeciesId == id).BaseStats.Speed);

            for (int i = 1; i < order.Length; i++)
            {
                Assert.That(speeds[i - 1], Is.GreaterThanOrEqualTo(speeds[i]), order[i - 1] + " should be at least as fast as " + order[i]);
            }

            Assert.That(speeds[order.Length - 2], Is.GreaterThan(speeds[order.Length - 1]), "golem should be the slowest beast");
        }

        [Test]
        public void Roster_CritChanceStaysInBand()
        {
            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.That(species.BaseStats.CritChance, Is.InRange(MinCritChance, MaxCritChance), species.SpeciesId);
            }
        }

        [Test]
        public void Roster_CritChancesMatchTheApprovedValues()
        {
            // User decision: crit chance varies per beast, highest on the fast strikers and casters.
            Dictionary<string, int> expected = new Dictionary<string, int>
            {
                { "thunderbird", 15 },
                { "basilisk", 12 },
                { "phoenix", 10 },
                { "griffin", 8 },
                { "tarasque", 6 },
                { "kirin", 5 },
                { "frost_wyrm", 5 },
                { "leviathan", 3 },
                { "treant", 3 },
                { "golem", 2 }
            };

            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.AreEqual(expected[species.SpeciesId], species.BaseStats.CritChance, species.SpeciesId);
            }
        }

        [Test]
        public void Validator_RejectsCritChanceOutsideAPercent_AndAcceptsZero()
        {
            BeastRosterData roster = LoadRoster();
            roster.Species[0].BaseStats.CritChance = -1;
            roster.Species[1].BaseStats.CritChance = 101;
            roster.Species[2].BaseStats.CritChance = 0;
            roster.Species[3].BaseStats.CritChance = 100;

            List<string> errors = BeastRosterValidator.Validate(roster);

            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("CritChance is -1")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("CritChance is 101")), string.Join("\n", errors));
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
                    // CritChance is a chance, not a combat stat: 0 is legal (see the crit band test).
                    int floor = stat == StatType.CritChance ? 0 : 1;
                    Assert.That(species.GetStatAtLevel(stat, 1), Is.GreaterThanOrEqualTo(floor), data.SpeciesId + " " + stat + " at level 1");
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
        public void Roster_StancesMatchTheApprovedRoles()
        {
            // Ranged: the artillery casters; Skirmisher: the fast strikers; everyone else holds the line.
            Dictionary<string, CombatStance> expected = new Dictionary<string, CombatStance>
            {
                { "phoenix", CombatStance.Ranged },
                { "kirin", CombatStance.Ranged },
                { "basilisk", CombatStance.Ranged },
                { "thunderbird", CombatStance.Skirmisher },
                { "griffin", CombatStance.Skirmisher },
                { "leviathan", CombatStance.Vanguard },
                { "golem", CombatStance.Vanguard },
                { "treant", CombatStance.Vanguard },
                { "tarasque", CombatStance.Vanguard },
                { "frost_wyrm", CombatStance.Vanguard }
            };

            foreach (SpeciesData species in LoadRoster().Species)
            {
                Assert.IsFalse(string.IsNullOrEmpty(species.Stance), species.SpeciesId + " should author its stance explicitly.");
                Assert.IsTrue(BeastRosterValidator.TryParseStance(species.Stance, out CombatStance stance), species.SpeciesId);
                Assert.AreEqual(expected[species.SpeciesId], stance, species.SpeciesId);
            }
        }

        [Test]
        public void Validator_RejectsBadStance()
        {
            BeastRosterData roster = LoadRoster();
            roster.Species[0].Stance = "Sniper";
            roster.Species[1].Stance = "ranged";
            roster.Species[2].Stance = "1";

            List<string> errors = BeastRosterValidator.Validate(roster);

            Assert.IsTrue(errors.Exists(e => e.Contains("'Sniper' is not a CombatStance name")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("'ranged' is not a CombatStance name")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("'1' is not a CombatStance name")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RefusesAnyFootprintButSingle_BeastsAreAlwaysOneTile()
        {
            BeastRosterData roster = LoadRoster();
            roster.Species[0].Footprint = "Hex7";
            roster.Species[1].Footprint = "Triangle";
            roster.Species[2].Footprint = "Single";
            roster.Species[3].Footprint = string.Empty;

            List<string> errors = BeastRosterValidator.Validate(roster);

            Assert.AreEqual(2, errors.Count, string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("Footprint 'Hex7' is not allowed")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("Footprint 'Triangle' is not allowed")), string.Join("\n", errors));
            Assert.IsTrue(Array.TrueForAll(LoadRoster().Species, s => string.IsNullOrEmpty(s.Footprint)), "the roster authors no footprint");
        }

        [Test]
        public void Validator_ParsesStanceNames_AndMissingMeansVanguard()
        {
            Assert.IsTrue(BeastRosterValidator.TryParseStance("Skirmisher", out CombatStance stance));
            Assert.AreEqual(CombatStance.Skirmisher, stance);
            Assert.IsTrue(BeastRosterValidator.TryParseStance(null, out stance));
            Assert.AreEqual(CombatStance.Vanguard, stance);
            Assert.IsTrue(BeastRosterValidator.TryParseStance(string.Empty, out stance));
            Assert.AreEqual(CombatStance.Vanguard, stance);
            Assert.IsFalse(BeastRosterValidator.TryParseStance("vanguard", out _));

            BeastRosterData roster = LoadRoster();
            roster.Species[0].Stance = null;
            Assert.IsEmpty(BeastRosterValidator.Validate(roster));
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
            GrowthRateCurve curve = new GrowthRateCurve();
            curve.Curve = curveData.ToAnimationCurve();
            curve.MaxLevel = curveData.MaxLevel;
            _created.Add(curve);

            CreatureSpeciesSO species = new CreatureSpeciesSO();
            species.SpeciesId = data.SpeciesId;
            species.BaseStats = data.BaseStats;
            species.GrowthRate = curve;
            _created.Add(species);
            return species;
        }

        /// <summary>
        /// Reads the roster JSON. In the Unity Editor the working directory is the project folder, so
        /// the project-relative path resolves directly; other runners find it by walking up from
        /// their working or base directory. Internal so other fixtures that measure against the
        /// authored roster (see <see cref="DamageFormulaTests"/>) read it the same way.
        /// </summary>
        internal static BeastRosterData LoadRoster()
        {
            string path = FindRosterFile();
            Assert.IsNotNull(path, "Could not find " + BeastRosterData.ProjectRelativePath);

            BeastRosterData roster = FieldJson.FromJson<BeastRosterData>(File.ReadAllText(path));
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
