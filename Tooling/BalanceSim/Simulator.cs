using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>The result of one simulated 1v1 (PvP round-robin) battle.</summary>
    public class BattleRecord
    {
        public KitMode Mode;
        public int Level;

        /// <summary>Species index (into the loaded roster) fielded on the player side.</summary>
        public int PlayerIndex;

        /// <summary>Species index fielded on the enemy side.</summary>
        public int EnemyIndex;

        public BattleOutcome Outcome;
        public int Rounds;

        /// <summary>Species index of the winner, or -1 for a mutual defeat or a stalemate.</summary>
        public int WinnerIndex = -1;

        /// <summary>The winner's remaining HP as a fraction of its starting HP (0 when nobody won).</summary>
        public double WinnerHpFraction;
    }

    /// <summary>
    /// The secondary, 1v1 mode (<c>--mode pvp</c>). Runs the round-robin with the real Runtime battle code: <see cref="BattleUnitFactory"/>,
    /// <see cref="TurnManager"/> and <see cref="BattleTurnExecutor.RunBattle"/> on a real
    /// <see cref="HexGrid"/>, so movement and approach behave exactly as in game.
    /// </summary>
    public static class Simulator
    {
        /// <summary>
        /// Every unordered pair of distinct species, at every level and mode, played twice with the
        /// sides swapped (A as player vs B as enemy, then B as player vs A as enemy). The ids per side
        /// stay fixed, so the ordinal-id speed-tie break favours each beast exactly once per pairing.
        /// Mirror matches are skipped.
        /// </summary>
        public static List<BattleRecord> Run(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species)
        {
            List<BattleRecord> records = new List<BattleRecord>();

            foreach (KitMode mode in options.Modes)
            {
                foreach (int level in options.Levels)
                {
                    for (int a = 0; a < species.Count; a++)
                    {
                        for (int b = a + 1; b < species.Count; b++)
                        {
                            records.Add(RunOne(options, species, mode, level, a, b));
                            records.Add(RunOne(options, species, mode, level, b, a));
                        }
                    }
                }
            }

            return records;
        }

        private static BattleRecord RunOne(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, KitMode mode, int level, int playerIndex, int enemyIndex)
        {
            HexGrid grid = new HexGrid(SimOptions.PvpArena);
            FindStartTiles(grid, out HexCoordinate playerTile, out HexCoordinate enemyTile);

            BattleUnit player = BattleUnitFactory.CreateBeast(SimOptions.PlayerUnitId, BattleTeam.Player, species[playerIndex], level, null,
                                                              playerTile, Kit.Loadout(Kit.BuildBeastKit(species[playerIndex], mode)));
            BattleUnit enemy = BattleUnitFactory.CreateBeast(SimOptions.EnemyUnitId, BattleTeam.Enemy, species[enemyIndex], level, null,
                                                             enemyTile, Kit.Loadout(Kit.BuildBeastKit(species[enemyIndex], mode)));

            if (!grid.TryPlaceUnit(player.Id, playerTile) || !grid.TryPlaceUnit(enemy.Id, enemyTile))
            {
                throw new InvalidOperationException("Could not place the units on their start tiles.");
            }

            int playerMaxHp = player.CurrentHp;
            int enemyMaxHp = enemy.CurrentHp;

            BattleUnit[] units = { player, enemy };
            TurnManager turnManager = new TurnManager(units);
            System.Random rng = new System.Random(DeriveSeed(options.Seed, mode, level, playerIndex, enemyIndex));
            BattleResult result = BattleTurnExecutor.RunBattle(turnManager, units, grid, rng, null, options.MaxRounds);

            BattleRecord record = new BattleRecord
            {
                Mode = mode,
                Level = level,
                PlayerIndex = playerIndex,
                EnemyIndex = enemyIndex,
                Outcome = result.Outcome,

                // A capped battle reports the round it stopped at opening (cap + 1); count rounds played.
                Rounds = Math.Min(result.Rounds, options.MaxRounds)
            };

            if (result.Outcome == BattleOutcome.PlayerVictory)
            {
                record.WinnerIndex = playerIndex;
                record.WinnerHpFraction = (double)player.CurrentHp / Math.Max(1, playerMaxHp);
            }
            else if (result.Outcome == BattleOutcome.EnemyVictory)
            {
                record.WinnerIndex = enemyIndex;
                record.WinnerHpFraction = (double)enemy.CurrentHp / Math.Max(1, enemyMaxHp);
            }

            return record;
        }

        /// <summary>
        /// The two start tiles: the player tile is the most central tile of the player's deployment
        /// zone (closest to the board's centre, then closest to the vertical centre line, first in
        /// the zone's stable order on a tie), and the enemy tile is its point mirror, which the
        /// board's 180-degree symmetry guarantees is in the enemy zone.
        /// </summary>
        public static void FindStartTiles(HexGrid grid, out HexCoordinate playerTile, out HexCoordinate enemyTile)
        {
            IReadOnlyList<HexCoordinate> zone = grid.GetDeploymentZone(BattleTeam.Player);
            playerTile = zone[0];
            int bestDistance = int.MaxValue;
            int bestOffset = int.MaxValue;

            foreach (HexCoordinate tile in zone)
            {
                int distance = tile.Distance(HexCoordinate.Zero);
                int offset = Math.Abs((2 * tile.Q) + tile.R);
                if (distance < bestDistance || (distance == bestDistance && offset < bestOffset))
                {
                    playerTile = tile;
                    bestDistance = distance;
                    bestOffset = offset;
                }
            }

            enemyTile = new HexCoordinate(-playerTile.Q, -playerTile.R);
            if (!grid.IsInDeploymentZone(enemyTile, BattleTeam.Enemy))
            {
                throw new InvalidOperationException("Mirrored enemy start tile " + enemyTile + " is outside the enemy deployment zone.");
            }
        }

        /// <summary>
        /// A per-battle seed that is a pure function of the inputs (never <c>string.GetHashCode</c>,
        /// which is randomised per process), so every battle's RNG is reproducible in isolation.
        /// </summary>
        private static int DeriveSeed(int seed, KitMode mode, int level, int playerIndex, int enemyIndex)
        {
            unchecked
            {
                int hash = seed;
                hash = (hash * 486187739) + (int)mode;
                hash = (hash * 486187739) + level;
                hash = (hash * 486187739) + playerIndex;
                hash = (hash * 486187739) + enemyIndex;
                return hash;
            }
        }
    }
}
