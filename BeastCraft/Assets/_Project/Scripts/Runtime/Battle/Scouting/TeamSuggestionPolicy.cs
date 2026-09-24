using BeastCraft.Save;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// When the game offers a <see cref="TeamSuggester"/> team before a fight (a user decision): only
    /// after the player has lost that battle <see cref="MinLossesBeforeSuggestion"/> times, and never
    /// when the player has turned suggestions off (<see cref="PlayerSettings.TeamSuggestionsEnabled"/>).
    /// The free enemy-element preview (<see cref="EncounterPreview"/>) is always shown; only the
    /// suggested team is gated. The loss count is per map location, kept by the campaign's map run
    /// (wired when the campaign lands). Pure. See the battle-system design doc, "Encounter preview".
    /// </summary>
    public static class TeamSuggestionPolicy
    {
        /// <summary>Losses on the same battle (map location) before a team is suggested.</summary>
        public const int MinLossesBeforeSuggestion = 3;

        /// <summary>
        /// Whether to suggest a team for a battle the player has lost
        /// <paramref name="lossesOnThisEncounter"/> times: at least
        /// <see cref="MinLossesBeforeSuggestion"/> losses and suggestions enabled. Null settings are the
        /// defaults (enabled).
        /// </summary>
        public static bool ShouldSuggest(int lossesOnThisEncounter, PlayerSettings settings)
        {
            if (settings != null && !settings.TeamSuggestionsEnabled)
            {
                return false;
            }

            return lossesOnThisEncounter >= MinLossesBeforeSuggestion;
        }
    }
}
