namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// How much of an encounter the pre-battle preview reveals (<see cref="EncounterPreview.Build"/>).
    /// <see cref="Full"/> is the default: enemy elements are meant to be visible and counterable.
    /// The partial levels are a future fog-of-war knob (an unscouted area, a "mysterious" encounter),
    /// not a current mechanic.
    /// <para>
    /// <strong>The values are explicit and must never be renumbered</strong>: a serialized setting
    /// stores the integer. Add new levels at the end with a new value.
    /// </para>
    /// </summary>
    public enum ScoutingDetail
    {
        /// <summary>Every group: display name, element, stance and count. The default.</summary>
        Full = 0,

        /// <summary>Elements and how many enemies carry each; names and stances hidden.</summary>
        ElementsOnly = 1,

        /// <summary>One line: the most common element and the total enemy count; nothing else.</summary>
        DominantElementOnly = 2
    }
}
