using System;

namespace BeastCraft.Save
{
    /// <summary>
    /// A <see cref="SaveSerializer"/> bound to an <see cref="ISaveStorage"/>: save and load a
    /// <see cref="PlayerSave"/> by slot name. Non-throwing, like the serializer.
    /// </summary>
    public class SaveStore
    {
        private readonly ISaveStorage _storage;
        private readonly SaveSerializer _serializer;

        public SaveStore(ISaveStorage storage, SaveSerializer serializer)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>Whether <paramref name="slot"/> holds a save.</summary>
        public bool Exists(string slot)
        {
            return _storage.Exists(slot);
        }

        /// <summary>Writes <paramref name="save"/> to <paramref name="slot"/>. False for a null save, a serializer failure or a storage failure.</summary>
        public bool Save(string slot, PlayerSave save)
        {
            string json;

            try
            {
                json = _serializer.Serialize(save);
            }
            catch (Exception)
            {
                return false;
            }

            return json != null && _storage.TryWrite(slot, json);
        }

        /// <summary>
        /// Reads <paramref name="slot"/>; see <see cref="SaveSerializer.Deserialize"/>. A missing slot
        /// is a failed result. The result's <see cref="SaveLoadResult.StorageSource"/> says which
        /// file was used.
        /// <para>
        /// Over an <see cref="IBackupSaveStorage"/> (e.g. <see cref="FileSaveStorage"/>): when the main
        /// file reads but fails to load (unparseable, a failed migration), the backup is loaded
        /// instead and <see cref="SaveLoadResult.MainFileProblem"/> says why. There is no retry when
        /// the main save was written by a newer build: loading the older backup, then saving, would
        /// overwrite that newer progress. When the backup fails too, the main file's failure is
        /// returned.
        /// </para>
        /// </summary>
        public SaveLoadResult Load(string slot)
        {
            if (!(_storage is IBackupSaveStorage backed))
            {
                if (!_storage.TryRead(slot, out string json))
                {
                    return SaveLoadResult.Failed("No save in slot '" + slot + "'.", 0);
                }

                return _serializer.Deserialize(json).WithStorage(SaveFileSource.Main, null);
            }

            SaveFileResult read = backed.Read(slot);

            if (!read.Success)
            {
                string error = read.ErrorKind == SaveFileError.NotFound ? "No save in slot '" + slot + "'." : read.Error;
                return SaveLoadResult.Failed(error, 0).WithStorage(SaveFileSource.None, read.MainFileProblem);
            }

            SaveLoadResult loaded = _serializer.Deserialize(read.Contents).WithStorage(read.Source, read.MainFileProblem);

            if (loaded.Success || read.Source != SaveFileSource.Main || loaded.SourceVersion > _serializer.CurrentVersion)
            {
                return loaded;
            }

            SaveFileResult backup = backed.ReadBackup(slot);

            if (!backup.Success)
            {
                return loaded;
            }

            SaveLoadResult fromBackup = _serializer.Deserialize(backup.Contents);

            if (!fromBackup.Success)
            {
                return loaded;
            }

            return fromBackup.WithStorage(SaveFileSource.Backup, "read but failed to load (" + loaded.Error + ")");
        }
    }
}
