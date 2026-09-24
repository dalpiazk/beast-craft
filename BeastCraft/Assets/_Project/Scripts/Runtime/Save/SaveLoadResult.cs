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
