using System;
using BeastCraft.Avatar;
using BeastCraft.Battle;

namespace BeastCraft.Save
{
    /// <summary>What <see cref="GearRules"/> did with an equip request.</summary>
    public enum GearEquipResult
    {
        /// <summary>The instance is now in the slot (or already was).</summary>
        Equipped,

        /// <summary>No such beast in the save.</summary>
        UnknownOwner,

        /// <summary>No such instance in the right inventory list.</summary>
        UnknownInstance,

        /// <summary>The instance's gear id is not in the catalog.</summary>
        UnknownGear,

        /// <summary>The gear belongs in a different slot.</summary>
        SlotMismatch,

        /// <summary>The beast is below the gear's <c>MinimumLevel</c>.</summary>
        LevelTooLow,

        /// <summary>The instance is already worn by another owner (unequip it there first).</summary>
        EquippedElsewhere
    }

    /// <summary>
    /// The gear equip rules over a <see cref="PlayerSave"/>:
    /// <list type="bullet">
    /// <item>a slot holds at most one instance, and the gear's own slot must be the slot equipped into;</item>
    /// <item>an instance is worn by at most one owner, in one slot (beast gear by beasts, avatar gear by the avatar);</item>
    /// <item>beast gear needs the beast at or above the gear's <c>MinimumLevel</c> when equipped
    /// (a beast is never de-levelled, so it stays valid; <c>StatCalculator</c> ignores under-level
    /// gear anyway).</item>
    /// </list>
    /// Equipping into an occupied slot replaces what was there (which returns to the inventory
    /// unequipped). Gear definitions are looked up through an <see cref="ISaveGearCatalog"/>.
    /// Slots are stored by enum value (<c>(int)GearSlot</c>, <c>(int)AvatarGearSlot</c>) as instance
    /// ids; empty is null or "". Non-throwing.
    /// </summary>
    public static class GearRules
    {
        /// <summary>How many beast gear slots there are (one per <see cref="GearSlot"/> value).</summary>
        public static readonly int BeastSlotCount = Enum.GetValues(typeof(GearSlot)).Length;

        /// <summary>How many avatar gear slots there are (one per <see cref="AvatarGearSlot"/> value).</summary>
        public static readonly int AvatarSlotCount = Enum.GetValues(typeof(AvatarGearSlot)).Length;

        /// <summary>Puts beast gear <paramref name="instanceId"/> into <paramref name="slot"/> of beast <paramref name="beastId"/>. See the class rules.</summary>
        public static GearEquipResult EquipBeastGear(PlayerSave save, string beastId, GearSlot slot, string instanceId, ISaveGearCatalog catalog)
        {
            OwnedBeast beast = save == null ? null : save.FindBeast(beastId);
            if (beast == null)
            {
                return GearEquipResult.UnknownOwner;
            }

            OwnedGear gear = save.Gear == null ? null : save.Gear.FindBeastGear(instanceId);
            if (gear == null)
            {
                return GearEquipResult.UnknownInstance;
            }

            if (catalog == null || !catalog.TryGetBeastGear(gear.GearId, out GearSlot gearSlot, out int minimumLevel))
            {
                return GearEquipResult.UnknownGear;
            }

            if (gearSlot != slot)
            {
                return GearEquipResult.SlotMismatch;
            }

            int level = beast.Progress == null ? 1 : beast.Progress.Level;
            if (level < minimumLevel)
            {
                return GearEquipResult.LevelTooLow;
            }

            string holder = FindBeastGearHolder(save, instanceId);
            if (holder != null && !string.Equals(holder, beast.BeastId, StringComparison.Ordinal))
            {
                return GearEquipResult.EquippedElsewhere;
            }

            beast.EquippedGear = Resized(beast.EquippedGear, BeastSlotCount);
            beast.EquippedGear[(int)slot] = instanceId;
            return GearEquipResult.Equipped;
        }

        /// <summary>Empties <paramref name="slot"/> of beast <paramref name="beastId"/>. False when there was nothing to remove.</summary>
        public static bool UnequipBeastGear(PlayerSave save, string beastId, GearSlot slot)
        {
            OwnedBeast beast = save == null ? null : save.FindBeast(beastId);
            if (beast == null || GetSlot(beast.EquippedGear, (int)slot) == null)
            {
                return false;
            }

            beast.EquippedGear[(int)slot] = null;
            return true;
        }

        /// <summary>Puts avatar gear <paramref name="instanceId"/> into the avatar's <paramref name="slot"/>. See the class rules.</summary>
        public static GearEquipResult EquipAvatarGear(PlayerSave save, AvatarGearSlot slot, string instanceId, ISaveGearCatalog catalog)
        {
            if (save == null)
            {
                return GearEquipResult.UnknownOwner;
            }

            OwnedGear gear = save.Gear == null ? null : save.Gear.FindAvatarGear(instanceId);
            if (gear == null)
            {
                return GearEquipResult.UnknownInstance;
            }

            if (catalog == null || !catalog.TryGetAvatarGear(gear.GearId, out AvatarGearSlot gearSlot))
            {
                return GearEquipResult.UnknownGear;
            }

            if (gearSlot != slot)
            {
                return GearEquipResult.SlotMismatch;
            }

            save.AvatarEquippedGear = Resized(save.AvatarEquippedGear, AvatarSlotCount);
            save.AvatarEquippedGear[(int)slot] = instanceId;
            return GearEquipResult.Equipped;
        }

        /// <summary>Empties the avatar's <paramref name="slot"/>. False when there was nothing to remove.</summary>
        public static bool UnequipAvatarGear(PlayerSave save, AvatarGearSlot slot)
        {
            if (save == null || GetSlot(save.AvatarEquippedGear, (int)slot) == null)
            {
                return false;
            }

            save.AvatarEquippedGear[(int)slot] = null;
            return true;
        }

        /// <summary>The id of the beast wearing beast gear <paramref name="instanceId"/>, or null when nobody is.</summary>
        public static string FindBeastGearHolder(PlayerSave save, string instanceId)
        {
            if (save == null || save.Beasts == null || string.IsNullOrEmpty(instanceId))
            {
                return null;
            }

            foreach (OwnedBeast beast in save.Beasts)
            {
                if (beast != null && beast.EquippedGear != null && Array.IndexOf(beast.EquippedGear, instanceId) >= 0)
                {
                    return beast.BeastId;
                }
            }

            return null;
        }

        /// <summary>How many beast slots, across every beast in the save, hold <paramref name="instanceId"/> (1 in a valid save when it is worn).</summary>
        public static int CountBeastGearWorn(PlayerSave save, string instanceId)
        {
            int count = 0;

            if (save == null || save.Beasts == null || string.IsNullOrEmpty(instanceId))
            {
                return count;
            }

            foreach (OwnedBeast beast in save.Beasts)
            {
                for (int slot = 0; beast != null && beast.EquippedGear != null && slot < beast.EquippedGear.Length; slot++)
                {
                    if (string.Equals(beast.EquippedGear[slot], instanceId, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>The instance id in <paramref name="slot"/> of <paramref name="equipped"/>, or null when empty or out of range.</summary>
        public static string GetSlot(string[] equipped, int slot)
        {
            if (equipped == null || slot < 0 || slot >= equipped.Length)
            {
                return null;
            }

            return string.IsNullOrEmpty(equipped[slot]) ? null : equipped[slot];
        }

        /// <summary><paramref name="slots"/> at exactly <paramref name="count"/> entries, keeping what fits (the same array when it already fits).</summary>
        internal static string[] Resized(string[] slots, int count)
        {
            if (slots != null && slots.Length == count)
            {
                return slots;
            }

            string[] resized = new string[count];
            if (slots != null)
            {
                Array.Copy(slots, resized, Math.Min(slots.Length, count));
            }

            return resized;
        }
    }
}
