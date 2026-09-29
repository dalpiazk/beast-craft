using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="FieldJson"/> is trim-safe: every save root and data file has a source-generated
    /// contract from its own assembly's context (Core's <c>CoreJsonContext</c>, Presentation's
    /// <c>PresentationJsonContext</c>), a type no context lists throws rather than falling back to
    /// reflection, and the public-field rules still hold on generated contracts.
    /// </summary>
    public class FieldJsonContractTests
    {
        [Test]
        public void EveryDataFileAndSaveRoot_HasAGeneratedContract_FromItsOwnAssembly()
        {
            List<Type> roots = new List<Type> { typeof(PlayerSave), typeof(SaveSerializer.SaveHeader), typeof(PlayerSettings) };
            foreach (Assembly assembly in new[] { typeof(PlayerSave).Assembly, typeof(GameContent).Assembly })
            {
                foreach (Type type in assembly.GetTypes())
                {
                    FieldInfo path = type.GetField("ProjectRelativePath", BindingFlags.Public | BindingFlags.Static);
                    if (path != null && path.IsLiteral && path.FieldType == typeof(string))
                    {
                        roots.Add(type);
                    }
                }
            }

            Assert.GreaterOrEqual(roots.Count, 29, "the three save roots and the data files");
            foreach (Type type in roots)
            {
                Assert.IsTrue(FieldJson.TryGetContract(type, out JsonTypeInfo contract),
                              type.FullName + " has no generated contract: add a [JsonSerializable] line to its assembly's context");
                Assert.IsInstanceOf<JsonSerializerContext>(contract.OriginatingResolver, type.FullName);
                Assert.AreSame(type.Assembly, contract.OriginatingResolver.GetType().Assembly,
                               type.FullName + " must be listed in its own assembly's context, not a test context");
            }
        }

        [Test]
        public void ATypeNoContextLists_Throws_InsteadOfFallingBackToReflection()
        {
            Assert.IsFalse(FieldJson.TryGetContract(typeof(Unlisted), out _));
            Assert.Throws<NotSupportedException>(() => FieldJson.ToJson(new Unlisted()));
            Assert.Throws<NotSupportedException>(() => FieldJson.FromJson<Unlisted>("{}"));
        }

        [Test]
        public void GeneratedContracts_KeepThePublicFieldRules()
        {
            RulesProbe probe = new RulesProbe { Kept = 1, Skipped = 2, Property = 3 };

            Assert.AreEqual("{\"Kept\":1,\"Enum\":1}", FieldJson.ToJson(probe));

            RulesProbe read = FieldJson.FromJson<RulesProbe>("{\"kept\":5,\"Kept\":6,\"Skipped\":7,\"Property\":8,\"Fixed\":9,\"Extra\":1}");
            Assert.AreEqual(6, read.Kept, "names are case-sensitive; unknown keys are ignored");
            Assert.AreEqual(0, read.Skipped, "[NonSerialized] fields are not read");
            Assert.AreEqual(0, read.Property, "properties are not read");
            Assert.AreEqual(4, read.Fixed, "readonly fields are not read");
        }

        private class Unlisted
        {
            public int Value = 1;
        }

        internal class RulesProbe
        {
            public int Kept;

            [NonSerialized]
            public int Skipped;

            public readonly int Fixed = 4;

            public ProbeKind Enum = ProbeKind.Second;

            public int Property { get; set; }
        }

        internal enum ProbeKind
        {
            First,
            Second
        }
    }

    /// <summary>
    /// The contracts for the runtime types the tests themselves write through <see cref="FieldJson"/>
    /// (to compare two objects field by field) and for <see cref="FieldJsonContractTests"/>' probe.
    /// Test-only: the game never serializes these.
    /// </summary>
    [JsonSourceGenerationOptions(IncludeFields = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(CreatureSpeciesSO))]
    [JsonSerializable(typeof(SkillSO))]
    [JsonSerializable(typeof(PassiveSkillSO))]
    [JsonSerializable(typeof(FieldJsonContractTests.RulesProbe))]
    internal partial class TestJsonContext : JsonSerializerContext
    {
        [ModuleInitializer]
        internal static void Register()
        {
            FieldJson.AddContracts(Default);
        }
    }
}
