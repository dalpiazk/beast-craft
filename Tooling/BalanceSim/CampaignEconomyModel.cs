using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The economy inside <c>--mode campaign</c> (<see cref="CampaignPacingSimulator"/>): gold from
    /// every clear (the drop tables' <c>Gold</c> with the campaign's reward modifiers), gear and
    /// cosmetic drops, pass and lair rewards, the Trader at every trading post the route takes and
    /// the travelling trader at every camp (the game's <see cref="ShopService"/> and frozen stock),
    /// and a greedy shopper. Every draw
    /// comes from its own <see cref="Random"/> (<c>DeriveSeed(campaign seed, </c><see cref="Stream"/><c>)</c>),
    /// so the economy never moves the battle, XP or material draws; purchases do not feed back into
    /// the clear-chance model either (the difficulty assumes typical gear — the lead's decision —
    /// so gear is measured against that profile, not added to it).
    /// <para>
    /// <strong>The shopper</strong>, at each trading post, wants in priority order: the focus
    /// skill's gate material when it waits at a gate without one; the best gear upgrade on offer
    /// (a piece of a newer band, or a higher rarity, than the weakest the team wears in that slot);
    /// consumables up to two held; one skill (a tome for a fielded beast, up to ten levels early, or
    /// an avatar skill); one look. It buys them in that order while the gold lasts (a look only
    /// while <see cref="LookReserveUnits"/> price units stay in the purse), sells the gear an
    /// upgrade replaces, and uses one consumable at every den, pass and lair battle.
    /// </para>
    /// </summary>
    public sealed class CampaignEconomyModel
    {
        /// <summary>The <c>LootRoller.DeriveSeed(campaign seed, …)</c> stream every economy draw comes from.</summary>
        public const int Stream = 7919;

        /// <summary>Consumables the shopper keeps in the pack.</summary>
        public const int ConsumablesWanted = 2;

        /// <summary>The shopper buys a look only while this many price units stay in the purse afterwards (looks are the gold sink, never at the essentials' cost).</summary>
        public const int LookReserveUnits = 2;

        /// <summary>The fielded species (the model's team; any species works, the kits only matter for tomes).</summary>
        public static readonly string[] FieldedSpecies = { "griffin", "phoenix", "golem" };

        /// <summary>The benched species.</summary>
        public static readonly string[] BenchSpecies = { "kirin", "treant", "tarasque" };

        /// <summary>The recruit's species.</summary>
        public const string RecruitSpecies = "basilisk";

        // Economy gates.
        public const double AffordabilityMin = 0.55;
        public const double AffordabilityMax = 0.80;
        public const double NothingAffordableMax = 0.05;
        public const double HeldVisitsMax = 2.0;

        /// <summary>Design targets for gold earned (per region 1, 5, 10, and the whole campaign), reported, not gated.</summary>
        public static readonly double[] GoldTargets = { 900, 4400, 8800, 55000 };

        private readonly World _world;
        private readonly Random _rng;
        private readonly Result _result;
        private readonly GearItem[,] _beastGear;
        private readonly GearItem[] _avatarGear = new GearItem[3];

        public CampaignEconomyModel(World world, int campaignSeed, int regions)
        {
            _world = world;
            _rng = new Random(LootRoller.DeriveSeed(campaignSeed, Stream));
            _result = new Result(regions);
            _beastGear = new GearItem[CampaignPacingSimulator.FieldedCount, 3];
        }

        public Result Stats
        {
            get { return _result; }
        }

        /// <summary>The economy's fixed inputs.</summary>
        public sealed class World
        {
            public EconomyContent Content;
            public ShopService Shop;
            public Dictionary<string, List<LearnEntryData>> Kits = new Dictionary<string, List<LearnEntryData>>(StringComparer.Ordinal);
            public Dictionary<string, int> MaterialTiers = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Loads the gear, consumable, cosmetic and shop data (validated); null with <paramref name="errors"/> filled on failure.</summary>
            public static World Load(SkillLibraryData library, List<string> errors)
            {
                JsonSerializerOptions json = new JsonSerializerOptions { IncludeFields = true };
                GearLibraryData gear = Read<GearLibraryData>(GearLibraryData.ProjectRelativePath, json, errors);
                ConsumableLibraryData consumables = Read<ConsumableLibraryData>(ConsumableLibraryData.ProjectRelativePath, json, errors);
                CosmeticLibraryData cosmetics = Read<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath, json, errors);
                ShopTableData shop = Read<ShopTableData>(ShopTableData.ProjectRelativePath, json, errors);
                if (errors.Count > 0)
                {
                    return null;
                }

                errors.AddRange(GearLibraryValidator.Validate(gear));
                errors.AddRange(ConsumableLibraryValidator.Validate(consumables));
                errors.AddRange(CosmeticLibraryValidator.Validate(cosmetics));
                errors.AddRange(ShopTableValidator.Validate(shop, library, null));
                if (errors.Count > 0)
                {
                    return null;
                }

                World world = new World
                {
                    Content = new EconomyContent
                    {
                        Gear = GearLibrary.Build(gear),
                        Consumables = ConsumableLibrary.Build(consumables),
                        Cosmetics = CosmeticLibrary.Build(cosmetics)
                    }
                };
                world.Shop = new ShopService(shop, world.Content, library.SpeciesKits);
                foreach (SpeciesKitData kit in library.SpeciesKits ?? new SpeciesKitData[0])
                {
                    world.Kits[kit.SpeciesId] = new List<LearnEntryData>(kit.LearnableSkills ?? new LearnEntryData[0]);
                }

                foreach (SkillMaterialData material in library.Materials ?? new SkillMaterialData[0])
                {
                    world.MaterialTiers[material.MaterialId] = material.Tier;
                }

                return world;
            }

            private static T Read<T>(string projectRelativePath, JsonSerializerOptions json, List<string> errors) where T : class
            {
                string path = RosterLoader.ResolveFile(null, "BeastCraft/" + projectRelativePath);
                if (path == null || !File.Exists(path))
                {
                    errors.Add("Could not find BeastCraft/" + projectRelativePath + ".");
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), json);
                }
                catch (Exception exception)
                {
                    errors.Add("Could not read " + path + ": " + exception.Message);
                    return null;
                }
            }
        }

        /// <summary>What one campaign's economy measured.</summary>
        public sealed class Result
        {
            public Result(int regions)
            {
                GoldByRegion = new double[regions];
                VisitsByRegion = new int[regions];
                HeldAtRegionEnd = new double[regions];
                ConsumablesUsed = new int[regions];
                TypicalShare = new double[regions];
            }

            public double[] GoldByRegion;
            public int[] VisitsByRegion;
            public double[] HeldAtRegionEnd;
            public int[] ConsumablesUsed;

            /// <summary>At each boss: the share of the fielded beasts' slots whose gear is at least the typical profile's rarity of the band.</summary>
            public double[] TypicalShare;

            public List<double> Affordability = new List<double>();
            public int VisitsWithWants;
            public int NothingAffordable;
            public int GearDrops;
            public int GearFromBosses;
            public int GearSold;
            public double GoldFromSales;
            public Dictionary<string, int> Bought = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, int> Cosmetics = new Dictionary<string, int>(StringComparer.Ordinal);
            public int[] MaterialsBoughtByTier = new int[4];
        }

        /// <summary>A clear: gold (with the node's modifiers and the cell's first-clear bonus), gear drops and a cosmetic drop.</summary>
        public void OnClear(PlayerSave save, DropTable table, MapNode node, string dropShape, bool firstClear, int regionIndex)
        {
            RewardModifiers modifiers = CampaignRules.RewardModifiersFor(node);
            int gold = table.Gold.Roll(dropShape, node.Level, firstClear, modifiers.GoldMultiplier, modifiers.BonusGold, _rng);
            _result.GoldByRegion[regionIndex] += Wallet.Add(save, gold);

            foreach (GearItem item in GearDrops.Roll(table, _world.Content.Gear, dropShape, node.Level, _rng))
            {
                _result.GearDrops++;
                Equip(save, item);
            }

            CosmeticOption look = CosmeticRules.RollDrop(table, _world.Content.Cosmetics, save, dropShape, node.Level, _rng);
            if (look != null && CosmeticRules.Unlock(save, _world.Content.Cosmetics, look.Key))
            {
                Count(_result.Cosmetics, look.Source);
            }
        }

        /// <summary>Before a battle: a den, pass or lair gets one consumable from the pack (spent win or lose).</summary>
        public void BeforeBattle(PlayerSave save, MapNode node, int regionIndex)
        {
            if (node.Type == MapNodeType.Battle || save.Consumables.Count == 0)
            {
                return;
            }

            if (ConsumableInventory.TryRemove(save, save.Consumables[0].ConsumableId, 1))
            {
                _result.ConsumablesUsed[regionIndex]++;
            }
        }

        /// <summary>A pass's or lair's first-clear rewards (<see cref="CampaignResult.GearGranted"/>, looks).</summary>
        public void OnResolved(PlayerSave save, CampaignResult resolved)
        {
            if (!string.IsNullOrEmpty(resolved.GearGranted))
            {
                _result.GearFromBosses++;
                Equip(save, _world.Content.Gear.Get(resolved.GearGranted));
            }

            foreach (string key in resolved.CosmeticsUnlocked)
            {
                Count(_result.Cosmetics, _world.Content.Cosmetics.GetOption(key).Source);
            }
        }

        /// <summary>At a region's boss clear: gold held, the gear against the typical profile, milestone looks.</summary>
        public void OnRegionEnd(PlayerSave save, int regionIndex, int level)
        {
            _result.HeldAtRegionEnd[regionIndex] = Wallet.Balance(save);
            int[] typical = GearKits.Rarities(GearProfile.Typical, level);
            int floor = _world.Content.Gear.BandFloor(level);
            int ok = 0;
            for (int b = 0; b < CampaignPacingSimulator.FieldedCount; b++)
            {
                for (int s = 0; s < 3; s++)
                {
                    GearItem item = _beastGear[b, s];
                    ok += item != null && (item.MinimumLevel > floor || (item.MinimumLevel == floor && item.Rarity >= typical[s])) ? 1 : 0;
                }
            }

            _result.TypicalShare[regionIndex] = ok / (double)(CampaignPacingSimulator.FieldedCount * 3);
            foreach (string key in CosmeticRules.UnlockMilestones(save, _world.Content.Cosmetics))
            {
                Count(_result.Cosmetics, CosmeticLibrary.SourceMilestone);
            }
        }

        /// <summary>
        /// A Trader visit at <paramref name="context"/> (a trading post opened by the campaign's
        /// Trade, or the travelling trader at a camp): the stock is the game's frozen stock, then the
        /// shopper buys.
        /// </summary>
        public void Visit(PlayerSave save, ShopContext context, List<OwnedBeast> fielded, SkillProgress focus, SkillProgressionDefinition definition, int regionIndex)
        {
            _result.VisitsByRegion[regionIndex]++;
            LearnByLevel(fielded);
            ShopVisit stock = _world.Shop.GetStock(save, context);
            List<int> wants = Wants(save, stock, fielded, focus, definition, out List<string> targets, out int meaningful);
            if (wants.Count == 0)
            {
                return;
            }

            double wanted = 0.0;
            double spent = 0.0;
            for (int w = 0; w < wants.Count; w++)
            {
                wanted += stock.Listings[wants[w]].Price;
            }

            // "Nothing meaningful affordable": on arrival, no material, gear, consumable or skill in stock is within reach.
            bool anyMeaningful = false;
            foreach (ShopListing listing in stock.Listings)
            {
                anyMeaningful |= listing.Remaining > 0 && listing.Category != ShopCategory.Cosmetic && Wallet.CanAfford(save, listing.Price);
            }

            _result.VisitsWithWants++;
            _result.NothingAffordable += anyMeaningful ? 0 : 1;

            for (int w = 0; w < wants.Count; w++)
            {
                ShopListing listing = stock.Listings[wants[w]];
                if (w >= meaningful && Wallet.Balance(save) - listing.Price < LookReserveUnits * _world.Shop.PriceUnit(context.Level))
                {
                    continue;
                }

                ShopPurchaseResult bought = _world.Shop.TryBuy(save, context, wants[w], targets[w]);
                if (!bought.Success)
                {
                    continue;
                }

                spent += bought.Price;
                Count(_result.Bought, listing.Category.ToString());
                if (listing.Category == ShopCategory.Material && _world.MaterialTiers.TryGetValue(listing.ItemId, out int tier) && tier < _result.MaterialsBoughtByTier.Length)
                {
                    _result.MaterialsBoughtByTier[tier]++;
                }

                if (listing.Category == ShopCategory.BeastGear || listing.Category == ShopCategory.AvatarGear)
                {
                    GearItem item = _world.Content.Gear.Get(listing.ItemId);
                    RemoveInstance(save, bought.GrantedInstanceId);
                    Equip(save, item);
                }

                if (listing.Category == ShopCategory.Cosmetic)
                {
                    Count(_result.Cosmetics, CosmeticLibrary.SourceShop);
                }
            }

            _result.Affordability.Add(wanted <= 0.0 ? 1.0 : spent / wanted);
        }

        /// <summary>The want-list (listing indices, in buying order) and each one's tome target; <paramref name="meaningful"/> = how many come before the look.</summary>
        private List<int> Wants(PlayerSave save, ShopVisit stock, List<OwnedBeast> fielded, SkillProgress focus, SkillProgressionDefinition definition, out List<string> targets,
                                out int meaningful)
        {
            List<int> wants = new List<int>();
            targets = new List<string>();
            List<ShopListing> listings = stock.Listings;

            // 1. The focus skill's gate material.
            if (SkillProgression.IsAwaitingBreakthrough(focus, definition))
            {
                int required = definition.GetTier(focus.Tier).RequiredMaterialTier;
                bool held = false;
                foreach (KeyValuePair<string, int> material in _world.MaterialTiers)
                {
                    held |= material.Value >= required && save.Materials.GetCount(material.Key) > 0;
                }

                int pick = held ? -1 : Cheapest(listings, l => l.Category == ShopCategory.Material && _world.MaterialTiers.TryGetValue(l.ItemId, out int t) && t >= required);
                Add(wants, targets, pick, null);
            }

            // 2. The best gear upgrade.
            int best = -1;
            int bestGain = 0;
            for (int i = 0; i < listings.Count; i++)
            {
                if (listings[i].Remaining <= 0 || (listings[i].Category != ShopCategory.BeastGear && listings[i].Category != ShopCategory.AvatarGear))
                {
                    continue;
                }

                GearItem item = _world.Content.Gear.Get(listings[i].ItemId);
                int gain = item == null ? 0 : Value(item) - Value(Weakest(item));
                if (gain > bestGain)
                {
                    best = i;
                    bestGain = gain;
                }
            }

            Add(wants, targets, best, null);

            // 3. Consumables up to two held.
            int held2 = 0;
            foreach (ConsumableStack stack in save.Consumables)
            {
                held2 += stack.Quantity;
            }

            List<int> consumables = new List<int>();
            for (int i = 0; i < listings.Count; i++)
            {
                if (listings[i].Category == ShopCategory.Consumable && listings[i].Remaining > 0)
                {
                    consumables.Add(i);
                }
            }

            consumables.Sort((a, b) => listings[a].Price != listings[b].Price ? listings[a].Price.CompareTo(listings[b].Price) : a.CompareTo(b));
            for (int k = 0; k < consumables.Count && held2 + k < ConsumablesWanted; k++)
            {
                Add(wants, targets, consumables[k], null);
            }

            // 4. One skill: a tome for a fielded beast, or an avatar skill.
            int skill = -1;
            string target = null;
            for (int i = 0; i < listings.Count && skill < 0; i++)
            {
                ShopListing l = listings[i];
                if (l.Remaining <= 0)
                {
                    continue;
                }

                if (l.Category == ShopCategory.AvatarSkill || l.Category == ShopCategory.AvatarPassive)
                {
                    skill = i;
                }
                else if (l.Category == ShopCategory.BeastSkill)
                {
                    foreach (OwnedBeast beast in fielded)
                    {
                        LearnEntryData entry = _world.Kits.TryGetValue(beast.Progress.SpeciesId, out List<LearnEntryData> kit) ? kit.Find(e => e.SkillId == l.ItemId) : null;
                        if (entry != null && !beast.Skills.Knows(l.ItemId) && entry.Level <= beast.Progress.Level + _world.Shop.Table.Skills.EarlyAccessLevels)
                        {
                            skill = i;
                            target = beast.BeastId;
                            break;
                        }
                    }
                }
            }

            Add(wants, targets, skill, target);
            meaningful = wants.Count;

            // 5. One look (the gold sink).
            Add(wants, targets, Cheapest(listings, l => l.Category == ShopCategory.Cosmetic), null);
            return wants;
        }

        private static void Add(List<int> wants, List<string> targets, int index, string target)
        {
            if (index >= 0 && !wants.Contains(index))
            {
                wants.Add(index);
                targets.Add(target);
            }
        }

        private static int Cheapest(List<ShopListing> listings, Predicate<ShopListing> match)
        {
            int best = -1;
            for (int i = 0; i < listings.Count; i++)
            {
                if (listings[i].Remaining > 0 && match(listings[i]) && (best < 0 || listings[i].Price < listings[best].Price))
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>A piece's standing: its band first, then its rarity (a newer band's common beats an older rare).</summary>
        private int Value(GearItem item)
        {
            if (item == null)
            {
                return 0;
            }

            int band = 0;
            IReadOnlyList<int> floors = _world.Content.Gear.BandFloors;
            for (int i = 0; i < floors.Count; i++)
            {
                band = floors[i] == item.MinimumLevel ? i : band;
            }

            return 1 + (band * 3) + item.Rarity;
        }

        /// <summary>The weakest piece the team wears in <paramref name="like"/>'s slot (null = an empty slot).</summary>
        private GearItem Weakest(GearItem like)
        {
            if (like.IsAvatarGear)
            {
                return _avatarGear[like.SlotIndex];
            }

            GearItem weakest = _beastGear[0, like.SlotIndex];
            for (int b = 1; b < CampaignPacingSimulator.FieldedCount; b++)
            {
                if (Value(_beastGear[b, like.SlotIndex]) < Value(weakest))
                {
                    weakest = _beastGear[b, like.SlotIndex];
                }
            }

            return weakest;
        }

        /// <summary>Wears <paramref name="item"/> in place of the weakest piece of its slot when it is better; the replaced piece is sold.</summary>
        private void Equip(PlayerSave save, GearItem item)
        {
            if (item == null)
            {
                return;
            }

            GearItem weakest = Weakest(item);
            if (Value(item) <= Value(weakest))
            {
                _result.GoldFromSales += Wallet.Add(save, _world.Shop.SellPrice(item));
                _result.GearSold++;
                return;
            }

            if (item.IsAvatarGear)
            {
                _avatarGear[item.SlotIndex] = item;
            }
            else
            {
                for (int b = 0; b < CampaignPacingSimulator.FieldedCount; b++)
                {
                    if (_beastGear[b, item.SlotIndex] == weakest)
                    {
                        _beastGear[b, item.SlotIndex] = item;
                        break;
                    }
                }
            }

            if (weakest != null)
            {
                _result.GoldFromSales += Wallet.Add(save, _world.Shop.SellPrice(weakest));
                _result.GearSold++;
            }
        }

        /// <summary>The model tracks worn gear itself; a bought instance is dropped from the save so the inventory stays small.</summary>
        private static void RemoveInstance(PlayerSave save, string instanceId)
        {
            OwnedGear gear = save.Gear.FindBeastGear(instanceId);
            if (gear != null)
            {
                save.Gear.BeastGear.Remove(gear);
                return;
            }

            gear = save.Gear.FindAvatarGear(instanceId);
            if (gear != null)
            {
                save.Gear.AvatarGear.Remove(gear);
            }
        }

        /// <summary>The fielded beasts learn every kit skill up to their level (the game's level-up learning), so tomes are only the early access.</summary>
        private void LearnByLevel(List<OwnedBeast> fielded)
        {
            foreach (OwnedBeast beast in fielded)
            {
                if (_world.Kits.TryGetValue(beast.Progress.SpeciesId, out List<LearnEntryData> kit))
                {
                    foreach (LearnEntryData entry in kit)
                    {
                        if (entry.Level <= beast.Progress.Level)
                        {
                            beast.Skills.Learn(entry.SkillId);
                        }
                    }
                }
            }
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        /// <summary>The report's economy section and its gates.</summary>
        public static void Report(StringBuilder sb, RegionLibrary regions, List<CampaignPacingSimulator.Result> runs, Func<List<double>, double, double> p, List<string> misses)
        {
            sb.Append("## Economy\n\n");
            sb.Append("Gold from every clear (`drop-tables.json` `Gold`: 10 + 2 x level, x the shape (solo 1.2, elite 1.5, horde 1.1), +/-10%, +5 on a\n");
            sb.Append("cell's first clear; dens x").Append(SimOptions.Format(CampaignRules.EliteGoldMultiplier)).Append(", passes +").Append(CampaignRules.GateBonusGold)
              .Append(", lairs +").Append(CampaignRules.BossBonusGold).Append("), gear and look drops, pass and lair first-clear rewards, and the Trader\n");
            sb.Append("(`shop-tables.json`, the game's `ShopService`) at every trading post the route takes and at every camp (its travelling trader;\n");
            sb.Append("every path crosses the camp row). The shopper wants, in order: the focus skill's gate material when it waits without one, the\n");
            sb.Append("best gear upgrade, consumables up to ").Append(ConsumablesWanted).Append(" held, one skill (a tome up to 10 levels early, or an avatar skill), one look;\n");
            sb.Append("it buys in that order while the gold lasts (a look only while ").Append(LookReserveUnits)
              .Append(" price units stay in the purse), sells replaced gear (25%), and uses a\n");
            sb.Append("consumable at every den, pass and lair battle. Purchases do not change the clear chance (the difficulty assumes typical gear).\n\n");

            sb.Append("| Region | Gold earned p50 | Visits (mean) | Battles per visit | Gold held at the boss p50 | Held / visit income p50 | Gear at typical (mean) | Consumables used (mean) | Verdict |\n");
            sb.Append("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |\n");
            for (int r = 0; r < regions.Regions.Count; r++)
            {
                int region = r;
                List<double> gold = runs.ConvertAll(run => run.Econ.GoldByRegion[region]);
                List<double> ratio = runs.ConvertAll(run => run.Econ.VisitsByRegion[region] == 0 || run.Econ.GoldByRegion[region] <= 0.0
                                                              ? 0.0
                                                              : run.Econ.HeldAtRegionEnd[region] / (run.Econ.GoldByRegion[region] / run.Econ.VisitsByRegion[region]));
                double visits = Mean(runs.ConvertAll(run => (double)run.Econ.VisitsByRegion[region]));
                double battles = Mean(runs.ConvertAll(run => (double)run.BattlesByRegion[region]));
                double held = p(ratio, 50);
                bool ok = held < HeldVisitsMax;
                if (!ok)
                {
                    misses.Add(regions.Regions[r].RegionId + ": gold held at the boss is " + SimOptions.Format(held) + " visits' income (p50), at least " + SimOptions.Format(HeldVisitsMax) + ".");
                }

                sb.Append("| ").Append(regions.Regions[r].RegionId).Append(" | ").Append(Int(p(gold, 50))).Append(" | ").Append(SimOptions.Format(visits)).Append(" | ")
                  .Append(visits <= 0.0 ? "-" : SimOptions.Format(battles / visits)).Append(" | ").Append(Int(p(runs.ConvertAll(run => run.Econ.HeldAtRegionEnd[region]), 50)))
                  .Append(" | ").Append(SimOptions.Format(held)).Append(" | ").Append(Pct(100.0 * Mean(runs.ConvertAll(run => run.Econ.TypicalShare[region]))))
                  .Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)run.Econ.ConsumablesUsed[region])))).Append(" | ").Append(ok ? "ok" : "**MISS**").Append(" |\n");
            }

            List<double> total = runs.ConvertAll(run => Sum(run.Econ.GoldByRegion));
            sb.Append("| **Campaign** | ").Append(Int(p(total, 50))).Append(" | ").Append(SimOptions.Format(Mean(runs.ConvertAll(run => (double)Sum(run.Econ.VisitsByRegion)))))
              .Append(" | | | | | | |\n\n");
            sb.Append("Design reference (not a gate): about ").Append(Int(GoldTargets[0])).Append(" gold in region 1, ").Append(Int(GoldTargets[1])).Append(" in region 5, ")
              .Append(Int(GoldTargets[2])).Append(" in region 10, ").Append(Int(GoldTargets[3])).Append(" over the campaign. Gold held target: under ")
              .Append(SimOptions.Format(HeldVisitsMax)).Append(" visits' income at every boss (p50). \"Gear at typical\" = the share of the fielded beasts' slots at or\n");
            sb.Append("above the typical profile (`--gear typical`) for the boss's band.\n\n");

            List<double> afford = new List<double>();
            int withWants = 0;
            int nothing = 0;
            foreach (CampaignPacingSimulator.Result run in runs)
            {
                afford.AddRange(run.Econ.Affordability);
                withWants += run.Econ.VisitsWithWants;
                nothing += run.Econ.NothingAffordable;
            }

            double a50 = p(afford, 50);
            double nothingShare = withWants == 0 ? 0.0 : nothing / (double)withWants;
            bool affordOk = a50 >= AffordabilityMin && a50 <= AffordabilityMax;
            bool nothingOk = nothingShare < NothingAffordableMax;
            if (!affordOk)
            {
                misses.Add("Want-list affordability p50 " + Pct(100.0 * a50) + " is outside " + Pct(100.0 * AffordabilityMin) + "-" + Pct(100.0 * AffordabilityMax) + ".");
            }

            if (!nothingOk)
            {
                misses.Add("Visits with nothing meaningful affordable " + Pct(100.0 * nothingShare) + " (at least " + Pct(100.0 * NothingAffordableMax) + ").");
            }

            sb.Append("| Gate | Target | Result | Verdict |\n| --- | --- | --- | --- |\n");
            sb.Append("| Want-list affordability (gold spent / wanted, per visit) | p50 ").Append(Pct(100.0 * AffordabilityMin)).Append('-').Append(Pct(100.0 * AffordabilityMax))
              .Append(" | p10 ").Append(Pct(100.0 * p(afford, 10))).Append(", p50 ").Append(Pct(100.0 * a50)).Append(", p90 ").Append(Pct(100.0 * p(afford, 90)))
              .Append(" | ").Append(affordOk ? "ok" : "**MISS**").Append(" |\n");
            sb.Append("| Visits where nothing meaningful in stock (material, gear, consumable, skill) is affordable on arrival | under ").Append(Pct(100.0 * NothingAffordableMax))
              .Append(" | ").Append(Pct(100.0 * nothingShare)).Append(" of ").Append(withWants).Append(" | ").Append(nothingOk ? "ok" : "**MISS**").Append(" |\n\n");

            Dictionary<string, double> bought = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, double> looks = new Dictionary<string, double>(StringComparer.Ordinal);
            double[] tiers = new double[4];
            double drops = 0.0;
            double bosses = 0.0;
            double sold = 0.0;
            double sales = 0.0;
            foreach (CampaignPacingSimulator.Result run in runs)
            {
                foreach (KeyValuePair<string, int> e in run.Econ.Bought)
                {
                    bought[e.Key] = (bought.TryGetValue(e.Key, out double n) ? n : 0.0) + e.Value;
                }

                foreach (KeyValuePair<string, int> e in run.Econ.Cosmetics)
                {
                    looks[e.Key] = (looks.TryGetValue(e.Key, out double n) ? n : 0.0) + e.Value;
                }

                for (int t = 0; t < tiers.Length; t++)
                {
                    tiers[t] += run.Econ.MaterialsBoughtByTier[t];
                }

                drops += run.Econ.GearDrops;
                bosses += run.Econ.GearFromBosses;
                sold += run.Econ.GearSold;
                sales += run.Econ.GoldFromSales;
            }

            sb.Append("Per campaign (means): bought ");
            List<string> keys = new List<string>(bought.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(i == 0 ? string.Empty : ", ").Append(keys[i]).Append(' ').Append(SimOptions.Format(bought[keys[i]] / runs.Count));
            }

            sb.Append(" (materials by tier: ").Append(SimOptions.Format(tiers[1] / runs.Count)).Append(" / ").Append(SimOptions.Format(tiers[2] / runs.Count)).Append(" / ")
              .Append(SimOptions.Format(tiers[3] / runs.Count)).Append("); gear dropped ").Append(SimOptions.Format(drops / runs.Count)).Append(", from passes and lairs ")
              .Append(SimOptions.Format(bosses / runs.Count)).Append(", sold back ").Append(SimOptions.Format(sold / runs.Count)).Append(" for ")
              .Append(Int(sales / runs.Count)).Append(" gold; looks unlocked by source ");
            keys = new List<string>(looks.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(i == 0 ? string.Empty : ", ").Append(keys[i]).Append(' ').Append(SimOptions.Format(looks[keys[i]] / runs.Count));
            }

            sb.Append(".\n\n");
        }

        private static double Mean(List<double> values)
        {
            double sum = 0.0;
            foreach (double v in values)
            {
                sum += v;
            }

            return values.Count == 0 ? 0.0 : sum / values.Count;
        }

        private static double Sum(double[] values)
        {
            double sum = 0.0;
            foreach (double v in values)
            {
                sum += v;
            }

            return sum;
        }

        private static int Sum(int[] values)
        {
            int sum = 0;
            foreach (int v in values)
            {
                sum += v;
            }

            return sum;
        }

        private static string Int(double value)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Pct(double value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
