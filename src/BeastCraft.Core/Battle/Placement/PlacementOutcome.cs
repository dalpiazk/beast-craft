using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// The verdict on one proposed starting position: which entry of the proposal it was, who was
    /// going to stand there, where, and everything wrong with it.
    /// <para>
    /// A named type rather than a tuple, and one entry per proposed position rather than one per
    /// <em>problem</em>, for the same reason <see cref="BattleSkillOutcome"/> is shaped the way it
    /// is: a caller — a future placement UI above all — wants to walk the things it drew and ask
    /// each one how it did, not walk a bag of errors and work out which marker each one belongs to.
    /// Valid positions therefore get an outcome too.
    /// </para>
    /// <para>
    /// A record of a judgement, not a handle to change one. Nothing here can place anybody.
    /// </para>
    /// </summary>
    public class PlacementOutcome
    {
        public PlacementOutcome(int index, string unitId, HexCoordinate position, PlacementStatus status)
        {
            Index = index;
            UnitId = unitId;
            Position = position;
            Status = status;
        }

        /// <summary>
        /// Which entry of the proposed list this was, counting from 0. The one thing that still
        /// identifies a position when its tile is a duplicate of another's and its id is missing.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// The unit that was to stand here, or <c>null</c> when the proposal was validated as bare
        /// coordinates. An empty or null id on an id-carrying proposal is reported as
        /// <see cref="PlacementStatus.MissingUnitId"/> rather than hidden here.
        /// </summary>
        public string UnitId { get; }

        /// <summary>The tile that was proposed. Echoed back even when it is off the board.</summary>
        public HexCoordinate Position { get; }

        /// <summary>
        /// Every rule this position breaks, or <see cref="PlacementStatus.Valid"/> when it breaks
        /// none. A flags value: test it with <see cref="System.Enum.HasFlag"/> or a bitwise and,
        /// not with equality, unless the thing being tested is validity itself.
        /// </summary>
        public PlacementStatus Status { get; }

        /// <summary>Shorthand for <c>Status == PlacementStatus.Valid</c>.</summary>
        public bool IsValid
        {
            get { return Status == PlacementStatus.Valid; }
        }
    }
}
