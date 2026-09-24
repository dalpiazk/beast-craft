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

        /// <summary>Whether the node is fought (Battle, Elite, Gate, Boss).</summary>
        public bool IsBattle
        {
            get { return Type == MapNodeType.Battle || Type == MapNodeType.Elite || Type == MapNodeType.Gate || Type == MapNodeType.Boss; }
        }
    }
}
