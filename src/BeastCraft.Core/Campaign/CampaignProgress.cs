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
        /// <summary>
        /// The first campaign region (the first of <c>regions.json</c> <c>Regions</c>): unlocked when
        /// Hearthglen is cleared or skipped (<c>StarterPicks</c>), and on every save migrated from before
        /// Hearthglen existed.
        /// </summary>
        public const string StartingRegionId = "r01";

        /// <summary>
        /// Hearthglen, the onboarding region (<c>regions.json</c> <c>TutorialRegions</c>): the one region a
        /// brand-new save starts with unlocked (<c>PlayerSave.CreateNew</c>). Unlocked only while it is
        /// being played: clearing (or skipping) it removes its entry and unlocks <see cref="StartingRegionId"/>.
        /// </summary>
        public const string TutorialRegionId = "r00";

        /// <summary>Owned seal ids (<c>regions.json</c> <c>Seals</c>), in the order granted. Each at most once.</summary>
        public List<string> Seals = new List<string>();

        /// <summary>Every unlocked region's progress, in the order unlocked. A region is unlocked when it has an entry.</summary>
        public List<RegionProgress> Regions = new List<RegionProgress>();

        /// <summary>The region last entered, or "" before the first expedition.</summary>
        public string CurrentRegionId = string.Empty;

        /// <summary>The expedition in progress; its <see cref="MapRun.RegionId"/> is "" when there is none.</summary>
        public MapRun ActiveRun = new MapRun();

        /// <summary>
        /// How many ordinary battle locations have ever been soothed with a Grove item instead of
        /// fought (<see cref="CampaignRules.Soothe"/>, the Grove design's "peaceful clears" — D3):
        /// never decreases, account-wide, replays included. Read by <c>Npc.NpcRules.BuildFacts</c>
        /// for the <c>location_soothed</c> NPC condition (a cascading count fact, like
        /// <c>decor_placed_count</c>). Added in schema 10 (extended in place; see
        /// <c>Save.SaveMigrations.AddGrove</c>'s remarks — schema 10 had not shipped on any other
        /// branch when this was added).
        /// </summary>
        public int LocationsSoothed;

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

        /// <summary>Locks <paramref name="regionId"/> again (drops its entry). False when it was not unlocked.</summary>
        public bool Lock(string regionId)
        {
            RegionProgress progress = FindRegion(regionId);
            return progress != null && Regions.Remove(progress);
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

            if (LocationsSoothed < 0)
            {
                LocationsSoothed = 0;
                repaired++;
            }
            foreach (RegionProgress region in Regions)
            {
                repaired += region.EnsureInitialized();
            }

            return repaired;
        }
    }

    /// <summary>
    /// One unlocked region's progress: how many of its stages are cleared and whether its boss is,
    /// and (schema 8) its discovery layer: the seed its points of interest are laid out from, the
    /// fog lifted on each stage's map, the points of interest found and whether the region's 100%
    /// reward was granted (<see cref="MapFog"/>, <c>BeastCraft.Discovery</c>).
    /// </summary>
    [Serializable]
    public class RegionProgress
    {
        /// <summary>The <c>regions.json</c> <c>RegionId</c>.</summary>
        public string RegionId;

        /// <summary>Gate stages cleared (0 to the region's stage count − 1; the last stage ends at the boss).</summary>
        public int StagesCleared;

        /// <summary>Whether the region's boss has been beaten (its seal granted, the regions it opens unlocked).</summary>
        public bool BossCleared;

        /// <summary>
        /// The seed the region's points of interest are laid out from (<c>PoiLayout</c>), independent of
        /// any expedition's map seed so a replay (a new map) never moves them or re-rolls what they
        /// hold. Assigned once, by the first expedition into the region
        /// (<see cref="CampaignRules.StartRun(BeastCraft.Save.PlayerSave, RegionLibrary, string, int, int, RunDifficulty)"/>);
        /// 0 = not yet assigned. Added in schema 8.
        /// </summary>
        public int DiscoverySeed;

        /// <summary>
        /// The fog lifted on each stage's map, one entry per stage visited (<see cref="MapFog"/>): never
        /// fogged again, whatever map a replay draws. Added in schema 8.
        /// </summary>
        public List<StageFog> Fog = new List<StageFog>();

        /// <summary>The points of interest found (visited; a Kinship site once its beast joined), by <c>PoiId</c>, in order. Added in schema 8.</summary>
        public List<string> FoundPoiIds = new List<string>();

        /// <summary>Whether the region's 100% exploration reward has been granted (<c>DiscoveryRules.TryComplete</c>). Added in schema 8.</summary>
        public bool Completed;

        /// <summary>The fog of stage <paramref name="stage"/>, or null when none of it has been seen.</summary>
        public StageFog FindFog(int stage)
        {
            if (Fog == null)
            {
                return null;
            }

            for (int i = 0; i < Fog.Count; i++)
            {
                if (Fog[i] != null && Fog[i].Stage == stage)
                {
                    return Fog[i];
                }
            }

            return null;
        }

        /// <summary>The fog of stage <paramref name="stage"/>, added (all fogged) when missing.</summary>
        public StageFog FogOf(int stage)
        {
            StageFog fog = FindFog(stage);
            if (fog == null)
            {
                if (Fog == null)
                {
                    Fog = new List<StageFog>();
                }

                fog = new StageFog { Stage = stage };
                Fog.Add(fog);
            }

            return fog;
        }

        /// <summary>Whether point of interest <paramref name="poiId"/> has been found.</summary>
        public bool HasFound(string poiId)
        {
            return !string.IsNullOrEmpty(poiId) && FoundPoiIds != null && FoundPoiIds.Contains(poiId);
        }

        /// <summary>Records <paramref name="poiId"/> as found. False when it already was, or the id is empty.</summary>
        public bool MarkFound(string poiId)
        {
            if (string.IsNullOrEmpty(poiId) || HasFound(poiId))
            {
                return false;
            }

            if (FoundPoiIds == null)
            {
                FoundPoiIds = new List<string>();
            }

            FoundPoiIds.Add(poiId);
            return true;
        }

        /// <summary>Replaces null lists with empty ones and drops null or repeated entries. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (Fog == null)
            {
                Fog = new List<StageFog>();
                repaired++;
            }

            repaired += Fog.RemoveAll(fog => fog == null);
            foreach (StageFog fog in Fog)
            {
                if (fog.Cells == null)
                {
                    fog.Cells = string.Empty;
                    repaired++;
                }
            }

            if (FoundPoiIds == null)
            {
                FoundPoiIds = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += FoundPoiIds.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }

    /// <summary>
    /// The fog lifted on one stage's map (<see cref="MapFog"/>): which cells of the stage's fog grid
    /// have been seen, as a bit set written in hexadecimal (cell <c>i</c> is bit <c>i % 4</c> of hex
    /// digit <c>i / 4</c>; compact, and JsonUtility-safe), and the highest row a location was
    /// cleared on (a Kinship site calls to a player who has walked up to its row). Save data.
    /// </summary>
    [Serializable]
    public class StageFog
    {
        /// <summary>The stage, 0-based.</summary>
        public int Stage;

        /// <summary>The highest map row (layer) a location was cleared on in any expedition of the stage; −1 before any.</summary>
        public int DeepestLayer = -1;

        /// <summary>The seen cells as a hexadecimal bit set ("" = all fogged). Read and written through <see cref="MapFog"/>.</summary>
        public string Cells = string.Empty;
    }
}
