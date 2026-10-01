using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The save slots (#59): three slots, the title's Continue on the most recent one and New Game in
    /// the first free one, the slot list's rows, delete, export and import (a bad file never touches a
    /// slot), the desktop folder transfer, and the mid-battle crash refund of a battle's consumables.
    /// </summary>
    public class SaveSlotsTests
    {
        private const int MapSeed = 424242;

        private string _folder;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "BeastCraftTests", "SaveSlots", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private static GameSession NewSession(ISaveStorage storage)
        {
            return new GameSession(Content, storage, () => MapSeed);
        }

        /// <summary>Starts a game in <paramref name="slot"/> of <paramref name="storage"/> and returns the session.</summary>
        private static GameSession StartIn(ISaveStorage storage, string slot)
        {
            GameSession session = NewSession(storage);
            Assert.IsTrue(session.UseSlot(slot));
            return TestSaves.Started(session);
        }

        // ------------------------------------------------------------------ slots

        [Test]
        public void ThreeSlots_NewGameFillsTheFirstFreeOne_AndFullSlotsNeedAPick()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            CollectionAssert.AreEqual(new[] { "slot1", "slot2", "slot3" }, GameSession.SlotIds);
            Assert.AreEqual(GameSession.SlotCount, GameSession.SlotIds.Count);

            foreach (string expected in GameSession.SlotIds)
            {
                GameSession session = NewSession(storage);
                Assert.AreEqual(expected, session.FirstEmptySlot());
                StartIn(storage, expected);
            }

            GameSession full = NewSession(storage);
            Assert.IsNull(full.FirstEmptySlot());
            // Every slot holds a game: the title's New Game always opens the slot list now (it no
            // longer asks the view-model first), and every row there offers the overwrite-with-confirm
            // flow (SaveSlotsViewModel.NewGameReplaces) — that is "every slot needs a pick" today.
            Assert.IsTrue(new SaveSlotsViewModel(full).Rows().TrueForAll(row => row.HasSave));
            Assert.IsFalse(new TitleViewModel(full).PrepareNewGame());
            Assert.IsFalse(full.UseSlot("slot4"));
            Assert.IsFalse(full.UseSlot("settings"));
        }

        [Test]
        public void Slots_AreSeparateGames_AndUseSlotPutsTheOldGameDown()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession one = StartIn(storage, "slot1");
            one.Save.Gold = 111;
            Assert.IsTrue(one.Autosave(AutosaveReason.PlayerEdit));
            GameSession two = StartIn(storage, "slot2");
            two.Save.Gold = 222;
            Assert.IsTrue(two.Autosave(AutosaveReason.PlayerEdit));

            Assert.IsTrue(two.UseSlot("slot1"));
            Assert.IsNull(two.Save, "the slot-2 game was put down");
            Assert.IsFalse(two.Autosave(AutosaveReason.Background), "nothing autosaves into slot 1 before it is loaded");
            Assert.IsTrue(two.Continue().Success);
            Assert.AreEqual(111, two.Save.Gold);

            GameSession reader = NewSession(storage);
            reader.UseSlot("slot2");
            Assert.IsTrue(reader.Continue().Success);
            Assert.AreEqual(222, reader.Save.Gold);
        }

        [Test]
        public void TitleContinue_LoadsTheMostRecentlyWrittenSlot()
        {
            FileSaveStorage storage = new FileSaveStorage(_folder);
            StartIn(storage, "slot1").Save.Gold = 1;
            GameSession three = StartIn(storage, "slot3");
            three.Save.Gold = 333;
            three.Autosave(AutosaveReason.PlayerEdit);
            File.SetLastWriteTimeUtc(storage.GetSlotPath("slot1"), DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(storage.GetSlotPath("slot3"), DateTime.UtcNow.AddHours(-1));

            GameSession session = NewSession(storage);
            Assert.AreEqual("slot3", session.MostRecentSlot());
            LoadOutcome outcome = new TitleViewModel(session).Continue();
            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.AreEqual("slot3", session.Slot);
            Assert.AreEqual(333, session.Save.Gold);
        }

        [Test]
        public void SlotList_DescribesEachSlot()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            StartIn(storage, "slot2");
            storage.Corrupt("slot3", "not a save");
            GameSession session = NewSession(storage);
            session.UseSlot("slot2");
            List<SaveSlotRow> rows = new SaveSlotsViewModel(session).Rows();

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("Slot 1", rows[0].Title);
            Assert.IsFalse(rows[0].HasSave);
            Assert.AreEqual("Empty", rows[0].Detail);
            Assert.AreEqual("New game", rows[0].PlayText);

            Assert.IsTrue(rows[1].HasSave && rows[1].Readable && rows[1].Current);
            Assert.AreEqual("Beastbinder level 1, 6 beasts", rows[1].Detail);
            Assert.AreEqual(Content.Text.Format("ui.save_slots.region_stage", Content.Campaign.GetRegion(CampaignProgress.StartingRegionId).DisplayName, 1), rows[1].Where,
                            "the memory storage has no write times: the region and stage alone");
            Assert.AreEqual("Continue", rows[1].PlayText);

            Assert.IsTrue(rows[2].HasSave);
            Assert.IsFalse(rows[2].Readable);
            StringAssert.StartsWith("This save cannot be loaded", rows[2].Detail);
            Assert.IsFalse(new SaveSlotsViewModel(session).CanTransfer, "no transfer: Export and Import are hidden");
        }

        [Test]
        public void Delete_RemovesTheSlot_AndPutsItsGameDown()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = StartIn(storage, "slot2");
            SaveSlotsViewModel slots = new SaveSlotsViewModel(session);

            Assert.IsTrue(slots.NewGameReplaces("slot2"));
            Assert.AreEqual("Slot 2 deleted.", slots.Delete("slot2"));
            Assert.IsFalse(storage.Exists("slot2"));
            Assert.IsNull(session.Save);
            Assert.IsFalse(session.Autosave(AutosaveReason.Background), "the deleted slot is not written back");
            Assert.AreEqual("Slot 2 could not be deleted.", slots.Delete("slot2"));
            Assert.IsFalse(slots.NewGameReplaces("slot2"));
        }

        // ------------------------------------------------------------------ export / import

        [Test]
        public void ExportThenImport_CopiesASlot_ThroughTheFolder()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = StartIn(storage, "slot1");
            session.Save.Gold = 4321;
            session.Autosave(AutosaveReason.PlayerEdit);
            FolderSaveTransfer folder = new FolderSaveTransfer(_folder);
            SaveSlotsViewModel slots = new SaveSlotsViewModel(session, folder, () => new DateTime(2026, 9, 29, 14, 5, 0, DateTimeKind.Utc));
            Assert.IsTrue(slots.CanTransfer);

            string exported = null;
            slots.Export("slot1", message => exported = message);
            string file = Path.Combine(_folder, "beastcraft-slot1-20260929-140500.json");
            Assert.AreEqual("Saved a copy to " + file + ".", exported);
            Assert.IsTrue(File.Exists(file));

            string imported = null;
            slots.Import("slot3", message => imported = message);
            Assert.AreEqual("Imported into slot 3.", imported);

            GameSession reader = NewSession(storage);
            reader.UseSlot("slot3");
            Assert.IsTrue(reader.Continue().Success);
            Assert.AreEqual(4321, reader.Save.Gold);
        }

        [Test]
        public void Import_OfABadFile_LeavesTheSlotUntouched()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = StartIn(storage, "slot1");
            string before = storage.Peek("slot1");
            int writes = storage.Writes;

            foreach (string bad in new[] { "not json", "{}", "{\"SchemaVersion\":" + (PlayerSave.CurrentSchemaVersion + 1) + "}", string.Empty })
            {
                Assert.IsFalse(session.ImportSlot("slot1", bad, out string error), bad);
                Assert.IsNotNull(error);
            }

            Assert.AreEqual(before, storage.Peek("slot1"));
            Assert.AreEqual(writes, storage.Writes, "nothing was written");
            Assert.IsNotNull(session.Save, "the game in play was not put down");

            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "junk.json"), "{\"SchemaVersion\":0}");
            string message = null;
            new SaveSlotsViewModel(session, new FolderSaveTransfer(_folder)).Import("slot1", m => message = m);
            StringAssert.StartsWith("That file is not a save this game can load", message);
            StringAssert.EndsWith("Slot 1 is unchanged.", message);
            Assert.AreEqual(before, storage.Peek("slot1"));
        }

        [Test]
        public void Import_MigratesAnOlderSave_AndWritesTheCurrentSchema()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = NewSession(storage);

            Assert.IsTrue(session.ImportSlot("slot2", "{\"SchemaVersion\":5}", out string error), error);
            StringAssert.Contains("\"SchemaVersion\": " + PlayerSave.CurrentSchemaVersion, storage.Peek("slot2"));
            Assert.IsFalse(session.ImportSlot("slot9", "{\"SchemaVersion\":5}", out error));
        }

        [Test]
        public void FolderTransfer_ImportsTheNewestFile_AndReportsAnEmptyFolder()
        {
            FolderSaveTransfer folder = new FolderSaveTransfer(_folder);
            SaveTransferResult result = null;
            folder.Import(r => result = r);
            Assert.IsFalse(result.Success);
            StringAssert.StartsWith("No save file found", result.Describe(Content.Text));

            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "old.json"), "old");
            File.WriteAllText(Path.Combine(_folder, "new.json"), "new");
            File.SetLastWriteTimeUtc(Path.Combine(_folder, "old.json"), DateTime.UtcNow.AddDays(-1));
            folder.Import(r => result = r);
            Assert.IsTrue(result.Success, result.ErrorKey);
            Assert.AreEqual("new", result.Contents);
        }

        [Test]
        public void Export_OfAnEmptySlot_SaysSo()
        {
            GameSession session = NewSession(new MemorySaveStorage());
            string message = null;
            new SaveSlotsViewModel(session, new FolderSaveTransfer(_folder)).Export("slot2", m => message = m);
            StringAssert.StartsWith("Nothing to export", message);
            Assert.IsFalse(Directory.Exists(_folder), "no file written");
        }

        // ------------------------------------------------------------------ mid-battle crash refund

        /// <summary>A session in <paramref name="storage"/> that begins a battle with one Fury Draught (of two held) and then "crashes" (is dropped).</summary>
        private static void BeginBattleAndCrash(MemorySaveStorage storage)
        {
            GameSession session = StartIn(storage, GameSession.SlotName);
            ConsumableInventory.TryAdd(session.Save, "fury_draught", 2, 5);
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            encounter.ToggleConsumable("fury_draught");
            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            Assert.AreEqual(1, ConsumableInventory.Quantity(session.Save, "fury_draught"));
            StringAssert.Contains("\"PendingBattleConsumables\": [\n    \"fury_draught\"", storage.Peek(GameSession.SlotName).Replace("\r\n", "\n"),
                                  "the battle-start autosave records what was spent");
        }

        [Test]
        public void CrashBeforeTheResults_RefundsTheConsumable_OnTheNextContinue()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            BeginBattleAndCrash(storage);

            GameSession next = NewSession(storage);
            LoadOutcome outcome = next.Continue();

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.AreEqual(2, ConsumableInventory.Quantity(next.Save, "fury_draught"));
            Assert.IsEmpty(next.Save.PendingBattleConsumables);
            Assert.AreEqual("Your last battle did not finish, so your " + Content.Battle.GetConsumable("fury_draught").DisplayName + " was returned.", outcome.RefundMessage);
            StringAssert.Contains("\"PendingBattleConsumables\": []", storage.Peek(GameSession.SlotName), "the refund is saved straight away");
        }

        [Test]
        public void Refund_IsPaidOnce_AcrossReloads()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            BeginBattleAndCrash(storage);
            Assert.IsNotNull(NewSession(storage).Continue().RefundMessage);

            GameSession again = NewSession(storage);
            LoadOutcome outcome = again.Continue();
            Assert.IsNull(outcome.RefundMessage);
            Assert.AreEqual(2, ConsumableInventory.Quantity(again.Save, "fury_draught"), "no second refund");
        }

        [Test]
        public void ANormallyResolvedBattle_RefundsNothing()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = StartIn(storage, GameSession.SlotName);
            ConsumableInventory.TryAdd(session.Save, "fury_draught", 2, 5);
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            encounter.ToggleConsumable("fury_draught");
            NodeBattle battle = encounter.Start(out string error);
            Assert.IsNotNull(battle, error);
            battle.Complete();
            Assert.IsEmpty(session.Save.PendingBattleConsumables);

            GameSession next = NewSession(storage);
            LoadOutcome outcome = next.Continue();
            Assert.IsNull(outcome.RefundMessage);
            Assert.AreEqual(1, ConsumableInventory.Quantity(next.Save, "fury_draught"), "the consumable stays spent");
        }

        [Test]
        public void Refund_DropsAnItemTheContentNoLongerHas_AndKeepsAFullStacksItemOwed()
        {
            PlayerSave save = PlayerSave.CreateNew();
            ConsumableInventory.TryAdd(save, "fury_draught", 5, 5);
            BattleConsumableRefund.Record(save, new[] { "fury_draught", "gone_item", null });
            CollectionAssert.AreEqual(new[] { "fury_draught", "gone_item" }, save.PendingBattleConsumables);

            List<string> refunded = BattleConsumableRefund.RefundPending(save, id => id == "fury_draught" ? 5 : (int?)null);

            Assert.IsEmpty(refunded);
            Assert.AreEqual(5, ConsumableInventory.Quantity(save, "fury_draught"), "never past the cap");
            CollectionAssert.AreEqual(new[] { "fury_draught" }, save.PendingBattleConsumables, "still owed, not lost; the gone item is dropped");

            // Room again (one used): the next refund pays it and the record empties.
            ConsumableStack stack = save.Consumables.Find(s => s.ConsumableId == "fury_draught");
            stack.Quantity = 4;
            CollectionAssert.AreEqual(new[] { "fury_draught" }, BattleConsumableRefund.RefundPending(save, id => 5));
            Assert.AreEqual(5, ConsumableInventory.Quantity(save, "fury_draught"));
            Assert.IsEmpty(save.PendingBattleConsumables);
        }

        [Test]
        public void ARefundWhoseSaveFails_IsUndone_AndPaidOnceLater()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            BeginBattleAndCrash(storage);

            storage.FailWrites = true;
            GameSession failing = NewSession(storage);
            LoadOutcome outcome = failing.Continue();

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.IsNull(outcome.RefundMessage, "nothing is announced when it could not be kept");
            Assert.AreEqual(1, ConsumableInventory.Quantity(failing.Save, "fury_draught"), "the refund is undone in memory");
            CollectionAssert.AreEqual(new[] { "fury_draught" }, failing.Save.PendingBattleConsumables, "the record stays, as on disk");

            // The disk recovers and this session saves: still owed once, not paid.
            storage.FailWrites = false;
            Assert.IsTrue(failing.Autosave(AutosaveReason.Results));

            GameSession next = NewSession(storage);
            Assert.IsNotNull(next.Continue().RefundMessage);
            Assert.AreEqual(2, ConsumableInventory.Quantity(next.Save, "fury_draught"), "paid once");
            GameSession again = NewSession(storage);
            Assert.IsNull(again.Continue().RefundMessage);
            Assert.AreEqual(2, ConsumableInventory.Quantity(again.Save, "fury_draught"), "and never twice");
        }

        [Test]
        public void Restore_TakesBackOnlyTheRefundedUnits()
        {
            PlayerSave save = PlayerSave.CreateNew();
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);
            BattleConsumableRefund.Record(save, new[] { "fury_draught", "mend_tonic" });
            BattleConsumableRefund.Snapshot before = BattleConsumableRefund.Take(save);
            List<string> refunded = BattleConsumableRefund.RefundPending(save, id => 5);
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);

            BattleConsumableRefund.Restore(save, before, refunded);

            Assert.AreEqual(2, ConsumableInventory.Quantity(save, "fury_draught"), "what came in after the refund stays");
            Assert.AreEqual(0, ConsumableInventory.Quantity(save, "mend_tonic"));
            CollectionAssert.AreEqual(new[] { "fury_draught", "mend_tonic" }, save.PendingBattleConsumables);
        }
    }
}
