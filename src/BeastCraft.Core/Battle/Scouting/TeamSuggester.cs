using System;
using System.Collections.Generic;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// One beast the player could field: its species and its level. The input to
    /// <see cref="TeamSuggester.Suggest"/>; the caller maps its owned beasts (the save's
    /// <c>OwnedBeast</c>, through the species catalog) onto these. Immutable.
    /// </summary>
    public sealed class TeamSuggestionCandidate
    {
        public TeamSuggestionCandidate(CreatureSpeciesSO species, int level)
        {
            Species = species;
            Level = level;
        }

        /// <summary>The beast's species; a null species is never suggested.</summary>
        public CreatureSpeciesSO Species { get; }

        public int Level { get; }
    }

    /// <summary>What <see cref="TeamSuggester.Suggest"/> reads. Every field has a usable default except <see cref="Preview"/> and <see cref="Owned"/>.</summary>
    public sealed class TeamSuggestionRequest
    {
        /// <summary>The encounter as the player sees it before the fight (normally <see cref="ScoutingDetail.Full"/>).</summary>
        public EncounterPreview Preview;

        /// <summary>The beasts the player owns, in the order the caller wants ties broken (earlier wins).</summary>
        public IReadOnlyList<TeamSuggestionCandidate> Owned;

        /// <summary>Beasts per team (the battle format's size); fewer when the player owns fewer distinct species.</summary>
        public int TeamSize = 4;

        /// <summary>Fewest Vanguards a suggested team fields, when the owned beasts allow it.</summary>
        public int MinVanguards = TeamSuggester.DefaultMinVanguards;

        /// <summary>The team bond library (the skill library's <c>TeamBonds</c>, in library order); null or empty = no bonds.</summary>
        public IReadOnlyList<TeamBondSO> Bonds;

        /// <summary>
        /// Whether any enemy of the encounter can stun or put damage over time on the team
        /// (<see cref="TeamSuggester.CanAfflict"/> over the enemies' kits). A bond that answers only
        /// afflicted allies is worth nothing when it is false. Defaults to true (the cautious answer).
        /// </summary>
        public bool EncounterCanAfflict = true;
    }

    /// <summary>A suggested team: which candidates, what it scored and which bonds it activates.</summary>
    public sealed class TeamSuggestion
    {
        public TeamSuggestion(IReadOnlyList<int> members, IReadOnlyList<CreatureSpeciesSO> species, double score, IReadOnlyList<ActiveTeamBond> bonds, bool meetsVanguardMin)
        {
            Members = members;
            Species = species;
            Score = score;
            Bonds = bonds;
            MeetsVanguardMin = meetsVanguardMin;
        }

        /// <summary>Indices into <see cref="TeamSuggestionRequest.Owned"/>, ascending; empty when nothing could be suggested.</summary>
        public IReadOnlyList<int> Members { get; }

        /// <summary>The members' species, in <see cref="Members"/> order.</summary>
        public IReadOnlyList<CreatureSpeciesSO> Species { get; }

        /// <summary>The team's score (<see cref="TeamSuggester"/>, "Scoring"); higher is better.</summary>
        public double Score { get; }

        /// <summary>The bonds the team activates (member indices into <see cref="Species"/>).</summary>
        public IReadOnlyList<ActiveTeamBond> Bonds { get; }

        /// <summary>False when the owned beasts hold too few Vanguards for <see cref="TeamSuggestionRequest.MinVanguards"/>.</summary>
        public bool MeetsVanguardMin { get; }
    }

    /// <summary>
    /// Suggests a counter team for an encounter from the beasts the player owns, reading only the
    /// pre-fight <see cref="EncounterPreview"/>. This is the balance simulator's bond-aware scouted
    /// picker (<c>Tooling/BalanceSim</c>, README "Scouted picking"), which calibrates the game's
    /// difficulty table: the simulator calls this class, so the team the game suggests is the team the
    /// difficulty was tuned against. Whether the game shows a suggestion at all is
    /// <see cref="TeamSuggestionPolicy"/>'s decision. Pure and deterministic: no rng, no battle state.
    /// See the battle-system design doc, "Encounter preview".
    /// <para>
    /// <strong>Scoring.</strong> Each beast scores, per enemy,
    /// <c>sum over groups of Count x (OffenceWeight x chart(beast element, group element) -
    /// DefenceWeight x chart(group element, beast elements)) / TotalEnemies</c> (a beast attacks with
    /// its first element, its kit element), plus <see cref="LevelWeight"/> per level below the
    /// highest-levelled candidate (nothing when every candidate has the same level, as in the
    /// simulator). A team scores its members' scores plus, per active bond
    /// (<see cref="TeamBondResolver"/>), <see cref="BondScore"/>.
    /// </para>
    /// <para>
    /// <strong>Choice.</strong> Every combination of <c>TeamSize</c> distinct species is considered, in
    /// lexicographic order of each species' first position in <c>Owned</c>; a species owned twice
    /// counts once, as its highest-levelled copy (ties to the earlier one). The best team with at least
    /// <c>MinVanguards</c> Vanguards wins, ties (within 1e-12) to the earlier combination; when no
    /// team can meet the minimum, the best team overall.
    /// </para>
    /// <para>
    /// <strong>Cost.</strong> C(distinct species, team size) bond resolutions: 210 for the ten-species
    /// starter roster and a team of four. A much larger collection should prune to the best-scored
    /// beasts first (not needed yet).
    /// </para>
    /// </summary>
    public static class TeamSuggester
    {
        public const int DefaultMinVanguards = 1;

        /// <summary>Weight of the beast's attack multiplier into each enemy.</summary>
        public const double OffenceWeight = 1.0;

        /// <summary>Weight of each enemy's attack multiplier into the beast.</summary>
        public const double DefenceWeight = 0.5;

        /// <summary>Score per tier of an active tiered bond not in <see cref="BondWeights"/> (per-enemy score units: a 2x matchup scores 1.0 more than a 1x one).</summary>
        public const double BondWeight = 0.5;

        /// <summary>Score per stack of an active scaling (<c>PerCount</c>) bond.</summary>
        public const double ScalingBondWeight = 0.125;

        /// <summary>
        /// Score per level a candidate is below the highest-levelled candidate. A starting knob, not
        /// tuned: ten levels weigh as much as a 2x element matchup against the whole encounter. It
        /// never applies in the simulator, whose beasts share one level.
        /// </summary>
        public const double LevelWeight = 0.1;

        /// <summary>
        /// Score per tier, by <c>BondId</c>, for the behaviour bonds: what the pick credits each bond
        /// with, fitted to its composition-panel excess in the balance simulator (see the tuning log,
        /// "Behaviour bonds and tiered difficulty"). A bond not listed weighs <see cref="BondWeight"/>.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double> BondWeights = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            // 0.1 per point of pooled elemental panel excess (5-seed mean at --target-clear 50), none below 0; at 0.25 per point
            // the pick leaned on winter_grove over better element matchups and trailed the plain heuristic.
            { "guardian", 0.0 },
            { "pack_hunters", 0.015 },
            { "crossfire", 0.0 },
            { "wildfire", 0.02 },
            { "storm_front", 0.02 },
            { "bedrock", 0.1 },
            { "winter_grove", 0.34 },
            { "twilight", 0.0 },
            { "combined_arms", 0.005 },
        };

        /// <summary>The per-enemy element score of <paramref name="species"/> against <paramref name="preview"/> (see the class notes); 0 for an empty preview.</summary>
        public static double ScoreBeast(EncounterPreview preview, CreatureSpeciesSO species)
        {
            if (preview == null || preview.TotalEnemies == 0 || species == null)
            {
                return 0.0;
            }

            Element[] elements = species.Elements ?? new Element[0];
            Element attack = elements.Length > 0 ? elements[0] : Element.None;
            double sum = 0.0;
            foreach (EncounterPreviewGroup group in preview.Groups)
            {
                sum += group.Count * ((OffenceWeight * ElementChart.GetMultiplier(attack, group.Element)) -
                                      (DefenceWeight * ElementChart.GetMultiplier(group.Element, elements)));
            }

            return sum / preview.TotalEnemies;
        }

        /// <summary>[i] <see cref="ScoreBeast"/> of <paramref name="species"/>[i].</summary>
        public static double[] ScoreBeasts(EncounterPreview preview, IReadOnlyList<CreatureSpeciesSO> species)
        {
            double[] scores = new double[species == null ? 0 : species.Count];
            for (int b = 0; b < scores.Length; b++)
            {
                scores[b] = ScoreBeast(preview, species[b]);
            }

            return scores;
        }

        /// <summary>
        /// What one active bond adds to a team's score: its <see cref="BondWeights"/> entry (else
        /// <see cref="BondWeight"/>) x tier for a tiered bond, nothing for a tiered bond whose reaction
        /// answers only afflicted allies when the encounter cannot afflict
        /// (<paramref name="encounterAfflicts"/>), <see cref="ScalingBondWeight"/> x stacks for a
        /// scaling bond, and nothing for an <c>Others</c> scaling bond whose members are the whole team
        /// (no one receives it).
        /// </summary>
        public static double BondScore(ActiveTeamBond bond, int teamSize, bool encounterAfflicts)
        {
            if (bond == null || bond.Bond == null)
            {
                return 0.0;
            }

            if (!bond.Bond.PerCount)
            {
                TeamBondTier tier = bond.TierDefinition;
                if (!encounterAfflicts && tier != null && tier.HasReaction && tier.Reaction.Trigger == BondTrigger.AllyTurnStartAfflicted)
                {
                    return 0.0;
                }

                return (BondWeights.TryGetValue(bond.Bond.BondId ?? string.Empty, out double weight) ? weight : BondWeight) * bond.Tier;
            }

            if (bond.Bond.Scope == TeamBondScope.Others && bond.Members.Count >= teamSize)
            {
                return 0.0;
            }

            return ScalingBondWeight * bond.Stacks;
        }

        /// <summary>
        /// Whether any of <paramref name="enemySkills"/> (the encounter's enemy kits) is an enemy-side
        /// skill that can stun or put damage over time on the team.
        /// </summary>
        public static bool CanAfflict(IEnumerable<SkillSO> enemySkills)
        {
            if (enemySkills == null)
            {
                return false;
            }

            foreach (SkillSO skill in enemySkills)
            {
                if (skill == null || skill.TargetSide != SkillTargetSide.Enemy || skill.Effects == null)
                {
                    continue;
                }

                foreach (SkillEffect effect in skill.Effects)
                {
                    if (effect != null && effect.EffectType == SkillEffectType.ApplyStatus &&
                        (effect.Status == StatusType.Stun || effect.Status == StatusType.DamageOverTime))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The choice rule shared by <see cref="Suggest"/> and the balance simulator: the index of the
        /// best of <paramref name="teams"/> (each an array of indices into <paramref name="scores"/> and
        /// <paramref name="species"/>), scored as its members' <paramref name="scores"/> plus
        /// <see cref="BondScore"/> of each of its <paramref name="bonds"/>; the best meeting
        /// <paramref name="minVanguards"/>, ties to the lower index, else the best overall; -1 for no
        /// teams.
        /// </summary>
        public static int SelectBest(IReadOnlyList<int[]> teams, IReadOnlyList<IReadOnlyList<ActiveTeamBond>> bonds, IReadOnlyList<double> scores,
                                     IReadOnlyList<CreatureSpeciesSO> species, int minVanguards, bool encounterAfflicts)
        {
            int best = -1;
            double bestValue = double.MinValue;
            int bestFeasible = -1;
            double bestFeasibleValue = double.MinValue;
            for (int t = 0; t < teams.Count; t++)
            {
                double value = TeamValue(teams[t], bonds[t], scores, encounterAfflicts);
                int vanguards = 0;
                foreach (int b in teams[t])
                {
                    vanguards += species[b].Stance == CombatStance.Vanguard ? 1 : 0;
                }

                if (value > bestValue + 1e-12)
                {
                    best = t;
                    bestValue = value;
                }

                if (vanguards >= minVanguards && value > bestFeasibleValue + 1e-12)
                {
                    bestFeasible = t;
                    bestFeasibleValue = value;
                }
            }

            return bestFeasible >= 0 ? bestFeasible : best;
        }

        /// <summary>The suggested team for <paramref name="request"/> (see the class notes). Never null; empty members when nothing can be fielded.</summary>
        public static TeamSuggestion Suggest(TeamSuggestionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // One candidate per species: the highest-levelled copy, ties to the earlier one.
            List<int> distinct = new List<int>();
            if (request.Owned != null)
            {
                for (int i = 0; i < request.Owned.Count; i++)
                {
                    TeamSuggestionCandidate candidate = request.Owned[i];
                    if (candidate == null || candidate.Species == null)
                    {
                        continue;
                    }

                    int same = distinct.FindIndex(d => request.Owned[d].Species == candidate.Species);
                    if (same < 0)
                    {
                        distinct.Add(i);
                    }
                    else if (candidate.Level > request.Owned[distinct[same]].Level)
                    {
                        distinct[same] = i;
                    }
                }
            }

            int size = Math.Min(Math.Max(request.TeamSize, 0), distinct.Count);
            if (size == 0)
            {
                return new TeamSuggestion(new int[0], new CreatureSpeciesSO[0], 0.0, new ActiveTeamBond[0], request.MinVanguards <= 0);
            }

            List<CreatureSpeciesSO> species = new List<CreatureSpeciesSO>();
            int topLevel = int.MinValue;
            foreach (int i in distinct)
            {
                species.Add(request.Owned[i].Species);
                topLevel = Math.Max(topLevel, request.Owned[i].Level);
            }

            double[] scores = ScoreBeasts(request.Preview, species);
            for (int d = 0; d < distinct.Count; d++)
            {
                int below = topLevel - request.Owned[distinct[d]].Level;
                if (below > 0)
                {
                    scores[d] -= LevelWeight * below;
                }
            }

            List<int[]> teams = Combinations(distinct.Count, size);
            List<IReadOnlyList<ActiveTeamBond>> bonds = new List<IReadOnlyList<ActiveTeamBond>>(teams.Count);
            foreach (int[] team in teams)
            {
                bonds.Add(ResolveBonds(request.Bonds, team, species));
            }

            int best = SelectBest(teams, bonds, scores, species, request.MinVanguards, request.EncounterCanAfflict);
            int[] chosen = teams[best];
            int[] members = new int[chosen.Length];
            CreatureSpeciesSO[] memberSpecies = new CreatureSpeciesSO[chosen.Length];
            int vanguards = 0;
            for (int m = 0; m < chosen.Length; m++)
            {
                members[m] = distinct[chosen[m]];
                memberSpecies[m] = species[chosen[m]];
                vanguards += memberSpecies[m].Stance == CombatStance.Vanguard ? 1 : 0;
            }

            // A later, higher-levelled copy keeps its species' first position in the order, so sort the
            // members by candidate index and resolve the bonds again in that order (same bonds, same
            // score; only the member indices follow the order).
            Array.Sort(members, memberSpecies);
            return new TeamSuggestion(members, memberSpecies, TeamValue(chosen, bonds[best], scores, request.EncounterCanAfflict),
                                      ResolveBonds(request.Bonds, memberSpecies), vanguards >= request.MinVanguards);
        }

        /// <summary>Every combination of <paramref name="size"/> of 0..<paramref name="count"/>-1, ascending, in lexicographic order.</summary>
        public static List<int[]> Combinations(int count, int size)
        {
            List<int[]> result = new List<int[]>();
            if (size < 0 || size > count)
            {
                return result;
            }

            int[] current = new int[size];
            for (int i = 0; i < size; i++)
            {
                current[i] = i;
            }

            while (true)
            {
                result.Add((int[])current.Clone());
                int at = size - 1;
                while (at >= 0 && current[at] == count - size + at)
                {
                    at--;
                }

                if (at < 0)
                {
                    return result;
                }

                current[at]++;
                for (int i = at + 1; i < size; i++)
                {
                    current[i] = current[i - 1] + 1;
                }
            }
        }

        private static double TeamValue(int[] team, IReadOnlyList<ActiveTeamBond> bonds, IReadOnlyList<double> scores, bool encounterAfflicts)
        {
            double value = 0.0;
            foreach (int b in team)
            {
                value += scores[b];
            }

            if (bonds != null)
            {
                foreach (ActiveTeamBond bond in bonds)
                {
                    value += BondScore(bond, team.Length, encounterAfflicts);
                }
            }

            return value;
        }

        private static List<ActiveTeamBond> ResolveBonds(IReadOnlyList<TeamBondSO> library, int[] team, IReadOnlyList<CreatureSpeciesSO> species)
        {
            CreatureSpeciesSO[] members = new CreatureSpeciesSO[team.Length];
            for (int m = 0; m < team.Length; m++)
            {
                members[m] = species[team[m]];
            }

            return ResolveBonds(library, members);
        }

        private static List<ActiveTeamBond> ResolveBonds(IReadOnlyList<TeamBondSO> library, IReadOnlyList<CreatureSpeciesSO> members)
        {
            if (library == null || library.Count == 0)
            {
                return new List<ActiveTeamBond>();
            }

            return TeamBondResolver.Resolve(library, TeamBondResolver.MembersOf(members));
        }
    }
}
