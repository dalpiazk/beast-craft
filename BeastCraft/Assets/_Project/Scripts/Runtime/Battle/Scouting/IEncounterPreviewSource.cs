using BeastCraft.Creatures;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// What <see cref="EncounterPreview"/> needs to know about one enemy of an encounter: its
    /// element, its stance and the name the preview shows. Implemented by whatever holds the
    /// encounter's lineup (game encounter data, the balance simulator's fixtures), so the preview
    /// never depends on how enemies are authored.
    /// </summary>
    public interface IEncounterPreviewSource
    {
        /// <summary>The enemy's element; <c>Element.None</c> for no affinity.</summary>
        Element Element { get; }

        CombatStance Stance { get; }

        /// <summary>The name the preview groups and shows it by (its type, not a unit id).</summary>
        string DisplayName { get; }
    }
}
