using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The imported gear library: <c>Data/Items/gear-library.json</c> copied verbatim by the Editor
    /// importer (menu: Beast Craft/Data/Import Gear Library), which also writes one <c>GearSO</c> /
    /// <c>AvatarGearSO</c> asset per piece. Never hand-edit <see cref="Data"/>; edit the JSON and
    /// re-import.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Economy/Gear Library", fileName = "GearLibrary")]
    public class GearLibrarySO : ScriptableObject
    {
        /// <summary>The authored library, as imported.</summary>
        public GearLibraryData Data = new GearLibraryData();

        private GearLibrary _built;

        /// <summary>The indexed library (<see cref="GearLibrary.Build"/>), built once and cached.</summary>
        public GearLibrary Library
        {
            get { return _built ?? (_built = GearLibrary.Build(Data)); }
        }

        /// <summary>Drops the cached <see cref="Library"/> (the importer calls it after replacing <see cref="Data"/>).</summary>
        public void ResetRuntimeCaches()
        {
            _built = null;
        }
    }
}
