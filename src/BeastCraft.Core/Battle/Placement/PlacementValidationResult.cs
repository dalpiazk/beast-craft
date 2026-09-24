using System.Collections.Generic;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// Whether a proposed starting layout for one team is legal, and — when it is not — every
    /// reason it is not, at once.
    /// <para>
    /// The return shape of <see cref="PlacementValidator.Validate(Grid.HexGrid, BattleTeam, BattleFormat, IReadOnlyList{Grid.HexCoordinate}, IReadOnlyList{Grid.HexCoordinate})"/>,
    /// and a rich result rather than a <c>bool</c> for the same reason
    /// <see cref="BattleResult"/> and <see cref="BattleSkillOutcome"/> are: the interesting fact is
    /// never just pass or fail. A layout is rejected per tile and per rule, and the caller that has
    /// to explain the rejection — a placement UI, eventually — needs to point at the offending
    /// markers rather than print "invalid".
    /// </para>
    /// <para>
    /// Purely a record. Producing it changes nothing: validation is non-mutating, and no unit is on
    /// the board because of it. Committing a layout is
    /// <see cref="PlacementValidator.TryPlaceAll"/>'s job.
    /// </para>
    /// </summary>
    public class PlacementValidationResult
    {
        private readonly List<PlacementOutcome> _invalidOutcomes;

        public PlacementValidationResult(
            BattleTeam team,
            BattleFormat format,
            int maxPartySize,
            PlacementCountStatus countStatus,
            IReadOnlyList<PlacementOutcome> outcomes)
        {
            Team = team;
            Format = format;
            MaxPartySize = maxPartySize;
            CountStatus = countStatus;
            Outcomes = outcomes ?? new List<PlacementOutcome>();

            _invalidOutcomes = new List<PlacementOutcome>();
            for (int i = 0; i < Outcomes.Count; i++)
            {
                if (!Outcomes[i].IsValid)
                {
                    _invalidOutcomes.Add(Outcomes[i]);
                }
            }
        }

        /// <summary>The side whose deployment zone the proposal was judged against.</summary>
        public BattleTeam Team { get; }

        /// <summary>The format whose party size the count was judged against.</summary>
        public BattleFormat Format { get; }

        /// <summary>
        /// <see cref="Format"/>'s <see cref="BattleFormatExtensions.MaxPartySize"/>, resolved once
        /// and carried so a caller reporting a count error does not have to look it up again.
        /// </summary>
        public int MaxPartySize { get; }

        /// <summary>Whether the proposal fields a legal number of beasts.</summary>
        public PlacementCountStatus CountStatus { get; }

        /// <summary>
        /// One entry per proposed position, in the order they were proposed — valid ones included,
        /// so the list lines up index for index with what was passed in. Never <c>null</c>; empty
        /// only when the proposal itself was, in which case <see cref="CountStatus"/> carries the
        /// failure.
        /// </summary>
        public IReadOnlyList<PlacementOutcome> Outcomes { get; }

        /// <summary>
        /// Just the positions that broke something, in proposal order. A convenience over filtering
        /// <see cref="Outcomes"/>, built once during construction. Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<PlacementOutcome> InvalidOutcomes
        {
            get { return _invalidOutcomes; }
        }

        /// <summary>
        /// True only when the count is legal <em>and</em> every position is. This is the whole
        /// verdict, and it is what <see cref="PlacementValidator.TryPlaceAll"/> gates on: a
        /// proposal with one bad tile among six places nobody.
        /// </summary>
        public bool IsValid
        {
            get { return CountStatus == PlacementCountStatus.Valid && _invalidOutcomes.Count == 0; }
        }
    }
}
