using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The discovery layer (docs/design/kinship-discovery.md): the points-of-interest layout per
    /// discovery seed, the fog's reveal rules and their persistence across replays, visiting each
    /// kind, the caches' fixed rewards, region completion and its reward, the content validator and
    /// the schema-8 migration.
    /// </summary>
    public class DiscoveryTests
    {
        private static readonly string[] DiscoveryRegions = { "r01", "r02", "r03", "r04", "r05", "r06" };

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static DiscoveryContent Discovery
        {
            get { return Content.Discovery; }
        }

        private static string Describe(IEnumerable<PointOfInterest> points)
        {
            StringBuilder text = new StringBuilder();
            foreach (PointOfInterest p in points)
            {
                text.Append(p.PoiId).Append(' ').Append(p.Kind).Append(' ').Append(p.HalfRow).Append(',').Append(p.Col).Append(" L").Append(p.Level).Append(' ')
                    .Append(p.RefId).Append(' ').Append(p.Seed).Append('\n');
            }

            return text.ToString();
        }

        [TestCase("r01", 12345)]
        [TestCase("r04", 777)]
        [TestCase("r06", 2026)]
        public void Layout_IsDeterministicPerSeed_AndDiffersAcrossSeeds(string regionId, int seed)
        {
            string first = Describe(PoiLayout.ForRegion(Discovery.Library, Content.Campaign, regionId, seed));
            string again = Describe(PoiLayout.ForRegion(Discovery.Library, Content.Campaign, regionId, seed));
            string other = Describe(PoiLayout.ForRegion(Discovery.Library, Content.Campaign, regionId, seed + 1));
            Assert.IsNotEmpty(first);
            Assert.AreEqual(first, again);
            Assert.AreNotEqual(first, other);
            Assert.IsEmpty(PoiLayout.ForRegion(Discovery.Library, Content.Campaign, regionId, 0), "no seed yet, no points");
            Assert.IsEmpty(PoiLayout.ForRegion(Discovery.Library, Content.Campaign, "r07", seed), "r07 has no discovery layer");
        }

        [Test]
        public void Layout_KeepsDensity_OffThePath_AndDealsEachEntryOnce()
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                foreach (string regionId in DiscoveryRegions)
                {
                    RegionDiscoveryData data = Discovery.Library.Region(regionId);
                    FogGrid grid = DiscoveryRules.GridOf(Discovery, regionId);
                    List<PointOfInterest> points = PoiLayout.ForRegion(Discovery.Library, Content.Campaign, regionId, seed);
                    Assert.AreEqual(points.Count, points.Select(p => p.PoiId).Distinct().Count());
                    List<string> refs = points.Where(p => p.Kind != PoiKind.Vista).Select(p => p.RefId).ToList();
                    Assert.AreEqual(refs.Count, refs.Distinct().Count(), regionId + " seed " + seed + ": each lore entry, cache, shrine and site once");
                    foreach (IGrouping<int, PointOfInterest> stage in points.GroupBy(p => p.Stage))
                    {
                        string at = regionId + " s" + stage.Key + " seed " + seed;
                        Assert.That(stage.Count(), Is.InRange(data.MinPois, data.MaxPois), at);
                        Assert.LessOrEqual(stage.Count(p => p.Kind == PoiKind.Vista), PoiLayout.VistasPerStage, at);
                        CollectionAssert.AreEquivalent(Discovery.Library.SitesOn(regionId, stage.Key).Select(s => s.SiteId),
                                                       stage.Where(p => p.Kind == PoiKind.KinshipSite).Select(p => p.RefId), at);
                        foreach (PointOfInterest point in stage)
                        {
                            Assert.AreEqual(1, point.HalfRow % 2, at + ": between two map rows, never on one");
                            Assert.AreEqual(0, point.Col % 2, at + ": between lanes (or at an edge), never on one");
                            Assert.That(point.HalfRow, Is.InRange(1, grid.HalfRows - 2), at);
                            foreach (PointOfInterest other in stage.Where(o => o != point))
                            {
                                double dr = (point.HalfRow - other.HalfRow) / PoiLayout.MinSpacing;
                                double dc = (point.Col - other.Col) / PoiLayout.MinSpacing;
                                Assert.GreaterOrEqual((dr * dr) + (dc * dc), 1.0, at + ": spaced");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Density_RisesFromTwoToThreeEarly_ToThreeToFourByR04()
        {
            Assert.AreEqual((2, 3), (Discovery.Library.Region("r01").MinPois, Discovery.Library.Region("r01").MaxPois));
            Assert.AreEqual((2, 3), (Discovery.Library.Region("r02").MinPois, Discovery.Library.Region("r02").MaxPois));
            foreach (string regionId in new[] { "r04", "r05", "r06" })
            {
                Assert.AreEqual((3, 4), (Discovery.Library.Region(regionId).MinPois, Discovery.Library.Region(regionId).MaxPois), regionId);
            }

            Assert.IsNull(Discovery.Library.Region("r00"), "Hearthglen is shown fully revealed");
        }

        [Test]
        public void DiscoverySeed_IsAssignedOnce_AndAReplayNeverMovesThePoints()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            Assert.AreEqual(0, save.Campaign.FindRegion("r01").DiscoverySeed);
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 41).Success);
            int seed = save.Campaign.FindRegion("r01").DiscoverySeed;
            Assert.AreNotEqual(0, seed);
            string before = Describe(DiscoveryRules.PointsOnMap(save, Discovery));
            string firstMap = string.Join(",", save.Campaign.ActiveRun.Nodes.Select(n => n.Layer + ":" + n.Lane + ":" + n.Type));

            CampaignRules.Retreat(save);
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 9999).Success);
            Assert.AreEqual(seed, save.Campaign.FindRegion("r01").DiscoverySeed, "assigned once");
            Assert.AreEqual(before, Describe(DiscoveryRules.PointsOnMap(save, Discovery)), "a new map, the same points");
            Assert.AreNotEqual(Describe(Array.Empty<PointOfInterest>()), before);
            Assert.AreNotEqual(firstMap, string.Join(",", save.Campaign.ActiveRun.Nodes.Select(n => n.Layer + ":" + n.Lane + ":" + n.Type)), "the map itself was redrawn");
        }

        [Test]
        public void Fog_StartsAtTheTrailhead_LiftsAroundClears_AndNeverComesBackOnAReplay()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 5);
            RegionProgress progress = save.Campaign.FindRegion("r01");
            FogGrid grid = DiscoveryRules.GridOf(Discovery, "r01");
            StageFog fog = progress.FindFog(0);
            MapRun run = save.Campaign.ActiveRun;
            foreach (MapNode node in run.Nodes)
            {
                bool shown = MapFog.IsRevealed(fog, grid, node);
                Assert.AreEqual(node.Layer == 0 || node.Layer == grid.MapRows, shown, "only the trailhead row and the pass are in sight at first: node " + node.NodeId);
            }

            int atStart = MapFog.CountRevealed(fog, grid);
            MapNode first = CampaignRules.Choices(run)[0];
            CampaignRules.ResolveBattle(save, Content.Campaign, first.NodeId, BattleOutcome.PlayerVictory);
            foreach (int next in first.Next)
            {
                Assert.IsTrue(MapFog.IsRevealed(fog, grid, run.Find(next)), "a cleared location reveals where it leads");
            }

            Assert.Greater(MapFog.CountRevealed(fog, grid), atStart);
            Assert.AreEqual(0, fog.DeepestLayer);
            KinshipTests.WalkTo(save, 4);
            int walked = MapFog.CountRevealed(fog, grid);
            string cells = fog.Cells;

            // Retreat and replay on a new map: nothing fogs again.
            CampaignRules.Retreat(save);
            CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 777);
            StageFog replay = progress.FindFog(0);
            Assert.AreSame(fog, replay);
            Assert.GreaterOrEqual(MapFog.CountRevealed(replay, grid), walked);
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                Assert.IsTrue(!MapFog.Get(cells, cell) || MapFog.IsRevealed(replay, cell), "cell " + cell + " never re-fogs");
            }

            Assert.AreEqual(4, replay.DeepestLayer, "the row reached persists");
        }

        [Test]
        public void Fog_BitSet_RoundTripsAndIgnoresJunk()
        {
            string hex = string.Empty;
            foreach (int bit in new[] { 0, 3, 4, 17, 188 })
            {
                hex = MapFog.Set(hex, bit);
            }

            for (int bit = 0; bit < 200; bit++)
            {
                Assert.AreEqual(bit == 0 || bit == 3 || bit == 4 || bit == 17 || bit == 188, MapFog.Get(hex, bit), "bit " + bit);
            }

            Assert.IsFalse(MapFog.Get("zz", 1));
            Assert.IsTrue(MapFog.Get(MapFog.Set("zz", 5), 5), "unreadable digits read as 0 and are rewritten");
        }

        [Test]
        public void Vista_LiftsABigChunk_Shrine_AndLore_RecordForLater()
        {
            (PlayerSave save, List<PointOfInterest> points) = OpenStage("r01", out int seed);
            RegionProgress progress = save.Campaign.FindRegion("r01");
            FogGrid grid = DiscoveryRules.GridOf(Discovery, "r01");
            foreach (PointOfInterest point in points.Where(p => p.Kind != PoiKind.KinshipSite))
            {
                RevealCell(progress, point);
                int before = MapFog.CountRevealed(progress.FindFog(point.Stage), grid);
                DiscoveryResult visit = DiscoveryRules.Visit(save, Discovery, point.PoiId);
                Assert.IsTrue(visit.Success, visit.Error + " seed " + seed);
                Assert.IsTrue(progress.HasFound(point.PoiId));
                Assert.IsFalse(DiscoveryRules.Visit(save, Discovery, point.PoiId).Success, "found once");
                switch (point.Kind)
                {
                    case PoiKind.Vista:
                        Assert.Greater(visit.CellsRevealed, 20);
                        Assert.AreEqual(before + visit.CellsRevealed, MapFog.CountRevealed(progress.FindFog(point.Stage), grid));
                        break;
                    case PoiKind.Shrine:
                        CollectionAssert.Contains(save.Discovery.GroveUnlockIds, Discovery.Library.Shrine(point.RefId).GroveUnlockId);
                        break;
                    case PoiKind.LoreStone:
                        CollectionAssert.Contains(save.Discovery.LoreIds, point.RefId);
                        Assert.IsNotNull(Discovery.Library.Lore(point.RefId).Text);
                        break;
                }
            }
        }

        [Test]
        public void Caches_PayTheirFixedRewards_TheSameForEveryone()
        {
            foreach (CacheData cache in Discovery.Library.Data.Caches)
            {
                PlayerSave a = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
                PlayerSave b = KinshipTests.TrioSave(new[] { "treant", "kirin", "thunderbird" }, 40);
                b.Gold = 17;
                DiscoveryResult ra = new DiscoveryResult();
                DiscoveryResult rb = new DiscoveryResult();
                DiscoveryRules.GrantCache(a, Discovery, cache.CacheId, ra);
                DiscoveryRules.GrantCache(b, Discovery, cache.CacheId, rb);
                Assert.AreEqual(cache.Gold, ra.Gold, cache.CacheId);
                Assert.AreEqual(ra.Gold, rb.Gold);
                Assert.AreEqual(17 + cache.Gold, b.Gold);
                CollectionAssert.AreEqual(ra.Looks, rb.Looks);
                Assert.AreEqual(string.Join(",", ra.Materials.Select(m => m.MaterialId + "x" + m.Quantity)), string.Join(",", rb.Materials.Select(m => m.MaterialId + "x" + m.Quantity)));
                foreach (CacheMaterialData material in cache.Materials)
                {
                    Assert.AreEqual(material.Quantity, a.Materials.GetCount(material.MaterialId), cache.CacheId);
                }

                if (!string.IsNullOrEmpty(cache.Look))
                {
                    CollectionAssert.AreEqual(new[] { cache.Look }, ra.Looks);
                    Assert.AreEqual(CosmeticLibrary.SourceDiscovery, Content.Economy.Cosmetics.GetOption(cache.Look).Source);
                }
            }
        }

        [Test]
        public void Rewards_AreSmall_NoCombatPower()
        {
            foreach (CacheData cache in Discovery.Library.Data.Caches)
            {
                Assert.LessOrEqual(cache.Gold, 300, cache.CacheId);
                foreach (CacheMaterialData material in cache.Materials)
                {
                    Assert.AreEqual("essence_shard", material.MaterialId, cache.CacheId + ": the lowest material tier only");
                    Assert.LessOrEqual(material.Quantity, 2, cache.CacheId);
                }
            }
        }

        [Test]
        public void Completion_CountsRowsAndPoints_AndPaysItsRewardOnce()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 8);
            RegionCompletion start = DiscoveryRules.Completion(save, Discovery, "r01");
            Assert.AreEqual(4 * 11, start.LocationsTotal);
            Assert.AreEqual(0, start.LocationsExplored);
            Assert.Greater(start.PoisTotal, 0);
            Assert.AreEqual(0, start.Percent);
            Assert.IsNull(DiscoveryRules.Completion(save, Discovery, "r07"), "no discovery layer, no percentage");

            KinshipTests.WalkTo(save, 2);
            Assert.AreEqual(3, DiscoveryRules.Completion(save, Discovery, "r01").LocationsExplored, "rows 0-2 walked");

            RegionProgress progress = save.Campaign.FindRegion("r01");
            progress.StagesCleared = 3;
            progress.BossCleared = true;
            foreach (PointOfInterest point in DiscoveryRules.PointsOf(save, Discovery, "r01"))
            {
                progress.MarkFound(point.PoiId);
            }

            RegionCompletion done = DiscoveryRules.Completion(save, Discovery, "r01");
            Assert.AreEqual(100, done.Percent);
            int gold = save.Gold;
            CompletionReward reward = DiscoveryRules.TryComplete(save, Discovery, "r01");
            Assert.IsNotNull(reward);
            Assert.AreEqual("avatar_cape/mosstrail_cape", reward.Look);
            Assert.IsTrue(save.Cosmetics.Has(reward.Look));
            Assert.AreEqual(gold + Discovery.Library.Region("r01").CompletionGold, save.Gold);
            Assert.IsNull(DiscoveryRules.TryComplete(save, Discovery, "r01"), "once");

            progress.FoundPoiIds.RemoveAt(0);
            Assert.That(DiscoveryRules.Completion(save, Discovery, "r01").Percent, Is.InRange(90, 99), "100 only when everything is found");
        }

        [Test]
        public void ShippedContent_Validates_AndTheValidatorCatchesMistakes()
        {
            HashSet<string> species = new HashSet<string>(KinshipRules.RosterOrder(Content.Species));
            HashSet<string> materials = new HashSet<string>(Content.SkillLibrary.Materials.Select(m => m.MaterialId));
            CosmeticLibraryData cosmetics = ReadJson<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath);
            EncounterLibraryDataHolder encounters = new EncounterLibraryDataHolder();
            List<string> ok = DiscoveryLibraryValidator.Validate(Discovery.Library.Data, Content.Regions, encounters.Data, species, materials, cosmetics);
            CollectionAssert.IsEmpty(ok, string.Join("\n", ok));

            DiscoveryLibraryData broken = ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath);
            broken.KinshipSites[0].Preferred = new[] { "dragon" };
            broken.KinshipSites[1].Stage = 9;
            broken.KinshipSites[2].TemplateId = "boss_nope";
            broken.KinshipSites[3].BondCondition = "stance:Healer";
            broken.Regions[0].MaxPois = 8;
            broken.Caches[0].Materials = new[] { new CacheMaterialData { MaterialId = "gold_bar", Quantity = 1 } };
            broken.Shrines[1].GroveUnlockId = broken.Shrines[0].GroveUnlockId;
            string errors = string.Join("\n", DiscoveryLibraryValidator.Validate(broken, Content.Regions, encounters.Data, species, materials, cosmetics));
            StringAssert.Contains("'dragon'", errors);
            StringAssert.Contains("Stage 9", errors);
            StringAssert.Contains("boss_nope", errors);
            StringAssert.Contains("stance:Healer", errors);
            StringAssert.Contains("drawable points", errors);
            StringAssert.Contains("known skill-library material", errors);
            StringAssert.Contains("GroveUnlockId", errors);
        }

        [Test]
        public void Migration_7To8_SeedsRegions_KeepsExploredGround_AndTouchesNoBeast()
        {
            PlayerSave v7 = TestSaves.SixStarters(Content, 12);
            v7.Campaign.FindRegion("r01").StagesCleared = 2;
            CampaignRules.StartRun(v7, Content.Campaign, "r01", 2, 314);
            KinshipTests.WalkTo(v7, 3);
            List<int> cleared = new List<int>(v7.Campaign.ActiveRun.Cleared);
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer());
            string json = serializer.Serialize(v7).Replace("\"SchemaVersion\":8", "\"SchemaVersion\":7");
            json = System.Text.RegularExpressions.Regex.Replace(json, ",\"DiscoverySeed\":-?\\d+,\"Fog\":\\[.*?\\],\"FoundPoiIds\":\\[\\],\"Completed\":false", string.Empty);
            json = json.Substring(0, json.IndexOf(",\"Discovery\":", StringComparison.Ordinal)) + "}";
            StringAssert.DoesNotContain("DiscoverySeed", json);

            SaveLoadResult loaded = serializer.Deserialize(json);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(7, loaded.SourceVersion);
            PlayerSave save = loaded.Save;
            RegionProgress r01 = save.Campaign.FindRegion("r01");
            Assert.AreEqual(6, save.Beasts.Count);
            Assert.IsEmpty(save.Discovery.ClaimedKinshipIds);
            Assert.AreEqual(LootRollerSeed(314), r01.DiscoverySeed, "the expedition in progress's map seed");
            FogGrid grid = DiscoveryRules.GridOf(Discovery, "r01");
            Assert.AreEqual(grid.CellCount, MapFog.CountRevealed(r01.FindFog(0), grid), "stages cleared before fog stay explored");
            Assert.AreEqual(grid.CellCount, MapFog.CountRevealed(r01.FindFog(1), grid));
            foreach (int nodeId in cleared)
            {
                Assert.IsTrue(MapFog.IsRevealed(r01.FindFog(2), grid, save.Campaign.ActiveRun.Find(nodeId)), "the expedition's ground stays seen");
            }

            Assert.AreEqual(3, r01.FindFog(2).DeepestLayer);
            Assert.AreEqual(serializer.Serialize(serializer.Deserialize(json).Save), serializer.Serialize(save), "deterministic");

            // A region with no expedition takes a seed from its id alone.
            PlayerSave idle = TestSaves.SixStarters(Content, 12);
            string idleJson = serializer.Serialize(idle).Replace("\"SchemaVersion\":8", "\"SchemaVersion\":7");
            PlayerSave idleSave = serializer.Deserialize(idleJson.Substring(0, idleJson.IndexOf(",\"Discovery\":", StringComparison.Ordinal)) + "}").Save;
            Assert.AreEqual(SaveMigrations.AddDiscovery.MigratedDiscoverySeed("r01"), idleSave.Campaign.FindRegion("r01").DiscoverySeed);
        }

        /// <summary>A shipped content file, read fresh (the tests may break it).</summary>
        internal static T ReadJson<T>(string projectRelativePath) where T : class
        {
            return FieldJson.FromJson<T>(System.IO.File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), projectRelativePath)));
        }

        private static int LootRollerSeed(int runSeed)
        {
            return Math.Max(1, Progression.LootRoller.DeriveSeed(runSeed, CampaignRules.DiscoverySeedStream));
        }

        /// <summary>A trio save on r01's first stage map with every point's cell lifted (to visit them all).</summary>
        private static (PlayerSave, List<PointOfInterest>) OpenStage(string regionId, out int seed)
        {
            seed = 23;
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            CampaignRules.StartRun(save, Content.Campaign, regionId, 0, seed);
            return (save, DiscoveryRules.PointsOnMap(save, Discovery));
        }

        private static void RevealCell(RegionProgress progress, PointOfInterest point)
        {
            FogGrid grid = DiscoveryRules.GridOf(Discovery, point.RegionId);
            MapFog.Reveal(progress.FogOf(point.Stage), grid.Index(point.HalfRow, point.Col));
        }

        /// <summary>The shipped encounter library's data (read once).</summary>
        private sealed class EncounterLibraryDataHolder
        {
            public Encounters.EncounterLibraryData Data { get; } = ReadJson<Encounters.EncounterLibraryData>(Encounters.EncounterLibraryData.ProjectRelativePath);
        }
    }
}
