using System;
using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Save
{
    /// <summary>
    /// Everything persisted about one player, as one versioned aggregate: the beast collection
    /// (each beast's species, level, XP and skill book), the avatar's level and skill books, and the
    /// material inventory with its first-clear and pity state.
    /// <para>
    /// <strong>JsonUtility-compatible by construction.</strong> Every type reachable from here is
    /// <c>[Serializable]</c> with public fields, and every map is a list (<c>JsonUtility</c> drops
    /// dictionaries and properties). Everything is named by stable string ids — species, skill,
    /// passive and material ids, all under the never-rename rule — never by asset reference.
    /// </para>
    /// <para>
    /// <strong>Versioned.</strong> <see cref="SchemaVersion"/> is stamped with
    /// <see cref="CurrentSchemaVersion"/> on every write by <see cref="SaveSerializer"/>, which
    /// upgrades older saves through its migration steps before reading them. Bump
    /// <see cref="CurrentSchemaVersion"/> and add a step to <see cref="SaveMigrations.All"/> whenever a
    /// change would make an old save read wrongly; purely additive fields with a sensible default
    /// need no bump.
    /// </para>
    /// </summary>
    [Serializable]
    public class PlayerSave
    {
        /// <summary>The schema this code writes, and the newest it reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The schema the data is in. 0 (or missing) is never valid.</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>The beast collection, in the order obtained.</summary>
        public List<OwnedBeast> Beasts = new List<OwnedBeast>();

        /// <summary>The avatar's own level and XP.</summary>
        public AvatarProgress Avatar = new AvatarProgress();

        /// <summary>The avatar's active and passive skill books.</summary>
        public AvatarSkillBook AvatarSkills = new AvatarSkillBook();

        /// <summary>Held materials, cleared cells and pity counters.</summary>
        public MaterialInventory Materials = new MaterialInventory();

        /// <summary>A brand-new player: no beasts, avatar level 1, nothing learned or held.</summary>
        public static PlayerSave CreateNew()
        {
            return new PlayerSave();
        }

        /// <summary>The beast with <paramref name="beastId"/>, or <c>null</c>.</summary>
        public OwnedBeast FindBeast(string beastId)
        {
            if (string.IsNullOrEmpty(beastId) || Beasts == null)
            {
                return null;
            }

            for (int i = 0; i < Beasts.Count; i++)
            {
                if (Beasts[i] != null && string.Equals(Beasts[i].BeastId, beastId, StringComparison.Ordinal))
                {
                    return Beasts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces any missing (null) collection or sub-object with its empty default and drops null
        /// beast entries, so code reading a loaded save never meets a null. <c>JsonUtility</c> never
        /// produces nulls here; other serializers, or a hand-edited file, can. Ids and numbers are
        /// left alone — reporting bad ones is <see cref="SaveValidator"/>'s job. Returns how many
        /// things were repaired.
        /// </summary>
        public int EnsureInitialized()
        {
            int repaired = 0;

            if (Beasts == null)
            {
                Beasts = new List<OwnedBeast>();
                repaired++;
            }

            repaired += Beasts.RemoveAll(beast => beast == null);

            foreach (OwnedBeast beast in Beasts)
            {
                if (beast.Progress == null)
                {
                    beast.Progress = new BeastProgress();
                    repaired++;
                }

                if (beast.Skills == null)
                {
                    beast.Skills = new BeastSkillBook();
                    repaired++;
                }

                repaired += RepairBook(beast.Skills);
            }

            if (Avatar == null)
            {
                Avatar = new AvatarProgress();
                repaired++;
            }

            if (AvatarSkills == null)
            {
                AvatarSkills = new AvatarSkillBook();
                repaired++;
            }

            if (AvatarSkills.Actives == null)
            {
                AvatarSkills.Actives = new AvatarActiveSkillBook();
                repaired++;
            }

            if (AvatarSkills.Passives == null)
            {
                AvatarSkills.Passives = new AvatarPassiveSkillBook();
                repaired++;
            }

            repaired += RepairBook(AvatarSkills.Actives);
            repaired += RepairBook(AvatarSkills.Passives);

            if (Materials == null)
            {
                Materials = new MaterialInventory();
                repaired++;
            }

            if (Materials.Materials == null)
            {
                Materials.Materials = new List<MaterialStack>();
                repaired++;
            }

            if (Materials.ClearedCells == null)
            {
                Materials.ClearedCells = new List<ClearedCell>();
                repaired++;
            }

            if (Materials.Pity == null)
            {
                Materials.Pity = new List<PityCounter>();
                repaired++;
            }

            repaired += Materials.Materials.RemoveAll(stack => stack == null);
            repaired += Materials.ClearedCells.RemoveAll(cell => cell == null);
            repaired += Materials.Pity.RemoveAll(counter => counter == null);
            return repaired;
        }

        private static int RepairBook(SkillBook book)
        {
            int repaired = 0;

            if (book.Known == null)
            {
                book.Known = new List<SkillProgress>();
                repaired++;
            }

            repaired += book.Known.RemoveAll(progress => progress == null);

            if (book.Equipped == null)
            {
                book.Equipped = new string[book.SlotCount];
                repaired++;
            }

            return repaired;
        }
    }
}
