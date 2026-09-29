using System;
using System.Collections.Generic;
using BeastCraft.Economy;
using BeastCraft.Grove;

namespace BeastCraft.Expeditions
{
    /// <summary>
    /// Validates <c>expedition-library.json</c> (<see cref="ExpeditionLibraryData"/>): unique
    /// snake_case ids; every destination's habitat unlock a known Grove habitat, 1-12 duration hours
    /// and 1-3 party size; every destination has exactly one outcome table with at least one entry,
    /// positive weights, at least one common and one non-common entry, a <c>"story"</c> entry naming a
    /// known story, a <c>"trinket"</c> entry naming a snake_case Grove item id, and a <c>"look"</c>
    /// entry a <c>"grove"</c> look when a cosmetic library is given; every story reachable by at least
    /// one destination's table. Display names and story text follow the content bible.
    /// </summary>
    public static class ExpeditionLibraryValidator
    {
        public const int MaxNameLength = 24;

        public const int MaxTextLength = 280;

        public const int MinDurationHours = 1;

        public const int MaxDurationHours = 12;

        public static List<string> Validate(ExpeditionLibraryData data, GroveLibraryData grove, CosmeticLibraryData cosmetics)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("expedition-library.json is missing.");
                return errors;
            }

            if (data.SchemaVersion != 1)
            {
                errors.Add("SchemaVersion must be 1.");
            }

            HashSet<string> habitatIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (HabitatData habitat in grove == null ? new HabitatData[0] : grove.Habitats ?? new HabitatData[0])
            {
                if (habitat != null && !string.IsNullOrEmpty(habitat.HabitatId))
                {
                    habitatIds.Add(habitat.HabitatId);
                }
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, DestinationData> destinations = new Dictionary<string, DestinationData>(StringComparer.Ordinal);
            foreach (DestinationData destination in data.Destinations ?? new DestinationData[0])
            {
                string at = "Destinations[" + (destination == null ? "?" : destination.DestinationId) + "]";
                if (destination == null || !CheckId(errors, "Destinations", destination.DestinationId, ids))
                {
                    continue;
                }

                destinations[destination.DestinationId] = destination;
                CheckName(errors, at + ": DisplayName", destination.DisplayName);
                if (destination.DurationHours < MinDurationHours || destination.DurationHours > MaxDurationHours)
                {
                    errors.Add(at + ": DurationHours must be " + MinDurationHours + "-" + MaxDurationHours + ".");
                }

                if (destination.PartySize < 1 || destination.PartySize > 3)
                {
                    errors.Add(at + ": PartySize must be 1-3.");
                }

                if (destination.UnlockSource == "habitat")
                {
                    if (string.IsNullOrEmpty(destination.UnlockId) || !habitatIds.Contains(destination.UnlockId))
                    {
                        errors.Add(at + ": UnlockId must be a Grove habitat when UnlockSource is habitat.");
                    }
                }
                else if (destination.UnlockSource != "start")
                {
                    errors.Add(at + ": UnlockSource must be start or habitat.");
                }
            }

            Dictionary<string, LoreStoryData> stories = new Dictionary<string, LoreStoryData>(StringComparer.Ordinal);
            foreach (LoreStoryData story in data.Stories ?? new LoreStoryData[0])
            {
                string at = "Stories[" + (story == null ? "?" : story.StoryId) + "]";
                if (story == null || !CheckId(errors, "Stories", story.StoryId, ids))
                {
                    continue;
                }

                stories[story.StoryId] = story;
                CheckText(errors, at + ": Text", story.Text);
                if (string.IsNullOrWhiteSpace(story.CodexCategory))
                {
                    errors.Add(at + ": CodexCategory is missing.");
                }
            }

            HashSet<string> tabled = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> reachedStories = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExpeditionOutcomeTableData table in data.OutcomeTables ?? new ExpeditionOutcomeTableData[0])
            {
                if (table == null || string.IsNullOrEmpty(table.DestinationId))
                {
                    errors.Add("OutcomeTables: an entry has no DestinationId.");
                    continue;
                }

                string at = "OutcomeTables[" + table.DestinationId + "]";
                if (!destinations.ContainsKey(table.DestinationId))
                {
                    errors.Add(at + ": DestinationId is not a known destination.");
                    continue;
                }

                if (!tabled.Add(table.DestinationId))
                {
                    errors.Add(at + ": listed twice.");
                }

                if (table.PityAt < 1)
                {
                    errors.Add(at + ": PityAt must be at least 1.");
                }

                int total = 0;
                bool hasCommon = false;
                bool hasNonCommon = false;
                foreach (ExpeditionOutcomeEntryData entry in table.Entries ?? new ExpeditionOutcomeEntryData[0])
                {
                    if (entry == null || entry.Weight <= 0)
                    {
                        errors.Add(at + ": every entry needs a positive Weight.");
                        continue;
                    }

                    total += entry.Weight;
                    hasCommon |= entry.IsCommon;
                    hasNonCommon |= !entry.IsCommon;

                    switch (entry.Kind)
                    {
                        case "story":
                            if (stories.ContainsKey(entry.Id ?? string.Empty))
                            {
                                reachedStories.Add(entry.Id);
                            }
                            else
                            {
                                errors.Add(at + ": story entry '" + entry.Id + "' is not a known story.");
                            }

                            break;
                        case "trinket":
                            if (!IsSnakeCase(entry.Id))
                            {
                                errors.Add(at + ": a trinket entry's Id must be snake_case.");
                            }

                            break;
                        case "look":
                            CheckLook(errors, at + ": look entry", entry.Id, "expedition_" + table.DestinationId, cosmetics);
                            break;
                        default:
                            errors.Add(at + ": Kind must be story, trinket or look.");
                            break;
                    }
                }

                if (total <= 0)
                {
                    errors.Add(at + ": needs at least one entry.");
                }

                if (!hasCommon || !hasNonCommon)
                {
                    errors.Add(at + ": needs at least one common and one non-common entry for pity to mean anything.");
                }
            }

            foreach (string destinationId in destinations.Keys)
            {
                if (!tabled.Contains(destinationId))
                {
                    errors.Add("Destinations[" + destinationId + "]: has no outcome table.");
                }
            }

            foreach (string storyId in stories.Keys)
            {
                if (!reachedStories.Contains(storyId))
                {
                    errors.Add("Stories[" + storyId + "]: no outcome table unlocks it.");
                }
            }

            return errors;
        }

        private static void CheckLook(List<string> errors, string at, string key, string unlockId, CosmeticLibraryData cosmetics)
        {
            if (string.IsNullOrEmpty(key))
            {
                errors.Add(at + " is missing.");
                return;
            }

            if (cosmetics == null)
            {
                return;
            }

            string[] parts = key.Split('/');
            CosmeticCategoryData category = parts.Length != 2 ? null : Array.Find(cosmetics.Categories ?? new CosmeticCategoryData[0], c => c != null && c.CategoryId == parts[0]);
            CosmeticOptionData option = category == null ? null : Array.Find(category.Options ?? new CosmeticOptionData[0], o => o != null && o.OptionId == parts[1]);
            if (option == null || option.Source != CosmeticLibrary.SourceGrove || option.UnlockId != unlockId)
            {
                errors.Add(at + " '" + key + "' must be a grove look whose UnlockId is '" + unlockId + "'.");
            }
        }

        private static bool CheckId(List<string> errors, string list, string id, HashSet<string> ids)
        {
            if (!IsSnakeCase(id))
            {
                errors.Add(list + ": id '" + id + "' must be snake_case.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add(list + ": id '" + id + "' is used twice in expedition-library.json.");
                return false;
            }

            return true;
        }

        private static void CheckName(List<string> errors, string at, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name.IndexOf('\'') >= 0)
            {
                errors.Add(at + " must be 1-" + MaxNameLength + " characters with no apostrophe.");
            }
        }

        private static void CheckText(List<string> errors, string at, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            {
                errors.Add(at + " must be 1-" + MaxTextLength + " characters.");
            }
        }

        private static bool IsSnakeCase(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
