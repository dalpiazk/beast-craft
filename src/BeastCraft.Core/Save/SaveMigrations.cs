using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Progression;

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
    /// <item>4 to 5: the idle reward clock (<see cref="PlayerSave.Idle"/>): <see cref="AddIdle"/>.</item>
    /// <item>5 to 6: the expedition's difficulty (<see cref="MapRun.Difficulty"/>): <see cref="AddRunDifficulty"/>.</item>
    /// <item>6 to 7: the onboarding state (<see cref="PlayerSave.Tutorial"/>): <see cref="AddTutorial"/>.</item>
    /// <item>7 to 8: the discovery layer (<see cref="PlayerSave.Discovery"/>, each region's fog, points of
    /// interest found and discovery seed): <see cref="AddDiscovery"/>.</item>
    /// <item>8 to 9: the compendium's achievements and titles (<see cref="PlayerSave.Achievements"/>)
    /// and look tokens (<see cref="PlayerSave.LookTokens"/>): <see cref="AddCompendium"/>.</item>
    /// </list>
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>A fresh list of every step (callers may append to it).</summary>
        public static List<ISaveMigration> All()
        {
            return new List<ISaveMigration>
            {
                new AddGear(), new AddCampaign(), new AddEconomy(), new AddIdle(), new AddRunDifficulty(), new AddTutorial(), new AddDiscovery(), new AddCompendium()
            };
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

        /// <summary>
        /// Schema 4 to 5: a v4 save has no idle clock. The upgrade reads it into the current type (the
        /// clock takes its default: not started, no seed, no claims — the first claim after the upgrade
        /// starts it and pays nothing, since no offline time can be proven), fills in anything missing
        /// and writes it back. Nothing else moves.
        /// </summary>
        public sealed class AddIdle : ISaveMigration
        {
            public int FromVersion
            {
                get { return 4; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.SchemaVersion = 5;
                return serializer.ToJson(save);
            }
        }

        /// <summary>
        /// Schema 5 to 6: a v5 expedition has no difficulty. The upgrade reads it into the current
        /// type and sets the expedition in progress (if any) to <see cref="RunDifficulty.Normal"/> —
        /// the only difficulty that existed — fills in anything missing and writes it back. Nothing
        /// else moves.
        /// </summary>
        public sealed class AddRunDifficulty : ISaveMigration
        {
            public int FromVersion
            {
                get { return 5; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.Campaign.ActiveRun.Difficulty = RunDifficulty.Normal;
                save.SchemaVersion = 6;
                return serializer.ToJson(save);
            }
        }

        /// <summary>
        /// Schema 6 to 7: a v6 save has no onboarding state. The upgrade reads it into the current type
        /// (no hints seen). A save that owns any beast was made before Hearthglen existed: it counts
        /// Hearthglen as cleared — never offered, no pick pending — and keeps (or gets) the first
        /// campaign region unlocked, so nothing re-gates a returning player and no beast is touched.
        /// A save with no beast at all and no expedition in progress (only tools and tests ever wrote
        /// one) starts Hearthglen like a new game: Hearthglen unlocked instead of the first campaign
        /// region, its New Game pick pending.
        /// </summary>
        public sealed class AddTutorial : ISaveMigration
        {
            public int FromVersion
            {
                get { return 6; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                if (save.Beasts.Count > 0)
                {
                    save.Tutorial.HearthglenCleared = true;
                    save.Campaign.Unlock(CampaignProgress.StartingRegionId);
                }
                else if (!save.Campaign.HasActiveRun)
                {
                    save.Campaign.Lock(CampaignProgress.StartingRegionId);
                    save.Campaign.Unlock(CampaignProgress.TutorialRegionId);
                }

                save.SchemaVersion = 7;
                return serializer.ToJson(save);
            }
        }

        /// <summary>
        /// Schema 7 to 8: a v7 save has no discovery layer. The upgrade reads it into the current type
        /// (no Kinship site claimed, no Grove unlock, no lore, nothing found) and gives every unlocked
        /// campaign region its discovery seed: the expedition in progress's map seed for its region,
        /// else one derived from the region id (<see cref="MigratedDiscoverySeed"/>), so the upgrade is
        /// deterministic. Ground already explored is not fogged again: every stage cleared before fog
        /// existed (and a cleared boss's last stage) is revealed whole, and the expedition in
        /// progress has its trailhead and every location it cleared revealed as if played with fog.
        /// No beast is touched and no Kinship site is claimed: a site never offers a beast the player
        /// owns, so a save that owns six beasts meets its four missing species at the next sites, and a
        /// site with nothing left to offer is a lore and cache stop instead. Tutorial regions have no fog.
        /// </summary>
        public sealed class AddDiscovery : ISaveMigration
        {
            /// <summary>The <c>LootRoller.DeriveSeed</c> stream a migrated region's seed is drawn on.</summary>
            public const int MigrationStream = 0x4D494752;

            /// <summary>The shipped regions' map shape (<c>regions.json</c> <c>MapRules</c>: 11 layers, 4 lanes), frozen for the migration.</summary>
            public const int DefaultMapRows = 10;

            /// <summary>See <see cref="DefaultMapRows"/>.</summary>
            public const int DefaultLanes = 4;

            public int FromVersion
            {
                get { return 7; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                MapRun run = save.Campaign.ActiveRun;
                foreach (RegionProgress progress in save.Campaign.Regions)
                {
                    if (string.IsNullOrEmpty(progress.RegionId) || progress.RegionId == CampaignProgress.TutorialRegionId)
                    {
                        continue;
                    }

                    bool active = save.Campaign.HasActiveRun && run.RegionId == progress.RegionId;
                    if (progress.DiscoverySeed == 0)
                    {
                        progress.DiscoverySeed = active && run.Seed != 0 ? Math.Max(1, LootRoller.DeriveSeed(run.Seed, CampaignRules.DiscoverySeedStream))
                                                     : MigratedDiscoverySeed(progress.RegionId);
                    }

                    // Stages explored before fog existed stay explored (read off the expedition's map, else
                    // the shipped regions' shape; a region with other rules is corrected on its next expedition).
                    FogGrid grid = MigrationGrid(run, active);
                    int explored = progress.BossCleared ? progress.StagesCleared + 1 : progress.StagesCleared;
                    for (int stage = 0; stage < explored; stage++)
                    {
                        MapFog.RevealAll(progress.FogOf(stage), grid);
                    }

                    if (active)
                    {
                        MapFog.StartStage(progress, grid, run.Stage);
                        foreach (int nodeId in run.Cleared)
                        {
                            MapFog.OnCleared(progress, grid, run.Stage, run.Find(nodeId), run.Nodes);
                        }
                    }
                }

                save.SchemaVersion = 8;
                return serializer.ToJson(save);
            }

            /// <summary>A migrated region's discovery seed: derived from its id alone (stable across runs and machines), never 0.</summary>
            public static int MigratedDiscoverySeed(string regionId)
            {
                int hash = 17;
                foreach (char c in regionId ?? string.Empty)
                {
                    hash = unchecked((hash * 31) + c);
                }

                return Math.Max(1, LootRoller.DeriveSeed(hash, MigrationStream));
            }

            private static FogGrid MigrationGrid(MapRun run, bool active)
            {
                int rows = 0;
                int lanes = 0;
                if (active)
                {
                    foreach (MapNode node in run.Nodes)
                    {
                        rows = Math.Max(rows, node.Layer);
                        lanes = Math.Max(lanes, node.Lane + 1);
                    }
                }

                return rows > 0 ? new FogGrid(rows, Math.Max(lanes, 1)) : new FogGrid(DefaultMapRows, DefaultLanes);
            }
        }

        /// <summary>
        /// Schema 8 to 9: a v8 save has no compendium. The upgrade reads it into the current type (no
        /// achievement earned, no title owned or equipped, no look tokens), fills in anything missing
        /// and writes it back. Nothing else moves: an achievement is only ever earned by
        /// <c>Progression.AchievementRules.Evaluate</c>, never by the migration itself.
        /// </summary>
        public sealed class AddCompendium : ISaveMigration
        {
            public int FromVersion
            {
                get { return 8; }
            }

            public string Upgrade(string json, ISaveJsonSerializer serializer)
            {
                PlayerSave save = serializer.FromJson<PlayerSave>(json);
                save.EnsureInitialized();
                save.SchemaVersion = 9;
                return serializer.ToJson(save);
            }
        }
    }
}
