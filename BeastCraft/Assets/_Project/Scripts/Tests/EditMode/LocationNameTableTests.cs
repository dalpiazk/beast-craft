using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The map location names: the authored <c>Data/Campaign/location-names.json</c> against
    /// <c>regions.json</c>, <see cref="LocationNameTableValidator"/>, and
    /// <see cref="LocationNameTable"/> resolving <see cref="MapNode.LabelKey"/>s (with its fallback),
    /// including every key the generator draws on the real maps.
    /// </summary>
    public class LocationNameTableTests
    {
        private static readonly LocationKind[] Kinds =
        {
            LocationKind.Wilds, LocationKind.Den, LocationKind.Camp, LocationKind.TradingPost, LocationKind.Pass, LocationKind.Lair
        };

        internal static LocationNameTableData LoadNames()
        {
            return EncounterContentTests.Load<LocationNameTableData>(LocationNameTableData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredLocationNames_PassValidationAgainstTheRegions()
        {
            LocationNameTableData names = LoadNames();
            RegionLibraryData regions = CampaignMapTests.LoadRegions();

            List<string> errors = LocationNameTableValidator.Validate(names, regions);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(regions.Regions.Length, names.Regions.Length, "one name set per region");
            foreach (LocationNameSetData set in names.Regions)
            {
                foreach (LocationKind kind in Kinds)
                {
                    Assert.AreEqual(NodeMapGenerator.LabelVariants, set.NamesFor(kind).Length, set.RegionId + " " + kind);
                }
            }
        }

        [Test]
        public void AuthoredRegionsAndSeals_HaveDisplayText()
        {
            RegionLibraryData regions = CampaignMapTests.LoadRegions();
            foreach (RegionData region in regions.Regions)
            {
                Assert.IsFalse(string.IsNullOrEmpty(region.DisplayName), region.RegionId);
                Assert.IsFalse(string.IsNullOrEmpty(region.Description), region.RegionId);
            }

            foreach (SealData seal in regions.Seals)
            {
                Assert.IsFalse(string.IsNullOrEmpty(seal.DisplayName), seal.SealId);
                Assert.IsFalse(string.IsNullOrEmpty(seal.Description), seal.SealId);
            }
        }

        [Test]
        public void EveryGeneratedLabelKey_ResolvesToItsRegionsAuthoredName()
        {
            LocationNameTableData data = LoadNames();
            LocationNameTable table = LocationNameTable.Build(data);
            RegionLibrary regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
            Dictionary<string, LocationNameSetData> sets = new Dictionary<string, LocationNameSetData>(StringComparer.Ordinal);
            foreach (LocationNameSetData set in data.Regions)
            {
                sets.Add(set.RegionId, set);
            }

            int resolved = 0;
            foreach (RegionData region in regions.Regions)
            {
                for (int stage = 0; stage < region.Stages; stage++)
                {
                    for (int seed = 1; seed <= 3; seed++)
                    {
                        foreach (MapNode node in NodeMapGenerator.Generate(regions, region.RegionId, stage, seed))
                        {
                            Assert.IsTrue(LocationNameTable.TryParseLabelKey(node.LabelKey, out string regionId, out LocationKind kind, out int variant), node.LabelKey);
                            Assert.AreEqual(region.RegionId, regionId);
                            Assert.AreEqual(node.Kind, kind);

                            string name = table.Resolve(node);
                            Assert.AreEqual(sets[region.RegionId].NamesFor(kind)[variant], name, node.LabelKey);
                            Assert.AreEqual(name, table.Resolve(node.LabelKey));
                            resolved++;
                        }
                    }
                }
            }

            Assert.Greater(resolved, 1000, "every region, stage and a few seeds");
        }

        [Test]
        public void Resolve_ReadsTheVariantsNameAndFallsBackForUncoveredKeys()
        {
            LocationNameTableData data = LoadNames();
            LocationNameTable table = LocationNameTable.Build(data);

            Assert.AreEqual(data.Regions[0].Wilds[0], table.Resolve(data.Regions[0].RegionId + "/wilds/0"));
            Assert.AreEqual(data.Regions[2].TradingPost[7], table.Resolve(data.Regions[2].RegionId + "/trading_post/7"));

            Assert.AreEqual("Den", table.Resolve("r99/den/2"), "unknown region: the kind's generic name");
            Assert.AreEqual("Trading Post", table.Resolve("r01/trading_post/8"), "variant past the list");
            Assert.AreEqual("Camp", LocationNameTable.Build(null).Resolve("r01/camp/0"), "an empty table falls back for everything");

            Assert.AreEqual(string.Empty, table.Resolve((string)null));
            Assert.AreEqual(string.Empty, table.Resolve(string.Empty));
            Assert.AreEqual(string.Empty, table.Resolve("r01/castle/1"), "unknown kind key");
            Assert.AreEqual(string.Empty, table.Resolve("r01/wilds"), "missing variant");
            Assert.AreEqual(string.Empty, table.Resolve("r01/wilds/-1"), "negative variant");
            Assert.AreEqual(string.Empty, table.Resolve("r01/Wilds/1"), "kind keys are case-sensitive");
            Assert.AreEqual(string.Empty, table.Resolve("/wilds/1"), "empty region");
            Assert.AreEqual(string.Empty, table.Resolve("r01/wilds/1/2"));

            Assert.AreEqual(string.Empty, table.Resolve((MapNode)null));
            Assert.AreEqual("Lair", table.Resolve(new MapNode { Kind = LocationKind.Lair }), "an unplaced node: its kind's generic name");
            Assert.AreEqual("Pass", table.Resolve(new MapNode { Kind = LocationKind.Pass, LabelKey = "garbage" }));
        }

        [Test]
        public void LocationKindKeys_RoundTrip()
        {
            foreach (LocationKind kind in Kinds)
            {
                Assert.IsTrue(LocationKinds.TryParseKey(LocationKinds.Key(kind), out LocationKind parsed), kind.ToString());
                Assert.AreEqual(kind, parsed);
                Assert.IsFalse(string.IsNullOrEmpty(LocationNameTable.FallbackName(kind)));
            }

            Assert.IsFalse(LocationKinds.TryParseKey("tradingpost", out LocationKind _));
            Assert.IsFalse(LocationKinds.TryParseKey(null, out LocationKind _));
        }

        [Test]
        public void Validator_ReportsBrokenTables()
        {
            RegionLibraryData regions = CampaignMapTests.LoadRegions();
            LocationNameTableData data = LoadNames();
            data.Regions[0].Wilds = new[] { "One", "Two", "Three", "Four", "Five", "Six", "Seven" };
            data.Regions[1].Den[3] = string.Empty;
            data.Regions[2].Camp[0] = " Padded Camp";
            data.Regions[3].Pass[1] = new string('x', LocationNameTableValidator.MaxNameLength + 1);
            data.Regions[4].Lair[2] = data.Regions[4].Lair[1].ToUpperInvariant();
            data.Regions[5].RegionId = "r99";
            data.Regions[6].RegionId = data.Regions[7].RegionId;
            data.Regions[8].TradingPost = null;

            List<string> errors = LocationNameTableValidator.Validate(data, regions);

            string all = string.Join("\n", errors);
            StringAssert.Contains("Region 'r01' Wilds has 7 names; it needs exactly 8", all);
            StringAssert.Contains("Region 'r02' Den[3] is empty", all);
            StringAssert.Contains("Region 'r03' Camp[0] ' Padded Camp' has leading or trailing whitespace", all);
            StringAssert.Contains("Region 'r04' Pass[1]", all);
            StringAssert.Contains("the most is 24", all);
            StringAssert.Contains("Region 'r05' Lair[2]", all);
            StringAssert.Contains("repeats another name in the region", all);
            StringAssert.Contains("Region 'r99' is not a region in regions.json", all);
            StringAssert.Contains("Region 'r08' appears more than once", all);
            StringAssert.Contains("Region 'r06' has no location names", all);
            StringAssert.Contains("Region 'r07' has no location names", all);
            StringAssert.Contains("Region 'r09' TradingPost has 0 names", all);

            Assert.IsNotEmpty(LocationNameTableValidator.Validate(null));
            LocationNameTableData wrongVersion = LoadNames();
            wrongVersion.SchemaVersion = 7;
            Assert.AreEqual(1, LocationNameTableValidator.Validate(wrongVersion).Count);
            Assert.IsEmpty(LocationNameTableValidator.Validate(LoadNames()), "valid without the region cross-check too");
        }

        [Test]
        public void RegionLibrarySO_BuildsTheNameTableOnceAndResetsIt()
        {
            RegionLibrarySO asset = ScriptableObject.CreateInstance<RegionLibrarySO>();
            try
            {
                asset.LocationNames = LoadNames();
                LocationNameTable first = asset.Names;
                Assert.AreSame(first, asset.Names, "built once, then shared");
                Assert.AreEqual(asset.LocationNames.Regions[0].Den[1], first.Resolve(asset.LocationNames.Regions[0].RegionId + "/den/1"));

                asset.LocationNames = new LocationNameTableData();
                asset.ResetRuntimeCaches();
                Assert.AreNotSame(first, asset.Names);
                Assert.AreEqual("Den", asset.Names.Resolve("r01/den/1"), "the re-imported (empty) table falls back");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
