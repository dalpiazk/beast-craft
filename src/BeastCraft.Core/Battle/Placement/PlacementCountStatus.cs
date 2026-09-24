namespace BeastCraft.Battle.Placement
{
    /// <summary>
    /// Whether a proposal fields a legal <em>number</em> of beasts for its
    /// <see cref="BattleFormat"/>. A property of the batch rather than of any one position, which
    /// is why it is separate from <see cref="PlacementStatus"/> and lives on
    /// <see cref="PlacementValidationResult"/> directly.
    /// <para>
    /// Not a flags enum: unlike a position, a count can only be wrong in one direction at a time.
    /// </para>
    /// </summary>
    public enum PlacementCountStatus
    {
        /// <summary>
        /// Between 1 and the format's <see cref="BattleFormatExtensions.MaxPartySize"/> inclusive.
        /// Deliberately a range rather than an exact match: decision 2 fixes the maximum a format
        /// deploys ("up to 4", "up to 6"), not a required headcount, so a player fielding three
        /// beasts in a four-slot format is making a choice, not an error.
        /// </summary>
        Valid = 0,

        /// <summary>
        /// Nothing was proposed at all — an empty list, or a null one. A side with no beasts on the
        /// board is not a battle, so this is a failure rather than a trivially legal layout.
        /// </summary>
        Empty = 1,

        /// <summary>
        /// More positions than the format allows. Which of them are the excess is not decided here:
        /// every position is still validated on its own merits, so the caller can show the count
        /// error and the per-tile errors together rather than one after the other.
        /// </summary>
        ExceedsFormat = 2
    }
}
