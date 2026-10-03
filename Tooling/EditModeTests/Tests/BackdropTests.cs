using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The journal UI kit's menu backdrop picker (#52, step 3; schema 14):
    /// <see cref="CampaignProgress.ReachedBackdropIds"/>/<see cref="CampaignProgress.SelectedBackdropId"/>,
    /// <see cref="CampaignRules.StartRun(BeastCraft.Save.PlayerSave, RegionLibrary, string, int, int, RunDifficulty)"/>
    /// marking a region's backdrop reached on first entry, the 13 to 14 migration
    /// (<see cref="SaveMigrations.AddBackdrops"/>), <see cref="SaveValidator"/>'s checks, and
    /// <see cref="BackdropPickerViewModel"/>, Settings &gt; Visuals' own view-model.
    /// </summary>
    public class BackdropTests
    {
        private RegionLibrary _regions;

        private static readonly SaveContentCatalog Catalog = new SaveContentCatalog(
            new string[0], new string[0], new string[0], new string[0], new[] { "r00", "r01", "r02" }, new string[0]);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [SetUp]
        public void SetUp()
        {
            _regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
        }

        [Test]
        public void NewSave_StartsWithOnlyHearthglenReached_AndSelected()
        {
            PlayerSave save = PlayerSave.CreateNew();

            CollectionAssert.AreEqual(new[] { CampaignProgress.TutorialRegionId }, save.Campaign.ReachedBackdropIds);
            Assert.AreEqual(CampaignProgress.TutorialRegionId, save.Campaign.SelectedBackdropId);
            Assert.IsTrue(save.Campaign.HasReachedBackdrop(CampaignProgress.TutorialRegionId));
            Assert.IsFalse(save.Campaign.HasReachedBackdrop(CampaignProgress.StartingRegionId), "r01 is unlocked but not yet entered");
        }

        [Test]
        public void MarkBackdropReached_IsIdempotent_AndSelectRefusesAnUnreachedRegion()
        {
            CampaignProgress campaign = new CampaignProgress();

            Assert.IsTrue(campaign.MarkBackdropReached("r01"));
            Assert.IsFalse(campaign.MarkBackdropReached("r01"), "already reached");
            Assert.IsFalse(campaign.MarkBackdropReached(""));
            Assert.IsFalse(campaign.MarkBackdropReached(null));
            CollectionAssert.AreEqual(new[] { CampaignProgress.TutorialRegionId, "r01" }, campaign.ReachedBackdropIds);

            Assert.IsFalse(campaign.SelectBackdrop("r02"), "r02 has not been reached");
            Assert.AreEqual(CampaignProgress.TutorialRegionId, campaign.SelectedBackdropId, "selection unchanged by a refused pick");
            Assert.IsTrue(campaign.SelectBackdrop("r01"));
            Assert.AreEqual("r01", campaign.SelectedBackdropId);
        }

        [Test]
        public void StartRun_MarksTheRegionsBackdropReached_OnlyOnce()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.IsFalse(save.Campaign.HasReachedBackdrop("r01"));

            Assert.IsTrue(CampaignRules.StartRun(save, _regions, "r01", 7).Success);
            Assert.IsTrue(save.Campaign.HasReachedBackdrop("r01"), "entering a region reaches its backdrop");
            CollectionAssert.AreEqual(new[] { CampaignProgress.TutorialRegionId, "r01" }, save.Campaign.ReachedBackdropIds);

            Assert.IsTrue(CampaignRules.Retreat(save).Success);
            Assert.IsTrue(CampaignRules.StartRun(save, _regions, "r01", 8).Success);
            CollectionAssert.AreEqual(new[] { CampaignProgress.TutorialRegionId, "r01" }, save.Campaign.ReachedBackdropIds, "a second entry does not duplicate it");
        }

        [Test]
        public void SelectedBackdrop_PersistsAcrossASaveRoundTrip()
        {
            PlayerSave save = PlayerSave.CreateNew();
            CampaignRules.StartRun(save, _regions, "r01", 7);
            Assert.IsTrue(save.Campaign.SelectBackdrop("r01"));

            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), Catalog);
            string json = serializer.Serialize(save);
            SaveLoadResult loaded = serializer.Deserialize(json);

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual("r01", loaded.Save.Campaign.SelectedBackdropId);
            CollectionAssert.AreEqual(new[] { CampaignProgress.TutorialRegionId, "r01" }, loaded.Save.Campaign.ReachedBackdropIds);
        }

        [Test]
        public void Migration_V13ToV14_DefaultsHearthglen_AndReachesEveryAlreadyUnlockedRegion()
        {
            const string v13 = "{\"SchemaVersion\":13," +
                               "\"Campaign\":{\"Seals\":[],\"Regions\":[{\"RegionId\":\"r00\",\"StagesCleared\":0,\"BossCleared\":false}," +
                               "{\"RegionId\":\"r01\",\"StagesCleared\":1,\"BossCleared\":false}],\"CurrentRegionId\":\"r01\"," +
                               "\"ActiveRun\":{\"RegionId\":\"\",\"Stage\":0,\"Seed\":0,\"Nodes\":[],\"CurrentNodeId\":-1,\"Cleared\":[],\"Attempts\":0,\"NodeAttempts\":0}}}";

            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), Catalog);
            SaveLoadResult migrated = serializer.Deserialize(v13);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(13, migrated.SourceVersion);
            Assert.AreEqual(14, migrated.Save.SchemaVersion);
            Assert.IsEmpty(migrated.Issues, string.Join("\n", migrated.Issues));
            CollectionAssert.AreEquivalent(new[] { "r00", "r01" }, migrated.Save.Campaign.ReachedBackdropIds,
                                           "both regions already on the v13 save count as reached");
            Assert.AreEqual("r00", migrated.Save.Campaign.SelectedBackdropId, "Hearthglen stays the default selection");
        }

        [Test]
        public void Migration_V13ToV14_WithNoUnlockedRegions_StillReachesHearthglen()
        {
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), Catalog);
            SaveLoadResult migrated = serializer.Deserialize("{\"SchemaVersion\":13}");

            Assert.IsTrue(migrated.Success, migrated.Error);
            CollectionAssert.AreEqual(new[] { "r00" }, migrated.Save.Campaign.ReachedBackdropIds);
            Assert.AreEqual("r00", migrated.Save.Campaign.SelectedBackdropId);
        }

        [Test]
        public void Validate_ReportsUnknownAndDuplicateReachedIds_AndAnUnreachedSelection()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.ReachedBackdropIds.Add("r01");
            save.Campaign.ReachedBackdropIds.Add("r01");
            save.Campaign.ReachedBackdropIds.Add("nowhere");
            save.Campaign.SelectedBackdropId = "r02";

            List<SaveIssue> issues = SaveValidator.Validate(save, Catalog);

            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidBackdrop && i.Id == "r01" && i.Path == "Campaign.ReachedBackdropIds[2]"),
                          string.Join("\n", issues));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidBackdrop && i.Id == "nowhere"));
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidBackdrop && i.Path == "Campaign.SelectedBackdropId" && i.Id == "r02"));
        }

        [Test]
        public void Validate_WithoutACatalog_OnlyChecksStructure()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.ReachedBackdropIds.Add("whatever-id");

            Assert.IsEmpty(SaveValidator.Validate(save, null));
        }

        [Test]
        public void BackdropPickerViewModel_ListsRowsInCampaignOrder_LockedUntilReached()
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => 1);
            session.StartWith(PlayerSave.CreateNew());
            CampaignRules.StartRun(session.Save, Content.Campaign, "r01", 7);

            BackdropPickerViewModel model = new BackdropPickerViewModel(session);

            Assert.AreEqual(12, model.Rows.Count);
            Assert.AreEqual("r00", model.Rows[0].RegionId);
            Assert.IsTrue(model.Rows[0].Reached);
            Assert.IsTrue(model.Rows[0].Selected, "Hearthglen is the default selection");
            Assert.AreEqual("r01", model.Rows[1].RegionId);
            Assert.IsTrue(model.Rows[1].Reached, "StartRun reached it");
            Assert.IsFalse(model.Rows[1].Selected);
            Assert.AreEqual("r02", model.Rows[2].RegionId);
            Assert.IsFalse(model.Rows[2].Reached);
            Assert.AreEqual(Content.Text.Get("region.r02.name"), model.Rows[2].DisplayName, "already-resolved region text, not a loc key");
        }

        [Test]
        public void BackdropPickerViewModel_Select_RefusesALockedRegion_AndPersistsAReachedOne()
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => 1);
            session.StartWith(PlayerSave.CreateNew());
            CampaignRules.StartRun(session.Save, Content.Campaign, "r01", 7);
            BackdropPickerViewModel model = new BackdropPickerViewModel(session);

            Assert.IsFalse(model.Select("r02"), "r02 has not been reached");
            Assert.IsTrue(model.Rows[0].Selected, "unchanged by a refused pick");

            int savesBefore = session.AutosaveCount;
            Assert.IsTrue(model.Select("r01"));
            Assert.Greater(session.AutosaveCount, savesBefore, "Select persists through GameSession.Autosave");
            Assert.IsTrue(model.Rows[1].Selected);
            Assert.IsFalse(model.Rows[0].Selected);
            Assert.AreEqual("r01", session.Save.Campaign.SelectedBackdropId);
        }
    }
}
