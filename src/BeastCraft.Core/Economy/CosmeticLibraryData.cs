using System;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The plain-data shape of <c>data/Cosmetics/cosmetic-library.json</c>: every cosmetic category
    /// (the avatar's, and each beast species' own — looks are per species, not per body type), their
    /// options and where each option comes from, and the milestones that unlock some of them.
    /// Purely cosmetic: nothing here has stats. <see cref="CosmeticLibraryValidator"/> checks it;
    /// <see cref="CosmeticLibrary.Build"/> indexes it; the Editor importer turns it into
    /// <c>CustomizationCategoryDefinition</c> assets and the avatar's and each species'
    /// <c>CustomizationSchema</c>.
    /// </summary>
    [Serializable]
    public class CosmeticLibraryData
    {
        /// <summary>Path of the file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Cosmetics/cosmetic-library.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        public CosmeticCategoryData[] Categories = new CosmeticCategoryData[0];

        public MilestoneData[] Milestones = new MilestoneData[0];
    }

    /// <summary>One category (a hair style, a crest, wing markings, a colour).</summary>
    [Serializable]
    public class CosmeticCategoryData
    {
        /// <summary>Globally unique, lowercase snake_case (unlock keys are <c>"categoryId/optionId"</c>).</summary>
        public string CategoryId;

        public string DisplayName;

        /// <summary><c>avatar</c>, or the <c>SpeciesId</c> whose beasts wear it.</summary>
        public string Scope;

        /// <summary><c>DiscreteOption</c> or <c>ColorPicker</c> (a <c>CustomizationValueType</c> name).</summary>
        public string ValueType = "DiscreteOption";

        /// <summary>A colour category's default, as <c>#RRGGBB</c>. Colour pickers are always free.</summary>
        public string DefaultColor = "#FFFFFF";

        /// <summary>A discrete category's options (a colour category has none).</summary>
        public CosmeticOptionData[] Options = new CosmeticOptionData[0];
    }

    /// <summary>One look.</summary>
    [Serializable]
    public class CosmeticOptionData
    {
        /// <summary>Unique within its category, lowercase snake_case.</summary>
        public string OptionId;

        public string DisplayName;

        /// <summary>Exactly one option per discrete category is the default (Source <c>default</c>).</summary>
        public bool IsDefault;

        public int SortOrder;

        public bool SupportsPaletteShader;

        /// <summary>0 common, 1 rare (the Trader's price tier).</summary>
        public int Rarity;

        /// <summary>
        /// Where it comes from: <c>default</c>, <c>starter</c> (free from the start), <c>shop</c> (the
        /// Trader sells it), <c>boss</c> (a lair's first clear; never sold), <c>milestone</c>
        /// (<see cref="UnlockId"/> names the milestone), <c>drop</c> (a low chance per battle), or
        /// <c>premium</c> (reserved for future purchases: never obtainable with gold or play in v1).
        /// </summary>
        public string Source;

        /// <summary>For <c>shop</c> and <c>drop</c>: the first region (1-based) it can appear in.</summary>
        public int MinRegion = 1;

        /// <summary>For <c>boss</c>: the region whose lair grants it; for <c>milestone</c>: the milestone id. Otherwise "".</summary>
        public string UnlockId = string.Empty;

        /// <summary>The art the look will use (a placeholder key until the art exists).</summary>
        public string ArtKey;
    }

    /// <summary>
    /// An achievement that unlocks every <c>milestone</c> look naming it: <c>BeastLevel</c> (a beast
    /// reaches <see cref="Threshold"/> — for a species' look, a beast of that species), <c>AvatarLevel</c>
    /// (the avatar does), or <c>BossesCleared</c> (that many region bosses beaten).
    /// </summary>
    [Serializable]
    public class MilestoneData
    {
        public string MilestoneId;

        public string DisplayName;

        /// <summary><c>BeastLevel</c>, <c>AvatarLevel</c> or <c>BossesCleared</c>.</summary>
        public string Kind;

        public int Threshold = 1;
    }
}
