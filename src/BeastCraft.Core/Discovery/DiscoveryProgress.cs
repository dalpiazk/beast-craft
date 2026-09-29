using System;
using System.Collections.Generic;

namespace BeastCraft.Discovery
{
    /// <summary>
    /// The account-wide discovery state of one save (<c>PlayerSave.Discovery</c>, schema 8): the
    /// Kinship sites whose beast has joined (or that had none left to offer), a won trial whose
    /// choice is still to be made, the retry count behind a trial's battle seeds, the Grove unlocks
    /// the shrines granted (held for the Grove, which consumes them later) and the lore entries
    /// found (the compendium's seed). The per-region fog and points of interest found live on
    /// <c>RegionProgress</c>. Plain serializable data with public fields; the rules are
    /// <see cref="KinshipRules"/>' and <see cref="DiscoveryRules"/>'.
    /// </summary>
    [Serializable]
    public class DiscoveryProgress
    {
        /// <summary>Kinship site ids (<c>discovery.json</c> <c>KinshipSites</c>) claimed: a beast joined there, or none was left to offer. Each once.</summary>
        public List<string> ClaimedKinshipIds = new List<string>();

        /// <summary>A Kinship site whose trial was won but whose beast is not chosen yet ("" = none): the map offers the choice before anything else.</summary>
        public string PendingKinshipId = string.Empty;

        /// <summary>The level the pending choice joins at (the fielded team's mean level at the win, minus <see cref="KinshipRules.JoinLevelsBelow"/>, at least 1).</summary>
        public int PendingKinshipLevel;

        /// <summary>Kinship trials lost so far (every retry is a new battle seed).</summary>
        public int KinshipLosses;

        /// <summary>Grove unlock ids the shrines granted, in order, each once. Held for the Grove (a later feature), which consumes them.</summary>
        public List<string> GroveUnlockIds = new List<string>();

        /// <summary>Lore entry ids (<c>discovery.json</c> <c>Lore</c>) found at lore stones, in order, each once. The compendium's seed.</summary>
        public List<string> LoreIds = new List<string>();

        /// <summary>Whether a won trial's choice is waiting.</summary>
        public bool HasPendingKinship
        {
            get { return !string.IsNullOrEmpty(PendingKinshipId); }
        }

        /// <summary>Whether Kinship site <paramref name="siteId"/> is claimed.</summary>
        public bool HasClaimed(string siteId)
        {
            return Contains(ClaimedKinshipIds, siteId);
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

        /// <summary>Replaces null lists and strings with empty ones and drops empty and repeated ids. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            repaired += Repair(ref ClaimedKinshipIds);
            repaired += Repair(ref GroveUnlockIds);
            repaired += Repair(ref LoreIds);
            if (PendingKinshipId == null)
            {
                PendingKinshipId = string.Empty;
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
