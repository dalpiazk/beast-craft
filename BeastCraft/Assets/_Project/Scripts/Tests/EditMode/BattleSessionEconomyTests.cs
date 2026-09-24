using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The economy side of <see cref="BattleSession.ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, string, int, DropTable, int, RewardModifiers, System.Random)"/>:
    /// gold on its own seed stream (the material rolls never move), reward modifiers, nothing on a
    /// defeat. Shares <see cref="BattleSessionTests"/>' fixtures.
    /// </summary>
    public partial class BattleSessionTests
    {
        [Test]
        public void ApplyRewards_PaysGoldOnItsOwnStream_WithoutMovingTheMaterialRolls()
        {
            DropTableData goldFreeData = DropTableTests.LoadTables();
            goldFreeData.Gold = new GoldData();
            DropTable goldFree = DropTableBuilder.Build(goldFreeData, DropTableBuilder.TierLookup(_library.Materials));
            Assert.IsFalse(goldFree.Gold.PaysGold);
            Assert.IsTrue(_drops.Gold.PaysGold, "the authored table pays gold");

            PlayerSave withGold = StarterSave();
            PlayerSave without = StarterSave();
            BattleSessionResult first = BattleSession.Run(Setup(withGold, 42, weakEnemies: true));
            BattleSessionResult second = BattleSession.Run(Setup(without, 42, weakEnemies: true));
            Assert.AreEqual(BattleOutcome.PlayerVictory, first.Outcome, first.Error);

            BattleRewardSummary paid = BattleSession.ApplyRewards(withGold, first, _content, Shape, EncounterLevel, _drops);
            BattleRewardSummary unpaid = BattleSession.ApplyRewards(without, second, _content, Shape, EncounterLevel, goldFree);

            Assert.AreEqual(Json(without.Materials), Json(withGold.Materials), "gold never shifts the material rolls");
            Assert.AreEqual(0, unpaid.GoldGained);
            Assert.AreEqual(0, without.Gold);
            int expected = _drops.Gold.Roll(Shape, EncounterLevel, paid.Loot.FirstClear, 1.0, 0, new System.Random(LootRoller.DeriveSeed(42, PostBattleAward.GoldStream)));
            Assert.Greater(expected, 0);
            Assert.AreEqual(expected, paid.GoldGained);
            Assert.AreEqual(expected, withGold.Gold);
        }

        [Test]
        public void ApplyRewards_WithModifiers_MultipliesAndAddsGold()
        {
            PlayerSave plain = StarterSave();
            PlayerSave boosted = StarterSave();
            BattleSessionResult first = BattleSession.Run(Setup(plain, 7, weakEnemies: true));
            BattleSessionResult second = BattleSession.Run(Setup(boosted, 7, weakEnemies: true));

            int basic = BattleSession.ApplyRewards(plain, first, _content, Shape, EncounterLevel, _drops).GoldGained;
            RewardModifiers modifiers = new RewardModifiers { GoldMultiplier = 2.0, BonusGold = 3 };
            int more = BattleSession.ApplyRewards(boosted, second, _content, Shape, EncounterLevel, _drops, BeastProgression.MaxLevel, modifiers).GoldGained;

            Assert.AreEqual((basic * 2) + 3, more);
        }

        [Test]
        public void ApplyRewards_WithTheGearLibrary_GrantsGearDropsOnTheirOwnStream()
        {
            DropTableData data = DropTableTests.LoadTables();
            data.GearDrops = new[] { new GearDropData { Shape = Shape, Rarity = 0, ChancePerMille = 1000 } };
            DropTable table = DropTableBuilder.Build(data, DropTableBuilder.TierLookup(_library.Materials));
            GearLibrary gear = GearLibrary.Build(GearLibraryTests.LoadGear());

            PlayerSave withGear = StarterSave();
            PlayerSave without = StarterSave();
            BattleSessionResult first = BattleSession.Run(Setup(withGear, 42, weakEnemies: true));
            BattleSessionResult second = BattleSession.Run(Setup(without, 42, weakEnemies: true));
            BattleRewardSummary dropped = BattleSession.ApplyRewards(withGear, first, _content, Shape, EncounterLevel, table, BeastProgression.MaxLevel,
                                                                     new RewardModifiers { Gear = gear });
            BattleRewardSummary plain = BattleSession.ApplyRewards(without, second, _content, Shape, EncounterLevel, table);

            Assert.AreEqual(1, dropped.GearGained.Count);
            Assert.IsEmpty(plain.GearGained, "no library, no gear");
            GearItem item = gear.Get(dropped.GearGained[0]);
            Assert.AreEqual(1, item.MinimumLevel, "level 8 draws from the first band");
            Assert.AreEqual(1, withGear.Gear.BeastGear.Count + withGear.Gear.AvatarGear.Count);
            Assert.AreEqual(Json(without.Materials), Json(withGear.Materials));
            Assert.AreEqual(plain.GoldGained, dropped.GoldGained, "gear never moves gold");
        }

        [Test]
        public void ApplyRewards_OnADefeat_PaysNoGold()
        {
            PlayerSave save = StarterSave();
            BattleSetup setup = Setup(save, 3);
            setup.TeamBeastIds = new List<string> { save.Beasts[0].BeastId };
            setup.IncludeAvatar = false;
            setup.Encounter.Enemies = new List<EnemySpec>();
            for (int i = 0; i < 4; i++)
            {
                setup.Encounter.Enemies.Add(new EnemySpec(_library.SpeciesKits[4 + i].SpeciesId, 60));
            }

            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.EnemyVictory, result.Outcome, result.Error);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops, BeastProgression.MaxLevel,
                                                                     new RewardModifiers { BonusGold = 50 });

            Assert.IsTrue(summary.Applied);
            Assert.AreEqual(0, summary.GoldGained, "no clear, no gold (not even a bonus)");
            Assert.AreEqual(0, save.Gold);
        }

        private readonly ConsumableLibrary _consumables = ConsumableLibrary.Build(ConsumableTests.LoadConsumables());

        [Test]
        public void Run_SpendsAConsumableAsTheBattleBegins_AndApplyRewardsNeverSpendsItAgain()
        {
            PlayerSave save = StarterSave();
            ConsumableInventory.TryAdd(save, "fury_draught", 2, 5);
            BattleSetup setup = Setup(save, 42, weakEnemies: true);
            setup.Consumables.Add("fury_draught");

            BattleSessionResult result = BattleSession.Run(setup);
            Assert.IsTrue(result.Success, result.Error);
            CollectionAssert.AreEqual(new[] { "fury_draught" }, result.ConsumablesUsed);
            Assert.IsTrue(result.ConsumablesDeducted);
            Assert.AreEqual(1, ConsumableInventory.Quantity(save, "fury_draught"), "spent as the battle began");

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops);
            Assert.IsTrue(summary.Applied, summary.Error);
            CollectionAssert.AreEqual(new[] { "fury_draught" }, summary.ConsumablesSpent, "reported, not spent again");
            Assert.AreEqual(1, ConsumableInventory.Quantity(save, "fury_draught"), "ApplyRewards never double-spends");
            Assert.IsFalse(BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops).Applied);
            Assert.AreEqual(1, ConsumableInventory.Quantity(save, "fury_draught"), "spent once");

            BattleSessionResult again = BattleSession.Run(setup);
            Assert.AreEqual(Trace(result), Trace(again), "deterministic per seed");
            Assert.AreEqual(0, ConsumableInventory.Quantity(save, "fury_draught"), "the second battle spent the last one");
        }

        [Test]
        public void Run_SpendsTheConsumable_EvenWhenRewardsAreNeverApplied_SoItCannotBeReused()
        {
            PlayerSave save = StarterSave();
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);
            BattleSetup setup = Setup(save, 42, weakEnemies: true);
            setup.Consumables.Add("fury_draught");

            BattleSessionResult first = BattleSession.Run(setup);
            Assert.IsTrue(first.Success, first.Error);
            Assert.AreEqual(0, ConsumableInventory.Quantity(save, "fury_draught"), "spent at Run, with no ApplyRewards");

            BattleSessionResult replay = AssertFails(setup, "is not held");
            Assert.IsFalse(replay.ConsumablesDeducted);
            Assert.IsEmpty(replay.ConsumablesUsed);
        }

        [Test]
        public void Run_WithAnInvalidSetup_LeavesTheSaveByteIdentical()
        {
            SaveSerializer serializer = new SaveSerializer(new JsonUtilitySaveSerializer(), SaveContentCatalog.FromData(_roster, _library));
            PlayerSave save = StarterSave();
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);
            string before = serializer.Serialize(save);

            // Fails validation (an unknown enemy species) with the consumable itself valid.
            BattleSetup invalid = Setup(save, 1);
            invalid.Consumables.Add("fury_draught");
            invalid.Encounter.Enemies.Add(new EnemySpec("no_such_species", 5));
            AssertFails(invalid, "unknown species");
            Assert.AreEqual(before, serializer.Serialize(save));

            // Passes validation but fails placement, which runs after the consumable check.
            BattleSetup badTile = Setup(save, 1);
            badTile.Consumables.Add("fury_draught");
            badTile.Encounter.Enemies[0].Position = new HexGrid(badTile.Encounter.Arena).GetDeploymentZone(BattleTeam.Player)[0];
            AssertFails(badTile, "cannot stand at");
            Assert.AreEqual(before, serializer.Serialize(save));
            Assert.AreEqual(1, ConsumableInventory.Quantity(save, "fury_draught"));
        }

        [Test]
        public void Run_WithAConsumable_ChangesTheBattle_AndWithoutOneIsTheSameBattle()
        {
            PlayerSave save = StarterSave();
            int changed = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                BattleSetup plain = Setup(save, seed);
                BattleSetup empty = Setup(save, seed);
                empty.Consumables = new List<string>();
                BattleSetup venom = Setup(save, seed);
                venom.Consumables.Add("venom_flask");
                ConsumableInventory.TryAdd(save, "venom_flask", 1, 3); // each venom battle spends one

                string plainTrace = Trace(BattleSession.Run(plain));
                Assert.AreEqual(plainTrace, Trace(BattleSession.Run(empty)), "no consumable is exactly the old battle");
                changed += plainTrace == Trace(BattleSession.Run(venom)) ? 0 : 1;
            }

            Assert.Greater(changed, 0, "the venom changes some battles");

            List<BattleUnit> team = new List<BattleUnit> { BattleUnitFactory.CreateBeast("t", BattleTeam.Player, _content.GetSpecies(_library.SpeciesKits[0].SpeciesId), 40, null,
                                                                                         default(BeastCraft.Battle.Grid.HexCoordinate), new SkillLoadout(new List<SkillSO>())) };
            int before = team[0].Stats.Defense;
            ConsumableLoadout.Apply(new[] { _consumables.Get("iron_tonic") }, team, new List<BattleUnit>(), null, new System.Random(1));
            Assert.Greater(team[0].Stats.Defense, before, "the tonic's percent Defense buff landed");
        }

        [Test]
        public void Run_SpendsTheConsumableOnADefeat_Too()
        {
            PlayerSave save = StarterSave();
            ConsumableInventory.TryAdd(save, "smoke_bomb", 1, 5);
            BattleSetup setup = Setup(save, 3);
            setup.TeamBeastIds = new List<string> { save.Beasts[0].BeastId };
            setup.IncludeAvatar = false;
            setup.Consumables.Add("smoke_bomb");
            setup.Encounter.Enemies = new List<EnemySpec>();
            for (int i = 0; i < 4; i++)
            {
                setup.Encounter.Enemies.Add(new EnemySpec(_library.SpeciesKits[4 + i].SpeciesId, 60));
            }

            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.EnemyVictory, result.Outcome, result.Error);
            Assert.AreEqual(0, ConsumableInventory.Quantity(save, "smoke_bomb"), "spent as the battle began");
            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops);
            CollectionAssert.AreEqual(new[] { "smoke_bomb" }, summary.ConsumablesSpent);
            Assert.AreEqual(0, ConsumableInventory.Quantity(save, "smoke_bomb"));
        }

        [Test]
        public void Run_RefusesTwoConsumables_OrOneNotHeld()
        {
            PlayerSave save = StarterSave();
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);
            ConsumableInventory.TryAdd(save, "iron_tonic", 1, 5);
            BattleSetup two = Setup(save, 1);
            two.Consumables.Add("fury_draught");
            two.Consumables.Add("iron_tonic");
            AssertFails(two, "At most 1 consumable");

            BattleSetup unheld = Setup(save, 1);
            unheld.Consumables.Add("venom_flask");
            AssertFails(unheld, "is not held");
        }

        private static string Json(object value)
        {
            return UnityEngine.JsonUtility.ToJson(value);
        }
    }
}
