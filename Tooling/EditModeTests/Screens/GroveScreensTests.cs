using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Npc;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// D4's screens (docs/design/grove.md, "D4 -- Grove screens"): the Grove hub's four sub
    /// view-models (Glade, Garden, Board, Npc), the encounter screen's "Soothe" option, the beast
    /// detail screen's colour forms, and the whole-sprite tint fallback (<see cref="ColourFormPresentation"/>).
    /// Every action here is a thin, autosaving wrapper over the Core rules already covered by
    /// SoothingAndColourFormTests and the Grove/Garden/Expeditions rules tests -- these tests check the
    /// view-model reads the right rows and the actions call through correctly, not the rules themselves.
    /// </summary>
    public class GroveScreensTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(ManualGameClock clock = null)
        {
            return TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => 424242, clock ?? new ManualGameClock(T0, TimeSpan.FromHours(1000))));
        }

        // ------------------------------------------------------------------ Glade

        [Test]
        public void GladeViewModel_ListsHabitatsAndBeasts_TierAndCooldownState()
        {
            GameSession session = NewSession();
            GladeViewModel model = new GladeViewModel(session);

            Assert.AreEqual(Content.GroveLibrary.Data.Habitats.Length, model.Habitats.Count);
            Assert.IsTrue(model.Habitats.All(h => !h.Unlocked), "a fresh save has unlocked no habitat yet");
            Assert.AreEqual(session.Save.Beasts.Count, model.Beasts.Count);
            GladeBeastRow first = model.Beasts[0];
            Assert.AreEqual(0, first.Tier);
            Assert.IsTrue(first.CanFeed);
            Assert.IsTrue(first.CanPlay);
            Assert.AreEqual(0, first.PendingGifts);
            Assert.IsNull(first.TintHex, "no colour form worn yet");
        }

        [Test]
        public void GladeViewModel_Feed_GrantsXpAndCooldownsUntilTomorrow_Autosaves()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(1000));
            GameSession session = NewSession(clock);
            GladeViewModel model = new GladeViewModel(session);
            string beastId = session.Save.Beasts[0].BeastId;
            int saves = session.AutosaveCount;

            GladeActionOutcome result = model.Feed(beastId);

            Assert.IsTrue(result.Success, result.Message);
            Assert.Greater(session.AutosaveCount, saves);
            GladeBeastRow row = model.Beasts.Single(b => b.BeastId == beastId);
            Assert.AreEqual(Content.GroveLibrary.Data.FeedXp, row.Xp);
            Assert.IsFalse(row.CanFeed, "cooldown until tomorrow");
            Assert.IsTrue(row.CanPlay, "feed and play cooldowns are independent");

            GladeActionOutcome refused = model.Feed(beastId);
            Assert.IsFalse(refused.Success);

            clock.Advance(TimeSpan.FromHours(Content.GroveLibrary.Data.DailyCooldownHours));
            model.Refresh();
            Assert.IsTrue(model.Beasts.Single(b => b.BeastId == beastId).CanFeed);
        }

        [Test]
        public void GladeViewModel_CollectGiftAndCollectAll()
        {
            GameSession session = NewSession();
            string beastId = session.Save.Beasts[0].BeastId;
            session.Save.Grove.AffinityOf(beastId).PendingGifts.Add(new GiftInstance { ItemKind = "lore", ItemId = "affinity_lore_phoenix" });
            session.Save.Grove.AffinityOf(beastId).PendingGifts.Add(new GiftInstance { ItemKind = "lore", ItemId = "affinity_lore_phoenix" });
            GladeViewModel model = new GladeViewModel(session);
            Assert.AreEqual(2, model.Beasts.Single(b => b.BeastId == beastId).PendingGifts);

            GladeActionOutcome one = model.CollectGift(beastId);
            Assert.IsTrue(one.Success);
            Assert.AreEqual(1, model.Beasts.Single(b => b.BeastId == beastId).PendingGifts);

            GladeActionOutcome all = model.CollectAllGifts(beastId);
            Assert.IsTrue(all.Success);
            Assert.AreEqual(0, model.Beasts.Single(b => b.BeastId == beastId).PendingGifts);

            GladeActionOutcome none = model.CollectGift(beastId);
            Assert.IsFalse(none.Success);
        }

        [Test]
        public void GladeViewModel_PlaceAndRemoveDecor_OnceHabitatUnlocked()
        {
            GameSession session = NewSession();
            session.Save.Grove.HabitatsUnlocked.Add("mossy_glade");
            session.Save.Grove.UnlockedDecorIds.Add("firefly_lantern");
            GladeViewModel model = new GladeViewModel(session);
            model.SelectHabitat("mossy_glade");

            Assert.AreEqual("mossy_glade", model.SelectedHabitatId);
            Assert.AreEqual(Content.GroveLibrary.Habitat("mossy_glade").SlotCount, model.Slots.Count);
            Assert.IsTrue(model.Slots.All(s => s.DecorId == null));
            CollectionAssert.Contains(model.AvailableDecor.Select(d => d.DecorId), "firefly_lantern");

            GladeActionOutcome placed = model.PlaceDecor("firefly_lantern");
            Assert.IsTrue(placed.Success, placed.Message);
            Assert.AreEqual(1, model.Slots.Count(s => s.DecorId == "firefly_lantern"));
            Assert.IsFalse(model.AvailableDecor.Any(d => d.DecorId == "firefly_lantern"), "placed decor is no longer offered to place again");

            GladeActionOutcome removed = model.RemoveDecor("firefly_lantern");
            Assert.IsTrue(removed.Success);
            Assert.IsTrue(model.Slots.All(s => s.DecorId == null));
            CollectionAssert.Contains(model.AvailableDecor.Select(d => d.DecorId), "firefly_lantern", "still owned, just put away");
        }

        // ------------------------------------------------------------------ Garden

        [Test]
        public void GardenViewModel_Plots_PlantHarvestAndCraft()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(1000));
            GameSession session = NewSession(clock);
            GardenViewModel model = new GardenViewModel(session);

            Assert.AreEqual(Content.GardenLibrary.Data.PlotCount, model.Plots.Count);
            CollectionAssert.Contains(model.AvailableSeeds.Select(s => s.SeedId), "sunpetal", "a starter seed is unlocked from the start");

            GardenActionOutcome planted = model.Plant(0, "sunpetal");
            Assert.IsTrue(planted.Success, planted.Message);
            Assert.AreEqual("sunpetal", model.Plots[0].SeedId);
            Assert.IsFalse(model.Plots[0].Ready);

            GardenActionOutcome tooSoon = model.Harvest(0);
            Assert.IsFalse(tooSoon.Success);

            SeedSpeciesData seed = Content.GardenLibrary.Seed("sunpetal");
            clock.Advance(TimeSpan.FromHours(seed.GrowthHours));
            model.Refresh();
            Assert.IsTrue(model.Plots[0].Ready);

            string selfVariety = Content.GardenLibrary.Cross("sunpetal", "sunpetal")?.ResultVarietyId;
            GardenActionOutcome harvested = model.Harvest(0);
            Assert.IsTrue(harvested.Success, harvested.Message);
            Assert.IsNull(model.Plots[0].SeedId, "empty again");
            Assert.AreEqual(1, model.Inventory.Single(i => i.ItemId == selfVariety).Quantity);
            CollectionAssert.Contains(model.Herbarium.Where(h => h.Discovered).Select(h => h.VarietyId), selfVariety);
        }

        [Test]
        public void GardenViewModel_PickForCross_TwoReadyPlots_HarvestsTheHybrid()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(1000));
            GameSession session = NewSession(clock);
            GardenViewModel model = new GardenViewModel(session);

            // Find two starter seeds whose pair (not a self-pair) is a curated cross.
            List<SeedSpeciesData> starters = Content.GardenLibrary.Data.Seeds.Where(s => s.Source == "starter").ToList();
            SeedSpeciesData a = null;
            SeedSpeciesData b = null;
            foreach (SeedSpeciesData x in starters)
            {
                foreach (SeedSpeciesData y in starters)
                {
                    if (x.SeedId != y.SeedId && Content.GardenLibrary.Cross(x.SeedId, y.SeedId) != null)
                    {
                        a = x;
                        b = y;
                        break;
                    }
                }

                if (a != null)
                {
                    break;
                }
            }

            Assert.IsNotNull(a, "at least two starter seeds cross-pollinate (content check)");
            model.Plant(0, a.SeedId);
            model.Plant(1, b.SeedId);
            clock.Advance(TimeSpan.FromHours(Math.Max(a.GrowthHours, b.GrowthHours)));
            model.Refresh();

            GardenActionOutcome pick1 = model.PickForCross(0);
            Assert.IsTrue(pick1.Success);
            Assert.AreEqual(0, model.CrossFirstPlotId);

            string cross = Content.GardenLibrary.Cross(a.SeedId, b.SeedId).ResultVarietyId;
            GardenActionOutcome pick2 = model.PickForCross(1);
            Assert.IsTrue(pick2.Success, pick2.Message);
            Assert.AreEqual(-1, model.CrossFirstPlotId);
            Assert.IsNull(model.Plots[0].SeedId);
            Assert.IsNull(model.Plots[1].SeedId);
            Assert.AreEqual(1, model.Inventory.Single(i => i.ItemId == cross).Quantity);
        }

        [Test]
        public void GardenViewModel_Craft_RefusesWithoutInputs_SucceedsOnceHeld()
        {
            GameSession session = NewSession();
            RecipeData recipe = Content.GardenLibrary.Data.Recipes[0];
            GardenViewModel model = new GardenViewModel(session);
            RecipeRow before = model.Recipes.Single(r => r.RecipeId == recipe.RecipeId);
            Assert.IsFalse(before.CanCraft);

            GardenActionOutcome refused = model.Craft(recipe.RecipeId);
            Assert.IsFalse(refused.Success);

            foreach (RecipeInputData input in recipe.Inputs)
            {
                session.Save.Grove.Items.Add(input.VarietyId, input.Count);
            }

            model.Refresh();
            Assert.IsTrue(model.Recipes.Single(r => r.RecipeId == recipe.RecipeId).CanCraft);
            GardenActionOutcome crafted = model.Craft(recipe.RecipeId);
            Assert.IsTrue(crafted.Success, crafted.Message);
            foreach (RecipeInputData input in recipe.Inputs)
            {
                Assert.AreEqual(0, session.Save.Grove.Items.GetCount(input.VarietyId));
            }
        }

        // ------------------------------------------------------------------ Board

        [Test]
        public void BoardViewModel_Destinations_LockedAvailableAwayReady_SendAndCollect()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(1000));
            GameSession session = NewSession(clock);
            BoardViewModel model = new BoardViewModel(session);

            DestinationRow start = model.Destinations.Single(d => d.DestinationId == "nearby_meadow");
            Assert.AreEqual(DestinationState.Available, start.State);
            DestinationData destination = Content.ExpeditionLibrary.Destination("nearby_meadow");
            Assert.IsTrue(model.Destinations.Any(d => d.State == DestinationState.Locked), "a habitat-gated destination is locked on a fresh save");

            string beastId = session.Save.Beasts[0].BeastId;
            BoardActionOutcome sent = model.Send("nearby_meadow", new[] { beastId });
            Assert.IsTrue(sent.Success, sent.Message);
            Assert.AreEqual(DestinationState.Away, model.Destinations.Single(d => d.DestinationId == "nearby_meadow").State);
            CollectionAssert.Contains(model.Destinations.Single(d => d.DestinationId == "nearby_meadow").AwayBeastNames, session.BeastName(session.Save.FindBeast(beastId)));

            BoardActionOutcome tooSoon = model.Collect("nearby_meadow");
            Assert.IsFalse(tooSoon.Success);

            clock.Advance(TimeSpan.FromHours(destination.DurationHours));
            model.Refresh();
            Assert.AreEqual(DestinationState.Ready, model.Destinations.Single(d => d.DestinationId == "nearby_meadow").State);

            BoardActionOutcome collected = model.Collect("nearby_meadow");
            Assert.IsTrue(collected.Success, collected.Message);
            Assert.AreEqual(DestinationState.Available, model.Destinations.Single(d => d.DestinationId == "nearby_meadow").State);
        }

        [Test]
        public void BoardViewModel_Beasts_StayAvailable_SendingDoesNotRemoveThemFromTheList()
        {
            GameSession session = NewSession();
            BoardViewModel model = new BoardViewModel(session);
            int before = model.Beasts.Count;
            string beastId = session.Save.Beasts[0].BeastId;

            model.Send("nearby_meadow", new[] { beastId });

            Assert.AreEqual(before, model.Beasts.Count, "a sent beast stays fully available for battle -- see docs/design/grove.md");
        }

        // ------------------------------------------------------------------ Npc

        [Test]
        public void NpcPanelViewModel_SelectsFirstNpc_ResolvesLine_AndMarksItSeen()
        {
            GameSession session = NewSession();
            NpcPanelViewModel model = new NpcPanelViewModel(session);

            Assert.AreEqual(Content.Dialogue.AllNpcs.Count, model.Npcs.Count);
            Assert.IsNotNull(model.SelectedNpcId);
            Assert.IsNotEmpty(session.Save.Npc.Dialogue.LinesSeen);

            string other = model.Npcs.Select(n => n.NpcId).FirstOrDefault(id => id != model.SelectedNpcId);
            if (other != null)
            {
                model.SelectNpc(other);
                Assert.AreEqual(other, model.SelectedNpcId);
            }
        }

        [Test]
        public void NpcPanelViewModel_Refresh_AutosavesOnlyWhenANewLineIsActuallySeen()
        {
            GameSession session = NewSession();
            int savesBeforeConstruct = session.AutosaveCount;
            NpcPanelViewModel model = new NpcPanelViewModel(session);

            // Constructing it resolves and marks the first NPC's line seen for the first time ever: a
            // genuine save-affecting change, so this must autosave.
            Assert.Greater(session.AutosaveCount, savesBeforeConstruct, "the first-ever resolved line must autosave");

            int savesAfterFirstLine = session.AutosaveCount;
            model.Refresh();
            model.Refresh();

            // Nothing changed (the same line resolves and is already marked seen): refreshing again
            // must not autosave, or every screen re-render would write the save file for nothing.
            Assert.AreEqual(savesAfterFirstLine, session.AutosaveCount, "no new line seen: Refresh must not autosave again");
        }

        [Test]
        public void NpcPanelViewModel_Request_FulfilEnabledOnlyWhenItemHeld_ThenFulfils()
        {
            GameSession session = NewSession();
            RequestData request = Content.Dialogue.Request("request_keeper_variety");
            Assert.IsNotNull(request, "authored in dialogue.json (docs/design/grove.md, D2)");
            session.Save.Garden.VarietiesDiscovered.AddRange(new[] { "v1", "v2", "v3" }); // request_keeper_variety's condition: variety_count:3
            NpcPanelViewModel model = new NpcPanelViewModel(session);
            model.SelectNpc(request.NpcId);

            NpcRequestRow row = model.Requests.Single(r => r.RequestId == request.RequestId);
            Assert.IsFalse(row.CanFulfill, "not held yet");

            session.Save.Grove.Items.Add(request.ItemId, request.Count);
            model.Refresh();
            Assert.IsTrue(model.Requests.Single(r => r.RequestId == request.RequestId).CanFulfill);

            NpcActionOutcome result = model.FulfillRequest(request.RequestId);
            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(model.Requests.Single(r => r.RequestId == request.RequestId).Fulfilled);
        }

        // ------------------------------------------------------------------ The hub

        [Test]
        public void GroveHubViewModel_BuildsAllFourSubModels_AndSelectsTabs()
        {
            GameSession session = NewSession();
            GroveHubViewModel hub = new GroveHubViewModel(session);

            Assert.AreEqual(GroveTab.Glade, hub.Tab);
            Assert.IsNotNull(hub.Glade);
            Assert.IsNotNull(hub.Garden);
            Assert.IsNotNull(hub.Board);
            Assert.IsNotNull(hub.Npc);

            hub.Select(GroveTab.Board);
            Assert.AreEqual(GroveTab.Board, hub.Tab);

            hub.RefreshAll();
            Assert.AreEqual(Content.GroveLibrary.Data.Habitats.Length, hub.Glade.Habitats.Count);
        }

        // ------------------------------------------------------------------ Soothing on the encounter screen

        /// <summary>The first row-0 location of a fresh r01 expedition whose type matches <paramref name="type"/>, trying seeds until one is found.</summary>
        private static MapNode FindNode(PlayerSave save, MapNodeType type, int maxSeed = 60)
        {
            if (save.Campaign.HasActiveRun)
            {
                CampaignRules.Retreat(save);
            }

            for (int seed = 1; seed <= maxSeed; seed++)
            {
                CampaignResult started = CampaignRules.StartRun(save, Content.Campaign, "r01", seed);
                Assert.IsTrue(started.Success, started.Error);
                MapNode node = CampaignRules.Choices(save.Campaign.ActiveRun).Find(n => n.Type == type);
                if (node != null)
                {
                    return node;
                }

                CampaignRules.Retreat(save);
            }

            Assert.Fail("No seed within " + maxSeed + " produced a reachable " + type + " node at row 0.");
            return null;
        }

        /// <summary>Walks the expedition (winning every Battle/Elite, camping/trading through Rest/Shop) until the stage's Gate is reachable.</summary>
        private static MapNode WalkToGate(PlayerSave save)
        {
            MapRun run = save.Campaign.ActiveRun;
            for (int guard = 0; guard < 60; guard++)
            {
                List<MapNode> choices = CampaignRules.Choices(run);
                MapNode gate = choices.Find(n => n.Type == MapNodeType.Gate || n.Type == MapNodeType.Boss);
                if (gate != null)
                {
                    return gate;
                }

                MapNode next = choices[0];
                switch (next.Type)
                {
                    case MapNodeType.Rest:
                        CampaignRules.Camp(save, Content.Campaign, next.NodeId, save.Beasts[0].BeastId);
                        break;
                    case MapNodeType.Shop:
                        CampaignRules.Trade(save, Content.Campaign, next.NodeId, null);
                        break;
                    default:
                        CampaignRules.ResolveBattle(save, Content.Campaign, next.NodeId, BattleOutcome.PlayerVictory);
                        break;
                }
            }

            Assert.Fail("Never reached the stage's Gate.");
            return null;
        }

        [Test]
        public void EncounterViewModel_CanSoothe_OnlyForAnOrdinaryBattleLocation_ListsHeldItems()
        {
            GameSession session = NewSession();
            MapNode battle = FindNode(session.Save, MapNodeType.Battle);
            EncounterViewModel onBattle = new EncounterViewModel(session, battle.NodeId);
            Assert.IsTrue(onBattle.CanSoothe);
            Assert.IsEmpty(onBattle.SoothingOptions, "none held yet");

            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            session.Save.Grove.Items.Add(item, 3);
            EncounterViewModel refreshed = new EncounterViewModel(session, battle.NodeId);
            Assert.AreEqual(1, refreshed.SoothingOptions.Count);
            Assert.AreEqual(3, refreshed.SoothingOptions[0].Held);

            MapNode gate = WalkToGate(session.Save);
            EncounterViewModel onGate = new EncounterViewModel(session, gate.NodeId);
            Assert.IsFalse(onGate.CanSoothe, "never for a Gate or Boss");
        }

        [Test]
        public void EncounterViewModel_Soothe_ClearsTheNode_PaysRewards_AndAutosaves()
        {
            GameSession session = NewSession();
            MapNode battle = FindNode(session.Save, MapNodeType.Battle);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            session.Save.Grove.Items.Add(item, 1);
            EncounterViewModel model = new EncounterViewModel(session, battle.NodeId);
            int saves = session.AutosaveCount;

            SootheOutcome outcome = model.Soothe(item);

            Assert.IsTrue(outcome.Success, outcome.Message);
            StringAssert.Contains("Soothed!", outcome.Message);
            Assert.IsTrue(session.Save.Campaign.ActiveRun.Cleared.Contains(battle.NodeId));
            Assert.AreEqual(1, session.Save.Campaign.LocationsSoothed);
            Assert.Greater(session.AutosaveCount, saves);

            SootheOutcome refused = model.Soothe(item);
            Assert.IsFalse(refused.Success, "no item left, and the node is already cleared");
        }

        // ------------------------------------------------------------------ Colour forms on the beast detail screen

        [Test]
        public void BeastDetailViewModel_ColourForms_LockedThenOwnedThenWorn()
        {
            ColourFormData form = Content.GroveLibrary.Data.ColourForms[0];
            GameSession session = NewSession();
            OwnedBeast beast = session.Save.Beasts.FirstOrDefault(b => b.Progress.SpeciesId == form.SpeciesId);
            if (beast == null)
            {
                beast = OwnedBeast.Create("cf-test", form.SpeciesId, 5);
                session.Save.Beasts.Add(beast);
            }

            BeastDetailViewModel model = new BeastDetailViewModel(session, beast.BeastId);
            ColourFormRow row = model.ColourForms.Single(c => c.ColourFormId == form.ColourFormId);
            Assert.IsFalse(row.Owned);
            Assert.IsFalse(row.Worn);

            session.Save.Grove.Items.Add(form.ItemId, form.ItemCount);
            model.Refresh();
            Assert.IsTrue(model.ColourForms.Single(c => c.ColourFormId == form.ColourFormId).ItemHeld >= form.ItemCount);

            ColourFormResult unlocked = model.UnlockColourForm(form.ColourFormId);
            Assert.IsTrue(unlocked.Success);
            Assert.IsTrue(unlocked.NewlyUnlocked);
            row = model.ColourForms.Single(c => c.ColourFormId == form.ColourFormId);
            Assert.IsTrue(row.Owned);
            Assert.IsFalse(row.Worn, "owned, but not worn until WearColourForm");

            CosmeticResult worn = model.WearColourForm(form.ColourFormId);
            Assert.AreEqual(CosmeticResult.Set, worn);
            Assert.IsTrue(model.ColourForms.Single(c => c.ColourFormId == form.ColourFormId).Worn);

            string tint = ColourFormPresentation.WornTint(session.Save, beast.BeastId, form.SpeciesId, Content.GroveLibrary, Content.Economy.Cosmetics);
            Assert.AreEqual(ColourFormPresentation.TintHex(form.ColourFormId), tint);

            CosmeticResult natural = model.WearNaturalColour(form.CosmeticCategoryId);
            Assert.AreEqual(CosmeticResult.Set, natural);
            Assert.IsFalse(model.ColourForms.Single(c => c.ColourFormId == form.ColourFormId).Worn);
            Assert.IsNull(ColourFormPresentation.WornTint(session.Save, beast.BeastId, form.SpeciesId, Content.GroveLibrary, Content.Economy.Cosmetics));
        }

        // ------------------------------------------------------------------ ColourFormPresentation

        [Test]
        public void ColourFormPresentation_TintHex_IsStableAndValidHex()
        {
            string a = ColourFormPresentation.TintHex("phoenix_ember_bloom");
            string b = ColourFormPresentation.TintHex("phoenix_ember_bloom");
            string c = ColourFormPresentation.TintHex("golem_honeyamber");

            Assert.AreEqual(a, b, "deterministic: the same id always yields the same colour");
            Assert.AreNotEqual(a, c);
            Assert.That(a, Does.Match("^#[0-9A-F]{6}$"));
        }

        [Test]
        public void ColourFormPresentation_TintHex_NeverThrows_EvenWhenTheHashIsIntMinValue()
        {
            // "xdygugiv鬋￡" is crafted so the FNV-ish id hash (hash = 17; hash = hash*31 + c)
            // lands exactly on int.MinValue: Math.Abs(int.MinValue) throws OverflowException (its
            // negation cannot be represented as a positive Int32), which is exactly the bug this
            // regresses — TintHex must map it to a valid hue with no exception, e.g. via `hash &
            // 0x7fffffff` instead of Math.Abs.
            string id = "xdygugiv鬋￡";

            string hex = null;
            Assert.DoesNotThrow(() => hex = ColourFormPresentation.TintHex(id));
            Assert.That(hex, Does.Match("^#[0-9A-F]{6}$"));
        }

        [Test]
        public void ColourFormPresentation_WornTint_NullForNaturalOrUnknownSpecies()
        {
            GameSession session = NewSession();
            string beastId = session.Save.Beasts[0].BeastId;
            string speciesId = session.Save.FindBeast(beastId).Progress.SpeciesId;

            Assert.IsNull(ColourFormPresentation.WornTint(session.Save, beastId, speciesId, Content.GroveLibrary, Content.Economy.Cosmetics));
            Assert.IsNull(ColourFormPresentation.WornTint(session.Save, beastId, "not_a_species", Content.GroveLibrary, Content.Economy.Cosmetics));
            Assert.IsNull(ColourFormPresentation.WornTint(null, beastId, speciesId, Content.GroveLibrary, Content.Economy.Cosmetics));
        }

        // ------------------------------------------------------------------ Home nav: Grove is real now (D4)

        [Test]
        public void HomeViewModel_Grove_IsAvailable_NotComingSoon()
        {
            HomeViewModel home = new HomeViewModel();
            home.Select(HomeTab.Grove);
            Assert.IsTrue(home.TabAvailable, "the Grove hub is built in D4 -- HomeScreen pushes GroveScreen rather than showing a placeholder");
        }
    }
}
