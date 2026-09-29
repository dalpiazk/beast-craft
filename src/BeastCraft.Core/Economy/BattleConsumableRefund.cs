using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The mid-battle crash refund. A campaign battle charges its consumables as it begins
    /// (<c>Session.BattleSession.Begin</c>) and the game autosaves then, so a process that dies before
    /// the results are applied would lose them. The battle's host records what it spent
    /// (<see cref="Record"/>, before that autosave) and clears the record as the results are applied
    /// (<see cref="Clear"/>, before the results autosave); a save loaded with a record still in it
    /// gets the items back (<see cref="RefundPending"/>), once: the refund clears the record.
    /// Non-throwing.
    /// </summary>
    public static class BattleConsumableRefund
    {
        /// <summary>Notes <paramref name="spent"/> as the consumables of the battle now in progress (replacing any earlier record).</summary>
        public static void Record(PlayerSave save, IEnumerable<string> spent)
        {
            if (save == null)
            {
                return;
            }

            save.PendingBattleConsumables = new List<string>();
            foreach (string id in spent ?? new string[0])
            {
                if (!string.IsNullOrEmpty(id))
                {
                    save.PendingBattleConsumables.Add(id);
                }
            }
        }

        /// <summary>The battle resolved: nothing is owed back.</summary>
        public static void Clear(PlayerSave save)
        {
            if (save != null)
            {
                save.PendingBattleConsumables = new List<string>();
            }
        }

        /// <summary>
        /// Hands back every recorded consumable and clears the record. <paramref name="maxStackOf"/>
        /// gives an item's stack cap, or null for an item the content no longer has (skipped: there is
        /// nothing to give back). An item whose stack is already full is not refunded past the cap.
        /// Returns the ids handed back, in order (empty when nothing was pending).
        /// </summary>
        public static List<string> RefundPending(PlayerSave save, Func<string, int?> maxStackOf)
        {
            List<string> refunded = new List<string>();
            if (save == null || save.PendingBattleConsumables == null || save.PendingBattleConsumables.Count == 0)
            {
                return refunded;
            }

            foreach (string id in save.PendingBattleConsumables)
            {
                int? maxStack = maxStackOf == null ? null : maxStackOf(id);
                if (maxStack.HasValue && ConsumableInventory.TryAdd(save, id, 1, maxStack.Value))
                {
                    refunded.Add(id);
                }
            }

            Clear(save);
            return refunded;
        }
    }
}
