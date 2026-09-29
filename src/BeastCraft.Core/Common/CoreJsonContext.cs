using System.Text.Json.Serialization;
using BeastCraft.Campaign;
using BeastCraft.Creatures.Roster;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Idle;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;
using BeastCraft.Tutorial;
using BeastCraft.Vfx;

namespace BeastCraft
{
    /// <summary>
    /// The source-generated JSON contracts for every Core type <see cref="FieldJson"/> reads or writes:
    /// the save and its header, the settings, and each content data file under <c>content/data/</c>.
    /// Generated at build time, so reading and writing them needs no runtime reflection over the types
    /// and survives trimming (Android release builds, a trimmed desktop publish).
    /// <para>
    /// Only the roots are listed: the generator follows their fields and adds every nested type. A new
    /// save root or data file gets a <c>[JsonSerializable]</c> line here (Presentation's own files go in
    /// <c>PresentationJsonContext</c>); without one <see cref="FieldJson"/> throws for it, and
    /// <c>FieldJsonContractTests</c> fails for any type with a <c>ProjectRelativePath</c>.
    /// </para>
    /// <para>
    /// Metadata mode only (no generated fast-path writers), so every read and write goes through the
    /// contracts <see cref="FieldJson"/> filters to its public-field rules.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(IncludeFields = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(PlayerSave))]
    [JsonSerializable(typeof(SaveSerializer.SaveHeader))]
    [JsonSerializable(typeof(PlayerSettings))]
    [JsonSerializable(typeof(AchievementLibraryData))]
    [JsonSerializable(typeof(ArtManifestData))]
    [JsonSerializable(typeof(BattleLayoutData))]
    [JsonSerializable(typeof(BeastRosterData))]
    [JsonSerializable(typeof(ConsumableLibraryData))]
    [JsonSerializable(typeof(CosmeticLibraryData))]
    [JsonSerializable(typeof(DialogueLibraryData))]
    [JsonSerializable(typeof(DiscoveryLibraryData))]
    [JsonSerializable(typeof(DropTableData))]
    [JsonSerializable(typeof(EncounterDifficultyData))]
    [JsonSerializable(typeof(EncounterLibraryData))]
    [JsonSerializable(typeof(EnemyLibraryData))]
    [JsonSerializable(typeof(ExpeditionLibraryData))]
    [JsonSerializable(typeof(GardenLibraryData))]
    [JsonSerializable(typeof(GearLibraryData))]
    [JsonSerializable(typeof(GroveLibraryData))]
    [JsonSerializable(typeof(HintLibraryData))]
    [JsonSerializable(typeof(IdleRewardsData))]
    [JsonSerializable(typeof(LocationNameTableData))]
    [JsonSerializable(typeof(RegionLibraryData))]
    [JsonSerializable(typeof(ShopTableData))]
    [JsonSerializable(typeof(SkillLibraryData))]
    [JsonSerializable(typeof(VfxLibraryData))]
    internal partial class CoreJsonContext : JsonSerializerContext
    {
    }
}
