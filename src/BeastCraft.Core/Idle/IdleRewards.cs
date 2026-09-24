using System;
using System.Collections.Generic;

namespace BeastCraft.Idle
{
    /// <summary>
    /// The runtime idle reward rates, built from <see cref="IdleRewardsData"/> by
    /// <see cref="IdleRewardsBuilder"/>; immutable once built. See <see cref="IdleRewardCalculator"/>.
    /// </summary>
    public sealed class IdleRewards
    {
        private readonly List<IdleBand> _bands;

        internal IdleRewards(int capHours, string shape, double materialRollsPerHour, List<IdleBand> bands)
        {
            CapHours = capHours;
            Shape = shape ?? string.Empty;
            MaterialRollsPerHour = materialRollsPerHour;
            _bands = bands;
        }

        /// <summary>The most idle hours one claim pays (time beyond it is not banked).</summary>
        public int CapHours { get; }

        /// <summary>The drop-table shape of the idle material cell.</summary>
        public string Shape { get; }

        /// <summary>Material rolls per idle hour.</summary>
        public double MaterialRollsPerHour { get; }

        /// <summary>The bands, ascending.</summary>
        public IReadOnlyList<IdleBand> Bands
        {
            get { return _bands; }
        }

        /// <summary>
        /// The band holding <paramref name="progressLevel"/>, or null below 1 (nothing cleared yet: no
        /// idle rewards). A level above the last band reads as the last band.
        /// </summary>
        public IdleBand BandFor(int progressLevel)
        {
            if (progressLevel < 1 || _bands.Count == 0)
            {
                return null;
            }

            foreach (IdleBand band in _bands)
            {
                if (progressLevel <= band.MaxProgressLevel)
                {
                    return band;
                }
            }

            return _bands[_bands.Count - 1];
        }
    }

    /// <summary>One band of <see cref="IdleRewards"/> (see <see cref="IdleBandData"/>).</summary>
    public sealed class IdleBand
    {
        internal IdleBand(int minProgressLevel, int maxProgressLevel, int xpPerHour, int goldPerHour, double materialChanceMultiplier, int cosmeticChancePer10k)
        {
            MinProgressLevel = minProgressLevel;
            MaxProgressLevel = maxProgressLevel;
            XpPerHour = Math.Max(0, xpPerHour);
            GoldPerHour = Math.Max(0, goldPerHour);
            MaterialChanceMultiplier = Math.Max(0.0, Math.Min(1.0, materialChanceMultiplier));
            CosmeticChancePer10k = Math.Max(0, Math.Min(IdleRewardsData.MaxCosmeticChancePer10k, cosmeticChancePer10k));
        }

        public int MinProgressLevel { get; }

        public int MaxProgressLevel { get; }

        public int XpPerHour { get; }

        public int GoldPerHour { get; }

        public double MaterialChanceMultiplier { get; }

        public int CosmeticChancePer10k { get; }
    }

    /// <summary>Builds <see cref="IdleRewards"/> from the authored <see cref="IdleRewardsData"/>.</summary>
    public static class IdleRewardsBuilder
    {
        /// <summary>
        /// The runtime rates of <paramref name="data"/> (validate it first with
        /// <see cref="IdleRewardsValidator"/>; this only skips null bands and clamps values into
        /// range). Null for null data.
        /// </summary>
        public static IdleRewards Build(IdleRewardsData data)
        {
            if (data == null)
            {
                return null;
            }

            List<IdleBand> bands = new List<IdleBand>();
            foreach (IdleBandData band in data.Bands ?? new IdleBandData[0])
            {
                if (band != null)
                {
                    bands.Add(new IdleBand(band.MinProgressLevel, band.MaxProgressLevel, band.XpPerHour, band.GoldPerHour, band.MaterialChanceMultiplier, band.CosmeticChancePer10k));
                }
            }

            bands.Sort((a, b) => a.MinProgressLevel.CompareTo(b.MinProgressLevel));
            double rolls = float.IsNaN(data.MaterialRollsPerHour) || data.MaterialRollsPerHour < 0f ? 0.0 : data.MaterialRollsPerHour;
            return new IdleRewards(Math.Max(1, Math.Min(IdleRewardsData.MaxCapHours, data.CapHours)), data.Shape, rolls, bands);
        }
    }
}
