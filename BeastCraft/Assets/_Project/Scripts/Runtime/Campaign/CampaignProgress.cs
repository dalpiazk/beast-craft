using System;
using System.Collections.Generic;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// Where the player's region campaign has got to, as save data (<c>PlayerSave.Campaign</c>,
    /// schema 3): the seals owned (key items that raise the beast level cap), each unlocked region's
    /// progress, the region last played, and the node map of the expedition in progress.
    /// <para>
    /// Plain serializable data with public fields, like the rest of the save. JsonUtility writes
    /// every class field (it has no null), so "no expedition" is <see cref="MapRun.RegionId"/> == "",
    /// never a null <see cref="ActiveRun"/>. Regions and seals are named by their stable
    /// <c>regions.json</c> ids. The rules are <see cref="CampaignRules"/>'; this type only holds
    /// the state.
    /// </para>
    /// </summary>
    [Serializable]
    public class CampaignProgress
    {
        /// <summary>The region every new (or migrated) save starts with unlocked: the first of <c>regions.json</c>.</summary>
        public const string StartingRegionId = "r01";

        /// <summary>Owned seal ids (<c>regions.json</c> <c>Seals</c>), in the order granted. Each at most once.</summary>
        public List<string> Seals = new List<string>();

        /// <summary>Every unlocked region's progress, in the order unlocked. A region is unlocked when it has an entry.</summary>
        public List<RegionProgress> Regions = new List<RegionProgress>();

        /// <summary>The region last entered, or "" before the first expedition.</summary>
        public string CurrentRegionId = string.Empty;

        /// <summary>The expedition in progress; its <see cref="MapRun.RegionId"/> is "" when there is none.</summary>
        public MapRun ActiveRun = new MapRun();

        /// <summary>Whether an expedition is in progress.</summary>
        public bool HasActiveRun
        {
            get { return ActiveRun != null && !string.IsNullOrEmpty(ActiveRun.RegionId); }
        }

        /// <summary><paramref name="regionId"/>'s progress, or null when it is not unlocked.</summary>
        public RegionProgress FindRegion(string regionId)
        {
            if (string.IsNullOrEmpty(regionId) || Regions == null)
            {
                return null;
            }

            for (int i = 0; i < Regions.Count; i++)
            {
                if (Regions[i] != null && string.Equals(Regions[i].RegionId, regionId, StringComparison.Ordinal))
                {
                    return Regions[i];
                }
            }

            return null;
        }

        /// <summary>Whether <paramref name="regionId"/> is unlocked.</summary>
        public bool IsUnlocked(string regionId)
        {
            return FindRegion(regionId) != null;
        }

        /// <summary>Unlocks <paramref name="regionId"/> (a fresh entry, nothing cleared). False when it already was, or the id is empty.</summary>
        public bool Unlock(string regionId)
        {
            if (string.IsNullOrEmpty(regionId) || IsUnlocked(regionId))
            {
                return false;
            }

            if (Regions == null)
            {
                Regions = new List<RegionProgress>();
            }

            Regions.Add(new RegionProgress { RegionId = regionId });
            return true;
        }

        /// <summary>Whether <paramref name="sealId"/> is owned.</summary>
        public bool HasSeal(string sealId)
        {
            return !string.IsNullOrEmpty(sealId) && Seals != null && Seals.Contains(sealId);
        }

        /// <summary>Adds <paramref name="sealId"/>. False when it was already owned, or the id is empty.</summary>
        public bool AddSeal(string sealId)
        {
            if (string.IsNullOrEmpty(sealId) || HasSeal(sealId))
            {
                return false;
            }

            if (Seals == null)
            {
                Seals = new List<string>();
            }

            Seals.Add(sealId);
            return true;
        }

        /// <summary>
        /// Replaces any null list, string or sub-object with its empty default and drops null
        /// entries (see <c>PlayerSave.EnsureInitialized</c>). Returns how many things were repaired.
        /// </summary>
        public int EnsureInitialized()
        {
            int repaired = 0;

            if (Seals == null)
            {
                Seals = new List<string>();
                repaired++;
            }

            if (Regions == null)
            {
                Regions = new List<RegionProgress>();
                repaired++;
            }

            repaired += Regions.RemoveAll(region => region == null);

            if (CurrentRegionId == null)
            {
                CurrentRegionId = string.Empty;
                repaired++;
            }

            if (ActiveRun == null)
            {
                ActiveRun = new MapRun();
                repaired++;
            }

            repaired += ActiveRun.EnsureInitialized();
            return repaired;
        }
    }

    /// <summary>One unlocked region's progress: how many of its stages are cleared and whether its boss is.</summary>
    [Serializable]
    public class RegionProgress
    {
        /// <summary>The <c>regions.json</c> <c>RegionId</c>.</summary>
        public string RegionId;

        /// <summary>Gate stages cleared (0 to the region's stage count − 1; the last stage ends at the boss).</summary>
        public int StagesCleared;

        /// <summary>Whether the region's boss has been beaten (its seal granted, the regions it opens unlocked).</summary>
        public bool BossCleared;
    }
}
