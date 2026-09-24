using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeastCraft.Save
{
    /// <summary>
    /// One owned piece of gear: a unique <see cref="InstanceId"/> (two copies of the same gear are
    /// two instances, each equippable once) and the gear definition it is a copy of —
    /// <c>GearSO.GearId</c> for beast gear, <c>AvatarGearSO.AvatarGearId</c> for avatar gear,
    /// depending on which <see cref="GearInventory"/> list holds it.
    /// </summary>
    [Serializable]
    public class OwnedGear
    {
        /// <summary>Parameterless, for serializers.</summary>
        public OwnedGear()
        {
        }

        public OwnedGear(string instanceId, string gearId)
        {
            InstanceId = instanceId;
            GearId = gearId;
        }

        /// <summary>Unique across the whole <see cref="GearInventory"/> (both lists). What equip slots store.</summary>
        public string InstanceId;

        /// <summary>The gear definition's stable id. Follows its never-rename rule.</summary>
        public string GearId;
    }

    /// <summary>
    /// Every piece of gear the player owns, equipped or not: beast gear (<see cref="BeastGear"/>,
    /// definitions are <c>GearSO</c>s) and avatar gear (<see cref="AvatarGear"/>, <c>AvatarGearSO</c>s).
    /// Which owner wears what is stored on the owner (<see cref="OwnedBeast.EquippedGear"/>,
    /// <see cref="PlayerSave.AvatarEquippedGear"/>) by instance id; the rules are
    /// <see cref="GearRules"/>'s.
    /// <para>
    /// Plain serializable save data, lists not dictionaries. Non-throwing.
    /// </para>
    /// </summary>
    [Serializable]
    public class GearInventory
    {
        /// <summary>Owned beast gear, in the order obtained.</summary>
        public List<OwnedGear> BeastGear = new List<OwnedGear>();

        /// <summary>Owned avatar gear, in the order obtained.</summary>
        public List<OwnedGear> AvatarGear = new List<OwnedGear>();

        /// <summary>The number the next generated instance id uses (<c>"gear" + n</c>). Only ever grows.</summary>
        public int NextInstanceNumber = 1;

        /// <summary>Adds a new instance of beast gear <paramref name="gearId"/> and returns its generated instance id (null for an empty id).</summary>
        public string AddBeastGear(string gearId)
        {
            return Add(BeastGear ?? (BeastGear = new List<OwnedGear>()), gearId);
        }

        /// <summary>Adds a new instance of avatar gear <paramref name="gearId"/> and returns its generated instance id (null for an empty id).</summary>
        public string AddAvatarGear(string gearId)
        {
            return Add(AvatarGear ?? (AvatarGear = new List<OwnedGear>()), gearId);
        }

        /// <summary>The beast gear instance <paramref name="instanceId"/>, or null.</summary>
        public OwnedGear FindBeastGear(string instanceId)
        {
            return Find(BeastGear, instanceId);
        }

        /// <summary>The avatar gear instance <paramref name="instanceId"/>, or null.</summary>
        public OwnedGear FindAvatarGear(string instanceId)
        {
            return Find(AvatarGear, instanceId);
        }

        /// <summary>Whether any instance, of either kind, has <paramref name="instanceId"/>.</summary>
        public bool ContainsInstance(string instanceId)
        {
            return FindBeastGear(instanceId) != null || FindAvatarGear(instanceId) != null;
        }

        private string Add(List<OwnedGear> into, string gearId)
        {
            if (string.IsNullOrEmpty(gearId))
            {
                return null;
            }

            string id;
            do
            {
                id = "gear" + (NextInstanceNumber < 1 ? 1 : NextInstanceNumber).ToString(CultureInfo.InvariantCulture);
                NextInstanceNumber = (NextInstanceNumber < 1 ? 1 : NextInstanceNumber) + 1;
            }
            while (ContainsInstance(id));

            into.Add(new OwnedGear(id, gearId));
            return id;
        }

        private static OwnedGear Find(List<OwnedGear> list, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || list == null)
            {
                return null;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && string.Equals(list[i].InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }
    }
}
