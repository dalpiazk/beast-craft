using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The core loop's view-models over the real content: New Game, Continue and autosave (with
    /// an in-memory storage, and the <c>.bak</c> fallback on disk), the region map (its spatial
    /// layout's determinism, every location's state, taps), the encounter screen (the free full
    /// preview, team and consumable validation, the suggestion banner and its toggle), and a whole
    /// node battle fought headless through to the consolidated results, won and lost.
    /// </summary>
    public class CampaignScreensTests
    {
        private const int MapSeed = 424242;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(ISaveStorage storage = null)
        {
            GameSession session = new GameSession(Content, storage ?? new MemorySaveStorage(), () => MapSeed);
            session.NewGame();
            return session;
        }

        // ------------------------------------------------------------------ save flow

        [Test]
        public void NewGame_CreatesTheStarterSave_StartsRegionOne_AndAutosaves()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = new GameSession(Content, storage, () => MapSeed);
            Assert.IsFalse(session.HasSave);
            Assert.IsFalse(session.Autosave(AutosaveReason.Background), "nothing to save before a game");

            session.NewGame();

            Assert.IsTrue(session.HasSave);
            Assert.AreEqual(1, session.AutosaveCount);
            Assert.AreEqual(AutosaveReason.NewGame, session.LastAutosaveReason);
            PlayerSave save = session.Save;
            CollectionAssert.AreEqual(StarterSave.Species, save.Beasts.ConvertAll(b => b.Progress.SpeciesId));
            Assert.IsTrue(save.Beasts.TrueForAll(b => b.Progress.Level == 1 && b.Skills.GetEquipped(0) != null), "level 1, default loadouts worn");
            Assert.AreEqual(1, save.Avatar.Level);
            Assert.IsNotNull(save.AvatarSkills.Actives.GetEquipped(0), "the avatar's default loadout");
            Assert.IsTrue(save.Campaign.HasActiveRun);
            Assert.AreEqual("r01", save.Campaign.ActiveRun.RegionId);
            Assert.AreEqual(0, save.Campaign.ActiveRun.Stage);
            Assert.AreEqual(MapSeed, save.Campaign.ActiveRun.Seed);
        }

        [Test]
        public void Continue_LoadsTheSavedGame()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession first = NewSession(storage);
            first.Save.Gold = 77;
            first.Autosave(AutosaveReason.Background);

            GameSession second = new GameSession(Content, storage, () => 1);
            Assert.IsTrue(second.HasSave);
            LoadOutcome outcome = second.Continue();

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.IsFalse(outcome.FromBackup);
            Assert.IsNull(outcome.Message);
            Assert.AreEqual(77, second.Save.Gold);
            Assert.AreEqual(MapSeed, second.Save.Campaign.ActiveRun.Seed, "the expedition in progress is kept, not restarted");
        }

        [Test]
        public void Continue_FallsBackToTheBackup_WithAMessage_WhenTheMainSaveIsCorrupt()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession first = NewSession(storage);
            first.Save.Gold = 5;
            first.Autosave(AutosaveReason.Results);
            storage.Corrupt(GameSession.SlotName, "{\"SchemaVersion\": 6, \"Beasts\": [");

            GameSession second = new GameSession(Content, storage, () => 1);
            LoadOutcome outcome = second.Continue();

            Assert.IsTrue(outcome.Success);
            Assert.IsTrue(outcome.FromBackup);
            StringAssert.Contains("backup was loaded", outcome.Message);
            Assert.AreEqual(0, second.Save.Gold, "the backup is the save before the last write");
            Assert.IsTrue(FileSaveStorage.LooksLikeCompleteJsonObject(storage.Peek(GameSession.SlotName)), "the restored backup is written back as the main save");
        }

        [Test]
        public void Continue_OnDisk_UsesTheBakFile_WhenTheMainFileIsTorn()
        {
            string dir = Path.Combine(Path.GetTempPath(), "beastcraft-screens-" + Guid.NewGuid().ToString("N"));
            try
            {
                FileSaveStorage storage = new FileSaveStorage(SaveLocations.DefaultDirectory(dir));
                GameSession first = NewSession(storage);
                first.Autosave(AutosaveReason.Results);
                Assert.IsTrue(File.Exists(storage.GetBackupPath(GameSession.SlotName)), "a second write keeps a .bak");
                File.WriteAllText(storage.GetSlotPath(GameSession.SlotName), "{\"Schema");

                LoadOutcome outcome = new GameSession(Content, new FileSaveStorage(SaveLocations.DefaultDirectory(dir)), () => 1).Continue();

                Assert.IsTrue(outcome.Success);
                Assert.IsTrue(outcome.FromBackup);
                StringAssert.Contains("could not be read", outcome.Message);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void Continue_WithNothingLoadable_Fails_AndKeepsNoGame()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            storage.Corrupt(GameSession.SlotName, "not json");
            GameSession session = new GameSession(Content, storage, () => 1);

            LoadOutcome outcome = session.Continue();

            Assert.IsFalse(outcome.Success);
            StringAssert.Contains("could not be loaded", outcome.Message);
            Assert.IsNull(session.Save);
        }

        [Test]
        public void Settings_AreSavedBesideTheGame_AndReadBack()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = new GameSession(Content, storage, () => 1);
            SettingsViewModel settings = new SettingsViewModel(session.Settings, session.SaveSettings);
            settings.Change(SettingsViewModel.TeamSuggestions);
            settings.Change(SettingsViewModel.Effects);

            Assert.AreEqual(2, settings.Saves);
            PlayerSettings read = new GameSession(Content, storage, () => 1).Settings;
            Assert.IsFalse(read.TeamSuggestionsEnabled);
            Assert.AreEqual(EffectsIntensity.Reduced, read.EffectsIntensity);
            Assert.AreEqual("Off", settings.Rows()[SettingsViewModel.TeamSuggestions].Value);
        }

        [Test]
        public void Title_OffersContinueOnlyWithASave_AndAsksBeforeReplacingIt()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            TitleViewModel fresh = new TitleViewModel(new GameSession(Content, storage, () => MapSeed));
            Assert.IsFalse(fresh.CanContinue);
            Assert.IsFalse(fresh.NewGameNeedsConfirm);
            fresh.NewGame();
            Assert.IsTrue(fresh.CanContinue);
            Assert.IsTrue(new TitleViewModel(new GameSession(Content, storage, () => 1)).NewGameNeedsConfirm);
        }

        // ------------------------------------------------------------------ region map

        [Test]
        public void MapLayout_IsDeterministic_SpatialAndNeverAGrid()
        {
            GameSession session = NewSession();
            MapRun run = session.Save.Campaign.ActiveRun;
            MapLayout a = MapLayout.Of(run.Nodes, run.Seed);
            MapLayout b = MapLayout.Of(run.Nodes, run.Seed);
            MapLayout other = MapLayout.Of(run.Nodes, run.Seed + 1);

            foreach (MapNode node in run.Nodes)
            {
                Assert.AreEqual(a.Positions[node.NodeId], b.Positions[node.NodeId], "same map, same seed, same place");
                Vec2 at = a.Positions[node.NodeId];
                Assert.That(at.X, Is.InRange(0f, MapLayout.WorldWidth));
                Assert.That(at.Y, Is.InRange(0f, a.WorldHeight));
            }

            Assert.AreEqual(a.Paths.Count, b.Paths.Count);
            for (int i = 0; i < a.Paths.Count; i++)
            {
                CollectionAssert.AreEqual(a.Paths[i].Points, b.Paths[i].Points);
            }

            Assert.AreEqual(a.Blobs.Count, b.Blobs.Count);
            Assert.IsTrue(run.Nodes.Exists(n => !a.Positions[n.NodeId].Equals(other.Positions[n.NodeId])), "another seed lays it out differently");

            // Rows climb the map: every link goes up, from the trailhead at the bottom to the pass or lair at the top.
            foreach (MapNode node in run.Nodes)
            {
                foreach (int next in node.Next)
                {
                    Assert.Less(a.Positions[next].Y, a.Positions[node.NodeId].Y);
                }
            }

            Assert.Greater(a.Trailhead.Y, a.Positions[run.Nodes.Find(n => n.Layer == 0).NodeId].Y);

            // Not a grid: a lane's locations do not share one x, and a row's do not share one y.
            List<MapNode> laneZero = run.Nodes.FindAll(n => n.Lane == 0);
            Assert.Greater(laneZero.Count, 2);
            Assert.IsTrue(laneZero.Exists(n => Math.Abs(a.Positions[n.NodeId].X - a.Positions[laneZero[0].NodeId].X) > 10f));
            List<MapNode> rowTwo = run.Nodes.FindAll(n => n.Layer == 2);
            Assert.IsTrue(rowTwo.Count < 2 || rowTwo.Exists(n => Math.Abs(a.Positions[n.NodeId].Y - a.Positions[rowTwo[0].NodeId].Y) > 5f));

            // Every trail is a curve from its location to the next.
            foreach (MapPathView path in a.Paths)
            {
                Vec2 from = path.FromId < 0 ? a.Trailhead : a.Positions[path.FromId];
                Assert.AreEqual(from, path.Points[0]);
                Assert.AreEqual(a.Positions[path.ToId].X, path.Points[path.Points.Count - 1].X, 0.01f);
                Assert.AreEqual(MapLayout.PathSamples + 1, path.Points.Count);
            }
        }

        [Test]
        public void Map_OnANewGame_ShowsRowZeroReachable_TheRestLocked_AndTheRegionHeader()
        {
            GameSession session = NewSession();
            MapViewModel map = new MapViewModel(session);
            MapRun run = session.Save.Campaign.ActiveRun;

            foreach (MapNodeView node in map.Nodes)
            {
                MapNode data = run.Find(node.NodeId);
                Assert.AreEqual(data.Layer == 0 ? MapNodeState.Reachable : MapNodeState.Locked, node.State, "node " + node.NodeId);
                Assert.IsFalse(string.IsNullOrEmpty(node.Name));
            }

            Assert.IsTrue(map.Paths.TrueForAll(p => p.FromId < 0 ? p.State == MapPathState.Open : p.State == MapPathState.Faint));
            RegionHeaderView header = map.Header;
            RegionData region = Content.Campaign.GetRegion("r01");
            Assert.AreEqual(region.DisplayName, header.Name);
            Assert.AreEqual("Lv " + region.MinLevel + "-" + region.MaxLevel, header.LevelBand);
            Assert.AreEqual("Stage 1 of " + region.Stages, header.StageText);
            Assert.AreEqual(Content.Campaign.GetSeal(region.BossRewardSealId).DisplayName, header.SealName);
            Assert.IsFalse(header.SealOwned);
            Assert.AreEqual(0f, header.SealProgress);
            Assert.AreEqual(Content.Campaign.StartingLevelCap, header.BindingLimit, "the binding limit before any seal");
        }

        [Test]
        public void Map_Taps_PreviewABattle_RefuseALockedOne_AndSayCampAndTraderAreComingSoon()
        {
            GameSession session = NewSession();
            MapViewModel map = new MapViewModel(session);
            MapNodeView first = map.Reachable()[0];
            Assert.AreEqual(MapTapKind.Preview, map.Tap(first.NodeId).Kind);
            MapNodeView locked = map.Nodes.Find(n => n.State == MapNodeState.Locked);
            MapTapResult refused = map.Tap(locked.NodeId);
            Assert.AreEqual(MapTapKind.Refused, refused.Kind);
            StringAssert.Contains(locked.Name, refused.Message);

            // Stand on the parent of a Camp (and of a Trader, when the map has one) and tap it.
            MapRun run = session.Save.Campaign.ActiveRun;
            foreach (MapNodeType type in new[] { MapNodeType.Rest, MapNodeType.Shop })
            {
                MapNode target = run.Nodes.Find(n => n.Type == type);
                if (target == null)
                {
                    Assert.AreEqual(MapNodeType.Shop, type, "every map has its camps");
                    continue;
                }

                MapNode parent = run.Nodes.Find(n => Array.IndexOf(n.Next, target.NodeId) >= 0);
                run.CurrentNodeId = parent.NodeId;
                map.Refresh();
                MapTapResult soon = map.Tap(target.NodeId);
                Assert.AreEqual(MapTapKind.ComingSoon, soon.Kind, type.ToString());
                StringAssert.Contains("soon", soon.Message);
            }
        }

        [Test]
        public void Map_AfterAClear_ShowsWhereThePlayerStands_WhatIsBypassed_AndTheWalkedTrail()
        {
            GameSession session = NewSession();
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode first = CampaignRules.Choices(run)[0];
            run.Cleared.Add(first.NodeId);
            run.CurrentNodeId = first.NodeId;

            MapViewModel map = new MapViewModel(session);

            Assert.AreEqual(MapNodeState.Current, map.Find(first.NodeId).State);
            foreach (MapNode sibling in run.Nodes.FindAll(n => n.Layer == 0 && n.NodeId != first.NodeId))
            {
                Assert.AreEqual(MapNodeState.Bypassed, map.Find(sibling.NodeId).State);
            }

            foreach (int next in first.Next)
            {
                Assert.AreEqual(MapNodeState.Reachable, map.Find(next).State);
                Assert.AreEqual(MapPathState.Open, map.Paths.Find(p => p.FromId == first.NodeId && p.ToId == next).State);
            }

            Assert.AreEqual(MapPathState.Walked, map.Paths.Find(p => p.FromId < 0 && p.ToId == first.NodeId).State);
            Assert.AreEqual(MapTapKind.Refused, map.Tap(first.NodeId).Kind, "already cleared");
            Assert.AreEqual(map.Find(first.NodeId).Position.Y, map.FocusY, "the view centres on where the player stands");
        }

        // ------------------------------------------------------------------ encounter screen

        [Test]
        public void Encounter_ShowsTheFullPreviewForFree()
        {
            GameSession session = NewSession();
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);

            Assert.IsNull(encounter.Error);
            EncounterPlan plan = CampaignRules.PlanFor(node, Content.Encounters, Content.Enemies);
            int total = 0;
            foreach (EnemyGroupView group in encounter.Enemies)
            {
                total += group.Count;
                Assert.AreEqual(node.Level, group.Level);
                Assert.IsNotNull(group.Stance, "the full preview shows stances");
                Assert.IsNotNull(group.ArtKey, "each enemy draws with its region art");
            }

            Assert.AreEqual(plan.Enemies.Count, total);
            Assert.AreEqual(plan.Arena.ToString(), encounter.Arena);
            Assert.AreEqual(node.Level, encounter.Level);
            BattleLayoutEntryData layout = BattleLayouts.Pick(Content.Battle.Layouts, "r01", plan.Arena, CampaignRules.BattleSeed(node, 0));
            Assert.AreEqual(layout?.ArtKey, encounter.BackdropArtKey, "the battlefield the battle will really stand on");
            if (layout != null)
            {
                Assert.AreEqual(layout.Cells.Length, encounter.Obstacles);
                Assert.IsFalse(string.IsNullOrEmpty(encounter.LayoutName));
            }

            Assert.AreEqual("Sun 1", EncounterViewModel.LayoutNameOf("backdrop/r01/sun0/medium"));
        }

        [Test]
        public void Encounter_TeamSelect_ValidatesPartySize_AndOneConsumable()
        {
            GameSession session = NewSession();
            ConsumableInventory.TryAdd(session.Save, "fury_draught", 2, 5);
            ConsumableInventory.TryAdd(session.Save, "iron_tonic", 1, 5);
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);

            Assert.AreEqual(3, GameSession.PartySize, "three beasts beside the Beastbinder");
            Assert.AreEqual(GameSession.PartySize, encounter.Team.Count, "starts with a full party");
            CollectionAssert.AreEqual(new[] { "b1", "b2", "b3" }, encounter.Team);
            Assert.IsTrue(encounter.CanStart);
            Assert.IsFalse(encounter.ToggleMember("b4", out string full));
            StringAssert.Contains("full", full);
            Assert.IsFalse(encounter.ToggleMember("nobody", out _));

            Assert.IsTrue(encounter.ToggleMember("b2", out _));
            Assert.IsTrue(encounter.ToggleMember("b5", out _));
            CollectionAssert.AreEqual(new[] { "b1", "b3", "b5" }, encounter.Team, "a new member joins at the back");
            Assert.AreEqual(2, encounter.Owned.Find(m => m.BeastId == "b5").PartyIndex);
            Assert.IsFalse(encounter.Owned.Find(m => m.BeastId == "b2").Selected);

            foreach (string id in new[] { "b1", "b3", "b5" })
            {
                encounter.ToggleMember(id, out _);
            }

            Assert.IsFalse(encounter.CanStart);
            StringAssert.Contains("at least one", encounter.Validate().Message);
            Assert.IsNull(encounter.Start(out string refused));
            Assert.IsNotNull(refused);
            encounter.ToggleMember("b3", out _);
            Assert.IsTrue(encounter.CanStart, "one beast is enough");

            Assert.AreEqual(2, encounter.Consumables.Count);
            encounter.ToggleConsumable("fury_draught");
            encounter.ToggleConsumable("iron_tonic");
            Assert.AreEqual("iron_tonic", encounter.SelectedConsumable, "one per battle: the new pick replaces the old");
            Assert.AreEqual(1, encounter.Consumables.FindAll(c => c.Selected).Count);
            encounter.ToggleConsumable("iron_tonic");
            Assert.IsNull(encounter.SelectedConsumable);
            encounter.ToggleConsumable("smoke_bomb");
            Assert.IsNull(encounter.SelectedConsumable, "not held");
        }

        [Test]
        public void Encounter_OffersTheSuggestionBanner_AfterThreeLosses_UnlessSwitchedOff_AndDismissible()
        {
            GameSession session = NewSession();
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode node = CampaignRules.Choices(run)[0];

            run.NodeAttemptsNodeId = node.NodeId;
            run.NodeAttempts = 2;
            Assert.IsNull(new EncounterViewModel(session, node.NodeId).Suggestion, "two losses: not yet");

            run.NodeAttempts = 3;
            EncounterViewModel offered = new EncounterViewModel(session, node.NodeId);
            Assert.IsNotNull(offered.Suggestion);
            Assert.AreEqual(3, offered.Suggestion.Losses);
            Assert.AreEqual(GameSession.PartySize, offered.Suggestion.BeastIds.Count);
            Assert.AreEqual(3, offered.Attempt);

            List<string> suggested = new List<string>(offered.Suggestion.BeastIds);
            offered.ApplySuggestion();
            Assert.IsNull(offered.Suggestion, "the banner goes once applied");
            CollectionAssert.AreEqual(suggested, offered.Team);
            Assert.IsNull(new EncounterViewModel(session, node.NodeId).Suggestion, "dismissed for the session");

            session.DismissedSuggestions.Clear();
            session.Settings.TeamSuggestionsEnabled = false;
            Assert.IsNull(new EncounterViewModel(session, node.NodeId).Suggestion, "the settings toggle is respected");
        }

        // ------------------------------------------------------------------ battle and results

        [Test]
        public void NodeBattle_Won_ClearsTheNode_PaysOut_AndConsolidatesTheResults()
        {
            GameSession session = NewSession();
            foreach (string id in new[] { "b1", "b2", "b3" })
            {
                // Four levels over the location's level 1: a sure win that still pays some XP (the falloff's 5%).
                session.Save.FindBeast(id).Progress.Level = 5;
            }

            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode node = CampaignRules.Choices(run)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            int saves = session.AutosaveCount;

            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            Assert.AreEqual(saves + 1, session.AutosaveCount);
            Assert.AreEqual(AutosaveReason.NodeEntry, session.LastAutosaveReason);
            Assert.AreEqual("r01", battle.Setup.Encounter.RegionId, "fought in the node's region: its backdrops and obstacles");
            Assert.AreEqual(CampaignRules.BattleSeed(node, 0), battle.Setup.Seed);
            Assert.AreEqual(battle.Layout, battle.Run.Result.Layout, "the preview's battlefield is the battle's");
            Assert.AreEqual("phoenix", battle.SpeciesByUnit["beast:b2"]);
            Assert.IsTrue(battle.Setup.IncludeAvatar, "the Beastbinder fights beside the team, as the difficulty was calibrated");
            Assert.IsNotNull(battle.Run.Avatar);
            Assert.AreEqual(CampaignAvatar.UnitId, battle.Run.Avatar.Id);
            Assert.AreEqual(3, battle.Run.Avatar.Skills.Skills.Count, "its default arts");
            Assert.Greater(battle.Run.Avatar.Stats.Attack, 1, "the simulator's stat fixture, not the no-stats avatar");

            ResultsViewModel results = battle.Complete();

            Assert.AreEqual(BattleOutcome.PlayerVictory, results.Outcome);
            Assert.IsTrue(results.Victory);
            Assert.AreEqual("Victory!", results.Title);
            Assert.AreEqual(CampaignOutcome.Cleared, results.MapOutcome);
            Assert.AreEqual(saves + 2, session.AutosaveCount);
            Assert.AreEqual(AutosaveReason.Results, session.LastAutosaveReason);
            Assert.IsTrue(run.IsCleared(node.NodeId));
            Assert.AreEqual(node.NodeId, run.CurrentNodeId);

            Assert.AreEqual(GameSession.PartySize, results.Team.Count);
            foreach (BeastResultRow row in results.Team)
            {
                OwnedBeast beast = session.Save.FindBeast(row.BeastId);
                Assert.AreEqual(beast.Progress.Level, row.LevelAfter);
                Assert.AreEqual(5, row.LevelBefore);
                Assert.Greater(row.XpGained, 0);
                Assert.That(row.FractionAfter, Is.InRange(0f, 1f));
                Assert.IsTrue(row.LevelsGained > 0 || row.FractionAfter > row.FractionBefore, row.Name + "'s bar moved");
            }

            Assert.Greater(results.BenchXp, 0, "the bench shares the XP");
            Assert.Greater(results.AvatarXp, 0, "the Beastbinder earns XP too");
            Assert.Greater(results.Gold, 0);
            Assert.AreEqual(session.Save.Gold, results.GoldTotal);
            Assert.IsTrue(results.FirstClear, "the first clear of its cell");
            Assert.IsNotEmpty(results.Drops);
            Assert.That(results.Notes, Has.Some.Contains("cleared"));
            Assert.Throws<InvalidOperationException>(() => battle.Complete(), "paid out once only");

            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(MapNodeState.Current, map.Find(node.NodeId).State);
        }

        [Test]
        public void NodeBattle_Lost_KeepsTheNode_CountsTheLoss_AndOffersTheRetry()
        {
            GameSession session = NewSession();
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode node = CampaignRules.Choices(run)[0];
            node.Level = 60;
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            encounter.ToggleMember("b1", out _);
            encounter.ToggleMember("b2", out _);

            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            ResultsViewModel results = battle.Complete();

            Assert.AreEqual(BattleOutcome.EnemyVictory, results.Outcome);
            Assert.AreEqual("Defeat", results.Title);
            Assert.AreEqual(CampaignOutcome.Lost, results.MapOutcome);
            Assert.AreEqual(1, results.Losses);
            Assert.AreEqual(0, results.Gold, "no clear, no gold");
            StringAssert.Contains("try", results.RetryNote);
            Assert.AreEqual(1, results.Team.Count);
            Assert.IsFalse(run.IsCleared(node.NodeId));
            Assert.AreEqual(AutosaveReason.Results, session.LastAutosaveReason, "the loss count is saved");

            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(MapNodeState.Reachable, map.Find(node.NodeId).State, "retry from the node");
            EncounterViewModel retry = new EncounterViewModel(session, node.NodeId);
            Assert.AreEqual(1, retry.Attempt);
            Assert.AreEqual(CampaignRules.BattleSeed(node, 1), retry.Battle.Seed, "a new battle seed for the retry");
            CollectionAssert.AreEqual(new[] { "b3" }, retry.Team, "the last team is remembered");
        }

        [Test]
        public void NodeBattle_CompletedTwice_Throws_AndPaysOutOnce()
        {
            GameSession session = NewSession();
            foreach (string id in new[] { "b1", "b2", "b3", "b4" })
            {
                session.Save.FindBeast(id).Progress.Level = 5;
            }

            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            NodeBattle battle = new EncounterViewModel(session, node.NodeId).Start(out string error);
            Assert.IsNotNull(battle, error);
            Assert.IsFalse(battle.IsCompleted);
            ResultsViewModel results = battle.Complete();
            Assert.IsTrue(battle.IsCompleted);

            string before = new JsonSaveSerializer().ToJson(session.Save);
            int saves = session.AutosaveCount;
            Assert.Throws<InvalidOperationException>(() => battle.Complete());
            Assert.AreEqual(before, new JsonSaveSerializer().ToJson(session.Save), "no second pay-out: gold, XP, drops and the map unchanged");
            Assert.AreEqual(saves, session.AutosaveCount);
            Assert.AreEqual(results.GoldTotal, session.Save.Gold);
        }

        [Test]
        public void NodeBattle_HandsBackOnce_EvenReentrantly()
        {
            GameSession session = NewSession();
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            Assert.IsFalse(encounter.Battle.TryHandBack(), "nothing to hand back before the battle begins");

            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            int handed = 0;

            // The battle screen's HandBack: claim the hand-back, then run the results callback, which
            // (re-entrantly) tries again, as a second Back or tap in the same frame would.
            void HandBack()
            {
                if (!battle.TryHandBack())
                {
                    return;
                }

                handed++;
                HandBack();
                battle.Complete();
            }

            HandBack();
            HandBack();
            Assert.AreEqual(1, handed);
            Assert.IsTrue(battle.IsCompleted);
        }

        [Test]
        public void NodeBattle_SpendsTheConsumable_AsTheBattleBegins()
        {
            GameSession session = NewSession();
            ConsumableInventory.TryAdd(session.Save, "fury_draught", 1, 5);
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            encounter.ToggleConsumable("fury_draught");

            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            Assert.AreEqual(0, ConsumableInventory.Quantity(session.Save, "fury_draught"));
            Assert.AreEqual(1, battle.Complete().ConsumablesSpent);
        }

        [Test]
        public void Encounter_ForANodeThatCannotBeEntered_ReportsWhy()
        {
            GameSession session = NewSession();
            MapNode top = session.Save.Campaign.ActiveRun.Nodes.Find(n => n.Type == MapNodeType.Gate || n.Type == MapNodeType.Boss);
            EncounterViewModel encounter = new EncounterViewModel(session, top.NodeId);
            Assert.IsNull(encounter.Battle);
            StringAssert.Contains("cannot be reached", encounter.Error);
            Assert.IsFalse(encounter.CanStart);
        }
    }
}
