
namespace BeastCraft.Economy
{
    /// <summary>
    /// The imported cosmetic library: <c>data/Cosmetics/cosmetic-library.json</c> copied verbatim by
    /// the Editor importer (menu: Beast Craft/Data/Import Cosmetics), which also writes the
    /// <c>CustomizationCategoryDefinition</c> assets, the avatar's and each species'
    /// <c>CustomizationSchema</c>. Never hand-edit <see cref="Data"/>; edit the JSON and re-import.
    /// </summary>
    public class CosmeticLibrarySO : ContentAsset
    {
        /// <summary>The authored library, as imported.</summary>
        public CosmeticLibraryData Data = new CosmeticLibraryData();

        private CosmeticLibrary _built;

        /// <summary>The indexed library, built once and cached.</summary>
        public CosmeticLibrary Library
        {
            get { return _built ?? (_built = CosmeticLibrary.Build(Data)); }
        }

        /// <summary>Drops the cached <see cref="Library"/> (the importer calls it after replacing <see cref="Data"/>).</summary>
        public void ResetRuntimeCaches()
        {
            _built = null;
        }
    }
}
