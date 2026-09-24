using System;
using System.Collections.Generic;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// One expedition in progress, as save data: which region and stage, the seed its node map was
    /// generated from, a snapshot of the map itself, where the player stands and what they have
    /// cleared. The map is stored, not regenerated, so a content or generator change never moves
    /// the ground under a saved expedition.
    /// <para>
    /// <see cref="RegionId"/> == "" means no expedition (JsonUtility has no null class fields). Node
    /// ids are the indices into <see cref="Nodes"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class MapRun
    {
        /// <summary>The region, or "" when there is no expedition.</summary>
        public string RegionId = string.Empty;

        /// <summary>The stage, 0-based: stages before the last end at a Gate, the last at the region's Boss.</summary>
        public int Stage;

        /// <summary>The seed the map was generated from (<see cref="NodeMapGenerator.Generate"/>).</summary>
        public int Seed;

        /// <summary>The map, node id = index.</summary>
        public List<MapNode> Nodes = new List<MapNode>();

        /// <summary>The node the player stands on (the last cleared), or −1 at the start (before layer 0).</summary>
        public int CurrentNodeId = -1;

        /// <summary>Cleared (or visited) node ids, in order.</summary>
        public List<int> Cleared = new List<int>();

        /// <summary>
        /// Battles lost on the way (each retry of a node uses a new battle seed:
        /// <see cref="CampaignRules.BattleSeed"/> of the node's attempts so far).
        /// </summary>
        public int Attempts;

        /// <summary>Attempts at the node currently being retried (reset when a node is cleared).</summary>
        public int NodeAttempts;

        /// <summary>The node with <paramref name="nodeId"/>, or null.</summary>
        public MapNode Find(int nodeId)
        {
            return Nodes != null && nodeId >= 0 && nodeId < Nodes.Count ? Nodes[nodeId] : null;
        }

        /// <summary>Whether <paramref name="nodeId"/> has been cleared.</summary>
        public bool IsCleared(int nodeId)
        {
            return Cleared != null && Cleared.Contains(nodeId);
        }

        /// <summary>Resets this to "no expedition".</summary>
        public void Clear()
        {
            RegionId = string.Empty;
            Stage = 0;
            Seed = 0;
            Nodes = new List<MapNode>();
            CurrentNodeId = -1;
            Cleared = new List<int>();
            Attempts = 0;
            NodeAttempts = 0;
        }

        /// <summary>Replaces null lists and strings with empty ones and drops null nodes. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;

            if (RegionId == null)
            {
                RegionId = string.Empty;
                repaired++;
            }

            if (Nodes == null)
            {
                Nodes = new List<MapNode>();
                repaired++;
            }

            repaired += Nodes.RemoveAll(node => node == null);

            foreach (MapNode node in Nodes)
            {
                if (node.Next == null)
                {
                    node.Next = new int[0];
                    repaired++;
                }
            }

            // A map stored before locations had positions (or hand-edited): place it again from its
            // own seed, exactly as the generator would have.
            if (Nodes.Exists(node => !node.IsPlaced || string.IsNullOrEmpty(node.LabelKey)))
            {
                NodeMapGenerator.Place(Nodes, RegionId, Seed);
                repaired++;
            }

            if (Cleared == null)
            {
                Cleared = new List<int>();
                repaired++;
            }

            return repaired;
        }
    }

    /// <summary>
    /// One node of an expedition's map: where it sits (layer, lane), what it is, the encounter it
    /// fields (level, shape or template, and the seed its lineup is drawn with) and the nodes it
    /// leads to. JsonUtility-safe: the type is an enum (written as its number), <see cref="Next"/>
    /// an array.
    /// </summary>
    [Serializable]
    public class MapNode
    {
        /// <summary>Its index in <see cref="MapRun.Nodes"/>.</summary>
        public int NodeId;

        /// <summary>Row, 0 (the start) to the map's top (its Gate or Boss).</summary>
        public int Layer;

        /// <summary>Column, 0 to the map's lanes − 1.</summary>
        public int Lane;

        /// <summary>What the node is.</summary>
        public MapNodeType Type;

        /// <summary>The encounter level (or, for Rest, the level Camp training pays at). 1-100.</summary>
        public int Level = 1;

        /// <summary>The encounter shape a battle node draws (a drop-table shape id); "" for Rest and Shop and template nodes.</summary>
        public string ShapeId = string.Empty;

        /// <summary>The <c>encounter-library.json</c> template a Gate or Boss fields; "" otherwise (generated).</summary>
        public string TemplateId = string.Empty;

        /// <summary>The seed the node's lineup is drawn with: <c>LootRoller.DeriveSeed(map seed, NodeId)</c>.</summary>
        public int EncounterSeed;

        /// <summary>The node ids this one leads to, all on the next layer. Empty at the top.</summary>
        public int[] Next = new int[0];

        /// <summary>
        /// How the location is presented on the region map (<see cref="LocationKinds.For"/> of
        /// <see cref="Type"/>). Presentation only: the rules read <see cref="Type"/>. Added to schema
        /// 3 before it shipped.
        /// </summary>
        public LocationKind Kind;

        /// <summary>
        /// Where the location sits on the region map, normalized: 0 = the map's left edge, 1 = its
        /// right (lanes spread left to right). Derived from <see cref="Lane"/> plus a small seeded
        /// jitter (<see cref="NodeMapGenerator.Place"/>), so a spatial map UI can lay the locations
        /// out as places to explore rather than a grid. In [0.05, 0.95] once placed.
        /// </summary>
        public float X;

        /// <summary>
        /// Where the location sits on the region map, normalized: 0 = the entry edge, 1 = the far
        /// edge (the Pass or the Lair). Derived from <see cref="Layer"/> plus a small seeded jitter.
        /// In [0.05, 0.95] once placed; (0, 0) means "not placed" (<see cref="MapRun.EnsureInitialized"/> places it).
        /// </summary>
        public float Y;

        /// <summary>
        /// The display-label hook: a stable localization key a map UI resolves to the location's
        /// name, <c>"{regionId}/{kind}/{variant}"</c> (e.g. <c>r01/wilds/3</c>; the kind is
        /// <see cref="LocationKinds.Key"/>), the variant drawn from the map seed, 0 to
        /// <see cref="NodeMapGenerator.LabelVariants"/> − 1. "" until placed.
        /// </summary>
        public string LabelKey = string.Empty;

        /// <summary>Whether the location has a map position (<see cref="X"/> and <see cref="Y"/> not both 0).</summary>
        public bool IsPlaced
        {
            get { return X != 0f || Y != 0f; }
        }

        /// <summary>Whether the node is fought (Battle, Elite, Gate, Boss).</summary>
        public bool IsBattle
        {
            get { return Type == MapNodeType.Battle || Type == MapNodeType.Elite || Type == MapNodeType.Gate || Type == MapNodeType.Boss; }
        }
    }
}
