using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// An in-memory <see cref="IBackupSaveStorage"/> that behaves like <see cref="FileSaveStorage"/>:
    /// each write keeps the previous text as the slot's backup, and a read falls back to the backup
    /// when the main text is not a complete JSON object. For tests and for screenshot runs, which
    /// must never touch the player's real save.
    /// </summary>
    public sealed class MemorySaveStorage : IBackupSaveStorage
    {
        private readonly Dictionary<string, string> _main = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _backup = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>How many writes succeeded (every slot).</summary>
        public int Writes { get; private set; }

        /// <summary>When set, every write fails (a full disk, a read-only folder).</summary>
        public bool FailWrites;

        public bool Exists(string slot)
        {
            return slot != null && (_main.ContainsKey(slot) || _backup.ContainsKey(slot));
        }

        public bool TryRead(string slot, out string contents)
        {
            SaveFileResult read = Read(slot);
            contents = read.Success ? read.Contents : null;
            return read.Success;
        }

        public bool TryWrite(string slot, string contents)
        {
            if (FailWrites || slot == null || contents == null)
            {
                return false;
            }

            if (_main.TryGetValue(slot, out string previous) && FileSaveStorage.LooksLikeCompleteJsonObject(previous))
            {
                _backup[slot] = previous;
            }

            _main[slot] = contents;
            Writes++;
            return true;
        }

        public bool Delete(string slot)
        {
            bool had = slot != null && (_main.Remove(slot) | _backup.Remove(slot));
            return had;
        }

        public SaveFileResult Read(string slot)
        {
            if (slot == null)
            {
                return SaveFileResult.Failed(SaveFileError.NotFound, "No slot.");
            }

            bool hasMain = _main.TryGetValue(slot, out string main);
            if (hasMain && FileSaveStorage.LooksLikeCompleteJsonObject(main))
            {
                return SaveFileResult.Read(main, SaveFileSource.Main, DateTime.MinValue, null);
            }

            if (_backup.TryGetValue(slot, out string backup))
            {
                return SaveFileResult.Read(backup, SaveFileSource.Backup, DateTime.MinValue, hasMain ? "corrupt (not a complete JSON object)" : "missing");
            }

            return hasMain
                       ? SaveFileResult.Failed(SaveFileError.Corrupt, "The save in slot '" + slot + "' is corrupt and there is no backup.")
                       : SaveFileResult.Failed(SaveFileError.NotFound, "No save in slot '" + slot + "'.");
        }

        public SaveFileResult ReadBackup(string slot)
        {
            return slot != null && _backup.TryGetValue(slot, out string backup)
                       ? SaveFileResult.Read(backup, SaveFileSource.Backup, DateTime.MinValue, null)
                       : SaveFileResult.Failed(SaveFileError.NotFound, "No backup of slot '" + slot + "'.");
        }

        /// <summary>The slot's main text as stored (null when none): for tests.</summary>
        public string Peek(string slot)
        {
            return slot != null && _main.TryGetValue(slot, out string text) ? text : null;
        }

        /// <summary>Overwrites the slot's main text without touching its backup (a torn write, for tests).</summary>
        public void Corrupt(string slot, string text)
        {
            _main[slot] = text;
        }
    }
}
