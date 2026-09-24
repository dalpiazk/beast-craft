using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BeastCraft
{
    /// <summary>
    /// The game's JSON reader/writer for saves, settings and data files, over System.Text.Json.
    /// <para>
    /// It keeps the rules every save DTO and data file was written against (Unity's JsonUtility
    /// rules): public instance fields only (no properties, no <c>readonly</c> fields, no
    /// <c>[NonSerialized]</c> fields), names exactly as declared and case-sensitive, enums as
    /// numbers, unknown keys ignored on read. It is a verbatim port of the System.Text.Json
    /// implementation the save and data tests have always run on (the options and the field filter
    /// are unchanged), so saves are written byte-for-byte as before; the golden save fixtures in
    /// <c>Tooling/EditModeTests/Goldens</c> pin that.
    /// </para>
    /// </summary>
    public static class FieldJson
    {
        private static readonly JsonSerializerOptions CompactOptions = CreateOptions(false);
        private static readonly JsonSerializerOptions PrettyOptions = CreateOptions(true);

        /// <summary>Reads <paramref name="json"/>; malformed or empty input throws (a JsonException / ArgumentNullException).</summary>
        public static T FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, CompactOptions);
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

            return JsonSerializer.Serialize(obj, obj.GetType(), prettyPrint ? PrettyOptions : CompactOptions);
        }

        private static JsonSerializerOptions CreateOptions(bool prettyPrint)
        {
            DefaultJsonTypeInfoResolver resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(KeepOnlySerializedFields);

            return new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = prettyPrint,
                TypeInfoResolver = resolver
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
    }
}
