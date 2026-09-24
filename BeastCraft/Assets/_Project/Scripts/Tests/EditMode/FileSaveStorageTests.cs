using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BeastCraft.Save;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="FileSaveStorage"/> against a real temp directory: the <see cref="SaveStore"/> round
    /// trip, atomic replace with a one-generation backup, fallback to the backup when the main file
    /// is missing or corrupt, slot-name validation, delete, IO failures as results rather than
    /// exceptions, in-process concurrency, the slot index and the Unity location adapter.
    /// </summary>
    public class FileSaveStorageTests
    {
        private const string SaveA = "{\"SchemaVersion\":2,\"Tag\":\"A\"}";
        private const string SaveB = "{\"SchemaVersion\":2,\"Tag\":\"B\"}";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "BeastCraftTests", "FileSaveStorage", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
            else if (File.Exists(_root))
            {
                File.Delete(_root);
            }
        }

        [Test]
        public void SaveStore_RoundTripsThroughFiles()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            SaveStore store = new SaveStore(storage, new SaveSerializer(new JsonUtilitySaveSerializer()));

            PlayerSave save = PlayerSave.CreateNew();
            save.Avatar.Level = 9;
            save.Materials.Add("shard", 14);

            Assert.IsFalse(store.Exists("main"));
            Assert.IsFalse(store.Load("main").Success);
            Assert.IsTrue(store.Save("main", save));
            Assert.IsTrue(store.Exists("main"));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "main.save")), "Directory and file are created on the first write.");

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(9, loaded.Save.Avatar.Level);
            Assert.AreEqual(14, loaded.Save.Materials.GetCount("shard"));
            Assert.AreEqual(SaveFileSource.Main, storage.Read("main").Source);
        }

        [Test]
        public void Write_IsUtf8WithoutBom_AndReadToleratesABom()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            string text = "{\"SchemaVersion\":2,\"Name\":\"Émberfox ✨\"}";

            Assert.IsTrue(storage.Write("main", text).Success);

            byte[] bytes = File.ReadAllBytes(storage.GetSlotPath("main"));
            Assert.AreNotEqual(0xEF, bytes[0], "No byte-order mark.");
            Assert.AreEqual(text, new UTF8Encoding(false, true).GetString(bytes));

            File.WriteAllText(storage.GetSlotPath("main"), SaveA, new UTF8Encoding(true));
            SaveFileResult read = storage.Read("main");

            Assert.IsTrue(read.Success, read.Error);
            Assert.AreEqual(SaveA, read.Contents);
        }

        [Test]
        public void SecondWrite_ReplacesAtomically_AndKeepsTheOldSaveAsBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);

            Assert.IsTrue(storage.Write("main", SaveA).Success);
            Assert.IsFalse(File.Exists(storage.GetBackupPath("main")), "A first write has nothing to back up.");

            Assert.IsTrue(storage.Write("main", SaveB).Success);

            Assert.AreEqual(SaveB, File.ReadAllText(storage.GetSlotPath("main")));
            Assert.AreEqual(SaveA, File.ReadAllText(storage.GetBackupPath("main")));
            Assert.IsFalse(File.Exists(storage.GetSlotPath("main") + FileSaveStorage.TempSuffix), "No temp file is left behind.");

            Assert.IsTrue(storage.Write("main", SaveA).Success);
            Assert.AreEqual(SaveB, File.ReadAllText(storage.GetBackupPath("main")), "Only one generation is kept.");
        }

        [Test]
        public void TruncatedMain_FallsBackToBackup_AndSaysSo()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);
            File.WriteAllText(storage.GetSlotPath("main"), "{\"SchemaVersion\":2,\"Ta");

            SaveFileResult read = storage.Read("main");

            Assert.IsTrue(read.Success, read.Error);
            Assert.AreEqual(SaveFileSource.Backup, read.Source);
            Assert.AreEqual(SaveA, read.Contents);
            StringAssert.Contains("content check", read.MainFileProblem);
            Assert.Greater(read.LastWriteUtc, DateTime.MinValue);
            Assert.IsTrue(storage.TryRead("main", out string viaInterface));
            Assert.AreEqual(SaveA, viaInterface);
        }

        [Test]
        public void MissingOrInvalidUtf8Main_FallsBackToBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);

            File.WriteAllBytes(storage.GetSlotPath("main"), new byte[] { (byte)'{', 0xC3, 0x28, (byte)'}' });
            SaveFileResult badUtf8 = storage.Read("main");
            Assert.AreEqual(SaveFileSource.Backup, badUtf8.Source);
            StringAssert.Contains("UTF-8", badUtf8.MainFileProblem);

            File.Delete(storage.GetSlotPath("main"));
            SaveFileResult missing = storage.Read("main");
            Assert.AreEqual(SaveFileSource.Backup, missing.Source);
            Assert.AreEqual("missing", missing.MainFileProblem);
            Assert.IsTrue(storage.Exists("main"), "A backup alone still counts as a save.");
        }

        [Test]
        public void CorruptMainAndBackup_IsACorruptFailure()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);
            File.WriteAllText(storage.GetSlotPath("main"), "   ");
            File.WriteAllText(storage.GetBackupPath("main"), "{\"trunc");

            SaveFileResult read = storage.Read("main");

            Assert.IsFalse(read.Success);
            Assert.AreEqual(SaveFileError.Corrupt, read.ErrorKind);
            StringAssert.Contains("empty", read.Error);
            Assert.IsNull(read.Contents);
            Assert.IsFalse(storage.TryRead("main", out string contents));
            Assert.IsNull(contents);
        }

        [Test]
        public void Write_DoesNotRotateACorruptMainOverAGoodBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);
            File.WriteAllText(storage.GetSlotPath("main"), "{\"broken");

            Assert.IsTrue(storage.Write("main", SaveB).Success);

            Assert.AreEqual(SaveB, File.ReadAllText(storage.GetSlotPath("main")));
            Assert.AreEqual(SaveA, File.ReadAllText(storage.GetBackupPath("main")), "The good backup survives.");
        }

        [Test]
        public void LeftoverTempFile_IsIgnoredAndOverwritten()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            string temp = storage.GetSlotPath("main") + FileSaveStorage.TempSuffix;
            File.WriteAllText(temp, "{\"half");

            Assert.AreEqual(SaveA, storage.Read("main").Contents);
            Assert.IsTrue(storage.Write("main", SaveB).Success);
            Assert.AreEqual(SaveB, storage.Read("main").Contents);
            Assert.IsFalse(File.Exists(temp));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("..")]
        [TestCase("../main")]
        [TestCase("..\\main")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("C:main")]
        [TestCase("/etc/passwd")]
        [TestCase("main.save")]
        [TestCase("main.txt")]
        [TestCase(" main")]
        [TestCase("main ")]
        [TestCase("my save")]
        [TestCase("_main")]
        [TestCase("-main")]
        [TestCase("CON")]
        [TestCase("nul")]
        [TestCase("Com1")]
        [TestCase("lpt9")]
        [TestCase("slöt")]
        [TestCase("main\0")]
        public void InvalidSlotNames_AreRejectedEverywhere(string slot)
        {
            FileSaveStorage storage = new FileSaveStorage(_root);

            Assert.IsFalse(FileSaveStorage.IsValidSlotName(slot));
            Assert.IsNull(storage.GetSlotPath(slot));
            Assert.AreEqual(SaveFileError.InvalidSlot, storage.Write(slot, SaveA).ErrorKind);
            Assert.AreEqual(SaveFileError.InvalidSlot, storage.Read(slot).ErrorKind);
            Assert.AreEqual(SaveFileError.InvalidSlot, storage.Remove(slot).ErrorKind);
            Assert.IsFalse(storage.TryWrite(slot, SaveA));
            Assert.IsFalse(storage.Exists(slot));
            Assert.IsFalse(storage.Delete(slot));
            Assert.IsFalse(Directory.Exists(_root), "Nothing was written anywhere.");
        }

        [Test]
        public void SlotNameLength_IsCapped()
        {
            Assert.IsTrue(FileSaveStorage.IsValidSlotName(new string('a', FileSaveStorage.MaxSlotNameLength)));
            Assert.IsFalse(FileSaveStorage.IsValidSlotName(new string('a', FileSaveStorage.MaxSlotNameLength + 1)));
        }

        [TestCase("main")]
        [TestCase("slot_1")]
        [TestCase("Auto-Save")]
        [TestCase("0")]
        [TestCase("console")]
        public void ValidSlotNames_MapIntoTheRootWithTheFixedExtension(string slot)
        {
            FileSaveStorage storage = new FileSaveStorage(_root);

            Assert.IsTrue(FileSaveStorage.IsValidSlotName(slot));
            Assert.AreEqual(Path.Combine(storage.RootDirectory, slot + FileSaveStorage.Extension), storage.GetSlotPath(slot));
            Assert.IsTrue(storage.Write(slot, SaveA).Success);
            Assert.AreEqual(SaveA, storage.Read(slot).Contents);
        }

        [Test]
        public void Delete_RemovesMainBackupAndTemp()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);
            storage.Write("other", SaveA);
            File.WriteAllText(storage.GetSlotPath("main") + FileSaveStorage.TempSuffix, "{}");

            Assert.IsTrue(storage.Delete("main"));

            Assert.IsFalse(storage.Exists("main"));
            Assert.IsFalse(File.Exists(storage.GetSlotPath("main")));
            Assert.IsFalse(File.Exists(storage.GetBackupPath("main")));
            Assert.IsFalse(File.Exists(storage.GetSlotPath("main") + FileSaveStorage.TempSuffix));
            Assert.AreEqual(SaveFileError.NotFound, storage.Read("main").ErrorKind);
            Assert.IsTrue(storage.Exists("other"), "Other slots are untouched.");

            SaveFileResult again = storage.Remove("main");
            Assert.IsFalse(again.Success);
            Assert.AreEqual(SaveFileError.NotFound, again.ErrorKind);
            Assert.IsFalse(storage.Delete("never"));
        }

        [Test]
        public void Write_RefusesTextThatWouldNotReadBack()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);

            Assert.AreEqual(SaveFileError.InvalidContents, storage.Write("main", null).ErrorKind);
            Assert.AreEqual(SaveFileError.InvalidContents, storage.Write("main", "").ErrorKind);
            Assert.AreEqual(SaveFileError.InvalidContents, storage.Write("main", "plain text").ErrorKind);
            Assert.AreEqual(SaveFileError.InvalidContents, storage.Write("main", "{\"cut").ErrorKind);
            Assert.IsFalse(storage.Exists("main"));

            FileSaveStorage anyText = new FileSaveStorage(_root, text => text.Length > 0);
            Assert.IsTrue(anyText.Write("notes", "plain text").Success);
            Assert.AreEqual("plain text", anyText.Read("notes").Contents);

            FileSaveStorage throwingCheck = new FileSaveStorage(_root, text => throw new InvalidOperationException("boom"));
            Assert.AreEqual(SaveFileError.InvalidContents, throwingCheck.Write("main", SaveA).ErrorKind);
        }

        [Test]
        public void IoFailure_OnWrite_IsAnErrorResultNotAnException()
        {
            // The "directory" is a file, so it can be neither created nor written into.
            Directory.CreateDirectory(Path.GetDirectoryName(_root));
            File.WriteAllText(_root, "not a directory");
            FileSaveStorage storage = new FileSaveStorage(_root);
            SaveStore store = new SaveStore(storage, new SaveSerializer(new JsonUtilitySaveSerializer()));

            SaveFileResult write = storage.Write("main", SaveA);

            Assert.IsFalse(write.Success);
            Assert.AreEqual(SaveFileError.Io, write.ErrorKind);
            StringAssert.Contains("main", write.Error);
            Assert.IsFalse(storage.TryWrite("main", SaveA));
            Assert.IsFalse(store.Save("main", PlayerSave.CreateNew()));
            Assert.IsFalse(storage.Read("main").Success);
            Assert.IsFalse(storage.Exists("main"));
            Assert.IsEmpty(storage.ListSlots());
        }

        [Test]
        public void IoFailure_OnALockedMain_FallsBackOnReadAndFailsTheWriteCleanly()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Exclusive file locks are only enforced on Windows.");
            }

            FileSaveStorage storage = new FileSaveStorage(_root);
            storage.Write("main", SaveA);
            storage.Write("main", SaveB);

            using (new FileStream(storage.GetSlotPath("main"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                SaveFileResult read = storage.Read("main");

                Assert.IsTrue(read.Success, read.Error);
                Assert.AreEqual(SaveFileSource.Backup, read.Source);
                StringAssert.StartsWith("unreadable", read.MainFileProblem);

                SaveFileResult write = storage.Write("main", "{\"SchemaVersion\":2,\"Tag\":\"C\"}");

                Assert.IsFalse(write.Success);
                Assert.AreEqual(SaveFileError.Io, write.ErrorKind);
                Assert.IsFalse(File.Exists(storage.GetSlotPath("main") + FileSaveStorage.TempSuffix), "The temp file is cleaned up.");
            }

            Assert.AreEqual(SaveB, storage.Read("main").Contents, "The failed write left the save intact.");
        }

        [Test]
        public void ConcurrentWritesInOneProcess_LeaveEveryFileWhole()
        {
            List<string> texts = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                texts.Add("{\"SchemaVersion\":2,\"Writer\":" + i + ",\"Pad\":\"" + new string((char)('a' + i), 2000) + "\"}");
            }

            Parallel.For(0, 64, i =>
            {
                // Separate instances over the same directory share the process-wide lock.
                FileSaveStorage storage = new FileSaveStorage(_root);
                Assert.IsTrue(storage.Write("shared", texts[i % texts.Count]).Success);
                Assert.IsTrue(storage.Write("own" + (i % 4), texts[i % texts.Count]).Success);
                Assert.IsTrue(storage.Read("shared").Success);
            });

            FileSaveStorage check = new FileSaveStorage(_root);
            SaveFileResult shared = check.Read("shared");

            Assert.AreEqual(SaveFileSource.Main, shared.Source);
            CollectionAssert.Contains(texts, shared.Contents);
            CollectionAssert.Contains(texts, File.ReadAllText(check.GetBackupPath("shared")));
            CollectionAssert.AreEqual(new[] { "own0", "own1", "own2", "own3", "shared" }, check.ListSlots());
        }

        [Test]
        public void SlotIndex_ListsSlotsNewestFirst_WithSchemaVersions()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            SaveStore store = new SaveStore(storage, new SaveSerializer(new JsonUtilitySaveSerializer()));
            store.Save("older", PlayerSave.CreateNew());
            store.Save("newer", PlayerSave.CreateNew());
            storage.Write("backup_only", "{\"SchemaVersion\":1}");
            storage.Write("backup_only", "{\"SchemaVersion\":2}");
            File.Delete(storage.GetSlotPath("backup_only"));
            File.WriteAllText(Path.Combine(_root, "broken.save"), "{\"cut");
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "{}");
            File.WriteAllText(Path.Combine(_root, "bad name.save"), "{}");

            DateTime now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(storage.GetSlotPath("older"), now.AddHours(-2));
            File.SetLastWriteTimeUtc(storage.GetSlotPath("newer"), now.AddHours(-1));
            File.SetLastWriteTimeUtc(storage.GetBackupPath("backup_only"), now.AddHours(-3));

            CollectionAssert.AreEqual(new[] { "backup_only", "broken", "newer", "older" }, storage.ListSlots());

            List<SaveSlotInfo> index = SaveSlotIndex.Build(storage, new JsonUtilitySaveSerializer());

            CollectionAssert.AreEqual(new[] { "newer", "older", "backup_only", "broken" }, index.ConvertAll(s => s.Slot));

            Assert.IsTrue(index[0].Readable);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, index[0].SchemaVersion);
            Assert.AreEqual(SaveFileSource.Main, index[0].Source);
            Assert.IsNull(index[0].Problem);
            Assert.That(index[0].LastWriteUtc, Is.EqualTo(now.AddHours(-1)).Within(TimeSpan.FromSeconds(2)));

            Assert.AreEqual(SaveFileSource.Backup, index[2].Source);
            Assert.AreEqual(1, index[2].SchemaVersion);
            StringAssert.Contains("missing", index[2].Problem);

            Assert.IsFalse(index[3].Readable);
            Assert.AreEqual(0, index[3].SchemaVersion);
            Assert.IsNotNull(index[3].Problem);
        }

        [Test]
        public void ReadBackup_ReadsOnlyTheBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);

            Assert.AreEqual(SaveFileError.InvalidSlot, storage.ReadBackup("../x").ErrorKind);
            Assert.AreEqual(SaveFileError.NotFound, storage.ReadBackup("main").ErrorKind);

            storage.Write("main", SaveA);
            Assert.AreEqual(SaveFileError.NotFound, storage.ReadBackup("main").ErrorKind, "A lone main file is not a backup.");

            storage.Write("main", SaveB);
            SaveFileResult backup = storage.ReadBackup("main");
            Assert.IsTrue(backup.Success, backup.Error);
            Assert.AreEqual(SaveFileSource.Backup, backup.Source);
            Assert.AreEqual(SaveA, backup.Contents);

            File.WriteAllText(storage.GetBackupPath("main"), "{\"cut");
            Assert.AreEqual(SaveFileError.Corrupt, storage.ReadBackup("main").ErrorKind);
        }

        [Test]
        public void SaveStoreLoad_ReportsTheMainFile()
        {
            SaveStore store = NewFileStore(out FileSaveStorage _);
            store.Save("main", SaveAtLevel(4));

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(SaveFileSource.Main, loaded.StorageSource);
            Assert.IsNull(loaded.MainFileProblem);

            SaveLoadResult missing = store.Load("empty");
            Assert.IsFalse(missing.Success);
            StringAssert.Contains("No save in slot 'empty'", missing.Error);
            Assert.AreEqual(SaveFileSource.None, missing.StorageSource);
        }

        [Test]
        public void SaveStoreLoad_CorruptMain_UsesBackupAndSaysWhy()
        {
            SaveStore store = NewFileStore(out FileSaveStorage storage);
            store.Save("main", SaveAtLevel(3));
            store.Save("main", SaveAtLevel(9));
            File.WriteAllText(storage.GetSlotPath("main"), "{\"SchemaVersion\":2,\"Avat");

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(3, loaded.Save.Avatar.Level);
            Assert.AreEqual(SaveFileSource.Backup, loaded.StorageSource);
            StringAssert.Contains("content check", loaded.MainFileProblem);
        }

        // Failures that the real JsonUtility and the stub's System.Text.Json twin agree on.
        [TestCase("{\"SchemaVersion\":0}", "SchemaVersion")]
        [TestCase("{\"Avatar\":{\"Level\":9}}", "SchemaVersion")]
        public void SaveStoreLoad_MainThatReadsButFailsToLoad_RetriesTheBackup(string mainText, string expectedReason)
        {
            SaveStore store = NewFileStore(out FileSaveStorage storage);
            store.Save("main", SaveAtLevel(3));
            store.Save("main", SaveAtLevel(9));
            File.WriteAllText(storage.GetSlotPath("main"), mainText);

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(3, loaded.Save.Avatar.Level);
            Assert.AreEqual(SaveFileSource.Backup, loaded.StorageSource);
            StringAssert.Contains("failed to load", loaded.MainFileProblem);
            StringAssert.Contains(expectedReason, loaded.MainFileProblem);
        }

        [Test]
        public void SaveStoreLoad_FailedMigration_RetriesTheBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            SaveStore store = new SaveStore(storage, new SaveSerializer(new JsonUtilitySaveSerializer(), migrations: new[] { new FailOnMarkerMigration() }));
            storage.Write("main", "{\"SchemaVersion\":1,\"Avatar\":{\"Level\":3,\"Xp\":0}}");
            storage.Write("main", "{\"SchemaVersion\":1,\"Avatar\":{\"Level\":9,\"Xp\":0},\"Marker\":\"poison\"}");

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(3, loaded.Save.Avatar.Level);
            Assert.IsTrue(loaded.Migrated);
            Assert.AreEqual(SaveFileSource.Backup, loaded.StorageSource);
            StringAssert.Contains("Migrating", loaded.MainFileProblem);
        }

        [Test]
        public void SaveStoreLoad_NewerBuildsMain_DoesNotFallBackToAnOlderBackup()
        {
            SaveStore store = NewFileStore(out FileSaveStorage storage);
            store.Save("main", SaveAtLevel(3));
            store.Save("main", SaveAtLevel(9));
            File.WriteAllText(storage.GetSlotPath("main"), "{\"SchemaVersion\":99}");

            SaveLoadResult loaded = store.Load("main");

            Assert.IsFalse(loaded.Success);
            StringAssert.Contains("newer build", loaded.Error);
            Assert.AreEqual(SaveFileSource.Main, loaded.StorageSource);
            Assert.IsNull(loaded.MainFileProblem);
        }

        [Test]
        public void SaveStoreLoad_MainAndBackupBothFailToLoad_ReturnsTheMainFailure()
        {
            SaveStore store = NewFileStore(out FileSaveStorage storage);
            store.Save("main", SaveAtLevel(3));
            store.Save("main", SaveAtLevel(9));
            File.WriteAllText(storage.GetSlotPath("main"), "{\"SchemaVersion\":0}");
            File.WriteAllText(storage.GetBackupPath("main"), "{\"SchemaVersion\":-1}");

            SaveLoadResult loaded = store.Load("main");

            Assert.IsFalse(loaded.Success);
            StringAssert.Contains("SchemaVersion", loaded.Error);
            Assert.AreEqual(SaveFileSource.Main, loaded.StorageSource);

            File.Delete(storage.GetBackupPath("main"));
            Assert.AreEqual(SaveFileSource.Main, store.Load("main").StorageSource, "No backup: the main failure stands.");
        }

        [Test]
        public void SaveStoreLoad_OverAPlainStorage_ReportsMain()
        {
            SaveStore store = new SaveStore(new SingleSlotStorage(), new SaveSerializer(new JsonUtilitySaveSerializer()));

            Assert.AreEqual(SaveFileSource.None, store.Load("main").StorageSource);
            Assert.IsTrue(store.Save("main", SaveAtLevel(5)));

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(SaveFileSource.Main, loaded.StorageSource);
            Assert.IsNull(loaded.MainFileProblem);
        }

        [Test]
        public void SlotIndex_OfAMissingDirectory_IsEmpty()
        {
            Assert.IsEmpty(SaveSlotIndex.Build(new FileSaveStorage(_root), new JsonUtilitySaveSerializer()));
        }

        [Test]
        public void Constructor_RequiresADirectory_AndNormalizesIt()
        {
            Assert.Throws<ArgumentException>(() => new FileSaveStorage(null));
            Assert.Throws<ArgumentException>(() => new FileSaveStorage("  "));
            Assert.AreEqual(Path.GetFullPath(_root), new FileSaveStorage(_root).RootDirectory);
        }

        [Test]
        public void UnitySaveLocations_UseASavesFolderUnderPersistentDataPath()
        {
            string expected = Path.Combine(Application.persistentDataPath, UnitySaveLocations.SavesFolderName);

            Assert.AreEqual(expected, UnitySaveLocations.DefaultDirectory());
            Assert.AreEqual(Path.GetFullPath(expected), UnitySaveLocations.Default().RootDirectory);
        }

        private static PlayerSave SaveAtLevel(int avatarLevel)
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Avatar.Level = avatarLevel;
            return save;
        }

        private SaveStore NewFileStore(out FileSaveStorage storage)
        {
            storage = new FileSaveStorage(_root);
            return new SaveStore(storage, new SaveSerializer(new JsonUtilitySaveSerializer()));
        }

        /// <summary>Schema 1 to 2 that fails on a save carrying <c>"Marker":"poison"</c>.</summary>
        private class FailOnMarkerMigration : ISaveMigration
        {
            public int FromVersion
            {
                get { return 1; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                if (json.Contains("poison"))
                {
                    throw new InvalidOperationException("poisoned save");
                }

                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.SchemaVersion = 2;
                return serializer.ToJson(save);
            }
        }

        /// <summary>A plain <see cref="ISaveStorage"/> (no backup) holding one slot in memory.</summary>
        private class SingleSlotStorage : ISaveStorage
        {
            private string _text;

            public bool Exists(string slot)
            {
                return _text != null;
            }

            public bool TryRead(string slot, out string contents)
            {
                contents = _text;
                return _text != null;
            }

            public bool TryWrite(string slot, string contents)
            {
                _text = contents;
                return true;
            }

            public bool Delete(string slot)
            {
                bool had = _text != null;
                _text = null;
                return had;
            }
        }
    }
}
