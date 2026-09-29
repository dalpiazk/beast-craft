using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BeastCraft
{
    /// <summary>
    /// The game's JSON reader/writer for saves, settings and data files, over System.Text.Json.
    /// <para>
    /// It keeps the rules every save DTO and data file was written against (Unity's JsonUtility
    /// rules): public instance fields only (no properties, no <c>readonly</c> fields, no
    /// <c>[NonSerialized]</c> fields), names exactly as declared and case-sensitive, enums as
    /// numbers, unknown keys ignored on read. Saves are written byte-for-byte as they always were;
    /// the golden save fixtures in <c>Tooling/EditModeTests/Goldens</c> pin that.
    /// </para>
    /// <para>
    /// Trim-safe: the contracts come only from source-generated contexts (<see cref="CoreJsonContext"/>,
    /// Presentation's <c>PresentationJsonContext</c>, added with <see cref="AddContracts"/>), never from
    /// runtime reflection over the types, so a trimmed build (Android release, a trimmed desktop publish)
    /// reads and writes the same JSON. A type no context lists throws <see cref="NotSupportedException"/>
    /// instead of silently falling back to reflection. The one reflection call left,
    /// <see cref="KeepOnlySerializedFields"/>, reads the <see cref="FieldInfo"/> the generated code hands
    /// over for each member (the generator references those fields statically, so trimming keeps them).
    /// </para>
    /// </summary>
    public static class FieldJson
    {
        private static readonly object Gate = new object();
        private static JsonSerializerContext[] _contexts = { CoreJsonContext.Default };

        private static readonly JsonSerializerOptions CompactOptions = CreateOptions(false);
        private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(true);

        /// <summary>
        /// Adds another assembly's source-generated contracts (Presentation's data files; a test
        /// assembly's own types). Call it from that assembly's module initializer: FieldJson runs a
        /// type's module initializer before giving up on it, so the contracts are always in place
        /// before the assembly's first type is read or written.
        /// </summary>
        public static void AddContracts(JsonSerializerContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (Gate)
            {
                if (Array.IndexOf(_contexts, context) >= 0)
                {
                    return;
                }

                JsonSerializerContext[] grown = new JsonSerializerContext[_contexts.Length + 1];
                Array.Copy(_contexts, grown, _contexts.Length);
                grown[_contexts.Length] = context;
                _contexts = grown;
            }
        }

        /// <summary>Reads <paramref name="json"/>; malformed or empty input throws (a JsonException / ArgumentNullException).</summary>
        public static T FromJson<T>(string json)
        {
            return (T)JsonSerializer.Deserialize(json, CompactOptions.GetTypeInfo(typeof(T)));
        }

        public static string ToJson(object obj)
        {
            return ToJson(obj, false);
        }

        /// <summary>Writes <paramref name="obj"/> by its runtime type; null writes an empty string.</summary>
        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            JsonSerializerOptions options = prettyPrint ? PrettyOptions : CompactOptions;
            return JsonSerializer.Serialize(obj, options.GetTypeInfo(obj.GetType()));
        }

        /// <summary>
        /// The contract FieldJson uses for <paramref name="type"/>, or false when no registered context
        /// lists it (reading or writing it would throw). For tests and diagnostics.
        /// </summary>
        public static bool TryGetContract(Type type, out JsonTypeInfo contract)
        {
            return CompactOptions.TryGetTypeInfo(type, out contract);
        }

        private static JsonSerializerOptions CreateOptions(bool prettyPrint)
        {
            // Field inclusion comes from the contexts ([JsonSourceGenerationOptions(IncludeFields = true)]);
            // everything else is the System.Text.Json default, as before.
            return new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = prettyPrint,
                TypeInfoResolver = new ContractResolver()
            };
        }

        /// <summary>Drops every member the rules exclude: properties, readonly fields and [NonSerialized] fields.</summary>
        private static void KeepOnlySerializedFields(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object)
            {
                return;
            }

            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                FieldInfo field = typeInfo.Properties[i].AttributeProvider as FieldInfo;

                if (field == null || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }

        /// <summary>The registered contexts, first match wins, filtered to the public-field rules.</summary>
        private sealed class ContractResolver : IJsonTypeInfoResolver
        {
            public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                JsonTypeInfo typeInfo = Find(type, options);
                if (typeInfo == null)
                {
                    // The type's assembly may not have run its module initializer (the one that calls
                    // AddContracts) yet, e.g. a data type read before any of its assembly's code ran.
                    // The runtime runs a module initializer at most once, so this is cheap and safe.
                    RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
                    typeInfo = Find(type, options);
                }

                if (typeInfo != null)
                {
                    KeepOnlySerializedFields(typeInfo);
                }

                return typeInfo;
            }

            private static JsonTypeInfo Find(Type type, JsonSerializerOptions options)
            {
                JsonSerializerContext[] contexts = _contexts;
                foreach (IJsonTypeInfoResolver context in contexts)
                {
                    JsonTypeInfo typeInfo = context.GetTypeInfo(type, options);
                    if (typeInfo != null)
                    {
                        return typeInfo;
                    }
                }

                return null;
            }
        }
    }
}
