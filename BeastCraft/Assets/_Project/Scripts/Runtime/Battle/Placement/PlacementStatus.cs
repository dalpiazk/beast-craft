using System;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// Everything wrong with one proposed starting position, or <see cref="Valid"/> when nothing
    /// is. Recorded per position in <see cref="PlacementOutcome"/>.
    /// <para>
    /// <strong>Why this is a flags enum</strong> when <see cref="BattleSkillStatus"/> — the other
    /// per-item status in this namespace — deliberately is not. That one describes situations that
    /// are genuinely mutually exclusive: a skill that found no candidate anywhere did not also run
    /// out of movement. These are not. One tile can sit in the wrong half of the board *and* be
    /// buried under terrain *and* already be spoken for, all at once, and the whole point of
    /// validating a layout is to hand back every reason it is illegal rather than making the caller
    /// fix one, re-submit, and discover the next. A single-value status would either lose the other
    /// reasons or need an arbitrary precedence rule to pick a winner.
    /// </para>
    /// <para>
    /// <strong>The one short-circuit.</strong> <see cref="OutOfBounds"/> is never combined with
    /// <see cref="Blocked"/> or <see cref="OutsideDeploymentZone"/>. Off the board those two are
    /// not additional facts: <see cref="Grid.HexGrid.IsBlocked"/> reads any off-board tile as
    /// blocked by design, and no off-board tile is in anybody's zone, so reporting them alongside
    /// would be noise that says nothing the caller can act on. Everything else is reported
    /// together, including duplicate-tile and unit-id problems, which are facts about the proposal
    /// rather than about the board and so stay meaningful even for a tile that does not exist.
    /// </para>
    /// </summary>
    [Flags]
    public enum PlacementStatus
    {
        /// <summary>Nothing is wrong with this position. The zero value, so no flag is set.</summary>
        Valid = 0,

        /// <summary>
        /// The tile is not on this board at all. Suppresses <see cref="Blocked"/> and
        /// <see cref="OutsideDeploymentZone"/>, per the note above.
        /// </summary>
        OutOfBounds = 1 << 0,

        /// <summary>Terrain sits on the tile, so nothing may stand there.</summary>
        Blocked = 1 << 1,

        /// <summary>
        /// The tile is on the board but outside the validating team's deployment zone — either in
        /// the neutral band across the middle or on the opposing side entirely. See
        /// <see cref="Grid.HexGrid.IsInDeploymentZone"/>.
        /// </summary>
        OutsideDeploymentZone = 1 << 2,

        /// <summary>
        /// This same tile already appeared earlier in this proposal. Set on the second and every
        /// later occurrence, never on the first — the first is a perfectly good placement, and it
        /// is the repeats that have nowhere to stand.
        /// </summary>
        DuplicateTile = 1 << 3,

        /// <summary>
        /// The tile is already spoken for by somebody outside this proposal: it appears in the
        /// <c>alreadyPlaced</c> list, or a unit is standing on it on the grid right now.
        /// </summary>
        Collision = 1 << 4,

        /// <summary>
        /// The placement carries no unit id, so there is nothing to key grid occupancy by. Only
        /// ever produced by the <see cref="PlacementRequest"/> overload of
        /// <see cref="PlacementValidator.Validate(Grid.HexGrid, BattleTeam, BattleFormat, System.Collections.Generic.IReadOnlyList{PlacementRequest}, System.Collections.Generic.IReadOnlyList{Grid.HexCoordinate})"/>;
        /// the coordinate-only overload is not given ids and never sets this.
        /// </summary>
        MissingUnitId = 1 << 5,

        /// <summary>
        /// The same unit id already appeared earlier in this proposal, which would ask one beast to
        /// stand in two places. Set on the second and every later occurrence, like
        /// <see cref="DuplicateTile"/>. Id-carrying overloads only, per
        /// <see cref="MissingUnitId"/>.
        /// </summary>
        DuplicateUnitId = 1 << 6
    }
}
