using System;
using System.Collections.Generic;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// Structural checks for <see cref="RegionLibraryData"/>: the rules <c>regions.json</c> must
    /// satisfy to import at all. Non-throwing; every problem is one message.
    /// <list type="bullet">
    /// <item>Ids present, unique, lowercase snake_case; the first region is
    /// <see cref="CampaignProgress.StartingRegionId"/> and requires nothing; every other requires an
    /// earlier region.</item>
    /// <item>Levels contiguous: the first region starts at 1, each starts one above the last one's
    /// max, the last ends at 100; stages at least 1.</item>
    /// <item>Map rules (the library's and every override) generate a map: at least 3 layers, a lane,
    /// a path, a rest row between the start and the top (or −1), positive weights of Battle, Elite,
    /// Shop or Rest, sane elite and shop counts, non-negative offsets, an elite shape.</item>
    /// <item>Seals: caps 1-100, each granted by at most one boss; the starting cap covers the first
    /// region; following the regions in order, the caps never fall and each covers the next
    /// region's max level.</item>
    /// <item>With the encounter library: every Battle shape, the elite shape, and every gate and boss
    /// template exists.</item>
    /// </list>
    /// Balance (how many battles a region takes, whether the cap bites) is the balance simulator's
    /// <c>--mode campaign</c>, not this.
    /// </summary>
    public static class RegionLibraryValidator
    {
        /// <summary>The top of the level range the regions must cover.</summary>
        public const int MaxLevel = 100;

        /// <summary>Every problem, without the encounter cross-checks.</summary>
        public static List<string> Validate(RegionLibraryData data)
        {
            return Validate(data, null);
        }

        /// <summary>Every problem found; empty means importable. With <paramref name="encounters"/>, shapes and templates are looked up too.</summary>
        public static List<string> Validate(RegionLibraryData data, EncounterLibraryData encounters)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Region library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != RegionLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + RegionLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> shapes = null;
            HashSet<string> templates = null;
            if (encounters != null)
            {
                shapes = new HashSet<string>(StringComparer.Ordinal);
                templates = new HashSet<string>(StringComparer.Ordinal);
                foreach (EncounterShapeData shape in encounters.Shapes ?? new EncounterShapeData[0])
                {
                    if (shape != null && !string.IsNullOrEmpty(shape.ShapeId))
                    {
                        shapes.Add(shape.ShapeId);
                    }
                }

                foreach (EncounterTemplateData template in encounters.Templates ?? new EncounterTemplateData[0])
                {
                    if (template != null && !string.IsNullOrEmpty(template.EncounterId))
                    {
                        templates.Add(template.EncounterId);
                    }
                }
            }

            ValidateRules(data.MapRules, "MapRules", shapes, errors);
            Dictionary<string, SealData> seals = ValidateSeals(data.Seals, errors);

            if (data.StartingLevelCap < 1 || data.StartingLevelCap > MaxLevel)
            {
                errors.Add("StartingLevelCap " + data.StartingLevelCap + " must be 1-" + MaxLevel + ".");
            }

            if (data.LevelCapMargin < 0)
            {
                errors.Add("LevelCapMargin must be 0 or more.");
            }

            ValidateRegions(data, seals, shapes, templates, errors);
            return errors;
        }

        private static Dictionary<string, SealData> ValidateSeals(SealData[] seals, List<string> errors)
        {
            Dictionary<string, SealData> byId = new Dictionary<string, SealData>(StringComparer.Ordinal);
            if (seals == null || seals.Length == 0)
            {
                errors.Add("No Seals.");
                return byId;
            }

            for (int i = 0; i < seals.Length; i++)
            {
                SealData seal = seals[i];
                if (seal == null)
                {
                    errors.Add("Seal #" + i + " is null.");
                    continue;
                }

                string where = "Seal '" + seal.SealId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(seal.SealId))
                {
                    errors.Add(where + ": SealId must be lowercase snake_case.");
                }
                else if (byId.ContainsKey(seal.SealId))
                {
                    errors.Add(where + ": duplicate SealId.");
                }
                else
                {
                    byId.Add(seal.SealId, seal);
                }

                if (seal.LevelCap < 1 || seal.LevelCap > MaxLevel)
                {
                    errors.Add(where + ": LevelCap " + seal.LevelCap + " must be 1-" + MaxLevel + ".");
                }
            }

            return byId;
        }

        private static void ValidateRegions(RegionLibraryData data, Dictionary<string, SealData> seals, HashSet<string> shapes, HashSet<string> templates,
                                            List<string> errors)
        {
            RegionData[] regions = data.Regions;
            if (regions == null || regions.Length == 0)
            {
                errors.Add("No Regions.");
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> grantedSeals = new HashSet<string>(StringComparer.Ordinal);
            int expectedMin = 1;
            int previousCap = data.StartingLevelCap;
            string previousCapSource = "StartingLevelCap";

            for (int i = 0; i < regions.Length; i++)
            {
                RegionData region = regions[i];
                if (region == null)
                {
                    errors.Add("Region #" + i + " is null.");
                    continue;
                }

                string where = "Region '" + region.RegionId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(region.RegionId))
                {
                    errors.Add(where + ": RegionId must be lowercase snake_case.");
                }
                else if (!seen.Add(region.RegionId))
                {
                    errors.Add(where + ": duplicate RegionId.");
                }

                if (i == 0)
                {
                    if (!string.Equals(region.RegionId, CampaignProgress.StartingRegionId, StringComparison.Ordinal))
                    {
                        errors.Add(where + ": the first region must be '" + CampaignProgress.StartingRegionId + "' (the region every save starts with).");
                    }

                    if (!string.IsNullOrEmpty(region.RequiresRegionId))
                    {
                        errors.Add(where + ": the first region cannot require another.");
                    }

                    if (data.StartingLevelCap < region.MaxLevel)
                    {
                        errors.Add("StartingLevelCap " + data.StartingLevelCap + " is below the first region's max level " + region.MaxLevel + ".");
                    }
                }
                else if (string.IsNullOrEmpty(region.RequiresRegionId) || !seen.Contains(region.RequiresRegionId) ||
                         string.Equals(region.RequiresRegionId, region.RegionId, StringComparison.Ordinal))
                {
                    errors.Add(where + ": RequiresRegionId '" + region.RequiresRegionId + "' must name an earlier region.");
                }

                if (region.MinLevel != expectedMin)
                {
                    errors.Add(where + ": MinLevel " + region.MinLevel + " should be " + expectedMin + " (levels are contiguous from 1).");
                }

                if (region.MaxLevel < region.MinLevel || region.MaxLevel > MaxLevel)
                {
                    errors.Add(where + ": MaxLevel " + region.MaxLevel + " must be between MinLevel and " + MaxLevel + ".");
                }

                expectedMin = region.MaxLevel + 1;

                if (region.Stages < 1)
                {
                    errors.Add(where + ": Stages must be at least 1.");
                }

                if (region.MapRules != null && region.MapRules.Layers > 0)
                {
                    ValidateRules(region.MapRules, where + " MapRules", shapes, errors);
                }

                ValidateShapeWeights(region, where, shapes, errors);

                string[] gates = region.GateTemplateIds ?? new string[0];
                if (gates.Length > Math.Max(0, region.Stages - 1))
                {
                    errors.Add(where + ": more GateTemplateIds (" + gates.Length + ") than gate stages (" + Math.Max(0, region.Stages - 1) + ").");
                }

                foreach (string gate in gates)
                {
                    if (!string.IsNullOrEmpty(gate) && templates != null && !templates.Contains(gate))
                    {
                        errors.Add(where + ": gate template '" + gate + "' is not in the encounter library.");
                    }
                }

                if (string.IsNullOrEmpty(region.BossTemplateId))
                {
                    errors.Add(where + ": no BossTemplateId.");
                }
                else if (templates != null && !templates.Contains(region.BossTemplateId))
                {
                    errors.Add(where + ": boss template '" + region.BossTemplateId + "' is not in the encounter library.");
                }

                if (string.IsNullOrEmpty(region.BossRewardSealId))
                {
                    continue;
                }

                if (!seals.TryGetValue(region.BossRewardSealId, out SealData seal))
                {
                    errors.Add(where + ": BossRewardSealId '" + region.BossRewardSealId + "' is not a seal.");
                    continue;
                }

                if (!grantedSeals.Add(seal.SealId))
                {
                    errors.Add(where + ": seal '" + seal.SealId + "' is already granted by another boss.");
                }

                if (seal.LevelCap < previousCap)
                {
                    errors.Add(where + ": seal '" + seal.SealId + "' caps at " + seal.LevelCap + ", below " + previousCapSource + " (" + previousCap + "); caps must not fall.");
                }

                RegionData next = i + 1 < regions.Length ? regions[i + 1] : null;
                if (next != null && seal.LevelCap < Math.Min(MaxLevel, next.MaxLevel))
                {
                    errors.Add(where + ": seal '" + seal.SealId + "' caps at " + seal.LevelCap + ", below the next region's max level " + next.MaxLevel + ".");
                }

                previousCap = seal.LevelCap;
                previousCapSource = "seal '" + seal.SealId + "'";
            }

            if (expectedMin != MaxLevel + 1)
            {
                errors.Add("The last region must end at level " + MaxLevel + ".");
            }
        }

        private static void ValidateShapeWeights(RegionData region, string where, HashSet<string> shapes, List<string> errors)
        {
            if (region.ShapeWeights == null || region.ShapeWeights.Length == 0)
            {
                errors.Add(where + ": no ShapeWeights (Battle nodes need a shape).");
                return;
            }

            HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShapeWeightData weight in region.ShapeWeights)
            {
                if (weight == null || string.IsNullOrEmpty(weight.ShapeId))
                {
                    errors.Add(where + ": a shape weight has no ShapeId.");
                    continue;
                }

                if (!listed.Add(weight.ShapeId))
                {
                    errors.Add(where + ": shape '" + weight.ShapeId + "' is weighted twice.");
                }

                if (weight.Weight < 1)
                {
                    errors.Add(where + ": shape '" + weight.ShapeId + "' needs a positive Weight.");
                }

                if (shapes != null && !shapes.Contains(weight.ShapeId))
                {
                    errors.Add(where + ": shape '" + weight.ShapeId + "' is not in the encounter library.");
                }
            }
        }

        private static void ValidateRules(MapRulesData rules, string where, HashSet<string> shapes, List<string> errors)
        {
            if (rules == null)
            {
                errors.Add(where + " is missing.");
                return;
            }

            if (rules.Layers < 3)
            {
                errors.Add(where + ": Layers must be at least 3 (a start row, a middle, the top).");
            }

            if (rules.Lanes < 1)
            {
                errors.Add(where + ": Lanes must be at least 1.");
            }

            if (rules.Paths < 1)
            {
                errors.Add(where + ": Paths must be at least 1.");
            }

            if (rules.RestLayer != -1 && (rules.RestLayer < 1 || rules.RestLayer > rules.Layers - 2))
            {
                errors.Add(where + ": RestLayer must be -1 (none) or 1 to Layers - 2.");
            }

            if (rules.EliteMinLayer < 1)
            {
                errors.Add(where + ": EliteMinLayer must be at least 1 (the start row is always a Battle).");
            }

            if (rules.MinElites < 0 || rules.MaxElites < rules.MinElites)
            {
                errors.Add(where + ": need 0 <= MinElites <= MaxElites.");
            }

            if (rules.MaxShops < 0)
            {
                errors.Add(where + ": MaxShops must be 0 or more.");
            }

            if (rules.EliteLevelOffset < 0 || rules.GateLevelOffset < 0)
            {
                errors.Add(where + ": level offsets must be 0 or more.");
            }

            if (string.IsNullOrEmpty(rules.EliteShapeId))
            {
                errors.Add(where + ": no EliteShapeId.");
            }
            else if (shapes != null && !shapes.Contains(rules.EliteShapeId))
            {
                errors.Add(where + ": EliteShapeId '" + rules.EliteShapeId + "' is not in the encounter library.");
            }

            if (rules.NodeWeights == null || rules.NodeWeights.Length == 0)
            {
                errors.Add(where + ": no NodeWeights.");
                return;
            }

            bool battle = false;
            HashSet<MapNodeType> listed = new HashSet<MapNodeType>();
            foreach (NodeWeightData weight in rules.NodeWeights)
            {
                if (weight == null || !TryParseDrawnType(weight.Type, out MapNodeType type))
                {
                    errors.Add(where + ": node weight type '" + (weight == null ? null : weight.Type) + "' must be Battle, Elite, Shop or Rest.");
                    continue;
                }

                if (!listed.Add(type))
                {
                    errors.Add(where + ": node type " + type + " is weighted twice.");
                }

                if (weight.Weight < 1)
                {
                    errors.Add(where + ": node type " + type + " needs a positive Weight.");
                }

                battle |= type == MapNodeType.Battle;
            }

            if (!battle)
            {
                errors.Add(where + ": NodeWeights must include Battle (the fallback type).");
            }
        }

        /// <summary>A node type the weighted draw may produce: Battle, Elite, Shop or Rest (case-sensitive member names).</summary>
        public static bool TryParseDrawnType(string name, out MapNodeType type)
        {
            type = MapNodeType.Battle;
            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(MapNodeType), name))
            {
                return false;
            }

            type = (MapNodeType)Enum.Parse(typeof(MapNodeType), name);
            return type == MapNodeType.Battle || type == MapNodeType.Elite || type == MapNodeType.Shop || type == MapNodeType.Rest;
        }
    }
}
