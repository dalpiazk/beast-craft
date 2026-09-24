using System;
using System.Collections.Generic;
using BeastCraft.Customization;
using BeastCraft.Economy;

namespace BeastCraft.Save
{
    /// <summary>
    /// <see cref="SaveValidator"/>'s schema-4 economy checks, kept apart for size: gold, consumable
    /// stacks, frozen Trader visits, cosmetic unlocks and appearances. Reports, never changes.
    /// </summary>
    internal static class SaveEconomyValidator
    {
        public static void Validate(PlayerSave save, ISaveEconomyCatalog catalog, List<SaveIssue> issues)
        {
            if (save.Gold < 0 || save.Gold > Wallet.MaxGold)
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidValue, "Gold", null, save.Gold + " is out of range (0 to " + Wallet.MaxGold + ")"));
            }

            ValidateConsumables(save.Consumables, catalog, issues);
            ValidateShops(save.Shops, issues);
            ValidateCosmetics(save, catalog, issues);
        }

        private static void ValidateConsumables(List<ConsumableStack> stacks, ISaveEconomyCatalog catalog, List<SaveIssue> issues)
        {
            if (stacks == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < stacks.Count; i++)
            {
                ConsumableStack stack = stacks[i];
                string path = "Consumables[" + i + "]";
                if (stack == null)
                {
                    continue;
                }

                int maxStack = int.MaxValue;
                if (string.IsNullOrEmpty(stack.ConsumableId) || (catalog != null && !catalog.TryGetConsumable(stack.ConsumableId, out maxStack)))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownConsumable, path + ".ConsumableId", stack.ConsumableId, "unknown consumable '" + stack.ConsumableId + "'"));
                    maxStack = int.MaxValue;
                }
                else if (!seen.Add(stack.ConsumableId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownConsumable, path + ".ConsumableId", stack.ConsumableId, "'" + stack.ConsumableId + "' is held in two stacks"));
                }

                if (stack.Quantity < 1 || stack.Quantity > maxStack)
                {
                    string range = maxStack == int.MaxValue ? "at least 1" : "1 to " + maxStack;
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidValue, path + ".Quantity", stack.ConsumableId, stack.Quantity + " is out of range (" + range + ")"));
                }
            }
        }

        private static void ValidateShops(List<ShopVisit> visits, List<SaveIssue> issues)
        {
            if (visits == null)
            {
                return;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < visits.Count; i++)
            {
                ShopVisit visit = visits[i];
                string path = "Shops[" + i + "]";
                if (visit == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(visit.NodeKey) || !keys.Add(visit.NodeKey))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidShopVisit, path + ".NodeKey", visit.NodeKey, "the visit's key is empty or used twice"));
                }

                for (int l = 0; visit.Listings != null && l < visit.Listings.Count; l++)
                {
                    ShopListing listing = visit.Listings[l];
                    if (listing == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(listing.ItemId) || !Enum.IsDefined(typeof(ShopCategory), listing.Category) || listing.Quantity < 1 ||
                        listing.Remaining < 0 || listing.Remaining > listing.Quantity || listing.Price < 0)
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.InvalidShopVisit, path + ".Listings[" + l + "]", listing.ItemId,
                                                 "listing " + listing.Category + " '" + listing.ItemId + "' has a bad id, category, count or price"));
                    }
                }
            }
        }

        private static void ValidateCosmetics(PlayerSave save, ISaveEconomyCatalog catalog, List<SaveIssue> issues)
        {
            HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);
            List<string> keys = save.Cosmetics == null ? null : save.Cosmetics.Unlocked;
            for (int i = 0; keys != null && i < keys.Count; i++)
            {
                string key = keys[i];
                string path = "Cosmetics.Unlocked[" + i + "]";
                int slash = key == null ? -1 : key.IndexOf('/');
                bool known = slash > 0 && slash < key.Length - 1 &&
                             (catalog == null || catalog.IsKnownCosmeticOption(key.Substring(0, slash), key.Substring(slash + 1)));
                if (!known)
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownCosmetic, path, key, "unknown cosmetic '" + key + "'"));
                }
                else if (!unlocked.Add(key))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownCosmetic, path, key, "'" + key + "' is unlocked twice"));
                }
            }

            ValidateAppearance(save.AvatarAppearance, "AvatarAppearance", string.Empty, catalog, unlocked, issues);
            for (int b = 0; save.Beasts != null && b < save.Beasts.Count; b++)
            {
                OwnedBeast beast = save.Beasts[b];
                if (beast != null)
                {
                    ValidateAppearance(beast.Appearance, "Beasts[" + b + "].Appearance", beast.Progress == null ? null : beast.Progress.SpeciesId, catalog, unlocked,
                                       issues);
                }
            }
        }

        private static void ValidateAppearance(CustomizationSelection appearance, string path, string ownerSpecies, ISaveEconomyCatalog catalog, HashSet<string> unlocked,
                                               List<SaveIssue> issues)
        {
            if (appearance == null)
            {
                return;
            }

            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; appearance.OptionEntries != null && i < appearance.OptionEntries.Count; i++)
            {
                CustomizationSelection.CategoryOptionEntry entry = appearance.OptionEntries[i];
                string at = path + ".OptionEntries[" + i + "]";
                if (string.IsNullOrEmpty(entry.CategoryId) || !categories.Add(entry.CategoryId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownCosmetic, at, entry.CategoryId, "category id is empty or chosen twice"));
                    continue;
                }

                if (catalog == null)
                {
                    continue;
                }

                if (!OwnsCategory(catalog, entry.CategoryId, ownerSpecies, false) || !catalog.IsKnownCosmeticOption(entry.CategoryId, entry.OptionId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownCosmetic, at, CosmeticCollection.Key(entry.CategoryId, entry.OptionId),
                                             "'" + CosmeticCollection.Key(entry.CategoryId, entry.OptionId) + "' is not a look this owner can wear"));
                }
                else if (!catalog.IsFreeCosmetic(entry.CategoryId, entry.OptionId) && !unlocked.Contains(CosmeticCollection.Key(entry.CategoryId, entry.OptionId)))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.CosmeticNotUnlocked, at, CosmeticCollection.Key(entry.CategoryId, entry.OptionId),
                                             "'" + CosmeticCollection.Key(entry.CategoryId, entry.OptionId) + "' is worn but not unlocked"));
                }
            }

            HashSet<string> colours = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; appearance.ColorEntries != null && i < appearance.ColorEntries.Count; i++)
            {
                CustomizationSelection.CategoryColorEntry entry = appearance.ColorEntries[i];
                string at = path + ".ColorEntries[" + i + "]";
                if (string.IsNullOrEmpty(entry.CategoryId) || !colours.Add(entry.CategoryId) ||
                    (catalog != null && !OwnsCategory(catalog, entry.CategoryId, ownerSpecies, true)))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownCosmetic, at, entry.CategoryId, "colour category '" + entry.CategoryId + "' is unknown, not this owner's, or chosen twice"));
                }
            }
        }

        private static bool OwnsCategory(ISaveEconomyCatalog catalog, string categoryId, string ownerSpecies, bool colour)
        {
            return catalog.TryGetCosmeticCategory(categoryId, out string species, out bool isColor) && isColor == colour &&
                   string.Equals(species ?? string.Empty, ownerSpecies ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
