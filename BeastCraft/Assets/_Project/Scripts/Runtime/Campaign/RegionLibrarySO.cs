using System;
using UnityEngine;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The imported region campaign: <c>Data/Campaign/regions.json</c> and its map location names
    /// (<c>location-names.json</c>) copied verbatim by the Editor importer (menu: Beast Craft/Data/Import
    /// Regions), so the game reads exactly the data the balance simulator's <c>--mode campaign</c>
    /// paced. Never hand-edit <see cref="Data"/> or <see cref="LocationNames"/>; edit the JSON and
    /// re-import. <see cref="Library"/> builds the runtime <see cref="RegionLibrary"/> and
    /// <see cref="Names"/> the <see cref="LocationNameTable"/> on first use and keeps them.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Campaign/Region Library", fileName = "RegionLibrary")]
    public class RegionLibrarySO : ScriptableObject
    {
        /// <summary>The regions, seals and map rules, as imported.</summary>
        public RegionLibraryData Data = new RegionLibraryData();

        /// <summary>The map location names (<c>Data/Campaign/location-names.json</c>), imported with the regions.</summary>
        public LocationNameTableData LocationNames = new LocationNameTableData();

        [NonSerialized]
        private RegionLibrary _library;

        [NonSerialized]
        private LocationNameTable _names;

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

        /// <summary>The built location-name table, resolving a map node's <c>LabelKey</c> (built once, then shared).</summary>
        public LocationNameTable Names
        {
            get
            {
                if (_names == null)
                {
                    _names = LocationNameTable.Build(LocationNames);
                }

                return _names;
            }
        }

        /// <summary>Drops the built library and name table, so the next <see cref="Library"/> and <see cref="Names"/> read the current data (the importer calls it).</summary>
        public void ResetRuntimeCaches()
        {
            _library = null;
            _names = null;
        }
    }
}
