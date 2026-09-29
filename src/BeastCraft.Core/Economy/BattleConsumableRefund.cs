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
    /// gets the items back (<see cref="RefundPending"/>), once: the refund takes them off the record, and a
    /// refund whose save fails is undone (<see cref="Restore"/>).
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
        /// Hands back the recorded consumables, one unit each. <paramref name="maxStackOf"/> gives an item's
        /// stack cap, or null for an item the content no longer has (dropped from the record: there is
        /// nothing to give back). An item whose stack is full stays in the record, still owed, and is
        /// handed back by a later refund once there is room; the stack never passes its cap (a shop
        /// purchase refuses at the cap the same way). Returns the ids handed back, in order (empty when
        /// nothing was pending). The caller saves straight after; if that save fails it puts the save back
        /// with <see cref="Restore"/>, so the refund is paid exactly once.
        /// </summary>
        public static List<string> RefundPending(PlayerSave save, Func<string, int?> maxStackOf)
        {
            List<string> refunded = new List<string>();
            if (save == null || save.PendingBattleConsumables == null || save.PendingBattleConsumables.Count == 0)
            {
                return refunded;
            }

            List<string> owed = new List<string>();
            foreach (string id in save.PendingBattleConsumables)
            {
                int? maxStack = string.IsNullOrEmpty(id) || maxStackOf == null ? null : maxStackOf(id);
                if (!maxStack.HasValue)
                {
                    continue;
                }

                if (ConsumableInventory.TryAdd(save, id, 1, maxStack.Value))
                {
                    refunded.Add(id);
                }
                else
                {
                    owed.Add(id);
                }
            }

            save.PendingBattleConsumables = owed;
            return refunded;
        }

        /// <summary>The refund record before <see cref="RefundPending"/> ran, for <see cref="Restore"/> when the save that should follow it fails.</summary>
        public sealed class Snapshot
        {
            internal List<string> Pending = new List<string>();
        }

        /// <summary>Takes a <see cref="Snapshot"/> of <paramref name="save"/>'s refund record.</summary>
        public static Snapshot Take(PlayerSave save)
        {
            Snapshot snapshot = new Snapshot();
            snapshot.Pending.AddRange(save?.PendingBattleConsumables ?? new List<string>());
            return snapshot;
        }

        /// <summary>
        /// Undoes a refund in memory: one unit of each of <paramref name="refunded"/> is taken back off its stack
        /// (anything else that changed the stacks since stays), and the record is put back as
        /// <paramref name="snapshot"/> had it. The save on disk still holds the record, so the refund is paid
        /// once, by the next load that can save it.
        /// </summary>
        public static void Restore(PlayerSave save, Snapshot snapshot, IEnumerable<string> refunded)
        {
            if (save == null || snapshot == null)
            {
                return;
            }

            foreach (string id in refunded ?? new string[0])
            {
                ConsumableStack stack = save.Consumables?.Find(s => s != null && s.ConsumableId == id);
                if (stack == null)
                {
                    continue;
                }

                stack.Quantity--;
                if (stack.Quantity <= 0)
                {
                    save.Consumables.Remove(stack);
                }
            }

            save.PendingBattleConsumables = new List<string>(snapshot.Pending);
        }
    }
}
