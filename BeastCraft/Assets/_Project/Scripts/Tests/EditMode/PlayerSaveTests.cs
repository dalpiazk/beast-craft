using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The save system: <see cref="PlayerSave"/> round-trips through <see cref="SaveSerializer"/>
    /// (over <see cref="JsonUtilitySaveSerializer"/> — the real JsonUtility in Unity, the stub's
    /// System.Text.Json twin in <c>Tooling/EditModeTests</c>), versions and migrations, validation
    /// that reports rather than crashes, and <see cref="SaveStore"/> over a storage seam.
    /// </summary>
    public class PlayerSaveTests
    {
        private static readonly SaveContentCatalog Catalog = new SaveContentCatalog(
            new[] { "emberfox", "tidepup" },
            new[] { "ember_bite", "flame_dash", "tail_whip", "rally" },
            new[] { "second_wind", "iron_will" },
            new[] { "shard", "core" });

        private static PlayerSave BuildSave()
        {
            PlayerSave save = PlayerSave.CreateNew();

            OwnedBeast fox = OwnedBeast.Create("b1", "emberfox", 12);
            fox.Progress.Xp = 340;
            fox.Skills.Learn("ember_bite");
            fox.Skills.Learn("flame_dash");
            fox.Skills.GetProgress("flame_dash").Level = 6;
            fox.Skills.GetProgress("flame_dash").Xp = 25;
            fox.Skills.GetProgress("flame_dash").Tier = 1;
            fox.Skills.Equip(0, "flame_dash");
            fox.Skills.Equip(2, "ember_bite");
            save.Beasts.Add(fox);

            OwnedBeast pup = OwnedBeast.Create("b2", "tidepup", 3);
            pup.Skills.Learn("tail_whip");
            pup.Skills.Equip(0, "tail_whip");
            save.Beasts.Add(pup);

            save.Avatar.Level = 7;
            save.Avatar.Xp = 150;
            save.AvatarSkills.Actives.Learn("rally");
            save.AvatarSkills.Actives.Equip(1, "rally");
            save.AvatarSkills.Passives.Learn("second_wind");
            save.AvatarSkills.Passives.Learn("iron_will");
            save.AvatarSkills.Passives.GetProgress("iron_will").Level = 3;
            save.AvatarSkills.Passives.Equip(0, "iron_will");

            save.Materials.Add("shard", 14);
            save.Materials.Add("core", 2);
            save.Materials.MarkCleared("solo", 1);
            save.Materials.MarkCleared("horde", 21);
            save.Materials.SetPity("solo", 2, 5);
            return save;
        }

        private static SaveSerializer NewSerializer(ISaveContentCatalog catalog = null, IEnumerable<ISaveMigration> migrations = null, int version = PlayerSave.CurrentSchemaVersion)
        {
            return new SaveSerializer(new JsonUtilitySaveSerializer(), catalog, migrations, version);
        }

        [Test]
        public void RoundTrip_PreservesEverything()
        {
            SaveSerializer serializer = NewSerializer(Catalog);

            SaveLoadResult result = serializer.Deserialize(serializer.Serialize(BuildSave()));

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsEmpty(result.Issues, string.Join("\n", result.Issues));
            Assert.IsFalse(result.Migrated);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, result.SourceVersion);

            PlayerSave save = result.Save;
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, save.SchemaVersion);
            Assert.AreEqual(2, save.Beasts.Count);

            OwnedBeast fox = save.FindBeast("b1");
            Assert.AreEqual("emberfox", fox.Progress.SpeciesId);
            Assert.AreEqual(12, fox.Progress.Level);
            Assert.AreEqual(340, fox.Progress.Xp);
            Assert.AreEqual(2, fox.Skills.Known.Count);
            Assert.AreEqual("ember_bite", fox.Skills.Known[0].SkillId, "learn order is kept");
            SkillProgress dash = fox.Skills.GetProgress("flame_dash");
            Assert.AreEqual(6, dash.Level);
            Assert.AreEqual(25, dash.Xp);
            Assert.AreEqual(1, dash.Tier);
            Assert.AreEqual("flame_dash", fox.Skills.GetEquipped(0));
            Assert.IsNull(fox.Skills.GetEquipped(1), "empty slot stays empty (null or \"\")");
            Assert.AreEqual("ember_bite", fox.Skills.GetEquipped(2));
            Assert.AreEqual(BeastSkillBook.EquipSlotCount, fox.Skills.Equipped.Length);

            Assert.AreEqual("tidepup", save.FindBeast("b2").Progress.SpeciesId);
            Assert.AreEqual("tail_whip", save.FindBeast("b2").Skills.GetEquipped(0));

            Assert.AreEqual(7, save.Avatar.Level);
            Assert.AreEqual(150, save.Avatar.Xp);
            Assert.AreEqual("rally", save.AvatarSkills.Actives.GetEquipped(1));
            Assert.IsTrue(save.AvatarSkills.Passives.Knows("second_wind"));
            Assert.AreEqual(3, save.AvatarSkills.Passives.GetProgress("iron_will").Level);
            Assert.AreEqual("iron_will", save.AvatarSkills.Passives.GetEquipped(0));

            Assert.AreEqual(14, save.Materials.GetCount("shard"));
            Assert.AreEqual(2, save.Materials.GetCount("core"));
            Assert.IsTrue(save.Materials.HasCleared("solo", 1));
            Assert.IsTrue(save.Materials.HasCleared("horde", 21));
            Assert.IsFalse(save.Materials.HasCleared("solo", 21));
            Assert.AreEqual(5, save.Materials.GetPity("solo", 2));
        }

        [Test]
        public void RoundTrip_IsStable_SecondWriteMatchesFirst()
        {
            SaveSerializer serializer = NewSerializer();
            string first = serializer.Serialize(BuildSave());

            string second = serializer.Serialize(serializer.Deserialize(first).Save);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void Serialize_StampsTheVersion_AndWritesFieldsOnly()
        {
            PlayerSave save = BuildSave();
            save.SchemaVersion = 0;

            string json = NewSerializer().Serialize(save);

            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, save.SchemaVersion);
            StringAssert.Contains("\"SchemaVersion\":" + PlayerSave.CurrentSchemaVersion, json);
            StringAssert.Contains("\"Beasts\":", json);
            StringAssert.DoesNotContain("SlotCount", json, "properties are not save data (JsonUtility never writes them)");
            Assert.IsNull(NewSerializer().Serialize(null));
        }

        [Test]
        public void FreshSave_RoundTrips_Clean()
        {
            SaveSerializer serializer = NewSerializer(Catalog);

            SaveLoadResult result = serializer.Deserialize(serializer.Serialize(PlayerSave.CreateNew()));

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsEmpty(result.Issues);
            Assert.IsEmpty(result.Save.Beasts);
            Assert.AreEqual(1, result.Save.Avatar.Level);
            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, result.Save.AvatarSkills.Passives.Equipped.Length);
        }

        [Test]
        public void SaveTypes_AreSerializable_WithNoDictionaries()
        {
            HashSet<Type> visited = new HashSet<Type>();
            AssertSerializableTree(typeof(PlayerSave), visited);
            Assert.IsTrue(visited.Contains(typeof(BeastProgress)));
            Assert.IsTrue(visited.Contains(typeof(PityCounter)));
        }

        private static void AssertSerializableTree(Type type, HashSet<Type> visited)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || !visited.Add(type))
            {
                return;
            }

            if (type.IsArray)
            {
                AssertSerializableTree(type.GetElementType(), visited);
                return;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                AssertSerializableTree(type.GetGenericArguments()[0], visited);
                return;
            }

            Assert.IsFalse(typeof(IDictionary).IsAssignableFrom(type), type.Name + " must not be a dictionary (JsonUtility drops it)");
            Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), type.Name + " is [Serializable]");
            Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes), type.Name + " has a parameterless constructor");

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                AssertSerializableTree(field.FieldType, visited);
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("this is not json")]
        [TestCase("{\"SchemaVersion\":")]
        public void Load_UnreadableText_FailsWithAReason(string json)
        {
            SaveLoadResult result = NewSerializer().Deserialize(json);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Save);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error));
        }

        [TestCase("{}")]
        [TestCase("{\"SchemaVersion\":0,\"Beasts\":[]}")]
        [TestCase("{\"SchemaVersion\":-3}")]
        public void Load_MissingOrInvalidVersion_Fails(string json)
        {
            SaveLoadResult result = NewSerializer().Deserialize(json);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("SchemaVersion", result.Error);
        }

        [Test]
        public void Load_NewerVersion_FailsWithoutTouchingIt()
        {
            SaveLoadResult result = NewSerializer().Deserialize("{\"SchemaVersion\":" + (PlayerSave.CurrentSchemaVersion + 1) + "}");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion + 1, result.SourceVersion);
            StringAssert.Contains("newer build", result.Error);
        }

        [Test]
        public void Load_MissingSections_AreFilledWithDefaults()
        {
            SaveLoadResult result = NewSerializer().Deserialize("{\"SchemaVersion\":1,\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":4}}]}");

            Assert.IsTrue(result.Success, result.Error);
            PlayerSave save = result.Save;
            Assert.AreEqual(4, save.FindBeast("b1").Progress.Level);
            Assert.IsNotNull(save.FindBeast("b1").Skills);
            Assert.IsNotNull(save.FindBeast("b1").Skills.Known);
            Assert.AreEqual(1, save.Avatar.Level);
            Assert.IsNotNull(save.AvatarSkills.Actives);
            Assert.IsNotNull(save.Materials.Pity);
        }

        [Test]
        public void EnsureInitialized_RepairsNulls()
        {
            PlayerSave save = new PlayerSave
            {
                Beasts = new List<OwnedBeast> { null, new OwnedBeast { BeastId = "b1", Progress = null, Skills = null } },
                Avatar = null,
                AvatarSkills = new AvatarSkillBook { Actives = null },
                Materials = new MaterialInventory { Pity = null }
            };
            save.AvatarSkills.Passives.Equipped = null;

            Assert.Greater(save.EnsureInitialized(), 0);

            Assert.AreEqual(1, save.Beasts.Count);
            Assert.IsNotNull(save.Beasts[0].Progress);
            Assert.AreEqual(BeastSkillBook.EquipSlotCount, save.Beasts[0].Skills.Equipped.Length);
            Assert.IsNotNull(save.Avatar);
            Assert.IsNotNull(save.AvatarSkills.Actives);
            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, save.AvatarSkills.Passives.Equipped.Length);
            Assert.IsNotNull(save.Materials.Pity);
            Assert.AreEqual(0, save.EnsureInitialized(), "a repaired save needs nothing more");
        }

        [Test]
        public void Load_UnknownIds_AreReported_NotFatal()
        {
            PlayerSave save = BuildSave();
            save.Beasts.Add(OwnedBeast.Create("b3", "gone_species", 5));
            save.Beasts[0].Skills.Learn("removed_skill");
            save.AvatarSkills.Actives.Learn("old_active");
            save.AvatarSkills.Passives.Learn("old_passive");
            save.Materials.Add("old_material", 3);
            SaveSerializer serializer = NewSerializer(Catalog);

            SaveLoadResult result = serializer.Deserialize(serializer.Serialize(save));

            Assert.IsTrue(result.Success, result.Error);
            AssertIssue(result.Issues, SaveIssueKind.UnknownSpecies, "gone_species", "Beasts[2].Progress.SpeciesId");
            AssertIssue(result.Issues, SaveIssueKind.UnknownSkill, "removed_skill", "Beasts[0].Skills.Known[2].SkillId");
            AssertIssue(result.Issues, SaveIssueKind.UnknownSkill, "old_active", "AvatarSkills.Actives.Known[1].SkillId");
            AssertIssue(result.Issues, SaveIssueKind.UnknownPassive, "old_passive", "AvatarSkills.Passives.Known[2].SkillId");
            AssertIssue(result.Issues, SaveIssueKind.UnknownMaterial, "old_material", "Materials.Materials[2].MaterialId");
            Assert.AreEqual(5, result.Issues.Count, string.Join("\n", result.Issues));

            Assert.IsTrue(result.Save.Beasts[0].Skills.Knows("removed_skill"), "reported, not removed");
            Assert.AreEqual(3, result.Save.Materials.GetCount("old_material"));
        }

        [Test]
        public void Validate_WithoutCatalog_RunsOnlyStructuralChecks()
        {
            PlayerSave save = BuildSave();
            save.Beasts.Add(OwnedBeast.Create("b3", "anything_goes", 5));

            Assert.IsEmpty(SaveValidator.Validate(save, null));
            Assert.IsEmpty(SaveValidator.Validate(null, Catalog));
        }

        [Test]
        public void Validate_ReportsStructuralProblems()
        {
            PlayerSave save = BuildSave();
            save.Beasts.Add(OwnedBeast.Create("b1", "tidepup", 1));
            save.Beasts.Add(OwnedBeast.Create(null, "tidepup", 1));
            save.Beasts[0].Progress.Level = 0;
            save.Beasts[0].Skills.Known.Add(new SkillProgress("ember_bite"));
            save.Beasts[1].Skills.Equipped[2] = "flame_dash";
            save.Avatar.Level = AvatarProgression.MaxLevel + 1;
            save.Materials.Materials[0].Quantity = 0;
            save.Materials.Pity[0].Misses = -1;

            List<SaveIssue> issues = SaveValidator.Validate(save, Catalog);

            AssertIssue(issues, SaveIssueKind.DuplicateBeastId, "b1", "Beasts[2].BeastId");
            AssertIssue(issues, SaveIssueKind.MissingBeastId, null, "Beasts[3].BeastId");
            AssertIssue(issues, SaveIssueKind.InvalidValue, null, "Beasts[0].Progress.Level");
            AssertIssue(issues, SaveIssueKind.DuplicateSkill, "ember_bite", "Beasts[0].Skills.Known[2].SkillId");
            AssertIssue(issues, SaveIssueKind.EquippedNotLearned, "flame_dash", "Beasts[1].Skills.Equipped[2]");
            AssertIssue(issues, SaveIssueKind.InvalidValue, null, "Avatar.Level");
            AssertIssue(issues, SaveIssueKind.InvalidValue, null, "Materials.Materials[0].Quantity");
            AssertIssue(issues, SaveIssueKind.InvalidValue, null, "Materials.Pity[0].Misses");
            Assert.AreEqual(8, issues.Count, string.Join("\n", issues));
        }

        [Test]
        public void Validate_AStarterSaveBuiltFromTheAuthoredData_IsClean()
        {
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            SaveContentCatalog catalog = SaveContentCatalog.FromData(roster, library);
            PlayerSave save = PlayerSave.CreateNew();

            for (int i = 0; i < library.SpeciesKits.Length; i++)
            {
                SpeciesKitData kit = library.SpeciesKits[i];
                OwnedBeast beast = OwnedBeast.Create("starter_" + i, kit.SpeciesId, 5);

                for (int slot = 0; slot < kit.DefaultLoadout.Length; slot++)
                {
                    beast.Skills.Learn(kit.DefaultLoadout[slot]);
                    Assert.AreEqual(SkillEquipResult.Equipped, beast.Skills.Equip(slot, kit.DefaultLoadout[slot]));
                }

                save.Beasts.Add(beast);
            }

            for (int i = 0; i < SkillLibraryData.AvatarDefaultActiveCount; i++)
            {
                save.AvatarSkills.Actives.Learn(library.AvatarActives[i].SkillId);
                save.AvatarSkills.Actives.Equip(i, library.AvatarActives[i].SkillId);
            }

            for (int i = 0; i < library.AvatarDefaultPassives.Length; i++)
            {
                save.AvatarSkills.Passives.Learn(library.AvatarDefaultPassives[i]);
                save.AvatarSkills.Passives.Equip(i, library.AvatarDefaultPassives[i]);
            }

            save.Materials.Add(library.Materials[0].MaterialId, 1);
            SaveSerializer serializer = NewSerializer(catalog);

            SaveLoadResult result = serializer.Deserialize(serializer.Serialize(save));

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(roster.Species.Length, result.Save.Beasts.Count);
            Assert.IsEmpty(result.Issues, string.Join("\n", result.Issues));
            Assert.IsFalse(catalog.IsKnownSpecies("not_a_beast"));
        }

        [Test]
        public void Migrations_RunInOrder_FromTheSavedVersion()
        {
            // A pretend schema 3: v1 -> v2 renames a material (typed step through PlayerSave),
            // v2 -> v3 raises every beast's level floor to 2 (another typed step).
            List<ISaveMigration> steps = new List<ISaveMigration>
            {
                new LevelFloorMigration(2, 2),
                new RenameMaterialMigration(1, "old_shard", "shard")
            };
            SaveSerializer v3 = NewSerializer(Catalog, steps, 3);
            PlayerSave legacy = PlayerSave.CreateNew();
            legacy.Beasts.Add(OwnedBeast.Create("b1", "emberfox", 1));
            legacy.Materials.Add("old_shard", 9);
            string v1Json = NewSerializer().Serialize(legacy);

            SaveLoadResult fromV1 = v3.Deserialize(v1Json);

            Assert.IsTrue(fromV1.Success, fromV1.Error);
            Assert.IsTrue(fromV1.Migrated);
            Assert.AreEqual(1, fromV1.SourceVersion);
            Assert.AreEqual(3, fromV1.Save.SchemaVersion);
            Assert.AreEqual(9, fromV1.Save.Materials.GetCount("shard"));
            Assert.AreEqual(0, fromV1.Save.Materials.GetCount("old_shard"));
            Assert.AreEqual(2, fromV1.Save.FindBeast("b1").Progress.Level);
            Assert.IsEmpty(fromV1.Issues, string.Join("\n", fromV1.Issues));

            SaveLoadResult fromV2 = v3.Deserialize(v1Json.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2"));

            Assert.IsTrue(fromV2.Success, fromV2.Error);
            Assert.AreEqual(2, fromV2.SourceVersion);
            Assert.AreEqual(9, fromV2.Save.Materials.GetCount("old_shard"), "only the v2 -> v3 step ran");

            StringAssert.Contains("\"SchemaVersion\":3", v3.Serialize(fromV1.Save));
        }

        [Test]
        public void Migrations_MissingOrFailingStep_FailsTheLoad()
        {
            string v1Json = NewSerializer().Serialize(BuildSave());

            SaveLoadResult missing = NewSerializer(null, new[] { new LevelFloorMigration(2, 2) }, 3).Deserialize(v1Json);
            SaveLoadResult throwing = NewSerializer(null, new ISaveMigration[] { new ThrowingMigration(1) }, 2).Deserialize(v1Json);

            Assert.IsFalse(missing.Success);
            StringAssert.Contains("schema 1 to 2", missing.Error);
            Assert.IsFalse(throwing.Success);
            StringAssert.Contains("boom", throwing.Error);
        }

        [Test]
        public void Constructor_RejectsDuplicateStepsAndBadArguments()
        {
            Assert.Throws<ArgumentException>(() => NewSerializer(null, new[] { new LevelFloorMigration(1, 2), new LevelFloorMigration(1, 3) }, 2));
            Assert.Throws<ArgumentNullException>(() => new SaveSerializer(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSerializer(null, null, 0));
            Assert.IsEmpty(SaveMigrations.All(), "schema 1 has no upgrades yet");
        }

        [Test]
        public void InjectedSerializer_Failures_AreReported()
        {
            SaveSerializer serializer = new SaveSerializer(new ThrowingJson());

            SaveLoadResult result = serializer.Deserialize("{\"SchemaVersion\":1}");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("not readable", result.Error);
        }

        [Test]
        public void SaveStore_SavesAndLoadsBySlot()
        {
            MemoryStorage storage = new MemoryStorage();
            SaveStore store = new SaveStore(storage, NewSerializer(Catalog));

            Assert.IsFalse(store.Exists("main"));
            Assert.IsFalse(store.Load("main").Success);
            Assert.IsTrue(store.Save("main", BuildSave()));
            Assert.IsTrue(store.Exists("main"));
            Assert.IsFalse(store.Save("main", null));

            SaveLoadResult loaded = store.Load("main");

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(14, loaded.Save.Materials.GetCount("shard"));

            storage.FailWrites = true;
            Assert.IsFalse(store.Save("main", BuildSave()));
        }

        private static void AssertIssue(List<SaveIssue> issues, SaveIssueKind kind, string id, string path)
        {
            bool found = issues.Exists(i => i.Kind == kind && i.Id == id && i.Path == path);
            Assert.IsTrue(found, "Expected " + kind + " for '" + id + "' at " + path + ", got:\n" + string.Join("\n", issues));
        }

        private class RenameMaterialMigration : ISaveMigration
        {
            private readonly string _from;
            private readonly string _to;

            public RenameMaterialMigration(int fromVersion, string from, string to)
            {
                FromVersion = fromVersion;
                _from = from;
                _to = to;
            }

            public int FromVersion { get; private set; }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                foreach (MaterialStack stack in save.Materials.Materials)
                {
                    if (stack.MaterialId == _from)
                    {
                        stack.MaterialId = _to;
                    }
                }

                return serializer.ToJson(save);
            }
        }

        private class LevelFloorMigration : ISaveMigration
        {
            private readonly int _floor;

            public LevelFloorMigration(int fromVersion, int floor)
            {
                FromVersion = fromVersion;
                _floor = floor;
            }

            public int FromVersion { get; private set; }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                foreach (OwnedBeast beast in save.Beasts)
                {
                    beast.Progress.Level = Math.Max(beast.Progress.Level, _floor);
                }

                return serializer.ToJson(save);
            }
        }

        private class ThrowingMigration : ISaveMigration
        {
            public ThrowingMigration(int fromVersion)
            {
                FromVersion = fromVersion;
            }

            public int FromVersion { get; private set; }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private class ThrowingJson : ISaveJsonSerializer
        {
            public string ToJson(object value)
            {
                throw new InvalidOperationException("no");
            }

            public T FromJson<T>(string json)
            {
                throw new InvalidOperationException("no");
            }
        }

        private class MemoryStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _slots = new Dictionary<string, string>();

            public bool FailWrites;

            public bool Exists(string slot)
            {
                return slot != null && _slots.ContainsKey(slot);
            }

            public bool TryRead(string slot, out string contents)
            {
                contents = null;
                return slot != null && _slots.TryGetValue(slot, out contents);
            }

            public bool TryWrite(string slot, string contents)
            {
                if (FailWrites || slot == null)
                {
                    return false;
                }

                _slots[slot] = contents;
                return true;
            }

            public bool Delete(string slot)
            {
                return slot != null && _slots.Remove(slot);
            }
        }
    }
}
