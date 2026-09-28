using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Creatures;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// The preview's ALWAYS-shown matchup warnings (producer decision, "assist + guidance"): short,
    /// plain-language flags for a fielded team against an <see cref="EncounterPreview"/>, shown
    /// before every campaign fight — independent of the Theorycrafter's detailed insight panel
    /// (<c>EncounterInsightView</c>, opt-in). Pure; no randomness, no state.
    /// </summary>
    public static class MatchupWarnings
    {
        public const string NoVanguard = "No Vanguard: nothing screens your Ranged and Skirmishers.";

        public const string NoSkirmisher = "No Skirmisher: your team gives up part of the stance system.";

        public const string ElementDisadvantage = "Element disadvantage: your kit is weak against this lineup.";

        /// <summary>
        /// The level gap (encounter level minus team level) at or above which "Under-levelled by N" shows.
        /// </summary>
        public const int UnderLevelledThreshold = 1;

        /// <summary>
        /// The mean <see cref="TeamSuggester.ScoreBeast"/> across the team at or below which
        /// <see cref="ElementDisadvantage"/> shows (0 = the team's offence and defence against this
        /// lineup net out to worse than neutral).
        /// </summary>
        public const double ElementDisadvantageThreshold = 0.0;

        /// <summary>
        /// The warnings <paramref name="team"/> (its species, any order; null entries skipped) sees
        /// fielded at <paramref name="teamLevel"/> against <paramref name="preview"/> at
        /// <paramref name="encounterLevel"/>, in this fixed order: no Vanguard, no Skirmisher, element
        /// disadvantage, under-levelled. Empty for a null or empty team. A null or empty
        /// <paramref name="preview"/> skips the element check only (the stance and level checks do
        /// not need it).
        /// </summary>
        public static List<string> For(EncounterPreview preview, IReadOnlyList<CreatureSpeciesSO> team, int teamLevel, int encounterLevel)
        {
            List<string> warnings = new List<string>();
            if (team == null || team.Count == 0)
            {
                return warnings;
            }

            bool vanguard = false;
            bool skirmisher = false;
            foreach (CreatureSpeciesSO species in team)
            {
                if (species == null)
                {
                    continue;
                }

                vanguard = vanguard || species.Stance == CombatStance.Vanguard;
                skirmisher = skirmisher || species.Stance == CombatStance.Skirmisher;
            }

            if (!vanguard)
            {
                warnings.Add(NoVanguard);
            }

            if (!skirmisher)
            {
                warnings.Add(NoSkirmisher);
            }

            if (preview != null && preview.TotalEnemies > 0)
            {
                double sum = 0.0;
                int counted = 0;
                foreach (CreatureSpeciesSO species in team)
                {
                    if (species == null)
                    {
                        continue;
                    }

                    sum += TeamSuggester.ScoreBeast(preview, species);
                    counted++;
                }

                if (counted > 0 && (sum / counted) <= ElementDisadvantageThreshold)
                {
                    warnings.Add(ElementDisadvantage);
                }
            }

            int gap = encounterLevel - teamLevel;
            if (gap >= UnderLevelledThreshold)
            {
                warnings.Add("Under-levelled by " + gap.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return warnings;
        }
    }
}
