using BeastCraft.Creatures;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// One line of an <see cref="EncounterPreview"/>: how many enemies share an element (and, at
    /// <see cref="ScoutingDetail.Full"/>, a stance and a name). Immutable.
    /// </summary>
    public sealed class EncounterPreviewGroup
    {
        public EncounterPreviewGroup(string displayName, Element element, CombatStance? stance, int count)
        {
            DisplayName = displayName;
            Element = element;
            Stance = stance;
            Count = count;
        }

        /// <summary>The enemies' shared name; null when the detail level hides names.</summary>
        public string DisplayName { get; }

        /// <summary>The shared element (<c>Element.None</c> = no affinity). Every detail level shows it.</summary>
        public Element Element { get; }

        /// <summary>The shared stance; null when the detail level hides stances.</summary>
        public CombatStance? Stance { get; }

        /// <summary>How many enemies the line stands for, at least 1.</summary>
        public int Count { get; }
    }
}
