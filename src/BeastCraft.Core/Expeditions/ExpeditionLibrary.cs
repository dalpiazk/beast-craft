using System;
using System.Collections.Generic;

namespace BeastCraft.Expeditions
{
    /// <summary>Lookups over a validated <see cref="ExpeditionLibraryData"/> (<c>expedition-library.json</c>).</summary>
    public sealed class ExpeditionLibrary
    {
        private readonly Dictionary<string, DestinationData> _destinations = new Dictionary<string, DestinationData>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpeditionOutcomeTableData> _outcomes = new Dictionary<string, ExpeditionOutcomeTableData>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoreStoryData> _stories = new Dictionary<string, LoreStoryData>(StringComparer.Ordinal);

        private ExpeditionLibrary(ExpeditionLibraryData data)
        {
            Data = data;
        }

        public ExpeditionLibraryData Data { get; }

        public static ExpeditionLibrary Build(ExpeditionLibraryData data)
        {
            ExpeditionLibrary library = new ExpeditionLibrary(data ?? new ExpeditionLibraryData());

            foreach (DestinationData destination in library.Data.Destinations ?? new DestinationData[0])
            {
                if (destination != null && !string.IsNullOrEmpty(destination.DestinationId))
                {
                    library._destinations[destination.DestinationId] = destination;
                }
            }

            foreach (ExpeditionOutcomeTableData table in library.Data.OutcomeTables ?? new ExpeditionOutcomeTableData[0])
            {
                if (table != null && !string.IsNullOrEmpty(table.DestinationId))
                {
                    library._outcomes[table.DestinationId] = table;
                }
            }

            foreach (LoreStoryData story in library.Data.Stories ?? new LoreStoryData[0])
            {
                if (story != null && !string.IsNullOrEmpty(story.StoryId))
                {
                    library._stories[story.StoryId] = story;
                }
            }

            return library;
        }

        public DestinationData Destination(string destinationId)
        {
            return destinationId != null && _destinations.TryGetValue(destinationId, out DestinationData destination) ? destination : null;
        }

        public ExpeditionOutcomeTableData OutcomeTable(string destinationId)
        {
            return destinationId != null && _outcomes.TryGetValue(destinationId, out ExpeditionOutcomeTableData table) ? table : null;
        }

        public LoreStoryData Story(string storyId)
        {
            return storyId != null && _stories.TryGetValue(storyId, out LoreStoryData story) ? story : null;
        }
    }
}
