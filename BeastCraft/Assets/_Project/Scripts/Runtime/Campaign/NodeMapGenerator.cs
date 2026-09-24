using System;
using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// Generates one stage's node map, Slay-the-Spire style, from a region, its map rules, the stage
    /// and a seed. Deterministic: one <see cref="System.Random"/> seeded with the seed, drawn in a
    /// fixed order (paths, then node types, then Battle shapes).
    /// <list type="number">
    /// <item><b>Paths.</b> <see cref="MapRulesData.Paths"/> paths each walk from a lane of row 0 up
    /// to the row under the top, one row at a time, stepping −1, 0 or +1 lane, never crossing an
    /// edge already walked (a straight step never crosses, so there is always one). The first two
    /// paths start in different lanes. The map is the union of the cells and edges walked; every
    /// node of the row under the top leads to the single top node.</item>
    /// <item><b>Types.</b> Row 0 is Battle, <see cref="MapRulesData.RestLayer"/> is Rest (Camp), the
    /// top is the Gate (or, on the region's last stage, the Boss); every other node is drawn from
    /// <see cref="MapRulesData.NodeWeights"/>, except that an Elite, Rest or Shop never follows a
    /// node of the same type, no Elite sits below <see cref="MapRulesData.EliteMinLayer"/>, and no
    /// Rest sits right under the rest row. A map with too few or too many Elites, or too many Shops,
    /// is re-drawn (up to <see cref="MaxTypeAttempts"/> times); the last draw is then fixed up
    /// deterministically (extra Shops and Elites become Battles, lowest-id eligible Battles become
    /// Elites).</item>
    /// <item><b>Levels</b> (<see cref="RowLevel"/>): the region's levels spread over all its stages'
    /// rows; an Elite is <see cref="MapRulesData.EliteLevelOffset"/> above its row, a Gate
    /// <see cref="MapRulesData.GateLevelOffset"/> above the top row, the Boss exactly the region's
    /// max level.</item>
    /// <item><b>Encounters.</b> A Battle draws its shape from the region's
    /// <see cref="RegionData.ShapeWeights"/>; an Elite (and a Gate without an authored template)
    /// uses <see cref="MapRulesData.EliteShapeId"/>; the Boss and authored Gates name their
    /// template. Every node's <see cref="MapNode.EncounterSeed"/> is
    /// <c>LootRoller.DeriveSeed(seed, NodeId)</c>.</item>
    /// </list>
    /// Node ids run row by row, lane by lane, the top last. Tunable starting rules (regions.json),
    /// paced by the balance simulator's <c>--mode campaign</c>.
    /// </summary>
    public static class NodeMapGenerator
    {
        /// <summary>How many full type draws are tried before the deterministic fix-up.</summary>
        public const int MaxTypeAttempts = 20;

        /// <summary>
        /// The base level of row <paramref name="layer"/> of stage <paramref name="stage"/>:
        /// <c>MinLevel + floor(t × (MaxLevel − MinLevel) / T)</c> with <c>t = stage × (Layers − 1) + layer</c>
        /// and <c>T = Stages × (Layers − 1)</c> — the first stage's row 0 at the region's min level,
        /// the last stage's top at its max. Clamped to 1-100.
        /// </summary>
        public static int RowLevel(RegionData region, MapRulesData rules, int stage, int layer)
        {
            int rows = Math.Max(1, rules.Layers - 1);
            int stages = Math.Max(1, region.Stages);
            long t = ((long)Clamp(stage, 0, stages - 1) * rows) + Clamp(layer, 0, rows);
            long total = (long)stages * rows;
            int level = region.MinLevel + (int)(t * Math.Max(0, region.MaxLevel - region.MinLevel) / total);
            return Clamp(level, 1, BeastProgression.MaxLevel);
        }

        /// <summary>
        /// Stage <paramref name="stage"/> of <paramref name="regionId"/> (with the region's effective
        /// rules, <see cref="RegionLibrary.RulesFor"/>). Null for an unknown region.
        /// </summary>
        public static List<MapNode> Generate(RegionLibrary library, string regionId, int stage, int seed)
        {
            RegionData region = library == null ? null : library.GetRegion(regionId);
            return region == null ? null : Generate(region, library.RulesFor(region), stage, seed);
        }

        /// <summary>Stage <paramref name="stage"/> (clamped to the region's stages) of <paramref name="region"/> under <paramref name="rules"/>. See the class remarks.</summary>
        public static List<MapNode> Generate(RegionData region, MapRulesData rules, int stage, int seed)
        {
            if (region == null || rules == null)
            {
                return null;
            }

            Random rng = new Random(seed);
            int stages = Math.Max(1, region.Stages);
            stage = Clamp(stage, 0, stages - 1);
            int layers = Math.Max(3, rules.Layers);
            int lanes = Math.Max(1, rules.Lanes);
            int rows = layers - 1;

            // 1. Paths over rows 0..rows-1.
            bool[,] cells = new bool[rows, lanes];
            HashSet<long> edges = new HashSet<long>();
            List<int[]> edgeList = new List<int[]>();
            int firstLane = -1;
            for (int p = 0; p < Math.Max(1, rules.Paths); p++)
            {
                int lane;
                if (p == 1 && lanes > 1)
                {
                    lane = rng.Next(lanes - 1);
                    lane = lane >= firstLane ? lane + 1 : lane;
                }
                else
                {
                    lane = rng.Next(lanes);
                }

                if (p == 0)
                {
                    firstLane = lane;
                }

                cells[0, lane] = true;
                for (int layer = 0; layer < rows - 1; layer++)
                {
                    List<int> steps = new List<int>(3);
                    for (int step = -1; step <= 1; step++)
                    {
                        int target = lane + step;
                        if (target >= 0 && target < lanes && !Crosses(edgeList, layer, lane, target))
                        {
                            steps.Add(target);
                        }
                    }

                    int to = steps[rng.Next(steps.Count)];
                    if (edges.Add(EdgeKey(layer, lane, to)))
                    {
                        edgeList.Add(new[] { layer, lane, to });
                    }

                    lane = to;
                    cells[layer + 1, lane] = true;
                }
            }

            // 2. Nodes, row by row, lane by lane, then the top.
            List<MapNode> nodes = new List<MapNode>();
            int[,] ids = new int[rows, lanes];
            for (int layer = 0; layer < rows; layer++)
            {
                for (int lane = 0; lane < lanes; lane++)
                {
                    ids[layer, lane] = -1;
                    if (cells[layer, lane])
                    {
                        ids[layer, lane] = nodes.Count;
                        nodes.Add(new MapNode { NodeId = nodes.Count, Layer = layer, Lane = lane });
                    }
                }
            }

            bool last = stage == stages - 1;
            MapNode top = new MapNode { NodeId = nodes.Count, Layer = rows, Lane = lanes / 2, Type = last ? MapNodeType.Boss : MapNodeType.Gate };
            nodes.Add(top);

            List<int>[] next = new List<int>[nodes.Count];
            List<int>[] parents = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                next[i] = new List<int>();
                parents[i] = new List<int>();
            }

            foreach (int[] edge in edgeList)
            {
                Link(next, parents, ids[edge[0], edge[1]], ids[edge[0] + 1, edge[2]]);
            }

            foreach (MapNode node in nodes)
            {
                if (node.Layer == rows - 1)
                {
                    Link(next, parents, node.NodeId, top.NodeId);
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                next[i].Sort();
                nodes[i].Next = next[i].ToArray();
            }

            // 3. Types.
            AssignTypes(nodes, parents, next, rules, rows, rng);

            // 4. Levels, encounters and seeds.
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode node = nodes[i];
                int rowLevel = RowLevel(region, rules, stage, node.Layer);
                node.EncounterSeed = LootRoller.DeriveSeed(seed, node.NodeId);
                switch (node.Type)
                {
                    case MapNodeType.Battle:
                        node.Level = rowLevel;
                        node.ShapeId = DrawShape(region.ShapeWeights, rng);
                        break;
                    case MapNodeType.Elite:
                        node.Level = Clamp(rowLevel + rules.EliteLevelOffset, 1, BeastProgression.MaxLevel);
                        node.ShapeId = rules.EliteShapeId ?? string.Empty;
                        break;
                    case MapNodeType.Gate:
                        node.Level = Clamp(rowLevel + rules.GateLevelOffset, 1, BeastProgression.MaxLevel);
                        string gate = region.GateTemplateIds != null && stage < region.GateTemplateIds.Length ? region.GateTemplateIds[stage] : null;
                        if (string.IsNullOrEmpty(gate))
                        {
                            node.ShapeId = rules.EliteShapeId ?? string.Empty;
                        }
                        else
                        {
                            node.TemplateId = gate;
                        }

                        break;
                    case MapNodeType.Boss:
                        node.Level = Clamp(region.MaxLevel, 1, BeastProgression.MaxLevel);
                        node.TemplateId = region.BossTemplateId ?? string.Empty;
                        break;
                    default:
                        node.Level = rowLevel;
                        break;
                }
            }

            return nodes;
        }

        private static void AssignTypes(List<MapNode> nodes, List<int>[] parents, List<int>[] children, MapRulesData rules, int rows, Random rng)
        {
            List<MapNodeType> weightTypes = new List<MapNodeType>();
            List<int> weights = new List<int>();
            foreach (NodeWeightData entry in rules.NodeWeights ?? new NodeWeightData[0])
            {
                if (entry != null && entry.Weight > 0 && RegionLibraryValidator.TryParseDrawnType(entry.Type, out MapNodeType type))
                {
                    weightTypes.Add(type);
                    weights.Add(entry.Weight);
                }
            }

            for (int attempt = 0; attempt < MaxTypeAttempts; attempt++)
            {
                DrawTypes(nodes, parents, rules, rows, weightTypes, weights, rng);
                int elites = Count(nodes, MapNodeType.Elite);
                if (elites >= rules.MinElites && elites <= rules.MaxElites && Count(nodes, MapNodeType.Shop) <= rules.MaxShops)
                {
                    return;
                }
            }

            FixCounts(nodes, parents, children, rules, rows);
        }

        private static void DrawTypes(List<MapNode> nodes, List<int>[] parents, MapRulesData rules, int rows, List<MapNodeType> types, List<int> weights, Random rng)
        {
            foreach (MapNode node in nodes)
            {
                if (node.Layer == rows)
                {
                    continue;
                }

                if (node.Layer == 0)
                {
                    node.Type = MapNodeType.Battle;
                    continue;
                }

                if (node.Layer == rules.RestLayer)
                {
                    node.Type = MapNodeType.Rest;
                    continue;
                }

                int total = 0;
                int[] allowed = new int[types.Count];
                for (int i = 0; i < types.Count; i++)
                {
                    allowed[i] = Allowed(nodes, parents, node, types[i], rules) ? weights[i] : 0;
                    total += allowed[i];
                }

                node.Type = MapNodeType.Battle;
                if (total <= 0)
                {
                    continue;
                }

                int roll = rng.Next(total);
                for (int i = 0; i < types.Count; i++)
                {
                    if (roll < allowed[i])
                    {
                        node.Type = types[i];
                        break;
                    }

                    roll -= allowed[i];
                }
            }
        }

        /// <summary>Whether <paramref name="node"/> may be <paramref name="type"/> given its parents (already typed) and its row.</summary>
        private static bool Allowed(List<MapNode> nodes, List<int>[] parents, MapNode node, MapNodeType type, MapRulesData rules)
        {
            if (type == MapNodeType.Elite && node.Layer < rules.EliteMinLayer)
            {
                return false;
            }

            if (type == MapNodeType.Rest && rules.RestLayer >= 0 && node.Layer == rules.RestLayer - 1)
            {
                return false;
            }

            if (type == MapNodeType.Battle)
            {
                return true;
            }

            foreach (int parent in parents[node.NodeId])
            {
                if (nodes[parent].Type == type)
                {
                    return false;
                }
            }

            return true;
        }

        private static void FixCounts(List<MapNode> nodes, List<int>[] parents, List<int>[] children, MapRulesData rules, int rows)
        {
            for (int i = nodes.Count - 1; i >= 0 && Count(nodes, MapNodeType.Shop) > rules.MaxShops; i--)
            {
                if (nodes[i].Type == MapNodeType.Shop)
                {
                    nodes[i].Type = MapNodeType.Battle;
                }
            }

            for (int i = nodes.Count - 1; i >= 0 && Count(nodes, MapNodeType.Elite) > rules.MaxElites; i--)
            {
                if (nodes[i].Type == MapNodeType.Elite)
                {
                    nodes[i].Type = MapNodeType.Battle;
                }
            }

            for (int i = 0; i < nodes.Count && Count(nodes, MapNodeType.Elite) < rules.MinElites; i++)
            {
                MapNode node = nodes[i];
                if (node.Type != MapNodeType.Battle || node.Layer == 0 || node.Layer == rows || node.Layer < rules.EliteMinLayer ||
                    Touches(nodes, parents[i], MapNodeType.Elite) || Touches(nodes, children[i], MapNodeType.Elite))
                {
                    continue;
                }

                node.Type = MapNodeType.Elite;
            }
        }

        private static bool Touches(List<MapNode> nodes, List<int> neighbours, MapNodeType type)
        {
            foreach (int id in neighbours)
            {
                if (nodes[id].Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Count(List<MapNode> nodes, MapNodeType type)
        {
            int count = 0;
            foreach (MapNode node in nodes)
            {
                count += node.Type == type ? 1 : 0;
            }

            return count;
        }

        private static string DrawShape(ShapeWeightData[] weights, Random rng)
        {
            int total = 0;
            foreach (ShapeWeightData weight in weights ?? new ShapeWeightData[0])
            {
                total += weight != null && weight.Weight > 0 ? weight.Weight : 0;
            }

            if (total <= 0)
            {
                return string.Empty;
            }

            int roll = rng.Next(total);
            foreach (ShapeWeightData weight in weights)
            {
                int w = weight != null && weight.Weight > 0 ? weight.Weight : 0;
                if (roll < w)
                {
                    return weight.ShapeId;
                }

                roll -= w;
            }

            return string.Empty;
        }

        /// <summary>Whether an edge from lane <paramref name="from"/> to <paramref name="to"/> on <paramref name="layer"/> crosses one already walked.</summary>
        private static bool Crosses(List<int[]> edges, int layer, int from, int to)
        {
            foreach (int[] edge in edges)
            {
                if (edge[0] == layer && ((edge[1] < from && edge[2] > to) || (edge[1] > from && edge[2] < to)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Link(List<int>[] next, List<int>[] parents, int from, int to)
        {
            if (from < 0 || to < 0 || next[from].Contains(to))
            {
                return;
            }

            next[from].Add(to);
            parents[to].Add(from);
        }

        private static long EdgeKey(int layer, int from, int to)
        {
            return ((long)layer << 32) | ((long)(from & 0xFFFF) << 16) | (uint)(to & 0xFFFF);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
