using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Scouting;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>The scouted-picking strategies a run reports (<c>--scouted</c>).</summary>
    [Flags]
    public enum ScoutStrategies
    {
        None = 0,

        /// <summary>A seeded random team per composition: the no-information control.</summary>
        Random = 1,

        /// <summary>The element counter-pick heuristic (<see cref="ScoutedPicker.Pick"/>).</summary>
        Heuristic = 2,

        /// <summary>The heuristic plus each team's active bonds (<see cref="ScoutedPicker.PickWithBonds"/>); bonds on only.</summary>
        BondAware = 4,

        /// <summary>Per composition, the team that did best against it in hindsight (and the best single team per cell).</summary>
        Oracle = 8,

        All = Random | Heuristic | BondAware | Oracle
    }

    /// <summary>The sim's thin adapter from a fixture enemy to what <see cref="EncounterPreview"/> reads.</summary>
    public sealed class EnemySlotPreviewSource : IEncounterPreviewSource
    {
        private readonly EnemySlot _slot;

        public EnemySlotPreviewSource(EnemySlot slot)
        {
            _slot = slot;
        }

        public Element Element
        {
            get { return _slot.Element; }
        }

        public CombatStance Stance
        {
            get { return _slot.Species.Stance; }
        }

        public string DisplayName
        {
            get { return _slot.GroupDisplayName; }
        }
    }

    /// <summary>One PvE cell's scouted picks: per strategy, the team fielded against each composition, and the clear rates that gives.</summary>
    public sealed class ScoutedCell
    {
        public PveCell Cell;

        /// <summary>
        /// Mean clear rate of every team over every composition: the unscouted average team (the
        /// no-scouting rate; what <c>--calibrate-on mean</c> aims at the target).
        /// </summary>
        public double Baseline;

        /// <summary>[strategy][composition] the team index picked (null when the strategy was not run).</summary>
        public int[][] Picks = new int[ScoutedPicker.StrategyCount][];

        /// <summary>[strategy] the clear rate of the picks: the mean over compositions of the picked team's rate against it.</summary>
        public double[] Rate = new double[ScoutedPicker.StrategyCount];

        /// <summary>[team] each team's clear rate over the whole cell.</summary>
        public double[] TeamRate;

        /// <summary>
        /// With <see cref="ScoutStrategies.Oracle"/>: the clear rate of the best single team, chosen
        /// without the per-composition view and held out (see <see cref="ScoutedPicker"/>, "Best team").
        /// </summary>
        public double BestFixed;

        /// <summary>The team behind <see cref="BestFixed"/>.</summary>
        public int BestFixedTeam;

        /// <summary>Whether <see cref="BestFixed"/> was chosen on other levels' battles (false = single level, in-sample).</summary>
        public bool BestFixedHeldOut;
    }

    /// <summary>
    /// Scouted picking: what the player gains by seeing an encounter (<see cref="EncounterPreview"/>)
    /// and picking a team for it. Pure post-processing of the battles a run already has: every
    /// strategy picks one of the simulated teams per composition, and that team's recorded result
    /// against the composition is its outcome, so no battle is run. Each strategy's gain over the mean
    /// of all teams (the no-scouting rate) reads as uplift. The heuristic pickers also drive the
    /// default calibration (<c>--calibrate-on bonds|heuristic</c>, <see cref="PicksFor"/>), which
    /// aims the picked team, not the mean, at the target.
    /// <para>
    /// <strong>Heuristic.</strong> Each beast scores against the preview, per enemy:
    /// <c>sum over groups of Count x (OffenceWeight x chart(beast element, group element) -
    /// DefenceWeight x chart(group element, beast elements)) / TotalEnemies</c> (chart =
    /// <see cref="ElementChart.GetMultiplier(Element, Element)"/>; a beast attacks with its first
    /// element, the kit element). The team is the <c>TeamSize</c> best scores (ties to the lower
    /// roster index); if it has fewer than <c>--scouted-vanguard-min</c> Vanguards, its lowest-scored
    /// non-Vanguard picks are swapped for the best unpicked Vanguards. No rng.
    /// </para>
    /// <para>
    /// <strong>Bond-aware.</strong> Every team meeting the Vanguard minimum scores its members'
    /// summed heuristic scores plus, per tier of each active tiered bond, that bond's weight
    /// (<see cref="TeamSuggester.BondWeights"/>, else <see cref="BondWeight"/>; a bond that reacts to afflicted
    /// allies weighs nothing against an encounter that cannot stun or burn, see
    /// <see cref="CanAfflict"/>), and <see cref="ScalingBondWeight"/> per stack of each active
    /// scaling bond (none for an <c>Others</c> bond on a team made only of its members, which has
    /// no recipient); the best team is fielded (ties to the lower team index). The scores, the bond
    /// weights and the choice rule are the game's own <see cref="TeamSuggester"/> (Runtime), so the
    /// team the game suggests is the team the difficulty is calibrated on; <see cref="SuggesterParity"/>
    /// checks that every run.
    /// </para>
    /// <para>
    /// <strong>Oracle.</strong> Per composition, the team with the best recorded clear rate against
    /// it (ties: the team's clear rate over the whole cell, then the lower team index): an upper
    /// bound, and with one battle per team and composition an inflated one (it keeps the luckiest
    /// of 210 coin flips).
    /// </para>
    /// <para>
    /// <strong>Best team.</strong> Reported with the oracle: the one lineup that did best in the same
    /// mode and shape at the <em>other</em> levels, scored at this level (<see cref="ScoutedCell.BestFixed"/>).
    /// Knowing which team is strong, not what it faces, and held out, so not inflated by the luck
    /// it was chosen on: the comparison that says whether counter-picking beats bringing a strong team.
    /// </para>
    /// </summary>
    public static class ScoutedPicker
    {
        public const int DefaultVanguardMin = 1;

        /// <summary>Weight of the beast's attack multiplier into each enemy (<see cref="TeamSuggester.OffenceWeight"/>).</summary>
        public const double OffenceWeight = TeamSuggester.OffenceWeight;

        /// <summary>Weight of each enemy's attack multiplier into the beast (<see cref="TeamSuggester.DefenceWeight"/>).</summary>
        public const double DefenceWeight = TeamSuggester.DefenceWeight;

        /// <summary>Bond-aware score per tier of an active bond not in <see cref="TeamSuggester.BondWeights"/> (<see cref="TeamSuggester.BondWeight"/>).</summary>
        public const double BondWeight = TeamSuggester.BondWeight;

        /// <summary>Bond-aware score per stack of each active scaling (<c>PerCount</c>) bond (<see cref="TeamSuggester.ScalingBondWeight"/>).</summary>
        public const double ScalingBondWeight = TeamSuggester.ScalingBondWeight;

        public const int StrategyCount = 4;
        public const int RandomIndex = 0;
        public const int HeuristicIndex = 1;
        public const int BondAwareIndex = 2;
        public const int OracleIndex = 3;

        public static readonly ScoutStrategies[] StrategyFlags = { ScoutStrategies.Random, ScoutStrategies.Heuristic, ScoutStrategies.BondAware, ScoutStrategies.Oracle };
        public static readonly string[] StrategyNames = { "Random", "Heuristic", "Heuristic + bonds", "Oracle" };

        /// <summary>Whether the run reports scouting at all.</summary>
        public static bool Active(SimOptions options)
        {
            return options.RunPve && options.Scouted != ScoutStrategies.None;
        }

        /// <summary>Whether strategy <paramref name="index"/> runs: asked for, and (bond-aware) bonds are on, else it would equal the heuristic.</summary>
        public static bool Runs(SimOptions options, int index)
        {
            return (options.Scouted & StrategyFlags[index]) != 0 && (index != BondAwareIndex || options.BondsActive);
        }

        public static EncounterPreview Preview(Encounter encounter, ScoutingDetail detail)
        {
            List<IEncounterPreviewSource> sources = new List<IEncounterPreviewSource>();
            foreach (EnemySlot slot in encounter.Enemies)
            {
                sources.Add(new EnemySlotPreviewSource(slot));
            }

            return EncounterPreview.Build(sources, encounter.Arena, detail);
        }

        /// <summary>[beast] the heuristic's per-enemy score against <paramref name="preview"/> (<see cref="TeamSuggester.ScoreBeasts"/>).</summary>
        public static double[] Scores(EncounterPreview preview, IReadOnlyList<CreatureSpeciesSO> species)
        {
            return TeamSuggester.ScoreBeasts(preview, species);
        }

        /// <summary>The heuristic team: ascending roster indices (see the class notes).</summary>
        public static int[] Pick(double[] scores, IReadOnlyList<CreatureSpeciesSO> species, int teamSize, int vanguardMin)
        {
            List<int> order = PveReport.Order(scores.Length, b => scores[b]);
            List<int> picked = order.GetRange(0, Math.Min(teamSize, order.Count));
            int vanguards = 0;
            foreach (int b in picked)
            {
                vanguards += species[b].Stance == CombatStance.Vanguard ? 1 : 0;
            }

            while (vanguards < vanguardMin)
            {
                int incoming = -1;
                foreach (int b in order)
                {
                    if (incoming < 0 && !picked.Contains(b) && species[b].Stance == CombatStance.Vanguard)
                    {
                        incoming = b;
                    }
                }

                int outgoing = -1;
                for (int i = picked.Count - 1; i >= 0 && outgoing < 0; i--)
                {
                    outgoing = species[picked[i]].Stance == CombatStance.Vanguard ? -1 : i;
                }

                if (incoming < 0 || outgoing < 0)
                {
                    break;
                }

                picked[outgoing] = incoming;
                picked.Sort((x, y) =>
                {
                    int byScore = scores[y].CompareTo(scores[x]);
                    return byScore != 0 ? byScore : x.CompareTo(y);
                });
                vanguards++;
            }

            int[] team = picked.ToArray();
            Array.Sort(team);
            return team;
        }

        /// <summary>What one active bond adds to a team's bond-aware score, against an encounter that can afflict the team (<see cref="TeamSuggester.BondScore"/>).</summary>
        public static double BondScore(ActiveTeamBond bond, int teamSize)
        {
            return TeamSuggester.BondScore(bond, teamSize, true);
        }

        /// <summary>
        /// Whether any enemy of <paramref name="encounter"/> carries an enemy-side skill that can stun
        /// or put damage-over-time on the team (<see cref="TeamSuggester.CanAfflict"/> over its kits, as
        /// the scouting preview's enemy types show).
        /// </summary>
        public static bool CanAfflict(Encounter encounter)
        {
            List<SkillSO> skills = new List<SkillSO>();
            foreach (EnemySlot slot in encounter.Enemies)
            {
                skills.AddRange(slot.ElementalKit);
            }

            return TeamSuggester.CanAfflict(skills);
        }

        /// <summary>The bond-aware team index (see the class notes), against an encounter that can afflict the team.</summary>
        public static int PickWithBonds(double[] scores, IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, List<ActiveTeamBond>[] bonds, int vanguardMin)
        {
            return PickWithBonds(scores, species, teams, bonds, vanguardMin, true);
        }

        /// <summary>
        /// The bond-aware team index among the simulator's <paramref name="teams"/>: the game's own
        /// choice rule, <see cref="TeamSuggester.SelectBest"/>, over the simulator's team list and its
        /// precomputed bonds.
        /// </summary>
        public static int PickWithBonds(double[] scores, IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, List<ActiveTeamBond>[] bonds, int vanguardMin,
                                        bool encounterAfflicts)
        {
            return TeamSuggester.SelectBest(teams, bonds, scores, species, vanguardMin, encounterAfflicts);
        }

        /// <summary>
        /// The game's <see cref="TeamSuggester.Suggest"/> for <paramref name="encounter"/>, given the whole
        /// roster (every species once, in roster order, at one level) as the owned beasts, the run's
        /// team size, Vanguard minimum and bonds: the team the game would suggest, as a simulator team
        /// index (-1 if it is none of them).
        /// </summary>
        public static int SuggestFor(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, Encounter encounter)
        {
            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>();
            foreach (CreatureSpeciesSO beast in species)
            {
                owned.Add(new TeamSuggestionCandidate(beast, options.Levels.Count > 0 ? options.Levels[0] : 1));
            }

            TeamSuggestion suggestion = TeamSuggester.Suggest(new TeamSuggestionRequest
            {
                Preview = Preview(encounter, options.ScoutedDetail),
                Owned = owned,
                TeamSize = options.TeamSize,
                MinVanguards = options.ScoutedVanguardMin,
                Bonds = options.BondsActive ? options.Library.TeamBonds : null,
                EncounterCanAfflict = CanAfflict(encounter)
            });

            string key = string.Join(",", suggestion.Members);
            for (int t = 0; t < teams.Count; t++)
            {
                if (Key(teams[t]) == key)
                {
                    return t;
                }
            }

            return -1;
        }

        /// <summary>
        /// How many of <paramref name="shapes"/>' compositions the game's <see cref="SuggestFor"/> gives
        /// exactly the simulator's bond-aware pick (<see cref="PickFor"/>) for, out of
        /// <paramref name="total"/>; both share the scoring and the choice rule, so anything short of
        /// every composition is a porting bug (the run fails).
        /// </summary>
        public static int SuggesterParity(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator, IEnumerable<EncounterShape> shapes,
                                          out int total)
        {
            int matches = 0;
            total = 0;
            foreach (EncounterShape shape in shapes)
            {
                foreach (Encounter encounter in shape.Compositions)
                {
                    total++;
                    int pick = PickFor(options, species, simulator.Teams, simulator.TeamBonds, encounter, BondAwareIndex);
                    matches += SuggestFor(options, species, simulator.Teams, encounter) == pick ? 1 : 0;
                }
            }

            return matches;
        }

        /// <summary>
        /// The team index <paramref name="strategy"/> (<see cref="HeuristicIndex"/> or
        /// <see cref="BondAwareIndex"/>) fields against <paramref name="encounter"/>: a pure function
        /// of what the preview shows, so the same in every kit mode and level.
        /// </summary>
        public static int PickFor(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, List<ActiveTeamBond>[] bonds, Encounter encounter,
                                  int strategy)
        {
            double[] scores = Scores(Preview(encounter, options.ScoutedDetail), species);
            if (strategy == BondAwareIndex)
            {
                return PickWithBonds(scores, species, teams, bonds, options.ScoutedVanguardMin, CanAfflict(encounter));
            }

            if (strategy != HeuristicIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(strategy), "Only the heuristic pickers pick from the preview alone.");
            }

            string key = Key(Pick(scores, species, options.TeamSize, options.ScoutedVanguardMin));
            for (int t = 0; t < teams.Count; t++)
            {
                if (Key(teams[t]) == key)
                {
                    return t;
                }
            }

            throw new InvalidOperationException("The heuristic picked " + key + ", which is not a simulated team.");
        }

        /// <summary>
        /// Per composition of <paramref name="shape"/>, the team index <paramref name="strategy"/>
        /// fields (<see cref="PickFor"/>): what a scouted-pick calibration
        /// (<c>--calibrate-on heuristic|bonds</c>) aims at the target, computed once per shape.
        /// </summary>
        public static int[] PicksFor(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, List<ActiveTeamBond>[] bonds, EncounterShape shape,
                                     int strategy)
        {
            int[] picks = new int[shape.Compositions.Count];
            for (int c = 0; c < picks.Length; c++)
            {
                picks[c] = PickFor(options, species, teams, bonds, shape.Compositions[c], strategy);
            }

            return picks;
        }

        /// <summary>The picker behind <paramref name="target"/> (<see cref="HeuristicIndex"/> or <see cref="BondAwareIndex"/>); -1 for the mean.</summary>
        public static int StrategyFor(CalibrationTarget target)
        {
            return target == CalibrationTarget.Bonds ? BondAwareIndex : target == CalibrationTarget.Heuristic ? HeuristicIndex : -1;
        }

        /// <summary>Team <paramref name="team"/>'s clear rate against composition <paramref name="composition"/> (percent, over the samples).</summary>
        public static double Rate(PveCell cell, int composition, int team)
        {
            int cleared = 0;
            for (int s = 0; s < cell.Samples; s++)
            {
                cleared += cell.Battles[(((composition * cell.TeamCount) + team) * cell.Samples) + s].Cleared ? 1 : 0;
            }

            return cell.Samples == 0 ? 0.0 : (100.0 * cleared) / cell.Samples;
        }

        /// <summary>Every cell's picks and rates, in <paramref name="cells"/>' order. Empty when scouting is off.</summary>
        public static List<ScoutedCell> Compute(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator, List<PveCell> cells)
        {
            List<ScoutedCell> result = new List<ScoutedCell>();
            if (!Active(options) || simulator == null)
            {
                return result;
            }

            // The heuristic sees only the composition, so its picks are shared by every mode and level.
            Dictionary<Encounter, int> heuristic = new Dictionary<Encounter, int>();
            Dictionary<Encounter, int> bondAware = new Dictionary<Encounter, int>();
            foreach (PveCell cell in cells)
            {
                foreach (Encounter encounter in cell.Shape.Compositions)
                {
                    if (heuristic.ContainsKey(encounter))
                    {
                        continue;
                    }

                    heuristic[encounter] = PickFor(options, species, simulator.Teams, simulator.TeamBonds, encounter, HeuristicIndex);
                    bondAware[encounter] = PickFor(options, species, simulator.Teams, simulator.TeamBonds, encounter, BondAwareIndex);
                }
            }

            foreach (PveCell cell in cells)
            {
                int compositions = cell.Shape.Compositions.Count;
                int teams = cell.TeamCount;
                TeamScope scope = TeamScope.FromCell(cell);
                ScoutedCell scouted = new ScoutedCell { Cell = cell };
                double sum = 0.0;
                foreach (double rate in scope.Rate)
                {
                    sum += rate;
                }

                scouted.Baseline = teams == 0 ? 0.0 : sum / teams;
                for (int k = 0; k < StrategyCount; k++)
                {
                    if (!Runs(options, k))
                    {
                        continue;
                    }

                    int[] picks = new int[compositions];
                    for (int c = 0; c < compositions; c++)
                    {
                        Encounter encounter = cell.Shape.Compositions[c];
                        switch (k)
                        {
                            case RandomIndex:
                                picks[c] = new Random(RandomSeed(options.Seed, cell, encounter.Id)).Next(teams);
                                break;
                            case HeuristicIndex:
                                picks[c] = heuristic[encounter];
                                break;
                            case BondAwareIndex:
                                picks[c] = bondAware[encounter];
                                break;
                            default:
                                picks[c] = Oracle(cell, c, scope.Rate);
                                break;
                        }
                    }

                    scouted.Picks[k] = picks;
                    double total = 0.0;
                    for (int c = 0; c < compositions; c++)
                    {
                        total += Rate(cell, c, picks[c]);
                    }

                    scouted.Rate[k] = compositions == 0 ? 0.0 : total / compositions;
                }

                scouted.TeamRate = scope.Rate;
                result.Add(scouted);
            }

            if (Runs(options, OracleIndex))
            {
                foreach (ScoutedCell scouted in result)
                {
                    BestFixed(scouted, result);
                }
            }

            return result;
        }

        /// <summary>
        /// The held-out best team: chosen by its clear rate in the same mode and shape at the other
        /// levels (ties: the same mode's other levels over every shape, then the lower index), and
        /// scored at this cell's level, so the choice never sees the battles it is scored on. With a
        /// single level it falls back to this cell's own best (in-sample, and so inflated).
        /// </summary>
        private static void BestFixed(ScoutedCell scouted, List<ScoutedCell> all)
        {
            PveCell cell = scouted.Cell;
            List<ScoutedCell> sameShape = all.FindAll(o => o.Cell.Mode == cell.Mode && o.Cell.Shape == cell.Shape && o.Cell.Level != cell.Level);
            List<ScoutedCell> anyShape = all.FindAll(o => o.Cell.Mode == cell.Mode && o.Cell.Level != cell.Level);
            scouted.BestFixedHeldOut = sameShape.Count > 0;
            if (sameShape.Count == 0)
            {
                sameShape.Add(scouted);
                anyShape.Add(scouted);
            }

            int teams = cell.TeamCount;
            double[] primary = new double[teams];
            double[] secondary = new double[teams];
            foreach (ScoutedCell other in sameShape)
            {
                for (int t = 0; t < teams; t++)
                {
                    primary[t] += other.TeamRate[t];
                }
            }

            foreach (ScoutedCell other in anyShape)
            {
                for (int t = 0; t < teams; t++)
                {
                    secondary[t] += other.TeamRate[t];
                }
            }

            int best = 0;
            for (int t = 1; t < teams; t++)
            {
                if (primary[t] > primary[best] + 1e-9 || (Math.Abs(primary[t] - primary[best]) <= 1e-9 && secondary[t] > secondary[best] + 1e-9))
                {
                    best = t;
                }
            }

            scouted.BestFixedTeam = best;
            scouted.BestFixed = teams == 0 ? 0.0 : scouted.TeamRate[best];
        }

        /// <summary>
        /// [beast] how many of <paramref name="scouted"/>'s picks by strategy <paramref name="index"/>
        /// field each beast (one pick per composition per cell).
        /// </summary>
        public static int[] PickCounts(IEnumerable<ScoutedCell> scouted, int index, List<int[]> teams, int speciesCount, out int picks)
        {
            int[] counts = new int[speciesCount];
            picks = 0;
            foreach (ScoutedCell cell in scouted)
            {
                foreach (int team in cell.Picks[index])
                {
                    picks++;
                    foreach (int b in teams[team])
                    {
                        counts[b]++;
                    }
                }
            }

            return counts;
        }

        /// <summary>
        /// Invariants over a run's scouting (empty = all hold): the oracle is at least the baseline and
        /// every other strategy in every cell, each picked team is a real team, the heuristic's picks
        /// meet the Vanguard minimum when the roster allows it, and each strategy's pick counts sum to
        /// compositions x team size per shape.
        /// </summary>
        public static List<string> Check(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator, List<PveCell> cells)
        {
            List<string> problems = new List<string>();
            List<ScoutedCell> scouted = Compute(options, species, simulator, cells);
            int vanguardsInRoster = 0;
            foreach (CreatureSpeciesSO beast in species)
            {
                vanguardsInRoster += beast.Stance == CombatStance.Vanguard ? 1 : 0;
            }

            int needed = Math.Min(options.ScoutedVanguardMin, vanguardsInRoster);
            foreach (ScoutedCell cell in scouted)
            {
                string where = "Scouting " + SimOptions.ModeName(cell.Cell.Mode) + "/" + cell.Cell.Shape.Id + "/L" + cell.Cell.Level + ": ";
                if (Runs(options, OracleIndex))
                {
                    double oracle = cell.Rate[OracleIndex];
                    if (oracle < cell.Baseline - 1e-9 || oracle < cell.BestFixed - 1e-9)
                    {
                        problems.Add(where + "oracle " + SimOptions.Format(oracle) + "% is below the baseline or the best fixed team.");
                    }

                    for (int k = 0; k < StrategyCount; k++)
                    {
                        if (k != OracleIndex && Runs(options, k) && cell.Rate[k] > oracle + 1e-9)
                        {
                            problems.Add(where + StrategyNames[k] + " " + SimOptions.Format(cell.Rate[k]) + "% beats the oracle.");
                        }
                    }
                }

                for (int k = 0; k < StrategyCount; k++)
                {
                    if (!Runs(options, k))
                    {
                        continue;
                    }

                    foreach (int team in cell.Picks[k])
                    {
                        if (team < 0 || team >= simulator.Teams.Count)
                        {
                            problems.Add(where + StrategyNames[k] + " picked no valid team.");
                            continue;
                        }

                        if (k == HeuristicIndex || k == BondAwareIndex)
                        {
                            int vanguards = 0;
                            foreach (int b in simulator.Teams[team])
                            {
                                vanguards += species[b].Stance == CombatStance.Vanguard ? 1 : 0;
                            }

                            if (vanguards < needed)
                            {
                                problems.Add(where + StrategyNames[k] + " fielded " + vanguards + " Vanguards, fewer than " + needed + ".");
                            }
                        }
                    }
                }
            }

            foreach (KitMode mode in options.Modes)
            {
                foreach (EncounterShape shape in ShapesOf(cells))
                {
                    List<ScoutedCell> inShape = scouted.FindAll(c => c.Cell.Mode == mode && c.Cell.Shape == shape);
                    for (int k = 0; k < StrategyCount; k++)
                    {
                        if (!Runs(options, k))
                        {
                            continue;
                        }

                        int[] counts = PickCounts(inShape, k, simulator.Teams, species.Count, out int picks);
                        int sum = 0;
                        foreach (int count in counts)
                        {
                            sum += count;
                        }

                        int expected = inShape.Count * shape.Compositions.Count * options.TeamSize;
                        if (picks != inShape.Count * shape.Compositions.Count || sum != expected)
                        {
                            problems.Add("Scouting " + SimOptions.ModeName(mode) + "/" + shape.Id + ": " + StrategyNames[k] + " pick counts sum to " + sum +
                                         ", expected " + expected + ".");
                        }
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Invariants over a scouted-pick calibration (empty = all hold, or <c>--calibrate-on mean</c>):
        /// every cell's picks are the picker's own for its shape (recomputed), real teams meeting the
        /// Vanguard minimum when the roster allows it, and the same in every mode and level of the
        /// shape; the picked battles are complete, their clear rate is the reported scouted rate, and
        /// each picked team's first sample is exactly its battle in the every-team run. With
        /// <paramref name="missIsProblem"/> (<c>--self-check</c>) a scouted rate further than
        /// <see cref="SimOptions.CalibrationTolerance"/> from the target is one too.
        /// </summary>
        public static List<string> CheckCalibration(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species, PveSimulator simulator, List<PveCell> cells,
                                                    bool missIsProblem)
        {
            List<string> problems = new List<string>();
            if (!options.CalibratesOnPick || simulator == null)
            {
                return problems;
            }

            int vanguardsInRoster = 0;
            foreach (CreatureSpeciesSO beast in species)
            {
                vanguardsInRoster += beast.Stance == CombatStance.Vanguard ? 1 : 0;
            }

            int needed = Math.Min(options.ScoutedVanguardMin, vanguardsInRoster);
            int strategy = StrategyFor(options.EffectiveCalibrateOn);
            Dictionary<EncounterShape, int[]> expected = new Dictionary<EncounterShape, int[]>();
            foreach (PveCell cell in cells)
            {
                string where = "Calibration " + SimOptions.ModeName(cell.Mode) + "/" + cell.Shape.Id + "/L" + cell.Level + ": ";
                int compositions = cell.Shape.Compositions.Count;
                int n = cell.CalibrationSamples;
                if (cell.CalibratedOn != options.EffectiveCalibrateOn || cell.Picks == null || cell.Picks.Length != compositions || cell.PickedBattles == null ||
                    n < 1 || cell.PickedBattles.Length != compositions * n)
                {
                    problems.Add(where + "the scouted-pick calibration left no complete set of picks and picked battles.");
                    continue;
                }

                if (!expected.TryGetValue(cell.Shape, out int[] picks))
                {
                    picks = PicksFor(options, species, simulator.Teams, simulator.TeamBonds, cell.Shape, strategy);
                    expected[cell.Shape] = picks;
                }

                int cleared = 0;
                for (int c = 0; c < compositions; c++)
                {
                    int team = cell.Picks[c];
                    if (team != picks[c])
                    {
                        problems.Add(where + "composition " + c + " fielded team " + team + ", but the picker picks " + picks[c] + ".");
                        continue;
                    }

                    if (team < 0 || team >= simulator.Teams.Count)
                    {
                        problems.Add(where + "picked no valid team for composition " + c + ".");
                        continue;
                    }

                    int vanguards = 0;
                    foreach (int b in simulator.Teams[team])
                    {
                        vanguards += species[b].Stance == CombatStance.Vanguard ? 1 : 0;
                    }

                    if (vanguards < needed)
                    {
                        problems.Add(where + "the pick for composition " + c + " fields " + vanguards + " Vanguards, fewer than " + needed + ".");
                    }

                    for (int s = 0; s < n; s++)
                    {
                        cleared += cell.PickedBattles[(c * n) + s].Cleared ? 1 : 0;
                    }

                    PveBattle first = cell.PickedBattles[c * n];
                    PveBattle all = cell.Battles[simulator.BattleIndex(c, team, 0)];
                    if (first.Outcome != all.Outcome || first.ElapsedTicks != all.ElapsedTicks || first.Actions != all.Actions)
                    {
                        problems.Add(where + "the picked team's first sample against composition " + c + " differs from its every-team battle.");
                    }
                }

                double rate = (100.0 * cleared) / (compositions * n);
                if (Math.Abs(rate - cell.ScoutedClearRate) > 1e-9)
                {
                    problems.Add(where + "picked battles clear " + SimOptions.Format(rate) + "%, but the scouted rate is " + SimOptions.Format(cell.ScoutedClearRate) + "%.");
                }

                double target = options.TargetFor(cell.Shape);
                if (missIsProblem && Math.Abs(cell.ScoutedClearRate - target) > SimOptions.CalibrationTolerance && !IsStep(cell, target))
                {
                    problems.Add(where + "the scouted rate " + SimOptions.Format(cell.ScoutedClearRate) + "% misses the " + SimOptions.Format(target) +
                                 "% target by more than " + SimOptions.Format(SimOptions.CalibrationTolerance) + " points.");
                }
            }

            return problems;
        }

        /// <summary>
        /// Whether a calibration miss is a step in the clear-rate curve that no multiplier splits
        /// (the report's <c>!</c>): the search evaluated a multiplier above the target rate and one
        /// below it within <see cref="StepWidth"/> of each other, so the rate jumps across the target
        /// between two multipliers that differ by little more than one stat rounding (typical at
        /// level 1, where the picked teams all meet the same few integer stat lines). A miss that is
        /// not a step means the search failed.
        /// </summary>
        public static bool IsStep(PveCell cell, double target)
        {
            foreach (CalibrationPoint above in cell.Evaluations)
            {
                if (above.Final || above.ClearRate <= target)
                {
                    continue;
                }

                foreach (CalibrationPoint below in cell.Evaluations)
                {
                    if (!below.Final && below.ClearRate < target && Math.Abs(below.Multiplier - above.Multiplier) <= StepWidth * Math.Min(above.Multiplier, below.Multiplier))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Relative multiplier width within which a jump across the target counts as a step (<see cref="IsStep"/>).</summary>
        public const double StepWidth = 0.01;

        /// <summary>The distinct shapes of <paramref name="cells"/>, in first-seen order.</summary>
        public static List<EncounterShape> ShapesOf(List<PveCell> cells)
        {
            List<EncounterShape> shapes = new List<EncounterShape>();
            foreach (PveCell cell in cells)
            {
                if (!shapes.Contains(cell.Shape))
                {
                    shapes.Add(cell.Shape);
                }
            }

            return shapes;
        }

        private static int Oracle(PveCell cell, int composition, double[] cellRate)
        {
            int best = 0;
            double bestRate = double.MinValue;
            for (int t = 0; t < cell.TeamCount; t++)
            {
                double rate = Rate(cell, composition, t);
                if (rate > bestRate + 1e-9 || (Math.Abs(rate - bestRate) <= 1e-9 && cellRate[t] > cellRate[best] + 1e-9))
                {
                    best = t;
                    bestRate = rate;
                }
            }

            return best;
        }

        private static string Key(int[] team)
        {
            return string.Join(",", team);
        }

        /// <summary>The random pick's seed: a pure function of the base seed, the cell and the composition (never <c>string.GetHashCode</c>).</summary>
        private static int RandomSeed(int seed, PveCell cell, string encounterId)
        {
            unchecked
            {
                int hash = (seed * 486187739) + 0x5c0;
                hash = (hash * 486187739) + (int)cell.Mode;
                hash = (hash * 486187739) + cell.Level;
                foreach (char c in encounterId)
                {
                    hash = (hash * 486187739) + c;
                }

                return hash;
            }
        }
    }
}
