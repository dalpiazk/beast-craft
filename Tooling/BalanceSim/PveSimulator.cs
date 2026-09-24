using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>One team's battle against one encounter (composition) at one difficulty.</summary>
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

        /// <summary>
        /// Per team member: fires of its physical single-target skill (Strike, or Shot for a Ranged
        /// beast) and of Blast. The kit parity table weighs them by power, per stance.
        /// </summary>
        public int[] MemberPhysicalFires;
        public int[] MemberSpecialFires;

        public int BurstFires;
        public int BurstTargets;

        /// <summary>Per team member: damage effects it landed (<see cref="SkillActivation.Hits"/>).</summary>
        public int[] MemberHits;

        /// <summary>Per team member: how many of those were critical hits.</summary>
        public int[] MemberCrits;

        /// <summary>
        /// Per avatar passive, in the preset's slot order: how many times it fired this battle
        /// (battle start included). <c>null</c> when no avatar was fielded.
        /// </summary>
        public int[] PassiveFirings;

        /// <summary>The avatar's own turns this battle (0 when no avatar was fielded).</summary>
        public int AvatarTurns;

        /// <summary>The avatar's active-skill casts this battle (every <c>AvatarActivations</c> entry).</summary>
        public int AvatarCasts;

        /// <summary>
        /// Per team member: the sum over its hits of the random multiplier applied to each,
        /// <c>(crit ? CritMultiplier : 1) * variance / 100</c>. Divided by <see cref="MemberHits"/>
        /// it is the average damage multiplier the rolls gave the beast.
        /// </summary>
        public double[] MemberRollMultiplier;

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

        /// <summary>Wall-clock seconds the evaluation took (for <c>--timings</c>; never in the report).</summary>
        public double Seconds;

        /// <summary>Battles the evaluation covered (for <c>--timings</c>).</summary>
        public int Battles;

        /// <summary>
        /// True when the multiplier scaled every enemy to exactly the stats an earlier evaluation
        /// of the cell did, so its battles were reused rather than re-run (see
        /// <see cref="PveSimulator.RunCell"/>). For <c>--timings</c>; never in the report.
        /// </summary>
        public bool Reused;

        /// <summary>
        /// With a scouted-pick calibration (<c>--calibrate-on heuristic|bonds</c>) or
        /// <c>--calibrate-sample</c>: true for the one evaluation of every team at the chosen
        /// multiplier that follows the search (the others then cover only the picked teams or the sample).
        /// </summary>
        public bool Final;
    }

    /// <summary>
    /// Every team against every composition of one shape (or one fixed encounter) at one level and
    /// kit mode, at the calibrated difficulty.
    /// </summary>
    public class PveCell
    {
        public KitMode Mode;
        public int Level;
        public EncounterShape Shape;
        public double Multiplier;

        /// <summary>
        /// The mean clear rate of every team over every composition at <see cref="Multiplier"/>: what
        /// <c>--calibrate-on mean</c> aims at the target, and otherwise the no-scouting rate (the
        /// player who does not look at the encounter and brings a random team).
        /// </summary>
        public double ClearRate;

        /// <summary>What the calibration aimed at the target (<see cref="SimOptions.EffectiveCalibrateOn"/>).</summary>
        public CalibrationTarget CalibratedOn;

        /// <summary>
        /// Scouted-pick calibration only: the picked team's clear rate at <see cref="Multiplier"/>,
        /// over <see cref="CalibrationSamples"/> battles per composition (the calibrated number);
        /// NaN with <c>--calibrate-on mean</c>.
        /// </summary>
        public double ScoutedClearRate = double.NaN;

        /// <summary>Scouted-pick calibration only: per composition, the team index the picker fields; null with <c>--calibrate-on mean</c>.</summary>
        public int[] Picks;

        /// <summary>Scouted-pick calibration only: battles per composition per search step (<c>--calibrate-samples</c>).</summary>
        public int CalibrationSamples;

        /// <summary>
        /// Scouted-pick calibration only: the picked teams' battles at <see cref="Multiplier"/>
        /// (<see cref="PveSimulator.RunPicked"/>: composition <c>c</c>, sample <c>s</c> at
        /// <c>c * CalibrationSamples + s</c>), behind <see cref="ScoutedClearRate"/>; null otherwise.
        /// </summary>
        public PveBattle[] PickedBattles;

        /// <summary>The rate the calibration aimed at the target: <see cref="ScoutedClearRate"/>, or <see cref="ClearRate"/> with <c>--calibrate-on mean</c>.</summary>
        public double CalibratedRate
        {
            get { return CalibratedOn == CalibrationTarget.Mean ? ClearRate : ScoutedClearRate; }
        }

        public List<CalibrationPoint> Evaluations = new List<CalibrationPoint>();

        /// <summary>
        /// Every battle at the calibrated multiplier: composition-major, then team, then sample, so
        /// composition <c>c</c>, team <c>t</c>, sample <c>s</c> is at
        /// <c>((c * TeamCount) + t) * Samples + s</c> (see <see cref="PveSimulator.BattleIndex"/>).
        /// </summary>
        public PveBattle[] Battles;

        /// <summary>Battles per team and composition: each is the same fight with a different seed (damage rolls differ).</summary>
        public int Samples;

        public int TeamCount;

        /// <summary>The team index of battle <paramref name="index"/>.</summary>
        public int TeamOf(int index)
        {
            return (index / Samples) % TeamCount;
        }

        /// <summary>The composition index (into <see cref="EncounterShape.Compositions"/>) of battle <paramref name="index"/>.</summary>
        public int CompositionOf(int index)
        {
            return index / (Samples * TeamCount);
        }
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
        private readonly SkillInstance[][] _elementalLibraryKits;
        private readonly SkillInstance[][] _neutralLibraryKits;

        public PveSimulator(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species)
        {
            _options = options;
            _species = species;
            // Every species shares the medium curve; the avatar's fixture stats follow it too.
            Avatar = new AvatarPresets(options.AvatarPreset, options.Library, species.Count > 0 ? species[0].GrowthRate : null);
            _elementalKits = new SkillSO[species.Count][];
            _neutralKits = new SkillSO[species.Count][];
            for (int i = 0; i < species.Count; i++)
            {
                _elementalKits[i] = Kit.BuildBeastKit(species[i], KitMode.Elemental);
                _neutralKits[i] = Kit.BuildBeastKit(species[i], KitMode.Neutral);
            }

            if (options.KitSource == KitSource.Library)
            {
                _elementalLibraryKits = new SkillInstance[species.Count][];
                _neutralLibraryKits = new SkillInstance[species.Count][];
                for (int i = 0; i < species.Count; i++)
                {
                    _elementalLibraryKits[i] = options.Library.BeastKit(species[i], KitMode.Elemental);
                    _neutralLibraryKits[i] = options.Library.BeastKit(species[i], KitMode.Neutral);
                }
            }

            Teams = Combinations(species.Count, options.TeamSize);
            SlotOrders = new int[Teams.Count][];
            for (int t = 0; t < Teams.Count; t++)
            {
                SlotOrders[t] = ShuffledSlots(options.Seed, t, options.TeamSize);
            }

            CalibrationTeams = SampleTeams(options.Seed, Teams.Count, options.CalibrateSample);
            TeamBonds = new List<ActiveTeamBond>[Teams.Count];
            for (int t = 0; t < Teams.Count; t++)
            {
                List<TeamBondMember> members = new List<TeamBondMember>();
                foreach (int speciesIndex in Teams[t])
                {
                    members.Add(TeamBondMember.FromSpecies(species[speciesIndex]));
                }

                TeamBonds[t] = options.BondsActive ? TeamBondResolver.Resolve(options.Library.TeamBonds, members) : new List<ActiveTeamBond>();
            }
        }

        /// <summary>
        /// Per team: its active bonds (<see cref="TeamBondResolver"/>, member indices in the team's
        /// roster order), or empty for every team when bonds are off (<see cref="SimOptions.BondsActive"/>).
        /// </summary>
        public List<ActiveTeamBond>[] TeamBonds { get; }

        /// <summary>
        /// With <c>--calibrate-sample n</c> (n below the team count): the team indices, ascending, a
        /// seeded draw of n, that the difficulty search evaluates. Null = every team (the default).
        /// </summary>
        public int[] CalibrationTeams { get; }

        /// <summary>Every combination of <c>TeamSize</c> distinct species, as ascending roster indices, in lexicographic order.</summary>
        public List<int[]> Teams { get; }

        /// <summary>The avatar fielded beside every player team (<c>--avatar</c>); disabled by default.</summary>
        public AvatarPresets Avatar { get; }

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
        /// an exact half of the teams against every composition in every (kit mode, level) cell. A prefix shared by
        /// a whole side leaves the order <em>within</em> the side (and so every targeting tie, which
        /// only ever compares units of one side) exactly as it was.
        /// </summary>
        public const string TieWinnerPrefix = "a";

        /// <summary>See <see cref="TieWinnerPrefix"/>.</summary>
        public const string TieLoserPrefix = "b";

        /// <summary>Battles per team per composition per evaluation (<see cref="SimOptions.PveSamples"/>).</summary>
        public int Samples
        {
            get { return _options.PveSamples; }
        }

        /// <summary>Where composition <paramref name="composition"/>, team <paramref name="teamIndex"/>, sample <paramref name="sample"/> sits in a battle array.</summary>
        public int BattleIndex(int composition, int teamIndex, int sample)
        {
            return (((composition * Teams.Count) + teamIndex) * Samples) + sample;
        }

        /// <summary>
        /// Calibrates the shape's difficulty for this level and mode (one multiplier over all its
        /// compositions), then returns the battles at the calibrated multiplier. Deterministic: every battle's rng is seeded from the inputs
        /// (never the multiplier), so the clear rates, and with them the evaluated multipliers,
        /// depend only on the inputs. Battles are random (damage variance and crits), so each team
        /// fights each composition <see cref="Samples"/> times with distinct seeds and the clear rate
        /// is over all of them.
        /// <para>
        /// What is aimed at the target is <see cref="SimOptions.EffectiveCalibrateOn"/>: by default
        /// the team the bond-aware scouted picker fields against each composition (the player is
        /// assumed to scout and counter-pick), whose battles alone are run at each step,
        /// <c>--calibrate-samples</c> times per composition (<see cref="RunPicked"/>); the chosen
        /// multiplier then runs every team once, which every metric and the no-scouting rate
        /// (<see cref="PveCell.ClearRate"/>) come from. <c>--calibrate-on mean</c> aims the mean of
        /// every team at the target instead (every team at every step), exactly as before scouting.
        /// </para>
        /// </summary>
        public PveCell RunCell(KitMode mode, int level, EncounterShape shape)
        {
            CalibrationTarget calibrateOn = _options.EffectiveCalibrateOn;
            PveCell cell = new PveCell { Mode = mode, Level = level, Shape = shape, Samples = Samples, TeamCount = Teams.Count, CalibratedOn = calibrateOn };
            double target = _options.TargetClearRate;
            PveBattle[] best = null;
            double bestGap = double.MaxValue;
            double bestRate = double.NaN;

            // --calibrate-on heuristic|bonds: the search runs only the team the picker fields against
            // each composition (the picks depend on the preview alone, so once per shape), each
            // CalibrateSamples times; the chosen multiplier then runs every team once (below).
            int[] picks = null;
            int pickSamples = 0;
            if (calibrateOn != CalibrationTarget.Mean)
            {
                picks = ScoutedPicker.PicksFor(_options, _species, Teams, TeamBonds, shape, ScoutedPicker.StrategyFor(calibrateOn));
                pickSamples = _options.CalibrateSamples;
                cell.Picks = picks;
                cell.CalibrationSamples = pickSamples;
            }

            // The multiplier reaches a battle only through Scale(enemy stats), and every battle's
            // seed ignores it, so two multipliers that scale every enemy of the shape to the same
            // stats play every battle identically. Late bisection steps often do (small stats
            // round alike, notably at level 1): those evaluations reuse the earlier battles.
            List<StatBlock> enemyStats = DistinctEnemyStats(shape, level);
            List<int[]> evaluatedScales = new List<int[]>();
            List<PveBattle[]> evaluatedBattles = new List<PveBattle[]>();

            // --calibrate-sample (mean only): the search evaluates a subset of the teams, and only the
            // chosen multiplier is then run with every team (below).
            int[] searchTeams = picks == null ? CalibrationTeams : null;

            double Evaluate(double multiplier)
            {
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                int[] scale = ScaledStats(enemyStats, multiplier);
                PveBattle[] battles = null;
                for (int e = 0; e < evaluatedScales.Count && battles == null; e++)
                {
                    if (SameStats(evaluatedScales[e], scale))
                    {
                        battles = evaluatedBattles[e];
                    }
                }

                bool reused = battles != null;
                if (!reused)
                {
                    battles = picks != null ? RunPicked(mode, level, shape, multiplier, picks, pickSamples)
                        : searchTeams == null ? RunAllTeams(mode, level, shape, multiplier) : RunTeams(mode, level, shape, multiplier, searchTeams);
                    evaluatedScales.Add(scale);
                    evaluatedBattles.Add(battles);
                }

                int cleared = 0;
                foreach (PveBattle battle in battles)
                {
                    cleared += battle.Cleared ? 1 : 0;
                }

                double rate = (100.0 * cleared) / battles.Length;
                cell.Evaluations.Add(new CalibrationPoint
                {
                    Multiplier = multiplier,
                    ClearRate = rate,
                    Seconds = clock.Elapsed.TotalSeconds,
                    Battles = battles.Length,
                    Reused = reused
                });

                double gap = Math.Abs(rate - target);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = battles;
                    bestRate = rate;
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

            if (picks != null)
            {
                cell.ScoutedClearRate = bestRate;
                cell.PickedBattles = best;
            }

            if (searchTeams != null || picks != null)
            {
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                best = RunAllTeams(mode, level, shape, cell.Multiplier);
                int cleared = 0;
                foreach (PveBattle battle in best)
                {
                    cleared += battle.Cleared ? 1 : 0;
                }

                cell.ClearRate = (100.0 * cleared) / best.Length;
                cell.Evaluations.Add(new CalibrationPoint
                {
                    Multiplier = cell.Multiplier,
                    ClearRate = cell.ClearRate,
                    Seconds = clock.Elapsed.TotalSeconds,
                    Battles = best.Length,
                    Final = true
                });
            }

            cell.Battles = best;
            return cell;
        }

        /// <summary>
        /// The scouted-pick calibration's battles: against each composition <c>c</c> of the shape, the
        /// picked team <paramref name="picks"/>[c], <paramref name="samples"/> times (samples 0 to
        /// n-1), stored at <c>c * samples + s</c>. Each battle is the one <see cref="RunAllTeams"/>
        /// would play for that team and sample, seed and all, so sample 0 is exactly the all-teams
        /// battle of the picked team.
        /// </summary>
        public PveBattle[] RunPicked(KitMode mode, int level, EncounterShape shape, double multiplier, int[] picks, int samples)
        {
            PveBattle[] battles = new PveBattle[shape.Compositions.Count * samples];
            bool[] playersWinTies = new bool[shape.Compositions.Count];
            for (int c = 0; c < shape.Compositions.Count; c++)
            {
                playersWinTies[c] = PlayersWinTies(mode, level, shape.Compositions[c].Id)[picks[c]];
            }

            Parallel.For(0, battles.Length, i =>
            {
                int c = i / samples;
                battles[i] = RunBattle(mode, level, shape.Compositions[c], multiplier, picks[c], i % samples, false, playersWinTies[c], out _);
            });

            return battles;
        }

        /// <summary>
        /// <see cref="RunAllTeams"/> for a subset of the teams only (<paramref name="teams"/>, team
        /// indices): composition-major, then the subset's order, then sample. Every battle is the one
        /// <see cref="RunAllTeams"/> would play for that team, seed and all.
        /// </summary>
        public PveBattle[] RunTeams(KitMode mode, int level, EncounterShape shape, double multiplier, int[] teams)
        {
            int samples = Samples;
            int count = teams.Length;
            PveBattle[] battles = new PveBattle[shape.Compositions.Count * count * samples];
            bool[][] playersWinTies = new bool[shape.Compositions.Count][];
            for (int c = 0; c < shape.Compositions.Count; c++)
            {
                playersWinTies[c] = PlayersWinTies(mode, level, shape.Compositions[c].Id);
            }

            Parallel.For(0, battles.Length, i =>
            {
                int t = teams[(i / samples) % count];
                int c = i / (samples * count);
                battles[i] = RunBattle(mode, level, shape.Compositions[c], multiplier, t, i % samples, false, playersWinTies[c][t], out _);
            });

            return battles;
        }

        /// <summary>
        /// <paramref name="size"/> distinct team indices out of <paramref name="teamCount"/>, a seeded
        /// shuffle's first <paramref name="size"/>, ascending; null when <paramref name="size"/> is 0
        /// (off) or covers every team, so the full search runs exactly as without the option.
        /// </summary>
        private static int[] SampleTeams(int seed, int teamCount, int size)
        {
            if (size <= 0 || size >= teamCount)
            {
                return null;
            }

            int[] order = new int[teamCount];
            for (int i = 0; i < teamCount; i++)
            {
                order[i] = i;
            }

            Random rng = new Random(unchecked((seed * 486187739) + 0x5a17));
            for (int i = teamCount - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            int[] sample = new int[size];
            Array.Copy(order, sample, size);
            Array.Sort(sample);
            return sample;
        }

        /// <summary>
        /// The unscaled stat block of every distinct enemy species in the shape's compositions at
        /// this level, in first-appearance order: what <see cref="Scale"/> is applied to.
        /// </summary>
        private static List<StatBlock> DistinctEnemyStats(EncounterShape shape, int level)
        {
            List<CreatureSpeciesSO> seen = new List<CreatureSpeciesSO>();
            List<StatBlock> stats = new List<StatBlock>();
            foreach (Encounter composition in shape.Compositions)
            {
                foreach (EnemySlot slot in composition.Enemies)
                {
                    if (!seen.Contains(slot.Species))
                    {
                        seen.Add(slot.Species);
                        stats.Add(StatCalculator.ComputeStats(slot.Species, level, null));
                    }
                }
            }

            return stats;
        }

        /// <summary>Every stat <see cref="Scale"/> changes, for every block, at this multiplier.</summary>
        private static int[] ScaledStats(List<StatBlock> stats, double multiplier)
        {
            int[] values = new int[stats.Count * 5];
            for (int i = 0; i < stats.Count; i++)
            {
                StatBlock scaled = Scale(stats[i], multiplier);
                values[(i * 5) + 0] = scaled.Hp;
                values[(i * 5) + 1] = scaled.Attack;
                values[(i * 5) + 2] = scaled.Defense;
                values[(i * 5) + 3] = scaled.SpecialAttack;
                values[(i * 5) + 4] = scaled.SpecialDefense;
            }

            return values;
        }

        private static bool SameStats(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Every team against every composition of the shape at one multiplier, <see cref="Samples"/>
        /// times each; parallel, results stored at <see cref="BattleIndex"/>.
        /// </summary>
        public PveBattle[] RunAllTeams(KitMode mode, int level, EncounterShape shape, double multiplier)
        {
            int samples = Samples;
            int teams = Teams.Count;
            PveBattle[] battles = new PveBattle[shape.Compositions.Count * teams * samples];
            bool[][] playersWinTies = new bool[shape.Compositions.Count][];
            for (int c = 0; c < shape.Compositions.Count; c++)
            {
                playersWinTies[c] = PlayersWinTies(mode, level, shape.Compositions[c].Id);
            }

            Parallel.For(0, battles.Length, i =>
            {
                int t = (i / samples) % teams;
                int c = i / (samples * teams);
                battles[i] = RunBattle(mode, level, shape.Compositions[c], multiplier, t, i % samples, false, playersWinTies[c][t], out _);
            });

            return battles;
        }

        /// <summary>
        /// Per team index, whether the team wins cross-side initiative ties against this encounter
        /// (composition) at this kit mode and level. A seeded shuffle of the team indices, first half true: exactly half the
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

            Random rng = new Random(DeriveSeed(_options.Seed, mode, level, encounterId, -1, -1));
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
        /// avatar's passives, when one is fielded with <c>--avatar</c>, are credited to that unit
        /// too; the battle-start ones to nobody), and a per-member turn count. With
        /// <paramref name="useRunBattle"/> the real RunBattle is called instead, which is what the
        /// self-check compares against. Everything else — lifting the defeated off the grid, the
        /// partial approach — is the Runtime's own rule, applied inside
        /// <see cref="BattleTurnExecutor.ExecuteTurn"/>; the loop adds no rules of its own.
        /// <paramref name="playersWinTies"/> picks the id prefixes (see <see cref="TieWinnerPrefix"/>).
        /// <paramref name="sample"/> picks the seed: the same team and fight with a different stream
        /// of damage rolls.
        /// </summary>
        public PveBattle RunBattle(KitMode mode, int level, Encounter encounter, double multiplier, int teamIndex, int sample, bool useRunBattle,
                                   bool playersWinTies, out List<BattleUnit> finalUnits)
        {
            int[] team = Teams[teamIndex];
            int[] slots = SlotOrders[teamIndex];
            HexGrid grid = new HexGrid(encounter.Arena);
            List<BattleUnit> units = new List<BattleUnit>();
            string playerPrefix = playersWinTies ? TieWinnerPrefix : TieLoserPrefix;
            string enemyPrefix = playersWinTies ? TieLoserPrefix : TieWinnerPrefix;

            // Enemies: packed front-most first, in fixture order (large enemies take the first anchor
            // their whole footprint fits; one-tile enemies simply take the front-most tiles).
            EnemyLayout layout = Layout(encounter);
            List<HexCoordinate> enemyTiles = layout.Covered;
            for (int i = 0; i < encounter.Enemies.Count; i++)
            {
                EnemySlot slot = encounter.Enemies[i];
                HexCoordinate anchor = layout.Anchors[i];
                BattleUnit enemy = BattleUnitFactory.CreateBeast(enemyPrefix + slot.UnitId, BattleTeam.Enemy, slot.Species, level, null, anchor,
                                                                 Kit.Loadout(slot.KitFor(mode)), slot.StatusResist);
                enemy.Stats = Scale(enemy.Stats, multiplier);
                enemy.CurrentHp = enemy.Stats.Hp;

                if (!grid.FitsDeploymentZone(anchor, enemy.Footprint, BattleTeam.Enemy) || !grid.TryPlaceUnit(enemy.Id, anchor, enemy.Footprint))
                {
                    throw new InvalidOperationException("Could not place enemy " + enemy.Id + " of '" + encounter.Id + "' at " + anchor + ".");
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
                string id = playerPrefix + "p" + (s + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                members[member] = BattleUnitFactory.CreateBeast(id, BattleTeam.Player, _species[speciesIndex], level, null, playerTiles[s],
                                                                BeastLoadout(speciesIndex, mode));
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
                MemberActions = new int[team.Length],
                MemberHits = new int[team.Length],
                MemberCrits = new int[team.Length],
                MemberRollMultiplier = new double[team.Length],
                MemberPhysicalFires = new int[team.Length],
                MemberSpecialFires = new int[team.Length],
                PassiveFirings = Avatar.Enabled ? new int[Avatar.Passives.Count] : null
            };

            Random rng = new Random(DeriveSeed(_options.Seed, mode, level, encounter.Id, teamIndex, sample));
            BattleUnit avatar = Avatar.Build(_options.AvatarLevel > 0 ? _options.AvatarLevel : level, out PassiveLoadout passives);

            // The avatar fills its own ATB gauge: it is in the turn order, never in the targeting roster.
            TurnManager turnManager = new TurnManager(avatar == null ? units : new List<BattleUnit>(units) { avatar });
            TeamBondLoadout bonds = TeamBonds[teamIndex].Count == 0 ? null : new TeamBondLoadout(TeamBonds[teamIndex], members);
            BattleOutcome outcome;
            long elapsedTicks;
            int actions;

            if (useRunBattle)
            {
                BattleResult result = BattleTurnExecutor.RunBattle(turnManager, units, grid, rng, avatar, passives, bonds, _options.MaxTime);
                outcome = result.Outcome;
                elapsedTicks = result.ElapsedTicks;
                actions = result.ActionCount;
                if (avatar != null)
                {
                    CountPassives(battle, result.OpeningPassiveActivations);
                    foreach (BattleTurnResult turn in result.Turns)
                    {
                        CountPassives(battle, turn.PassiveActivations);
                        CountAvatar(battle, turn, avatar);
                    }
                }
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

                // RunBattle's battle-start hook: the team's bonds, then the avatar's passives (each a no-op when absent).
                CountPassives(battle, BattleTurnExecutor.BeginBattle(units, grid, rng, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> _));

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
                        BattleTurnResult turn = current == avatar
                            ? BattleTurnExecutor.ExecuteAvatarTurn(avatar, units, grid, rng, passives)
                            : BattleTurnExecutor.ExecuteTurn(current, units, grid, rng, avatar, passives);
                        actions++;
                        CountPassives(battle, turn.PassiveActivations);
                        CountAvatar(battle, turn, avatar);

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
                            CountFires(battle, turn, actor);
                            CountRolls(battle, turn, actor);
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

        /// <summary>
        /// A fresh loadout for a player beast: the standard kit (the default), or with
        /// <c>--skill-kit library</c> its authored default loadout at <c>--skill-level</c>.
        /// </summary>
        private SkillLoadout BeastLoadout(int speciesIndex, KitMode mode)
        {
            if (_options.KitSource == KitSource.Library)
            {
                return SkillLoadout.FromInstances(mode == KitMode.Elemental ? _elementalLibraryKits[speciesIndex] : _neutralLibraryKits[speciesIndex]);
            }

            return Kit.Loadout(mode == KitMode.Elemental ? _elementalKits[speciesIndex] : _neutralKits[speciesIndex]);
        }

        private static void CountAvatar(PveBattle battle, BattleTurnResult turn, BattleUnit avatar)
        {
            if (avatar != null && turn.Unit == avatar)
            {
                battle.AvatarTurns++;
            }

            battle.AvatarCasts += turn.AvatarActivations.Count;
        }

        private static void CountPassives(PveBattle battle, IReadOnlyList<PassiveActivation> activations)
        {
            if (battle.PassiveFirings == null)
            {
                return;
            }

            foreach (PassiveActivation activation in activations)
            {
                battle.PassiveFirings[activation.SlotIndex]++;
            }
        }

        private static void CountFires(PveBattle battle, BattleTurnResult turn, int actor)
        {
            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (!outcome.Fired)
                {
                    continue;
                }

                if (Kit.IsPhysicalSingle(outcome.Skill))
                {
                    battle.MemberPhysicalFires[actor]++;
                }
                else if (Kit.IsBlast(outcome.Skill))
                {
                    battle.MemberSpecialFires[actor]++;
                }
                else if (Kit.IsBurstUse(outcome.Skill))
                {
                    battle.BurstFires++;
                    battle.BurstTargets += outcome.Activation == null || outcome.Activation.Targets == null ? 0 : outcome.Activation.Targets.Count;
                }
            }
        }

        /// <summary>
        /// Adds the actor's damage rolls this turn (every fired skill's <see cref="SkillActivation.Hits"/>)
        /// to its hit, crit and roll-multiplier totals. Read-only over the turn result, so it cannot
        /// change the battle.
        /// </summary>
        private static void CountRolls(PveBattle battle, BattleTurnResult turn, int actor)
        {
            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (!outcome.Fired || outcome.Activation == null)
                {
                    continue;
                }

                foreach (DamageHit hit in outcome.Activation.Hits)
                {
                    battle.MemberHits[actor]++;
                    battle.MemberCrits[actor] += hit.Roll.IsCrit ? 1 : 0;
                    battle.MemberRollMultiplier[actor] += (hit.Roll.IsCrit ? DamageFormula.CritMultiplier : 1.0) * hit.Roll.VariancePercent / 100.0;
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
        /// multiplier (rounded, floored at 1). Speed, MoveRange and CritChance do not: under the ATB
        /// gauge Speed is how many turns a unit gets, so scaling it would make the knob change the
        /// enemies' action economy rather than just their toughness and punch, move range is a small
        /// tactical integer, and crit chance is a probability that the knob should not turn into
        /// certainty.
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
        /// Where an encounter's enemies stand: <see cref="DeploymentPacker.TryPack"/> of their
        /// footprints in fixture order on an empty board of the encounter's arena, worked out once per
        /// encounter and shared by every battle against it. For one-tile enemies the anchors are
        /// exactly <see cref="FrontTiles"/> of the enemy zone. Throws when they do not fit (the loader
        /// and the generator refuse such lineups first).
        /// </summary>
        public static EnemyLayout Layout(Encounter encounter)
        {
            EnemyLayout layout = encounter.Layout;
            if (layout != null)
            {
                return layout;
            }

            List<UnitFootprint> footprints = new List<UnitFootprint>();
            foreach (EnemySlot slot in encounter.Enemies)
            {
                footprints.Add(slot.Species.Footprint);
            }

            layout = new EnemyLayout();
            if (!DeploymentPacker.TryPack(new HexGrid(encounter.Arena), BattleTeam.Enemy, footprints, layout.Anchors, layout.Covered))
            {
                throw new InvalidOperationException("The " + encounter.Enemies.Count + " enemies of '" + encounter.Id + "' do not fit the " + encounter.Arena +
                                                    " enemy deployment zone.");
            }

            encounter.Layout = layout;
            return layout;
        }

        /// <summary>
        /// The <paramref name="count"/> tiles of a team's deployment zone nearest the centre line:
        /// front row first (smallest |R|), then outward from the board's vertical centre line, then
        /// by Q (<see cref="DeploymentPacker.FrontOrder"/>). Both sides therefore start as close as
        /// the zones allow.
        /// </summary>
        public static List<HexCoordinate> FrontTiles(HexGrid grid, BattleTeam team, int count)
        {
            List<HexCoordinate> zone = DeploymentPacker.FrontOrder(grid, team);

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

        /// <summary>
        /// A per-battle seed that is a pure function of the inputs (the multiplier deliberately
        /// excluded, so calibration compares every multiplier on the same damage rolls).
        /// <paramref name="sample"/> separates a team's repeated battles; -1 (with team -1) is the
        /// tie-shuffle seed.
        /// </summary>
        private static int DeriveSeed(int seed, KitMode mode, int level, string encounterId, int teamIndex, int sample)
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
                hash = (hash * 486187739) + sample;
                return hash;
            }
        }
    }
}
