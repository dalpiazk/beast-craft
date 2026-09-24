using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Customization;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Save schema 4 (the economy): <see cref="Wallet"/>, the 3 → 4 migration
    /// (<see cref="SaveMigrations.AddEconomy"/>), <see cref="PlayerSave.EnsureInitialized"/>'s
    /// economy repairs, <see cref="SaveValidator"/>'s economy checks, the drop tables' schema-2 gold
    /// (<see cref="GoldTable"/>, <see cref="DropTableValidator"/>) and the campaign's reward modifiers.
    /// </summary>
    public class EconomySaveTests
    {
        private static SaveSerializer NewSerializer(ISaveEconomyCatalog economy = null)
        {
            return new SaveSerializer(new JsonSaveSerializer(), null, null, PlayerSave.CurrentSchemaVersion, null, economy);
        }

        [Test]
        public void Wallet_AddsUpToTheCeiling_AndSpendsOnlyWhatIsHeld()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.AreEqual(0, Wallet.Balance(save));
            Assert.AreEqual(25, Wallet.Add(save, 25));
            Assert.AreEqual(0, Wallet.Add(save, -5));
            Assert.IsFalse(Wallet.TrySpend(save, 26));
            Assert.AreEqual(25, save.Gold, "a refused spend changes nothing");
            Assert.IsFalse(Wallet.TrySpend(save, -1));
            Assert.IsTrue(Wallet.TrySpend(save, 25));
            Assert.AreEqual(0, save.Gold);

            save.Gold = Wallet.MaxGold - 3;
            Assert.AreEqual(3, Wallet.Add(save, 100), "income past the ceiling is lost");
            Assert.AreEqual(Wallet.MaxGold, save.Gold);
            Assert.AreEqual(0, Wallet.Add(null, 5));
            Assert.IsFalse(Wallet.TrySpend(null, 0));
        }

        [Test]
        public void Migration_V3ToV4_AddsAnEmptyEconomy_AndRoundTrips()
        {
            const string v3 = "{\"SchemaVersion\":3," +
                              "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"griffin\",\"Level\":14,\"Xp\":5,\"BankedXp\":0}," +
                              "\"Skills\":{\"Known\":[],\"Equipped\":[\"\",\"\",\"\"]},\"EquippedGear\":[\"\",\"\",\"\"]}]," +
                              "\"Avatar\":{\"Level\":9,\"Xp\":3}," +
                              "\"Materials\":{\"Materials\":[{\"MaterialId\":\"essence_shard\",\"Quantity\":2}],\"ClearedCells\":[],\"Pity\":[]}," +
                              "\"Gear\":{\"BeastGear\":[],\"AvatarGear\":[],\"NextInstanceNumber\":1},\"AvatarEquippedGear\":[\"\",\"\",\"\"]," +
                              "\"Campaign\":{\"Seals\":[],\"Regions\":[{\"RegionId\":\"r01\",\"StagesCleared\":2,\"BossCleared\":false}],\"CurrentRegionId\":\"r01\"," +
                              "\"ActiveRun\":{\"RegionId\":\"\",\"Stage\":0,\"Seed\":0,\"Nodes\":[],\"CurrentNodeId\":-1,\"Cleared\":[],\"Attempts\":0,\"NodeAttempts\":0}}}";

            SaveLoadResult migrated = NewSerializer().Deserialize(v3);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(3, migrated.SourceVersion);
            Assert.IsEmpty(migrated.Issues, string.Join("\n", migrated.Issues));
            PlayerSave save = migrated.Save;
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, save.SchemaVersion, "3 -> 4 -> current");
            Assert.AreEqual(0, save.Gold);
            Assert.IsEmpty(save.Consumables);
            Assert.IsEmpty(save.Shops);
            Assert.IsEmpty(save.Cosmetics.Unlocked);
            Assert.IsEmpty(save.AvatarAppearance.OptionEntries);
            Assert.IsNotNull(save.FindBeast("b1").Appearance);
            Assert.AreEqual(14, save.FindBeast("b1").Progress.Level);
            Assert.AreEqual(2, save.Materials.GetCount("essence_shard"));
            Assert.AreEqual(2, save.Campaign.FindRegion("r01").StagesCleared);

            save.Gold = 120;
            save.Consumables.Add(new ConsumableStack("fury_draught", 2));
            save.Cosmetics.Unlock("griffin_crest/crest_storm");
            save.AvatarAppearance.SetOption("avatar_hair", "hair_braids");
            save.AvatarAppearance.SetColor("avatar_hair_color", new Color(0.2f, 0.4f, 0.6f, 1f));
            save.Shops.Add(new ShopVisit
            {
                NodeKey = "r01/2/5/123",
                Listings = new List<ShopListing> { new ShopListing { Category = ShopCategory.Material, ItemId = "essence_shard", Quantity = 2, Price = 3, Remaining = 1 } }
            });

            SaveSerializer serializer = NewSerializer();
            string v4 = serializer.Serialize(save);
            SaveLoadResult reloaded = serializer.Deserialize(v4);
            Assert.IsTrue(reloaded.Success, reloaded.Error);
            Assert.IsFalse(reloaded.Migrated);
            Assert.IsEmpty(reloaded.Issues, string.Join("\n", reloaded.Issues));
            Assert.AreEqual(v4, serializer.Serialize(reloaded.Save));
            Assert.AreEqual(120, reloaded.Save.Gold);
            Assert.AreEqual(2, reloaded.Save.Consumables[0].Quantity);
            Assert.AreEqual("hair_braids", reloaded.Save.AvatarAppearance.GetOption("avatar_hair"));
            Assert.AreEqual(0.4f, reloaded.Save.AvatarAppearance.GetColor("avatar_hair_color").g, 1e-6f);
            Assert.AreEqual(1, reloaded.Save.Shops[0].Listings[0].Remaining);
        }

        [Test]
        public void EnsureInitialized_RepairsTheEconomy()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", "griffin", 3));
            save.Consumables = null;
            save.Shops = new List<ShopVisit> { null, new ShopVisit { NodeKey = "k", Listings = null } };
            save.Cosmetics = null;
            save.AvatarAppearance = null;
            save.Beasts[0].Appearance = null;

            Assert.AreEqual(6, save.EnsureInitialized(), "consumables, a null visit, a visit's listings, cosmetics, avatar and beast appearance (each one)");
            Assert.IsNotNull(save.Consumables);
            Assert.AreEqual(1, save.Shops.Count);
            Assert.IsNotNull(save.Shops[0].Listings);
            Assert.IsNotNull(save.Cosmetics.Unlocked);
            Assert.IsNotNull(save.AvatarAppearance.ColorEntries);
            Assert.IsNotNull(save.Beasts[0].Appearance.OptionEntries);
            Assert.AreEqual(0, save.EnsureInitialized());
        }

        [Test]
        public void Validate_ReportsBadEconomyData()
        {
            FakeEconomy economy = new FakeEconomy();
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", "griffin", 3));
            Assert.IsEmpty(SaveValidator.Validate(save, null, null, economy));

            save.Gold = -1;
            save.Consumables.Add(new ConsumableStack("fury_draught", 6));
            save.Consumables.Add(new ConsumableStack("fury_draught", 1));
            save.Consumables.Add(new ConsumableStack("mystery", 1));
            save.Shops.Add(new ShopVisit { NodeKey = "k" });
            save.Shops.Add(new ShopVisit { NodeKey = "k" });
            save.Shops[0].Listings.Add(new ShopListing { ItemId = "x", Quantity = 1, Remaining = 2 });
            save.Cosmetics.Unlock("griffin_crest/crest_storm");
            save.Cosmetics.Unlocked.Add("griffin_crest/crest_storm");
            save.Cosmetics.Unlock("nope");
            save.AvatarAppearance.SetOption("avatar_hair", "hair_mohawk");
            save.AvatarAppearance.SetOption("griffin_crest", "crest_plain");
            save.Beasts[0].Appearance.SetOption("griffin_crest", "crest_royal");
            save.Beasts[0].Appearance.SetOption("griffin_crest_bogus", "x");
            save.Beasts[0].Appearance.SetColor("avatar_hair_color", Color.black);

            List<SaveIssue> issues = SaveValidator.Validate(save, null, null, economy);
            string all = string.Join("\n", issues);

            Assert.AreEqual(1, issues.FindAll(i => i.Path == "Gold").Count, all);
            Assert.AreEqual(3, issues.FindAll(i => i.Path.StartsWith("Consumables", StringComparison.Ordinal)).Count, all);
            Assert.AreEqual(2, issues.FindAll(i => i.Kind == SaveIssueKind.InvalidShopVisit).Count, all);
            Assert.AreEqual(2, issues.FindAll(i => i.Path.StartsWith("Cosmetics", StringComparison.Ordinal)).Count, all);
            Assert.AreEqual(1, issues.FindAll(i => i.Path.StartsWith("AvatarAppearance", StringComparison.Ordinal) && i.Kind == SaveIssueKind.CosmeticNotUnlocked).Count, all);
            Assert.AreEqual(1, issues.FindAll(i => i.Path.StartsWith("AvatarAppearance", StringComparison.Ordinal) && i.Kind == SaveIssueKind.UnknownCosmetic).Count,
                            all + "\n(a griffin look on the avatar)");
            Assert.AreEqual(3, issues.FindAll(i => i.Path.StartsWith("Beasts[0].Appearance", StringComparison.Ordinal)).Count,
                            all + "\n(royal crest locked, bogus category, the avatar's colour)");
            Assert.AreEqual(13, issues.Count, all);

            Assert.AreEqual(6, SaveValidator.Validate(save, null, null, null).Count, "without a catalog only structure: gold, a second stack, 2 visits, a twice-unlocked key, the bad key");
        }

        [Test]
        public void GoldTable_RollsTheFormula_WithOneDraw()
        {
            DropTableData data = DropTableTests.LoadTables();
            DropTable table = DropTableBuilder.Build(data, null);
            GoldTable gold = table.Gold;

            Assert.AreEqual(data.Gold.Base, gold.Base);
            Assert.AreEqual(1.5, gold.Multiplier("elite"), 1e-6);
            Assert.AreEqual(1.0, gold.Multiplier("unknown"), 1e-6);
            Assert.AreEqual((gold.Base + (gold.PerLevel * 20)) * 1.5, gold.BaseGold("elite", 20), 1e-6);
            Assert.AreEqual(Math.Round(gold.BaseGold("squad", 30)), gold.Roll("squad", 30, false, 1.0, 0, null), "a null rng rolls no variance");
            Assert.AreEqual(Math.Round(gold.BaseGold("squad", 30)) + gold.FirstClearBonus, gold.Roll("squad", 30, true, 1.0, 0, null));
            Assert.AreEqual((Math.Round(gold.BaseGold("squad", 30)) * 2) + 7, gold.Roll("squad", 30, false, 2.0, 7, null));

            Random a = new Random(5);
            Random b = new Random(5);
            int low = int.MaxValue;
            int high = 0;
            for (int i = 0; i < 400; i++)
            {
                int rolled = gold.Roll("horde", 50, false, 1.0, 0, a);
                b.Next((2 * gold.VariancePct) + 1);
                low = Math.Min(low, rolled);
                high = Math.Max(high, rolled);
            }

            Assert.AreEqual(a.Next(), b.Next(), "one draw per roll");
            double mid = gold.BaseGold("horde", 50);
            Assert.That(low, Is.InRange(mid * 0.89, mid * 0.92));
            Assert.That(high, Is.InRange(mid * 1.08, mid * 1.11));
            Assert.IsFalse(GoldTable.None.PaysGold);
        }

        [Test]
        public void DropTableValidator_ChecksTheEconomySections()
        {
            DropTableData table = DropTableTests.LoadTables();
            Assert.IsEmpty(DropTableValidator.Validate(table));

            table.Gold.VariancePct = 80;
            table.Gold.ShapeMultipliers = new[] { new ShapeMultiplierData { Shape = "solo", Multiplier = 0f }, new ShapeMultiplierData { Shape = "bogus", Multiplier = 1f } };
            table.GearDrops = new[] { new GearDropData { Shape = "elite", Rarity = 3, ChancePerMille = 0 } };
            table.CosmeticDrops = new[] { new CosmeticDropData { Shape = "squad", ChancePerMille = 5 }, new CosmeticDropData { Shape = "squad", ChancePerMille = 5 } };
            List<string> errors = DropTableValidator.Validate(table);
            Assert.AreEqual(6, errors.Count, string.Join("\n", errors));

            DropTableData v1 = DropTableTests.LoadTables();
            v1.SchemaVersion = 1;
            StringAssert.Contains("SchemaVersion 2", string.Join("\n", DropTableValidator.Validate(v1)), "gold needs schema 2");
            v1.Gold = new GoldData();
            v1.GearDrops = new GearDropData[0];
            v1.CosmeticDrops = new CosmeticDropData[0];
            Assert.IsEmpty(DropTableValidator.Validate(v1), "a version-1 table without gold still reads");
        }

        [Test]
        public void RewardModifiersFor_PaysDensPassesAndLairs()
        {
            Assert.AreEqual(1.0, CampaignRules.RewardModifiersFor(new MapNode { Type = MapNodeType.Battle }).GoldMultiplier);
            Assert.AreEqual(CampaignRules.EliteGoldMultiplier, CampaignRules.RewardModifiersFor(new MapNode { Type = MapNodeType.Elite }).GoldMultiplier);
            Assert.AreEqual(CampaignRules.GateBonusGold, CampaignRules.RewardModifiersFor(new MapNode { Type = MapNodeType.Gate }).BonusGold);
            Assert.AreEqual(CampaignRules.BossBonusGold, CampaignRules.RewardModifiersFor(new MapNode { Type = MapNodeType.Boss }).BonusGold);
            Assert.AreEqual(0, CampaignRules.RewardModifiersFor(null).BonusGold);
            Assert.AreNotSame(CampaignRules.RewardModifiersFor(null), CampaignRules.RewardModifiersFor(null));
        }

        /// <summary>
        /// A tiny economy catalog: one consumable (max stack 5); an avatar hair category (default
        /// short, starter braids, mohawk locked), an avatar hair colour, and a griffin crest (default
        /// plain, storm and royal locked).
        /// </summary>
        internal sealed class FakeEconomy : ISaveEconomyCatalog
        {
            public bool TryGetConsumable(string consumableId, out int maxStack)
            {
                maxStack = 5;
                return consumableId == "fury_draught";
            }

            public bool TryGetCosmeticCategory(string categoryId, out string speciesId, out bool isColor)
            {
                speciesId = categoryId == "griffin_crest" ? "griffin" : string.Empty;
                isColor = categoryId == "avatar_hair_color";
                return categoryId == "griffin_crest" || categoryId == "avatar_hair" || categoryId == "avatar_hair_color";
            }

            public bool IsKnownCosmeticOption(string categoryId, string optionId)
            {
                return (categoryId == "avatar_hair" && (optionId == "hair_short" || optionId == "hair_braids" || optionId == "hair_mohawk")) ||
                       (categoryId == "griffin_crest" && (optionId == "crest_plain" || optionId == "crest_storm" || optionId == "crest_royal"));
            }

            public bool IsFreeCosmetic(string categoryId, string optionId)
            {
                return optionId == "hair_short" || optionId == "hair_braids" || optionId == "crest_plain";
            }
        }
    }
}
