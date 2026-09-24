using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Customization;
using BeastCraft.Progression;
using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The cosmetic library (<c>cosmetic-library.json</c>) indexed for the game: categories by id
    /// with their owner (the avatar, or one species), options by key, which looks are free, and the
    /// pools the Trader, battle drops, lairs and milestones draw from. Built from
    /// <see cref="CosmeticLibraryData"/> (expects data that passed <see cref="CosmeticLibraryValidator"/>;
    /// bad entries are skipped). Immutable once built.
    /// </summary>
    public sealed class CosmeticLibrary
    {
        public const string SourceDefault = "default";
        public const string SourceStarter = "starter";
        public const string SourceShop = "shop";
        public const string SourceBoss = "boss";
        public const string SourceMilestone = "milestone";
        public const string SourceDrop = "drop";
        public const string SourcePremium = "premium";

        /// <summary>A category's <see cref="CosmeticCategory.Scope"/> for the avatar.</summary>
        public const string AvatarScope = "avatar";

        /// <summary>Every source name, in the validator's order.</summary>
        public static readonly string[] Sources = { SourceDefault, SourceStarter, SourceShop, SourceBoss, SourceMilestone, SourceDrop, SourcePremium };

        private readonly List<CosmeticCategory> _categories = new List<CosmeticCategory>();
        private readonly Dictionary<string, CosmeticCategory> _byId = new Dictionary<string, CosmeticCategory>(StringComparer.Ordinal);
        private readonly Dictionary<string, CosmeticOption> _byKey = new Dictionary<string, CosmeticOption>(StringComparer.Ordinal);
        private readonly List<MilestoneData> _milestones = new List<MilestoneData>();

        private CosmeticLibrary()
        {
        }

        /// <summary>Every category, in authored order.</summary>
        public IReadOnlyList<CosmeticCategory> Categories
        {
            get { return _categories; }
        }

        /// <summary>The milestones, in authored order.</summary>
        public IReadOnlyList<MilestoneData> Milestones
        {
            get { return _milestones; }
        }

        /// <summary>Indexes <paramref name="data"/> (null gives an empty library).</summary>
        public static CosmeticLibrary Build(CosmeticLibraryData data)
        {
            CosmeticLibrary library = new CosmeticLibrary();
            if (data == null)
            {
                return library;
            }

            foreach (CosmeticCategoryData c in data.Categories ?? new CosmeticCategoryData[0])
            {
                if (c == null || string.IsNullOrEmpty(c.CategoryId) || library._byId.ContainsKey(c.CategoryId))
                {
                    continue;
                }

                CosmeticCategory category = new CosmeticCategory(c);
                library._categories.Add(category);
                library._byId.Add(c.CategoryId, category);
                foreach (CosmeticOption option in category.Options)
                {
                    library._byKey[option.Key] = option;
                }
            }

            foreach (MilestoneData m in data.Milestones ?? new MilestoneData[0])
            {
                if (m != null && !string.IsNullOrEmpty(m.MilestoneId))
                {
                    library._milestones.Add(m);
                }
            }

            return library;
        }

        /// <summary>The category <paramref name="categoryId"/>, or null.</summary>
        public CosmeticCategory GetCategory(string categoryId)
        {
            return categoryId != null && _byId.TryGetValue(categoryId, out CosmeticCategory c) ? c : null;
        }

        /// <summary>The option with unlock key <paramref name="key"/> (<c>"categoryId/optionId"</c>), or null.</summary>
        public CosmeticOption GetOption(string key)
        {
            return key != null && _byKey.TryGetValue(key, out CosmeticOption o) ? o : null;
        }

        /// <summary>The option <paramref name="optionId"/> of <paramref name="categoryId"/>, or null.</summary>
        public CosmeticOption GetOption(string categoryId, string optionId)
        {
            return GetOption(CosmeticCollection.Key(categoryId, optionId));
        }

        /// <summary>The milestone <paramref name="milestoneId"/>, or null.</summary>
        public MilestoneData GetMilestone(string milestoneId)
        {
            return _milestones.Find(m => string.Equals(m.MilestoneId, milestoneId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Options tagged <paramref name="source"/> whose <see cref="CosmeticOption.MinRegion"/> is at
        /// most <paramref name="region"/> (1-based), sorted by key (a seeded draw is stable whatever
        /// the authored order).
        /// </summary>
        public List<CosmeticOption> Pool(string source, int region)
        {
            List<CosmeticOption> pool = new List<CosmeticOption>();
            foreach (CosmeticCategory category in _categories)
            {
                foreach (CosmeticOption option in category.Options)
                {
                    if (option.Source == source && option.MinRegion <= region)
                    {
                        pool.Add(option);
                    }
                }
            }

            pool.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pool;
        }

        /// <summary>The 1-based region an encounter level falls in (levels 1-10 region 1, …, 91-100 region 10).</summary>
        public static int RegionOfLevel(int level)
        {
            return Math.Max(1, Math.Min(10, ((Math.Max(1, level) - 1) / 10) + 1));
        }
    }

    /// <summary>One category of the <see cref="CosmeticLibrary"/>.</summary>
    public sealed class CosmeticCategory
    {
        private readonly List<CosmeticOption> _options = new List<CosmeticOption>();

        internal CosmeticCategory(CosmeticCategoryData data)
        {
            CategoryId = data.CategoryId;
            DisplayName = data.DisplayName ?? data.CategoryId;
            Scope = data.Scope ?? string.Empty;
            IsColor = data.ValueType == CustomizationValueType.ColorPicker.ToString();
            DefaultColor = ParseColor(data.DefaultColor);
            foreach (CosmeticOptionData o in data.Options ?? new CosmeticOptionData[0])
            {
                if (o != null && !string.IsNullOrEmpty(o.OptionId) && _options.Find(x => x.OptionId == o.OptionId) == null)
                {
                    _options.Add(new CosmeticOption(this, o));
                }
            }
        }

        public string CategoryId { get; }

        public string DisplayName { get; }

        /// <summary><see cref="CosmeticLibrary.AvatarScope"/>, or the species id whose beasts wear it.</summary>
        public string Scope { get; }

        /// <summary>Whether it is the avatar's.</summary>
        public bool IsAvatar
        {
            get { return Scope == CosmeticLibrary.AvatarScope; }
        }

        /// <summary>A colour picker (free, no options) rather than discrete options.</summary>
        public bool IsColor { get; }

        public Color DefaultColor { get; }

        public IReadOnlyList<CosmeticOption> Options
        {
            get { return _options; }
        }

        /// <summary>The default option (null for a colour category).</summary>
        public CosmeticOption Default
        {
            get { return _options.Find(o => o.IsDefault); }
        }

        /// <summary><c>#RRGGBB</c> as a colour (white when unreadable).</summary>
        public static Color ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#' ||
                !int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return Color.white;
            }

            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }

    /// <summary>One look of a <see cref="CosmeticCategory"/>.</summary>
    public sealed class CosmeticOption
    {
        internal CosmeticOption(CosmeticCategory category, CosmeticOptionData data)
        {
            Category = category;
            OptionId = data.OptionId;
            Key = CosmeticCollection.Key(category.CategoryId, data.OptionId);
            DisplayName = data.DisplayName ?? data.OptionId;
            IsDefault = data.IsDefault;
            SortOrder = data.SortOrder;
            SupportsPaletteShader = data.SupportsPaletteShader;
            Rarity = data.Rarity;
            Source = data.Source ?? string.Empty;
            MinRegion = Math.Max(1, data.MinRegion);
            UnlockId = data.UnlockId ?? string.Empty;
            ArtKey = data.ArtKey ?? string.Empty;
        }

        public CosmeticCategory Category { get; }

        public string OptionId { get; }

        /// <summary>The unlock key, <c>"categoryId/optionId"</c>.</summary>
        public string Key { get; }

        public string DisplayName { get; }

        public bool IsDefault { get; }

        public int SortOrder { get; }

        public bool SupportsPaletteShader { get; }

        public int Rarity { get; }

        public string Source { get; }

        public int MinRegion { get; }

        public string UnlockId { get; }

        public string ArtKey { get; }

        /// <summary>Free to wear without an unlock: the default or a starter look.</summary>
        public bool IsFree
        {
            get { return IsDefault || Source == CosmeticLibrary.SourceDefault || Source == CosmeticLibrary.SourceStarter; }
        }
    }
}
