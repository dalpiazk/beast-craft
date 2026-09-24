using BeastCraft.Battle;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The campaign XP rules: the level-gap falloff (<see cref="LevelGapXp"/>) on beast and avatar
    /// battle XP, the bench share with its catch-up (<see cref="BeastProgression.BenchXp"/>), and the
    /// beast level cap with its bank (<see cref="BeastProgression.AddXp(BeastProgress, int, int)"/>,
    /// <see cref="LevelCap"/>).
    /// </summary>
    public class LevelCapAndFalloffTests
    {
        [TestCase(-20, 100)]
        [TestCase(0, 100)]
        [TestCase(1, 60)]
        [TestCase(2, 25)]
        [TestCase(3, 10)]
        [TestCase(4, 5)]
        [TestCase(5, 0)]
        [TestCase(40, 0)]
        public void Falloff_Table(int gap, int percent)
        {
            Assert.AreEqual(percent, LevelGapXp.Percent(gap));
        }

        [Test]
        public void Falloff_AppliesToTheWholeBattleXp_RoundingDown()
        {
            Assert.AreEqual(4, LevelGapXp.MaxPaidGap);
            Assert.AreEqual(2, LevelGapXp.Gap(12, 10));
            Assert.AreEqual(-9, LevelGapXp.Gap(1, 10));
            Assert.AreEqual(0, LevelGapXp.Gap(0, 1), "levels below 1 read as 1");
            Assert.AreEqual(47, LevelGapXp.Apply(79, 1), "79 x 60% = 47.4");
            Assert.AreEqual(0, LevelGapXp.Apply(-5, 0));

            int raw = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false);
            Assert.AreEqual(raw, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false, 10));
            Assert.AreEqual(raw, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false, 3), "below the encounter: no falloff (and no bonus)");
            Assert.AreEqual(raw * 25 / 100, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false, 12));
            Assert.AreEqual(BeastProgression.ParticipationXp * 60 / 100, BeastProgression.BattleXp(BattleOutcome.EnemyVictory, 10, false, 11), "participation too");
            Assert.AreEqual(0, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false, 15));

            int avatarRaw = AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, 20);
            Assert.AreEqual(avatarRaw * 10 / 100, AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, 20, 23));
        }

        [Test]
        public void AwardBattle_FallsOffOnTheLevelBeforeTheAward()
        {
            BeastProgress beast = new BeastProgress("emberfox", 11);
            int expected = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false) * 60 / 100;

            BeastProgression.AwardBattle(beast, BattleOutcome.PlayerVictory, 10, false);

            Assert.AreEqual(expected, beast.Xp);

            AvatarProgress avatar = new AvatarProgress { Level = 30 };
            Assert.AreEqual(0, AvatarProgression.AwardBattle(avatar, BattleOutcome.PlayerVictory, 20));
            Assert.AreEqual(0, avatar.Xp, "ten levels down pays the avatar nothing");
            Assert.AreEqual(0, AvatarProgression.AwardBattle(null, BattleOutcome.PlayerVictory, 20));
        }

        [TestCase(10, 10, 100)]
        [TestCase(10, 15, 100)]
        [TestCase(10, 9, 190)]
        [TestCase(10, 4, 640)]
        [TestCase(10, 1, 910)]
        [TestCase(20, 10, 1000)]
        [TestCase(40, 1, 1000)]
        public void BenchShare_SmallAtTheEnemysLevel_CatchingUpBelowIt(int enemyLevel, int benchLevel, int permille)
        {
            Assert.AreEqual(permille, BeastProgression.BenchSharePermille(enemyLevel, benchLevel));
        }

        [Test]
        public void BenchXp_IsAShareOfAStandingBeastsXp_ThenFallsOff()
        {
            int standing = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 20, false);

            Assert.AreEqual(standing / 10, BeastProgression.BenchXp(BattleOutcome.PlayerVictory, 20, 20));
            Assert.AreEqual(standing * 550 / 1000, BeastProgression.BenchXp(BattleOutcome.PlayerVictory, 20, 15));
            Assert.AreEqual(standing, BeastProgression.BenchXp(BattleOutcome.PlayerVictory, 20, 1));
            Assert.AreEqual(standing / 10 * 60 / 100, BeastProgression.BenchXp(BattleOutcome.PlayerVictory, 20, 21), "a bench beast above the encounter falls off too");
            Assert.AreEqual(BeastProgression.ParticipationXp * 100 / 1000, BeastProgression.BenchXp(BattleOutcome.EnemyVictory, 20, 20), "a loss pays the share of participation");

            for (int benchLevel = 1; benchLevel <= 30; benchLevel++)
            {
                Assert.LessOrEqual(BeastProgression.BenchXp(BattleOutcome.PlayerVictory, 20, benchLevel), standing, "never more than a fielded beast");
            }

            BeastProgress reserve = new BeastProgress("emberfox", 20);
            BeastProgression.AwardBench(reserve, BattleOutcome.PlayerVictory, 20);
            Assert.AreEqual(standing / 10, reserve.Xp);
            Assert.AreEqual(0, BeastProgression.AwardBench(null, BattleOutcome.PlayerVictory, 20));
        }

        [Test]
        public void AddXp_BelowTheCap_LevelsUpToItThenBanks()
        {
            BeastProgress beast = new BeastProgress("emberfox", 10);
            int toCap = BeastProgression.XpToNextLevel(10) + BeastProgression.XpToNextLevel(11);

            Assert.AreEqual(2, BeastProgression.AddXp(beast, toCap + 500, 12));

            Assert.AreEqual(12, beast.Level, "levels stop at the cap");
            Assert.AreEqual(BeastProgression.XpToNextLevel(12) - 1, beast.Xp, "Xp fills to one short of the next level");
            Assert.AreEqual(500 - (BeastProgression.XpToNextLevel(12) - 1), beast.BankedXp, "the rest waits in the bank");
            Assert.AreEqual(1, LevelCap.BankedLevels(beast));

            Assert.AreEqual(1, LevelCap.Release(beast, 22));
            Assert.AreEqual(13, beast.Level);
            Assert.AreEqual(500 - BeastProgression.XpToNextLevel(12), beast.Xp);
            Assert.AreEqual(0, beast.BankedXp, "below the cap nothing is banked");
        }

        [Test]
        public void AddXp_AtTheCap_BanksAtMostThreeLevels()
        {
            BeastProgress beast = new BeastProgress("emberfox", 12);
            int threeLevels = BeastProgression.TotalXpToReach(12 + LevelCap.BankLevelLimit) - BeastProgression.TotalXpToReach(12);

            Assert.AreEqual(0, BeastProgression.AddXp(beast, int.MaxValue, 12));

            Assert.AreEqual(12, beast.Level);
            Assert.AreEqual(threeLevels, beast.Xp + beast.BankedXp, "held XP stops at three levels' worth");
            Assert.AreEqual(LevelCap.BankLevelLimit, LevelCap.BankedLevels(beast));

            Assert.AreEqual(LevelCap.BankLevelLimit, LevelCap.Release(beast, 40), "a big cap rise pays out at most three levels");
            Assert.AreEqual(15, beast.Level);
            Assert.AreEqual(0, beast.Xp);
            Assert.AreEqual(0, beast.BankedXp);

            Assert.AreEqual(0, LevelCap.Release(null, 40));
            Assert.AreEqual(0, LevelCap.BankedLevels(null));
        }

        [Test]
        public void AddXp_ARaisedCapBelowTheBank_ReleasesPartAndRebanksTheRest()
        {
            BeastProgress beast = new BeastProgress("emberfox", 12);
            BeastProgression.AddXp(beast, int.MaxValue, 12);

            Assert.AreEqual(1, LevelCap.Release(beast, 13));

            Assert.AreEqual(13, beast.Level);
            Assert.AreEqual(BeastProgression.XpToNextLevel(13) - 1, beast.Xp);
            Assert.AreEqual(BeastProgression.XpToNextLevel(14) + 1, beast.BankedXp,
                            "what is left of the three levels is still banked (within a fresh three-level limit)");
        }

        [Test]
        public void AddXp_ABeastObtainedAboveTheCap_KeepsItsLevelAndOnlyBanks()
        {
            BeastProgress beast = new BeastProgress("emberfox", 40) { Xp = 120 };

            Assert.AreEqual(0, BeastProgression.AwardBattle(beast, BattleOutcome.PlayerVictory, 40, false, 12));
            Assert.AreEqual(40, beast.Level);
            Assert.AreEqual(120 + BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 40, false), beast.Xp + beast.BankedXp);

            BeastProgression.AddXp(beast, int.MaxValue, 12);
            Assert.AreEqual(40, beast.Level);
            Assert.AreEqual(BeastProgression.TotalXpToReach(43) - BeastProgression.TotalXpToReach(40), beast.Xp + beast.BankedXp, "three levels from its own level");

            Assert.AreEqual(0, LevelCap.Release(beast, 22), "still above the new cap");
            Assert.AreEqual(3, LevelCap.Release(beast, 100));
            Assert.AreEqual(43, beast.Level);
        }

        [Test]
        public void AddXp_WithoutACap_IsTheOldRule_AndTheMaxLevelHoldsNothing()
        {
            BeastProgress beast = new BeastProgress("emberfox", 1);
            Assert.AreEqual(2, BeastProgression.AddXp(beast, 173 + 186 + 5));
            Assert.AreEqual(5, beast.Xp);
            Assert.AreEqual(0, beast.BankedXp);

            BeastProgress top = new BeastProgress("emberfox", BeastProgression.MaxLevel - 1) { BankedXp = 50 };
            Assert.AreEqual(1, BeastProgression.AddXp(top, int.MaxValue, BeastProgression.MaxLevel));
            Assert.AreEqual(0, top.Xp);
            Assert.AreEqual(0, top.BankedXp);

            BeastProgress odd = new BeastProgress { Level = 5, Xp = -3, BankedXp = -7 };
            Assert.AreEqual(0, BeastProgression.AddXp(odd, 10, 0), "a cap below 1 reads as 1");
            Assert.AreEqual(5, odd.Level);
            Assert.AreEqual(10, odd.Xp + odd.BankedXp);
        }

        [Test]
        public void MigratedLevel40Beast_UnderTheStartingCap_KeepsItsLevelAndBanks()
        {
            const string v2 = "{\"SchemaVersion\":2,\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":40,\"Xp\":0}}]}";
            SaveLoadResult loaded = new SaveSerializer(new JsonSaveSerializer()).Deserialize(v2);
            Assert.IsTrue(loaded.Success, loaded.Error);
            BeastProgress beast = loaded.Save.FindBeast("b1").Progress;

            BeastProgression.AwardBattle(beast, BattleOutcome.PlayerVictory, 40, false, 12);

            Assert.AreEqual(40, beast.Level);
            Assert.AreEqual(BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 40, false), beast.Xp);
        }
    }
}
