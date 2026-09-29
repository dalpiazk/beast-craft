using System;
using System.Collections.Generic;

namespace BeastCraft.Grove
{
    /// <summary>
    /// The Grove's account-wide state of one save (<c>PlayerSave.Grove</c>, schema 10): which habitats
    /// and decor pieces are unlocked (<see cref="HabitatsUnlocked"/>, <see cref="UnlockedDecorIds"/> —
    /// a habitat unlocked by a shrine is read live off <c>DiscoveryProgress.GroveUnlockIds</c> by
    /// <see cref="GroveRules"/>, then recorded here once, the same "claim it once, hold it forever"
    /// shape as <c>DiscoveryProgress.ClaimedKinshipIds</c>), where decor is placed
    /// (<see cref="PlacedDecor"/>), every beast's affinity (<see cref="Affinity"/>), the Grove's own
    /// small lore codex (<see cref="LoreIds"/>, separate from the discovery layer's — an affinity or
    /// gift lore reward is Grove flavour, not a discovery find) and the generic Grove item inventory
    /// (<see cref="Items"/>): what the Garden grows and crafts and the Board's expeditions find, one
    /// counted pool keyed by id. The seam a later PR reads for peaceful clears and colour evolutions
    /// (see <c>docs/design/grove.md</c>, "Producer additions"). Plain serializable data with public
    /// fields; the rules are <see cref="GroveRules"/>'.
    /// </summary>
    [Serializable]
    public class GroveProgress
    {
        /// <summary>Habitat ids unlocked, each once, in the order unlocked.</summary>
        public List<string> HabitatsUnlocked = new List<string>();

        /// <summary>Decor ids owned (whether placed or not), each once, in the order unlocked.</summary>
        public List<string> UnlockedDecorIds = new List<string>();

        /// <summary>Decor placed in a habitat. A decor id may be placed at most once across every habitat.</summary>
        public List<PlacedDecorEntry> PlacedDecor = new List<PlacedDecorEntry>();

        /// <summary>Every beast that has ever been fed or played with, by <c>OwnedBeast.BeastId</c>.</summary>
        public List<BeastAffinityState> Affinity = new List<BeastAffinityState>();

        /// <summary>The Grove's own lore entries found (an affinity or gift reward), each once, in order.</summary>
        public List<string> LoreIds = new List<string>();

        /// <summary>Grown, crafted and found Grove items, one counted pool. See the class remarks.</summary>
        public GroveItemInventory Items = new GroveItemInventory();

        /// <summary>Beast <paramref name="beastId"/>'s affinity state, or null (never fed or played with).</summary>
        public BeastAffinityState FindAffinity(string beastId)
        {
            if (string.IsNullOrEmpty(beastId))
            {
                return null;
            }

            foreach (BeastAffinityState state in Affinity)
            {
                if (state != null && state.BeastId == beastId)
                {
                    return state;
                }
            }

            return null;
        }

        /// <summary>Beast <paramref name="beastId"/>'s affinity state, creating an empty one (Tier 0) if it has none yet.</summary>
        public BeastAffinityState AffinityOf(string beastId)
        {
            BeastAffinityState state = FindAffinity(beastId);
            if (state != null)
            {
                return state;
            }

            state = new BeastAffinityState { BeastId = beastId };
            Affinity.Add(state);
            return state;
        }

        /// <summary>Whether decor <paramref name="decorId"/> is placed somewhere.</summary>
        public bool IsPlaced(string decorId)
        {
            if (string.IsNullOrEmpty(decorId))
            {
                return false;
            }

            foreach (PlacedDecorEntry entry in PlacedDecor)
            {
                if (entry != null && entry.DecorId == decorId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How many decor pieces are placed in <paramref name="habitatId"/>.</summary>
        public int PlacedCountIn(string habitatId)
        {
            int count = 0;
            foreach (PlacedDecorEntry entry in PlacedDecor)
            {
                if (entry != null && entry.HabitatId == habitatId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Replaces null lists and sub-objects with empty ones and drops empty and repeated ids. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            repaired += Repair(ref HabitatsUnlocked);
            repaired += Repair(ref UnlockedDecorIds);
            repaired += Repair(ref LoreIds);

            if (PlacedDecor == null)
            {
                PlacedDecor = new List<PlacedDecorEntry>();
                repaired++;
            }

            repaired += PlacedDecor.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.HabitatId) || string.IsNullOrEmpty(entry.DecorId));
            HashSet<string> placed = new HashSet<string>(StringComparer.Ordinal);
            repaired += PlacedDecor.RemoveAll(entry => !placed.Add(entry.DecorId));
            foreach (PlacedDecorEntry entry in PlacedDecor)
            {
                // X and Y are fractions of the habitat canvas (GroveRules.MoveDecor).
                float x = GroveRules.ClampUnit(entry.X);
                float y = GroveRules.ClampUnit(entry.Y);
                if (x != entry.X || y != entry.Y)
                {
                    entry.X = x;
                    entry.Y = y;
                    repaired++;
                }
            }

            if (Affinity == null)
            {
                Affinity = new List<BeastAffinityState>();
                repaired++;
            }

            repaired += Affinity.RemoveAll(state => state == null || string.IsNullOrEmpty(state.BeastId));
            HashSet<string> beasts = new HashSet<string>(StringComparer.Ordinal);
            repaired += Affinity.RemoveAll(state => !beasts.Add(state.BeastId));
            foreach (BeastAffinityState state in Affinity)
            {
                repaired += state.EnsureInitialized();
            }

            if (Items == null)
            {
                Items = new GroveItemInventory();
                repaired++;
            }

            repaired += Items.EnsureInitialized();
            return repaired;
        }

        private static int Repair(ref List<string> list)
        {
            int repaired = 0;
            if (list == null)
            {
                list = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += list.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }

    /// <summary>One decor piece placed in a habitat (<see cref="GroveProgress.PlacedDecor"/>).</summary>
    [Serializable]
    public class PlacedDecorEntry
    {
        public string HabitatId;

        public string DecorId;

        public float X;

        public float Y;

        public int Rotation;
    }

    /// <summary>
    /// One beast's affinity: its XP and tier (<see cref="GroveRules.Feed"/>/<see cref="GroveRules.Play"/>,
    /// no decay), the daily cooldown anchors for each action (<see cref="Common.OfflineClock"/>-checked,
    /// same anti-tamper stance as idle), the gift clock (its own seed and stream index, the same
    /// seeded-roll-plus-pity idiom as <c>IdleRewardCalculator</c>) and the gifts rolled but not yet
    /// collected (<see cref="PendingGifts"/>, capped at <see cref="GroveRules.MaxPendingGifts"/>).
    /// </summary>
    [Serializable]
    public class BeastAffinityState
    {
        public string BeastId;

        /// <summary>Affinity XP, never decays.</summary>
        public int Xp;

        /// <summary>0 (never reached tier 1) to 5.</summary>
        public int Tier;

        public long LastFeedUtcTicks;

        public long LastFeedMonotonicMs;

        public long LastPlayUtcTicks;

        public long LastPlayMonotonicMs;

        /// <summary>The gift clock's anchor: the last time gifts were rolled forward. 0 = never (no gift owed yet).</summary>
        public long LastGiftUtcTicks;

        public long LastGiftMonotonicMs;

        /// <summary>The gift roll stream's seed, assigned on this beast's first gift (0 = not yet assigned).</summary>
        public int GiftSeed;

        /// <summary>How many gifts this beast has ever been rolled (the stream index the next roll derives on).</summary>
        public int GiftIndex;

        /// <summary>Consecutive gifts landing on a common entry (<see cref="GroveRules.RollGift"/>'s pity counter).</summary>
        public int GiftPityMisses;

        /// <summary>Gifts rolled but not yet collected, oldest first, at most <see cref="GroveRules.MaxPendingGifts"/>.</summary>
        public List<GiftInstance> PendingGifts = new List<GiftInstance>();

        /// <summary>Replaces null lists and drops null entries. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (PendingGifts == null)
            {
                PendingGifts = new List<GiftInstance>();
                repaired++;
            }

            repaired += PendingGifts.RemoveAll(gift => gift == null || string.IsNullOrEmpty(gift.ItemId));
            return repaired;
        }
    }

    /// <summary>One rolled, uncollected gift (<see cref="BeastAffinityState.PendingGifts"/>).</summary>
    [Serializable]
    public class GiftInstance
    {
        /// <summary>"decor", "lore" or "cosmetic" (<c>GiftEntryData.ItemKind</c>).</summary>
        public string ItemKind;

        public string ItemId;
    }
}
