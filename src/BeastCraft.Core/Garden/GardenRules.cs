using System;
using System.Collections.Generic;
using BeastCraft.Common;
using BeastCraft.Economy;
using BeastCraft.Save;

namespace BeastCraft.Garden
{
    /// <summary>
    /// The Wildgarden's rules (plant, grow, harvest, craft): deterministic, non-throwing,
    /// refuse-and-no-op on bad input. No combat power anywhere: nothing here touches stats, damage or
    /// campaign difficulty.
    /// <para>
    /// Growth is checked through <see cref="OfflineClock"/>, the same anti-tamper reconciliation idle
    /// rewards and the Grove's gifts use. Cross-pollination is a curated, deterministic matrix, never
    /// rolled: harvesting a lone plot (<see cref="Harvest"/>) always yields its seed's self-pair
    /// variety; harvesting two ready plots together (<see cref="HarvestPair"/>) always yields the
    /// matrix's fixed hybrid for that pair of seeds (or refuses when the content defines none — not
    /// every pair of seeds is a curated hybrid). Every harvest deposits one of the resulting variety
    /// into <see cref="Grove.GroveItemInventory"/> (<c>PlayerSave.Grove.Items</c>) and records the
    /// herbarium find; crafting (<see cref="Craft"/>) spends from the same pool.
    /// </para>
    /// </summary>
    public static class GardenRules
    {
        private const double MsPerHour = 3600000.0;

        /// <summary>Plants <paramref name="seedId"/> in plot <paramref name="plotId"/>: refuses when the plot is out of range, occupied, or the seed is not unlocked.</summary>
        public static GardenActionResult Plant(PlayerSave save, GardenLibrary library, int plotId, string seedId, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            if (save == null || library == null)
            {
                return GardenActionResult.Refused("No save or Garden content.");
            }

            save.EnsureInitialized();
            if (plotId < 0 || plotId >= library.Data.PlotCount)
            {
                return GardenActionResult.Refused("That is not one of the Wildgarden's plots.");
            }

            if (save.Garden.FindPlot(plotId) != null)
            {
                return GardenActionResult.Refused("That plot is already planted.");
            }

            SeedSpeciesData seed = library.Seed(seedId);
            if (seed == null || !IsSeedUnlocked(save, seed))
            {
                return GardenActionResult.Refused("That seed is not unlocked.");
            }

            save.Garden.Plots.Add(new PlotState
            {
                PlotId = plotId,
                SeedId = seedId,
                StartUtcTicks = OfflineClock.UtcTicks(nowUtc),
                StartMonotonicMs = OfflineClock.MonotonicMs(nowMonotonic)
            });
            return GardenActionResult.Succeeded();
        }

        /// <summary>Whether <paramref name="seed"/> is unlocked: a starter seed, or a shrine one whose unlock has been granted.</summary>
        public static bool IsSeedUnlocked(PlayerSave save, SeedSpeciesData seed)
        {
            if (save == null || seed == null)
            {
                return false;
            }

            if (seed.Source == "starter")
            {
                return true;
            }

            return seed.Source == "shrine" && !string.IsNullOrEmpty(seed.UnlockId) && save.Discovery?.GroveUnlockIds != null &&
                   save.Discovery.GroveUnlockIds.Contains(seed.UnlockId);
        }

        /// <summary>Plot <paramref name="plot"/>'s growth, 0 to 1 (1 = ready). 0 for an unknown seed.</summary>
        public static double GrowthProgress(GardenLibrary library, PlotState plot, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            SeedSpeciesData seed = plot == null || library == null ? null : library.Seed(plot.SeedId);
            if (seed == null)
            {
                return 0.0;
            }

            long elapsed = OfflineClock.ElapsedMs(plot.StartUtcTicks, plot.StartMonotonicMs, OfflineClock.UtcTicks(nowUtc), OfflineClock.MonotonicMs(nowMonotonic), out bool _);
            double neededMs = Math.Max(1, seed.GrowthHours) * MsPerHour;
            return Math.Min(1.0, elapsed / neededMs);
        }

        /// <summary>Whether plot <paramref name="plot"/> has finished growing.</summary>
        public static bool IsReady(GardenLibrary library, PlotState plot, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            return plot != null && GrowthProgress(library, plot, nowUtc, nowMonotonic) >= 1.0;
        }

        /// <summary>Harvests a lone, ready plot: always its seed's self-pair variety (see the class remarks).</summary>
        public static GardenHarvestResult Harvest(PlayerSave save, GardenLibrary library, int plotId, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            if (save == null || library == null)
            {
                return GardenHarvestResult.Refused("No save or Garden content.");
            }

            PlotState plot = save.Garden.FindPlot(plotId);
            if (plot == null)
            {
                return GardenHarvestResult.Refused("That plot is empty.");
            }

            if (!IsReady(library, plot, nowUtc, nowMonotonic))
            {
                return GardenHarvestResult.Refused("That plot is not ready yet.");
            }

            CrossPollinationData self = library.Cross(plot.SeedId, plot.SeedId);
            if (self == null)
            {
                return GardenHarvestResult.Refused("That seed has no variety of its own (content error).");
            }

            save.Garden.Plots.Remove(plot);
            return Deposit(save, self.ResultVarietyId);
        }

        /// <summary>
        /// Harvests two different, ready plots together: the matrix's fixed hybrid for their two
        /// seeds, or refuses when the content defines no pair for them.
        /// </summary>
        public static GardenHarvestResult HarvestPair(PlayerSave save, GardenLibrary library, int plotIdA, int plotIdB, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            if (save == null || library == null)
            {
                return GardenHarvestResult.Refused("No save or Garden content.");
            }

            if (plotIdA == plotIdB)
            {
                return GardenHarvestResult.Refused("Cross-pollination needs two different plots.");
            }

            PlotState plotA = save.Garden.FindPlot(plotIdA);
            PlotState plotB = save.Garden.FindPlot(plotIdB);
            if (plotA == null || plotB == null)
            {
                return GardenHarvestResult.Refused("Both plots must be planted.");
            }

            if (!IsReady(library, plotA, nowUtc, nowMonotonic) || !IsReady(library, plotB, nowUtc, nowMonotonic))
            {
                return GardenHarvestResult.Refused("Both plots must be ready.");
            }

            CrossPollinationData cross = library.Cross(plotA.SeedId, plotB.SeedId);
            if (cross == null)
            {
                return GardenHarvestResult.Refused("These two do not cross-pollinate.");
            }

            save.Garden.Plots.Remove(plotA);
            save.Garden.Plots.Remove(plotB);
            return Deposit(save, cross.ResultVarietyId);
        }

        private static GardenHarvestResult Deposit(PlayerSave save, string varietyId)
        {
            save.Grove.EnsureInitialized();
            bool discovered = AddOnce(save.Garden.VarietiesDiscovered, varietyId);
            save.Grove.Items.Add(varietyId, 1);
            return GardenHarvestResult.Succeeded(varietyId, discovered);
        }

        /// <summary>
        /// Crafts <paramref name="recipeId"/>: refuses when it is unknown or the Grove item inventory
        /// does not hold every input's count; otherwise consumes them and applies the output (a decor
        /// unlock, a dye item added to the Grove inventory, or — once authored — a cosmetic look).
        /// </summary>
        public static GardenActionResult Craft(PlayerSave save, GardenLibrary library, string recipeId, CosmeticLibrary cosmetics = null)
        {
            if (save == null || library == null)
            {
                return GardenActionResult.Refused("No save or Garden content.");
            }

            RecipeData recipe = library.Recipe(recipeId);
            if (recipe == null)
            {
                return GardenActionResult.Refused("Unknown recipe.");
            }

            save.Grove.EnsureInitialized();

            // Sum the required count per distinct VarietyId first: a recipe naming the same variety
            // in two input rows must check (and later consume) their combined total, not each row
            // against the held count alone (which would under-count what the craft actually spends).
            Dictionary<string, int> required = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RecipeInputData input in recipe.Inputs ?? new RecipeInputData[0])
            {
                if (input == null || string.IsNullOrEmpty(input.VarietyId) || input.Count <= 0)
                {
                    return GardenActionResult.Refused("Not enough " + (input == null ? "?" : input.VarietyId) + ".");
                }

                required.TryGetValue(input.VarietyId, out int soFar);
                required[input.VarietyId] = soFar + input.Count;
            }

            foreach (KeyValuePair<string, int> entry in required)
            {
                if (save.Grove.Items.GetCount(entry.Key) < entry.Value)
                {
                    return GardenActionResult.Refused("Not enough " + entry.Key + ".");
                }
            }

            foreach (KeyValuePair<string, int> entry in required)
            {
                if (!save.Grove.Items.TryConsume(entry.Key, entry.Value))
                {
                    return GardenActionResult.Refused("Not enough " + entry.Key + ".");
                }
            }

            switch (recipe.Output)
            {
                case "decor":
                    AddOnce(save.Grove.UnlockedDecorIds, recipe.OutputId);
                    break;
                case "dye":
                    save.Grove.Items.Add(recipe.OutputId, 1);
                    break;
                case "cosmetic":
                    if (cosmetics != null)
                    {
                        CosmeticRules.UnlockOrRefund(save, cosmetics, recipe.OutputId, out int _);
                    }

                    break;
            }

            return GardenActionResult.Succeeded();
        }

        private static bool AddOnce(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }
    }

    /// <summary>What a non-harvest Garden action (<see cref="GardenRules.Plant"/>, <see cref="GardenRules.Craft"/>) did.</summary>
    public sealed class GardenActionResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        internal static GardenActionResult Succeeded()
        {
            return new GardenActionResult();
        }

        internal static GardenActionResult Refused(string error)
        {
            return new GardenActionResult { Success = false, Error = error };
        }
    }

    /// <summary>What a harvest (<see cref="GardenRules.Harvest"/>, <see cref="GardenRules.HarvestPair"/>) yielded.</summary>
    public sealed class GardenHarvestResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        public string VarietyId { get; private set; }

        /// <summary>Whether this was the first time <see cref="VarietyId"/> was ever harvested (a new herbarium entry).</summary>
        public bool Discovered { get; private set; }

        internal static GardenHarvestResult Succeeded(string varietyId, bool discovered)
        {
            return new GardenHarvestResult { VarietyId = varietyId, Discovered = discovered };
        }

        internal static GardenHarvestResult Refused(string error)
        {
            return new GardenHarvestResult { Success = false, Error = error };
        }
    }
}
