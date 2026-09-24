using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Progression;

namespace BeastCraft.Save
{
    /// <summary>
    /// Checks a loaded <see cref="PlayerSave"/> for references the game cannot resolve and values
    /// out of range, and reports each as a <see cref="SaveIssue"/>. It never throws and never
    /// changes the save: an id that has disappeared from the content (a renamed or removed skill, a
    /// save from a newer content build) is the game's to handle, not a reason to refuse the file.
    /// <para>
    /// With a null catalog only the structural checks run (ids present and unique, ranges, equipped
    /// entries learned); with one, every species, skill, passive and material id is also looked up.
    /// </para>
    /// </summary>
    public static class SaveValidator
    {
        /// <summary>Every issue in <paramref name="save"/>, in save order. Empty when it is clean (or null).</summary>
        public static List<SaveIssue> Validate(PlayerSave save, ISaveContentCatalog catalog)
        {
            return Validate(save, catalog, null);
        }

        /// <summary>
        /// <see cref="Validate(PlayerSave, ISaveContentCatalog)"/> plus the gear checks against
        /// <paramref name="gearCatalog"/>: unknown gear ids, worn slots that do not match the gear's
        /// slot, and beast gear worn below its minimum level. Without a gear catalog only the
        /// structural gear checks run (instance ids present and unique, worn instances owned, no
        /// instance worn twice).
        /// </summary>
        public static List<SaveIssue> Validate(PlayerSave save, ISaveContentCatalog catalog, ISaveGearCatalog gearCatalog)
        {
            List<SaveIssue> issues = ValidateCore(save, catalog);

            if (save != null)
            {
                ValidateGear(save, gearCatalog, issues);
            }

            return issues;
        }

        private static List<SaveIssue> ValidateCore(PlayerSave save, ISaveContentCatalog catalog)
        {
            List<SaveIssue> issues = new List<SaveIssue>();

            if (save == null)
            {
                return issues;
            }

            ValidateBeasts(save.Beasts, catalog, issues);

            if (save.Avatar != null)
            {
                CheckRange(issues, "Avatar.Level", save.Avatar.Level, 1, AvatarProgression.MaxLevel);
                CheckRange(issues, "Avatar.Xp", save.Avatar.Xp, 0, int.MaxValue);
            }

            if (save.AvatarSkills != null)
            {
                Func<string, bool> skillKnown = catalog == null ? null : new Func<string, bool>(catalog.IsKnownSkill);
                Func<string, bool> passiveKnown = catalog == null ? null : new Func<string, bool>(catalog.IsKnownPassive);
                ValidateBook(save.AvatarSkills.Actives, "AvatarSkills.Actives", skillKnown, SaveIssueKind.UnknownSkill, issues);
                ValidateBook(save.AvatarSkills.Passives, "AvatarSkills.Passives", passiveKnown, SaveIssueKind.UnknownPassive, issues);
            }

            ValidateMaterials(save.Materials, catalog, issues);
            return issues;
        }

        private static void ValidateBeasts(List<OwnedBeast> beasts, ISaveContentCatalog catalog, List<SaveIssue> issues)
        {
            if (beasts == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            Func<string, bool> skillKnown = catalog == null ? null : new Func<string, bool>(catalog.IsKnownSkill);

            for (int i = 0; i < beasts.Count; i++)
            {
                OwnedBeast beast = beasts[i];
                string path = "Beasts[" + i + "]";

                if (beast == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(beast.BeastId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.MissingBeastId, path + ".BeastId", null, "beast has no id"));
                }
                else if (!seen.Add(beast.BeastId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.DuplicateBeastId, path + ".BeastId", beast.BeastId, "beast id '" + beast.BeastId + "' is used more than once"));
                }

                if (beast.Progress != null)
                {
                    string speciesId = beast.Progress.SpeciesId;

                    if (string.IsNullOrEmpty(speciesId))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.UnknownSpecies, path + ".Progress.SpeciesId", speciesId, "beast has no species"));
                    }
                    else if (catalog != null && !catalog.IsKnownSpecies(speciesId))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.UnknownSpecies, path + ".Progress.SpeciesId", speciesId, "unknown species '" + speciesId + "'"));
                    }

                    CheckRange(issues, path + ".Progress.Level", beast.Progress.Level, 1, int.MaxValue);
                    CheckRange(issues, path + ".Progress.Xp", beast.Progress.Xp, 0, int.MaxValue);
                }

                ValidateBook(beast.Skills, path + ".Skills", skillKnown, SaveIssueKind.UnknownSkill, issues);
            }
        }

        private static void ValidateBook(SkillBook book, string path, Func<string, bool> isKnownId, SaveIssueKind unknownKind, List<SaveIssue> issues)
        {
            if (book == null)
            {
                return;
            }

            HashSet<string> learned = new HashSet<string>(StringComparer.Ordinal);

            if (book.Known != null)
            {
                for (int i = 0; i < book.Known.Count; i++)
                {
                    SkillProgress progress = book.Known[i];
                    string entryPath = path + ".Known[" + i + "]";

                    if (progress == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(progress.SkillId) || (isKnownId != null && !isKnownId(progress.SkillId)))
                    {
                        issues.Add(new SaveIssue(unknownKind, entryPath + ".SkillId", progress.SkillId, "unknown id '" + progress.SkillId + "'"));
                    }
                    else if (!learned.Add(progress.SkillId))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.DuplicateSkill, entryPath + ".SkillId", progress.SkillId, "'" + progress.SkillId + "' is learned more than once"));
                    }

                    CheckRange(issues, entryPath + ".Level", progress.Level, 1, int.MaxValue);
                    CheckRange(issues, entryPath + ".Xp", progress.Xp, 0, int.MaxValue);
                    CheckRange(issues, entryPath + ".Tier", progress.Tier, 0, int.MaxValue);
                }
            }

            if (book.Equipped == null)
            {
                return;
            }

            for (int slot = 0; slot < book.Equipped.Length; slot++)
            {
                string id = book.Equipped[slot];

                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                string slotPath = path + ".Equipped[" + slot + "]";

                if (slot >= book.SlotCount)
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidValue, slotPath, id, "slot past the book's " + book.SlotCount + " slots (dropped on the next equip change)"));
                }
                else if (!learned.Contains(id))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.EquippedNotLearned, slotPath, id, "'" + id + "' is equipped but not learned"));
                }
            }
        }

        private static void ValidateMaterials(MaterialInventory inventory, ISaveContentCatalog catalog, List<SaveIssue> issues)
        {
            if (inventory == null)
            {
                return;
            }

            if (inventory.Materials != null)
            {
                for (int i = 0; i < inventory.Materials.Count; i++)
                {
                    MaterialStack stack = inventory.Materials[i];
                    string path = "Materials.Materials[" + i + "]";

                    if (stack == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(stack.MaterialId) || (catalog != null && !catalog.IsKnownMaterial(stack.MaterialId)))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.UnknownMaterial, path + ".MaterialId", stack.MaterialId, "unknown material '" + stack.MaterialId + "'"));
                    }

                    CheckRange(issues, path + ".Quantity", stack.Quantity, 1, int.MaxValue);
                }
            }

            if (inventory.Pity != null)
            {
                for (int i = 0; i < inventory.Pity.Count; i++)
                {
                    if (inventory.Pity[i] != null)
                    {
                        CheckRange(issues, "Materials.Pity[" + i + "].Misses", inventory.Pity[i].Misses, 0, int.MaxValue);
                    }
                }
            }
        }

        private static void ValidateGear(PlayerSave save, ISaveGearCatalog gearCatalog, List<SaveIssue> issues)
        {
            GearInventory inventory = save.Gear;
            HashSet<string> instanceIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, OwnedGear> beastGear = new Dictionary<string, OwnedGear>(StringComparer.Ordinal);
            Dictionary<string, OwnedGear> avatarGear = new Dictionary<string, OwnedGear>(StringComparer.Ordinal);

            if (inventory != null)
            {
                IndexGear(inventory.BeastGear, "Gear.BeastGear", true, gearCatalog, instanceIds, beastGear, issues);
                IndexGear(inventory.AvatarGear, "Gear.AvatarGear", false, gearCatalog, instanceIds, avatarGear, issues);
            }

            HashSet<string> worn = new HashSet<string>(StringComparer.Ordinal);

            if (save.Beasts != null)
            {
                for (int b = 0; b < save.Beasts.Count; b++)
                {
                    OwnedBeast beast = save.Beasts[b];
                    if (beast == null || beast.EquippedGear == null)
                    {
                        continue;
                    }

                    int level = beast.Progress == null ? 1 : beast.Progress.Level;
                    for (int slot = 0; slot < beast.EquippedGear.Length; slot++)
                    {
                        string instanceId = beast.EquippedGear[slot];
                        string path = "Beasts[" + b + "].EquippedGear[" + slot + "]";

                        if (!CheckWorn(instanceId, path, slot, GearRules.BeastSlotCount, beastGear, worn, issues, out OwnedGear gear))
                        {
                            continue;
                        }

                        if (gearCatalog != null && gearCatalog.TryGetBeastGear(gear.GearId, out GearSlot gearSlot, out int minimumLevel))
                        {
                            if ((int)gearSlot != slot)
                            {
                                issues.Add(new SaveIssue(SaveIssueKind.GearSlotMismatch, path, instanceId, "'" + gear.GearId + "' belongs in the " + gearSlot + " slot"));
                            }

                            if (level < minimumLevel)
                            {
                                issues.Add(new SaveIssue(SaveIssueKind.GearLevelTooLow, path, instanceId, "'" + gear.GearId + "' needs level " + minimumLevel + "; the beast is " + level));
                            }
                        }
                    }
                }
            }

            if (save.AvatarEquippedGear != null)
            {
                for (int slot = 0; slot < save.AvatarEquippedGear.Length; slot++)
                {
                    string instanceId = save.AvatarEquippedGear[slot];
                    string path = "AvatarEquippedGear[" + slot + "]";

                    if (CheckWorn(instanceId, path, slot, GearRules.AvatarSlotCount, avatarGear, worn, issues, out OwnedGear gear) &&
                        gearCatalog != null && gearCatalog.TryGetAvatarGear(gear.GearId, out BeastCraft.Avatar.AvatarGearSlot gearSlot) && (int)gearSlot != slot)
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.GearSlotMismatch, path, instanceId, "'" + gear.GearId + "' belongs in the " + gearSlot + " slot"));
                    }
                }
            }
        }

        private static void IndexGear(List<OwnedGear> list, string path, bool beast, ISaveGearCatalog gearCatalog, HashSet<string> instanceIds,
                                      Dictionary<string, OwnedGear> index, List<SaveIssue> issues)
        {
            if (list == null)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                OwnedGear gear = list[i];
                string entryPath = path + "[" + i + "]";

                if (gear == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(gear.InstanceId) || !instanceIds.Add(gear.InstanceId))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.DuplicateGearInstance, entryPath + ".InstanceId", gear.InstanceId, "instance id is empty or used more than once"));
                }
                else
                {
                    index[gear.InstanceId] = gear;
                }

                bool known = !string.IsNullOrEmpty(gear.GearId) &&
                             (gearCatalog == null || (beast ? gearCatalog.TryGetBeastGear(gear.GearId, out GearSlot _, out int _) : gearCatalog.TryGetAvatarGear(gear.GearId, out BeastCraft.Avatar.AvatarGearSlot _)));
                if (!known)
                {
                    issues.Add(new SaveIssue(SaveIssueKind.UnknownGear, entryPath + ".GearId", gear.GearId, "unknown gear '" + gear.GearId + "'"));
                }
            }
        }

        /// <summary>The shared checks on one worn slot. True (with the owned instance) when it holds a wearable instance worth checking further.</summary>
        private static bool CheckWorn(string instanceId, string path, int slot, int slotCount, Dictionary<string, OwnedGear> owned, HashSet<string> worn,
                                      List<SaveIssue> issues, out OwnedGear gear)
        {
            gear = null;

            if (string.IsNullOrEmpty(instanceId))
            {
                return false;
            }

            if (slot >= slotCount)
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidValue, path, instanceId, "slot past the " + slotCount + " gear slots"));
                return false;
            }

            if (!owned.TryGetValue(instanceId, out gear))
            {
                issues.Add(new SaveIssue(SaveIssueKind.UnknownGearInstance, path, instanceId, "worn instance '" + instanceId + "' is not in the inventory"));
                return false;
            }

            if (!worn.Add(instanceId))
            {
                issues.Add(new SaveIssue(SaveIssueKind.DoubleEquippedGear, path, instanceId, "instance '" + instanceId + "' is worn more than once"));
                return false;
            }

            return true;
        }

        private static void CheckRange(List<SaveIssue> issues, string path, int value, int min, int max)
        {
            if (value < min || value > max)
            {
                string range = max == int.MaxValue ? "at least " + min : min + " to " + max;
                issues.Add(new SaveIssue(SaveIssueKind.InvalidValue, path, null, value + " is out of range (" + range + ")"));
            }
        }
    }
}
