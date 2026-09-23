using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>One team's battle against one encounter at one difficulty.</summary>
    public class PveBattle
    {
        public BattleOutcome Outcome;

        /// <summary><see cref="BattleResult.ElapsedTicks"/>: when the last turn was taken, in gauge ticks.</summary>
        public long ElapsedTicks;

        /// <summary>Turns taken by every unit on both sides.</summary>
        public int Actions;

        /// <summary>Per team member, in the team's roster order (not slot order).</summary>
        public int[] DamageDealt;
        public int[] DamageTaken;
        public bool[] Alive;

        /// <summary>Per team member: turns it took.</summary>
        public int[] MemberActions;

        public int StrikeFires;
        public int BlastFires;
        public int BurstFires;
        public int BurstTargets;

        public bool Cleared
        {
            get { return Outcome == BattleOutcome.PlayerVictory; }
        }

        /// <summary>Normalized battle time (<see cref="BattleResult.Time"/>): 1.0 = one turn of a Speed-100 unit.</summary>
        public double Time
        {
            get { return (double)ElapsedTicks / TurnManager.TicksPerTimeUnit; }
        }
    }

    /// <summary>One point the difficulty calibration evaluated.</summary>
    public class CalibrationPoint
    {
        public double Multiplier;
        public double ClearRate;
    }

    /// <summary>Every team against one encounter at one level and kit mode, at the calibrated difficulty.</summary>
    public class PveCell
    {
        public KitMode Mode;
        public int Level;
        public Encounter Encounter;
        public double Multiplier;
        public double ClearRate;
        public List<CalibrationPoint> Evaluations = new List<CalibrationPoint>();

        /// <summary>Indexed like <see cref="PveSimulator.Teams"/>.</summary>
        public PveBattle[] Battles;
    }

    /// <summary>
    /// Team-vs-encounter battles through the real Runtime code: beasts and fixture enemies are both
    /// built by <see cref="BattleUnitFactory.CreateBeast"/>, the player team is committed with
    /// <see cref="PlacementValidator.TryPlaceAll"/>, and every turn is a real
    /// <see cref="BattleTurnExecutor.ExecuteTurn"/> on a real <see cref="HexGrid"/>.
    /// </summary>
    public class PveSimulator
    {
        private readonly SimOptions _options;
        private readonly IReadOnlyList<CreatureSpeciesSO> _species;
        private readonly SkillSO[][] _elementalKits;
        private readonly SkillSO[][] _neutralKits;

        public PveSimulator(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species)
        {
            _options = options;
            _species = species;
            _elementalKits = new SkillSO[species.Count][];
            _neutralKits = new SkillSO[species.Count][];
            for (int i = 0; i < species.Count; i++)
            {
                _elementalKits[i] = Kit.BuildBeastKit(species[i], KitMode.Elemental);
                _neutralKits[i] = Kit.BuildBeastKit(species[i], KitMode.Neutral);
            }

            Teams = Combinations(species.Count, options.TeamSize);
            SlotOrders = new int[Teams.Count][];
            for (int t = 0; t < Teams.Count; t++)
            {
                SlotOrders[t] = ShuffledSlots(options.Seed, t, options.TeamSize);
            }
        }

        /// <summary>Every combination of <c>TeamSize</c> distinct species, as ascending roster indices, in lexicographic order.</summary>
        public List<int[]> Teams { get; }

        /// <summary>
        /// Per team, which member stands in which deployment slot (and so gets which unit id). A
        /// fixed, seeded shuffle per team: slot and id decide the initiative tie break between team
        /// members (equal gauge and equal Speed) and which beast an enemy picks between equidistant
        /// targets (both break ties on the ordinal id), so pinning them to roster order would
        /// systematically expose the first species in the roster.
        /// </summary>
        public int[][] SlotOrders { get; }

        /// <summary>
        /// Id prefixes that neutralise the initiative tie between the two sides.
        /// <see cref="TurnManager"/> breaks a tie between equally full, equally fast gauges on the
        /// ordinal unit id (under the ATB gauge, units of equal Speed fill in lockstep, so this is
        /// every turn they share), and the raw ids (<c>e01</c>... for enemies, <c>p1</c>... for the
        /// team) would hand every cross-side tie to the enemies. Each battle
        /// instead prefixes one side with <see cref="TieWinnerPrefix"/> and the other with
        /// <see cref="TieLoserPrefix"/>; which side wins is decided by <see cref="PlayersWinTies"/>,
        /// an exact half of the teams in every (kit mode, encounter, level) cell. A prefix shared by
        /// a whole side leaves the order <em>within</em> the side (and so every targeting tie, which
        /// only ever compares units of one side) exactly as it was.
        /// </summary>
        public const string TieWinnerPrefix = "a";

        /// <summary>See <see cref="TieWinnerPrefix"/>.</summary>
        public const string TieLoserPrefix = "b";

        /// <summary>
        /// Calibrates the encounter's difficulty for this level and mode, then returns the battles at
        /// the calibrated multiplier. Deterministic: the evaluated multipliers depend only on the
        /// clear rates, which depend only on the inputs.
        /// </summary>
        public PveCell RunCell(KitMode mode, int level, Encounter encounter)
        {
            PveCell cell = new PveCell { Mode = mode, Level = level, Encounter = encounter };
            double target = _options.TargetClearRate;
            PveBattle[] best = null;
            double bestGap = double.MaxValue;

            double Evaluate(double multiplier)
            {
                PveBattle[] battles = RunAllTeams(mode, level, encounter, multiplier);
                int cleared = 0;
                foreach (PveBattle battle in battles)
                {
                    cleared += battle.Cleared ? 1 : 0;
                }

                double rate = (100.0 * cleared) / battles.Length;
                cell.Evaluations.Add(new CalibrationPoint { Multiplier = multiplier, ClearRate = rate });

                double gap = Math.Abs(rate - target);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = battles;
                    cell.Multiplier = multiplier;
                    cell.ClearRate = rate;
                }

                return rate;
            }

            // Bracket: clear rate falls as the multiplier rises. Double or halve from 1 until the
            // target sits between two evaluated multipliers, then bisect.
            double easy = 1.0;
            double hard = 1.0;
            double rateAtOne = Evaluate(1.0);
            if (rateAtOne > target)
            {
                double m = 1.0;
                double rate = rateAtOne;
                while (rate > target && m < SimOptions.MaxMultiplier)
                {
                    easy = m;
                    m *= 2.0;
                    rate = Evaluate(m);
                }

                hard = m;
                if (rate > target)
                {
                    easy = hard;
                }
            }
            else if (rateAtOne < target)
            {
                double m = 1.0;
                double rate = rateAtOne;
                while (rate < target && m > SimOptions.MinMultiplier)
                {
                    hard = m;
                    m /= 2.0;
                    rate = Evaluate(m);
                }

                easy = m;
                if (rate < target)
                {
                    hard = easy;
                }
            }

            for (int step = 0; step < SimOptions.CalibrationBisections && bestGap > 0.0 && hard > easy; step++)
            {
                double middle = (easy + hard) / 2.0;
                double rate = Evaluate(middle);
                if (rate > target)
                {
                    easy = middle;
                }
                else
                {
                    hard = middle;
                }
            }

            cell.Battles = best;
            return cell;
        }

        /// <summary>Every team against the encounter at one multiplier; parallel, results stored by team index.</summary>
        public PveBattle[] RunAllTeams(KitMode mode, int level, Encounter encounter, double multiplier)
        {
            PveBattle[] battles = new PveBattle[Teams.Count];
            bool[] playersWinTies = PlayersWinTies(mode, level, encounter.Id);
            Parallel.For(0, Teams.Count, t =>
            {
                battles[t] = RunBattle(mode, level, encounter, multiplier, t, false, playersWinTies[t], out _);
            });

            return battles;
        }

        /// <summary>
        /// Per team index, whether the team wins cross-side initiative ties in this (kit mode, level,
        /// encounter) cell. A seeded shuffle of the team indices, first half true: exactly half the
        /// teams (the extra one of an odd count goes to the enemies), independent of the difficulty
        /// multiplier so calibration compares like with like. A pure function of the inputs.
        /// </summary>
        public bool[] PlayersWinTies(KitMode mode, int level, string encounterId)
        {
            int count = Teams.Count;
            int[] order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            Random rng = new Random(DeriveSeed(_options.Seed, mode, level, encounterId, -1));
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            bool[] wins = new bool[count];
            for (int i = 0; i < count / 2; i++)
            {
                wins[order[i]] = true;
            }

            return wins;
        }

        /// <summary>
        /// One battle. The loop below is <see cref="BattleTurnExecutor.RunBattle"/>'s loop reproduced
        /// statement for statement (the ATB time cap, the last-turn timestamp and all), with an HP
        /// snapshot around each turn so damage can be attributed to the unit whose turn it was (the
        /// only actor: no avatar is fielded), and a per-member turn count. With
        /// <paramref name="useRunBattle"/> the real RunBattle is called instead, which is what the
        /// self-check compares against. Everything else — lifting the defeated off the grid, the
        /// partial approach — is the Runtime's own rule, applied inside
        /// <see cref="BattleTurnExecutor.ExecuteTurn"/>; the loop adds no rules of its own.
        /// <paramref name="playersWinTies"/> picks the id prefixes (see <see cref="TieWinnerPrefix"/>).
        /// </summary>
        public PveBattle RunBattle(KitMode mode, int level, Encounter encounter, double multiplier, int teamIndex, bool useRunBattle, bool playersWinTies,
                                   out List<BattleUnit> finalUnits)
        {
            int[] team = Teams[teamIndex];
            int[] slots = SlotOrders[teamIndex];
            HexGrid grid = new HexGrid(encounter.Data.ParsedArena);
            List<BattleUnit> units = new List<BattleUnit>();
            string playerPrefix = playersWinTies ? TieWinnerPrefix : TieLoserPrefix;
            string enemyPrefix = playersWinTies ? TieLoserPrefix : TieWinnerPrefix;

            // Enemies: front-most tiles of the enemy zone, in fixture order.
            List<HexCoordinate> enemyTiles = FrontTiles(grid, BattleTeam.Enemy, encounter.Enemies.Count);
            for (int i = 0; i < encounter.Enemies.Count; i++)
            {
                EnemySlot slot = encounter.Enemies[i];
                BattleUnit enemy = BattleUnitFactory.CreateBeast(enemyPrefix + slot.UnitId, BattleTeam.Enemy, slot.Species, level, null, enemyTiles[i],
                                                                 Kit.Loadout(slot.KitFor(mode)));
                enemy.Stats = Scale(enemy.Stats, multiplier);
                enemy.CurrentHp = enemy.Stats.Hp;

                if (!grid.IsInDeploymentZone(enemyTiles[i], BattleTeam.Enemy) || !grid.TryPlaceUnit(enemy.Id, enemyTiles[i]))
                {
                    throw new InvalidOperationException("Could not place enemy " + enemy.Id + " of '" + encounter.Id + "' at " + enemyTiles[i] + ".");
                }

                units.Add(enemy);
            }

            // Players: front-most tiles of the player zone, committed through the real placement validator.
            List<HexCoordinate> playerTiles = FrontTiles(grid, BattleTeam.Player, team.Length);
            BattleUnit[] members = new BattleUnit[team.Length];
            List<PlacementRequest> requests = new List<PlacementRequest>();
            for (int s = 0; s < team.Length; s++)
            {
                int member = slots[s];
                int speciesIndex = team[member];
                SkillSO[] kit = mode == KitMode.Elemental ? _elementalKits[speciesIndex] : _neutralKits[speciesIndex];
                string id = playerPrefix + "p" + (s + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                members[member] = BattleUnitFactory.CreateBeast(id, BattleTeam.Player, _species[speciesIndex], level, null, playerTiles[s],
                                                                Kit.Loadout(kit));
                requests.Add(new PlacementRequest(id, playerTiles[s]));
            }

            if (!PlacementValidator.TryPlaceAll(grid, BattleTeam.Player, SimOptions.FormatForTeamSize(team.Length), requests, enemyTiles,
                                                out PlacementValidationResult _))
            {
                throw new InvalidOperationException("Player placement was rejected for team " + teamIndex + " in '" + encounter.Id + "'.");
            }

            units.AddRange(members);

            PveBattle battle = new PveBattle
            {
                DamageDealt = new int[team.Length],
                DamageTaken = new int[team.Length],
                Alive = new bool[team.Length],
                MemberActions = new int[team.Length]
            };

            TurnManager turnManager = new TurnManager(units);
            Random rng = new Random(DeriveSeed(_options.Seed, mode, level, encounter.Id, teamIndex));
            BattleOutcome outcome;
            long elapsedTicks;
            int actions;

            if (useRunBattle)
            {
                BattleResult result = BattleTurnExecutor.RunBattle(turnManager, units, grid, rng, null, _options.MaxTime);
                outcome = result.Outcome;
                elapsedTicks = result.ElapsedTicks;
                actions = result.ActionCount;
            }
            else
            {
                Dictionary<BattleUnit, int> memberIndex = new Dictionary<BattleUnit, int>();
                for (int m = 0; m < members.Length; m++)
                {
                    memberIndex[members[m]] = m;
                }

                int[] before = new int[units.Count];
                long capTicks = (long)(_options.MaxTime < 1 ? 1 : _options.MaxTime) * TurnManager.TicksPerTimeUnit;
                long lastTurnTicks = 0;
                actions = 0;

                while (true)
                {
                    if (TryConclude(units, out outcome))
                    {
                        break;
                    }

                    if (turnManager.ElapsedTicks > capTicks)
                    {
                        outcome = BattleOutcome.Stalemate;
                        break;
                    }

                    BattleUnit current = turnManager.CurrentUnit;
                    if (current == null)
                    {
                        outcome = BattleOutcome.Stalemate;
                        break;
                    }

                    if (!current.IsDefeated)
                    {
                        for (int u = 0; u < units.Count; u++)
                        {
                            before[u] = units[u].CurrentHp;
                        }

                        lastTurnTicks = turnManager.ElapsedTicks;
                        BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(current, units, grid, rng, null);
                        actions++;

                        bool actorIsMember = memberIndex.TryGetValue(current, out int actor);
                        for (int u = 0; u < units.Count; u++)
                        {
                            int lost = before[u] - units[u].CurrentHp;
                            if (lost <= 0)
                            {
                                continue;
                            }

                            if (actorIsMember)
                            {
                                battle.DamageDealt[actor] += lost;
                            }

                            if (memberIndex.TryGetValue(units[u], out int victim))
                            {
                                battle.DamageTaken[victim] += lost;
                            }
                        }

                        if (actorIsMember)
                        {
                            battle.MemberActions[actor]++;
                            CountFires(battle, turn);
                        }
                    }

                    turnManager.AdvanceTurn();
                }

                elapsedTicks = lastTurnTicks;
            }

            battle.Outcome = outcome;
            battle.ElapsedTicks = elapsedTicks;
            battle.Actions = actions;
            for (int m = 0; m < members.Length; m++)
            {
                battle.Alive[m] = !members[m].IsDefeated;
            }

            finalUnits = units;
            return battle;
        }

        private static void CountFires(PveBattle battle, BattleTurnResult turn)
        {
            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (!outcome.Fired)
                {
                    continue;
                }

                if (Kit.IsStrike(outcome.Skill))
                {
                    battle.StrikeFires++;
                }
                else if (Kit.IsBlast(outcome.Skill))
                {
                    battle.BlastFires++;
                }
                else if (Kit.IsBurstUse(outcome.Skill))
                {
                    battle.BurstFires++;
                    battle.BurstTargets += outcome.Activation == null || outcome.Activation.Targets == null ? 0 : outcome.Activation.Targets.Count;
                }
            }
        }

        /// <summary>Same rule as BattleTurnExecutor's private TryConclude: the battle ends when at most one team has living units.</summary>
        private static bool TryConclude(List<BattleUnit> units, out BattleOutcome outcome)
        {
            bool player = false;
            bool enemy = false;
            foreach (BattleUnit unit in units)
            {
                if (unit.IsDefeated)
                {
                    continue;
                }

                if (unit.Team == BattleTeam.Player)
                {
                    player = true;
                }
                else
                {
                    enemy = true;
                }
            }

            if (player && enemy)
            {
                outcome = BattleOutcome.Stalemate;
                return false;
            }

            outcome = player ? BattleOutcome.PlayerVictory : enemy ? BattleOutcome.EnemyVictory : BattleOutcome.MutualDefeat;
            return true;
        }

        /// <summary>
        /// The difficulty knob: HP, Attack, Defense, SpecialAttack and SpecialDefense scale by the
        /// multiplier (rounded, floored at 1). Speed and MoveRange do not: under the ATB gauge Speed
        /// is how many turns a unit gets, so scaling it would make the knob change the enemies'
        /// action economy rather than just their toughness and punch, and move range is a small
        /// tactical integer.
        /// </summary>
        public static StatBlock Scale(StatBlock stats, double multiplier)
        {
            StatBlock scaled = stats;
            scaled.Hp = ScaleStat(stats.Hp, multiplier);
            scaled.Attack = ScaleStat(stats.Attack, multiplier);
            scaled.Defense = ScaleStat(stats.Defense, multiplier);
            scaled.SpecialAttack = ScaleStat(stats.SpecialAttack, multiplier);
            scaled.SpecialDefense = ScaleStat(stats.SpecialDefense, multiplier);
            return scaled;
        }

        private static int ScaleStat(int value, double multiplier)
        {
            int scaled = (int)Math.Round(value * multiplier, MidpointRounding.AwayFromZero);
            return scaled < 1 ? 1 : scaled;
        }

        /// <summary>
        /// The <paramref name="count"/> tiles of a team's deployment zone nearest the centre line:
        /// front row first (smallest |R|), then outward from the board's vertical centre line, then
        /// by Q. Both sides therefore start as close as the zones allow.
        /// </summary>
        public static List<HexCoordinate> FrontTiles(HexGrid grid, BattleTeam team, int count)
        {
            List<HexCoordinate> zone = new List<HexCoordinate>(grid.GetDeploymentZone(team));
            zone.Sort((a, b) =>
            {
                int byRow = Math.Abs(a.R).CompareTo(Math.Abs(b.R));
                if (byRow != 0)
                {
                    return byRow;
                }

                int byOffset = Math.Abs((2 * a.Q) + a.R).CompareTo(Math.Abs((2 * b.Q) + b.R));
                return byOffset != 0 ? byOffset : a.Q.CompareTo(b.Q);
            });

            if (zone.Count < count)
            {
                throw new InvalidOperationException(count + " units do not fit a " + zone.Count + "-tile " + team + " deployment zone on " + grid.Size + ".");
            }

            return zone.GetRange(0, count);
        }

        private static List<int[]> Combinations(int n, int k)
        {
            List<int[]> result = new List<int[]>();
            int[] current = new int[k];

            void Recurse(int start, int depth)
            {
                if (depth == k)
                {
                    result.Add((int[])current.Clone());
                    return;
                }

                for (int i = start; i <= n - (k - depth); i++)
                {
                    current[depth] = i;
                    Recurse(i + 1, depth + 1);
                }
            }

            if (k <= n)
            {
                Recurse(0, 0);
            }

            return result;
        }

        private static int[] ShuffledSlots(int seed, int teamIndex, int size)
        {
            int[] order = new int[size];
            for (int i = 0; i < size; i++)
            {
                order[i] = i;
            }

            Random rng = new Random(unchecked((seed * 486187739) + teamIndex));
            for (int i = size - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            return order;
        }

        /// <summary>A per-battle seed that is a pure function of the inputs (the multiplier deliberately excluded).</summary>
        private static int DeriveSeed(int seed, KitMode mode, int level, string encounterId, int teamIndex)
        {
            unchecked
            {
                int hash = seed;
                hash = (hash * 486187739) + 7;
                hash = (hash * 486187739) + (int)mode;
                hash = (hash * 486187739) + level;
                foreach (char c in encounterId)
                {
                    hash = (hash * 486187739) + c;
                }

                hash = (hash * 486187739) + teamIndex;
                return hash;
            }
        }
    }
}
