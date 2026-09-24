using System.Collections.Generic;
using BeastCraft.Campaign;

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
    /// <item>2 to 3: the region campaign (<see cref="PlayerSave.Campaign"/>) and the level-cap bank
    /// (<c>BeastProgress.BankedXp</c>): <see cref="AddCampaign"/>.</item>
    /// <item>3 to 4: the economy (<see cref="PlayerSave.Gold"/>, <see cref="PlayerSave.Consumables"/>,
    /// <see cref="PlayerSave.Shops"/>, <see cref="PlayerSave.Cosmetics"/>,
    /// <see cref="PlayerSave.AvatarAppearance"/>, <see cref="OwnedBeast.Appearance"/>): <see cref="AddEconomy"/>.</item>
    /// </list>
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>A fresh list of every step (callers may append to it).</summary>
        public static List<ISaveMigration> All()
        {
            return new List<ISaveMigration> { new AddGear(), new AddCampaign(), new AddEconomy() };
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

        /// <summary>
        /// Schema 2 to 3: a v2 save has no campaign and no level-cap bank. The upgrade reads it into
        /// the current type (the campaign and every <c>BankedXp</c> take their empty defaults), fills
        /// in anything missing, unlocks the starting region and writes it back. Beasts keep their
        /// levels: one above the starting cap stays there and only banks XP until the cap passes it.
        /// </summary>
        public sealed class AddCampaign : ISaveMigration
        {
            public int FromVersion
            {
                get { return 2; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.Campaign.Unlock(CampaignProgress.StartingRegionId);
                save.SchemaVersion = 3;
                return serializer.ToJson(save);
            }
        }

        /// <summary>
        /// Schema 3 to 4: a v3 save has no economy. The upgrade reads it into the current type (no
        /// gold, no consumables, no Trader visits, no cosmetic unlocks — defaults and starter looks
        /// are free anyway — and every appearance at its defaults), fills in anything missing and
        /// writes it back. Nothing else moves.
        /// </summary>
        public sealed class AddEconomy : ISaveMigration
        {
            public int FromVersion
            {
                get { return 3; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.SchemaVersion = 4;
                return serializer.ToJson(save);
            }
        }
    }
}
