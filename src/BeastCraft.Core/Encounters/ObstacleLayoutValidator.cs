using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// Checks <c>battle-layouts.json</c> (<see cref="BattleLayoutData"/>), every layout on its own
    /// arena's board: a known region (<c>regions.json</c>) and arena, a well-formed ArtKey used once,
    /// (RegionId, ArtKey, Arena) unique; its cells on the board, each once, and in neither deployment
    /// zone (both sides deploy exactly as on an open board); the density cap of its arena
    /// (<see cref="MinCells"/>-<see cref="MaxCells"/>); balanced-count fairness (as many cells on the
    /// enemy half as on the player half, and left as right of the board's vertical centre line, each
    /// to within one); connectivity (the free tiles form one region, so no pocket is walled off from
    /// either side); and encounter fit (every authored template of that arena, bosses and post-game
    /// ones included, still seats on the obstructed board: <see cref="EncounterFit.Fits(HexGrid, IReadOnlyList{UnitFootprint})"/>).
    /// Returns every problem found (empty = valid); never throws.
    /// </summary>
    public static class ObstacleLayoutValidator
    {
        /// <summary>The fewest obstacles a layout of <paramref name="arena"/> has: Small 0, Medium 2, Large 3.</summary>
        public static int MinCells(ArenaSize arena)
        {
            return arena == ArenaSize.Small ? 0 : arena == ArenaSize.Medium ? 2 : 3;
        }

        /// <summary>The most obstacles a layout of <paramref name="arena"/> has: Small 2, Medium 5, Large 8.</summary>
        public static int MaxCells(ArenaSize arena)
        {
            return arena == ArenaSize.Small ? 2 : arena == ArenaSize.Medium ? 5 : 8;
        }

        /// <summary>
        /// Validates <paramref name="data"/>. <paramref name="regions"/> (null skips the check) holds the
        /// region ids; <paramref name="encounters"/> and <paramref name="enemies"/> (either null skips
        /// the check) the templates that must still fit.
        /// </summary>
        public static List<string> Validate(BattleLayoutData data, RegionLibraryData regions, EncounterLibraryData encounters, EnemyLibraryData enemies)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No battle layout data.");
                return errors;
            }

            if (data.SchemaVersion != BattleLayoutData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + BattleLayoutData.CurrentSchemaVersion + ".");
            }

            HashSet<string> artKeys = new HashSet<string>(StringComparer.Ordinal);
            BattleLayoutEntryData[] layouts = data.Layouts ?? new BattleLayoutEntryData[0];
            for (int i = 0; i < layouts.Length; i++)
            {
                BattleLayoutEntryData layout = layouts[i];
                if (layout == null)
                {
                    errors.Add("Layouts[" + i + "] is null.");
                    continue;
                }

                string at = "Layout '" + layout.ArtKey + "' (" + layout.RegionId + ", " + layout.Arena + ")";
                if (regions != null && !Array.Exists(regions.Regions ?? new RegionData[0], r => r != null && r.RegionId == layout.RegionId))
                {
                    errors.Add(at + ": RegionId '" + layout.RegionId + "' is not a region.");
                }

                if (!Vfx.ArtReferenceValidator.IsWellFormed(layout.ArtKey))
                {
                    errors.Add(at + ": ArtKey is not lowercase snake_case segments joined by '/'.");
                }
                else if (!artKeys.Add(layout.ArtKey))
                {
                    errors.Add(at + ": ArtKey is listed twice (a painted backdrop has one layout).");
                }

                if (!TryParseArena(layout.Arena, out ArenaSize arena))
                {
                    errors.Add(at + ": Arena '" + layout.Arena + "' is not Small, Medium or Large.");
                    continue;
                }

                ValidateCells(layout, arena, at, encounters, enemies, errors);
            }

            return errors;
        }

        /// <summary>The rules on one layout's cells, on its arena's board.</summary>
        private static void ValidateCells(BattleLayoutEntryData layout, ArenaSize arena, string at, EncounterLibraryData encounters, EnemyLibraryData enemies,
                                          List<string> errors)
        {
            HexGrid grid = new HexGrid(arena);
            List<HexCoordinate> cells = layout.Coordinates();
            HashSet<HexCoordinate> seen = new HashSet<HexCoordinate>();
            bool onBoard = true;
            foreach (HexCoordinate cell in cells)
            {
                if (!grid.IsInBounds(cell))
                {
                    errors.Add(at + ": cell " + cell + " is off the " + arena + " board.");
                    onBoard = false;
                    continue;
                }

                if (!seen.Add(cell))
                {
                    errors.Add(at + ": cell " + cell + " is listed twice.");
                }

                if (grid.IsInDeploymentZone(cell, BattleTeam.Player) || grid.IsInDeploymentZone(cell, BattleTeam.Enemy))
                {
                    errors.Add(at + ": cell " + cell + " is in a deployment zone (obstacles stay in the neutral band).");
                }

                grid.SetBlocked(cell, true);
            }

            if (seen.Count < MinCells(arena) || seen.Count > MaxCells(arena))
            {
                errors.Add(at + ": " + seen.Count + " obstacles; a " + arena + " layout has " + MinCells(arena) + "-" + MaxCells(arena) + ".");
            }

            if (!onBoard)
            {
                return;
            }

            int enemyHalf = 0;
            int playerHalf = 0;
            int left = 0;
            int right = 0;
            foreach (HexCoordinate cell in seen)
            {
                enemyHalf += cell.R < 0 ? 1 : 0;
                playerHalf += cell.R > 0 ? 1 : 0;
                int side = Side(grid, cell);
                left += side < 0 ? 1 : 0;
                right += side > 0 ? 1 : 0;
            }

            if (Math.Abs(enemyHalf - playerHalf) > 1)
            {
                errors.Add(at + ": " + enemyHalf + " obstacles on the enemy half and " + playerHalf + " on the player half; fairness allows a difference of one.");
            }

            if (Math.Abs(left - right) > 1)
            {
                errors.Add(at + ": " + left + " obstacles left of the centre line and " + right + " right of it; fairness allows a difference of one.");
            }

            int free = CountFree(grid);
            int reached = Reachable(grid);
            if (reached != free)
            {
                errors.Add(at + ": the obstacles wall off " + (free - reached) + " of the " + free + " free tiles; the free board must be one connected region.");
            }

            if (encounters != null && enemies != null)
            {
                foreach (EncounterTemplateData template in encounters.Templates ?? new EncounterTemplateData[0])
                {
                    if (template == null || EncounterLibrary.ParseArena(template.Arena) != arena)
                    {
                        continue;
                    }

                    List<UnitFootprint> footprints = Footprints(template, enemies);
                    if (!EncounterFit.Fits(grid, footprints))
                    {
                        errors.Add(at + ": template '" + template.EncounterId + "' (" + footprints.Count + " enemies) no longer fits the enemy deployment zone.");
                    }
                }
            }
        }

        /// <summary>
        /// Which side of the board's vertical centre line <paramref name="cell"/> is on: -1 left, 1
        /// right, 0 on it. In doubled columns (<c>2Q + R</c>, one per half hex) against the centre of
        /// the arena's widest rows (<c>MinColumn + MaxColumn</c> doubled, plus a half column for the
        /// odd rows' shift).
        /// </summary>
        public static int Side(HexGrid grid, HexCoordinate cell)
        {
            int x = 2 * cell.Q + cell.R;                              // doubled column, 0 at tile (0, 0)
            int centre = grid.MinColumn + grid.MaxColumn;              // doubled centre of an even row
            int doubled = 2 * x - (2 * centre + (grid.Height > 1 ? 1 : 0));
            return Math.Sign(doubled);
        }

        private static List<UnitFootprint> Footprints(EncounterTemplateData template, EnemyLibraryData enemies)
        {
            List<UnitFootprint> footprints = new List<UnitFootprint>();
            foreach (EncounterGroupData group in template.Groups ?? new EncounterGroupData[0])
            {
                EnemyData enemy = group == null ? null : Array.Find(enemies.Enemies ?? new EnemyData[0], e => e != null && e.EnemyId == group.EnemyId);
                EnemyLibraryValidator.TryParseFootprint(enemy == null ? null : enemy.Footprint, out UnitFootprint footprint);
                for (int i = 0; group != null && i < group.Count; i++)
                {
                    footprints.Add(footprint);
                }
            }

            return footprints;
        }

        private static int CountFree(HexGrid grid)
        {
            int free = 0;
            foreach (HexCoordinate tile in grid.Tiles)
            {
                free += grid.IsBlocked(tile) ? 0 : 1;
            }

            return free;
        }

        /// <summary>How many free tiles one flood fill from the first free tile reaches.</summary>
        private static int Reachable(HexGrid grid)
        {
            HexCoordinate? start = null;
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (!grid.IsBlocked(tile))
                {
                    start = tile;
                    break;
                }
            }

            if (!start.HasValue)
            {
                return 0;
            }

            HashSet<HexCoordinate> reached = new HashSet<HexCoordinate> { start.Value };
            Queue<HexCoordinate> open = new Queue<HexCoordinate>();
            open.Enqueue(start.Value);
            while (open.Count > 0)
            {
                HexCoordinate tile = open.Dequeue();
                foreach (HexCoordinate next in tile.Neighbors())
                {
                    if (!grid.IsBlocked(next) && reached.Add(next))
                    {
                        open.Enqueue(next);
                    }
                }
            }

            return reached.Count;
        }

        /// <summary>An <see cref="ArenaSize"/> name, exactly as written.</summary>
        public static bool TryParseArena(string name, out ArenaSize arena)
        {
            arena = ArenaSize.Medium;
            return !string.IsNullOrEmpty(name) && Enum.TryParse(name, false, out arena) && Enum.IsDefined(typeof(ArenaSize), arena) && arena.ToString() == name;
        }
    }
}
