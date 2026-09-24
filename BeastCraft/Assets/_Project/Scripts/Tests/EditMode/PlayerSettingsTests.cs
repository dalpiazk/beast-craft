using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="PlayerSettings"/> and <see cref="PlayerSettingsStore"/>: the defaults, a round trip
    /// through <see cref="FileSaveStorage"/>, defaults for a missing, corrupt or unparseable slot and
    /// for a key the file lacks, and the settings slot reserved from game saves.
    /// </summary>
    public class PlayerSettingsTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "BeastCraftTests", "PlayerSettings", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void Defaults_SuggestionsOn()
        {
            PlayerSettings settings = new PlayerSettings();

            Assert.IsTrue(settings.TeamSuggestionsEnabled);
            Assert.AreEqual(PlayerSettings.CurrentSchemaVersion, settings.SchemaVersion);
        }

        [Test]
        public void RoundTripsThroughFileSaveStorage()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            PlayerSettingsStore store = new PlayerSettingsStore(storage, new JsonSaveSerializer());

            Assert.IsTrue(store.Save(new PlayerSettings { TeamSuggestionsEnabled = false, SchemaVersion = 0 }));
            Assert.IsTrue(File.Exists(storage.GetSlotPath(PlayerSettingsStore.SlotName)));

            PlayerSettings loaded = new PlayerSettingsStore(new FileSaveStorage(_root), new JsonSaveSerializer()).Load();

            Assert.IsFalse(loaded.TeamSuggestionsEnabled);
            Assert.AreEqual(PlayerSettings.CurrentSchemaVersion, loaded.SchemaVersion, "Save stamps the current version");
        }

        [Test]
        public void Missing_LoadsTheDefaults()
        {
            PlayerSettingsStore store = new PlayerSettingsStore(new FileSaveStorage(_root), new JsonSaveSerializer());

            Assert.IsFalse(store.TryLoad(out PlayerSettings settings));
            Assert.IsTrue(settings.TeamSuggestionsEnabled);
            Assert.IsTrue(store.Load().TeamSuggestionsEnabled);
        }

        [TestCase("{\"SchemaVersion\":1,\"TeamSuggestionsEnabled\":fal")]
        [TestCase("not json at all")]
        [TestCase("{not json}")]
        [TestCase("{\"SchemaVersion\":0,\"TeamSuggestionsEnabled\":false}")]
        [TestCase("")]
        public void CorruptUnparseableOrVersionZero_LoadsTheDefaults(string text)
        {
            Directory.CreateDirectory(_root);
            FileSaveStorage storage = new FileSaveStorage(_root);
            File.WriteAllText(storage.GetSlotPath(PlayerSettingsStore.SlotName), text);
            PlayerSettingsStore store = new PlayerSettingsStore(storage, new JsonSaveSerializer());

            PlayerSettings settings = store.Load();

            Assert.IsNotNull(settings);
            Assert.IsTrue(settings.TeamSuggestionsEnabled);
            Assert.IsFalse(store.TryLoad(out PlayerSettings _));
        }

        [Test]
        public void CorruptMainFile_FallsBackToTheBackup()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            PlayerSettingsStore store = new PlayerSettingsStore(storage, new JsonSaveSerializer());
            store.Save(new PlayerSettings { TeamSuggestionsEnabled = false });
            store.Save(new PlayerSettings { TeamSuggestionsEnabled = false });
            File.WriteAllText(storage.GetSlotPath(PlayerSettingsStore.SlotName), "{\"cut");

            Assert.IsFalse(store.Load().TeamSuggestionsEnabled, "the storage reads the previous write's backup");
        }

        [Test]
        public void AKeyTheFileLacks_KeepsItsDefault()
        {
            Directory.CreateDirectory(_root);
            FileSaveStorage storage = new FileSaveStorage(_root);
            File.WriteAllText(storage.GetSlotPath(PlayerSettingsStore.SlotName), "{\"SchemaVersion\":1,\"SomeFutureSetting\":3}");

            PlayerSettingsStore store = new PlayerSettingsStore(storage, new JsonSaveSerializer());

            Assert.IsTrue(store.TryLoad(out PlayerSettings settings));
            Assert.IsTrue(settings.TeamSuggestionsEnabled);
        }

        [Test]
        public void TheSettingsSlot_IsNeverAGameSave()
        {
            FileSaveStorage storage = new FileSaveStorage(_root);
            SaveStore saves = new SaveStore(storage, new SaveSerializer(new JsonSaveSerializer()));
            PlayerSettingsStore settings = new PlayerSettingsStore(storage, new JsonSaveSerializer());

            Assert.IsTrue(PlayerSettingsStore.IsReservedSlot("settings"));
            Assert.IsTrue(PlayerSettingsStore.IsReservedSlot("Settings"), "any letter case: file names are case-insensitive on Windows and macOS");
            Assert.IsFalse(PlayerSettingsStore.IsReservedSlot("main"));
            Assert.IsFalse(saves.Save("settings", PlayerSave.CreateNew()));
            Assert.IsFalse(saves.Save("SETTINGS", PlayerSave.CreateNew()));

            Assert.IsTrue(settings.Save(new PlayerSettings()));
            Assert.IsTrue(saves.Save("main", PlayerSave.CreateNew()));

            Assert.IsFalse(saves.Exists("settings"));
            Assert.IsFalse(saves.Load("settings").Success);
            List<SaveSlotInfo> index = SaveSlotIndex.Build(storage, new JsonSaveSerializer());
            CollectionAssert.AreEqual(new[] { "main" }, index.ConvertAll(s => s.Slot), "the slot index lists game saves only");
            Assert.IsTrue(settings.TryLoad(out PlayerSettings _), "a game save beside it leaves the settings alone");
        }

        [Test]
        public void Save_Null_IsRefused()
        {
            Assert.IsFalse(new PlayerSettingsStore(new FileSaveStorage(_root), new JsonSaveSerializer()).Save(null));
            Assert.Throws<ArgumentNullException>(() => new PlayerSettingsStore(null, new JsonSaveSerializer()));
        }
    }
}
