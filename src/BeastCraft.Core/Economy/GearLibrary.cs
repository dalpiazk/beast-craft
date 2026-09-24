using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The gear library (<c>gear-library.json</c>) indexed for the game: every piece of beast and
    /// avatar gear by id, its band, and the pools the Trader, battle drops and boss rewards draw
    /// from. Built from <see cref="GearLibraryData"/> (expects data that passed
    /// <see cref="GearLibraryValidator"/>; bad entries are skipped, never thrown on). Immutable once
    /// built, except that the Unity assets it hands out (<see cref="BeastGearAssets"/>,
    /// <see cref="AvatarGearAssets"/>) are created once on first use.
    /// </summary>
    public sealed class GearLibrary
    {
        /// <summary>Source tag: sold by the Trader.</summary>
        public const string SourceShop = "shop";

        /// <summary>Source tag: dropped by battles (<c>drop-tables.json</c> <c>GearDrops</c>).</summary>
        public const string SourceDrop = "drop";

        /// <summary>Source tag: granted by a pass's or lair's first clear.</summary>
        public const string SourceBoss = "boss";

        private readonly List<GearItem> _items = new List<GearItem>();
        private readonly Dictionary<string, GearItem> _byId = new Dictionary<string, GearItem>(StringComparer.Ordinal);
        private readonly List<int> _bandFloors = new List<int>();
        private readonly object _assetLock = new object();
        private List<GearSO> _beastAssets;
        private List<AvatarGearSO> _avatarAssets;

        private GearLibrary()
        {
        }

        /// <summary>Every piece, beast gear first, each in authored order.</summary>
        public IReadOnlyList<GearItem> Items
        {
            get { return _items; }
        }

        /// <summary>The distinct <see cref="GearItem.MinimumLevel"/>s (the band floors), ascending.</summary>
        public IReadOnlyList<int> BandFloors
        {
            get { return _bandFloors; }
        }

        /// <summary>Indexes <paramref name="data"/>. Null data gives an empty library.</summary>
        public static GearLibrary Build(GearLibraryData data)
        {
            GearLibrary library = new GearLibrary();
            if (data == null)
            {
                return library;
            }

            foreach (GearItemData entry in data.BeastGear ?? new GearItemData[0])
            {
                library.Add(entry, false);
            }

            foreach (GearItemData entry in data.AvatarGear ?? new GearItemData[0])
            {
                library.Add(entry, true);
            }

            library._bandFloors.Sort();
            return library;
        }

        /// <summary>The piece with <paramref name="gearId"/>, or null.</summary>
        public GearItem Get(string gearId)
        {
            return gearId != null && _byId.TryGetValue(gearId, out GearItem item) ? item : null;
        }

        /// <summary>
        /// The band <paramref name="level"/> falls in: the highest band floor at or below it (the
        /// lowest band for a level under every floor; 1 for an empty library).
        /// </summary>
        public int BandFloor(int level)
        {
            int floor = _bandFloors.Count == 0 ? 1 : _bandFloors[0];
            foreach (int f in _bandFloors)
            {
                if (f <= level)
                {
                    floor = f;
                }
            }

            return floor;
        }

        /// <summary>
        /// The pieces of <paramref name="rarity"/> tagged <paramref name="source"/> in
        /// <paramref name="level"/>'s band (<see cref="BandFloor"/>), sorted by id (so a seeded draw is
        /// stable whatever the authored order). <paramref name="avatar"/> null = beast and avatar gear,
        /// else just the one kind.
        /// </summary>
        public List<GearItem> Pool(string source, int rarity, int level, bool? avatar = null)
        {
            int floor = BandFloor(level);
            List<GearItem> pool = _items.FindAll(item => item.Rarity == rarity && item.MinimumLevel == floor && item.HasSource(source) &&
                                                        (avatar == null || item.IsAvatarGear == avatar.Value));
            pool.Sort((a, b) => string.CompareOrdinal(a.GearId, b.GearId));
            return pool;
        }

        /// <summary>A <see cref="GearSO"/> per piece of beast gear, in library order, created on first use and then shared.</summary>
        public IReadOnlyList<GearSO> BeastGearAssets
        {
            get
            {
                EnsureAssets();
                return _beastAssets;
            }
        }

        /// <summary>An <see cref="AvatarGearSO"/> per piece of avatar gear, in library order, created on first use and then shared.</summary>
        public IReadOnlyList<AvatarGearSO> AvatarGearAssets
        {
            get
            {
                EnsureAssets();
                return _avatarAssets;
            }
        }

        /// <summary>The shared <see cref="GearSO"/> of beast gear <paramref name="gearId"/>, or null.</summary>
        public GearSO BeastGearAsset(string gearId)
        {
            foreach (GearSO asset in BeastGearAssets)
            {
                if (string.Equals(asset.GearId, gearId, StringComparison.Ordinal))
                {
                    return asset;
                }
            }

            return null;
        }

        /// <summary>Writes <paramref name="item"/> onto <paramref name="asset"/> (the importer's and <see cref="BeastGearAssets"/>' shared mapping).</summary>
        public static void Apply(GearItem item, GearSO asset)
        {
            asset.name = item.GearId;
            asset.GearId = item.GearId;
            asset.DisplayName = item.DisplayName;
            asset.Description = item.Description;
            asset.Slot = (GearSlot)item.SlotIndex;
            asset.Rarity = item.Rarity;
            asset.MinimumLevel = item.MinimumLevel;
            asset.Modifiers = item.CopyModifiers();
        }

        /// <summary>Writes <paramref name="item"/> onto <paramref name="asset"/>.</summary>
        public static void Apply(GearItem item, AvatarGearSO asset)
        {
            asset.name = item.GearId;
            asset.AvatarGearId = item.GearId;
            asset.DisplayName = item.DisplayName;
            asset.Description = item.Description;
            asset.Slot = (AvatarGearSlot)item.SlotIndex;
            asset.Rarity = item.Rarity;
            asset.MinimumLevel = item.MinimumLevel;
            asset.Modifiers = item.CopyModifiers();
        }

        private void EnsureAssets()
        {
            lock (_assetLock)
            {
                if (_beastAssets != null)
                {
                    return;
                }

                List<GearSO> beast = new List<GearSO>();
                List<AvatarGearSO> avatar = new List<AvatarGearSO>();
                foreach (GearItem item in _items)
                {
                    if (item.IsAvatarGear)
                    {
                        AvatarGearSO asset = new AvatarGearSO();
                        Apply(item, asset);
                        avatar.Add(asset);
                    }
                    else
                    {
                        GearSO asset = new GearSO();
                        Apply(item, asset);
                        beast.Add(asset);
                    }
                }

                _avatarAssets = avatar;
                _beastAssets = beast;
            }
        }

        private void Add(GearItemData entry, bool avatar)
        {
            if (entry == null || string.IsNullOrEmpty(entry.GearId) || _byId.ContainsKey(entry.GearId))
            {
                return;
            }

            int slot;
            if (avatar)
            {
                if (!Enum.TryParse(entry.Slot, false, out AvatarGearSlot parsed) || !Enum.IsDefined(typeof(AvatarGearSlot), parsed))
                {
                    return;
                }

                slot = (int)parsed;
            }
            else
            {
                if (!Enum.TryParse(entry.Slot, false, out GearSlot parsed) || !Enum.IsDefined(typeof(GearSlot), parsed))
                {
                    return;
                }

                slot = (int)parsed;
            }

            List<StatModifier> modifiers = new List<StatModifier>();
            foreach (GearModifierData m in entry.Modifiers ?? new GearModifierData[0])
            {
                if (m != null && Enum.TryParse(m.Stat, false, out StatType stat) && Enum.IsDefined(typeof(StatType), stat))
                {
                    modifiers.Add(new StatModifier { Stat = stat, FlatBonus = m.Flat, PercentBonus = m.Pct });
                }
            }

            GearItem item = new GearItem(entry.GearId, entry.DisplayName, entry.Description, avatar, slot, entry.Rarity, Math.Max(1, entry.MinimumLevel),
                                         entry.Archetype ?? string.Empty, modifiers, entry.Sources ?? new string[0]);
            _items.Add(item);
            _byId.Add(item.GearId, item);
            if (!_bandFloors.Contains(item.MinimumLevel))
            {
                _bandFloors.Add(item.MinimumLevel);
            }
        }
    }

    /// <summary>One piece of the <see cref="GearLibrary"/>.</summary>
    public sealed class GearItem
    {
        private readonly List<StatModifier> _modifiers;
        private readonly string[] _sources;

        internal GearItem(string gearId, string displayName, string description, bool avatar, int slotIndex, int rarity, int minimumLevel, string archetype,
                          List<StatModifier> modifiers, string[] sources)
        {
            GearId = gearId;
            DisplayName = displayName ?? gearId;
            Description = description ?? string.Empty;
            IsAvatarGear = avatar;
            SlotIndex = slotIndex;
            Rarity = rarity;
            MinimumLevel = minimumLevel;
            Archetype = archetype;
            _modifiers = modifiers;
            _sources = sources;
        }

        public string GearId { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// <summary>Avatar gear (an <c>AvatarGearSO</c>) rather than beast gear (a <c>GearSO</c>).</summary>
        public bool IsAvatarGear { get; }

        /// <summary>The slot as its enum value: <c>(int)GearSlot</c> for beast gear, <c>(int)AvatarGearSlot</c> for avatar gear.</summary>
        public int SlotIndex { get; }

        public int Rarity { get; }

        public int MinimumLevel { get; }

        public string Archetype { get; }

        /// <summary>The stat changes, as authored. Read only: <see cref="CopyModifiers"/> for a list to hand an asset.</summary>
        public IReadOnlyList<StatModifier> Modifiers
        {
            get { return _modifiers; }
        }

        /// <summary>Whether it is tagged <paramref name="source"/> (<see cref="GearLibrary.SourceShop"/>, <see cref="GearLibrary.SourceDrop"/>, <see cref="GearLibrary.SourceBoss"/>).</summary>
        public bool HasSource(string source)
        {
            return Array.IndexOf(_sources, source) >= 0;
        }

        /// <summary>Fresh copies of <see cref="Modifiers"/>.</summary>
        public List<StatModifier> CopyModifiers()
        {
            List<StatModifier> copy = new List<StatModifier>();
            foreach (StatModifier m in _modifiers)
            {
                copy.Add(new StatModifier { Stat = m.Stat, FlatBonus = m.FlatBonus, PercentBonus = m.PercentBonus });
            }

            return copy;
        }
    }
}
