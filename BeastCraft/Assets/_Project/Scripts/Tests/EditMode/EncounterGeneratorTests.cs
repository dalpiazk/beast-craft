using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The runtime <see cref="EncounterGenerator"/> over the authored encounter content: determinism,
    /// the shape rules every draw must keep, the element-scheme semantics, and the scouting preview of
    /// a lineup. The balance simulator draws its default run's compositions through the same class,
    /// so its committed report pins the exact lineups; these tests pin the rules.
    /// </summary>
    public class EncounterGeneratorTests
    {
        private EncounterLibrary _library;
        private EnemyCatalog _enemies;

        [SetUp]
        public void SetUp()
        {
            _library = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary());
            _enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
        }

        /// <summary>
        /// The generator (and every seeded battle) relies on <see cref="Random"/>'s seeded sequence
        /// being the same everywhere the game runs. These are the legacy seeded algorithm's first
        /// draws, as .NET Framework (and Mono, which Unity uses) produce them; modern .NET keeps that
        /// algorithm for seeded instances. If this fails, seeded content differs between the
        /// simulator and the game.
        /// </summary>
        [Test]
        public void SeededRandom_FirstDrawsMatchTheLegacyAlgorithm()
        {
            Random a = new Random(12345);
            Assert.AreEqual(new[] { 66, 70, 774, 511, 797 }, new[] { a.Next(1000), a.Next(1000), a.Next(1000), a.Next(1000), a.Next(1000) });

            Random b = new Random(42);
            Assert.AreEqual(new[] { 1434747710, 302596119, 269548474 }, new[] { b.Next(), b.Next(), b.Next() });
        }

        [Test]
        public void Draw_SameSeedAndSequence_GivesTheSameLineups()
        {
            List<string> first = DrawAll(7);
            List<string> second = DrawAll(7);

            CollectionAssert.AreEqual(first, second);
            CollectionAssert.AreNotEqual(first, DrawAll(8), "a different seed draws different lineups");
        }

        [Test]
        public void Draw_EveryShape_KeepsItsBudgetTypesAndArenaFit()
        {
            foreach (EncounterShapeData shape in _library.Shapes)
            {
                ArenaSize arena = EncounterLibrary.ParseArena(shape.Arena);
                for (int seed = 0; seed < 500; seed++)
                {
                    EncounterLineup lineup = new EncounterGenerator(_library, _enemies, seed).Draw(shape.ShapeId);
                    string where = shape.ShapeId + " seed " + seed;

                    Assert.IsNotNull(lineup, where);
                    Assert.AreEqual(shape.ShapeId, lineup.ShapeId);
                    Assert.AreEqual(arena, lineup.Arena);

                    double threat = 0.0;
                    HashSet<string> types = new HashSet<string>();
                    List<UnitFootprint> footprints = new List<UnitFootprint>();
                    int previousRank = -1;
                    foreach (EncounterLineupEnemy enemy in lineup.Enemies)
                    {
                        threat += _enemies.Get(enemy.EnemyId).Threat;
                        types.Add(enemy.EnemyId);
                        footprints.Add(_enemies.FootprintOf(enemy.EnemyId));

                        int rank = enemy.Stance == CombatStance.Vanguard ? 0 : enemy.Stance == CombatStance.Skirmisher ? 1 : 2;
                        Assert.GreaterOrEqual(rank, previousRank, where + ": front to back by stance");
                        previousRank = rank;
                    }

                    Assert.AreEqual(threat, lineup.Threat, 1e-9, where);
                    Assert.GreaterOrEqual(threat, shape.ThreatMin - 1e-9, where);
                    Assert.LessOrEqual(threat, shape.ThreatMax + 1e-9, where);
                    Assert.GreaterOrEqual(types.Count, shape.MinDistinctTypes, where);
                    Assert.IsTrue(EncounterFit.Fits(arena, footprints), where + ": seats on the arena");
                }
            }
        }

        [Test]
        public void Draw_ElementSchemes_AssignElementsAsNamed()
        {
            HashSet<ElementScheme> seenSchemes = new HashSet<ElementScheme>();
            for (int seed = 0; seed < 200; seed++)
            {
                EncounterLineup lineup = new EncounterGenerator(_library, _enemies, seed).Draw("squad");
                seenSchemes.Add(lineup.ElementScheme);
                Dictionary<string, Element> byType = new Dictionary<string, Element>();

                foreach (EncounterLineupEnemy enemy in lineup.Enemies)
                {
                    switch (lineup.ElementScheme)
                    {
                        case ElementScheme.None:
                            Assert.AreEqual(Element.None, enemy.Element);
                            break;
                        case ElementScheme.Uniform:
                            Assert.AreNotEqual(Element.None, enemy.Element);
                            Assert.AreEqual(lineup.Enemies[0].Element, enemy.Element);
                            break;
                        case ElementScheme.PerType:
                            Assert.AreNotEqual(Element.None, enemy.Element);
                            if (byType.TryGetValue(enemy.EnemyId, out Element typeElement))
                            {
                                Assert.AreEqual(typeElement, enemy.Element, "units of a type share its element");
                            }

                            byType[enemy.EnemyId] = enemy.Element;
                            break;
                        default:
                            Assert.AreNotEqual(Element.None, enemy.Element);
                            break;
                    }
                }
            }

            Assert.AreEqual(4, seenSchemes.Count, "every weighted scheme comes up over 200 draws");
        }

        [Test]
        public void Draw_ElementDeck_DealsAllTenBeforeRepeating()
        {
            // A fresh generator's first per-unit lineup starts on a fresh deck, so its first ten
            // units (a horde has 16 or more) hold every element exactly once.
            int checkedLineups = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                EncounterLineup lineup = new EncounterGenerator(_library, _enemies, seed).Draw("horde");
                if (lineup.ElementScheme != ElementScheme.PerUnit)
                {
                    continue;
                }

                HashSet<Element> firstTen = new HashSet<Element>();
                for (int i = 0; i < 10; i++)
                {
                    firstTen.Add(lineup.Enemies[i].Element);
                }

                Assert.AreEqual(10, firstTen.Count, "seed " + seed);
                checkedLineups++;
            }

            Assert.Greater(checkedLineups, 0);
        }

        [Test]
        public void Draw_WithASeenSet_AvoidsRepeats()
        {
            EncounterGenerator generator = new EncounterGenerator(_library, _enemies, 11);
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < 8; i++)
            {
                Assert.IsNotNull(generator.Draw("squad", seen));
            }

            Assert.AreEqual(8, seen.Count, "eight distinct squads");
        }

        [Test]
        public void Draw_UnknownShape_IsNull()
        {
            Assert.IsNull(new EncounterGenerator(_library, _enemies, 1).Draw("nowhere"));
            Assert.IsNull(new EncounterGenerator(_library, _enemies, 1).Draw(null));
        }

        [Test]
        public void Lineup_PreviewsGroupedByTypeAndElement()
        {
            EncounterLineup lineup = new EncounterGenerator(_library, _enemies, 5).Draw("horde");
            EncounterPreview preview = EncounterPreview.Build(lineup.Enemies, lineup.Arena);

            Assert.AreEqual(lineup.Enemies.Count, preview.TotalEnemies);
            Assert.AreEqual(lineup.Arena, preview.Arena);

            int counted = 0;
            foreach (EncounterPreviewGroup group in preview.Groups)
            {
                counted += group.Count;
                int matching = 0;
                foreach (EncounterLineupEnemy enemy in lineup.Enemies)
                {
                    matching += enemy.DisplayName == group.DisplayName && enemy.Element == group.Element ? 1 : 0;
                }

                Assert.AreEqual(matching, group.Count, group.DisplayName + " " + group.Element);
            }

            Assert.AreEqual(lineup.Enemies.Count, counted);
        }

        private List<string> DrawAll(int seed)
        {
            EncounterGenerator generator = new EncounterGenerator(_library, _enemies, seed);
            List<string> lineups = new List<string>();
            foreach (EncounterShapeData shape in _library.Shapes)
            {
                HashSet<string> seen = new HashSet<string>();
                for (int i = 0; i < 3; i++)
                {
                    EncounterLineup lineup = generator.Draw(shape.ShapeId, seen);
                    List<string> parts = new List<string> { lineup.ShapeId, lineup.ElementScheme.ToString(), lineup.Threat.ToString("R") };
                    foreach (EncounterLineupEnemy enemy in lineup.Enemies)
                    {
                        parts.Add(enemy.EnemyId + ":" + enemy.Element);
                    }

                    lineups.Add(string.Join(" ", parts));
                }
            }

            return lineups;
        }
    }
}
