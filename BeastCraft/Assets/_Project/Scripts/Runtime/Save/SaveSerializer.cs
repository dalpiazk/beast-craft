using System;
using System.Collections.Generic;

namespace BeastCraft.Save
{
    /// <summary>
    /// Turns a <see cref="PlayerSave"/> into JSON text and back, through an injected
    /// <see cref="ISaveJsonSerializer"/> (Unity's <c>JsonUtility</c> in the game). Pure C#: file or
    /// cloud IO is <see cref="ISaveStorage"/>'s job (see <see cref="SaveStore"/>).
    /// <para>
    /// <strong>Loading</strong> reads the schema version first, refuses a missing version or one
    /// newer than this build, runs the <see cref="ISaveMigration"/> steps from the save's version up
    /// to the current one, reads the result, fills in any missing collections
    /// (<see cref="PlayerSave.EnsureInitialized"/>) and reports unresolvable ids and bad values with
    /// <see cref="SaveValidator"/>. It never throws: every failure is a
    /// <see cref="SaveLoadResult.Failed"/> result with a reason.
    /// </para>
    /// </summary>
    public class SaveSerializer
    {
        private readonly ISaveJsonSerializer _json;
        private readonly ISaveContentCatalog _catalog;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        /// <param name="json">The JSON engine. Required.</param>
        /// <param name="catalog">Ids to validate against; null runs only the structural checks.</param>
        /// <param name="migrations">Upgrade steps; null means <see cref="SaveMigrations.All"/>. At most one per <see cref="ISaveMigration.FromVersion"/>.</param>
        /// <param name="currentVersion">
        /// The version written and read up to. Defaults to <see cref="PlayerSave.CurrentSchemaVersion"/>;
        /// overridable so migration chains can be tested ahead of a real schema bump.
        /// </param>
        public SaveSerializer(ISaveJsonSerializer json, ISaveContentCatalog catalog = null, IEnumerable<ISaveMigration> migrations = null, int currentVersion = PlayerSave.CurrentSchemaVersion)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (currentVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(currentVersion), "Schema versions start at 1.");
            }

            _json = json;
            _catalog = catalog;
            CurrentVersion = currentVersion;

            foreach (ISaveMigration migration in migrations ?? SaveMigrations.All())
            {
                if (migration == null)
                {
                    continue;
                }

                if (_migrations.ContainsKey(migration.FromVersion))
                {
                    throw new ArgumentException("Two save migrations upgrade from version " + migration.FromVersion + ".", nameof(migrations));
                }

                _migrations.Add(migration.FromVersion, migration);
            }
        }

        /// <summary>The schema version this serializer writes and reads up to.</summary>
        public int CurrentVersion { get; private set; }

        /// <summary>
        /// <paramref name="save"/> as JSON, with its <see cref="PlayerSave.SchemaVersion"/> stamped to
        /// <see cref="CurrentVersion"/> first. Null for a null save.
        /// </summary>
        public string Serialize(PlayerSave save)
        {
            if (save == null)
            {
                return null;
            }

            save.SchemaVersion = CurrentVersion;
            return _json.ToJson(save);
        }

        /// <summary>Reads, upgrades and validates <paramref name="json"/>. Never throws; see the class remarks.</summary>
        public SaveLoadResult Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return SaveLoadResult.Failed("The save is empty.", 0);
            }

            SaveHeader header;

            try
            {
                header = _json.FromJson<SaveHeader>(json);
            }
            catch (Exception e)
            {
                return SaveLoadResult.Failed("The save is not readable JSON: " + e.Message, 0);
            }

            int sourceVersion = header == null ? 0 : header.SchemaVersion;

            if (sourceVersion < 1)
            {
                return SaveLoadResult.Failed("The save has no valid SchemaVersion.", sourceVersion);
            }

            if (sourceVersion > CurrentVersion)
            {
                return SaveLoadResult.Failed("The save was written by a newer build (schema " + sourceVersion + "); this build reads up to schema " + CurrentVersion + ".", sourceVersion);
            }

            string text = json;

            for (int version = sourceVersion; version < CurrentVersion; version++)
            {
                if (!_migrations.TryGetValue(version, out ISaveMigration migration))
                {
                    return SaveLoadResult.Failed("No migration upgrades a save from schema " + version + " to " + (version + 1) + ".", sourceVersion);
                }

                try
                {
                    text = migration.Upgrade(text, _json);
                }
                catch (Exception e)
                {
                    return SaveLoadResult.Failed("Migrating the save from schema " + version + " to " + (version + 1) + " failed: " + e.Message, sourceVersion);
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    return SaveLoadResult.Failed("Migrating the save from schema " + version + " to " + (version + 1) + " produced nothing.", sourceVersion);
                }
            }

            PlayerSave save;

            try
            {
                save = _json.FromJson<PlayerSave>(text);
            }
            catch (Exception e)
            {
                return SaveLoadResult.Failed("The save could not be read: " + e.Message, sourceVersion);
            }

            if (save == null)
            {
                return SaveLoadResult.Failed("The save could not be read.", sourceVersion);
            }

            save.SchemaVersion = CurrentVersion;
            save.EnsureInitialized();
            return SaveLoadResult.Loaded(save, sourceVersion, sourceVersion < CurrentVersion, SaveValidator.Validate(save, _catalog));
        }

        /// <summary>Just the version field of a save, read before anything else so migrations can be chosen.</summary>
        [Serializable]
        public class SaveHeader
        {
            public int SchemaVersion;
        }
    }
}
