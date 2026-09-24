using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BeastCraft.Save
{
    /// <summary>
    /// <see cref="ISaveStorage"/> over local files in one directory — pure System.IO.
    /// (<see cref="SaveLocations"/> supplies the game's default directory.)
    /// <para>
    /// <strong>Layout.</strong> Slot <c>main</c> is <c>main.save</c>; its one-generation backup is
    /// <c>main.save.bak</c>; <c>main.save.tmp</c> exists only mid-write (or after a crash mid-write,
    /// and is then ignored and overwritten). Slot names are validated
    /// (<see cref="IsValidSlotName"/>): letters, digits, <c>_</c> and <c>-</c> only, so a name can
    /// never leave the directory or pick its own extension.
    /// </para>
    /// <para>
    /// <strong>Writes are atomic.</strong> The text is written in full to the temp file and flushed to
    /// disk, then swapped in: the previous main file becomes the backup and the temp file becomes the
    /// main file (<see cref="File.Replace(string, string, string, bool)"/>, with a copy/delete/move
    /// fallback where that is unsupported). A crash at any point leaves either the old or the new
    /// save readable. A previous main file that is itself corrupt is discarded rather than rotated
    /// into the backup, so a good backup is never overwritten by a bad file.
    /// </para>
    /// <para>
    /// <strong>Reads fall back.</strong> When the main file is missing, unreadable or corrupt, the
    /// backup is read instead and the result says so (<see cref="SaveFileResult.Source"/>,
    /// <see cref="SaveFileResult.MainFileProblem"/>). "Corrupt" means not valid UTF-8, blank, or
    /// failing the content check — by default <see cref="LooksLikeCompleteJsonObject"/>, which catches
    /// the truncation a torn write or bad sector leaves. Writes refuse text that would fail the same
    /// check.
    /// </para>
    /// <para>
    /// <strong>Encoding.</strong> UTF-8 without a byte-order mark on write; a leading BOM is
    /// tolerated on read (a hand-edited file).
    /// </para>
    /// <para>
    /// <strong>Errors.</strong> The detailed methods (<see cref="Read"/>, <see cref="Write"/>,
    /// <see cref="Remove"/>) never throw: IO failures come back as a <see cref="SaveFileResult"/> with
    /// an <see cref="SaveFileError"/> and message. The <see cref="ISaveStorage"/> methods wrap them as
    /// plain booleans.
    /// </para>
    /// <para>
    /// <strong>Concurrency.</strong> Every operation holds one process-wide lock, so any number of
    /// instances and threads in one process are safe (saves are rare; contention does not matter).
    /// There is no cross-process file locking: two game processes writing the same directory can
    /// race, and the last swap wins — each file is still whole, never interleaved.
    /// </para>
    /// </summary>
    public class FileSaveStorage : IBackupSaveStorage
    {
        /// <summary>The main file's extension.</summary>
        public const string Extension = ".save";

        /// <summary>The backup file's extension (after the main one).</summary>
        public const string BackupSuffix = ".bak";

        /// <summary>The in-progress write's extension (after the main one).</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>The longest slot name allowed.</summary>
        public const int MaxSlotNameLength = 64;

        private static readonly object Gate = new object();

        private static readonly UTF8Encoding WriteEncoding = new UTF8Encoding(false);

        private static readonly UTF8Encoding StrictReadEncoding = new UTF8Encoding(false, true);

        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private readonly Func<string, bool> _contentCheck;

        /// <param name="rootDirectory">The directory the slot files live in. Created on the first write if missing.</param>
        /// <param name="contentCheck">
        /// Whether decoded text is a usable save; a file failing it is treated as corrupt. Null means
        /// <see cref="LooksLikeCompleteJsonObject"/>. Must not throw.
        /// </param>
        public FileSaveStorage(string rootDirectory, Func<string, bool> contentCheck = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("A save directory is required.", nameof(rootDirectory));
            }

            RootDirectory = Path.GetFullPath(rootDirectory);
            _contentCheck = contentCheck ?? LooksLikeCompleteJsonObject;
        }

        /// <summary>The absolute directory the slot files live in.</summary>
        public string RootDirectory { get; private set; }

        /// <summary>
        /// Whether <paramref name="slot"/> is an allowed slot name: 1 to <see cref="MaxSlotNameLength"/>
        /// ASCII letters, digits, <c>_</c> or <c>-</c>, starting with a letter or digit, and not a
        /// Windows device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>…, any case). No dots, separators or
        /// whitespace, so no path traversal and no extension of its own.
        /// </summary>
        public static bool IsValidSlotName(string slot)
        {
            if (string.IsNullOrEmpty(slot) || slot.Length > MaxSlotNameLength || !IsAsciiLetterOrDigit(slot[0]))
            {
                return false;
            }

            foreach (char c in slot)
            {
                if (!IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
                {
                    return false;
                }
            }

            foreach (string reserved in ReservedDeviceNames)
            {
                if (string.Equals(slot, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The default content check: after trimming whitespace, the text starts with <c>{</c> and
        /// ends with <c>}</c> — a whole JSON object, as <see cref="SaveSerializer"/> writes. It is a
        /// cheap truncation check, not a parse; the serializer reports anything subtler on load.
        /// </summary>
        public static bool LooksLikeCompleteJsonObject(string text)
        {
            if (text == null)
            {
                return false;
            }

            string trimmed = text.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}';
        }

        /// <summary>The main file for <paramref name="slot"/>; null for an invalid slot name.</summary>
        public string GetSlotPath(string slot)
        {
            return IsValidSlotName(slot) ? Path.Combine(RootDirectory, slot + Extension) : null;
        }

        /// <summary>The backup file for <paramref name="slot"/>; null for an invalid slot name.</summary>
        public string GetBackupPath(string slot)
        {
            string main = GetSlotPath(slot);
            return main == null ? null : main + BackupSuffix;
        }

        /// <summary>Whether <paramref name="slot"/> has a main or backup file (their contents are not checked). False for an invalid name or an IO failure.</summary>
        public bool Exists(string slot)
        {
            string main = GetSlotPath(slot);

            if (main == null)
            {
                return false;
            }

            lock (Gate)
            {
                try
                {
                    return File.Exists(main) || File.Exists(main + BackupSuffix);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary><see cref="Read"/>'s text; false (and null) on any failure.</summary>
        public bool TryRead(string slot, out string contents)
        {
            SaveFileResult result = Read(slot);
            contents = result.Contents;
            return result.Success;
        }

        /// <summary><see cref="Write"/>; false on any failure.</summary>
        public bool TryWrite(string slot, string contents)
        {
            return Write(slot, contents).Success;
        }

        /// <summary><see cref="Remove"/>; false when there was nothing to remove or it could not be removed.</summary>
        public bool Delete(string slot)
        {
            return Remove(slot).Success;
        }

        /// <summary>
        /// Reads <paramref name="slot"/>: the main file if it is usable, otherwise the backup (see
        /// the class remarks). Never throws.
        /// </summary>
        public SaveFileResult Read(string slot)
        {
            string main = GetSlotPath(slot);

            if (main == null)
            {
                return InvalidSlot(slot);
            }

            lock (Gate)
            {
                FileProbe mainProbe = Probe(main);

                if (mainProbe.Usable)
                {
                    return SaveFileResult.Read(mainProbe.Text, SaveFileSource.Main, mainProbe.LastWriteUtc, null);
                }

                FileProbe backupProbe = Probe(main + BackupSuffix);

                if (backupProbe.Usable)
                {
                    return SaveFileResult.Read(backupProbe.Text, SaveFileSource.Backup, backupProbe.LastWriteUtc, mainProbe.Problem);
                }

                if (mainProbe.Missing && backupProbe.Missing)
                {
                    return SaveFileResult.Failed(SaveFileError.NotFound, "No save in slot '" + slot + "'.");
                }

                SaveFileError kind = mainProbe.IoFailure || backupProbe.IoFailure ? SaveFileError.Io : SaveFileError.Corrupt;
                return SaveFileResult.Failed(
                    kind,
                    "Slot '" + slot + "' could not be read: main file " + mainProbe.Problem + "; backup " + backupProbe.Problem + ".",
                    mainProbe.Problem);
            }
        }

        /// <summary>
        /// Reads only <paramref name="slot"/>'s backup file, ignoring the main one — for a caller
        /// (<see cref="SaveStore.Load"/>) whose main file read fine but did not load. Never throws.
        /// </summary>
        public SaveFileResult ReadBackup(string slot)
        {
            string main = GetSlotPath(slot);

            if (main == null)
            {
                return InvalidSlot(slot);
            }

            lock (Gate)
            {
                FileProbe backupProbe = Probe(main + BackupSuffix);

                if (backupProbe.Usable)
                {
                    return SaveFileResult.Read(backupProbe.Text, SaveFileSource.Backup, backupProbe.LastWriteUtc, null);
                }

                if (backupProbe.Missing)
                {
                    return SaveFileResult.Failed(SaveFileError.NotFound, "Slot '" + slot + "' has no backup.");
                }

                return SaveFileResult.Failed(
                    backupProbe.IoFailure ? SaveFileError.Io : SaveFileError.Corrupt,
                    "Slot '" + slot + "' backup " + backupProbe.Problem + ".");
            }
        }

        /// <summary>
        /// Atomically replaces <paramref name="slot"/>'s text, keeping the previous save as the
        /// backup (see the class remarks). Creates the directory if needed. Never throws.
        /// </summary>
        public SaveFileResult Write(string slot, string contents)
        {
            string main = GetSlotPath(slot);

            if (main == null)
            {
                return InvalidSlot(slot);
            }

            if (contents == null || !SafeCheck(contents))
            {
                return SaveFileResult.Failed(SaveFileError.InvalidContents, "Refusing to write text to slot '" + slot + "' that would not read back as a save.");
            }

            string backup = main + BackupSuffix;
            string temp = main + TempSuffix;

            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(RootDirectory);
                    WriteFully(temp, WriteEncoding.GetBytes(contents));

                    FileProbe previous = Probe(main);

                    if (previous.Corrupt)
                    {
                        // Never rotate a corrupt file over a possibly good backup. (An unreadable
                        // one — locked, no permission — is left alone; the swap below reports it.)
                        File.Delete(main);
                    }

                    if (File.Exists(main))
                    {
                        SwapIn(temp, main, backup);
                    }
                    else
                    {
                        File.Move(temp, main);
                    }

                    return SaveFileResult.Done();
                }
                catch (Exception e)
                {
                    TryDeleteQuietly(temp);
                    return SaveFileResult.Failed(SaveFileError.Io, "Could not write slot '" + slot + "': " + e.Message);
                }
            }
        }

        /// <summary>
        /// Removes <paramref name="slot"/>'s main, backup and any leftover temp file. Fails with
        /// <see cref="SaveFileError.NotFound"/> when there was no main or backup file. Never throws.
        /// </summary>
        public SaveFileResult Remove(string slot)
        {
            string main = GetSlotPath(slot);

            if (main == null)
            {
                return InvalidSlot(slot);
            }

            lock (Gate)
            {
                try
                {
                    bool existed = File.Exists(main) || File.Exists(main + BackupSuffix);

                    File.Delete(main);
                    File.Delete(main + BackupSuffix);
                    File.Delete(main + TempSuffix);

                    return existed ? SaveFileResult.Done() : SaveFileResult.Failed(SaveFileError.NotFound, "No save in slot '" + slot + "'.");
                }
                catch (Exception e)
                {
                    return SaveFileResult.Failed(SaveFileError.Io, "Could not delete slot '" + slot + "': " + e.Message);
                }
            }
        }

        /// <summary>
        /// The slots with a main or backup file in <see cref="RootDirectory"/>, sorted ordinally.
        /// Files whose names are not valid slots are ignored. Empty when the directory is missing or
        /// unreadable.
        /// </summary>
        public List<string> ListSlots()
        {
            SortedSet<string> slots = new SortedSet<string>(StringComparer.Ordinal);

            lock (Gate)
            {
                try
                {
                    if (!Directory.Exists(RootDirectory))
                    {
                        return new List<string>();
                    }

                    foreach (string path in Directory.GetFiles(RootDirectory))
                    {
                        string name = Path.GetFileName(path);
                        string slot = null;

                        if (name.EndsWith(Extension, StringComparison.Ordinal))
                        {
                            slot = name.Substring(0, name.Length - Extension.Length);
                        }
                        else if (name.EndsWith(Extension + BackupSuffix, StringComparison.Ordinal))
                        {
                            slot = name.Substring(0, name.Length - Extension.Length - BackupSuffix.Length);
                        }

                        if (IsValidSlotName(slot))
                        {
                            slots.Add(slot);
                        }
                    }
                }
                catch (Exception)
                {
                    return new List<string>();
                }
            }

            return new List<string>(slots);
        }

        private static SaveFileResult InvalidSlot(string slot)
        {
            return SaveFileResult.Failed(
                SaveFileError.InvalidSlot,
                "'" + (slot ?? "(null)") + "' is not a valid save slot name (1-" + MaxSlotNameLength + " letters, digits, '_' or '-', starting with a letter or digit).");
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        private static void WriteFully(string path, byte[] bytes)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        /// <summary>temp becomes main, main becomes backup. File.Replace where it works; otherwise copy/delete/move, which a crash can only leave with the old save in the backup.</summary>
        private static void SwapIn(string temp, string main, string backup)
        {
            try
            {
                File.Replace(temp, main, backup, true);
                return;
            }
            catch (Exception)
            {
                // Some platforms or file systems do not support File.Replace; fall through.
            }

            if (!File.Exists(temp))
            {
                // Replace moved the new text in but failed afterwards (e.g. on the backup's metadata).
                if (File.Exists(main))
                {
                    return;
                }

                throw new IOException("The new save was lost while swapping it in.");
            }

            if (File.Exists(main))
            {
                File.Copy(main, backup, true);
                File.Delete(main);
            }

            File.Move(temp, main);
        }

        private static void TryDeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort: a leftover temp file is ignored by reads and overwritten by the next write.
            }
        }

        private bool SafeCheck(string text)
        {
            try
            {
                return _contentCheck(text);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private FileProbe Probe(string path)
        {
            FileProbe probe = new FileProbe();

            try
            {
                if (!File.Exists(path))
                {
                    probe.Missing = true;
                    probe.Problem = "missing";
                    return probe;
                }

                byte[] bytes = File.ReadAllBytes(path);
                probe.LastWriteUtc = File.GetLastWriteTimeUtc(path);

                int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                string text;

                try
                {
                    text = StrictReadEncoding.GetString(bytes, start, bytes.Length - start);
                }
                catch (DecoderFallbackException)
                {
                    probe.Problem = "not valid UTF-8";
                    return probe;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    probe.Problem = "empty";
                    return probe;
                }

                if (!SafeCheck(text))
                {
                    probe.Problem = "failed the content check (truncated or not a save)";
                    return probe;
                }

                probe.Usable = true;
                probe.Text = text;
                return probe;
            }
            catch (Exception e)
            {
                probe.IoFailure = true;
                probe.Problem = "unreadable (" + e.Message + ")";
                return probe;
            }
        }

        private class FileProbe
        {
            public bool Usable;
            public bool Missing;
            public bool IoFailure;
            public string Text;
            public string Problem;
            public DateTime LastWriteUtc = DateTime.MinValue;

            /// <summary>Present and readable, but its text is not a usable save.</summary>
            public bool Corrupt
            {
                get { return !Usable && !Missing && !IoFailure; }
            }
        }
    }
}
