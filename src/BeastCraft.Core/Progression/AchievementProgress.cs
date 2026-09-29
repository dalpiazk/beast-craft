using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The account-wide achievement and title state of one save (<c>PlayerSave.Achievements</c>,
    /// schema 9): every achievement earned (<see cref="EarnedIds"/>, each once, deterministic — no
    /// RNG), the titles it unlocked (<see cref="OwnedTitleIds"/>) and the one shown
    /// (<see cref="EquippedTitleId"/>, "" = none). Titles are display only: owning or equipping one
    /// never changes a stat. Plain serializable data with public fields; the rules are
    /// <see cref="AchievementRules"/>'.
    /// </summary>
    [Serializable]
    public class AchievementProgress
    {
        /// <summary>Achievement ids (<c>achievements.json</c>) earned, in order, each once.</summary>
        public List<string> EarnedIds = new List<string>();

        /// <summary>Title ids owned (an earned achievement's <c>TitleId</c>), in order, each once.</summary>
        public List<string> OwnedTitleIds = new List<string>();

        /// <summary>The title shown ("" = none). Always one of <see cref="OwnedTitleIds"/>.</summary>
        public string EquippedTitleId = string.Empty;

        /// <summary>Whether achievement <paramref name="achievementId"/> is earned.</summary>
        public bool HasEarned(string achievementId)
        {
            return Contains(EarnedIds, achievementId);
        }

        /// <summary>Whether title <paramref name="titleId"/> is owned.</summary>
        public bool HasTitle(string titleId)
        {
            return Contains(OwnedTitleIds, titleId);
        }

        /// <summary>Adds <paramref name="id"/> to <paramref name="list"/> unless empty or already there; returns whether it was added.</summary>
        public static bool AddOnce(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }

        /// <summary>
        /// Replaces null lists and strings with empty ones and drops empty and repeated ids. Ids and
        /// values are otherwise left alone — an equipped title that is not owned is
        /// <see cref="Save.SaveValidator"/>'s to report, not this call's to silently fix. Returns how
        /// many things were repaired.
        /// </summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            repaired += Repair(ref EarnedIds);
            repaired += Repair(ref OwnedTitleIds);
            if (EquippedTitleId == null)
            {
                EquippedTitleId = string.Empty;
                repaired++;
            }

            return repaired;
        }

        private static bool Contains(List<string> list, string id)
        {
            return !string.IsNullOrEmpty(id) && list != null && list.Contains(id);
        }

        private static int Repair(ref List<string> list)
        {
            int repaired = 0;
            if (list == null)
            {
                list = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += list.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }
}
