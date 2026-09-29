using System;
using System.Globalization;
using System.IO;
using BeastCraft.Localization;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>What an export or import through an <see cref="ISaveTransfer"/> did.</summary>
    public sealed class SaveTransferResult
    {
        /// <summary>Whether the file was written (export) or read (import).</summary>
        public bool Success;

        /// <summary>The player cancelled (a system file picker was closed): not an error, nothing to say.</summary>
        public bool Cancelled;

        /// <summary>The file's text (import only).</summary>
        public string Contents;

        /// <summary>Where the file is, for the player ("Documents/BeastCraft/beastcraft-slot1-....json").</summary>
        public string Location;

        /// <summary>Why it failed, as plain text (a host's own message); null on success or when <see cref="ErrorKey"/> says it.</summary>
        public string Error;

        /// <summary>Why it failed, as a text key (<c>ui.save_transfer.*</c>) with <see cref="ErrorArg"/>; null otherwise.</summary>
        public string ErrorKey;

        /// <summary>The value <see cref="ErrorKey"/>'s text shows (a path, a file name, a system message).</summary>
        public string ErrorArg;

        public static SaveTransferResult Failed(string error)
        {
            return new SaveTransferResult { Error = error };
        }

        /// <summary>A failure the player reads as <paramref name="errorKey"/>'s text with <paramref name="arg"/>.</summary>
        public static SaveTransferResult FailedWith(string errorKey, string arg)
        {
            return new SaveTransferResult { ErrorKey = errorKey, ErrorArg = arg };
        }

        /// <summary>Why it failed, for the player: <see cref="ErrorKey"/>'s text from <paramref name="text"/>, else <see cref="Error"/>.</summary>
        public string Describe(StringTable text)
        {
            return ErrorKey != null && text != null ? text.Format(ErrorKey, ErrorArg) : Error;
        }

        public static SaveTransferResult Cancel()
        {
            return new SaveTransferResult { Cancelled = true };
        }
    }

    /// <summary>
    /// The platform seam for exporting a save slot to a file the player keeps, and importing one back.
    /// Engine-neutral: the desktop host writes and reads a folder (<see cref="FolderSaveTransfer"/>);
    /// a phone would hand the file to the system's document picker, which answers later, so both calls
    /// report through a callback (called once, possibly on a later frame). The game validates an
    /// imported save (<see cref="GameSession.ImportSlot"/>) before anything is overwritten.
    /// </summary>
    public interface ISaveTransfer
    {
        /// <summary>Writes <paramref name="contents"/> as a file named like <paramref name="suggestedName"/>.</summary>
        void Export(string suggestedName, string contents, Action<SaveTransferResult> done);

        /// <summary>Reads a save file the player chose (or the platform's latest export).</summary>
        void Import(Action<SaveTransferResult> done);
    }

    /// <summary>
    /// <see cref="ISaveTransfer"/> over a plain folder (the desktop host: <c>Documents/BeastCraft</c>).
    /// Export writes <c>&lt;name&gt;.json</c> there; import reads the newest <c>.json</c> file in it, so
    /// "export, copy the file to another machine's folder, import" needs no file picker. Never throws.
    /// </summary>
    public sealed class FolderSaveTransfer : ISaveTransfer
    {
        /// <summary>The largest file import reads (a real save is a few tens of KB).</summary>
        public const long MaxImportBytes = 4 * 1024 * 1024;

        public FolderSaveTransfer(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("A folder is required.", nameof(folder));
            }

            Folder = folder;
        }

        /// <summary>The folder exports go to and imports come from.</summary>
        public string Folder { get; }

        /// <summary>The desktop default: the user's Documents folder (else the per-user data folder), under <c>BeastCraft</c>.</summary>
        public static string DefaultFolder()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(documents) ? Path.Combine(BeastCraft.Save.SaveLocations.DefaultRoot(), "exports") : Path.Combine(documents, BeastCraft.Save.SaveLocations.AppFolderName);
        }

        /// <summary>An export file name for <paramref name="slot"/> at <paramref name="utc"/>: <c>beastcraft-slot1-20260929-1405</c>.</summary>
        public static string ExportName(string slot, DateTime utc)
        {
            return "beastcraft-" + slot + "-" + utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        }

        public void Export(string suggestedName, string contents, Action<SaveTransferResult> done)
        {
            SaveTransferResult result;
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, SafeName(suggestedName) + ".json");
                File.WriteAllText(path, contents ?? string.Empty);
                result = new SaveTransferResult { Success = true, Location = path };
            }
            catch (Exception e)
            {
                result = SaveTransferResult.FailedWith("ui.save_transfer.write_failed", e.Message);
            }

            done?.Invoke(result);
        }

        public void Import(Action<SaveTransferResult> done)
        {
            SaveTransferResult result;
            try
            {
                FileInfo newest = null;
                if (Directory.Exists(Folder))
                {
                    foreach (FileInfo file in new DirectoryInfo(Folder).GetFiles("*.json"))
                    {
                        if (newest == null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                        {
                            newest = file;
                        }
                    }
                }

                if (newest == null)
                {
                    result = SaveTransferResult.FailedWith("ui.save_transfer.none_found", Folder);
                }
                else if (newest.Length > MaxImportBytes)
                {
                    result = SaveTransferResult.FailedWith("ui.save_transfer.too_large", newest.Name);
                }
                else
                {
                    result = new SaveTransferResult { Success = true, Contents = File.ReadAllText(newest.FullName), Location = newest.FullName };
                }
            }
            catch (Exception e)
            {
                result = SaveTransferResult.FailedWith("ui.save_transfer.read_failed", e.Message);
            }

            done?.Invoke(result);
        }

        private static string SafeName(string name)
        {
            string safe = string.IsNullOrWhiteSpace(name) ? "beastcraft-save" : name;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '-');
            }

            return safe;
        }
    }
}
