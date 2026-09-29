using System;

namespace BeastCraft.Garden
{
    /// <summary>
    /// The Wildgarden's content (<c>content/data/Grove/garden-library.json</c>): the seeds, the
    /// curated cross-pollination matrix (deterministic, never rolled — see <see cref="GardenRules"/>),
    /// the varieties the matrix can yield (the herbarium) and the crafting recipes. Plain data with
    /// public fields and arrays (no dictionaries); validated by <see cref="GardenLibraryValidator"/>,
    /// looked up through <see cref="GardenLibrary"/>.
    /// <para>
    /// No combat power anywhere in this file: nothing here may be read by battle, stats or campaign
    /// difficulty. See <c>docs/design/grove.md</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public class GardenLibraryData
    {
        public const string ProjectRelativePath = "content/data/Grove/garden-library.json";

        public int SchemaVersion = 1;

        /// <summary>How many plots the Wildgarden has.</summary>
        public int PlotCount = 6;

        public SeedSpeciesData[] Seeds = new SeedSpeciesData[0];

        /// <summary>
        /// The curated hybrid matrix: planting <see cref="CrossPollinationData.ParentA"/> and
        /// <see cref="CrossPollinationData.ParentB"/> in two plots and harvesting both together
        /// (<see cref="GardenRules.HarvestPair"/>) always yields <see cref="CrossPollinationData.ResultVarietyId"/> —
        /// never rolled. A self-pair (<c>ParentA == ParentB</c>) is what a lone plot harvests
        /// (<see cref="GardenRules.Harvest"/>): every seed needs one.
        /// </summary>
        public CrossPollinationData[] CrossPollinations = new CrossPollinationData[0];

        /// <summary>Every variety the matrix can yield — the herbarium's entries.</summary>
        public VarietyData[] Varieties = new VarietyData[0];

        /// <summary>Deterministic crafting recipes: no rolls.</summary>
        public RecipeData[] Recipes = new RecipeData[0];
    }

    /// <summary>A seed species.</summary>
    [Serializable]
    public class SeedSpeciesData
    {
        public string SeedId;

        public string DisplayName;

        /// <summary>3 to 24: hours to grow, checked through <c>OfflineClock</c>.</summary>
        public int GrowthHours;

        /// <summary><c>"shrine"</c> (see <see cref="UnlockId"/>) or <c>"starter"</c> (unlocked from the first save).</summary>
        public string Source;

        /// <summary>For <c>"shrine"</c>: the <c>ShrineData.GroveUnlockId</c> that grants it. Otherwise "".</summary>
        public string UnlockId = string.Empty;

        public string ArtKey;
    }

    /// <summary>One matrix entry (see <see cref="GardenLibraryData.CrossPollinations"/>). Order-independent: (A, B) and (B, A) are the same entry.</summary>
    [Serializable]
    public class CrossPollinationData
    {
        public string ParentA;

        public string ParentB;

        public string ResultVarietyId;
    }

    /// <summary>A grown variety (the herbarium's entry). Harvesting deposits one as a Grove item (<see cref="Grove.GroveItemInventory"/>).</summary>
    [Serializable]
    public class VarietyData
    {
        public string VarietyId;

        public string DisplayName;

        public string ArtKey;

        /// <summary>The herbarium's flavour text for this variety (DRAFT, the content bible's tone).</summary>
        public string HerbariumEntry;
    }

    /// <summary>A deterministic crafting recipe: consumes Grove item varieties, produces one output.</summary>
    [Serializable]
    public class RecipeData
    {
        public string RecipeId;

        public RecipeInputData[] Inputs = new RecipeInputData[0];

        /// <summary>
        /// <c>"decor"</c> (<see cref="OutputId"/> is a <c>Grove.DecorData.DecorId</c> whose Source is
        /// <c>"garden_craft"</c> and whose UnlockId is this recipe), <c>"dye"</c> (a Grove item id, the
        /// seam a later PR spends for colour evolutions), or <c>"cosmetic"</c> (a <c>"grove"</c> look;
        /// not authored in v1).
        /// </summary>
        public string Output;

        public string OutputId;
    }

    /// <summary>One recipe input: <see cref="Count"/> of a grown variety, consumed from the Grove item inventory.</summary>
    [Serializable]
    public class RecipeInputData
    {
        public string VarietyId;

        public int Count;
    }
}
