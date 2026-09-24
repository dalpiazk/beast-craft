using System;
using BeastCraft.Creatures;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The imported encounter content: <c>data/Encounters/enemy-library.json</c>,
    /// <c>encounter-library.json</c> and the simulator-written <c>encounter-difficulty.json</c>,
    /// copied verbatim by the Editor importer (menu: Beast Craft/Data/Import Encounters) together
    /// with a link to the enemies' <see cref="GrowthRateCurve"/> asset, so the game reads exactly the
    /// data the balance simulator calibrated. Never hand-edit the data fields; edit the JSON and
    /// re-import.
    /// <para>
    /// <see cref="Catalog"/> and <see cref="Library"/> build the runtime objects on first use and keep
    /// them, so every encounter of a session shares one set of enemy species and kits.
    /// </para>
    /// </summary>
    public class EncounterLibrarySO : ContentAsset
    {
        /// <summary>The enemy library, as imported.</summary>
        public EnemyLibraryData Enemies = new EnemyLibraryData();

        /// <summary>The encounter shapes and templates, as imported.</summary>
        public EncounterLibraryData Encounters = new EncounterLibraryData();

        /// <summary>The calibrated difficulty, as imported.</summary>
        public EncounterDifficultyData Difficulty = new EncounterDifficultyData();

        /// <summary>The growth curve named by <see cref="EnemyLibraryData.GrowthCurveId"/> (the roster importer's asset).</summary>
        public GrowthRateCurve GrowthRate;

        [NonSerialized]
        private EnemyCatalog _catalog;

        [NonSerialized]
        private EncounterLibrary _library;

        /// <summary>The enemies as battle-ready species and kits (built once, then shared).</summary>
        public EnemyCatalog Catalog
        {
            get
            {
                if (_catalog == null)
                {
                    _catalog = EnemyCatalog.Build(Enemies, GrowthRate);
                }

                return _catalog;
            }
        }

        /// <summary>The encounter library with its difficulty table (built once, then shared).</summary>
        public EncounterLibrary Library
        {
            get
            {
                if (_library == null)
                {
                    _library = EncounterLibrary.Build(Encounters, EncounterDifficultyTable.Build(Difficulty));
                }

                return _library;
            }
        }

        /// <summary>Drops the built objects, so the next <see cref="Catalog"/> / <see cref="Library"/> reads the current data (the importer calls it).</summary>
        public void ResetRuntimeCaches()
        {
            _catalog = null;
            _library = null;
        }
    }
}
