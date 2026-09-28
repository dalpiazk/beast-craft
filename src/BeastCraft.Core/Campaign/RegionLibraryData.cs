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
        /// Early-region easing (producer decision), per kind of fight: the FULL discount, a scale on
        /// the stat multiplier, of each shape (an <c>encounter-library.json</c> shape id) and of the
        /// authored templates (the bosses, id <see cref="EasingBossId"/>); a kind not listed is
        /// never eased. How much of it applies is each region's <see cref="RegionData.StageEasing"/>.
        /// The calibrated table assumes typical gear and a scouted pick from the whole roster; a new
        /// player has neither, and each kind of fight needs its own discount (measured by the balance
        /// simulator's <c>--mode newplayer</c>).
        /// </summary>
        public ShapeScaleData[] EasingShapeScales = new ShapeScaleData[0];

        /// <summary>The <see cref="EasingShapeScales"/> id of an authored template (a region boss or gate).</summary>
        public const string EasingBossId = "boss";

        /// <summary>
        /// The regions, in campaign order: the mainline ones, levels contiguous from 1 to 100, and
        /// after them any post-game ones (<see cref="RegionData.IsPostGame"/>, flat level 100).
        /// </summary>
        public RegionData[] Regions = new RegionData[0];

        /// <summary>
        /// The onboarding (tutorial) regions, kept apart from <see cref="Regions"/> so the mainline
        /// band, its calibration and every tool that walks the campaign never see them: today one,
        /// Hearthglen (<c>r00</c>, <see cref="CampaignProgress.TutorialRegionId"/>). Each is
        /// <see cref="RegionData.IsTutorial"/>, played once as one expedition over its authored
        /// <see cref="RegionData.FixedNodes"/> (no <see cref="NodeMapGenerator"/>, no seed), every
        /// fight a fixed template at its own <c>DifficultyOverride</c> (never the calibrated table, never
        /// the early-region easing); clearing its last location unlocks the first mainline region.
        /// Validated in their own pass (<see cref="RegionLibraryValidator"/>).
        /// </summary>
        public RegionData[] TutorialRegions = new RegionData[0];
    }

    /// <summary>
    /// One authored location of a tutorial region's fixed map (<see cref="RegionData.FixedNodes"/>),
    /// in path order: the node id is its index, each leads to the next, the last ends the region.
    /// </summary>
    [Serializable]
    public class FixedNodeData
    {
        /// <summary>A <see cref="MapNodeType"/> name: Story, Battle, Trial or Rest.</summary>
        public string Type;

        /// <summary>The location's display name (DRAFT text; tutorial locations are named here, not in <c>location-names.json</c>).</summary>
        public string Name;

        /// <summary>The encounter level (a Rest: the level Camp training pays at), within the region's band.</summary>
        public int Level = 1;

        /// <summary>A Battle's or Trial's <c>encounter-library.json</c> template (with its own <c>DifficultyOverride</c>); "" otherwise.</summary>
        public string TemplateId = string.Empty;

        /// <summary>A Trial's beast pick (<c>StarterPicks</c>): 2 or 3, each exactly once, in order; 0 elsewhere.</summary>
        public int PickStep;

        /// <summary>The dialogue scene a Story location plays (<c>dialogue.json</c>); "" for none.</summary>
        public string SceneId = string.Empty;

        /// <summary>Which column of the map the location sits in (0 = left), for the spatial map's layout.</summary>
        public int Lane;

        /// <summary>
        /// A <see cref="LocationKind"/> key (<see cref="LocationKinds.Key"/>) the location is presented as;
        /// "" = its type's (<see cref="LocationKinds.For"/>).
        /// </summary>
        public string Kind = string.Empty;

        /// <summary>
        /// A fight on the open board (no obstacles), for the fights before the obstacle tutorial beat;
        /// false = the region's battlefields (<see cref="RegionData.BattlefieldRegionId"/>).
        /// </summary>
        public bool OpenBoard;

        /// <summary>
        /// The enemies' element adapts to the player's team (<c>ElementAdaptation</c>): one element
        /// neutral (1x both ways) against every owned beast's elements, chosen deterministically, in
        /// place of the template's authored elements. For the finale, so the element chart neither
        /// punishes nor favours any pick.
        /// </summary>
        public bool AdaptiveElements;

        /// <summary>Consumables a Story location hands over when visited (the first visit only: the region is played once).</summary>
        public ItemGrantData[] Grants = new ItemGrantData[0];
    }

    /// <summary>A quantity of one consumable (<see cref="FixedNodeData.Grants"/>).</summary>
    [Serializable]
    public class ItemGrantData
    {
        /// <summary>A <c>consumable-library.json</c> ConsumableId.</summary>
        public string ConsumableId;

        /// <summary>How many, at least 1.</summary>
        public int Quantity = 1;
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

        /// <summary>
        /// Early-region easing (producer decision): per stage (index = stage), how much of the full
        /// per-shape discount (<see cref="RegionLibraryData.EasingShapeScales"/>) a campaign battle in
        /// that stage's expedition gets: 1 = all of it, 0 = none; the scale is
        /// <c>1 - weight x (1 - full scale)</c>. Empty (the default) = 0 everywhere; otherwise one
        /// entry per stage in [0, 1], never rising from stage to stage or region to region, and
        /// reaching 0 before the easing stops. Mainline regions only. Read by
        /// <see cref="RegionLibrary.DifficultyScaleFor"/> and applied by
        /// <see cref="CampaignRules.PlanFor(MapRun, MapNode, Encounters.EncounterLibrary, Encounters.EnemyCatalog, RegionLibrary)"/>;
        /// the balance simulator's calibration never sees it.
        /// </summary>
        public double[] StageEasing = new double[0];

        /// <summary>
        /// A per-region override of the early-region easing for the region's boss (its lair, the last
        /// stage's authored template): the scale the boss is fielded at, in (0, 1], instead of the
        /// stage's weight of the library's <c>boss</c> discount. 0 (the default) = no override. For a
        /// boss whose own need differs from the shared discount (r02's Ember Twins: x0.92, measured by
        /// <c>--mode newplayer</c>). Mainline regions only; only where <see cref="StageEasing"/> eases the last stage.
        /// </summary>
        public double BossScale;

        /// <summary>
        /// An onboarding region (only in <see cref="RegionLibraryData.TutorialRegions"/>): one
        /// expedition over its <see cref="FixedNodes"/>, played once. False for every campaign region.
        /// </summary>
        public bool IsTutorial;

        /// <summary>A tutorial region's authored map, in path order (<see cref="FixedNodeData"/>). Empty elsewhere.</summary>
        public FixedNodeData[] FixedNodes = new FixedNodeData[0];

        /// <summary>
        /// A tutorial region fights on another region's battlefields (its painted backdrops and
        /// obstacle layouts; <c>battle-layouts.json</c>): that mainline region's id. "" = its own
        /// (<see cref="RegionLibrary.BattlefieldRegionOf"/>).
        /// </summary>
        public string BattlefieldRegionId = string.Empty;

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

    /// <summary>One kind of fight's full early-region discount (<see cref="RegionLibraryData.EasingShapeScales"/>).</summary>
    [Serializable]
    public class ShapeScaleData
    {
        /// <summary>An <c>encounter-library.json</c> shape id, or <see cref="RegionLibraryData.EasingBossId"/> for the authored templates.</summary>
        public string ShapeId;

        /// <summary>The scale on the stat multiplier at the full discount, above 0 and at most 1.</summary>
        public double Scale;
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
