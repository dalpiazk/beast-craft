using System;
using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Economy;

namespace BeastCraft.Grove
{
    /// <summary>
    /// Validates <c>grove-library.json</c> (<see cref="GroveLibraryData"/>): unique snake_case ids;
    /// every habitat and decor piece's shrine unlock a known <c>discovery.json</c>
    /// <c>ShrineData.GroveUnlockId</c>, used by no other habitat or decor in this file; every affinity
    /// tier row a known species at tier 1-5 with an ascending XP threshold and, per its
    /// <c>RewardKind</c>, a reward that resolves (a Grove lore entry, a decor piece, or a <c>"grove"</c>
    /// look when a cosmetic library is given); every gift table's entries positive-weight and
    /// resolving, with at least one common and one non-common entry (pity needs both) and a
    /// <c>"default"</c> table present so every species always has one. Display names and lore follow
    /// the content bible (<see cref="MaxNameLength"/>, <see cref="MaxTextLength"/>).
    /// </summary>
    public static class GroveLibraryValidator
    {
        public const int MaxNameLength = 24;

        public const int MaxTextLength = 280;

        public const int MinSlotCount = 1;

        public const int MaxSlotCount = 12;

        public static List<string> Validate(GroveLibraryData data, DiscoveryLibraryData discovery, ICollection<string> speciesIds, CosmeticLibraryData cosmetics)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("grove-library.json is missing.");
                return errors;
            }

            if (data.SchemaVersion != 1)
            {
                errors.Add("SchemaVersion must be 1.");
            }

            if (data.FeedXp <= 0 || data.PlayXp <= 0)
            {
                errors.Add("FeedXp and PlayXp must be positive.");
            }

            if (data.DailyCooldownHours <= 0)
            {
                errors.Add("DailyCooldownHours must be positive.");
            }

            CheckGiftHours(errors, data.GiftHoursByTier);

            HashSet<string> shrineIds = ShrineIds(discovery);
            HashSet<string> usedShrineIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            Dictionary<string, HabitatData> habitats = new Dictionary<string, HabitatData>(StringComparer.Ordinal);
            foreach (HabitatData habitat in data.Habitats ?? new HabitatData[0])
            {
                string at = "Habitats[" + (habitat == null ? "?" : habitat.HabitatId) + "]";
                if (habitat == null || !CheckId(errors, "Habitats", habitat.HabitatId, ids))
                {
                    continue;
                }

                habitats[habitat.HabitatId] = habitat;
                CheckName(errors, at + ": DisplayName", habitat.DisplayName);
                if (habitat.SlotCount < MinSlotCount || habitat.SlotCount > MaxSlotCount)
                {
                    errors.Add(at + ": SlotCount must be " + MinSlotCount + "-" + MaxSlotCount + ".");
                }

                CheckShrineSource(errors, at, habitat.UnlockSource, habitat.UnlockId, shrineIds, usedShrineIds, new[] { "shrine", "start" });
            }

            Dictionary<string, DecorData> decor = new Dictionary<string, DecorData>(StringComparer.Ordinal);
            foreach (DecorData piece in data.Decor ?? new DecorData[0])
            {
                string at = "Decor[" + (piece == null ? "?" : piece.DecorId) + "]";
                if (piece == null || !CheckId(errors, "Decor", piece.DecorId, ids))
                {
                    continue;
                }

                decor[piece.DecorId] = piece;
                CheckName(errors, at + ": DisplayName", piece.DisplayName);
                if (!string.IsNullOrEmpty(piece.HabitatScope) && !habitats.ContainsKey(piece.HabitatScope))
                {
                    errors.Add(at + ": HabitatScope '" + piece.HabitatScope + "' is not a habitat (or \"\" for any).");
                }

                switch (piece.Source)
                {
                    case "shrine":
                        CheckShrineSource(errors, at, piece.Source, piece.UnlockId, shrineIds, usedShrineIds, new[] { "shrine" });
                        break;
                    case "affinity":
                    case "garden_craft":
                    case "starter":
                    case "expedition":
                        break;
                    default:
                        errors.Add(at + ": Source must be shrine, affinity, garden_craft, expedition or starter.");
                        break;
                }
            }

            Dictionary<string, GroveLoreEntryData> lore = new Dictionary<string, GroveLoreEntryData>(StringComparer.Ordinal);
            foreach (GroveLoreEntryData entry in data.Lore ?? new GroveLoreEntryData[0])
            {
                string at = "Lore[" + (entry == null ? "?" : entry.LoreId) + "]";
                if (entry == null || !CheckId(errors, "Lore", entry.LoreId, ids))
                {
                    continue;
                }

                lore[entry.LoreId] = entry;
                CheckName(errors, at + ": Title", entry.Title);
                CheckText(errors, at + ": Text", entry.Text);
            }

            HashSet<string> tierKeys = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> highestThreshold = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (AffinityTierData tier in data.AffinityTiers ?? new AffinityTierData[0])
            {
                if (tier == null)
                {
                    errors.Add("AffinityTiers: an entry is null.");
                    continue;
                }

                string at = "AffinityTiers[" + tier.SpeciesId + "/" + tier.Tier + "]";
                if (string.IsNullOrEmpty(tier.SpeciesId) || (speciesIds != null && !speciesIds.Contains(tier.SpeciesId)))
                {
                    errors.Add(at + ": SpeciesId '" + tier.SpeciesId + "' is unknown.");
                }

                if (tier.Tier < 1 || tier.Tier > 5)
                {
                    errors.Add(at + ": Tier must be 1-5.");
                }

                string key = tier.SpeciesId + "/" + tier.Tier;
                if (!tierKeys.Add(key))
                {
                    errors.Add(at + ": listed twice for this species.");
                }

                int previous = highestThreshold.TryGetValue(tier.SpeciesId ?? string.Empty, out int value) ? value : 0;
                if (tier.XpThreshold <= previous)
                {
                    errors.Add(at + ": XpThreshold must strictly increase with Tier.");
                }

                highestThreshold[tier.SpeciesId ?? string.Empty] = Math.Max(previous, tier.XpThreshold);

                switch (tier.RewardKind)
                {
                    case "lore":
                        if (!lore.ContainsKey(tier.RewardId ?? string.Empty))
                        {
                            errors.Add(at + ": RewardId must be a Grove lore entry.");
                        }

                        break;
                    case "decor":
                        if (!decor.ContainsKey(tier.RewardId ?? string.Empty))
                        {
                            errors.Add(at + ": RewardId must be a decor piece.");
                        }

                        break;
                    case "look":
                        CheckLook(errors, at + ": RewardId", tier.RewardId, "affinity_" + tier.SpeciesId + "_" + tier.Tier, cosmetics);
                        break;
                    case "idle_anim":
                    case "none":
                        break;
                    default:
                        errors.Add(at + ": RewardKind must be lore, decor, look, idle_anim or none.");
                        break;
                }
            }

            if (speciesIds != null)
            {
                foreach (string speciesId in speciesIds)
                {
                    for (int tier = 1; tier <= 5; tier++)
                    {
                        if (!tierKeys.Contains(speciesId + "/" + tier))
                        {
                            errors.Add("AffinityTiers: species '" + speciesId + "' is missing tier " + tier + ".");
                        }
                    }
                }
            }

            bool hasDefaultTable = false;
            HashSet<string> tableSpecies = new HashSet<string>(StringComparer.Ordinal);
            foreach (GiftTableData table in data.GiftTables ?? new GiftTableData[0])
            {
                if (table == null || string.IsNullOrEmpty(table.SpeciesId))
                {
                    errors.Add("GiftTables: an entry has no SpeciesId.");
                    continue;
                }

                string at = "GiftTables[" + table.SpeciesId + "]";
                if (!tableSpecies.Add(table.SpeciesId))
                {
                    errors.Add(at + ": listed twice.");
                }

                hasDefaultTable |= table.SpeciesId == "default";
                if (table.SpeciesId != "default" && speciesIds != null && !speciesIds.Contains(table.SpeciesId))
                {
                    errors.Add(at + ": SpeciesId is neither a known species nor \"default\".");
                }

                if (table.PityAt < 1)
                {
                    errors.Add(at + ": PityAt must be at least 1.");
                }

                ValidateGiftEntries(errors, at, table.Entries, decor, lore, cosmetics);
            }

            if (!hasDefaultTable)
            {
                errors.Add("GiftTables: needs a \"default\" table (species without their own fall back to it).");
            }

            return errors;
        }

        private static void ValidateGiftEntries(List<string> errors, string at, GiftEntryData[] entries, Dictionary<string, DecorData> decor,
                                                 Dictionary<string, GroveLoreEntryData> lore, CosmeticLibraryData cosmetics)
        {
            int total = 0;
            bool hasCommon = false;
            bool hasNonCommon = false;
            foreach (GiftEntryData entry in entries ?? new GiftEntryData[0])
            {
                if (entry == null || entry.Weight <= 0)
                {
                    errors.Add(at + ": every entry needs a positive Weight.");
                    continue;
                }

                total += entry.Weight;
                hasCommon |= entry.IsCommon;
                hasNonCommon |= !entry.IsCommon;

                switch (entry.ItemKind)
                {
                    case "decor":
                        if (!decor.ContainsKey(entry.ItemId ?? string.Empty))
                        {
                            errors.Add(at + ": decor entry '" + entry.ItemId + "' is not a decor piece.");
                        }

                        break;
                    case "lore":
                        if (!lore.ContainsKey(entry.ItemId ?? string.Empty))
                        {
                            errors.Add(at + ": lore entry '" + entry.ItemId + "' is not a Grove lore entry.");
                        }

                        break;
                    case "cosmetic":
                        CheckLook(errors, at + ": cosmetic entry", entry.ItemId, "gift_" + entry.ItemId, cosmetics);
                        break;
                    default:
                        errors.Add(at + ": ItemKind must be decor, lore or cosmetic.");
                        break;
                }
            }

            if (total <= 0)
            {
                errors.Add(at + ": needs at least one entry.");
            }

            if (!hasCommon || !hasNonCommon)
            {
                errors.Add(at + ": needs at least one common and one non-common entry for pity to mean anything.");
            }
        }

        private static void CheckGiftHours(List<string> errors, int[] hours)
        {
            if (hours == null || hours.Length != 5)
            {
                errors.Add("GiftHoursByTier must have exactly 5 entries.");
                return;
            }

            for (int i = 0; i < hours.Length; i++)
            {
                if (hours[i] <= 0)
                {
                    errors.Add("GiftHoursByTier[" + i + "] must be positive.");
                }

                if (i > 0 && hours[i] > hours[i - 1])
                {
                    errors.Add("GiftHoursByTier must not increase with tier (index " + i + " is longer than " + (i - 1) + ").");
                }
            }
        }

        private static void CheckShrineSource(List<string> errors, string at, string source, string unlockId, HashSet<string> shrineIds,
                                              HashSet<string> usedShrineIds, string[] allowedSources)
        {
            if (Array.IndexOf(allowedSources, source) < 0)
            {
                errors.Add(at + ": UnlockSource must be " + string.Join(" or ", allowedSources) + ".");
                return;
            }

            if (source != "shrine")
            {
                return;
            }

            if (string.IsNullOrEmpty(unlockId) || (shrineIds != null && !shrineIds.Contains(unlockId)))
            {
                errors.Add(at + ": UnlockId '" + unlockId + "' is not a shrine's GroveUnlockId (discovery.json).");
                return;
            }

            if (!usedShrineIds.Add(unlockId))
            {
                errors.Add(at + ": UnlockId '" + unlockId + "' is claimed by more than one habitat or decor piece.");
            }
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

        private static bool CheckId(List<string> errors, string list, string id, HashSet<string> ids)
        {
            if (!IsSnakeCase(id))
            {
                errors.Add(list + ": id '" + id + "' must be snake_case.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add(list + ": id '" + id + "' is used twice in grove-library.json.");
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
