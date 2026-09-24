using System;
using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Idle
{
    /// <summary>
    /// Checks <c>idle-rewards.json</c> (<see cref="IdleRewardsData"/>) before it is imported or used:
    /// the schema version; a cap of 1-<see cref="IdleRewardsData.MaxCapHours"/> hours; a material
    /// shape, and a non-negative number of rolls per hour; bands ascending and contiguous over
    /// progress levels 1-100; no negative rate; XP and gold per hour never falling from one band to
    /// the next (the rates are monotonic step functions of the progress level); material chance
    /// multipliers in 0-1; look chances of at most 1% per claim. With the drop tables, the material
    /// shape must be one of theirs. Returns every problem found (empty = valid); never throws.
    /// </summary>
    public static class IdleRewardsValidator
    {
        /// <summary>Validates the file on its own.</summary>
        public static List<string> Validate(IdleRewardsData data)
        {
            return Validate(data, null);
        }

        /// <summary>Validates the file and cross-checks its material shape against <paramref name="dropTables"/> (skipped when null).</summary>
        public static List<string> Validate(IdleRewardsData data, DropTableData dropTables)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No idle reward data.");
                return errors;
            }

            if (data.SchemaVersion != IdleRewardsData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + IdleRewardsData.CurrentSchemaVersion + ".");
            }

            if (data.CapHours < 1 || data.CapHours > IdleRewardsData.MaxCapHours)
            {
                errors.Add("CapHours " + data.CapHours + " is outside 1-" + IdleRewardsData.MaxCapHours + ".");
            }

            if (string.IsNullOrEmpty(data.Shape))
            {
                errors.Add("Shape is empty (the drop-table shape the idle material rolls use).");
            }
            else if (dropTables != null && Array.IndexOf(dropTables.Shapes ?? new string[0], data.Shape) < 0)
            {
                errors.Add("Shape '" + data.Shape + "' is not a drop-table shape.");
            }

            if (float.IsNaN(data.MaterialRollsPerHour) || float.IsInfinity(data.MaterialRollsPerHour) || data.MaterialRollsPerHour < 0f)
            {
                errors.Add("MaterialRollsPerHour must be a number of at least 0.");
            }

            ValidateBands(data.Bands, errors);
            return errors;
        }

        private static void ValidateBands(IdleBandData[] bands, List<string> errors)
        {
            if (bands == null || bands.Length == 0)
            {
                errors.Add("Bands is empty; it must cover progress levels 1-" + BeastProgression.MaxLevel + ".");
                return;
            }

            int expectedMin = 1;
            IdleBandData previous = null;
            for (int i = 0; i < bands.Length; i++)
            {
                IdleBandData band = bands[i];
                string at = "Bands[" + i + "]";
                if (band == null)
                {
                    errors.Add(at + " is null.");
                    previous = null;
                    continue;
                }

                if (band.MinProgressLevel != expectedMin)
                {
                    errors.Add(at + " starts at " + band.MinProgressLevel + "; expected " + expectedMin + " (bands must be ascending and contiguous from 1).");
                }

                if (band.MaxProgressLevel < band.MinProgressLevel || band.MaxProgressLevel > BeastProgression.MaxLevel)
                {
                    errors.Add(at + " ends at " + band.MaxProgressLevel + "; it must be " + band.MinProgressLevel + "-" + BeastProgression.MaxLevel + ".");
                }

                expectedMin = band.MaxProgressLevel + 1;
                if (band.XpPerHour < 0 || band.GoldPerHour < 0)
                {
                    errors.Add(at + " has a negative rate (XpPerHour " + band.XpPerHour + ", GoldPerHour " + band.GoldPerHour + ").");
                }

                if (float.IsNaN(band.MaterialChanceMultiplier) || band.MaterialChanceMultiplier < 0f || band.MaterialChanceMultiplier > 1f)
                {
                    errors.Add(at + " MaterialChanceMultiplier " + band.MaterialChanceMultiplier + " is outside 0-1.");
                }

                if (band.CosmeticChancePer10k < 0 || band.CosmeticChancePer10k > IdleRewardsData.MaxCosmeticChancePer10k)
                {
                    errors.Add(at + " CosmeticChancePer10k " + band.CosmeticChancePer10k + " is outside 0-" + IdleRewardsData.MaxCosmeticChancePer10k + " (at most 1% per claim).");
                }

                if (previous != null && (band.XpPerHour < previous.XpPerHour || band.GoldPerHour < previous.GoldPerHour))
                {
                    errors.Add(at + " pays less per hour than the band before it; XP and gold per hour must never fall as the progress level rises.");
                }

                previous = band;
            }

            if (expectedMin != BeastProgression.MaxLevel + 1)
            {
                errors.Add("Bands end at " + (expectedMin - 1) + "; they must cover progress levels 1-" + BeastProgression.MaxLevel + ".");
            }
        }
    }
}
