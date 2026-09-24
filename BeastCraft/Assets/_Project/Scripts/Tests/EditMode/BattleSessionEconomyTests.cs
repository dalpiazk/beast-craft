using System.Collections.Generic;
using BeastCraft.Battle;
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

        private static string Json(object value)
        {
            return UnityEngine.JsonUtility.ToJson(value);
        }
    }
}
