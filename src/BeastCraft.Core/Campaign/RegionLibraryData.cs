using System;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The plain-data shape of <c>data/Campaign/regions.json</c>: the region campaign. Ten regions
    /// cover levels 1-100 in turn; each is played as <see cref="RegionData.Stages"/> expeditions,
    /// each a seeded node map (<see cref="NodeMapGenerator"/>) that ends at a Gate (every stage but
    /// the last) or at the region's Boss, whose clear grants a seal (<see cref="SealData"/>) that
    /// raises the beast level cap. Post-game regions (<see cref="RegionData.IsPostGame"/>) follow
    /// the ten, outside that band: flat level 100, no seal, playable on <see cref="RunDifficulty.Hard"/>.
    /// <see cref="RegionLibraryValidator"/> holds the rules;
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
        /// <summary>Path of the file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Campaign/regions.json";

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

        /// <summary>
        /// The regions, in campaign order: the mainline ones, levels contiguous from 1 to 100, and
        /// after them any post-game ones (<see cref="RegionData.IsPostGame"/>, flat level 100).
        /// </summary>
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

        /// <summary>A copy of these rules (<see cref="NodeWeights"/> shared, not cloned).</summary>
        public MapRulesData Copy()
        {
            return (MapRulesData)MemberwiseClone();
        }
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

        /// <summary>
        /// A post-game region: outside the contiguous 1-100 campaign band, fought at a flat
        /// <see cref="RegionLibraryValidator.MaxLevel"/> (<see cref="MinLevel"/> ==
        /// <see cref="MaxLevel"/> == 100), with no seal (the cap is already at its peak). Validated in
        /// its own pass (<see cref="RegionLibraryValidator"/>), after the mainline band, which never
        /// sees it; draws only post-game shapes (<c>EncounterShapeData.PostGame</c>); the only kind of
        /// region an expedition may start on <see cref="RunDifficulty.Hard"/> (<see cref="HardMode"/>).
        /// False (the default) for the ten mainline regions.
        /// </summary>
        public bool IsPostGame;

        /// <summary>
        /// A post-game region's <see cref="RunDifficulty.Hard"/> encounters (<see cref="RegionLibrary.RegionFor"/>):
        /// the shapes Battle nodes draw, the shape Elites and generated Gates use, and the boss
        /// template. Empty (every field unset) on a mainline region.
        /// </summary>
        public RegionHardModeData HardMode = new RegionHardModeData();

        /// <summary>A copy of this region (arrays and <see cref="MapRules"/> shared, not cloned).</summary>
        public RegionData Copy()
        {
            return (RegionData)MemberwiseClone();
        }
    }

    /// <summary>
    /// What a post-game region fields on <see cref="RunDifficulty.Hard"/>, in place of its Normal
    /// <see cref="RegionData.ShapeWeights"/>, its map rules' <see cref="MapRulesData.EliteShapeId"/>
    /// and its <see cref="RegionData.BossTemplateId"/>. Everything else (map rules, levels, stages,
    /// rewards) is the region's own. With the same weights, in the same order, as the Normal
    /// <see cref="RegionData.ShapeWeights"/>, a seed generates the same map on either difficulty,
    /// only with the harder shape and boss ids.
    /// </summary>
    [Serializable]
    public class RegionHardModeData
    {
        /// <summary>The Hard shapes Battle nodes draw, weighted (post-game shapes).</summary>
        public ShapeWeightData[] ShapeWeights = new ShapeWeightData[0];

        /// <summary>The Hard shape Elite nodes and generated Gates draw (a post-game shape).</summary>
        public string EliteShapeId = string.Empty;

        /// <summary>The Hard boss's <c>encounter-library.json</c> template (with its own <c>DifficultyOverride</c>).</summary>
        public string BossTemplateId = string.Empty;

        /// <summary>Whether anything is authored (a mainline region must leave it all unset).</summary>
        public bool IsSet
        {
            get { return (ShapeWeights != null && ShapeWeights.Length > 0) || !string.IsNullOrEmpty(EliteShapeId) || !string.IsNullOrEmpty(BossTemplateId); }
        }
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
