using System;
using System.Collections.Generic;

namespace BeastCraft.Expeditions
{
    /// <summary>
    /// The Board's account-wide state of one save (<c>PlayerSave.Expeditions</c>, schema 10): every
    /// expedition away (<see cref="Active"/> — a timer on the destination, never a lock on a beast:
    /// producer decision, an expedition's beasts stay available for battle the whole time), the
    /// stories unlocked and each destination's outcome pity counter. Plain serializable data with
    /// public fields; the rules are <see cref="ExpeditionRules"/>'.
    /// <para>
    /// Distinct from the region campaign's own "expedition" (<c>CampaignProgress.ActiveRun</c>, a
    /// <c>MapRun</c>): that word names one stage's traversal of a region map there. This is the
    /// Grove's Board feature — sending beasts to a destination for a timed outcome roll. The two never
    /// interact; see <c>docs/design/grove.md</c>, "Naming: two different 'expeditions'".
    /// </para>
    /// </summary>
    [Serializable]
    public class ExpeditionProgress
    {
        public List<ActiveExpedition> Active = new List<ActiveExpedition>();

        public List<string> StoriesUnlocked = new List<string>();

        public List<ExpeditionPityCounter> Pity = new List<ExpeditionPityCounter>();

        /// <summary>
        /// How many expeditions have ever been sent from this save (never decreases): mixed into every
        /// <see cref="ActiveExpedition.Seed"/> (<c>ExpeditionRules.SeedFrom</c>) so no two sends ever
        /// draw the same outcome roll, even to the same destination at the same wall-clock instant —
        /// the seed must never be player-controlled, and the player controls the clock.
        /// </summary>
        public int SendCount;

        /// <summary>The expedition away at <paramref name="destinationId"/>, or null (none away there).</summary>
        public ActiveExpedition FindActive(string destinationId)
        {
            foreach (ActiveExpedition active in Active)
            {
                if (active != null && active.DestinationId == destinationId)
                {
                    return active;
                }
            }

            return null;
        }

        /// <summary><paramref name="destinationId"/>'s pity counter, creating one at 0 misses if it has none yet.</summary>
        public ExpeditionPityCounter PityFor(string destinationId)
        {
            foreach (ExpeditionPityCounter counter in Pity)
            {
                if (counter != null && counter.DestinationId == destinationId)
                {
                    return counter;
                }
            }

            ExpeditionPityCounter created = new ExpeditionPityCounter { DestinationId = destinationId };
            Pity.Add(created);
            return created;
        }

        /// <summary>Replaces null lists with empty ones and drops invalid entries. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (Active == null)
            {
                Active = new List<ActiveExpedition>();
                repaired++;
            }

            repaired += Active.RemoveAll(active => active == null || string.IsNullOrEmpty(active.DestinationId));
            foreach (ActiveExpedition active in Active)
            {
                if (active.BeastIds == null)
                {
                    active.BeastIds = new List<string>();
                    repaired++;
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += Active.RemoveAll(active => !seen.Add(active.DestinationId));

            if (StoriesUnlocked == null)
            {
                StoriesUnlocked = new List<string>();
                repaired++;
            }

            HashSet<string> stories = new HashSet<string>(StringComparer.Ordinal);
            repaired += StoriesUnlocked.RemoveAll(id => string.IsNullOrEmpty(id) || !stories.Add(id));

            if (Pity == null)
            {
                Pity = new List<ExpeditionPityCounter>();
                repaired++;
            }

            repaired += Pity.RemoveAll(counter => counter == null || string.IsNullOrEmpty(counter.DestinationId));
            HashSet<string> pityKeys = new HashSet<string>(StringComparer.Ordinal);
            repaired += Pity.RemoveAll(counter => !pityKeys.Add(counter.DestinationId));

            if (SendCount < 0)
            {
                SendCount = 0;
                repaired++;
            }

            return repaired;
        }
    }

    /// <summary>One expedition away: a timer on the destination, never a lock on <see cref="BeastIds"/> (see the class remarks).</summary>
    [Serializable]
    public class ActiveExpedition
    {
        public string DestinationId;

        public List<string> BeastIds = new List<string>();

        public long StartUtcTicks;

        public long StartMonotonicMs;

        /// <summary>The outcome roll's seed, assigned when sent.</summary>
        public int Seed;
    }

    /// <summary>One destination's outcome pity counter (consecutive common outcomes).</summary>
    [Serializable]
    public class ExpeditionPityCounter
    {
        public string DestinationId;

        public int Misses;
    }
}
