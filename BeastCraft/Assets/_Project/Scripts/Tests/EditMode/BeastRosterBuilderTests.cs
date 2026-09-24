using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary><see cref="BeastRosterBuilder"/>: the roster JSON to species/curve mapping shared by the importer and everything outside the Editor.</summary>
    public class BeastRosterBuilderTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        [Test]
        public void BuildAll_MapsEveryJsonOwnedField()
        {
            BeastRosterData roster = BeastRosterTests.LoadRoster();

            List<CreatureSpeciesSO> species = BeastRosterBuilder.BuildAll(roster, out Dictionary<string, GrowthRateCurve> curves);
            _created.AddRange(species);
            _created.AddRange(curves.Values);

            Assert.AreEqual(roster.GrowthCurves.Length, curves.Count);
            foreach (GrowthCurveData data in roster.GrowthCurves)
            {
                GrowthRateCurve curve = curves[data.CurveId];
                Assert.AreEqual(data.CurveId, curve.CurveId);
                Assert.AreEqual(data.MaxLevel, curve.MaxLevel);
                Assert.AreEqual(BeastRosterValidator.ScaleAtLevel(data, 50), curve.GetScaleAtLevel(50), 1e-4);
            }

            Assert.AreEqual(roster.Species.Length, species.Count);
            for (int i = 0; i < species.Count; i++)
            {
                SpeciesData data = roster.Species[i];
                CreatureSpeciesSO beast = species[i];

                Assert.AreEqual(data.SpeciesId, beast.SpeciesId);
                Assert.AreEqual(data.SpeciesId, beast.name);
                Assert.AreEqual(data.DisplayName, beast.DisplayName);
                Assert.AreEqual(data.Description, beast.Description);
                Assert.AreEqual(data.BaseStats, beast.BaseStats);
                Assert.AreSame(curves[data.GrowthCurveId], beast.GrowthRate);
                Assert.AreEqual(data.Elements.Length, beast.Elements.Length);
                for (int e = 0; e < data.Elements.Length; e++)
                {
                    Assert.AreEqual(data.Elements[e], beast.Elements[e].ToString());
                }

                BeastRosterValidator.TryParseStance(data.Stance, out CombatStance stance);
                Assert.AreEqual(stance, beast.Stance);
                Assert.AreEqual(UnitFootprint.Single, beast.Footprint);
            }
        }

        [Test]
        public void ParseFootprint_NamesOnly_DefaultsToSingle()
        {
            Assert.AreEqual(UnitFootprint.Single, BeastRosterBuilder.ParseFootprint(null));
            Assert.AreEqual(UnitFootprint.Single, BeastRosterBuilder.ParseFootprint(string.Empty));
            Assert.AreEqual(UnitFootprint.Single, BeastRosterBuilder.ParseFootprint("Single"));
            Assert.AreEqual(UnitFootprint.Hex7, BeastRosterBuilder.ParseFootprint("Hex7"));
            Assert.AreEqual(UnitFootprint.Triangle, BeastRosterBuilder.ParseFootprint("Triangle"));
            Assert.AreEqual(UnitFootprint.Single, BeastRosterBuilder.ParseFootprint("hex7"));
            Assert.AreEqual(UnitFootprint.Single, BeastRosterBuilder.ParseFootprint("2"));
        }

        [Test]
        public void BuildAll_NullRoster_IsEmpty()
        {
            Assert.IsEmpty(BeastRosterBuilder.BuildAll(null, out Dictionary<string, GrowthRateCurve> curves));
            Assert.IsEmpty(curves);
        }
    }
}
