using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Economy
{
    /// <summary>What a <see cref="ShopService"/> call did.</summary>
    public enum ShopOutcome
    {
        Bought,
        Sold,

        /// <summary>No stock at this trading post, or no such listing.</summary>
        UnknownListing,

        SoldOut,
        NotEnoughGold,

        /// <summary>The pack already holds the most of that consumable.</summary>
        StackFull,

        /// <summary>A skill tome needs a beast of a species that learns it, within its early-access window.</summary>
        IneligibleTarget,

        /// <summary>The beast or the avatar already knows the skill.</summary>
        AlreadyKnown,

        /// <summary>The avatar is below the skill's level.</summary>
        LevelTooLow,

        /// <summary>The look is already unlocked (or free).</summary>
        AlreadyUnlocked,

        /// <summary>The item (or gear instance) is not known.</summary>
        UnknownItem,

        /// <summary>A worn gear instance cannot be sold (unequip it first).</summary>
        Equipped
    }

    /// <summary>What <see cref="ShopService.TryBuy"/> did. On anything but <see cref="ShopOutcome.Bought"/> nothing changed.</summary>
    public sealed class ShopPurchaseResult
    {
        internal ShopPurchaseResult(ShopOutcome outcome, ShopListing listing, int price, string instanceId)
        {
            Outcome = outcome;
            Listing = listing;
            Price = price;
            GrantedInstanceId = instanceId;
        }

        public ShopOutcome Outcome { get; }

        public bool Success
        {
            get { return Outcome == ShopOutcome.Bought; }
        }

        /// <summary>The listing (null when unknown).</summary>
        public ShopListing Listing { get; }

        /// <summary>Gold spent (0 unless bought).</summary>
        public int Price { get; }

        /// <summary>The new gear instance, for gear; null otherwise.</summary>
        public string GrantedInstanceId { get; }
    }

    /// <summary>What <see cref="ShopService.TrySellGear"/> did. On anything but <see cref="ShopOutcome.Sold"/> nothing changed.</summary>
    public sealed class ShopSaleResult
    {
        internal ShopSaleResult(ShopOutcome outcome, string gearId, int gold)
        {
            Outcome = outcome;
            GearId = gearId;
            Gold = gold;
        }

        public ShopOutcome Outcome { get; }

        public bool Success
        {
            get { return Outcome == ShopOutcome.Sold; }
        }

        public string GearId { get; }

        /// <summary>Gold received (0 unless sold).</summary>
        public int Gold { get; }
    }

    /// <summary>
    /// The Trader: the <see cref="IShopService"/> a map's trading post opens. Stock is rolled once
    /// per trading post (<see cref="ShopContext.NodeKey"/>) and frozen into the save
    /// (<see cref="PlayerSave.Shops"/>, the most recent <see cref="MaxRememberedShops"/>), so leaving
    /// and returning, or reloading, never re-rolls it. See the economy design doc, "The Trader".
    /// <para>
    /// <strong>Stock</strong> (<see cref="Roll"/>): one <see cref="Random"/> seeded
    /// <c>LootRoller.DeriveSeed(context.Seed, </c><see cref="StockStream"/><c>)</c>, categories in the
    /// fixed order Material, Gear, Skill, Consumable, Cosmetic: a listing count uniform in the band's
    /// [Min, Max] (always drawn), then that many weighted draws without replacement from the
    /// category's pool (sorted by id, so authored order never matters), each listing's stock
    /// quantity drawn right after it. Pools: materials sold at the Trader's level; shop gear of
    /// rarity common or rare whose band is the Trader's or the one before; skill tomes for species
    /// the player owns (a skill some beast of it does not know and reaches within the early-access
    /// window) and avatar skills the avatar is level enough for and does not know; consumables of
    /// the Trader's region; shop looks of the region not yet usable.
    /// </para>
    /// <para>
    /// <strong>Prices</strong> are price units x one unit at a reference level (<c>Base + PerLevel x
    /// level</c>, the squad gold curve): materials and consumables at the Trader's level, gear at its
    /// MinimumLevel + 10, a tome at the skill's learn level, an avatar skill at its level, looks at
    /// the region's middle level. Gear sells back for <c>SellbackPct</c> of its price (gear only).
    /// </para>
    /// <para>
    /// Every call validates first and changes the save only on success. Non-throwing.
    /// </para>
    /// </summary>
    public sealed class ShopService : IShopService
    {
        /// <summary>How many Traders' frozen stock the save keeps (oldest dropped first).</summary>
        public const int MaxRememberedShops = 16;

        /// <summary>The <c>LootRoller.DeriveSeed(context.Seed, …)</c> stream the stock is rolled on.</summary>
        public const int StockStream = 6;

        private readonly Dictionary<string, List<LearnEntryData>> _kits = new Dictionary<string, List<LearnEntryData>>(StringComparer.Ordinal);

        /// <param name="table">The shop tables (required).</param>
        /// <param name="economy">The gear, consumable and cosmetic libraries (any may be null: that category stocks nothing).</param>
        /// <param name="kits">Each species' learnable skills (the skill library's <c>SpeciesKits</c>; null = no tomes).</param>
        public ShopService(ShopTableData table, EconomyContent economy, IEnumerable<SpeciesKitData> kits)
        {
            Table = table ?? new ShopTableData();
            Economy = economy ?? new EconomyContent();
            foreach (SpeciesKitData kit in kits ?? new SpeciesKitData[0])
            {
                if (kit != null && !string.IsNullOrEmpty(kit.SpeciesId) && !_kits.ContainsKey(kit.SpeciesId))
                {
                    List<LearnEntryData> entries = new List<LearnEntryData>();
                    foreach (LearnEntryData entry in kit.LearnableSkills ?? new LearnEntryData[0])
                    {
                        if (entry != null && !string.IsNullOrEmpty(entry.SkillId))
                        {
                            entries.Add(entry);
                        }
                    }

                    _kits.Add(kit.SpeciesId, entries);
                }
            }
        }

        public ShopTableData Table { get; }

        public EconomyContent Economy { get; }

        /// <summary>One price unit at <paramref name="level"/> (1-100).</summary>
        public int PriceUnit(int level)
        {
            PriceUnitData unit = Table.PriceUnit ?? new PriceUnitData();
            return unit.Base + (unit.PerLevel * Math.Max(1, Math.Min(100, level)));
        }

        /// <summary><paramref name="units"/> price units at <paramref name="level"/>, rounded, at least 1.</summary>
        public int Price(float units, int level)
        {
            return Math.Max(1, (int)Math.Round(units * PriceUnit(level), MidpointRounding.AwayFromZero));
        }

        /// <summary>What <paramref name="item"/> costs at a Trader (its rarity's units at MinimumLevel + 10).</summary>
        public int GearPrice(GearItem item)
        {
            ShopGearData gear = Table.Gear ?? new ShopGearData();
            float units = item.Rarity >= 2 ? gear.EpicUnits : item.Rarity == 1 ? gear.RareUnits : gear.CommonUnits;
            return Price(units, item.MinimumLevel + 10);
        }

        /// <summary>What a Trader pays for <paramref name="item"/>: <c>SellbackPct</c> of <see cref="GearPrice"/>, at least 1.</summary>
        public int SellPrice(GearItem item)
        {
            return Math.Max(1, GearPrice(item) * Math.Max(0, Table.SellbackPct) / 100);
        }

        public bool Open(PlayerSave save, ShopContext context)
        {
            ShopVisit visit = GetStock(save, context);
            return visit != null && visit.Listings.Count > 0;
        }

        /// <summary>
        /// The stock at <paramref name="context"/>'s trading post: the frozen visit when the save has
        /// one, otherwise a fresh <see cref="Roll"/> frozen into the save now. Null only for a null
        /// save or context.
        /// </summary>
        public ShopVisit GetStock(PlayerSave save, ShopContext context)
        {
            if (save == null || context == null)
            {
                return null;
            }

            ShopVisit visit = Find(save, context.NodeKey);
            if (visit != null)
            {
                return visit;
            }

            visit = new ShopVisit { NodeKey = context.NodeKey, Listings = Roll(save, context) };
            if (save.Shops == null)
            {
                save.Shops = new List<ShopVisit>();
            }

            save.Shops.Add(visit);
            while (save.Shops.Count > MaxRememberedShops)
            {
                save.Shops.RemoveAt(0);
            }

            return visit;
        }

        /// <summary>A fresh stock for <paramref name="context"/> (see the class remarks). Does not change the save.</summary>
        public List<ShopListing> Roll(PlayerSave save, ShopContext context)
        {
            List<ShopListing> listings = new List<ShopListing>();
            if (context == null)
            {
                return listings;
            }

            Random rng = new Random(LootRoller.DeriveSeed(context.Seed, StockStream));
            int level = Math.Max(1, Math.Min(100, context.Level));
            int region = CosmeticLibrary.RegionOfLevel(level);
            ShopBandData band = BandFor(level);
            foreach (string category in ShopTableValidator.Categories)
            {
                ShopCategoryCountData count = band == null ? null : Array.Find(band.Categories ?? new ShopCategoryCountData[0], c => c != null && c.Category == category);
                int min = count == null ? 0 : Math.Max(0, count.Min);
                int max = count == null ? 0 : Math.Max(min, count.Max);
                int wanted = min + rng.Next(max - min + 1);
                List<Candidate> pool = Pool(category, save, level, region);
                for (int i = 0; i < wanted && pool.Count > 0; i++)
                {
                    Candidate pick = Draw(pool, rng);
                    int quantity = pick.MaxQuantity <= 1 ? 1 : 1 + rng.Next(pick.MaxQuantity);
                    listings.Add(new ShopListing { Category = pick.Category, ItemId = pick.ItemId, Quantity = quantity, Remaining = quantity, Price = pick.Price });
                }
            }

            return listings;
        }

        /// <summary>
        /// Buys one of listing <paramref name="listingIndex"/> at <paramref name="context"/>'s trading
        /// post (a skill tome is taught to <paramref name="targetBeastId"/>). Validates everything
        /// first; changes the save only when bought (gold spent, the item granted, the listing's
        /// remaining stock down one).
        /// </summary>
        public ShopPurchaseResult TryBuy(PlayerSave save, ShopContext context, int listingIndex, string targetBeastId = null)
        {
            ShopVisit visit = save == null || context == null ? null : Find(save, context.NodeKey);
            ShopListing listing = visit == null || listingIndex < 0 || listingIndex >= visit.Listings.Count ? null : visit.Listings[listingIndex];
            if (listing == null)
            {
                return Fail(ShopOutcome.UnknownListing, null);
            }

            if (listing.Remaining <= 0)
            {
                return Fail(ShopOutcome.SoldOut, listing);
            }

            ShopOutcome check = CheckGrant(save, listing, targetBeastId);
            if (check != ShopOutcome.Bought)
            {
                return Fail(check, listing);
            }

            if (!Wallet.CanAfford(save, listing.Price))
            {
                return Fail(ShopOutcome.NotEnoughGold, listing);
            }

            Wallet.TrySpend(save, listing.Price);
            string instance = Grant(save, listing, targetBeastId);
            listing.Remaining--;
            return new ShopPurchaseResult(ShopOutcome.Bought, listing, listing.Price, instance);
        }

        /// <summary>Sells an unworn gear instance for <see cref="SellPrice"/>. Changes the save only when sold.</summary>
        public ShopSaleResult TrySellGear(PlayerSave save, string gearInstanceId)
        {
            OwnedGear beast = save == null || save.Gear == null ? null : save.Gear.FindBeastGear(gearInstanceId);
            OwnedGear avatar = beast != null || save == null || save.Gear == null ? null : save.Gear.FindAvatarGear(gearInstanceId);
            OwnedGear owned = beast ?? avatar;
            GearItem item = owned == null || Economy.Gear == null ? null : Economy.Gear.Get(owned.GearId);
            if (owned == null || item == null)
            {
                return new ShopSaleResult(ShopOutcome.UnknownItem, owned == null ? null : owned.GearId, 0);
            }

            bool worn = beast != null ? GearRules.FindBeastGearHolder(save, gearInstanceId) != null : Array.IndexOf(save.AvatarEquippedGear ?? new string[0], gearInstanceId) >= 0;
            if (worn)
            {
                return new ShopSaleResult(ShopOutcome.Equipped, owned.GearId, 0);
            }

            int gold = SellPrice(item);
            (beast != null ? save.Gear.BeastGear : save.Gear.AvatarGear).Remove(owned);
            Wallet.Add(save, gold);
            return new ShopSaleResult(ShopOutcome.Sold, owned.GearId, gold);
        }

        /// <summary>Whether <paramref name="listing"/> could be granted now (everything but the gold).</summary>
        private ShopOutcome CheckGrant(PlayerSave save, ShopListing listing, string targetBeastId)
        {
            switch (listing.Category)
            {
                case ShopCategory.Material:
                    return string.IsNullOrEmpty(listing.ItemId) ? ShopOutcome.UnknownItem : ShopOutcome.Bought;
                case ShopCategory.BeastGear:
                case ShopCategory.AvatarGear:
                    GearItem gear = Economy.Gear == null ? null : Economy.Gear.Get(listing.ItemId);
                    return gear == null || gear.IsAvatarGear != (listing.Category == ShopCategory.AvatarGear) ? ShopOutcome.UnknownItem : ShopOutcome.Bought;
                case ShopCategory.Consumable:
                    ConsumableSO consumable = Economy.Consumables == null ? null : Economy.Consumables.Get(listing.ItemId);
                    if (consumable == null)
                    {
                        return ShopOutcome.UnknownItem;
                    }

                    return ConsumableInventory.CanAdd(save, listing.ItemId, 1, consumable.MaxStack) ? ShopOutcome.Bought : ShopOutcome.StackFull;
                case ShopCategory.Cosmetic:
                    CosmeticOption look = Economy.Cosmetics == null ? null : Economy.Cosmetics.GetOption(listing.ItemId);
                    if (look == null)
                    {
                        return ShopOutcome.UnknownItem;
                    }

                    return CosmeticRules.IsUsable(save, look) ? ShopOutcome.AlreadyUnlocked : ShopOutcome.Bought;
                case ShopCategory.BeastSkill:
                    OwnedBeast beast = save.FindBeast(targetBeastId);
                    LearnEntryData entry = beast == null || beast.Progress == null ? null : EntryFor(beast.Progress.SpeciesId, listing.ItemId);
                    if (entry == null || entry.Level > beast.Progress.Level + EarlyAccess())
                    {
                        return ShopOutcome.IneligibleTarget;
                    }

                    return beast.Skills != null && beast.Skills.Knows(listing.ItemId) ? ShopOutcome.AlreadyKnown : ShopOutcome.Bought;
                case ShopCategory.AvatarSkill:
                case ShopCategory.AvatarPassive:
                    AvatarSkillUnlockData unlock = UnlockFor(listing.ItemId);
                    if (unlock == null)
                    {
                        return ShopOutcome.UnknownItem;
                    }

                    if (KnowsAvatarSkill(save, listing))
                    {
                        return ShopOutcome.AlreadyKnown;
                    }

                    return (save.Avatar == null ? 1 : save.Avatar.Level) < unlock.MinAvatarLevel ? ShopOutcome.LevelTooLow : ShopOutcome.Bought;
                default:
                    return ShopOutcome.UnknownItem;
            }
        }

        private string Grant(PlayerSave save, ShopListing listing, string targetBeastId)
        {
            switch (listing.Category)
            {
                case ShopCategory.Material:
                    save.Materials.Add(listing.ItemId, 1);
                    return null;
                case ShopCategory.BeastGear:
                case ShopCategory.AvatarGear:
                    return GearDrops.Grant(save, Economy.Gear.Get(listing.ItemId));
                case ShopCategory.Consumable:
                    ConsumableInventory.TryAdd(save, listing.ItemId, 1, Economy.Consumables.Get(listing.ItemId).MaxStack);
                    return null;
                case ShopCategory.Cosmetic:
                    CosmeticRules.Unlock(save, Economy.Cosmetics, listing.ItemId);
                    return null;
                case ShopCategory.BeastSkill:
                    save.FindBeast(targetBeastId).Skills.Learn(listing.ItemId);
                    return null;
                case ShopCategory.AvatarSkill:
                    save.AvatarSkills.Actives.Learn(listing.ItemId);
                    return null;
                default:
                    save.AvatarSkills.Passives.Learn(listing.ItemId);
                    return null;
            }
        }

        private static bool KnowsAvatarSkill(PlayerSave save, ShopListing listing)
        {
            if (save.AvatarSkills == null)
            {
                return false;
            }

            SkillBook book = listing.Category == ShopCategory.AvatarSkill ? (SkillBook)save.AvatarSkills.Actives : save.AvatarSkills.Passives;
            return book != null && book.Knows(listing.ItemId);
        }

        private sealed class Candidate
        {
            public ShopCategory Category;
            public string ItemId;
            public int Price;
            public int Weight = 1;
            public int MaxQuantity = 1;
        }

        private List<Candidate> Pool(string category, PlayerSave save, int level, int region)
        {
            List<Candidate> pool = new List<Candidate>();
            switch (category)
            {
                case "Material":
                    foreach (ShopMaterialData m in Table.Materials ?? new ShopMaterialData[0])
                    {
                        if (m != null && !string.IsNullOrEmpty(m.MaterialId) && m.MinShopLevel <= level)
                        {
                            pool.Add(new Candidate { Category = ShopCategory.Material, ItemId = m.MaterialId, Price = Price(m.PriceUnits, level), Weight = Math.Max(1, m.Weight), MaxQuantity = m.MaxQuantity });
                        }
                    }

                    break;
                case "Gear":
                    AddGear(pool, level);
                    break;
                case "Skill":
                    AddSkills(pool, save);
                    break;
                case "Consumable":
                    ShopConsumableData consumables = Table.Consumables ?? new ShopConsumableData();
                    foreach (ConsumableSO c in Economy.Consumables == null ? new ConsumableSO[0] : (IEnumerable<ConsumableSO>)Economy.Consumables.All)
                    {
                        if (c.MinRegion <= region)
                        {
                            pool.Add(new Candidate
                            {
                                Category = ShopCategory.Consumable,
                                ItemId = c.ConsumableId,
                                Price = Price(c.Rarity >= 1 ? consumables.RareUnits : consumables.CommonUnits, level),
                                MaxQuantity = Math.Min(consumables.MaxQuantity, c.MaxStack)
                            });
                        }
                    }

                    break;
                case "Cosmetic":
                    ShopCosmeticData cosmetics = Table.Cosmetics ?? new ShopCosmeticData();
                    foreach (CosmeticOption look in Economy.Cosmetics == null ? new List<CosmeticOption>() : Economy.Cosmetics.Pool(CosmeticLibrary.SourceShop, region))
                    {
                        if (!CosmeticRules.IsUsable(save, look))
                        {
                            pool.Add(new Candidate { Category = ShopCategory.Cosmetic, ItemId = look.Key, Price = Price(look.Rarity >= 1 ? cosmetics.RareUnits : cosmetics.CommonUnits, ((region - 1) * 10) + 5) });
                        }
                    }

                    break;
            }

            pool.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId));
            return pool;
        }

        private void AddGear(List<Candidate> pool, int level)
        {
            if (Economy.Gear == null)
            {
                return;
            }

            ShopGearData gear = Table.Gear ?? new ShopGearData();
            int floor = Economy.Gear.BandFloor(level) - gear.BandLookback;
            foreach (GearItem item in Economy.Gear.Items)
            {
                if (item.HasSource(GearLibrary.SourceShop) && item.Rarity <= 1 && item.MinimumLevel <= level && item.MinimumLevel >= floor)
                {
                    int weight = item.Rarity == 1 ? gear.RareWeight : gear.CommonWeight;
                    if (weight > 0)
                    {
                        pool.Add(new Candidate { Category = item.IsAvatarGear ? ShopCategory.AvatarGear : ShopCategory.BeastGear, ItemId = item.GearId, Price = GearPrice(item), Weight = weight });
                    }
                }
            }
        }

        private void AddSkills(List<Candidate> pool, PlayerSave save)
        {
            if (save == null)
            {
                return;
            }

            ShopSkillData skills = Table.Skills ?? new ShopSkillData();
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            foreach (OwnedBeast beast in save.Beasts ?? new List<OwnedBeast>())
            {
                if (beast == null || beast.Progress == null || !_kits.TryGetValue(beast.Progress.SpeciesId ?? string.Empty, out List<LearnEntryData> entries))
                {
                    continue;
                }

                foreach (LearnEntryData entry in entries)
                {
                    if (entry.Level <= beast.Progress.Level + EarlyAccess() && (beast.Skills == null || !beast.Skills.Knows(entry.SkillId)) && added.Add(entry.SkillId))
                    {
                        pool.Add(new Candidate { Category = ShopCategory.BeastSkill, ItemId = entry.SkillId, Price = Price(skills.BeastTomeUnits, entry.Level) });
                    }
                }
            }

            foreach (AvatarSkillUnlockData unlock in Table.AvatarSkillUnlocks ?? new AvatarSkillUnlockData[0])
            {
                if (unlock == null || string.IsNullOrEmpty(unlock.SkillId) || (save.Avatar == null ? 1 : save.Avatar.Level) < unlock.MinAvatarLevel)
                {
                    continue;
                }

                ShopListing probe = new ShopListing { Category = unlock.Kind == "Passive" ? ShopCategory.AvatarPassive : ShopCategory.AvatarSkill, ItemId = unlock.SkillId };
                if (!KnowsAvatarSkill(save, probe))
                {
                    pool.Add(new Candidate { Category = probe.Category, ItemId = unlock.SkillId, Price = Price(unlock.PriceUnits, unlock.MinAvatarLevel) });
                }
            }
        }

        private static Candidate Draw(List<Candidate> pool, Random rng)
        {
            int total = 0;
            foreach (Candidate c in pool)
            {
                total += c.Weight;
            }

            int roll = rng.Next(Math.Max(1, total));
            for (int i = 0; i < pool.Count; i++)
            {
                if (roll < pool[i].Weight)
                {
                    Candidate pick = pool[i];
                    pool.RemoveAt(i);
                    return pick;
                }

                roll -= pool[i].Weight;
            }

            Candidate last = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return last;
        }

        private ShopBandData BandFor(int level)
        {
            ShopBandData last = null;
            foreach (ShopBandData band in Table.Bands ?? new ShopBandData[0])
            {
                if (band == null)
                {
                    continue;
                }

                last = band;
                if (level >= band.MinLevel && level <= band.MaxLevel)
                {
                    return band;
                }
            }

            return last;
        }

        private LearnEntryData EntryFor(string speciesId, string skillId)
        {
            return speciesId != null && _kits.TryGetValue(speciesId, out List<LearnEntryData> entries) ? entries.Find(e => e.SkillId == skillId) : null;
        }

        private AvatarSkillUnlockData UnlockFor(string skillId)
        {
            return Array.Find(Table.AvatarSkillUnlocks ?? new AvatarSkillUnlockData[0], u => u != null && u.SkillId == skillId);
        }

        private int EarlyAccess()
        {
            return Table.Skills == null ? 0 : Math.Max(0, Table.Skills.EarlyAccessLevels);
        }

        private static ShopVisit Find(PlayerSave save, string nodeKey)
        {
            return save.Shops == null ? null : save.Shops.Find(v => v != null && string.Equals(v.NodeKey, nodeKey, StringComparison.Ordinal));
        }

        private static ShopPurchaseResult Fail(ShopOutcome outcome, ShopListing listing)
        {
            return new ShopPurchaseResult(outcome, listing, 0, null);
        }
    }
}
