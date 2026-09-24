namespace BeastCraft.Save
{
    /// <summary>
    /// An <see cref="ISaveStorage"/> that keeps a backup of each slot and can say which file a read
    /// used. <see cref="SaveStore"/> uses it to report the source of a load and to retry the backup
    /// when the main file reads but does not load. Implemented by <see cref="FileSaveStorage"/>.
    /// Like <see cref="ISaveStorage"/>, implementations report failure through results, not throws.
    /// </summary>
    public interface IBackupSaveStorage : ISaveStorage
    {
        /// <summary>The slot's main file if usable, otherwise its backup, saying which and why.</summary>
        SaveFileResult Read(string slot);

        /// <summary>The slot's backup only (<see cref="SaveFileSource.Backup"/> on success).</summary>
        SaveFileResult ReadBackup(string slot);
    }
}
