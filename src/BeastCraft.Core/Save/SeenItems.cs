using System;
using System.Collections.Generic;
using BeastCraft.Economy;

namespace BeastCraft.Save
{
    /// <summary>
    /// What the player has already looked at, for the screens' "new" dots (<c>PlayerSave.Seen</c>,
    /// schema 12): owned gear by instance id, and unlocked looks by their <c>"categoryId/optionId"</c>
    /// key (<see cref="CosmeticCollection.Key"/>). An owned thing not listed here is new; it is listed
    /// once its row has been on screen (<see cref="SeenRules"/>). Presentation state only: nothing in a
    /// battle or a reward reads it.
    /// </summary>
    [Serializable]
    public class SeenItems
    {
        /// <summary>Gear instance ids (beast and avatar gear) the player has seen, each at most once.</summary>
        public List<string> Gear = new List<string>();

        /// <summary>Unlocked look keys the player has seen, each at most once.</summary>
        public List<string> Looks = new List<string>();
    }

    /// <summary>The "new" markers' rules over <see cref="SeenItems"/>.</summary>
    public static class SeenRules
    {
        /// <summary>Whether gear <paramref name="instanceId"/> is owned (beast or avatar gear) and not seen yet.</summary>
        public static bool IsNewGear(PlayerSave save, string instanceId)
        {
            if (save?.Gear == null || string.IsNullOrEmpty(instanceId))
            {
                return false;
            }

            bool owned = save.Gear.FindBeastGear(instanceId) != null || save.Gear.FindAvatarGear(instanceId) != null;
            return owned && !Contains(save.Seen?.Gear, instanceId);
        }

        /// <summary>Whether look <paramref name="key"/> is unlocked (earned, not a free default) and not seen yet.</summary>
        public static bool IsNewLook(PlayerSave save, string key)
        {
            return save?.Cosmetics != null && save.Cosmetics.Has(key) && !Contains(save.Seen?.Looks, key);
        }

        /// <summary>Records gear <paramref name="instanceId"/> as seen; true when it was not before.</summary>
        public static bool MarkGearSeen(PlayerSave save, string instanceId)
        {
            return Add(Ensure(save)?.Gear, instanceId);
        }

        /// <summary>Records look <paramref name="key"/> as seen; true when it was not before.</summary>
        public static bool MarkLookSeen(PlayerSave save, string key)
        {
            return Add(Ensure(save)?.Looks, key);
        }

        /// <summary>
        /// Records everything the save owns now as seen: every gear instance and every unlocked look
        /// (the 11-to-12 migration, so an updated save shows nothing as new). Returns how many were added.
        /// </summary>
        public static int MarkAllOwnedSeen(PlayerSave save)
        {
            SeenItems seen = Ensure(save);
            if (seen == null)
            {
                return 0;
            }

            int added = 0;
            foreach (OwnedGear gear in save.Gear?.BeastGear ?? new List<OwnedGear>())
            {
                added += gear != null && Add(seen.Gear, gear.InstanceId) ? 1 : 0;
            }

            foreach (OwnedGear gear in save.Gear?.AvatarGear ?? new List<OwnedGear>())
            {
                added += gear != null && Add(seen.Gear, gear.InstanceId) ? 1 : 0;
            }

            foreach (string key in save.Cosmetics?.Unlocked ?? new List<string>())
            {
                added += Add(seen.Looks, key) ? 1 : 0;
            }

            return added;
        }

        private static SeenItems Ensure(PlayerSave save)
        {
            if (save == null)
            {
                return null;
            }

            if (save.Seen == null)
            {
                save.Seen = new SeenItems();
            }

            if (save.Seen.Gear == null)
            {
                save.Seen.Gear = new List<string>();
            }

            if (save.Seen.Looks == null)
            {
                save.Seen.Looks = new List<string>();
            }

            return save.Seen;
        }

        private static bool Contains(List<string> list, string value)
        {
            return list != null && list.Contains(value);
        }

        private static bool Add(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value) || list.Contains(value))
            {
                return false;
            }

            list.Add(value);
            return true;
        }
    }
}
