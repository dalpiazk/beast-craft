using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
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
    /// entries learned, the expedition in progress consistent); with one, every species, skill,
    /// passive, material, region and seal id is also looked up.
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
            return Validate(save, catalog, gearCatalog, null);
        }

        /// <summary>
        /// <see cref="Validate(PlayerSave, ISaveContentCatalog, ISaveGearCatalog)"/> plus the economy
        /// checks (schema 4): gold in range, consumable stacks (known ids with
        /// <paramref name="economyCatalog"/>, 1 to the max stack, one stack per id), frozen Trader
        /// visits, cosmetic unlocks (known keys, each once) and appearances (known categories of the
        /// right owner, known options, each free or unlocked). Without an economy catalog only the
        /// structural checks run.
        /// </summary>
        public static List<SaveIssue> Validate(PlayerSave save, ISaveContentCatalog catalog, ISaveGearCatalog gearCatalog, ISaveEconomyCatalog economyCatalog)
        {
            List<SaveIssue> issues = ValidateCore(save, catalog);

            if (save != null)
            {
                ValidateGear(save, gearCatalog, issues);
                SaveEconomyValidator.Validate(save, economyCatalog, issues);
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
            ValidateCampaign(save.Campaign, catalog, issues);
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
                    CheckRange(issues, path + ".Progress.BankedXp", beast.Progress.BankedXp, 0, int.MaxValue);
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

        private static void ValidateCampaign(CampaignProgress campaign, ISaveContentCatalog catalog, List<SaveIssue> issues)
        {
            if (campaign == null)
            {
                return;
            }

            if (campaign.Seals != null)
            {
                HashSet<string> seals = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < campaign.Seals.Count; i++)
                {
                    string sealId = campaign.Seals[i];
                    string path = "Campaign.Seals[" + i + "]";
                    if (string.IsNullOrEmpty(sealId) || (catalog != null && !catalog.IsKnownSeal(sealId)))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.UnknownSeal, path, sealId, "unknown seal '" + sealId + "'"));
                    }
                    else if (!seals.Add(sealId))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.DuplicateCampaignEntry, path, sealId, "seal '" + sealId + "' is owned more than once"));
                    }
                }
            }

            if (campaign.Regions != null)
            {
                HashSet<string> regions = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < campaign.Regions.Count; i++)
                {
                    RegionProgress region = campaign.Regions[i];
                    string path = "Campaign.Regions[" + i + "]";
                    if (region == null)
                    {
                        continue;
                    }

                    if (CheckRegionId(region.RegionId, path + ".RegionId", catalog, issues) && !regions.Add(region.RegionId))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.DuplicateCampaignEntry, path + ".RegionId", region.RegionId, "region '" + region.RegionId + "' is listed more than once"));
                    }

                    CheckRange(issues, path + ".StagesCleared", region.StagesCleared, 0, int.MaxValue);
                }
            }

            if (!string.IsNullOrEmpty(campaign.CurrentRegionId))
            {
                CheckRegionId(campaign.CurrentRegionId, "Campaign.CurrentRegionId", catalog, issues);
            }

            ValidateRun(campaign, catalog, issues);
        }

        /// <summary>Reports an empty or (with a catalog) unknown region id. True when the id is fine.</summary>
        private static bool CheckRegionId(string regionId, string path, ISaveContentCatalog catalog, List<SaveIssue> issues)
        {
            if (string.IsNullOrEmpty(regionId) || (catalog != null && !catalog.IsKnownRegion(regionId)))
            {
                issues.Add(new SaveIssue(SaveIssueKind.UnknownRegion, path, regionId, "unknown region '" + regionId + "'"));
                return false;
            }

            return true;
        }

        /// <summary>
        /// The expedition in progress: no nodes without a run; with one, a known and unlocked region,
        /// nodes whose ids are their indices, known types, levels 1-100, links only to nodes one layer
        /// up, a current node and a retried node on the map (or -1) and cleared nodes on the map, each once.
        /// </summary>
        private static void ValidateRun(CampaignProgress campaign, ISaveContentCatalog catalog, List<SaveIssue> issues)
        {
            MapRun run = campaign.ActiveRun;
            if (run == null)
            {
                return;
            }

            int nodeCount = run.Nodes == null ? 0 : run.Nodes.Count;
            if (string.IsNullOrEmpty(run.RegionId))
            {
                if (nodeCount > 0 || (run.Cleared != null && run.Cleared.Count > 0))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun", null, "no expedition (RegionId is empty) but nodes or cleared nodes are stored"));
                }

                return;
            }

            if (CheckRegionId(run.RegionId, "Campaign.ActiveRun.RegionId", catalog, issues) && !campaign.IsUnlocked(run.RegionId))
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun.RegionId", run.RegionId, "the expedition's region '" + run.RegionId + "' is not unlocked"));
            }

            CheckRange(issues, "Campaign.ActiveRun.Stage", run.Stage, 0, int.MaxValue);
            CheckRange(issues, "Campaign.ActiveRun.Attempts", run.Attempts, 0, int.MaxValue);
            CheckRange(issues, "Campaign.ActiveRun.NodeAttempts", run.NodeAttempts, 0, int.MaxValue);

            if (nodeCount == 0)
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun.Nodes", run.RegionId, "an expedition with no map nodes"));
                return;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                MapNode node = run.Nodes[i];
                string path = "Campaign.ActiveRun.Nodes[" + i + "]";
                if (node == null)
                {
                    continue;
                }

                if (node.NodeId != i)
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, path + ".NodeId", null, "node id " + node.NodeId + " is not its index " + i));
                }

                if (!Enum.IsDefined(typeof(MapNodeType), node.Type))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, path + ".Type", null, "unknown node type " + (int)node.Type));
                }

                CheckRange(issues, path + ".Level", node.Level, 1, BeastProgression.MaxLevel);
                CheckRange(issues, path + ".Layer", node.Layer, 0, int.MaxValue);

                if (!Enum.IsDefined(typeof(LocationKind), node.Kind))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, path + ".Kind", null, "unknown location kind " + (int)node.Kind));
                }

                if (!(node.X >= 0f && node.X <= 1f) || !(node.Y >= 0f && node.Y <= 1f))
                {
                    issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, path + ".X", null, "map position (" + node.X + ", " + node.Y + ") is outside 0-1"));
                }

                if (node.Next == null)
                {
                    continue;
                }

                for (int n = 0; n < node.Next.Length; n++)
                {
                    MapNode next = run.Find(node.Next[n]);
                    if (next == null || next.Layer != node.Layer + 1)
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, path + ".Next[" + n + "]", null, "link to " + node.Next[n] + " is not a node on the next layer"));
                    }
                }
            }

            if (run.CurrentNodeId != -1 && run.Find(run.CurrentNodeId) == null)
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun.CurrentNodeId", null, "current node " + run.CurrentNodeId + " is not on the map"));
            }

            if (run.NodeAttemptsNodeId != -1 && run.Find(run.NodeAttemptsNodeId) == null)
            {
                issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun.NodeAttemptsNodeId", null, "retried node " + run.NodeAttemptsNodeId + " is not on the map"));
            }

            if (run.Cleared != null)
            {
                HashSet<int> cleared = new HashSet<int>();
                for (int i = 0; i < run.Cleared.Count; i++)
                {
                    if (run.Find(run.Cleared[i]) == null || !cleared.Add(run.Cleared[i]))
                    {
                        issues.Add(new SaveIssue(SaveIssueKind.InvalidMapRun, "Campaign.ActiveRun.Cleared[" + i + "]", null, "cleared node " + run.Cleared[i] + " is not on the map or is listed twice"));
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
                        gearCatalog != null && gearCatalog.TryGetAvatarGear(gear.GearId, out BeastCraft.Avatar.AvatarGearSlot gearSlot, out int minimumLevel))
                    {
                        if ((int)gearSlot != slot)
                        {
                            issues.Add(new SaveIssue(SaveIssueKind.GearSlotMismatch, path, instanceId, "'" + gear.GearId + "' belongs in the " + gearSlot + " slot"));
                        }

                        int avatarLevel = save.Avatar == null ? 1 : save.Avatar.Level;
                        if (avatarLevel < minimumLevel)
                        {
                            issues.Add(new SaveIssue(SaveIssueKind.GearLevelTooLow, path, instanceId, "'" + gear.GearId + "' needs avatar level " + minimumLevel + "; the avatar is " + avatarLevel));
                        }
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
                             (gearCatalog == null || (beast ? gearCatalog.TryGetBeastGear(gear.GearId, out GearSlot _, out int _) : gearCatalog.TryGetAvatarGear(gear.GearId, out BeastCraft.Avatar.AvatarGearSlot _, out int _)));
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
