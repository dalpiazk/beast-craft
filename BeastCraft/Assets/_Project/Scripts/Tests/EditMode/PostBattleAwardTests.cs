using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="PostBattleAward"/>: practice XP to every player beast's fired skills on any
    /// finished battle, drops only on a clear.
    /// </summary>
    public class PostBattleAwardTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [Test]
        public void AwardPractice_CreditsEachBookItsOwnUnitsFires()
        {
            SkillSO strike = Skill("strike", 10f);
            SkillSO poke = Skill("poke", 1f);
            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 50, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { strike }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(100, 10, 50, 0, 0, 10), new HexCoordinate(2, 0),
                                              new SkillLoadout(new[] { poke }));
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(3), null);

            BeastSkillBook book = new BeastSkillBook();
            book.Learn(strike);
            BeastSkillBook bystander = new BeastSkillBook();
            bystander.Learn(strike);
            Dictionary<string, BeastSkillBook> books = new Dictionary<string, BeastSkillBook> { { "p", book }, { "absent", bystander } };

            PostBattleAward.AwardPractice(result, books, id => id == "strike" ? strike : null);

            int fired = BattleSkillUsage.CountFiredSkillsFor(result, "p")["strike"];
            SkillProgress progress = book.GetProgress("strike");
            Assert.Greater(fired, 0);
            Assert.AreEqual(System.Math.Min(fired, SkillProgression.PracticeUseCapPerAward) * SkillProgression.PracticeXpPerUse,
                            SkillProgression.TotalXpToReach(progress.Level) + progress.Xp);
            Assert.AreEqual(0, bystander.GetProgress("strike").Xp, "a book whose unit was not in the battle");
            Assert.AreEqual(0, PostBattleAward.AwardPractice(null, books, null));
        }

        [TestCase(BattleOutcome.EnemyVictory)]
        [TestCase(BattleOutcome.MutualDefeat)]
        [TestCase(BattleOutcome.Stalemate)]
        public void AwardDrops_NothingUnlessCleared(BattleOutcome outcome)
        {
            MaterialInventory inventory = new MaterialInventory();
            LootResult loot = PostBattleAward.AwardDrops(new BattleResult(outcome, 0, null), Table(), "solo", 1, inventory, new System.Random(1));

            Assert.AreEqual(0, loot.Drops.Count);
            Assert.AreEqual(0, inventory.Materials.Count);
            Assert.AreEqual(0, inventory.ClearedCells.Count, "a loss is not a first clear");
        }

        [Test]
        public void AwardDrops_OnAClear_RollsIntoTheInventory()
        {
            MaterialInventory inventory = new MaterialInventory();
            LootResult loot = PostBattleAward.AwardDrops(new BattleResult(BattleOutcome.PlayerVictory, 0, null), Table(), "solo", 1, inventory,
                                                         new System.Random(1));

            Assert.IsTrue(loot.FirstClear);
            Assert.GreaterOrEqual(inventory.GetCount("shard"), 1);
            Assert.AreEqual(loot.QuantityOf("shard"), inventory.GetCount("shard"));
        }

        private static DropTable Table()
        {
            return DropTableBuilder.Build(DropTableTests.Minimal(), DropTableBuilder.TierLookup(DropTableTests.Materials()));
        }

        private SkillSO Skill(string id, float power)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            _created.Add(skill);
            skill.SkillId = id;
            skill.Cooldown = 0;
            skill.TargetShape = SkillTargetShape.AllEnemies;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power });
            return skill;
        }
    }
}
