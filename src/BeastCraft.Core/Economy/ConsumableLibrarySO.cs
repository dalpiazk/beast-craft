
namespace BeastCraft.Economy
{
    /// <summary>
    /// The imported consumable library: <c>data/Items/consumable-library.json</c> copied verbatim by
    /// the Editor importer (menu: Beast Craft/Data/Import Consumables), which also writes one
    /// <see cref="ConsumableSO"/> asset per consumable (what <c>BattleContent</c> resolves). Never
    /// hand-edit <see cref="Data"/>; edit the JSON and re-import.
    /// </summary>
    public class ConsumableLibrarySO : ContentAsset
    {
        /// <summary>The authored library, as imported.</summary>
        public ConsumableLibraryData Data = new ConsumableLibraryData();

        private ConsumableLibrary _built;

        /// <summary>The indexed library, built once and cached.</summary>
        public ConsumableLibrary Library
        {
            get { return _built ?? (_built = ConsumableLibrary.Build(Data)); }
        }

        /// <summary>Drops the cached <see cref="Library"/> (the importer calls it after replacing <see cref="Data"/>).</summary>
        public void ResetRuntimeCaches()
        {
            _built = null;
        }
    }
}
