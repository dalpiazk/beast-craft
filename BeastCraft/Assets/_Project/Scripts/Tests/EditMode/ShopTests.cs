using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Trader (<see cref="ShopService"/>) over the authored <c>shop-tables.json</c> and the
    /// economy libraries: tables validate; stock is stable per trading post and frozen after the
    /// first visit; prices; every purchase and sale outcome, a refused one leaving the save
    /// byte-identical; the skill-tome window; the campaign's Trade opening it.
    /// </summary>
    public class ShopTests
    {
        private ShopTableData _table;
        private SkillLibraryData _library;
        private EconomyContent _economy;
        private ShopService _shop;

        [SetUp]
        public void SetUp()
        {
            _table = LoadTables();
            _library = SkillLibraryTests.LoadLibrary();
            _economy = new EconomyContent
            {
                Gear = GearLibrary.Build(GearLibraryTests.LoadGear()),
                Consumables = ConsumableLibrary.Build(ConsumableTests.LoadConsumables()),
                Cosmetics = CosmeticLibrary.Build(CosmeticTests.LoadCosmetics())
            };
            _shop = new ShopService(_table, _economy, _library.SpeciesKits);
        }

        internal static ShopTableData LoadTables()
        {
            return EncounterContentTests.Load<ShopTableData>(ShopTableData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredTables_AreValid()
        {
            List<string> errors = ShopTableValidator.Validate(_table, _library, DropTableTests.LoadTables());
            Assert.IsEmpty(errors, string.Join("\n", errors));

            ShopTableData broken = LoadTables();
            broken.Bands[1].MinLevel = 22;
            broken.Materials[1].MinShopLevel = 5;
            broken.AvatarSkillUnlocks[0].SkillId = "nope";
            broken.Bands[0].Categories[0].Max = 9;
            string all = string.Join("\n", ShopTableValidator.Validate(broken, _library, DropTableTests.LoadTables()));
            StringAssert.Contains("contiguous", all);
            StringAssert.Contains("before its first-clear band", all);
            StringAssert.Contains("'nope'", all);
            StringAssert.Contains("Max <= 6", all);
        }

        [Test]
        public void Stock_IsStablePerTradingPost_AndFrozenAfterTheFirstVisit()
        {
            ShopContext here = new ShopContext("r02", 1, 7, 15, 424242);
            PlayerSave first = Save(15);
            PlayerSave second = Save(15);

            ShopVisit a = _shop.GetStock(first, here);
            ShopVisit b = _shop.GetStock(second, here);
            Assert.AreEqual(FieldJson.ToJson(a), FieldJson.ToJson(b), "same trading post, same stock");
            Assert.AreEqual(here.NodeKey, a.NodeKey);
            Assert.AreEqual("r02/1/7/424242", here.NodeKey);
            Assert.AreNotEqual(FieldJson.ToJson(a.Listings), FieldJson.ToJson(_shop.Roll(Save(15), new ShopContext("r02", 1, 7, 15, 99))));
            Assert.That(a.Listings.Count, Is.InRange(6, 13));
            Assert.IsTrue(a.Listings.TrueForAll(l => l.Price > 0 && l.Remaining == l.Quantity));

            first.Beasts.Add(OwnedBeast.Create("late", "phoenix", 15));
            first.Gold = 999;
            Assert.AreSame(a, _shop.GetStock(first, here), "frozen: a changed save never re-rolls a trading post");
            Assert.AreEqual(1, first.Shops.Count);
            Assert.IsTrue(_shop.Open(first, here));

            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer());
            SaveLoadResult reloaded = serializer.Deserialize(serializer.Serialize(first));
            Assert.AreEqual(FieldJson.ToJson(a), FieldJson.ToJson(_shop.GetStock(reloaded.Save, here)), "frozen across a reload");

            for (int i = 0; i < ShopService.MaxRememberedShops + 3; i++)
            {
                _shop.GetStock(first, new ShopContext("r02", 1, i, 15, 1000 + i));
            }

            Assert.AreEqual(ShopService.MaxRememberedShops, first.Shops.Count, "only the most recent Traders are kept");
        }

        [Test]
        public void Stock_FollowsTheBandsAndTheTiers()
        {
            List<ShopListing> low = _shop.Roll(Save(8), new ShopContext("r01", 0, 3, 8, 5));
            Assert.IsTrue(low.TrueForAll(l => l.Category != ShopCategory.Material || l.ItemId == "essence_shard"), "no crystal or core before their first-clear bands");
            Assert.IsTrue(low.FindAll(l => l.Category == ShopCategory.BeastGear || l.Category == ShopCategory.AvatarGear)
                             .TrueForAll(l => _economy.Gear.Get(l.ItemId).MinimumLevel == 1 && _economy.Gear.Get(l.ItemId).Rarity <= 1));
            Assert.IsTrue(low.FindAll(l => l.Category == ShopCategory.Consumable).TrueForAll(l => _economy.Consumables.Get(l.ItemId).MinRegion == 1));

            for (int seed = 0; seed < 30; seed++)
            {
                foreach (ShopListing l in _shop.Roll(Save(55), new ShopContext("r06", 2, 4, 55, seed)))
                {
                    if (l.Category == ShopCategory.BeastGear || l.Category == ShopCategory.AvatarGear)
                    {
                        GearItem item = _economy.Gear.Get(l.ItemId);
                        Assert.That(item.MinimumLevel, Is.InRange(21, 41), "the band or the one before");
                        Assert.Less(item.Rarity, 2, "never an epic");
                        Assert.AreEqual(_shop.GearPrice(item), l.Price);
                    }

                    if (l.Category == ShopCategory.Cosmetic)
                    {
                        Assert.AreEqual(CosmeticLibrary.SourceShop, _economy.Cosmetics.GetOption(l.ItemId).Source, "premium, boss, milestone and drop looks are never sold");
                    }
                }
            }
        }

        [Test]
        public void Prices_FollowTheIncomeCurve()
        {
            Assert.AreEqual(40, _shop.PriceUnit(15));
            Assert.AreEqual(210, _shop.PriceUnit(100));
            Assert.AreEqual(Round(_table.Gear.CommonUnits * (10 + (2 * 11))), _shop.GearPrice(_economy.Gear.Get("fang_t1")), "a common at its MinimumLevel + 10");
            int rare = Round(_table.Gear.RareUnits * (10 + (2 * 51)));
            Assert.AreEqual(rare, _shop.GearPrice(_economy.Gear.Get("fang_t3_rare")));
            Assert.AreEqual(rare * 25 / 100, _shop.SellPrice(_economy.Gear.Get("fang_t3_rare")));
            Assert.AreEqual(Round(_table.Gear.EpicUnits * (10 + (2 * 91))) * 25 / 100, _shop.SellPrice(_economy.Gear.Get("fang_t5_epic")), "epics sell back though never sold");
            Assert.Less(_shop.GearPrice(_economy.Gear.Get("fang_t1")), _shop.GearPrice(_economy.Gear.Get("fang_t1_rare")));
            Assert.AreEqual(1, _shop.Price(0.001f, 1), "never free");
        }

        private static int Round(double value)
        {
            return (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
        }

        [Test]
        public void TryBuy_GrantsEachKindOfItem_AndSpendsTheGold()
        {
            PlayerSave save = Save(20);
            save.Gold = 100000;
            ShopContext here = Stocked(save, new List<ShopListing>
            {
                Listing(ShopCategory.Material, "essence_shard", 2, 50),
                Listing(ShopCategory.BeastGear, "barding_t1", 1, 60),
                Listing(ShopCategory.AvatarGear, "avatar_coat_t1", 1, 60),
                Listing(ShopCategory.Consumable, "fury_draught", 2, 10),
                Listing(ShopCategory.Cosmetic, "griffin_crest/sunlit", 1, 300),
                Listing(ShopCategory.BeastSkill, "tailwind", 1, 250),
                Listing(ShopCategory.AvatarPassive, "iron_will", 1, 200),
                Listing(ShopCategory.AvatarSkill, "hex_of_frailty", 1, 200)
            });

            for (int i = 0; i < 8; i++)
            {
                ShopPurchaseResult bought = _shop.TryBuy(save, here, i, "g");
                Assert.AreEqual(ShopOutcome.Bought, bought.Outcome, "listing " + i);
            }

            Assert.AreEqual(100000 - (50 + 60 + 60 + 10 + 300 + 250 + 200 + 200), save.Gold);
            Assert.AreEqual(1, save.Materials.GetCount("essence_shard"));
            Assert.AreEqual(1, save.Gear.BeastGear.Count);
            Assert.AreEqual(1, save.Gear.AvatarGear.Count);
            Assert.AreEqual(1, ConsumableInventory.Quantity(save, "fury_draught"));
            Assert.IsTrue(save.Cosmetics.Has("griffin_crest/sunlit"));
            Assert.IsTrue(save.FindBeast("g").Skills.Knows("tailwind"));
            Assert.IsTrue(save.AvatarSkills.Passives.Knows("iron_will"));
            Assert.IsTrue(save.AvatarSkills.Actives.Knows("hex_of_frailty"));
            Assert.AreEqual(1, save.Shops[0].Listings[0].Remaining);
            Assert.AreEqual(ShopOutcome.Bought, _shop.TryBuy(save, here, 0).Outcome, "a stack of two sells twice");
        }

        [Test]
        public void TryBuy_EveryRefusal_LeavesTheSaveByteIdentical()
        {
            PlayerSave save = Save(20);
            save.Gold = 40;
            ConsumableInventory.TryAdd(save, "iron_tonic", 5, 5);
            save.Cosmetics.Unlock("griffin_crest/sunlit");
            save.FindBeast("g").Skills.Learn("gust");
            ShopContext here = Stocked(save, new List<ShopListing>
            {
                Listing(ShopCategory.Material, "essence_shard", 0, 10),
                Listing(ShopCategory.Material, "essence_shard", 1, 50),
                Listing(ShopCategory.Consumable, "iron_tonic", 1, 5),
                Listing(ShopCategory.Cosmetic, "griffin_crest/sunlit", 1, 5),
                Listing(ShopCategory.Cosmetic, "griffin_crest/swept", 1, 5),
                Listing(ShopCategory.BeastSkill, "gust", 1, 5),
                Listing(ShopCategory.BeastSkill, "sky_rend", 1, 5),
                Listing(ShopCategory.BeastSkill, "boulder_slam", 1, 5),
                Listing(ShopCategory.AvatarPassive, "verdant_pulse", 1, 5),
                Listing(ShopCategory.BeastGear, "no_such_gear", 1, 5)
            });

            AssertRefused(save, here, 0, "g", ShopOutcome.SoldOut);
            AssertRefused(save, here, 1, "g", ShopOutcome.NotEnoughGold);
            AssertRefused(save, here, 2, "g", ShopOutcome.StackFull);
            AssertRefused(save, here, 3, "g", ShopOutcome.AlreadyUnlocked);
            AssertRefused(save, here, 4, "g", ShopOutcome.AlreadyUnlocked, "a free look is never sold");
            AssertRefused(save, here, 5, "g", ShopOutcome.AlreadyKnown);
            AssertRefused(save, here, 6, "g", ShopOutcome.IneligibleTarget, "sky_rend is learned at 50: past the early-access window of a level-20 griffin");
            AssertRefused(save, here, 7, "g", ShopOutcome.IneligibleTarget, "own species only: a griffin cannot learn a golem skill");
            AssertRefused(save, here, 7, null, ShopOutcome.IneligibleTarget);
            AssertRefused(save, here, 8, "g", ShopOutcome.LevelTooLow);
            AssertRefused(save, here, 9, "g", ShopOutcome.UnknownItem);
            AssertRefused(save, here, 42, "g", ShopOutcome.UnknownListing);
            AssertRefused(save, new ShopContext("r09", 0, 0, 90, 1), 0, "g", ShopOutcome.UnknownListing, "never visited");
        }

        [Test]
        public void SkillTomes_OpenTenLevelsEarly()
        {
            PlayerSave save = Save(1);
            save.Gold = 100000;
            save.Beasts.Add(OwnedBeast.Create("golem", "golem", 1));
            ShopContext here = Stocked(save, new List<ShopListing> { Listing(ShopCategory.BeastSkill, "tectonic_shove", 1, 5) });

            AssertRefused(save, here, 0, "golem", ShopOutcome.IneligibleTarget, "learned at 12: a level-1 golem is 11 levels short");
            save.FindBeast("golem").Progress.Level = 2;
            Assert.AreEqual(ShopOutcome.Bought, _shop.TryBuy(save, here, 0, "golem").Outcome);
            Assert.IsTrue(save.FindBeast("golem").Skills.Knows("tectonic_shove"));
        }

        [Test]
        public void TrySellGear_PaysAQuarter_OnlyForUnwornGear()
        {
            PlayerSave save = Save(45);
            string worn = GearDrops.Grant(save, _economy.Gear.Get("fang_t3"));
            string spare = GearDrops.Grant(save, _economy.Gear.Get("fang_t3_rare"));
            string ring = GearDrops.Grant(save, _economy.Gear.Get("avatar_ring_t1"));
            BattleContent content = new BattleContent(null, null, null, null, _economy.Gear.BeastGearAssets, _economy.Gear.AvatarGearAssets);
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "g", GearSlot.WeaponOrCore, worn, content));
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipAvatarGear(save, AvatarGearSlot.Trinket, ring, content));

            string before = FieldJson.ToJson(save);
            Assert.AreEqual(ShopOutcome.Equipped, _shop.TrySellGear(save, worn).Outcome);
            Assert.AreEqual(ShopOutcome.Equipped, _shop.TrySellGear(save, ring).Outcome);
            Assert.AreEqual(ShopOutcome.UnknownItem, _shop.TrySellGear(save, "gear999").Outcome);
            Assert.AreEqual(before, FieldJson.ToJson(save), "a refused sale changes nothing");

            ShopSaleResult sold = _shop.TrySellGear(save, spare);
            Assert.AreEqual(ShopOutcome.Sold, sold.Outcome);
            Assert.AreEqual(_shop.GearPrice(_economy.Gear.Get("fang_t3_rare")) / 4, sold.Gold);
            Assert.AreEqual(sold.Gold, save.Gold);
            Assert.IsNull(save.Gear.FindBeastGear(spare));
        }

        [Test]
        public void CampaignTrade_OpensTheTrader_AndTheStubOffersNothing()
        {
            RegionLibrary regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
            PlayerSave save = Save(5);
            MapNode shopNode = null;
            for (int seed = 0; shopNode == null; seed++)
            {
                save.Campaign.ActiveRun.Clear();
                CampaignRules.StartRun(save, regions, "r01", 0, seed);
                shopNode = save.Campaign.ActiveRun.Nodes.Find(n => n.Type == MapNodeType.Shop && n.Layer == 1 &&
                                                                    save.Campaign.ActiveRun.Nodes.Exists(p => p.Layer == 0 && System.Array.IndexOf(p.Next, n.NodeId) >= 0));
            }

            MapNode entry = save.Campaign.ActiveRun.Nodes.Find(p => p.Layer == 0 && System.Array.IndexOf(p.Next, shopNode.NodeId) >= 0);
            Assert.IsTrue(CampaignRules.ResolveBattle(save, regions, entry.NodeId, BattleOutcome.PlayerVictory).Success);
            ShopContext context = CampaignRules.ShopContextFor(save.Campaign.ActiveRun, shopNode);
            CampaignResult traded = CampaignRules.Trade(save, regions, shopNode.NodeId, _shop);

            Assert.IsTrue(traded.ShopOpened);
            Assert.AreEqual(1, save.Shops.Count);
            Assert.AreEqual(context.NodeKey, save.Shops[0].NodeKey);
            Assert.IsNotNull(_shop.GetStock(save, context));
            Assert.IsFalse(new ShopServiceStub().Open(save, context));
            Assert.AreEqual(ShopOutcome.UnknownListing, new ShopServiceStub().TryBuy(save, context, 0).Outcome);
        }

        private void AssertRefused(PlayerSave save, ShopContext here, int index, string target, ShopOutcome expected, string because = null)
        {
            string before = FieldJson.ToJson(save);
            ShopPurchaseResult result = _shop.TryBuy(save, here, index, target);
            Assert.AreEqual(expected, result.Outcome, because);
            Assert.AreEqual(0, result.Price);
            Assert.AreEqual(before, FieldJson.ToJson(save), "a refused purchase changes nothing (" + expected + ")");
        }

        /// <summary>A save with a griffin "g" at <paramref name="level"/> and the avatar at the same level.</summary>
        private static PlayerSave Save(int level)
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("g", "griffin", level));
            save.Avatar.Level = level;
            return save;
        }

        private static ShopContext Stocked(PlayerSave save, List<ShopListing> listings)
        {
            ShopContext here = new ShopContext("r02", 0, 3, 15, 7);
            save.Shops.Add(new ShopVisit { NodeKey = here.NodeKey, Listings = listings });
            return here;
        }

        private static ShopListing Listing(ShopCategory category, string id, int quantity, int price)
        {
            return new ShopListing { Category = category, ItemId = id, Quantity = System.Math.Max(1, quantity), Remaining = quantity, Price = price };
        }
    }
}
