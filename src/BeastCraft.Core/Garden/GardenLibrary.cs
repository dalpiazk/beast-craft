using System;
using System.Collections.Generic;

namespace BeastCraft.Garden
{
    /// <summary>Lookups over a validated <see cref="GardenLibraryData"/> (<c>garden-library.json</c>).</summary>
    public sealed class GardenLibrary
    {
        private readonly Dictionary<string, SeedSpeciesData> _seeds = new Dictionary<string, SeedSpeciesData>(StringComparer.Ordinal);
        private readonly Dictionary<string, VarietyData> _varieties = new Dictionary<string, VarietyData>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecipeData> _recipes = new Dictionary<string, RecipeData>(StringComparer.Ordinal);
        private readonly Dictionary<string, CrossPollinationData> _matrix = new Dictionary<string, CrossPollinationData>(StringComparer.Ordinal);

        private GardenLibrary(GardenLibraryData data)
        {
            Data = data;
        }

        public GardenLibraryData Data { get; }

        public static GardenLibrary Build(GardenLibraryData data)
        {
            GardenLibrary library = new GardenLibrary(data ?? new GardenLibraryData());

            foreach (SeedSpeciesData seed in library.Data.Seeds ?? new SeedSpeciesData[0])
            {
                if (seed != null && !string.IsNullOrEmpty(seed.SeedId))
                {
                    library._seeds[seed.SeedId] = seed;
                }
            }

            foreach (VarietyData variety in library.Data.Varieties ?? new VarietyData[0])
            {
                if (variety != null && !string.IsNullOrEmpty(variety.VarietyId))
                {
                    library._varieties[variety.VarietyId] = variety;
                }
            }

            foreach (RecipeData recipe in library.Data.Recipes ?? new RecipeData[0])
            {
                if (recipe != null && !string.IsNullOrEmpty(recipe.RecipeId))
                {
                    library._recipes[recipe.RecipeId] = recipe;
                }
            }

            foreach (CrossPollinationData cross in library.Data.CrossPollinations ?? new CrossPollinationData[0])
            {
                if (cross != null && !string.IsNullOrEmpty(cross.ParentA) && !string.IsNullOrEmpty(cross.ParentB))
                {
                    library._matrix[PairKey(cross.ParentA, cross.ParentB)] = cross;
                }
            }

            return library;
        }

        public SeedSpeciesData Seed(string seedId)
        {
            return seedId != null && _seeds.TryGetValue(seedId, out SeedSpeciesData seed) ? seed : null;
        }

        public VarietyData Variety(string varietyId)
        {
            return varietyId != null && _varieties.TryGetValue(varietyId, out VarietyData variety) ? variety : null;
        }

        public RecipeData Recipe(string recipeId)
        {
            return recipeId != null && _recipes.TryGetValue(recipeId, out RecipeData recipe) ? recipe : null;
        }

        /// <summary>The matrix entry for (<paramref name="seedA"/>, <paramref name="seedB"/>), order-independent, or null.</summary>
        public CrossPollinationData Cross(string seedA, string seedB)
        {
            if (string.IsNullOrEmpty(seedA) || string.IsNullOrEmpty(seedB))
            {
                return null;
            }

            return _matrix.TryGetValue(PairKey(seedA, seedB), out CrossPollinationData cross) ? cross : null;
        }

        /// <summary>The unordered pair key two seed ids share, regardless of order.</summary>
        public static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }
    }
}
