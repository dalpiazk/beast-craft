using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The arena geometry (<see cref="HexGrid"/>): each preset is an odd-r offset rectangle of
    /// pointy-top hexes (Small 5 x 7, Medium 8 x 11, Large 11 x 15), its bounds and tile set agree,
    /// neighbours stop cleanly at the zigzag edges, distance is symmetric and the board is its own
    /// mirror image top to bottom, the deployment zones are the bottom / top rows, and everything the
    /// encounter content fields (large footprints, the biggest horde, every template and boss,
    /// DRAFT and post-game ones included) seats on its arena.
    /// </summary>
    public class HexGridTests
    {
        private static readonly ArenaSize[] Sizes = { ArenaSize.Small, ArenaSize.Medium, ArenaSize.Large };

        [TestCase(ArenaSize.Small, 5, 7, 35)]
        [TestCase(ArenaSize.Medium, 8, 11, 88)]
        [TestCase(ArenaSize.Large, 11, 15, 165)]
        public void EachPreset_IsItsRectangle(ArenaSize size, int width, int height, int cells)
        {
            HexGrid grid = new HexGrid(size);

            Assert.AreEqual(width, grid.Width);
            Assert.AreEqual(height, grid.Height);
            Assert.AreEqual(cells, grid.TileCount);
            Assert.AreEqual(cells, grid.TileIndexCapacity, "a rectangle has no holes in its index space");
            Assert.AreEqual(cells, new HashSet<HexCoordinate>(grid.Tiles).Count);
            Assert.AreEqual(-(height - 1) / 2, grid.MinRow);
            Assert.AreEqual((height - 1) / 2, grid.MaxRow, "centred on the R = 0 row");
            Assert.AreEqual(-(width / 2), grid.MinColumn);
            Assert.AreEqual(grid.MinColumn + width - 1, grid.MaxColumn);

            HexGrid.DimensionsFor(size, out int w, out int h);
            Assert.AreEqual((width, height), (w, h));
        }

        [Test]
        public void Bounds_AreExactlyTheTileSet_EveryRowWidthTiles_IndicesDense()
        {
            foreach (ArenaSize size in Sizes)
            {
                HexGrid grid = new HexGrid(size);
                HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>(grid.Tiles);
                Dictionary<int, int> perRow = new Dictionary<int, int>();
                HashSet<int> indices = new HashSet<int>();

                for (int q = -20; q <= 20; q++)
                {
                    for (int r = -20; r <= 20; r++)
                    {
                        HexCoordinate tile = new HexCoordinate(q, r);
                        bool inBounds = grid.IsInBounds(tile);
                        Assert.AreEqual(tiles.Contains(tile), inBounds, size + " " + tile);

                        int column = HexGrid.ColumnOf(tile);
                        bool inRectangle = r >= grid.MinRow && r <= grid.MaxRow && column >= grid.MinColumn && column <= grid.MaxColumn;
                        Assert.AreEqual(inRectangle, inBounds, size + " " + tile);
                        Assert.AreEqual(tile, HexGrid.FromOffset(column, r), "offset round trip");

                        int index = grid.TileIndex(tile);
                        if (!inBounds)
                        {
                            Assert.AreEqual(-1, index);
                            continue;
                        }

                        Assert.That(index, Is.InRange(0, grid.TileIndexCapacity - 1));
                        Assert.IsTrue(indices.Add(index), "two tiles share index " + index);
                        perRow[r] = perRow.TryGetValue(r, out int n) ? n + 1 : 1;
                    }
                }

                Assert.AreEqual(grid.Height, perRow.Count);
                foreach (KeyValuePair<int, int> row in perRow)
                {
                    Assert.AreEqual(grid.Width, row.Value, size + " row " + row.Key);
                }
            }
        }

        [Test]
        public void Offset_OddRowsSitHalfATileRight_CentreRowIsQ()
        {
            Assert.AreEqual(0, HexGrid.ColumnOf(HexCoordinate.Zero));
            Assert.AreEqual(new HexCoordinate(3, 0), HexGrid.FromOffset(3, 0), "Q runs along the centre row");
            Assert.AreEqual(new HexCoordinate(0, 1), HexGrid.FromOffset(0, 1), "odd row 1: column 0 is (0, 1), half a tile right of (0, 0)");
            Assert.AreEqual(new HexCoordinate(1, -1), HexGrid.FromOffset(0, -1), "odd row -1: column 0 is (1, -1), also half a tile right");
            Assert.AreEqual(new HexCoordinate(-1, 2), HexGrid.FromOffset(0, 2), "even row 2: straight below (0, 0)");
            Assert.AreEqual(new HexCoordinate(2, -3), HexGrid.FromOffset(0, -3));
        }

        [Test]
        public void Neighbours_AtTheEdges_StopCleanly_OnEvenAndOddRows()
        {
            foreach (ArenaSize size in Sizes)
            {
                HexGrid grid = new HexGrid(size);
                for (int r = grid.MinRow + 1; r < grid.MaxRow; r++)
                {
                    bool odd = (r & 1) != 0;
                    HexCoordinate left = HexGrid.FromOffset(grid.MinColumn, r);
                    HexCoordinate right = HexGrid.FromOffset(grid.MaxColumn, r);

                    // An even row's left end and an odd row's right end are the outermost tiles:
                    // they lose their three outward neighbours. The other ends are half a tile in,
                    // so the rows above and below still reach past them and only one is lost.
                    Assert.AreEqual(odd ? 5 : 3, InBoundsNeighbours(grid, left), size + " left end of row " + r);
                    Assert.AreEqual(odd ? 3 : 5, InBoundsNeighbours(grid, right), size + " right end of row " + r);
                    Assert.IsFalse(grid.IsInBounds(left + new HexCoordinate(-1, 0)));
                    Assert.IsFalse(grid.IsInBounds(right + new HexCoordinate(1, 0)));
                }

                // The top and bottom rows lose their neighbours above / below; corners lose more.
                HexCoordinate topMiddle = HexGrid.FromOffset(0, grid.MinRow);
                Assert.AreEqual(4, InBoundsNeighbours(grid, topMiddle), size + " top row");
                Assert.AreEqual(4, InBoundsNeighbours(grid, HexGrid.FromOffset(0, grid.MaxRow)), size + " bottom row");

                // Every interior tile keeps all six.
                foreach (HexCoordinate tile in grid.Tiles)
                {
                    int column = HexGrid.ColumnOf(tile);
                    if (tile.R > grid.MinRow && tile.R < grid.MaxRow && column > grid.MinColumn && column < grid.MaxColumn)
                    {
                        Assert.AreEqual(6, InBoundsNeighbours(grid, tile), size + " " + tile);
                    }
                }
            }
        }

        [Test]
        public void Distance_IsSymmetric_TheMirrorIsASymmetry_AndEmptyBoardPathsAreHexDistance()
        {
            foreach (ArenaSize size in Sizes)
            {
                HexGrid grid = new HexGrid(size);
                List<HexCoordinate> tiles = new List<HexCoordinate>(grid.Tiles);

                foreach (HexCoordinate a in tiles)
                {
                    HexCoordinate mirrorA = Mirror(a);
                    Assert.IsTrue(grid.IsInBounds(mirrorA), size + ": " + a + " mirrors onto the board");
                    Assert.AreEqual(a, Mirror(mirrorA));

                    Dictionary<HexCoordinate, int> steps = Walk(grid, a);
                    Assert.AreEqual(tiles.Count, steps.Count, "every tile reachable from " + a);

                    foreach (HexCoordinate b in tiles)
                    {
                        Assert.AreEqual(a.Distance(b), b.Distance(a));
                        Assert.AreEqual(a.Distance(b), Mirror(a).Distance(Mirror(b)), "the mirror keeps distance");
                        Assert.AreEqual(a.Distance(b), steps[b], size + ": a shortest path " + a + " -> " + b + " stays on the board");
                    }
                }

                // The pathfinder agrees, corner to corner.
                HexCoordinate topLeft = HexGrid.FromOffset(grid.MinColumn, grid.MinRow);
                HexCoordinate bottomRight = HexGrid.FromOffset(grid.MaxColumn, grid.MaxRow);
                IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, topLeft, bottomRight, "x");
                Assert.AreEqual(topLeft.Distance(bottomRight) + 1, path.Count, size.ToString());
            }
        }

        [Test]
        public void EveryStraightLine_CrossesTheBoardInOnePiece()
        {
            // What lets a Line or Cross arm stop at the first off-board tile (SkillTargetResolver).
            foreach (ArenaSize size in Sizes)
            {
                HexGrid grid = new HexGrid(size);
                foreach (HexCoordinate start in grid.Tiles)
                {
                    foreach (HexCoordinate direction in HexCoordinate.AxialDirections)
                    {
                        bool left = false;
                        for (int step = 1; step <= 40; step++)
                        {
                            bool inBounds = grid.IsInBounds(start + new HexCoordinate(direction.Q * step, direction.R * step));
                            Assert.IsFalse(left && inBounds, size + ": the line from " + start + " along " + direction + " re-enters at step " + step);
                            left |= !inBounds;
                        }
                    }
                }
            }
        }

        [Test]
        public void RangeQuery_IsTheDiscClippedToTheRectangle()
        {
            HexGrid grid = new HexGrid(ArenaSize.Medium);
            HexCoordinate corner = HexGrid.FromOffset(grid.MinColumn, grid.MaxRow);

            IReadOnlyList<HexCoordinate> range = grid.GetTilesInRange(corner, 2);
            HashSet<HexCoordinate> expected = new HashSet<HexCoordinate>();
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (tile.Distance(corner) <= 2)
                {
                    expected.Add(tile);
                }
            }

            CollectionAssert.AreEquivalent(expected, range);
            Assert.Less(range.Count, 19, "a corner sees less than a full radius-2 disc");
        }

        [TestCase(ArenaSize.Small, 2, 10)]
        [TestCase(ArenaSize.Medium, 3, 24)]
        [TestCase(ArenaSize.Large, 4, 44)]
        public void DeploymentZones_AreTheBottomAndTopRows_MirrorImages(ArenaSize size, int depth, int tilesPerSide)
        {
            HexGrid grid = new HexGrid(size);
            IReadOnlyList<HexCoordinate> player = grid.GetDeploymentZone(BattleTeam.Player);
            IReadOnlyList<HexCoordinate> enemy = grid.GetDeploymentZone(BattleTeam.Enemy);

            Assert.AreEqual(depth, grid.DeploymentZoneDepth);
            Assert.AreEqual(tilesPerSide, player.Count);
            Assert.AreEqual(tilesPerSide, enemy.Count);

            foreach (HexCoordinate tile in grid.Tiles)
            {
                bool bottom = tile.R > grid.MaxRow - depth;
                bool top = tile.R < grid.MinRow + depth;
                Assert.AreEqual(bottom, grid.IsInDeploymentZone(tile, BattleTeam.Player), tile.ToString());
                Assert.AreEqual(top, grid.IsInDeploymentZone(tile, BattleTeam.Enemy), tile.ToString());
                Assert.AreEqual(grid.IsInDeploymentZone(tile, BattleTeam.Player), grid.IsInDeploymentZone(Mirror(tile), BattleTeam.Enemy), "mirror images");
            }

            HashSet<int> neutralRows = new HashSet<int>();
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (!grid.IsInDeploymentZone(tile, BattleTeam.Player) && !grid.IsInDeploymentZone(tile, BattleTeam.Enemy))
                {
                    neutralRows.Add(tile.R);
                }
            }

            Assert.AreEqual(grid.Height - 2 * depth, neutralRows.Count, "the neutral band between the zones");
            Assert.AreEqual(2 * depth - 1, neutralRows.Count, "3 / 5 / 7 rows, as on the hexagons");

            for (int i = 1; i < player.Count; i++)
            {
                Assert.IsTrue(player[i - 1].R < player[i].R || (player[i - 1].R == player[i].R && player[i - 1].Q < player[i].Q), "row by row, left to right");
            }
        }

        [Test]
        public void FrontOrder_FrontRowFirst_CentreOutByColumn()
        {
            foreach (ArenaSize size in Sizes)
            {
                HexGrid grid = new HexGrid(size);
                foreach (BattleTeam team in new[] { BattleTeam.Player, BattleTeam.Enemy })
                {
                    List<HexCoordinate> order = DeploymentPacker.FrontOrder(grid, team);
                    int front = team == BattleTeam.Player ? grid.MaxRow - grid.DeploymentZoneDepth + 1 : grid.MinRow + grid.DeploymentZoneDepth - 1;

                    for (int i = 0; i < grid.Width; i++)
                    {
                        Assert.AreEqual(front, order[i].R, size + " " + team + ": the front row first");
                    }

                    // The front row is symmetric about the board's vertical centre line: its first
                    // tile is on it (odd widths) or half a tile beside it (even widths).
                    Assert.LessOrEqual(Math.Abs((2 * order[0].Q) + order[0].R), 1, size + " " + team);
                    int leftmost = int.MaxValue;
                    int rightmost = int.MinValue;
                    for (int i = 0; i < grid.Width; i++)
                    {
                        leftmost = Math.Min(leftmost, (2 * order[i].Q) + order[i].R);
                        rightmost = Math.Max(rightmost, (2 * order[i].Q) + order[i].R);
                    }

                    Assert.AreEqual(-leftmost, rightmost, size + " " + team + ": the front row is centred");

                    for (int i = 1; i < order.Count; i++)
                    {
                        int rowA = Math.Abs(order[i - 1].R);
                        int rowB = Math.Abs(order[i].R);
                        Assert.IsTrue(rowA < rowB || (rowA == rowB && Math.Abs((2 * order[i - 1].Q) + order[i - 1].R) <= Math.Abs((2 * order[i].Q) + order[i].R)),
                                      size + " " + team + " at " + i);
                    }
                }
            }
        }

        [Test]
        public void LargeFootprints_FitWhereTheirRowsAllow()
        {
            foreach (ArenaSize size in Sizes)
            {
                Assert.IsTrue(EncounterFit.Fits(size, new[] { UnitFootprint.Triangle }), size + ": a champion fits every arena");
                Assert.IsTrue(EncounterFit.Fits(size, new[] { UnitFootprint.Triangle, UnitFootprint.Triangle }), size + ": two champions too");

                // A giant is three rows tall: Small's two-row zone cannot hold one (as before).
                Assert.AreEqual(size != ArenaSize.Small, EncounterFit.Fits(size, new[] { UnitFootprint.Hex7 }), size + ": giant");
                Assert.AreEqual(size != ArenaSize.Small, EncounterFit.Fits(size, new[] { UnitFootprint.Hex7, UnitFootprint.Single, UnitFootprint.Single }), size + ": giant + escort");

                HexGrid grid = new HexGrid(size);
                foreach (BattleTeam team in new[] { BattleTeam.Player, BattleTeam.Enemy })
                {
                    List<HexCoordinate> anchors = new List<HexCoordinate>();
                    List<HexCoordinate> covered = new List<HexCoordinate>();
                    Assert.IsTrue(DeploymentPacker.TryPack(grid, team, new[] { UnitFootprint.Triangle }, anchors, covered));
                    foreach (HexCoordinate tile in covered)
                    {
                        Assert.IsTrue(grid.IsInDeploymentZone(tile, team));
                    }
                }
            }

            Assert.IsTrue(EncounterFit.Fits(ArenaSize.Large, new[] { UnitFootprint.Hex7, UnitFootprint.Hex7, UnitFootprint.Hex7 }), "three giants on Large");
        }

        [Test]
        public void TheLargestHorde_TwentySwarmAndFourRanged_FitsLarge()
        {
            List<UnitFootprint> horde = new List<UnitFootprint>();
            for (int i = 0; i < 24; i++)
            {
                horde.Add(UnitFootprint.Single);
            }

            Assert.IsTrue(EncounterFit.Fits(ArenaSize.Large, horde), "44 tiles for 24");

            // And the authored horde shapes really are that size, on Large.
            EncounterLibraryData library = EncounterContentTests.LoadEncounterLibrary();
            int hordes = 0;
            foreach (EncounterShapeData shape in library.Shapes)
            {
                if (shape.ShapeId != "horde" && shape.DropShapeId != "horde")
                {
                    continue;
                }

                hordes++;
                Assert.AreEqual("Large", shape.Arena, shape.ShapeId);
                int max = 0;
                foreach (EncounterSlotData slot in shape.Variants[0].Slots)
                {
                    max += slot.Max;
                }

                Assert.AreEqual(24, max, shape.ShapeId + ": 20 swarm + 4 ranged");
            }

            Assert.GreaterOrEqual(hordes, 3, "horde, horde_postgame, horde_postgame_hard");
        }

        [Test]
        public void EveryAuthoredShapeAndTemplate_SeatsOnItsArena()
        {
            EncounterLibraryData library = EncounterContentTests.LoadEncounterLibrary();
            Dictionary<string, EnemyData> enemies = new Dictionary<string, EnemyData>(StringComparer.Ordinal);
            List<string> libraryOrder = new List<string>();
            foreach (EnemyData enemy in EncounterContentTests.LoadEnemyLibrary().Enemies)
            {
                enemies[enemy.EnemyId] = enemy;
                libraryOrder.Add(enemy.EnemyId);
            }

            // Shapes, worst case: every slot at its Max, each unit its slot's largest type, placed
            // in the generator's order. (EncounterLibraryValidator checks every count; this is the
            // biggest draw, stated directly.)
            foreach (EncounterShapeData shape in library.Shapes)
            {
                ArenaSize arena = ParseArena(shape.Arena);
                foreach (EncounterVariantData variant in shape.Variants)
                {
                    List<(UnitFootprint Footprint, CombatStance Stance, int Index)> units = new List<(UnitFootprint, CombatStance, int)>();
                    foreach (EncounterSlotData slot in variant.Slots)
                    {
                        EnemyData largest = null;
                        foreach (string type in slot.Types)
                        {
                            if (largest == null || Footprints.TileCount(FootprintOf(enemies[type])) > Footprints.TileCount(FootprintOf(largest)))
                            {
                                largest = enemies[type];
                            }
                        }

                        for (int i = 0; i < slot.Max; i++)
                        {
                            units.Add((FootprintOf(largest), StanceOf(largest), libraryOrder.IndexOf(largest.EnemyId)));
                        }
                    }

                    units.Sort((a, b) => EncounterFit.ComparePlacement(a.Stance, a.Index, b.Stance, b.Index));
                    List<UnitFootprint> lineup = units.ConvertAll(u => u.Footprint);
                    Assert.IsTrue(EncounterFit.Fits(arena, lineup), shape.ShapeId + " '" + variant.Label + "' (" + lineup.Count + " enemies) on " + arena);
                }
            }

            // Templates: every boss, DRAFT and post-game (Normal and Hard) alike.
            int templates = 0;
            int drafts = 0;
            int postGame = 0;
            foreach (EncounterTemplateData template in library.Templates)
            {
                List<UnitFootprint> lineup = new List<UnitFootprint>();
                foreach (EncounterGroupData group in template.Groups)
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        lineup.Add(FootprintOf(enemies[group.EnemyId]));
                    }
                }

                Assert.IsTrue(EncounterFit.Fits(ParseArena(template.Arena), lineup), template.EncounterId + " on " + template.Arena);
                templates++;
                drafts += template.Draft ? 1 : 0;
                postGame += template.EncounterId.StartsWith("boss_r11", StringComparison.Ordinal) ? 1 : 0;
            }

            Assert.GreaterOrEqual(templates, 12, "ten mainline bosses and the post-game twins on Normal and Hard");
            Assert.Greater(drafts, 0);
            Assert.AreEqual(2, postGame);
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>The top-to-bottom reflection: same screen column, mirrored row.</summary>
        private static HexCoordinate Mirror(HexCoordinate tile)
        {
            return new HexCoordinate(tile.Q + tile.R, -tile.R);
        }

        private static int InBoundsNeighbours(HexGrid grid, HexCoordinate tile)
        {
            int count = 0;
            foreach (HexCoordinate direction in HexCoordinate.AxialDirections)
            {
                count += grid.IsInBounds(tile + direction) ? 1 : 0;
            }

            return count;
        }

        /// <summary>Breadth-first steps from <paramref name="start"/> over the board's tiles only.</summary>
        private static Dictionary<HexCoordinate, int> Walk(HexGrid grid, HexCoordinate start)
        {
            Dictionary<HexCoordinate, int> steps = new Dictionary<HexCoordinate, int> { { start, 0 } };
            Queue<HexCoordinate> frontier = new Queue<HexCoordinate>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                HexCoordinate at = frontier.Dequeue();
                foreach (HexCoordinate direction in HexCoordinate.AxialDirections)
                {
                    HexCoordinate next = at + direction;
                    if (grid.IsInBounds(next) && !steps.ContainsKey(next))
                    {
                        steps.Add(next, steps[at] + 1);
                        frontier.Enqueue(next);
                    }
                }
            }

            return steps;
        }

        private static ArenaSize ParseArena(string arena)
        {
            Assert.IsTrue(Enum.TryParse(arena, out ArenaSize size), arena);
            return size;
        }

        private static UnitFootprint FootprintOf(EnemyData enemy)
        {
            EnemyLibraryValidator.TryParseFootprint(enemy.Footprint, out UnitFootprint footprint);
            return footprint;
        }

        private static CombatStance StanceOf(EnemyData enemy)
        {
            BeastRosterValidator.TryParseStance(enemy.Stance, out CombatStance stance);
            return stance;
        }
    }
}
