using System;
using System.Collections.Generic;
using BeastCraft.Customization;
using BeastCraft.Progression;
using BeastCraft.Save;
using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>What <see cref="CosmeticRules.TrySetOption"/> / <see cref="CosmeticRules.TrySetColor"/> did.</summary>
    public enum CosmeticResult
    {
        /// <summary>The look is now worn (or already was).</summary>
        Set,

        /// <summary>No such beast in the save.</summary>
        UnknownOwner,

        /// <summary>No such category in the library.</summary>
        UnknownCategory,

        /// <summary>The category is another owner's (a species' look on the avatar, or another species').</summary>
        WrongOwner,

        /// <summary>No such option in the category.</summary>
        UnknownOption,

        /// <summary>The look is neither free nor unlocked.</summary>
        Locked,

        /// <summary>A colour was set on a discrete category, or an option on a colour category.</summary>
        WrongValueType
    }

    /// <summary>
    /// The cosmetic rules over a <see cref="PlayerSave"/> (schema 4): which looks are usable (free —
    /// the default and starter looks — or unlocked account-wide in <see cref="PlayerSave.Cosmetics"/>),
    /// wearing them (the avatar's <see cref="PlayerSave.AvatarAppearance"/>, each beast's
    /// <see cref="OwnedBeast.Appearance"/>; a beast wears only its own species' categories), and the
    /// unlock sources other than the Trader: lairs (boss-exclusive looks), milestones (a beast of the
    /// species reaching a level, the avatar's level, bosses beaten), and low-chance battle drops.
    /// Colour pickers are always free. A category missing from an appearance reads as its default.
    /// Purely cosmetic: nothing here touches stats. Non-throwing; a refused call changes nothing.
    /// </summary>
    public static class CosmeticRules
    {
        /// <summary>Whether <paramref name="option"/> may be worn: free, or unlocked in the save.</summary>
        public static bool IsUsable(PlayerSave save, CosmeticOption option)
        {
            return option != null && (option.IsFree || (save != null && save.Cosmetics != null && save.Cosmetics.Has(option.Key)));
        }

        /// <summary>
        /// The appearance of <paramref name="beastId"/> (null = the avatar), or null for an unknown beast.
        /// </summary>
        public static CustomizationSelection AppearanceOf(PlayerSave save, string beastId)
        {
            if (save == null)
            {
                return null;
            }

            if (beastId == null)
            {
                return save.AvatarAppearance ?? (save.AvatarAppearance = new CustomizationSelection());
            }

            OwnedBeast beast = save.FindBeast(beastId);
            return beast == null ? null : beast.Appearance ?? (beast.Appearance = new CustomizationSelection());
        }

        /// <summary>
        /// The option <paramref name="beastId"/> (null = the avatar) wears in <paramref name="categoryId"/>:
        /// its chosen one when usable, else the category's default. Null for an unknown category.
        /// </summary>
        public static CosmeticOption Worn(PlayerSave save, string beastId, string categoryId, CosmeticLibrary library)
        {
            CosmeticCategory category = library == null ? null : library.GetCategory(categoryId);
            if (category == null || category.IsColor)
            {
                return null;
            }

            CustomizationSelection appearance = AppearanceOf(save, beastId);
            CosmeticOption chosen = appearance == null ? null : library.GetOption(categoryId, appearance.GetOption(categoryId));
            return IsUsable(save, chosen) ? chosen : category.Default;
        }

        /// <summary>Wears <paramref name="optionId"/> of <paramref name="categoryId"/> on <paramref name="beastId"/> (null = the avatar). See <see cref="CosmeticResult"/>.</summary>
        public static CosmeticResult TrySetOption(PlayerSave save, string beastId, string categoryId, string optionId, CosmeticLibrary library)
        {
            CosmeticResult check = CheckOwner(save, beastId, categoryId, library, false, out CosmeticCategory category);
            if (check != CosmeticResult.Set)
            {
                return check;
            }

            CosmeticOption option = library.GetOption(category.CategoryId, optionId);
            if (option == null)
            {
                return CosmeticResult.UnknownOption;
            }

            if (!IsUsable(save, option))
            {
                return CosmeticResult.Locked;
            }

            AppearanceOf(save, beastId).SetOption(category.CategoryId, option.OptionId);
            return CosmeticResult.Set;
        }

        /// <summary>Sets colour category <paramref name="categoryId"/> on <paramref name="beastId"/> (null = the avatar). Colours are free.</summary>
        public static CosmeticResult TrySetColor(PlayerSave save, string beastId, string categoryId, Color color, CosmeticLibrary library)
        {
            CosmeticResult check = CheckOwner(save, beastId, categoryId, library, true, out CosmeticCategory category);
            if (check != CosmeticResult.Set)
            {
                return check;
            }

            AppearanceOf(save, beastId).SetColor(category.CategoryId, color);
            return CosmeticResult.Set;
        }

        /// <summary>Unlocks the look with <paramref name="key"/> account-wide. False when unknown, free, or already unlocked.</summary>
        public static bool Unlock(PlayerSave save, CosmeticLibrary library, string key)
        {
            CosmeticOption option = library == null ? null : library.GetOption(key);
            if (save == null || option == null || option.IsFree)
            {
                return false;
            }

            if (save.Cosmetics == null)
            {
                save.Cosmetics = new CosmeticCollection();
            }

            return save.Cosmetics.Unlock(option.Key);
        }

        /// <summary>Unlocks every boss-exclusive look <paramref name="regionId"/>'s lair grants. Returns the keys newly unlocked.</summary>
        public static List<string> UnlockBossLooks(PlayerSave save, CosmeticLibrary library, string regionId)
        {
            List<string> unlocked = new List<string>();
            foreach (CosmeticOption option in AllOptions(library))
            {
                if (option.Source == CosmeticLibrary.SourceBoss && string.Equals(option.UnlockId, regionId, StringComparison.Ordinal) && Unlock(save, library, option.Key))
                {
                    unlocked.Add(option.Key);
                }
            }

            return unlocked;
        }

        /// <summary>
        /// Unlocks every milestone look whose milestone the save has reached (see
        /// <see cref="MilestoneData"/>; a species look's <c>BeastLevel</c> needs a beast of that
        /// species, an avatar look's any beast). Idempotent. Returns the keys newly unlocked.
        /// </summary>
        public static List<string> UnlockMilestones(PlayerSave save, CosmeticLibrary library)
        {
            List<string> unlocked = new List<string>();
            if (save == null || library == null)
            {
                return unlocked;
            }

            foreach (CosmeticOption option in AllOptions(library))
            {
                if (option.Source == CosmeticLibrary.SourceMilestone && Reached(save, library.GetMilestone(option.UnlockId), option.Category) &&
                    Unlock(save, library, option.Key))
                {
                    unlocked.Add(option.Key);
                }
            }

            return unlocked;
        }

        /// <summary>
        /// A battle's cosmetic drop: <c>rng.Next(1000) &lt; table.CosmeticDropPerMille(shape)</c>
        /// (always drawn), and on a hit one look (a second draw) from the library's <c>drop</c> pool
        /// for the level's region (<see cref="CosmeticLibrary.RegionOfLevel"/>) that the save has not
        /// unlocked. Returns it (not yet unlocked; the caller unlocks it), or null.
        /// </summary>
        public static CosmeticOption RollDrop(DropTable table, CosmeticLibrary library, PlayerSave save, string shape, int level, System.Random rng)
        {
            if (table == null || library == null || rng == null)
            {
                return null;
            }

            if (rng.Next(1000) >= table.CosmeticDropPerMille(shape))
            {
                return null;
            }

            List<CosmeticOption> pool = library.Pool(CosmeticLibrary.SourceDrop, CosmeticLibrary.RegionOfLevel(level));
            pool.RemoveAll(o => IsUsable(save, o));
            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
        }

        /// <summary>
        /// Drops every appearance entry that is unknown, another owner's, or no longer usable (it
        /// reads as the default again). Returns how many entries were dropped.
        /// </summary>
        public static int RepairAppearances(PlayerSave save, CosmeticLibrary library)
        {
            if (save == null || library == null)
            {
                return 0;
            }

            int dropped = Repair(save, save.AvatarAppearance, null, library);
            foreach (OwnedBeast beast in save.Beasts ?? new List<OwnedBeast>())
            {
                dropped += beast == null ? 0 : Repair(save, beast.Appearance, beast.Progress == null ? null : beast.Progress.SpeciesId ?? string.Empty, library);
            }

            return dropped;
        }

        private static int Repair(PlayerSave save, CustomizationSelection appearance, string species, CosmeticLibrary library)
        {
            if (appearance == null)
            {
                return 0;
            }

            int dropped = 0;
            if (appearance.OptionEntries != null)
            {
                dropped += appearance.OptionEntries.RemoveAll(e =>
                {
                    CosmeticCategory category = library.GetCategory(e.CategoryId);
                    return category == null || category.IsColor || !Owns(category, species) || !IsUsable(save, library.GetOption(e.CategoryId, e.OptionId));
                });
            }

            if (appearance.ColorEntries != null)
            {
                dropped += appearance.ColorEntries.RemoveAll(e =>
                {
                    CosmeticCategory category = library.GetCategory(e.CategoryId);
                    return category == null || !category.IsColor || !Owns(category, species);
                });
            }

            return dropped;
        }

        private static bool Owns(CosmeticCategory category, string species)
        {
            return species == null ? category.IsAvatar : !category.IsAvatar && string.Equals(category.Scope, species, StringComparison.Ordinal);
        }

        private static CosmeticResult CheckOwner(PlayerSave save, string beastId, string categoryId, CosmeticLibrary library, bool colour, out CosmeticCategory category)
        {
            category = library == null ? null : library.GetCategory(categoryId);
            if (AppearanceOf(save, beastId) == null)
            {
                return CosmeticResult.UnknownOwner;
            }

            if (category == null)
            {
                return CosmeticResult.UnknownCategory;
            }

            string species = beastId == null ? null : save.FindBeast(beastId).Progress == null ? string.Empty : save.FindBeast(beastId).Progress.SpeciesId;
            if (!Owns(category, species))
            {
                return CosmeticResult.WrongOwner;
            }

            return category.IsColor == colour ? CosmeticResult.Set : CosmeticResult.WrongValueType;
        }

        private static bool Reached(PlayerSave save, MilestoneData milestone, CosmeticCategory category)
        {
            if (milestone == null)
            {
                return false;
            }

            switch (milestone.Kind)
            {
                case "AvatarLevel":
                    return save.Avatar != null && save.Avatar.Level >= milestone.Threshold;
                case "BossesCleared":
                    int bosses = 0;
                    foreach (Campaign.RegionProgress region in save.Campaign == null || save.Campaign.Regions == null ? new List<Campaign.RegionProgress>() : save.Campaign.Regions)
                    {
                        bosses += region != null && region.BossCleared ? 1 : 0;
                    }

                    return bosses >= milestone.Threshold;
                case "BeastLevel":
                    foreach (OwnedBeast beast in save.Beasts ?? new List<OwnedBeast>())
                    {
                        if (beast != null && beast.Progress != null && beast.Progress.Level >= milestone.Threshold &&
                            (category.IsAvatar || string.Equals(beast.Progress.SpeciesId, category.Scope, StringComparison.Ordinal)))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        private static IEnumerable<CosmeticOption> AllOptions(CosmeticLibrary library)
        {
            if (library == null)
            {
                yield break;
            }

            foreach (CosmeticCategory category in library.Categories)
            {
                foreach (CosmeticOption option in category.Options)
                {
                    yield return option;
                }
            }
        }
    }
}
