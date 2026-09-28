using System;
using System.Collections.Generic;

namespace BeastCraft.Tutorial
{
    /// <summary>
    /// The onboarding state of one save (<c>PlayerSave.Tutorial</c>, schema 7): whether Hearthglen,
    /// the tutorial region, is behind the player (played through, skipped, or a save that owned
    /// beasts before Hearthglen existed), and which tutorial hints have been shown and dismissed.
    /// Plain serializable data with public fields, like the rest of the save; the rules are
    /// <see cref="StarterPicks"/>' and <c>CampaignRules</c>'.
    /// </summary>
    [Serializable]
    public class TutorialProgress
    {
        /// <summary>
        /// Hearthglen is behind the player: its last location was reached, the player skipped it, or
        /// the save was migrated from before it existed (any save that already owned a beast). Once
        /// true, Hearthglen is never offered again and no beast pick is pending.
        /// </summary>
        public bool HearthglenCleared;

        /// <summary>Whether the player took the skip (the three picks without the fights and hints).</summary>
        public bool Skipped;

        /// <summary>
        /// Every tutorial hint (<c>hints.json</c> HintId) shown and dismissed, in the order seen, each
        /// once: a seen hint is never shown again.
        /// </summary>
        public List<string> SeenHintIds = new List<string>();

        /// <summary>Whether hint <paramref name="hintId"/> has been seen.</summary>
        public bool HasSeen(string hintId)
        {
            return !string.IsNullOrEmpty(hintId) && SeenHintIds != null && SeenHintIds.Contains(hintId);
        }

        /// <summary>Records hint <paramref name="hintId"/> as seen. False when it already was, or the id is empty.</summary>
        public bool MarkSeen(string hintId)
        {
            if (string.IsNullOrEmpty(hintId) || HasSeen(hintId))
            {
                return false;
            }

            if (SeenHintIds == null)
            {
                SeenHintIds = new List<string>();
            }

            SeenHintIds.Add(hintId);
            return true;
        }

        /// <summary>Replaces a null list with an empty one and drops empty and repeated ids. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (SeenHintIds == null)
            {
                SeenHintIds = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += SeenHintIds.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }
}
