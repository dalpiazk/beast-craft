using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Economy;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>The gear the player team wears in PvE (<c>--gear</c>).</summary>
    public enum GearProfile
    {
        /// <summary>No gear (the default; the balance guard and the committed report are gearless).</summary>
        None = 0,

        /// <summary>Three commons of the encounter level's band.</summary>
        Common = 1,

        /// <summary>Three rares of the band.</summary>
        Rare = 2,

        /// <summary>Three epics of the band (rares below band 41, where there are no epics).</summary>
        Epic = 3,

        /// <summary>
        /// What a player normally wears at the level (the lead's "difficulty assumes typical gear"):
        /// weapon / armor / accessory commons in band 1-20; a rare weapon in 21-40; rare weapon and
        /// armor in 41-60; an epic weapon and rare armor and accessory in 61-80; epic weapon and
        /// armor and a rare accessory in 81-100.
        /// </summary>
        Typical = 4
    }

    /// <summary>
    /// The PvE player team's gear by <see cref="GearProfile"/>: for each beast three pieces of the
    /// encounter level's band (<see cref="GearLibrary.BandFloor"/>) matched to it — a fang if its
    /// base Attack is at least its SpecialAttack else a focus stone; barding if its Defense is at
    /// least its SpecialDefense else a warding mantle; a keen collar if its base crit is at least 8
    /// else a wind charm — at the profile's rarity per slot. The pieces are the library's own
    /// <see cref="GearSO"/>s (<see cref="GearLibrary.BeastGearAssets"/>), shared read-only.
    /// </summary>
    public sealed class GearKits
    {
        /// <summary>The gear library's path relative to the repo root.</summary>
        public const string RepoRelativePath = "BeastCraft/" + GearLibraryData.ProjectRelativePath;

        private readonly Dictionary<string, List<GearSO>> _cache = new Dictionary<string, List<GearSO>>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public GearKits(GearLibrary library)
        {
            Library = library;
        }

        public GearLibrary Library { get; }

        /// <summary>Loads and validates <c>gear-library.json</c> (budget against <paramref name="species"/>); null with <paramref name="errors"/> filled on failure.</summary>
        public static GearKits Load(string path, IReadOnlyList<CreatureSpeciesSO> species, List<string> errors)
        {
            string resolved = RosterLoader.ResolveFile(path, RepoRelativePath);
            if (resolved == null || !File.Exists(resolved))
            {
                errors.Add("Could not find " + RepoRelativePath + "; run from inside the repo or pass --gear-library <path>.");
                return null;
            }

            GearLibraryData data;
            try
            {
                data = JsonSerializer.Deserialize<GearLibraryData>(File.ReadAllText(resolved), new JsonSerializerOptions { IncludeFields = true });
            }
            catch (Exception exception)
            {
                errors.Add("Could not read " + resolved + ": " + exception.Message);
                return null;
            }

            List<string> problems = GearLibraryValidator.Validate(data, species);
            if (problems.Count > 0)
            {
                errors.AddRange(problems);
                return null;
            }

            return new GearKits(GearLibrary.Build(data));
        }

        /// <summary>The rarity per slot (weapon, armor, accessory) <paramref name="profile"/> wears at <paramref name="level"/>; null for none.</summary>
        public static int[] Rarities(GearProfile profile, int level)
        {
            switch (profile)
            {
                case GearProfile.Common:
                    return new[] { 0, 0, 0 };
                case GearProfile.Rare:
                    return new[] { 1, 1, 1 };
                case GearProfile.Epic:
                    return new[] { 2, 2, 2 };
                case GearProfile.Typical:
                    if (level <= 20)
                    {
                        return new[] { 0, 0, 0 };
                    }

                    if (level <= 40)
                    {
                        return new[] { 1, 0, 0 };
                    }

                    if (level <= 60)
                    {
                        return new[] { 1, 1, 0 };
                    }

                    return level <= 80 ? new[] { 2, 1, 1 } : new[] { 2, 2, 1 };
                default:
                    return null;
            }
        }

        /// <summary>The pieces <paramref name="species"/> wears at <paramref name="level"/> under <paramref name="profile"/> (null for none). Cached; thread-safe.</summary>
        public List<GearSO> For(CreatureSpeciesSO species, int level, GearProfile profile)
        {
            int[] rarities = Rarities(profile, level);
            if (rarities == null || species == null)
            {
                return null;
            }

            int floor = Library.BandFloor(level);
            string key = species.SpeciesId + "/" + floor + "/" + (int)profile + "/" + rarities[0] + rarities[1] + rarities[2];
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out List<GearSO> cached))
                {
                    return cached;
                }

                StatBlock stats = species.BaseStats;
                int band = 0;
                for (int i = 0; i < Library.BandFloors.Count; i++)
                {
                    band = Library.BandFloors[i] == floor ? i : band;
                }

                string tier = "t" + (band + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                List<GearSO> gear = new List<GearSO>
                {
                    Piece(stats.Attack >= stats.SpecialAttack ? "fang" : "focus", tier, rarities[0]),
                    Piece(stats.Defense >= stats.SpecialDefense ? "barding" : "mantle", tier, rarities[1]),
                    Piece(stats.CritChance >= 8 ? "keen_collar" : "wind_charm", tier, rarities[2])
                };
                gear.RemoveAll(g => g == null);
                _cache.Add(key, gear);
                return gear;
            }
        }

        private GearSO Piece(string piece, string tier, int rarity)
        {
            GearSO asset = rarity >= 2 ? Library.BeastGearAsset(piece + "_" + tier + "_epic") : null;
            if (asset == null && rarity >= 1)
            {
                asset = Library.BeastGearAsset(piece + "_" + tier + "_rare");
            }

            return asset ?? Library.BeastGearAsset(piece + "_" + tier);
        }
    }
}
