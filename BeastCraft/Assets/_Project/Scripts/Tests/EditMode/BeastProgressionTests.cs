using BeastCraft.Battle;
using BeastCraft.Progression;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary><see cref="BeastProgression"/>: the beast XP curve, what a battle pays a fielded beast, and that it paces with the encounters.</summary>
    public class BeastProgressionTests
    {
        [Test]
        public void Curve_IsStrictlyIncreasing_WithPinnedNumbers()
        {
            for (int level = 1; level < BeastProgression.MaxLevel; level++)
            {
                Assert.Greater(BeastProgression.XpToNextLevel(level + 1), BeastProgression.XpToNextLevel(level), "level " + level);
            }

            Assert.AreEqual(173, BeastProgression.XpToNextLevel(1));
            Assert.AreEqual(173, BeastProgression.XpToNextLevel(0));
            Assert.AreEqual(1447, BeastProgression.XpToNextLevel(99));
            Assert.AreEqual(0, BeastProgression.TotalXpToReach(1));
            Assert.AreEqual(173 + 186, BeastProgression.TotalXpToReach(3));
            Assert.AreEqual(BeastProgression.TotalXpToReach(BeastProgression.MaxLevel), BeastProgression.TotalXpToReach(500));
        }

        [Test]
        public void BattleXp_ParticipationForEveryFieldedBeast_ClearBonusForThoseStanding()
        {
            int participation = BeastProgression.ParticipationXp;
            int clearAt10 = BeastProgression.ClearBaseXp + (BeastProgression.ClearXpPerEnemyLevel * 10);

            Assert.AreEqual(participation + clearAt10, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, false));
            Assert.AreEqual(participation, BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 10, true), "knocked out: participation only");
            Assert.AreEqual(participation, BeastProgression.BattleXp(BattleOutcome.EnemyVictory, 10, false));
            Assert.AreEqual(participation, BeastProgression.BattleXp(BattleOutcome.Stalemate, 10, false));
            Assert.AreEqual(BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 1, false), BeastProgression.BattleXp(BattleOutcome.PlayerVictory, 0, false));
        }

        [Test]
        public void AddXp_LevelsThroughSeveralLevels_AndCapsAtMax()
        {
            BeastProgress progress = new BeastProgress("emberfox", 1);

            Assert.AreEqual(2, BeastProgression.AddXp(progress, 173 + 186 + 5));
            Assert.AreEqual(3, progress.Level);
            Assert.AreEqual(5, progress.Xp);

            BeastProgress top = new BeastProgress("emberfox", BeastProgression.MaxLevel - 1);
            Assert.AreEqual(1, BeastProgression.AddXp(top, int.MaxValue));
            Assert.AreEqual(BeastProgression.MaxLevel, top.Level);
            Assert.AreEqual(0, top.Xp);

            BeastProgress odd = new BeastProgress { Level = 0, Xp = -50 };
            Assert.AreEqual(0, BeastProgression.AddXp(odd, -10));
            Assert.AreEqual(1, odd.Level);
            Assert.AreEqual(0, odd.Xp);

            Assert.AreEqual(0, BeastProgression.AwardBattle(null, BattleOutcome.PlayerVictory, 5, false));
        }

        [Test]
        public void Pacing_AFieldedBeastTracksTheEncounterLevel()
        {
            // The pacing campaign made deterministic: 5 battles per encounter level, 4 in 5 cleared,
            // and a knockout pattern that leaves 64% of battles paying the clear bonus (80% cleared x
            // 80% standing) — the pacing model's expectation, without its dice.
            int[] first = Campaign();
            int[] second = Campaign();

            for (int b = 50; b <= 500; b += 50)
            {
                int encounter = System.Math.Min(100, 1 + ((b - 1) / 5));
                Assert.AreEqual(first[b - 1], second[b - 1], "deterministic");
                Assert.LessOrEqual(System.Math.Abs(first[b - 1] - encounter), 3, "battle " + b + ": level " + first[b - 1] + " vs encounter " + encounter);
            }
        }

        private static int[] Campaign()
        {
            BeastProgress beast = new BeastProgress("fielded", 1);
            int[] levels = new int[500];

            for (int i = 0; i < levels.Length; i++)
            {
                int level = System.Math.Min(100, 1 + (i / 5));
                bool cleared = i % 5 != 4;
                bool knockedOut = i % 5 == (i / 5) % 5;
                BeastProgression.AwardBattle(beast, cleared ? BattleOutcome.PlayerVictory : BattleOutcome.EnemyVictory, level, knockedOut);
                levels[i] = beast.Level;
            }

            return levels;
        }
    }
}
