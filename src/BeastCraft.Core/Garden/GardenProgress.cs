using System;
using System.Collections.Generic;

namespace BeastCraft.Garden
{
    /// <summary>
    /// The Wildgarden's account-wide state of one save (<c>PlayerSave.Garden</c>, schema 10): every
    /// planted plot (<see cref="Plots"/> — no entry for a plot id means it is empty and plantable) and
    /// the herbarium (<see cref="VarietiesDiscovered"/>, each variety recorded the first time it is
    /// harvested, whether alone or paired). Plain serializable data with public fields; the rules are
    /// <see cref="GardenRules"/>'.
    /// </summary>
    [Serializable]
    public class GardenProgress
    {
        public List<PlotState> Plots = new List<PlotState>();

        public List<string> VarietiesDiscovered = new List<string>();

        /// <summary>Plot <paramref name="plotId"/>'s state, or null (empty).</summary>
        public PlotState FindPlot(int plotId)
        {
            foreach (PlotState plot in Plots)
            {
                if (plot != null && plot.PlotId == plotId)
                {
                    return plot;
                }
            }

            return null;
        }

        /// <summary>Replaces a null list with an empty one and drops invalid or duplicate-id entries. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (Plots == null)
            {
                Plots = new List<PlotState>();
                repaired++;
            }

            repaired += Plots.RemoveAll(plot => plot == null || string.IsNullOrEmpty(plot.SeedId));
            HashSet<int> plotIds = new HashSet<int>();
            repaired += Plots.RemoveAll(plot => !plotIds.Add(plot.PlotId));

            if (VarietiesDiscovered == null)
            {
                VarietiesDiscovered = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += VarietiesDiscovered.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }

    /// <summary>One planted plot. No entry for a plot id means it is empty.</summary>
    [Serializable]
    public class PlotState
    {
        public int PlotId;

        public string SeedId;

        public long StartUtcTicks;

        public long StartMonotonicMs;
    }
}
