using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Golden save fixtures for every schema version (1-5): each committed input is loaded (and
    /// migrated) by <see cref="SaveSerializer"/> and written back, and the text must equal the
    /// committed expected output byte for byte. The fixtures and outputs were captured on the
    /// Unity-era JsonUtility serializer; the engine-neutral serializer must reproduce them exactly.
    /// <para>
    /// <c>rich-v5</c> is a save with every field of every save DTO filled (generated once, by
    /// reflection, then frozen as a file); <c>rich-v1</c>..<c>rich-v4</c> are the same text stamped
    /// with an older SchemaVersion, so every migration runs over every field. The <c>min-v*</c>
    /// inputs are the small hand-written saves of each era the migration tests use.
    /// </para>
    /// </summary>
    public class GoldenSaveTests
    {
        private static readonly string[] MinimalInputs =
        {
            "{\"SchemaVersion\":1,\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":4}}]}",
            "{\"SchemaVersion\":2," +
            "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":40,\"Xp\":120}," +
            "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]}]," +
            "\"Avatar\":{\"Level\":38,\"Xp\":40}," +
            "\"Materials\":{\"Materials\":[],\"ClearedCells\":[],\"Pity\":[]}," +
            "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]}",
            "{\"SchemaVersion\":3," +
            "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"griffin\",\"Level\":14,\"Xp\":5,\"BankedXp\":0}," +
            "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]}]," +
            "\"Avatar\":{\"Level\":9,\"Xp\":3}," +
            "\"Materials\":{\"Materials\":[{\"MaterialId\":\"essence_shard\",\"Quantity\":2}],\"ClearedCells\":[],\"Pity\":[]}," +
            "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]," +
            "\"Campaign\":{\"Seals\":[],\"Regions\":[{\"RegionId\":\"r01\",\"StagesCleared\":2,\"BossCleared\":false}],\"CurrentRegionId\":\"r01\"," +
            "\"ActiveRun\":{\"RegionId\":\"\",\"Stage\":0,\"Seed\":0,\"Nodes\":[],\"CurrentNodeId\":-1,\"Cleared\":[],\"Attempts\":0,\"NodeAttempts\":0}}}",
            "{\"SchemaVersion\":4," +
            "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"griffin\",\"Level\":14,\"Xp\":5,\"BankedXp\":0}," +
            "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]," +
            "\"Appearance\":{\"OptionEntries\":[],\"ColorEntries\":[{\"CategoryId\":\"fur\",\"Color\":{\"r\":0.1,\"g\":0.25,\"b\":0.7,\"a\":1}}]}}]," +
            "\"Avatar\":{\"Level\":9,\"Xp\":3}," +
            "\"Materials\":{\"Materials\":[{\"MaterialId\":\"essence_shard\",\"Quantity\":2}],\"ClearedCells\":[],\"Pity\":[]}," +
            "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]," +
            "\"Campaign\":{\"Seals\":[],\"Regions\":[{\"RegionId\":\"r01\",\"StagesCleared\":2,\"BossCleared\":false}],\"CurrentRegionId\":\"r01\"," +
            "\"ActiveRun\":{\"RegionId\":\"\",\"Stage\":0,\"Seed\":0,\"Nodes\":[],\"CurrentNodeId\":-1,\"Cleared\":[],\"Attempts\":0,\"NodeAttempts\":0}}," +
            "\"Gold\":77,\"Consumables\":[],\"Shops\":[],\"Cosmetics\":{\"Unlocked\":[]}," +
            "\"AvatarAppearance\":{\"OptionEntries\":[],\"ColorEntries\":[]}}",
            "{\"SchemaVersion\":5}"
        };

        private static SaveSerializer NewSerializer()
        {
            return new SaveSerializer(new JsonSaveSerializer());
        }

        private static string RichV5()
        {
            if (GoldenFiles.Updating)
            {
                PlayerSave save = new PlayerSave();
                int seed = 1;
                Populate(save, ref seed, 0);
                GoldenFiles.Write("Saves/rich-v5.input.json", NewSerializer().Serialize(save));
            }

            return GoldenFiles.Read("Saves/rich-v5.input.json");
        }

        [Test]
        public void RichV5_RoundTripsByteIdentical()
        {
            string input = RichV5();
            SaveLoadResult result = NewSerializer().Deserialize(input);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsFalse(result.Migrated);
            Assert.AreEqual(input, NewSerializer().Serialize(result.Save), "a current-schema save must write back exactly as read");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void RichSave_LoadsAndMigrates_ToTheGoldenText(int version)
        {
            string input = RichV5().Replace("\"SchemaVersion\":5", "\"SchemaVersion\":" + version);
            AssertGolden("rich-v" + version, input, version);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void MinimalSave_LoadsAndMigrates_ToTheGoldenText(int version)
        {
            AssertGolden("min-v" + version, MinimalInputs[version - 1], version);
        }

        [Test]
        public void PlayerSettings_RoundTripToTheGoldenText()
        {
            JsonSaveSerializer json = new JsonSaveSerializer();
            PlayerSettings settings = json.FromJson<PlayerSettings>("{\"SchemaVersion\":1,\"TeamSuggestionsEnabled\":false,\"Unknown\":3}");

            GoldenFiles.AssertMatches("Saves/settings.expected.json", json.ToJson(settings) + "\n" + json.ToJson(new PlayerSettings()));
        }

        private static void AssertGolden(string name, string input, int version)
        {
            SaveLoadResult result = NewSerializer().Deserialize(input);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(version, result.SourceVersion);
            Assert.AreEqual(version < PlayerSave.CurrentSchemaVersion, result.Migrated);

            string output = NewSerializer().Serialize(result.Save);
            GoldenFiles.AssertMatches("Saves/" + name + ".expected.json", output);

            // Written text reads back to itself: the saved form is a fixed point.
            SaveLoadResult again = NewSerializer().Deserialize(output);
            Assert.IsTrue(again.Success, again.Error);
            Assert.AreEqual(output, NewSerializer().Serialize(again.Save));
        }

        // ---- Deterministic fill of every serialized field (used only to capture rich-v5) ----

        private static void Populate(object target, ref int seed, int depth)
        {
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    continue;
                }

                object value = Make(field.FieldType, ref seed, depth + 1);

                if (value != null)
                {
                    field.SetValue(target, value);
                }
            }
        }

        private static object Make(Type type, ref int seed, int depth)
        {
            if (depth > 12)
            {
                return null;
            }

            if (type == typeof(string))
            {
                return "s" + seed++;
            }

            if (type == typeof(int))
            {
                return seed++;
            }

            if (type == typeof(long))
            {
                return 1700000000000L + seed++;
            }

            if (type == typeof(float))
            {
                return (seed++ * 0.25f) + 0.1f;
            }

            if (type == typeof(double))
            {
                return (seed++ * 0.25) + 0.1;
            }

            if (type == typeof(bool))
            {
                return seed++ % 2 == 0;
            }

            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                return values.GetValue(seed++ % values.Length);
            }

            if (type.IsArray)
            {
                Type element = type.GetElementType();
                Array array = Array.CreateInstance(element, 2);

                for (int i = 0; i < 2; i++)
                {
                    array.SetValue(Make(element, ref seed, depth + 1), i);
                }

                return array;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = type.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(type);

                for (int i = 0; i < 2; i++)
                {
                    list.Add(Make(element, ref seed, depth + 1));
                }

                return list;
            }

            bool ours = type.Namespace != null && type.Namespace.StartsWith("BeastCraft", StringComparison.Ordinal);
            bool colour = type.Name == "Color";

            if ((ours || colour) && (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null))
            {
                object instance = Activator.CreateInstance(type);
                Populate(instance, ref seed, depth);
                return instance;
            }

            return null;
        }
    }
}
