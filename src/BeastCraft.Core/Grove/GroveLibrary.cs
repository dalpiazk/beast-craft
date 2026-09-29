using System;
using System.Collections.Generic;

namespace BeastCraft.Grove
{
    /// <summary>Lookups over a validated <see cref="GroveLibraryData"/> (<c>grove-library.json</c>).</summary>
    public sealed class GroveLibrary
    {
        private readonly Dictionary<string, HabitatData> _habitats = new Dictionary<string, HabitatData>(StringComparer.Ordinal);
        private readonly Dictionary<string, DecorData> _decor = new Dictionary<string, DecorData>(StringComparer.Ordinal);
        private readonly Dictionary<string, GroveLoreEntryData> _lore = new Dictionary<string, GroveLoreEntryData>(StringComparer.Ordinal);
        private readonly Dictionary<string, GiftTableData> _giftTables = new Dictionary<string, GiftTableData>(StringComparer.Ordinal);

        // (SpeciesId, Tier) -> row.
        private readonly Dictionary<string, AffinityTierData> _tiers = new Dictionary<string, AffinityTierData>(StringComparer.Ordinal);

        private GroveLibrary(GroveLibraryData data)
        {
            Data = data;
        }

        public GroveLibraryData Data { get; }

        public static GroveLibrary Build(GroveLibraryData data)
        {
            GroveLibrary library = new GroveLibrary(data ?? new GroveLibraryData());

            foreach (HabitatData habitat in library.Data.Habitats ?? new HabitatData[0])
            {
                if (habitat != null && !string.IsNullOrEmpty(habitat.HabitatId))
                {
                    library._habitats[habitat.HabitatId] = habitat;
                }
            }

            foreach (DecorData decor in library.Data.Decor ?? new DecorData[0])
            {
                if (decor != null && !string.IsNullOrEmpty(decor.DecorId))
                {
                    library._decor[decor.DecorId] = decor;
                }
            }

            foreach (GroveLoreEntryData lore in library.Data.Lore ?? new GroveLoreEntryData[0])
            {
                if (lore != null && !string.IsNullOrEmpty(lore.LoreId))
                {
                    library._lore[lore.LoreId] = lore;
                }
            }

            foreach (GiftTableData table in library.Data.GiftTables ?? new GiftTableData[0])
            {
                if (table != null && !string.IsNullOrEmpty(table.SpeciesId))
                {
                    library._giftTables[table.SpeciesId] = table;
                }
            }

            foreach (AffinityTierData tier in library.Data.AffinityTiers ?? new AffinityTierData[0])
            {
                if (tier != null && !string.IsNullOrEmpty(tier.SpeciesId))
                {
                    library._tiers[TierKey(tier.SpeciesId, tier.Tier)] = tier;
                }
            }

            return library;
        }

        public HabitatData Habitat(string habitatId)
        {
            return habitatId != null && _habitats.TryGetValue(habitatId, out HabitatData habitat) ? habitat : null;
        }

        public DecorData Decor(string decorId)
        {
            return decorId != null && _decor.TryGetValue(decorId, out DecorData decor) ? decor : null;
        }

        public GroveLoreEntryData Lore(string loreId)
        {
            return loreId != null && _lore.TryGetValue(loreId, out GroveLoreEntryData lore) ? lore : null;
        }

        /// <summary>Species <paramref name="speciesId"/>'s tier row, or null (unknown tier).</summary>
        public AffinityTierData Tier(string speciesId, int tier)
        {
            return speciesId != null && _tiers.TryGetValue(TierKey(speciesId, tier), out AffinityTierData row) ? row : null;
        }

        /// <summary>The highest tier defined for any species (5 in v1's content).</summary>
        public int MaxTier
        {
            get
            {
                int max = 0;
                foreach (AffinityTierData tier in Data.AffinityTiers ?? new AffinityTierData[0])
                {
                    if (tier != null)
                    {
                        max = Math.Max(max, tier.Tier);
                    }
                }

                return max;
            }
        }

        /// <summary>
        /// <paramref name="speciesId"/>'s own gift table, else the <c>"default"</c> one, else null (no
        /// gift table at all, which the validator refuses to ship).
        /// </summary>
        public GiftTableData GiftTableFor(string speciesId)
        {
            if (speciesId != null && _giftTables.TryGetValue(speciesId, out GiftTableData own))
            {
                return own;
            }

            return _giftTables.TryGetValue("default", out GiftTableData fallback) ? fallback : null;
        }

        /// <summary>The gift interval in hours for <paramref name="tier"/> (1-5; out of range clamps to the nearest defined entry).</summary>
        public int GiftHoursForTier(int tier)
        {
            int[] byTier = Data.GiftHoursByTier ?? new int[0];
            if (byTier.Length == 0)
            {
                return 4;
            }

            int index = Math.Max(0, Math.Min(byTier.Length - 1, tier - 1));
            return Math.Max(1, byTier[index]);
        }

        private static string TierKey(string speciesId, int tier)
        {
            return speciesId + "/" + tier.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
