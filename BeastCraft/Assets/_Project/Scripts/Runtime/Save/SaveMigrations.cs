using System.Collections.Generic;

namespace BeastCraft.Save
{
    /// <summary>
    /// The game's registered schema upgrades, oldest first. When the schema changes: bump
    /// <see cref="PlayerSave.CurrentSchemaVersion"/>, add a step whose
    /// <see cref="ISaveMigration.FromVersion"/> is the previous one, and add a round-trip test that
    /// loads an example of the old shape.
    /// <list type="bullet">
    /// <item>1 to 2: gear (<see cref="PlayerSave.Gear"/>, <see cref="PlayerSave.AvatarEquippedGear"/>,
    /// <see cref="OwnedBeast.EquippedGear"/>): <see cref="AddGear"/>.</item>
    /// </list>
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>A fresh list of every step (callers may append to it).</summary>
        public static List<ISaveMigration> All()
        {
            return new List<ISaveMigration> { new AddGear() };
        }

        /// <summary>
        /// Schema 1 to 2: a v1 save owns no gear, so the upgrade reads it into the current type (the
        /// new gear fields take their empty defaults), fills in anything missing and writes it back.
        /// </summary>
        public sealed class AddGear : ISaveMigration
        {
            public int FromVersion
            {
                get { return 1; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.SchemaVersion = 2;
                return serializer.ToJson(save);
            }
        }
    }
}
