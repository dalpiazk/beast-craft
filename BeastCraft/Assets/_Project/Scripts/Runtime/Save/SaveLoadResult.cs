using System.Collections.Generic;

namespace BeastCraft.Save
{
    /// <summary>
    /// What <see cref="SaveSerializer.Deserialize"/> (or <see cref="SaveStore.Load"/>) produced.
    /// <para>
    /// <see cref="Success"/> false means there is no usable save — empty or malformed text, a
    /// missing or newer schema version, a failed or missing migration step — and
    /// <see cref="Error"/> says why; <see cref="Save"/> is then null. <see cref="Success"/> true
    /// means <see cref="Save"/> is a fully initialized current-version save, possibly with
    /// <see cref="Issues"/>: unresolvable ids or out-of-range values that were reported, not fixed.
    /// </para>
    /// </summary>
    public class SaveLoadResult
    {
        private SaveLoadResult()
        {
        }

        /// <summary>Whether <see cref="Save"/> was read.</summary>
        public bool Success { get; private set; }

        /// <summary>The loaded save at the current schema version, or null on failure.</summary>
        public PlayerSave Save { get; private set; }

        /// <summary>Why the load failed; null on success.</summary>
        public string Error { get; private set; }

        /// <summary>The schema version the text was written at (0 when it could not be read).</summary>
        public int SourceVersion { get; private set; }

        /// <summary>Whether migration steps ran (the source version was older than the current one).</summary>
        public bool Migrated { get; private set; }

        /// <summary>Validation findings on a successful load; empty when the save is clean.</summary>
        public List<SaveIssue> Issues { get; private set; }

        /// <summary>
        /// Which stored file <see cref="SaveStore.Load"/> used: <see cref="SaveFileSource.Main"/>,
        /// <see cref="SaveFileSource.Backup"/> (the main file was missing, corrupt, or read but
        /// failed to load — see <see cref="MainFileProblem"/>), or <see cref="SaveFileSource.None"/>
        /// when nothing was read or the result came straight from <see cref="SaveSerializer"/>.
        /// </summary>
        public SaveFileSource StorageSource { get; private set; }

        /// <summary>
        /// When <see cref="SaveStore.Load"/> fell back to the backup, why the main file was skipped;
        /// null otherwise. Worth logging: the player lost their latest save.
        /// </summary>
        public string MainFileProblem { get; private set; }

        /// <summary>This result, stamped with where <see cref="SaveStore"/> read it from.</summary>
        internal SaveLoadResult WithStorage(SaveFileSource source, string mainFileProblem)
        {
            StorageSource = source;
            MainFileProblem = mainFileProblem;
            return this;
        }

        /// <summary>A successful load.</summary>
        public static SaveLoadResult Loaded(PlayerSave save, int sourceVersion, bool migrated, List<SaveIssue> issues)
        {
            return new SaveLoadResult
            {
                Success = true,
                Save = save,
                SourceVersion = sourceVersion,
                Migrated = migrated,
                Issues = issues ?? new List<SaveIssue>()
            };
        }

        /// <summary>A failed load.</summary>
        public static SaveLoadResult Failed(string error, int sourceVersion)
        {
            return new SaveLoadResult
            {
                Success = false,
                Error = error,
                SourceVersion = sourceVersion,
                Issues = new List<SaveIssue>()
            };
        }
    }
}
