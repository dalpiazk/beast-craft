using System;
using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Grove;

namespace BeastCraft.Garden
{
    /// <summary>
    /// Validates <c>garden-library.json</c> (<see cref="GardenLibraryData"/>): unique snake_case ids;
    /// every seed's shrine unlock a known <c>discovery.json</c> shrine, used by no other seed or
    /// Grove habitat/decor; growth 3-24 hours; every matrix pair's seeds known and its result a known
    /// variety, no pair listed twice (order-independent) and every seed has its own self-pair (what a
    /// lone plot harvests); every variety reachable by at least one pair (no orphan herbarium entry);
    /// every recipe's inputs known varieties with a positive count, and its output resolving — a
    /// <c>"decor"</c> output a Grove decor piece sourced <c>"garden_craft"</c> naming this recipe,
    /// naming no other recipe; a <c>"cosmetic"</c> output a <c>"grove"</c> look when a cosmetic library
    /// is given. Every Grove <c>"garden_craft"</c> decor piece is some recipe's output. Display names
    /// and herbarium text follow the content bible.
    /// </summary>
    public static class GardenLibraryValidator
    {
        public const int MaxNameLength = 24;

        public const int MaxTextLength = 280;

        public const int MinGrowthHours = 3;

        public const int MaxGrowthHours = 24;

        public static List<string> Validate(GardenLibraryData data, DiscoveryLibraryData discovery, GroveLibraryData grove, CosmeticLibraryData cosmetics)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("garden-library.json is missing.");
                return errors;
            }

            if (data.SchemaVersion != 1)
            {
                errors.Add("SchemaVersion must be 1.");
            }

            if (data.PlotCount < 1)
            {
                errors.Add("PlotCount must be at least 1.");
            }

            HashSet<string> shrineIds = ShrineIds(discovery);
            HashSet<string> usedShrineIds = UsedGroveShrineIds(grove);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            Dictionary<string, SeedSpeciesData> seeds = new Dictionary<string, SeedSpeciesData>(StringComparer.Ordinal);
            foreach (SeedSpeciesData seed in data.Seeds ?? new SeedSpeciesData[0])
            {
                string at = "Seeds[" + (seed == null ? "?" : seed.SeedId) + "]";
                if (seed == null || !CheckId(errors, "Seeds", seed.SeedId, ids))
                {
                    continue;
                }

                seeds[seed.SeedId] = seed;
                CheckName(errors, at + ": DisplayName", seed.DisplayName);
                if (seed.GrowthHours < MinGrowthHours || seed.GrowthHours > MaxGrowthHours)
                {
                    errors.Add(at + ": GrowthHours must be " + MinGrowthHours + "-" + MaxGrowthHours + ".");
                }

                if (seed.Source == "shrine")
                {
                    if (string.IsNullOrEmpty(seed.UnlockId) || (shrineIds != null && !shrineIds.Contains(seed.UnlockId)))
                    {
                        errors.Add(at + ": UnlockId '" + seed.UnlockId + "' is not a shrine's GroveUnlockId (discovery.json).");
                    }
                    else if (!usedShrineIds.Add(seed.UnlockId))
                    {
                        errors.Add(at + ": UnlockId '" + seed.UnlockId + "' is claimed by more than one shrine-sourced seed, habitat or decor piece.");
                    }
                }
                else if (seed.Source != "starter")
                {
                    errors.Add(at + ": Source must be shrine or starter.");
                }
            }

            Dictionary<string, VarietyData> varieties = new Dictionary<string, VarietyData>(StringComparer.Ordinal);
            foreach (VarietyData variety in data.Varieties ?? new VarietyData[0])
            {
                string at = "Varieties[" + (variety == null ? "?" : variety.VarietyId) + "]";
                if (variety == null || !CheckId(errors, "Varieties", variety.VarietyId, ids))
                {
                    continue;
                }

                varieties[variety.VarietyId] = variety;
                CheckName(errors, at + ": DisplayName", variety.DisplayName);
                CheckText(errors, at + ": HerbariumEntry", variety.HerbariumEntry);
            }

            HashSet<string> pairs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> selfPaired = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> reachedVarieties = new HashSet<string>(StringComparer.Ordinal);
            foreach (CrossPollinationData cross in data.CrossPollinations ?? new CrossPollinationData[0])
            {
                if (cross == null)
                {
                    errors.Add("CrossPollinations: an entry is null.");
                    continue;
                }

                string at = "CrossPollinations[" + cross.ParentA + "+" + cross.ParentB + "]";
                bool knownA = !string.IsNullOrEmpty(cross.ParentA) && seeds.ContainsKey(cross.ParentA);
                bool knownB = !string.IsNullOrEmpty(cross.ParentB) && seeds.ContainsKey(cross.ParentB);
                if (!knownA || !knownB)
                {
                    errors.Add(at + ": ParentA and ParentB must both be known seeds.");
                    continue;
                }

                string key = GardenLibrary.PairKey(cross.ParentA, cross.ParentB);
                if (!pairs.Add(key))
                {
                    errors.Add(at + ": this pair is listed twice.");
                }

                if (cross.ParentA == cross.ParentB)
                {
                    selfPaired.Add(cross.ParentA);
                }

                if (string.IsNullOrEmpty(cross.ResultVarietyId) || !varieties.ContainsKey(cross.ResultVarietyId))
                {
                    errors.Add(at + ": ResultVarietyId must be a known variety.");
                }
                else
                {
                    reachedVarieties.Add(cross.ResultVarietyId);
                }
            }

            foreach (string seedId in seeds.Keys)
            {
                if (!selfPaired.Contains(seedId))
                {
                    errors.Add("CrossPollinations: seed '" + seedId + "' has no self-pair (what a lone plot of it harvests).");
                }
            }

            foreach (string varietyId in varieties.Keys)
            {
                if (!reachedVarieties.Contains(varietyId))
                {
                    errors.Add("Varieties[" + varietyId + "]: no CrossPollinations entry yields it.");
                }
            }

            HashSet<string> decorRecipeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecipeData recipe in data.Recipes ?? new RecipeData[0])
            {
                string at = "Recipes[" + (recipe == null ? "?" : recipe.RecipeId) + "]";
                if (recipe == null || !CheckId(errors, "Recipes", recipe.RecipeId, ids))
                {
                    continue;
                }

                if (recipe.Inputs == null || recipe.Inputs.Length == 0)
                {
                    errors.Add(at + ": needs at least one input.");
                }

                foreach (RecipeInputData input in recipe.Inputs ?? new RecipeInputData[0])
                {
                    if (input == null || input.Count <= 0 || string.IsNullOrEmpty(input.VarietyId) || !varieties.ContainsKey(input.VarietyId))
                    {
                        errors.Add(at + ": every input needs a known VarietyId and a positive Count.");
                    }
                }

                switch (recipe.Output)
                {
                    case "decor":
                        DecorData decor = FindGroveDecor(grove, recipe.OutputId);
                        if (decor == null || decor.Source != "garden_craft" || decor.UnlockId != recipe.RecipeId)
                        {
                            errors.Add(at + ": OutputId must be a Grove decor piece sourced garden_craft whose UnlockId is this recipe.");
                        }
                        else
                        {
                            decorRecipeIds.Add(decor.DecorId);
                        }

                        break;
                    case "dye":
                        if (!IsSnakeCase(recipe.OutputId))
                        {
                            errors.Add(at + ": a dye OutputId must be snake_case.");
                        }

                        break;
                    case "cosmetic":
                        CheckLook(errors, at + ": OutputId", recipe.OutputId, "recipe_" + recipe.RecipeId, cosmetics);
                        break;
                    default:
                        errors.Add(at + ": Output must be decor, dye or cosmetic.");
                        break;
                }
            }

            foreach (DecorData decor in grove == null ? new DecorData[0] : grove.Decor ?? new DecorData[0])
            {
                if (decor != null && decor.Source == "garden_craft" && !decorRecipeIds.Contains(decor.DecorId))
                {
                    errors.Add("grove-library.json Decor[" + decor.DecorId + "]: sourced garden_craft, but no garden-library.json recipe outputs it.");
                }
            }

            return errors;
        }

        private static DecorData FindGroveDecor(GroveLibraryData grove, string decorId)
        {
            if (grove == null || string.IsNullOrEmpty(decorId))
            {
                return null;
            }

            return Array.Find(grove.Decor ?? new DecorData[0], d => d != null && d.DecorId == decorId);
        }

        private static void CheckLook(List<string> errors, string at, string key, string unlockId, CosmeticLibraryData cosmetics)
        {
            if (string.IsNullOrEmpty(key))
            {
                errors.Add(at + " is missing.");
                return;
            }

            if (cosmetics == null)
            {
                return;
            }

            string[] parts = key.Split('/');
            CosmeticCategoryData category = parts.Length != 2 ? null : Array.Find(cosmetics.Categories ?? new CosmeticCategoryData[0], c => c != null && c.CategoryId == parts[0]);
            CosmeticOptionData option = category == null ? null : Array.Find(category.Options ?? new CosmeticOptionData[0], o => o != null && o.OptionId == parts[1]);
            if (option == null || option.Source != CosmeticLibrary.SourceGrove || option.UnlockId != unlockId)
            {
                errors.Add(at + " '" + key + "' must be a grove look whose UnlockId is '" + unlockId + "'.");
            }
        }

        private static HashSet<string> ShrineIds(DiscoveryLibraryData discovery)
        {
            if (discovery == null)
            {
                return null;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShrineData shrine in discovery.Shrines ?? new ShrineData[0])
            {
                if (shrine != null && !string.IsNullOrEmpty(shrine.GroveUnlockId))
                {
                    ids.Add(shrine.GroveUnlockId);
                }
            }

            return ids;
        }

        /// <summary>The shrine unlock ids already claimed by grove-library.json's habitats and decor (so a seed cannot also claim one).</summary>
        private static HashSet<string> UsedGroveShrineIds(GroveLibraryData grove)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            if (grove == null)
            {
                return used;
            }

            foreach (HabitatData habitat in grove.Habitats ?? new HabitatData[0])
            {
                if (habitat != null && habitat.UnlockSource == "shrine" && !string.IsNullOrEmpty(habitat.UnlockId))
                {
                    used.Add(habitat.UnlockId);
                }
            }

            foreach (DecorData decor in grove.Decor ?? new DecorData[0])
            {
                if (decor != null && decor.Source == "shrine" && !string.IsNullOrEmpty(decor.UnlockId))
                {
                    used.Add(decor.UnlockId);
                }
            }

            return used;
        }

        private static bool CheckId(List<string> errors, string list, string id, HashSet<string> ids)
        {
            if (!IsSnakeCase(id))
            {
                errors.Add(list + ": id '" + id + "' must be snake_case.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add(list + ": id '" + id + "' is used twice in garden-library.json.");
                return false;
            }

            return true;
        }

        private static void CheckName(List<string> errors, string at, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name.IndexOf('\'') >= 0)
            {
                errors.Add(at + " must be 1-" + MaxNameLength + " characters with no apostrophe.");
            }
        }

        private static void CheckText(List<string> errors, string at, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            {
                errors.Add(at + " must be 1-" + MaxTextLength + " characters.");
            }
        }

        private static bool IsSnakeCase(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
