using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using BeastCraft.Presentation.Art;
using BeastCraft.Presentation.Text;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// The source-generated JSON contracts for Presentation's own data files, the Presentation half of
    /// Core's <c>CoreJsonContext</c> (see <see cref="FieldJson"/>): no runtime reflection over the
    /// types, so they load the same in a trimmed build. A new Presentation data file gets a
    /// <c>[JsonSerializable]</c> line here.
    /// </summary>
    [JsonSourceGenerationOptions(IncludeFields = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(BattleArtData))]
    [JsonSerializable(typeof(GlossaryData))]
    [JsonSerializable(typeof(UiStyleData))]
    internal partial class PresentationJsonContext : JsonSerializerContext
    {
        /// <summary>Hands the contracts to <see cref="FieldJson"/> when this assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            FieldJson.AddContracts(Default);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>The C# 9 module initializer marker, which netstandard2.1 does not ship; the compiler only needs the name.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
