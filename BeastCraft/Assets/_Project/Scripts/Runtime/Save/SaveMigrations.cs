using System.Collections.Generic;

namespace BeastCraft.Save
{
    /// <summary>
    /// The game's registered schema upgrades, oldest first. Empty while
    /// <see cref="PlayerSave.CurrentSchemaVersion"/> is 1. When the schema changes: bump the version,
    /// add a step whose <see cref="ISaveMigration.FromVersion"/> is the previous one, and add a
    /// round-trip test that loads a checked-in example of the old shape.
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>A fresh list of every step (callers may append to it).</summary>
        public static List<ISaveMigration> All()
        {
            return new List<ISaveMigration>();
        }
    }
}
