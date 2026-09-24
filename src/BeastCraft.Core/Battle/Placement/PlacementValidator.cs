using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// Decides whether a proposed set of starting positions for one team is a legal pre-battle
    /// layout, and — separately — commits one that is.
    /// <para>
    /// This is decision 2's placement phase reduced to its one mechanical question. It answers
    /// "would this layout be allowed", nothing more: it does not choose where to put anybody, and
    /// there is no placement AI here or anywhere else. A proposal comes from outside — from a
    /// player dragging markers, once a UI exists, or from encounter data for the enemy side.
    /// </para>
    /// <para>
    /// <strong>Validation never touches the board.</strong> <see cref="Validate(HexGrid, BattleTeam, BattleFormat, IReadOnlyList{HexCoordinate}, IReadOnlyList{HexCoordinate})"/>
    /// reads the grid's bounds, terrain, occupancy and deployment zones and writes none of them, so
    /// it is safe to call on every drag of a marker. Committing is the deliberately separate step
    /// <see cref="TryPlaceAll"/>, which validates first and is all-or-nothing: a batch with one
    /// illegal tile in it places nobody rather than seating the legal five and leaving the caller
    /// to unpick a half-built layout.
    /// </para>
    /// <para>
    /// Static and stateless, like <see cref="SkillTargetResolver"/> and
    /// <see cref="HexPathfinder"/>: it owns no state of its own and every answer is a pure function
    /// of the grid and the proposal it is handed.
    /// </para>
    /// </summary>
    public static class PlacementValidator
    {
        /// <summary>
        /// Checks a proposed layout given as bare tiles, for a caller that has not yet paired
        /// beasts with positions. Identical to the <see cref="PlacementRequest"/> overload except
        /// that with no ids to inspect it can never report
        /// <see cref="PlacementStatus.MissingUnitId"/> or
        /// <see cref="PlacementStatus.DuplicateUnitId"/>.
        /// <para>
        /// <paramref name="alreadyPlaced"/> is every tile outside this proposal that is already
        /// spoken for and must not be landed on — in practice the other side's committed layout,
        /// chosen but not yet written to the grid. It deliberately does not assume whose those
        /// tiles are, so re-validating a team against its own committed tiles is possible; it is
        /// also not the only source of collisions, since tiles a unit is standing on <em>on the
        /// grid</em> are rejected whether or not the caller remembered to list them. May be
        /// <c>null</c> for "nobody else has been placed yet".
        /// </para>
        /// </summary>
        public static PlacementValidationResult Validate(
            HexGrid grid,
            BattleTeam team,
            BattleFormat format,
            IReadOnlyList<HexCoordinate> proposedPositions,
            IReadOnlyList<HexCoordinate> alreadyPlaced)
        {
            int count = proposedPositions == null ? 0 : proposedPositions.Count;
            PlacementRequest[] requests = new PlacementRequest[count];
            for (int i = 0; i < count; i++)
            {
                requests[i] = new PlacementRequest(null, proposedPositions[i]);
            }

            return ValidateCore(grid, team, format, requests, alreadyPlaced, false);
        }

        /// <summary>
        /// Checks a proposed layout that already names which beast stands where, additionally
        /// rejecting a placement with no unit id and a unit id used twice. This is what
        /// <see cref="TryPlaceAll"/> runs, and the overload to use once beasts and tiles have been
        /// paired up.
        /// <para>
        /// <paramref name="alreadyPlaced"/> means exactly what it does on the other overload.
        /// </para>
        /// </summary>
        public static PlacementValidationResult Validate(
            HexGrid grid,
            BattleTeam team,
            BattleFormat format,
            IReadOnlyList<PlacementRequest> proposedPlacements,
            IReadOnlyList<HexCoordinate> alreadyPlaced)
        {
            return ValidateCore(grid, team, format, proposedPlacements, alreadyPlaced, true);
        }

        /// <summary>
        /// Validates a proposed layout and, only if the whole of it is legal, seats every beast in
        /// it on the grid. Returns whether it committed, with the full verdict in
        /// <paramref name="result"/> either way.
        /// <para>
        /// <strong>All or nothing.</strong> Nothing is placed until the entire batch has passed, so
        /// a rejected proposal leaves the grid exactly as it found it. Once it has passed, every
        /// <see cref="HexGrid.TryPlaceUnit"/> call is guaranteed to succeed — validation has
        /// already established that each tile is in bounds and unoccupied, that no two entries
        /// share a tile, and that every unit id is present and distinct, which is the complete list
        /// of ways that call can fail.
        /// </para>
        /// <para>
        /// <strong>This seats a team that is not on the board yet.</strong> Re-arranging a layout
        /// that has already been committed will be rejected, because those units occupy the tiles
        /// being validated and so read as collisions. Lift them off first — with
        /// <see cref="HexGrid.RemoveUnit"/> or <see cref="HexGrid.ClearOccupancy"/> — and place the
        /// new layout against a clear board.
        /// </para>
        /// </summary>
        public static bool TryPlaceAll(
            HexGrid grid,
            BattleTeam team,
            BattleFormat format,
            IReadOnlyList<PlacementRequest> proposedPlacements,
            IReadOnlyList<HexCoordinate> alreadyPlaced,
            out PlacementValidationResult result)
        {
            result = ValidateCore(grid, team, format, proposedPlacements, alreadyPlaced, true);
            if (!result.IsValid)
            {
                return false;
            }

            for (int i = 0; i < proposedPlacements.Count; i++)
            {
                PlacementRequest request = proposedPlacements[i];
                grid.TryPlaceUnit(request.UnitId, request.Position);
            }

            return true;
        }

        /// <summary>
        /// The one implementation behind both overloads. Every rule is checked against every
        /// position — there is no early exit on the first failure, because a caller explaining a
        /// bad layout needs all of it at once.
        /// <para>
        /// A missing <paramref name="grid"/> is treated as a board with no tiles on it rather than
        /// thrown on: every position then comes back
        /// <see cref="PlacementStatus.OutOfBounds"/>, which is the honest answer and keeps this
        /// consistent with <see cref="BattleTurnExecutor"/>, which also tolerates a null grid.
        /// </para>
        /// </summary>
        private static PlacementValidationResult ValidateCore(
            HexGrid grid,
            BattleTeam team,
            BattleFormat format,
            IReadOnlyList<PlacementRequest> placements,
            IReadOnlyList<HexCoordinate> alreadyPlaced,
            bool checkUnitIds)
        {
            int maxPartySize = format.MaxPartySize();
            int count = placements == null ? 0 : placements.Count;

            PlacementCountStatus countStatus;
            if (count < 1)
            {
                countStatus = PlacementCountStatus.Empty;
            }
            else if (count > maxPartySize)
            {
                countStatus = PlacementCountStatus.ExceedsFormat;
            }
            else
            {
                countStatus = PlacementCountStatus.Valid;
            }

            HashSet<HexCoordinate> claimedElsewhere = new HashSet<HexCoordinate>();
            if (alreadyPlaced != null)
            {
                for (int i = 0; i < alreadyPlaced.Count; i++)
                {
                    claimedElsewhere.Add(alreadyPlaced[i]);
                }
            }

            HashSet<HexCoordinate> seenTiles = new HashSet<HexCoordinate>();
            HashSet<string> seenUnitIds = new HashSet<string>(StringComparer.Ordinal);
            List<PlacementOutcome> outcomes = new List<PlacementOutcome>(count);

            for (int i = 0; i < count; i++)
            {
                PlacementRequest request = placements[i];
                PlacementStatus status = PlacementStatus.Valid;

                if (grid == null || !grid.IsInBounds(request.Position))
                {
                    // Off the board, so terrain and zone membership are not separate facts to
                    // report -- see PlacementStatus.OutOfBounds.
                    status |= PlacementStatus.OutOfBounds;
                }
                else
                {
                    if (grid.IsBlocked(request.Position))
                    {
                        status |= PlacementStatus.Blocked;
                    }

                    if (!grid.IsInDeploymentZone(request.Position, team))
                    {
                        status |= PlacementStatus.OutsideDeploymentZone;
                    }

                    if (claimedElsewhere.Contains(request.Position) || grid.IsOccupied(request.Position))
                    {
                        status |= PlacementStatus.Collision;
                    }
                }

                // Facts about the proposal rather than about the board, so they are still worth
                // reporting for a tile that does not exist. HashSet.Add is false when the tile was
                // already there, which makes the first occurrence clean and every repeat a
                // duplicate without a second pass.
                if (!seenTiles.Add(request.Position))
                {
                    status |= PlacementStatus.DuplicateTile;
                }

                if (checkUnitIds)
                {
                    if (string.IsNullOrEmpty(request.UnitId))
                    {
                        status |= PlacementStatus.MissingUnitId;
                    }
                    else if (!seenUnitIds.Add(request.UnitId))
                    {
                        status |= PlacementStatus.DuplicateUnitId;
                    }
                }

                outcomes.Add(new PlacementOutcome(i, request.UnitId, request.Position, status));
            }

            return new PlacementValidationResult(team, format, maxPartySize, countStatus, outcomes);
        }
    }
}
