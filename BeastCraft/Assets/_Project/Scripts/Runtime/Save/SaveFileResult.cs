using System;

namespace BeastCraft.Save
{
    /// <summary>Which file a <see cref="FileSaveStorage"/> read came from.</summary>
    public enum SaveFileSource
    {
        /// <summary>Nothing was read (a failed result, or a write/delete).</summary>
        None,

        /// <summary>The slot's main file.</summary>
        Main,

        /// <summary>The slot's one-generation backup: the main file was missing, unreadable or corrupt.</summary>
        Backup
    }

    /// <summary>Why a <see cref="FileSaveStorage"/> operation failed.</summary>
    public enum SaveFileError
    {
        /// <summary>No failure.</summary>
        None,

        /// <summary>The slot name is not allowed (see <see cref="FileSaveStorage.IsValidSlotName"/>).</summary>
        InvalidSlot,

        /// <summary>The slot holds nothing: no main file and no backup (or nothing to delete).</summary>
        NotFound,

        /// <summary>The files exist but none of them holds usable text (bad UTF-8, empty, failed the content check).</summary>
        Corrupt,

        /// <summary>Contents to write were null or failed the content check, so they would not read back.</summary>
        InvalidContents,

        /// <summary>The file system refused: permissions, a locked or missing directory, a full disk, and so on.</summary>
        Io
    }

    /// <summary>
    /// What a <see cref="FileSaveStorage"/> read, write or delete did. Never thrown: failures carry
    /// an <see cref="ErrorKind"/> and a human-readable <see cref="Error"/>.
    /// </summary>
    public class SaveFileResult
    {
        private SaveFileResult()
        {
        }

        /// <summary>Whether the operation did what was asked.</summary>
        public bool Success { get; private set; }

        /// <summary>The text read, on a successful read; null otherwise.</summary>
        public string Contents { get; private set; }

        /// <summary>Which file a successful read used.</summary>
        public SaveFileSource Source { get; private set; }

        /// <summary>When the file that was read was last written (UTC); <see cref="DateTime.MinValue"/> when unknown.</summary>
        public DateTime LastWriteUtc { get; private set; }

        /// <summary>The failure category; <see cref="SaveFileError.None"/> on success.</summary>
        public SaveFileError ErrorKind { get; private set; }

        /// <summary>Why the operation failed; null on success.</summary>
        public string Error { get; private set; }

        /// <summary>
        /// On a read served from the backup, why the main file was skipped (e.g. "missing" or
        /// "not valid UTF-8"); null otherwise. Worth logging: the player lost their latest save.
        /// </summary>
        public string MainFileProblem { get; private set; }

        /// <summary>A successful read.</summary>
        public static SaveFileResult Read(string contents, SaveFileSource source, DateTime lastWriteUtc, string mainFileProblem)
        {
            return new SaveFileResult
            {
                Success = true,
                Contents = contents,
                Source = source,
                LastWriteUtc = lastWriteUtc,
                MainFileProblem = mainFileProblem
            };
        }

        /// <summary>A successful write or delete.</summary>
        public static SaveFileResult Done()
        {
            return new SaveFileResult { Success = true };
        }

        /// <summary>A failure.</summary>
        public static SaveFileResult Failed(SaveFileError kind, string error, string mainFileProblem = null)
        {
            return new SaveFileResult
            {
                Success = false,
                ErrorKind = kind,
                Error = error,
                MainFileProblem = mainFileProblem
            };
        }

        public override string ToString()
        {
            return Success ? "OK (" + Source + ")" : ErrorKind + ": " + Error;
        }
    }
}
