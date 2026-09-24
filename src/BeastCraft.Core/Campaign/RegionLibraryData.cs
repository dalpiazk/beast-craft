using System;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The plain-data shape of <c>Data/Campaign/regions.json</c>: the region campaign. Ten regions
    /// cover levels 1-100 in turn; each is played as <see cref="RegionData.Stages"/> expeditions,
    /// each a seeded node map (<see cref="NodeMapGenerator"/>) that ends at a Gate (every stage but
    /// the last) or at the region's Boss, whose clear grants a seal (<see cref="SealData"/>) that
    /// raises the beast level cap. <see cref="RegionLibraryValidator"/> holds the rules;
    /// <see cref="RegionLibrary"/> is the built, indexed form.
    /// <para>
    /// JsonUtility-compatible: arrays, no nullable fields; node types are member names checked by the
    /// validator. A region's <see cref="RegionData.MapRules"/> overrides the library's when its
    /// <see cref="MapRulesData.Layers"/> is above 0 (JsonUtility always fills a class field, so
    /// "absent" is all zeros).
    /// </para>
    /// </summary>
    [Serializable]
    public class RegionLibraryData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Campaign/regions.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>The beast level cap before any seal: the first region's max level plus <see cref="LevelCapMargin"/>.</summary>
        public int StartingLevelCap;

        /// <summary>How far above a region's max level its cap sits (documentation for the authored caps; the validator checks caps against it).</summary>
        public int LevelCapMargin;

        /// <summary>The node-map rules every region uses unless it overrides them.</summary>
        public MapRulesData MapRules = new MapRulesData();

        /// <summary>The seals: key items, each raising the beast level cap.</summary>
        public SealData[] Seals = new SealData[0];

        /// <summary>The regions, in campaign order, levels contiguous from 1 to 100.</summary>
        public RegionData[] Regions = new RegionData[0];
    }

    /// <summary>
    /// How a stage's node map is generated (Slay-the-Spire style; see <see cref="NodeMapGenerator"/>)
    /// and how its node levels are offset.
    /// </summary>
    [Serializable]
    public class MapRulesData
    {
        /// <summary>Rows, including the start row (0) and the single Gate/Boss row on top. At least 3.</summary>
        public int Layers;

        /// <summary>Columns a path may walk, at least 1.</summary>
        public int Lanes;

        /// <summary>How many paths are walked from the bottom row to the row under the top; their union is the map.</summary>
        public int Paths;

        /// <summary>The lowest row an Elite node may appear on.</summary>
        public int EliteMinLayer;

        /// <summary>The row every node of is a Rest (Camp) node; −1 for none. Must be between the start row and the top.</summary>
        public int RestLayer;

        /// <summary>The weighted draw for every other node (types Battle, Elite, Shop, Rest).</summary>
        public NodeWeightData[] NodeWeights = new NodeWeightData[0];

        /// <summary>Fewest Elite nodes on a map (a best effort when the map has too few eligible nodes).</summary>
        public int MinElites;

        /// <summary>Most Elite nodes on a map.</summary>
        public int MaxElites;

        /// <summary>Most Shop nodes on a map.</summary>
        public int MaxShops;

        /// <summary>An Elite node's level above its row's.</summary>
        public int EliteLevelOffset;

        /// <summary>A Gate's level above its row's.</summary>
        public int GateLevelOffset;

        /// <summary>The encounter shape Elite nodes and generated Gates draw.</summary>
        public string EliteShapeId;
    }

    /// <summary>One node type's draw weight.</summary>
    [Serializable]
    public class NodeWeightData
    {
        /// <summary>A <see cref="MapNodeType"/> name: Battle, Elite, Shop or Rest.</summary>
        public string Type;

        /// <summary>Relative weight, above 0.</summary>
        public int Weight;
    }

    /// <summary>A seal: a key item that raises the beast level cap to <see cref="LevelCap"/>.</summary>
    [Serializable]
    public class SealData
    {
        /// <summary>Stable lowercase snake_case id (saved in <see cref="CampaignProgress.Seals"/>).</summary>
        public string SealId;

        public string DisplayName;

        /// <summary>Flavour text for the key item (presentation only; "" when not authored).</summary>
        public string Description;

        /// <summary>The beast level cap while this is the highest seal owned, 1-100.</summary>
        public int LevelCap;
    }

    /// <summary>One region of the campaign.</summary>
    [Serializable]
    public class RegionData
    {
        /// <summary>Stable lowercase snake_case id (saved in <see cref="RegionProgress.RegionId"/>).</summary>
        public string RegionId;

        public string DisplayName;

        public string Description;

        /// <summary>The first stage's bottom-row level.</summary>
        public int MinLevel;

        /// <summary>The last stage's top level: the boss fights at exactly this.</summary>
        public int MaxLevel;

        /// <summary>Expeditions to the boss, at least 1: stages before the last end at a Gate.</summary>
        public int Stages;

        /// <summary>The region whose boss unlocks this one; "" for the first.</summary>
        public string RequiresRegionId;

        /// <summary>The shapes Battle nodes draw, weighted.</summary>
        public ShapeWeightData[] ShapeWeights = new ShapeWeightData[0];

        /// <summary>
        /// Per stage (index = stage), an authored Gate template id; a missing or "" entry fields a
        /// generated <see cref="MapRulesData.EliteShapeId"/> encounter at the Gate's level instead.
        /// </summary>
        public string[] GateTemplateIds = new string[0];

        /// <summary>The encounter-library template the region's boss fields, at <see cref="MaxLevel"/>.</summary>
        public string BossTemplateId;

        /// <summary>The seal the boss grants; "" for none.</summary>
        public string BossRewardSealId;

        /// <summary>This region's own map rules when their <see cref="MapRulesData.Layers"/> is above 0; otherwise the library's.</summary>
        public MapRulesData MapRules = new MapRulesData();
    }

    /// <summary>One encounter shape's draw weight on Battle nodes.</summary>
    [Serializable]
    public class ShapeWeightData
    {
        /// <summary>An <c>encounter-library.json</c> shape id.</summary>
        public string ShapeId;

        /// <summary>Relative weight, above 0.</summary>
        public int Weight;
    }
}
