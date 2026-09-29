using System;
using System.Collections.Generic;
using BeastCraft.Common;
using BeastCraft.Economy;
using BeastCraft.Grove;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Expeditions
{
    /// <summary>
    /// The Board's rules (send, wait, collect): deterministic, non-throwing, refuse-and-no-op on bad
    /// input. No combat power anywhere: nothing here touches stats, damage or campaign difficulty.
    /// <para>
    /// <strong>Producer decision:</strong> an expedition is a timer on the destination, never a lock
    /// on a beast — <see cref="Send"/> does not check or change anything about the beasts named beyond
    /// that they exist; they stay fully available for battle and idle XP the whole time away. Waiting
    /// is checked through <see cref="OfflineClock"/> (the same anti-tamper reconciliation idle rewards
    /// and the Grove's gifts use). The outcome roll uses the same seeded-roll-plus-pity idiom as the
    /// Grove's gifts (<see cref="GroveRules.RollGift"/>).
    /// </para>
    /// </summary>
    public static class ExpeditionRules
    {
        private const double MsPerHour = 3600000.0;

        private const int SeedStream = 0x45585044;

        /// <summary>Whether <paramref name="destination"/> is unlocked: from the start, or its habitat is unlocked.</summary>
        public static bool IsDestinationUnlocked(PlayerSave save, DestinationData destination)
        {
            if (destination == null)
            {
                return false;
            }

            if (destination.UnlockSource == "start")
            {
                return true;
            }

            return destination.UnlockSource == "habitat" && save?.Grove?.HabitatsUnlocked != null && save.Grove.HabitatsUnlocked.Contains(destination.UnlockId);
        }

        /// <summary>
        /// Sends up to <paramref name="destination"/>'s party size of <paramref name="beastIds"/> (unknown
        /// or repeated ids are ignored) to <paramref name="destinationId"/>: refuses when the destination
        /// is unknown, locked, already has an expedition away, or the surviving party is empty or over
        /// its party size. The named beasts are not locked — see the class remarks.
        /// </summary>
        public static ExpeditionActionResult Send(PlayerSave save, ExpeditionLibrary library, string destinationId, IEnumerable<string> beastIds, DateTime nowUtc,
                                                   TimeSpan nowMonotonic)
        {
            if (save == null || library == null)
            {
                return ExpeditionActionResult.Refused("No save or Board content.");
            }

            save.EnsureInitialized();
            DestinationData destination = library.Destination(destinationId);
            if (destination == null || !IsDestinationUnlocked(save, destination))
            {
                return ExpeditionActionResult.Refused("That destination is not unlocked.");
            }

            if (save.Expeditions.FindActive(destinationId) != null)
            {
                return ExpeditionActionResult.Refused("An expedition is already away there.");
            }

            List<string> party = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in beastIds ?? new string[0])
            {
                if (!string.IsNullOrEmpty(id) && seen.Add(id) && save.FindBeast(id) != null)
                {
                    party.Add(id);
                }
            }

            if (party.Count == 0 || party.Count > destination.PartySize)
            {
                return ExpeditionActionResult.Refused("Send 1 to " + destination.PartySize + " beasts.");
            }

            int sendIndex = save.Expeditions.SendCount;
            save.Expeditions.SendCount = sendIndex + 1;
            ActiveExpedition active = new ActiveExpedition
            {
                DestinationId = destinationId,
                BeastIds = party,
                StartUtcTicks = OfflineClock.UtcTicks(nowUtc),
                StartMonotonicMs = OfflineClock.MonotonicMs(nowMonotonic),
                Seed = SeedFrom(save, destinationId, sendIndex)
            };
            save.Expeditions.Active.Add(active);
            return ExpeditionActionResult.Succeeded();
        }

        /// <summary>Hours left before <paramref name="active"/> is back (0 = back already).</summary>
        public static double TimeRemainingHours(ExpeditionLibrary library, ActiveExpedition active, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            DestinationData destination = active == null || library == null ? null : library.Destination(active.DestinationId);
            if (destination == null)
            {
                return 0.0;
            }

            long elapsedMs = OfflineClock.ElapsedMs(active.StartUtcTicks, active.StartMonotonicMs, OfflineClock.UtcTicks(nowUtc), OfflineClock.MonotonicMs(nowMonotonic), out bool _);
            double totalMs = destination.DurationHours * MsPerHour;
            return Math.Max(0.0, (totalMs - elapsedMs) / MsPerHour);
        }

        /// <summary>Whether <paramref name="active"/> has finished its timer.</summary>
        public static bool IsReturned(ExpeditionLibrary library, ActiveExpedition active, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            return active != null && TimeRemainingHours(library, active, nowUtc, nowMonotonic) <= 0.0;
        }

        /// <summary>
        /// Collects the expedition away at <paramref name="destinationId"/>: refuses when none is away
        /// there or it has not returned yet; otherwise rolls its outcome (seed plus pity) and applies
        /// it (a story, a Grove item trinket, or — once authored — a cosmetic look).
        /// </summary>
        public static ExpeditionCollectResult Collect(PlayerSave save, ExpeditionLibrary library, string destinationId, DateTime nowUtc, TimeSpan nowMonotonic,
                                                       CosmeticLibrary cosmetics = null)
        {
            if (save == null || library == null)
            {
                return ExpeditionCollectResult.Refused("No save or Board content.");
            }

            ActiveExpedition active = save.Expeditions.FindActive(destinationId);
            if (active == null)
            {
                return ExpeditionCollectResult.Refused("No expedition is away there.");
            }

            if (!IsReturned(library, active, nowUtc, nowMonotonic))
            {
                return ExpeditionCollectResult.Refused("That expedition is not back yet.");
            }

            ExpeditionOutcomeTableData table = library.OutcomeTable(destinationId);
            if (table == null || table.Entries == null || table.Entries.Length == 0)
            {
                return ExpeditionCollectResult.Refused("That destination has no outcomes (content error).");
            }

            ExpeditionPityCounter pity = save.Expeditions.PityFor(destinationId);
            ExpeditionOutcomeEntryData chosen = RollOutcome(table, active.Seed, pity);
            save.Expeditions.Active.Remove(active);

            ExpeditionCollectResult result = ExpeditionCollectResult.Succeeded(chosen.Kind, chosen.Id);
            switch (chosen.Kind)
            {
                case "story":
                    result.NewStory = AddOnce(save.Expeditions.StoriesUnlocked, chosen.Id);
                    break;
                case "trinket":
                    save.Grove.EnsureInitialized();
                    save.Grove.Items.Add(chosen.Id, 1);
                    break;
                case "look":
                    if (cosmetics != null)
                    {
                        CosmeticRules.UnlockOrRefund(save, cosmetics, chosen.Id, out int _);
                    }

                    break;
            }

            return result;
        }

        private static ExpeditionOutcomeEntryData RollOutcome(ExpeditionOutcomeTableData table, int seed, ExpeditionPityCounter pity)
        {
            int rollSeed = LootRoller.DeriveSeed(seed, 0);
            Random rng = new Random(rollSeed);
            bool forceNonCommon = pity.Misses >= table.PityAt - 1;

            List<ExpeditionOutcomeEntryData> pool = new List<ExpeditionOutcomeEntryData>();
            if (forceNonCommon)
            {
                foreach (ExpeditionOutcomeEntryData entry in table.Entries)
                {
                    if (entry != null && !entry.IsCommon)
                    {
                        pool.Add(entry);
                    }
                }
            }

            if (pool.Count == 0)
            {
                foreach (ExpeditionOutcomeEntryData entry in table.Entries)
                {
                    if (entry != null)
                    {
                        pool.Add(entry);
                    }
                }
            }

            int total = 0;
            foreach (ExpeditionOutcomeEntryData entry in pool)
            {
                total += Math.Max(0, entry.Weight);
            }

            int roll = total <= 0 ? 0 : rng.Next(total);
            int cursor = 0;
            ExpeditionOutcomeEntryData chosen = pool[pool.Count - 1];
            foreach (ExpeditionOutcomeEntryData entry in pool)
            {
                cursor += Math.Max(0, entry.Weight);
                if (roll < cursor)
                {
                    chosen = entry;
                    break;
                }
            }

            pity.Misses = chosen.IsCommon ? pity.Misses + 1 : 0;
            return chosen;
        }

        /// <summary>
        /// One Send's outcome-roll seed: <paramref name="destinationId"/> mixed with
        /// <paramref name="sendIndex"/> (<see cref="ExpeditionProgress.SendCount"/> at the time of this
        /// send) and the save's own stable seed (<c>PlayerSave.Idle.IdleSeed</c>) — the same
        /// "never the wall clock" idiom <see cref="GroveRules.RollGift"/> uses for a beast's
        /// <c>GiftSeed</c>. Deliberately takes no <see cref="DateTime"/>: the wall clock is
        /// player-controlled, so it must never be able to decide (or re-roll, by resending at a
        /// different clock reading) an outcome.
        /// </summary>
        private static int SeedFrom(PlayerSave save, string destinationId, int sendIndex)
        {
            int hash = unchecked(save.Idle.IdleSeed * 31);
            foreach (char c in destinationId ?? string.Empty)
            {
                hash = unchecked((hash * 31) + c);
            }

            int seed = LootRoller.DeriveSeed(LootRoller.DeriveSeed(hash, sendIndex), SeedStream);
            return seed == 0 ? 1 : seed;
        }

        private static bool AddOnce(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }
    }

    /// <summary>What a non-collect Board action (<see cref="ExpeditionRules.Send"/>) did.</summary>
    public sealed class ExpeditionActionResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        internal static ExpeditionActionResult Succeeded()
        {
            return new ExpeditionActionResult();
        }

        internal static ExpeditionActionResult Refused(string error)
        {
            return new ExpeditionActionResult { Success = false, Error = error };
        }
    }

    /// <summary>What <see cref="ExpeditionRules.Collect"/> rolled.</summary>
    public sealed class ExpeditionCollectResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        /// <summary>"story", "trinket" or "look".</summary>
        public string Kind { get; private set; }

        public string Id { get; private set; }

        /// <summary>Whether a "story" outcome was newly unlocked (false if already owned, or another kind).</summary>
        public bool NewStory { get; internal set; }

        internal static ExpeditionCollectResult Succeeded(string kind, string id)
        {
            return new ExpeditionCollectResult { Kind = kind, Id = id };
        }

        internal static ExpeditionCollectResult Refused(string error)
        {
            return new ExpeditionCollectResult { Success = false, Error = error };
        }
    }
}
