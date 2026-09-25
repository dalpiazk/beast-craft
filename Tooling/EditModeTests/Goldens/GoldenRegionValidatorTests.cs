using System;
using System.Collections.Generic;
using System.Text;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Golden error text for <see cref="RegionLibraryValidator"/> over the MAINLINE campaign (the
    /// ten regions r01-r10 of <c>regions.json</c>): the authored data, and a fixed set of broken
    /// variants covering every rule of the contiguous 1-100 band (ids, unlock order, levels, stages,
    /// map rules, shape weights, gates, bosses, seals and caps). Captured on the validator before the
    /// post-game region (r11) existed, so the post-game pass must leave every mainline message,
    /// and their order, byte-identical.
    /// </summary>
    public class GoldenRegionValidatorTests
    {
        private const string GoldenPath = "region-validator.golden.txt";

        /// <summary>The mainline regions, in campaign order.</summary>
        internal static readonly string[] MainlineRegionIds = { "r01", "r02", "r03", "r04", "r05", "r06", "r07", "r08", "r09", "r10" };

        /// <summary>The authored library cut down to the mainline regions (the post-game ones dropped).</summary>
        internal static RegionLibraryData LoadMainline()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            List<RegionData> mainline = new List<RegionData>();
            foreach (RegionData region in data.Regions)
            {
                if (Array.IndexOf(MainlineRegionIds, region.RegionId) >= 0)
                {
                    mainline.Add(region);
                }
            }

            data.Regions = mainline.ToArray();
            return data;
        }

        /// <summary>Every broken variant, by name; each mutates a fresh mainline library.</summary>
        internal static List<KeyValuePair<string, Action<RegionLibraryData>>> Cases()
        {
            return new List<KeyValuePair<string, Action<RegionLibraryData>>>
            {
                Case("authored", d => { }),
                Case("mixed", d =>
                {
                    d.Regions[0].RegionId = "start";
                    d.Regions[2].MinLevel = 25;
                    d.Regions[3].RequiresRegionId = "r09";
                    d.Regions[4].ShapeWeights = new[] { new ShapeWeightData { ShapeId = "nope", Weight = 1 } };
                    d.Regions[5].BossTemplateId = "no_boss";
                    d.Seals[6].LevelCap = 50;
                    d.Seals[1].DisplayName = string.Empty;
                    d.Seals[2].Description = "  ";
                    d.MapRules.Layers = 2;
                    d.MapRules.NodeWeights = new[] { new NodeWeightData { Type = "Gate", Weight = 5 } };
                }),
                Case("first region requires another", d => d.Regions[0].RequiresRegionId = "r02"),
                Case("level gap and short end", d =>
                {
                    d.Regions[4].MinLevel = 43;
                    d.Regions[9].MaxLevel = 99;
                }),
                Case("max level past 100", d => d.Regions[9].MaxLevel = 101),
                Case("duplicate and malformed ids", d =>
                {
                    d.Regions[3].RegionId = "r03";
                    d.Regions[6].RegionId = "R7";
                }),
                Case("null region", d => d.Regions[5] = null),
                Case("stages, gates and boss", d =>
                {
                    d.Regions[1].Stages = 0;
                    d.Regions[2].GateTemplateIds = new[] { "a", "b", "c", "d" };
                    d.Regions[3].GateTemplateIds = new[] { string.Empty, "no_gate" };
                    d.Regions[4].BossTemplateId = string.Empty;
                }),
                Case("shape weights", d =>
                {
                    d.Regions[1].ShapeWeights = new ShapeWeightData[0];
                    d.Regions[2].ShapeWeights = new[]
                    {
                        new ShapeWeightData { ShapeId = "squad", Weight = 1 }, new ShapeWeightData { ShapeId = "squad", Weight = 0 },
                        new ShapeWeightData { ShapeId = string.Empty, Weight = 1 }
                    };
                }),
                Case("region map rules override", d =>
                {
                    d.Regions[6].MapRules = new MapRulesData
                    {
                        Layers = 2,
                        Lanes = 0,
                        Paths = 0,
                        EliteMinLayer = 0,
                        RestLayer = 5,
                        MinElites = 3,
                        MaxElites = 1,
                        MaxShops = -1,
                        EliteLevelOffset = -1,
                        EliteShapeId = "nope",
                        NodeWeights = new[] { new NodeWeightData { Type = "Elite", Weight = 0 }, new NodeWeightData { Type = "Elite", Weight = 1 } }
                    };
                }),
                Case("seals", d =>
                {
                    d.Regions[2].BossRewardSealId = "seal_nope";
                    d.Regions[4].BossRewardSealId = "seal_r04";
                    d.Seals[7].LevelCap = 75;
                    d.Seals[8].LevelCap = 101;
                    d.Seals[0].SealId = "Seal 1";
                    d.Seals[9] = null;
                }),
                Case("duplicate seal", d => d.Seals[5].SealId = "seal_r05"),
                Case("caps", d =>
                {
                    d.StartingLevelCap = 5;
                    d.LevelCapMargin = -1;
                }),
                Case("no seals", d => d.Seals = new SealData[0]),
                Case("no regions", d => d.Regions = new RegionData[0])
            };
        }

        [Test]
        public void MainlineValidation_MatchesTheGoldenErrorText()
        {
            EncounterLibraryData encounters = EncounterContentTests.LoadEncounterLibrary();
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, Action<RegionLibraryData>> entry in Cases())
            {
                RegionLibraryData data = LoadMainline();
                entry.Value(data);
                text.Append("## ").Append(entry.Key).Append('\n');
                foreach (string error in RegionLibraryValidator.Validate(data, encounters))
                {
                    text.Append(error).Append('\n');
                }

                text.Append("## ").Append(entry.Key).Append(" (no encounter library)\n");
                foreach (string error in RegionLibraryValidator.Validate(data))
                {
                    text.Append(error).Append('\n');
                }
            }

            GoldenFiles.AssertMatches(GoldenPath, text.ToString());
        }

        private static KeyValuePair<string, Action<RegionLibraryData>> Case(string name, Action<RegionLibraryData> mutate)
        {
            return new KeyValuePair<string, Action<RegionLibraryData>>(name, mutate);
        }
    }
}
