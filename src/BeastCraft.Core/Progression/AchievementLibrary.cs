using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>Lookups over a validated <see cref="AchievementLibraryData"/> (<c>achievements.json</c>).</summary>
    public sealed class AchievementLibrary
    {
        private readonly Dictionary<string, AchievementData> _byId = new Dictionary<string, AchievementData>(StringComparer.Ordinal);
        private readonly List<AchievementData> _order = new List<AchievementData>();

        private AchievementLibrary(AchievementLibraryData data)
        {
            Data = data;
        }

        public AchievementLibraryData Data { get; }

        /// <summary>Every achievement, in file order.</summary>
        public IReadOnlyList<AchievementData> All
        {
            get { return _order; }
        }

        public static AchievementLibrary Build(AchievementLibraryData data)
        {
            AchievementLibrary library = new AchievementLibrary(data ?? new AchievementLibraryData());
            foreach (AchievementData achievement in library.Data.Achievements ?? new AchievementData[0])
            {
                if (achievement != null && !string.IsNullOrEmpty(achievement.AchievementId) && !library._byId.ContainsKey(achievement.AchievementId))
                {
                    library._byId.Add(achievement.AchievementId, achievement);
                    library._order.Add(achievement);
                }
            }

            return library;
        }

        /// <summary>The achievement <paramref name="achievementId"/>, or null.</summary>
        public AchievementData Get(string achievementId)
        {
            return achievementId != null && _byId.TryGetValue(achievementId, out AchievementData achievement) ? achievement : null;
        }
    }
}
