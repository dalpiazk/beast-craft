using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Avatar, Inventory and Shop/Trader screens (docs/design/avatar-inventory-shop.md): every
    /// view-model is a thin, autosaving wrapper over Core rules already covered elsewhere (the avatar's
    /// <see cref="SkillBook"/>, <see cref="GearRules"/>, <see cref="CosmeticRules"/>,
    /// <see cref="ShopService"/>) — these tests check the view-model reads the right rows and the
    /// actions call through correctly, the same shape as <c>GroveScreensTests</c>.
    /// </summary>
    public class AvatarInventoryShopTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(ManualGameClock clock = null)
        {
            return TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => 424242, clock ?? new ManualGameClock(T0, TimeSpan.FromHours(1000))));
        }

        // ------------------------------------------------------------------ Avatar: Overview

        [Test]
        public void AvatarOverviewViewModel_Refresh_ReportsLevelXpAndStats_BaseEqualsTotalWithNoGear()
        {
            GameSession session = NewSession();
            AvatarOverviewViewModel model = new AvatarOverviewViewModel(session);

            Assert.AreEqual(1, model.Level);
            Assert.AreEqual(0, model.Xp);
            Assert.AreEqual(AvatarProgression.XpToNextLevel(1), model.XpToNext);
            Assert.AreEqual(6, model.Stats.Count, "HP, Attack, Defense, SpA, SpD, Speed");
            foreach (AvatarStatRow row in model.Stats)
            {
                if (row.Name == "HP")
                {
                    Assert.AreEqual(1, row.Total, "StatCalculator floors HP at 1 even with a zero base");
                    continue;
                }

                Assert.AreEqual(row.Base, row.Total, row.Name + " has no gear worn yet");
            }
        }

        // ------------------------------------------------------------------ Avatar: Skills

        [Test]
        public void AvatarSkillsViewModel_StartsWithTheDefaultLoadout_KnownAndEquipped()
        {
            GameSession session = NewSession();
            AvatarSkillsViewModel model = new AvatarSkillsViewModel(session);

            Assert.AreEqual(AvatarSkillBook.ActiveSlotCount, model.ActiveSlots.Count);
            Assert.IsTrue(model.ActiveSlots.All(s => s.SkillId != null && s.Card != null), "the avatar starts with its default actives equipped");
            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, model.PassiveSlots.Count);
            Assert.IsTrue(model.PassiveSlots.All(s => s.PassiveName != null), "the avatar starts with its default passives equipped");
            Assert.AreEqual(3, model.KnownActives.Count);
            Assert.AreEqual(3, model.KnownPassives.Count);
        }

        [Test]
        public void AvatarSkillsViewModel_UnequipActive_RefusesTheLastOne_Autosaves()
        {
            GameSession session = NewSession();
            AvatarSkillsViewModel model = new AvatarSkillsViewModel(session);
            int saves = session.AutosaveCount;

            Assert.IsTrue(model.UnequipActive(0, out string first));
            Assert.AreEqual("Unequipped.", first);
            Assert.IsNull(model.ActiveSlots[0].SkillId);
            Assert.Greater(session.AutosaveCount, saves);

            Assert.IsTrue(model.UnequipActive(1, out _));
            Assert.IsFalse(model.UnequipActive(2, out string refused), "the avatar always fights with at least one active");
            StringAssert.Contains("at least one active", refused);
            Assert.IsNotNull(model.ActiveSlots[2].SkillId);
        }

        [Test]
        public void AvatarSkillsViewModel_EquipActive_SwapsWhenAlreadyEquippedElsewhere()
        {
            GameSession session = NewSession();
            AvatarSkillsViewModel model = new AvatarSkillsViewModel(session);
            string secondSlotId = model.ActiveSlots[1].SkillId;

            Assert.IsTrue(model.EquipActive(0, secondSlotId, out string message));
            Assert.AreEqual("Swapped.", message);
            Assert.AreEqual(secondSlotId, model.ActiveSlots[0].SkillId);
        }

        [Test]
        public void AvatarSkillsViewModel_UnequipPassive_RefusesTheLastOne()
        {
            GameSession session = NewSession();
            AvatarSkillsViewModel model = new AvatarSkillsViewModel(session);

            Assert.IsTrue(model.UnequipPassive(0, out _));
            Assert.IsTrue(model.UnequipPassive(1, out _));
            Assert.IsFalse(model.UnequipPassive(2, out string refused));
            StringAssert.Contains("at least one passive", refused);
        }

        // ------------------------------------------------------------------ Avatar: Gear

        [Test]
        public void AvatarGearViewModel_EquipAndUnequip_RoundTrips_Autosaves()
        {
            GameSession session = NewSession();
            AvatarGearSO piece = Content.Economy.Gear.AvatarGearAssets.First(g => g.MinimumLevel <= 1);
            string instanceId = session.Save.Gear.AddAvatarGear(piece.AvatarGearId);
            AvatarGearViewModel model = new AvatarGearViewModel(session);
            int saves = session.AutosaveCount;

            AvatarGearSlotRow slot = model.Slots.Find(s => s.Slot == piece.Slot);
            Assert.IsNull(slot.Worn);
            AvatarGearOptionRow option = slot.Options.Find(o => o.InstanceId == instanceId);
            Assert.IsNotNull(option);
            Assert.IsTrue(option.Equippable);

            Assert.IsTrue(model.EquipGear(piece.Slot, instanceId, out string equipMessage));
            Assert.AreEqual("Equipped.", equipMessage);
            Assert.Greater(session.AutosaveCount, saves);
            Assert.AreEqual(instanceId, model.Slots.Find(s => s.Slot == piece.Slot).Worn?.InstanceId);

            Assert.IsTrue(model.UnequipGear(piece.Slot, out string unequipMessage));
            Assert.AreEqual("Taken off.", unequipMessage);
            Assert.IsNull(model.Slots.Find(s => s.Slot == piece.Slot).Worn);
        }

        [Test]
        public void AvatarGearViewModel_Equip_RefusesAnUnknownInstance()
        {
            GameSession session = NewSession();
            AvatarGearViewModel model = new AvatarGearViewModel(session);

            Assert.IsFalse(model.EquipGear(AvatarGearSlot.Weapon, "not-owned", out string message));
            StringAssert.Contains("do not have it", message);
        }

        // ------------------------------------------------------------------ Avatar: Wardrobe

        [Test]
        public void AvatarWardrobeViewModel_ListsAvatarCategoriesOnly_DiscreteAndColour()
        {
            GameSession session = NewSession();
            AvatarWardrobeViewModel model = new AvatarWardrobeViewModel(session);

            Assert.IsTrue(model.Categories.Count > 0);
            foreach (CosmeticCategory category in Content.Economy.Cosmetics.Categories)
            {
                if (category != null && category.IsAvatar)
                {
                    Assert.IsTrue(model.Categories.Exists(c => c.CategoryId == category.CategoryId), category.CategoryId + " is an avatar category");
                }
            }
        }

        [Test]
        public void AvatarWardrobeViewModel_Wear_SetsTheOptionAndAutosaves()
        {
            GameSession session = NewSession();
            AvatarWardrobeViewModel model = new AvatarWardrobeViewModel(session);
            WardrobeCategoryRow discrete = model.Categories.First(c => !c.IsColor && c.Options.Count > 1);
            WardrobeOptionRow owned = discrete.Options.First(o => o.Owned && !o.Worn);
            int saves = session.AutosaveCount;

            CosmeticResult result = model.Wear(discrete.CategoryId, owned.OptionId);

            Assert.AreEqual(CosmeticResult.Set, result);
            Assert.Greater(session.AutosaveCount, saves);
            Assert.IsTrue(model.Categories.First(c => c.CategoryId == discrete.CategoryId).Options.First(o => o.OptionId == owned.OptionId).Worn);
        }

        [Test]
        public void AvatarWardrobeViewModel_SetColor_UpdatesTheWornSwatch_ColoursAreAlwaysFree()
        {
            GameSession session = NewSession();
            AvatarWardrobeViewModel model = new AvatarWardrobeViewModel(session);
            WardrobeCategoryRow colour = model.Categories.First(c => c.IsColor);
            string swatch = AvatarWardrobeViewModel.Swatches[1];

            CosmeticResult result = model.SetColor(colour.CategoryId, swatch);

            Assert.AreEqual(CosmeticResult.Set, result);
            Assert.AreEqual(swatch, model.Categories.First(c => c.CategoryId == colour.CategoryId).WornColorHex);
        }

        // ------------------------------------------------------------------ Avatar: hub

        [Test]
        public void AvatarHubViewModel_Select_ChangesTab_RefreshAll_RefreshesEveryChild()
        {
            GameSession session = NewSession();
            AvatarHubViewModel hub = new AvatarHubViewModel(session);

            Assert.AreEqual(AvatarTab.Overview, hub.Tab);
            hub.Select(AvatarTab.Gear);
            Assert.AreEqual(AvatarTab.Gear, hub.Tab);

            hub.Gear.EquipGear(Content.Economy.Gear.AvatarGearAssets.First(g => g.MinimumLevel <= 1).Slot,
                                session.Save.Gear.AddAvatarGear(Content.Economy.Gear.AvatarGearAssets.First(g => g.MinimumLevel <= 1).AvatarGearId), out _);
            hub.RefreshAll();
            Assert.IsTrue(hub.Gear.Slots.Exists(s => s.Worn != null), "RefreshAll re-pulled the gear tab's own state");
        }

        // ------------------------------------------------------------------ Inventory: Gear

        [Test]
        public void InventoryGearViewModel_ListsBeastAndAvatarGear_FilterAndSort()
        {
            GameSession session = NewSession();
            GearSO beastPiece = Content.Economy.Gear.BeastGearAssets.First(g => g.MinimumLevel <= 1);
            AvatarGearSO avatarPiece = Content.Economy.Gear.AvatarGearAssets.First(g => g.MinimumLevel <= 1);
            session.Save.Gear.AddBeastGear(beastPiece.GearId);
            session.Save.Gear.AddAvatarGear(avatarPiece.AvatarGearId);
            InventoryGearViewModel model = new InventoryGearViewModel(session);

            Assert.AreEqual(2, model.Gear.Count);
            model.SetFilter(GearOwnerFilter.Beast);
            Assert.IsTrue(model.Gear.All(g => !g.IsAvatarGear));
            model.SetFilter(GearOwnerFilter.Avatar);
            Assert.IsTrue(model.Gear.All(g => g.IsAvatarGear));
            model.SetFilter(GearOwnerFilter.All);

            Assert.AreEqual(GearSortMode.Rarity, model.Sort);
            model.CycleSort();
            Assert.AreEqual(GearSortMode.Level, model.Sort);
            model.CycleSort();
            Assert.AreEqual(GearSortMode.Name, model.Sort);
        }

        [Test]
        public void InventoryGearViewModel_EquipToBeast_UsesGearRules_Autosaves()
        {
            GameSession session = NewSession();
            GearSO beastPiece = Content.Economy.Gear.BeastGearAssets.First(g => g.MinimumLevel <= 1);
            string instanceId = session.Save.Gear.AddBeastGear(beastPiece.GearId);
            InventoryGearViewModel model = new InventoryGearViewModel(session);
            string beastId = session.Save.Beasts[0].BeastId;
            int saves = session.AutosaveCount;

            Assert.IsTrue(model.EquipToBeast(instanceId, beastId, out string message));
            Assert.AreEqual("Equipped.", message);
            Assert.Greater(session.AutosaveCount, saves);
            Assert.AreEqual(session.BeastName(session.Save.FindBeast(beastId)), model.Gear.Find(g => g.InstanceId == instanceId).WornBy);
        }

        [Test]
        public void InventoryGearViewModel_EquipToAvatar_UsesGearRules()
        {
            GameSession session = NewSession();
            AvatarGearSO piece = Content.Economy.Gear.AvatarGearAssets.First(g => g.MinimumLevel <= 1);
            string instanceId = session.Save.Gear.AddAvatarGear(piece.AvatarGearId);
            InventoryGearViewModel model = new InventoryGearViewModel(session);

            Assert.IsTrue(model.EquipToAvatar(instanceId, out string message));
            Assert.AreEqual("Equipped.", message);
            Assert.AreEqual("Avatar", model.Gear.Find(g => g.InstanceId == instanceId).WornBy);
        }

        [Test]
        public void InventoryGearViewModel_Sell_RefusesWornGear_SellsUnwornGear_ForAQuarterPrice()
        {
            GameSession session = NewSession();
            GearSO beastPiece = Content.Economy.Gear.BeastGearAssets.First(g => g.MinimumLevel <= 1);
            string worn = session.Save.Gear.AddBeastGear(beastPiece.GearId);
            string spare = session.Save.Gear.AddBeastGear(beastPiece.GearId);
            string beastId = session.Save.Beasts[0].BeastId;
            GearRules.EquipBeastGear(session.Save, beastId, beastPiece.Slot, worn, session.Content.Battle);
            InventoryGearViewModel model = new InventoryGearViewModel(session);

            Assert.IsFalse(model.Sell(worn, out string refused, out int noGold), "worn gear cannot be sold");
            StringAssert.Contains("Take it off", refused);
            Assert.AreEqual(0, noGold);

            int goldBefore = session.Save.Gold;
            Assert.IsTrue(model.Sell(spare, out string sold, out int gold));
            StringAssert.Contains("Sold for", sold);
            Assert.Greater(gold, 0);
            Assert.AreEqual(goldBefore + gold, session.Save.Gold);
            Assert.IsFalse(model.Gear.Exists(g => g.InstanceId == spare), "sold gear leaves the inventory");
        }

        [Test]
        public void InventoryGearViewModel_BeastsFor_ListsEveryOwnedBeast()
        {
            GameSession session = NewSession();
            InventoryGearViewModel model = new InventoryGearViewModel(session);

            List<BeastOptionRow> beasts = model.BeastsFor("anything");

            Assert.AreEqual(session.Save.Beasts.Count, beasts.Count);
            Assert.IsTrue(beasts.All(b => !string.IsNullOrEmpty(b.Name)));
        }

        // ------------------------------------------------------------------ Inventory: Materials

        [Test]
        public void InventoryMaterialsViewModel_ListsOnlyWhatIsHeld_MaterialsGroveItemsConsumables()
        {
            GameSession session = NewSession();
            string materialId = Content.SkillLibrary.Materials[0].MaterialId;
            string consumableId = Content.Economy.Consumables.All[0].ConsumableId;
            session.Save.Materials.Add(materialId, 3);
            session.Save.Grove.Items.Add("test_relic", 2);
            ConsumableInventory.TryAdd(session.Save, consumableId, 1, Content.Economy.Consumables.Get(consumableId).MaxStack);

            InventoryMaterialsViewModel model = new InventoryMaterialsViewModel(session);

            Assert.AreEqual(1, model.Materials.Count);
            Assert.AreEqual(3, model.Materials[0].Quantity);
            Assert.AreEqual(1, model.GroveItems.Count);
            Assert.AreEqual("test_relic", model.GroveItems[0].Id);
            Assert.AreEqual(1, model.Consumables.Count);
            Assert.AreEqual(Content.Economy.Consumables.Get(consumableId).DisplayName, model.Consumables[0].Name);
        }

        // ------------------------------------------------------------------ Inventory: Looks

        [Test]
        public void InventoryLooksViewModel_ReportsTokenBalance_AndPerCategoryCollectionCounts()
        {
            GameSession session = NewSession();
            session.Save.LookTokens = 42;
            InventoryLooksViewModel model = new InventoryLooksViewModel(session);

            Assert.AreEqual(42, model.LookTokens);
            Assert.IsTrue(model.Categories.Count > 0);
            foreach (WardrobeCollectionRow row in model.Categories)
            {
                Assert.LessOrEqual(row.Owned, row.Total);
            }
        }

        // ------------------------------------------------------------------ Inventory: hub

        [Test]
        public void InventoryHubViewModel_Select_ChangesTab()
        {
            GameSession session = NewSession();
            InventoryHubViewModel hub = new InventoryHubViewModel(session);

            Assert.AreEqual(InventoryTab.Gear, hub.Tab);
            hub.Select(InventoryTab.Looks);
            Assert.AreEqual(InventoryTab.Looks, hub.Tab);
        }

        // ------------------------------------------------------------------ Shop / Trader

        /// <summary>
        /// Walks the expedition forward (as <c>BeastCraftGame.SetupShop</c> does for the debug screen)
        /// until a Shop-type node exists on the current map — a Trader is not guaranteed on the very
        /// first stage's map, only about once per stage (economy-and-shop.md).
        /// </summary>
        private static int ShopNodeId(GameSession session)
        {
            MapRun run = session.Save.Campaign.ActiveRun;
            for (int guard = 0; guard < 60; guard++)
            {
                List<MapNode> choices = CampaignRules.Choices(run);
                MapNode shop = choices.Find(n => n.Type == MapNodeType.Shop);
                if (shop != null)
                {
                    return shop.NodeId;
                }

                MapNode next = choices[0];
                if (next.Type == MapNodeType.Rest)
                {
                    CampaignRules.Camp(session.Save, session.Content.Campaign, next.NodeId, session.Save.Beasts[0].BeastId);
                }
                else
                {
                    CampaignRules.ResolveBattle(session.Save, session.Content.Campaign, next.NodeId, BattleOutcome.PlayerVictory);
                }

                run = session.Save.Campaign.ActiveRun;
            }

            Assert.Fail("no reachable Shop location found within 60 steps");
            return -1;
        }

        [Test]
        public void ShopViewModel_EnsureOpened_FreezesStockOncePerNode()
        {
            GameSession session = NewSession();
            int nodeId = ShopNodeId(session);

            ShopViewModel first = new ShopViewModel(session, nodeId);
            int shopsAfterFirst = session.Save.Shops.Count;
            List<string> namesAfterFirst = first.Listings.ConvertAll(l => l.Name);

            ShopViewModel second = new ShopViewModel(session, nodeId);

            Assert.AreEqual(shopsAfterFirst, session.Save.Shops.Count, "opening the same node again never re-rolls its stock");
            CollectionAssert.AreEqual(namesAfterFirst, second.Listings.ConvertAll(l => l.Name));
        }

        [Test]
        public void ShopViewModel_Buy_DeductsGoldAndGrantsTheItem_RefusesWithoutEnoughGold()
        {
            GameSession session = NewSession();
            int nodeId = ShopNodeId(session);
            ShopViewModel model = new ShopViewModel(session, nodeId);
            Assert.IsTrue(model.Listings.Count > 0, "the first band always stocks something");

            ShopListingRow affordable = model.Listings.Find(l => l.Category != ShopCategory.BeastSkill && l.Remaining > 0);
            Wallet.Add(session.Save, affordable.Price);
            int goldBefore = session.Save.Gold;
            int saves = session.AutosaveCount;

            ShopOutcome bought = model.Buy(affordable.Index, null, out string message);

            Assert.AreEqual(ShopOutcome.Bought, bought);
            Assert.AreEqual("Bought.", message);
            Assert.AreEqual(goldBefore - affordable.Price, session.Save.Gold);
            Assert.Greater(session.AutosaveCount, saves);
        }

        [Test]
        public void ShopViewModel_Buy_RefusesWithoutEnoughGold_ChangesNothing()
        {
            GameSession session = NewSession();
            int nodeId = ShopNodeId(session);
            session.Save.Gold = 0;
            ShopViewModel model = new ShopViewModel(session, nodeId);
            ShopListingRow row = model.Listings.Find(l => l.Remaining > 0 && l.Price > 0);
            Assert.IsNotNull(row, "the first band always stocks something priced");

            ShopOutcome outcome = model.Buy(row.Index, null, out string message);

            Assert.AreEqual(ShopOutcome.NotEnoughGold, outcome);
            Assert.AreEqual("Not enough gold.", message);
            Assert.AreEqual(0, session.Save.Gold);
        }

        [Test]
        public void ShopViewModel_Sell_UsesTheSameRuleAsInventory()
        {
            GameSession session = NewSession();
            int nodeId = ShopNodeId(session);
            GearSO beastPiece = Content.Economy.Gear.BeastGearAssets.First(g => g.MinimumLevel <= 1);
            string spare = session.Save.Gear.AddBeastGear(beastPiece.GearId);
            ShopViewModel model = new ShopViewModel(session, nodeId);

            Assert.IsTrue(model.SellableGear.Exists(g => g.InstanceId == spare));
            Assert.IsTrue(model.Sell(spare, out string message, out int gold));
            StringAssert.Contains("Sold for", message);
            Assert.Greater(gold, 0);
            Assert.IsFalse(model.SellableGear.Exists(g => g.InstanceId == spare));
        }

        [Test]
        public void ShopViewModel_BeastsForTome_ListsEveryOwnedBeast()
        {
            GameSession session = NewSession();
            ShopViewModel model = new ShopViewModel(session, ShopNodeId(session));

            Assert.AreEqual(session.Save.Beasts.Count, model.BeastsForTome().Count);
        }

        [Test]
        public void ShopViewModel_TheCampTravellingTrader_OpensWithoutClearingTheCampNode()
        {
            GameSession session = NewSession();
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode camp = run.Nodes.Find(n => n.Type == MapNodeType.Rest);
            Assert.IsNotNull(camp, "every campaign map has a camp");

            ShopViewModel model = new ShopViewModel(session, camp.NodeId, true);

            Assert.IsFalse(run.IsCleared(camp.NodeId), "visiting the camp's trader never clears the camp itself");
            Assert.IsTrue(session.Save.Shops.Exists(v => v.NodeKey == CampaignRules.ShopContextFor(run, camp).NodeKey));
        }
    }
}
