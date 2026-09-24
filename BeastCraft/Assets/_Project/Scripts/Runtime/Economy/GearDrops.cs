using System;
using System.Collections.Generic;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Economy
{
    /// <summary>
    /// Gear from battles and bosses. <see cref="Roll"/> is a clear's drops (<c>drop-tables.json</c>
    /// <c>GearDrops</c>); <see cref="RollGuaranteed"/> a pass's or lair's first-clear reward;
    /// <see cref="Grant"/> puts a piece in the save as a new instance. Deterministic: the only
    /// randomness is the rng handed in (the session seeds it
    /// <c>LootRoller.DeriveSeed(battleSeed, PostBattleAward.GearStream)</c>, so gear never moves the
    /// material or gold rolls). Non-throwing.
    /// </summary>
    public static class GearDrops
    {
        /// <summary>
        /// Every gear drop roll of a clear of <paramref name="shape"/> at <paramref name="level"/>, in
        /// authored order: <c>rng.Next(1000) &lt; ChancePerMille</c> (always drawn), and on a hit one
        /// piece (a second draw) from <paramref name="library"/>'s <c>drop</c> pool of that rarity in
        /// the level's band (<see cref="GearLibrary.Pool"/>, beast and avatar gear alike). A hit on
        /// an empty pool grants nothing. Returns the pieces (empty, never null).
        /// </summary>
        public static List<GearItem> Roll(DropTable table, GearLibrary library, string shape, int level, Random rng)
        {
            List<GearItem> dropped = new List<GearItem>();
            if (table == null || library == null || rng == null)
            {
                return dropped;
            }

            foreach (GearDropChance chance in table.GearDropsFor(shape))
            {
                if (rng.Next(1000) >= chance.ChancePerMille)
                {
                    continue;
                }

                List<GearItem> pool = library.Pool(GearLibrary.SourceDrop, chance.Rarity, level);
                if (pool.Count > 0)
                {
                    dropped.Add(pool[rng.Next(pool.Count)]);
                }
            }

            return dropped;
        }

        /// <summary>
        /// A guaranteed piece of <paramref name="rarity"/> from the <c>boss</c> pool of
        /// <paramref name="level"/>'s band; when that band has none of that rarity (no epics below
        /// level 41), the next rarity down. One draw. Null only for an empty library.
        /// </summary>
        public static GearItem RollGuaranteed(GearLibrary library, int rarity, int level, Random rng)
        {
            if (library == null || rng == null)
            {
                return null;
            }

            for (int r = rarity; r >= 0; r--)
            {
                List<GearItem> pool = library.Pool(GearLibrary.SourceBoss, r, level);
                if (pool.Count > 0)
                {
                    return pool[rng.Next(pool.Count)];
                }
            }

            return null;
        }

        /// <summary>Adds <paramref name="item"/> to <paramref name="save"/>'s gear inventory as a new, unequipped instance. Returns its instance id, or null.</summary>
        public static string Grant(PlayerSave save, GearItem item)
        {
            if (save == null || item == null)
            {
                return null;
            }

            if (save.Gear == null)
            {
                save.Gear = new GearInventory();
            }

            return item.IsAvatarGear ? save.Gear.AddAvatarGear(item.GearId) : save.Gear.AddBeastGear(item.GearId);
        }
    }
}
