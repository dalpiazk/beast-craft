using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Save schema 3: the region campaign (<see cref="CampaignProgress"/>, <see cref="MapRun"/>,
    /// <see cref="MapNode"/>) and <c>BeastProgress.BankedXp</c> round-trip, the 2 → 3 migration
    /// (<see cref="SaveMigrations.AddCampaign"/>), <see cref="PlayerSave.EnsureInitialized"/>'s
    /// campaign repairs, and <see cref="SaveValidator"/>'s campaign checks.
    /// </summary>
    public class CampaignSaveTests
    {
        private static readonly SaveContentCatalog Catalog = new SaveContentCatalog(
            new[] { "emberfox" }, new string[0], new string[0], new string[0], new[] { "r01", "r02" }, new[] { "seal_r01" });

        private static SaveSerializer NewSerializer(ISaveContentCatalog catalog = null)
        {
            return new SaveSerializer(new JsonUtilitySaveSerializer(), catalog);
        }

        /// <summary>A three-node map: 0 (layer 0) → 1 and 2 (layer 1).</summary>
        private static PlayerSave SaveWithRun()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", "emberfox", 12));
            save.Beasts[0].Progress.BankedXp = 250;
            save.Campaign.AddSeal("seal_r01");
            save.Campaign.Unlock("r02");
            save.Campaign.FindRegion("r01").StagesCleared = 3;
            save.Campaign.FindRegion("r01").BossCleared = true;
            save.Campaign.CurrentRegionId = "r02";
            MapRun run = save.Campaign.ActiveRun;
            run.RegionId = "r02";
            run.Stage = 1;
            run.Seed = 777;
            run.Nodes.Add(new MapNode { NodeId = 0, Layer = 0, Lane = 1, Type = MapNodeType.Battle, Level = 13, ShapeId = "squad", EncounterSeed = 5, Next = new[] { 1, 2 } });
            run.Nodes.Add(new MapNode { NodeId = 1, Layer = 1, Lane = 0, Type = MapNodeType.Elite, Level = 14, ShapeId = "elite", EncounterSeed = 6 });
            run.Nodes.Add(new MapNode { NodeId = 2, Layer = 1, Lane = 2, Type = MapNodeType.Rest, Level = 13 });
            NodeMapGenerator.Place(run.Nodes, run.RegionId, run.Seed);
            run.CurrentNodeId = 0;
            run.Cleared.Add(0);
            run.Attempts = 2;
            return save;
        }

        [Test]
        public void NewSave_StartsWithTheFirstRegionUnlocked_AndNoExpedition()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.AreEqual(4, PlayerSave.CurrentSchemaVersion);
            Assert.IsTrue(save.Campaign.IsUnlocked(CampaignProgress.StartingRegionId));
            Assert.AreEqual(1, save.Campaign.Regions.Count);
            Assert.IsEmpty(save.Campaign.Seals);
            Assert.IsFalse(save.Campaign.HasActiveRun);
            Assert.AreEqual(string.Empty, save.Campaign.ActiveRun.RegionId);
            Assert.AreEqual(-1, save.Campaign.ActiveRun.CurrentNodeId);
        }

        [Test]
        public void CampaignAndBank_RoundTrip()
        {
            SaveSerializer serializer = NewSerializer(Catalog);
            PlayerSave save = SaveWithRun();

            string json = serializer.Serialize(save);
            SaveLoadResult loaded = serializer.Deserialize(json);

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsFalse(loaded.Migrated);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual(json, serializer.Serialize(loaded.Save));

            CampaignProgress campaign = loaded.Save.Campaign;
            Assert.AreEqual(250, loaded.Save.FindBeast("b1").Progress.BankedXp);
            CollectionAssert.AreEqual(new[] { "seal_r01" }, campaign.Seals);
            Assert.AreEqual(3, campaign.FindRegion("r01").StagesCleared);
            Assert.IsTrue(campaign.FindRegion("r01").BossCleared);
            Assert.IsTrue(campaign.IsUnlocked("r02"));
            Assert.AreEqual("r02", campaign.CurrentRegionId);
            Assert.IsTrue(campaign.HasActiveRun);
            Assert.AreEqual(1, campaign.ActiveRun.Stage);
            Assert.AreEqual(777, campaign.ActiveRun.Seed);
            Assert.AreEqual(3, campaign.ActiveRun.Nodes.Count);
            Assert.AreEqual(MapNodeType.Elite, campaign.ActiveRun.Nodes[1].Type);
            Assert.AreEqual("elite", campaign.ActiveRun.Nodes[1].ShapeId);
            CollectionAssert.AreEqual(new[] { 1, 2 }, campaign.ActiveRun.Nodes[0].Next);
            Assert.IsEmpty(campaign.ActiveRun.Nodes[2].Next);
            Assert.AreEqual(0, campaign.ActiveRun.CurrentNodeId);
            CollectionAssert.AreEqual(new[] { 0 }, campaign.ActiveRun.Cleared);
            Assert.AreEqual(2, campaign.ActiveRun.Attempts);
        }

        [Test]
        public void Migration_V2ToV3_UnlocksTheFirstRegion_AndKeepsEveryBeastsLevel()
        {
            const string v2 = "{\"SchemaVersion\":2," +
                              "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":40,\"Xp\":120}," +
                              "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]}]," +
                              "\"Avatar\":{\"Level\":38,\"Xp\":40}," +
                              "\"Materials\":{\"Materials\":[],\"ClearedCells\":[],\"Pity\":[]}," +
                              "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]}";

            SaveLoadResult migrated = NewSerializer(Catalog).Deserialize(v2);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(2, migrated.SourceVersion);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, migrated.Save.SchemaVersion, "2 -> 3 -> current");
            Assert.IsEmpty(migrated.Issues, string.Join("\n", migrated.Issues));
            Assert.AreEqual(40, migrated.Save.FindBeast("b1").Progress.Level, "a beast above the starting cap keeps its level");
            Assert.AreEqual(120, migrated.Save.FindBeast("b1").Progress.Xp);
            Assert.AreEqual(0, migrated.Save.FindBeast("b1").Progress.BankedXp);
            Assert.AreEqual(38, migrated.Save.Avatar.Level);
            Assert.IsTrue(migrated.Save.Campaign.IsUnlocked(CampaignProgress.StartingRegionId));
            Assert.IsFalse(migrated.Save.Campaign.HasActiveRun);

            SaveSerializer serializer = NewSerializer(Catalog);
            string v3 = serializer.Serialize(migrated.Save);
            StringAssert.Contains("\"SchemaVersion\":" + PlayerSave.CurrentSchemaVersion, v3);
            SaveLoadResult reloaded = serializer.Deserialize(v3);
            Assert.IsTrue(reloaded.Success, reloaded.Error);
            Assert.IsFalse(reloaded.Migrated);
            Assert.AreEqual(v3, serializer.Serialize(reloaded.Save));
        }

        [Test]
        public void Migration_FromV1_RunsBothSteps()
        {
            SaveLoadResult migrated = NewSerializer().Deserialize("{\"SchemaVersion\":1,\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":4}}]}");

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.AreEqual(1, migrated.SourceVersion);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, migrated.Save.SchemaVersion);
            Assert.IsTrue(migrated.Save.Campaign.IsUnlocked(CampaignProgress.StartingRegionId));
            Assert.IsNotNull(migrated.Save.Gear);
        }

        [Test]
        public void UnplacedMap_IsPlacedOnLoad_ExactlyAsThePlacementWould()
        {
            PlayerSave placed = SaveWithRun();
            PlayerSave stored = SaveWithRun();
            foreach (MapNode node in stored.Campaign.ActiveRun.Nodes)
            {
                node.X = 0f;
                node.Y = 0f;
                node.Kind = LocationKind.Wilds;
                node.LabelKey = string.Empty;
            }

            SaveSerializer serializer = NewSerializer(Catalog);
            SaveLoadResult loaded = serializer.Deserialize(new JsonUtilitySaveSerializer().ToJson(stored));

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual(serializer.Serialize(placed), serializer.Serialize(loaded.Save));
            MapNode elite = loaded.Save.Campaign.ActiveRun.Nodes[1];
            Assert.AreEqual(LocationKind.Den, elite.Kind);
            StringAssert.StartsWith("r02/den/", elite.LabelKey);
            Assert.IsTrue(elite.IsPlaced);
        }

        [Test]
        public void Validate_ReportsALocationOffTheMap()
        {
            PlayerSave save = SaveWithRun();
            save.Campaign.ActiveRun.Nodes[0].X = 1.5f;
            save.Campaign.ActiveRun.Nodes[1].Kind = (LocationKind)99;

            List<SaveIssue> issues = SaveValidator.Validate(save, Catalog);

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.TrueForAll(i => i.Kind == SaveIssueKind.InvalidMapRun));
        }

        [Test]
        public void EnsureInitialized_RepairsTheCampaign()
        {
            PlayerSave save = SaveWithRun();
            save.Campaign.ActiveRun.Nodes[1].Next = null;
            save.Campaign.ActiveRun.Nodes.Add(null);
            save.Campaign.Regions.Add(null);

            Assert.AreEqual(3, save.EnsureInitialized());
            Assert.AreEqual(3, save.Campaign.ActiveRun.Nodes.Count);
            Assert.IsNotNull(save.Campaign.ActiveRun.Nodes[1].Next);

            save.Campaign = null;
            Assert.Greater(save.EnsureInitialized(), 0);
            Assert.IsNotNull(save.Campaign.ActiveRun);
            Assert.AreEqual(string.Empty, save.Campaign.ActiveRun.RegionId);

            save.Campaign.Seals = null;
            save.Campaign.Regions = null;
            save.Campaign.CurrentRegionId = null;
            save.Campaign.ActiveRun = null;
            Assert.AreEqual(4, save.EnsureInitialized(), "seals, regions, current region, run (a fresh run needs nothing more)");
            Assert.IsEmpty(SaveValidator.Validate(save, Catalog));
        }

        [Test]
        public void Validate_ReportsUnknownAndDuplicateCampaignIds()
        {
            PlayerSave save = SaveWithRun();
            save.Campaign.Seals.Add("seal_r01");
            save.Campaign.Seals.Add("seal_nowhere");
            save.Campaign.Regions.Add(new RegionProgress { RegionId = "r02" });
            save.Campaign.Regions.Add(new RegionProgress { RegionId = "r99", StagesCleared = -1 });
            save.Campaign.CurrentRegionId = "r77";
            save.Beasts[0].Progress.BankedXp = -5;

            List<SaveIssue> issues = SaveValidator.Validate(save, Catalog);

            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.DuplicateCampaignEntry && i.Id == "seal_r01"), string.Join("\n", issues));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.UnknownSeal && i.Id == "seal_nowhere"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.DuplicateCampaignEntry && i.Id == "r02"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.UnknownRegion && i.Id == "r99"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.UnknownRegion && i.Id == "r77"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidValue && i.Path == "Campaign.Regions[3].StagesCleared"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidValue && i.Path == "Beasts[0].Progress.BankedXp"));
            Assert.AreEqual(7, issues.Count, string.Join("\n", issues));
        }

        [Test]
        public void Validate_WithoutCampaignData_ChecksOnlyStructure()
        {
            SaveContentCatalog noCampaign = new SaveContentCatalog(new[] { "emberfox" }, null, null, null);
            PlayerSave save = SaveWithRun();
            save.Campaign.Seals.Add("any_seal");

            Assert.IsTrue(noCampaign.IsKnownRegion("r42"));
            Assert.IsFalse(noCampaign.IsKnownRegion(""));
            Assert.IsEmpty(SaveValidator.Validate(save, noCampaign));
            Assert.IsEmpty(SaveValidator.Validate(save, null));

            save.Campaign.Seals.Add("");
            Assert.AreEqual(1, SaveValidator.Validate(save, null).Count, "an empty seal id is never valid");
        }

        [Test]
        public void Validate_ReportsAnInconsistentExpedition()
        {
            PlayerSave save = SaveWithRun();
            MapRun run = save.Campaign.ActiveRun;
            run.Nodes[0].Next = new[] { 1, 2, 9 };
            run.Nodes[1].Next = new[] { 2 };
            run.Nodes[2].NodeId = 5;
            run.Nodes[2].Level = 0;
            run.CurrentNodeId = 12;
            run.NodeAttemptsNodeId = 13;
            run.Cleared.Add(0);
            run.Cleared.Add(40);

            List<SaveIssue> issues = SaveValidator.Validate(save, Catalog);

            Assert.AreEqual(8, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.TrueForAll(i => i.Kind == SaveIssueKind.InvalidMapRun || i.Kind == SaveIssueKind.InvalidValue));

            PlayerSave locked = SaveWithRun();
            locked.Campaign.Regions.RemoveAll(r => r.RegionId == "r02");
            List<SaveIssue> lockedIssues = SaveValidator.Validate(locked, Catalog);
            Assert.AreEqual(1, lockedIssues.Count, string.Join("\n", lockedIssues));
            StringAssert.Contains("not unlocked", lockedIssues[0].Message);

            PlayerSave stale = SaveWithRun();
            stale.Campaign.ActiveRun.RegionId = string.Empty;
            List<SaveIssue> staleIssues = SaveValidator.Validate(stale, Catalog);
            Assert.AreEqual(1, staleIssues.Count, string.Join("\n", staleIssues));
            Assert.AreEqual(SaveIssueKind.InvalidMapRun, staleIssues[0].Kind);

            PlayerSave empty = SaveWithRun();
            empty.Campaign.ActiveRun.Nodes.Clear();
            empty.Campaign.ActiveRun.Cleared.Clear();
            empty.Campaign.ActiveRun.CurrentNodeId = -1;
            Assert.AreEqual(1, SaveValidator.Validate(empty, Catalog).Count, "an expedition with no nodes");

            save.Campaign.ActiveRun.Clear();
            Assert.IsFalse(save.Campaign.HasActiveRun);
            Assert.IsEmpty(SaveValidator.Validate(save, Catalog));
        }
    }
}
