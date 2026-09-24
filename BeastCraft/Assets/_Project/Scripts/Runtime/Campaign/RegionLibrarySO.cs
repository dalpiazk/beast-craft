using System;
using UnityEngine;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The imported region campaign: <c>Data/Campaign/regions.json</c> copied verbatim by the Editor
    /// importer (menu: Beast Craft/Data/Import Regions), so the game reads exactly the data the
    /// balance simulator's <c>--mode campaign</c> paced. Never hand-edit <see cref="Data"/>; edit the
    /// JSON and re-import. <see cref="Library"/> builds the runtime <see cref="RegionLibrary"/> on
    /// first use and keeps it.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Campaign/Region Library", fileName = "RegionLibrary")]
    public class RegionLibrarySO : ScriptableObject
    {
        /// <summary>The regions, seals and map rules, as imported.</summary>
        public RegionLibraryData Data = new RegionLibraryData();

        [NonSerialized]
        private RegionLibrary _library;

        /// <summary>The built library (built once, then shared).</summary>
        public RegionLibrary Library
        {
            get
            {
                if (_library == null)
                {
                    _library = RegionLibrary.Build(Data);
                }

                return _library;
            }
        }

        /// <summary>Drops the built library, so the next <see cref="Library"/> reads the current data (the importer calls it).</summary>
        public void ResetRuntimeCaches()
        {
            _library = null;
        }
    }
}
