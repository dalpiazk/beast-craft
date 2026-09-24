using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The avatar's own level: <see cref="AvatarProgression"/>'s curve and battle awards, and
    /// <see cref="AvatarStatsSO"/> scaling its base by level.
    /// </summary>
    public class AvatarProgressionTests
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
        public void Curve_IsStrictlyIncreasing_WithPinnedNumbers()
        {
            for (int level = 1; level < AvatarProgression.MaxLevel; level++)
            {
                Assert.Greater(AvatarProgression.XpToNextLevel(level + 1), AvatarProgression.XpToNextLevel(level), "level " + level);
            }

            Assert.AreEqual(216, AvatarProgression.XpToNextLevel(1));
            Assert.AreEqual(216, AvatarProgression.XpToNextLevel(0));
            Assert.AreEqual(1784, AvatarProgression.XpToNextLevel(99));
            Assert.AreEqual(0, AvatarProgression.TotalXpToReach(1));
            Assert.AreEqual(216 + 232, AvatarProgression.TotalXpToReach(3));
            Assert.AreEqual(AvatarProgression.TotalXpToReach(100), AvatarProgression.TotalXpToReach(150), "clamped to the max level");
        }

        [Test]
        public void BattleXp_PaysParticipationAlways_AndTheClearBonusOnlyOnAWin()
        {
            Assert.AreEqual(8, AvatarProgression.BattleXp(BattleOutcome.EnemyVictory, 50));
            Assert.AreEqual(8, AvatarProgression.BattleXp(BattleOutcome.Stalemate, 50));
            Assert.AreEqual(8, AvatarProgression.BattleXp(BattleOutcome.MutualDefeat, 50));
            Assert.AreEqual(8 + 50 + 250, AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, 50));
            Assert.AreEqual(8 + 50 + 5, AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, 0), "enemy level read as at least 1");
        }

        [Test]
        public void AwardBattle_LevelsUpAndCarriesTheRemainder()
        {
            AvatarProgress progress = new AvatarProgress();

            int gained = AvatarProgression.AddXp(progress, 216 + 232 + 10);

            Assert.AreEqual(2, gained);
            Assert.AreEqual(3, progress.Level);
            Assert.AreEqual(10, progress.Xp);

            Assert.AreEqual(0, AvatarProgression.AwardBattle(progress, BattleOutcome.EnemyVictory, 3));
            Assert.AreEqual(18, progress.Xp);
            Assert.AreEqual(0, AvatarProgression.AddXp(progress, -50), "negative XP adds nothing");
            Assert.AreEqual(18, progress.Xp);
            Assert.AreEqual(0, AvatarProgression.AddXp(null, 100));
        }

        [Test]
        public void MaxLevel_HoldsXpAtZero_AndNormalizesBadSaves()
        {
            AvatarProgress progress = new AvatarProgress();
            AvatarProgression.AddXp(progress, int.MaxValue);
            Assert.AreEqual(AvatarProgression.MaxLevel, progress.Level);
            Assert.AreEqual(0, progress.Xp);

            AvatarProgress broken = new AvatarProgress { Level = -4, Xp = -9 };
            AvatarProgression.AddXp(broken, 0);
            Assert.AreEqual(1, broken.Level);
            Assert.AreEqual(0, broken.Xp);
        }

        [Test]
        public void EveryLevelAlongsideTheEncounters_TakesAboutFourAndAHalfBattlesAtTheCampaignsClearRate()
        {
            // The region campaign clears about 70% of its battles (80% squads, harder elites, gates and bosses).
            foreach (int level in new[] { 1, 25, 50, 99 })
            {
                double perBattle = AvatarProgression.ParticipationXp +
                                   (0.7 * (AvatarProgression.ClearBaseXp + (AvatarProgression.ClearXpPerEnemyLevel * level)));
                Assert.That(AvatarProgression.XpToNextLevel(level) / perBattle, Is.InRange(4.3, 5.0), "level " + level);
            }
        }

        [Test]
        public void StatsAtLevel_ScaleByTheGrowthCurve_ExceptMoveAndCrit()
        {
            AvatarStatsSO stats = ScriptableObject.CreateInstance<AvatarStatsSO>();
            GrowthRateCurve growth = ScriptableObject.CreateInstance<GrowthRateCurve>();
            _created.Add(stats);
            _created.Add(growth);
            growth.Curve = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            growth.MaxLevel = 100;
            stats.BaseStats = new StatBlock(500, 100, 80, 120, 90, 100, 3, 5);
            stats.Growth = growth;

            StatBlock atMax = stats.GetStatsAtLevel(100);
            StatBlock atOne = stats.GetStatsAtLevel(1);

            Assert.AreEqual(stats.BaseStats.Hp, atMax.Hp, "max level is the authored block");
            Assert.AreEqual(100, atMax.Speed);
            Assert.AreEqual(100, atOne.Hp);
            Assert.AreEqual(20, atOne.Speed, "Speed scales");
            Assert.AreEqual(24, atOne.SpecialAttack);
            Assert.AreEqual(3, atOne.MoveRange, "MoveRange is exempt");
            Assert.AreEqual(5, atOne.CritChance, "CritChance is exempt");
            Assert.AreEqual(stats.GetStatAtLevel(StatType.Defense, 50), stats.GetStatsAtLevel(50).Defense);
            Assert.Less(stats.GetStatAtLevel(StatType.Attack, 30), stats.GetStatAtLevel(StatType.Attack, 60));
        }

        [Test]
        public void StatsAtLevel_WithoutGrowth_IsTheFlatBlock()
        {
            AvatarStatsSO stats = ScriptableObject.CreateInstance<AvatarStatsSO>();
            _created.Add(stats);
            stats.BaseStats = new StatBlock(500, 100, 80, 120, 90, 100, 3, 5);

            Assert.AreEqual(stats.BaseStats.ToString(), stats.GetStatsAtLevel(1).ToString());
            Assert.AreEqual(stats.BaseStats.ToString(), stats.GetStatsAtLevel(77).ToString());
        }
    }
}
