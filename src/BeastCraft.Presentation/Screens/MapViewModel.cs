using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>How a location on the region map shows.</summary>
    public enum MapNodeState
    {
        /// <summary>Not reachable yet (further up the trail).</summary>
        Locked,

        /// <summary>Can be entered now.</summary>
        Reachable,

        /// <summary>Cleared on the way.</summary>
        Cleared,

        /// <summary>Cleared, and where the player stands.</summary>
        Current,

        /// <summary>Left behind: on a row already passed, never cleared (another path was taken).</summary>
        Bypassed
    }

    /// <summary>How a trail between two locations shows.</summary>
    public enum MapPathState
    {
        /// <summary>Not on the way yet.</summary>
        Faint,

        /// <summary>Leads from where the player stands to a reachable location.</summary>
        Open,

        /// <summary>Already walked (both ends cleared, or the trailhead to the first cleared location).</summary>
        Walked
    }

    /// <summary>One location as the map draws it.</summary>
    public sealed class MapNodeView
    {
        public int NodeId;
        public MapNodeType Type;
        public MapNodeState State;

        /// <summary>Where it sits in map-world pixels (the map is <see cref="MapLayout.WorldWidth"/> wide, scrolled vertically).</summary>
        public Vec2 Position;

        public float Radius;
        public string Name;
        public int Level;
        public int Layer;

        /// <summary>A short label for the type: Wilds, Den, Pass, Lair, Trader, Camp.</summary>
        public string KindLabel;
    }

    /// <summary>A winding trail between two locations: points along a curve, in map-world pixels.</summary>
    public sealed class MapPathView
    {
        /// <summary>The location it leaves (−1 = the trailhead).</summary>
        public int FromId;

        public int ToId;
        public MapPathState State;
        public List<Vec2> Points = new List<Vec2>();
    }

    /// <summary>A soft blob of the placeholder painted background (meadow, darker grass, a pond), map-world pixels.</summary>
    public readonly struct MapBlob
    {
        public MapBlob(Vec2 center, float radius, int tone)
        {
            Center = center;
            Radius = radius;
            Tone = tone;
        }

        public Vec2 Center { get; }

        public float Radius { get; }

        /// <summary>0 light meadow, 1 deep grass, 2 pond, 3 a small tuft or bush.</summary>
        public int Tone { get; }
    }

    /// <summary>
    /// Where a stage's locations sit on the region map, as view data. The Core map is layered
    /// (rows and lanes, the internal pacing model); this places it as a map to explore: rows run up
    /// the map from the trailhead at the bottom to the pass or lair at the top, lanes spread across,
    /// every location nudged by a seeded jitter and the whole trail swayed by a gentle meander, so it
    /// never reads as a grid. Trails are curves between linked locations, and the placeholder
    /// painted background is a few soft blobs. Pure and deterministic: the same map and seed always
    /// lay out the same way.
    /// </summary>
    public sealed class MapLayout
    {
        public const float WorldWidth = PortraitLayout.CanvasWidth;

        /// <summary>Map-world pixels between rows.</summary>
        public const float RowStep = 210f;

        /// <summary>Room above the top row (under the region header).</summary>
        public const float TopPad = 380f;

        /// <summary>Room below the bottom row (the trailhead, above the bottom nav).</summary>
        public const float BottomPad = 520f;

        public const float SideMargin = 170f;

        public const float NodeRadius = 58f;

        public const float BigNodeRadius = 84f;

        public const int PathSamples = 20;

        private MapLayout()
        {
        }

        public float WorldHeight { get; private set; }

        public Vec2 Trailhead { get; private set; }

        public Dictionary<int, Vec2> Positions { get; } = new Dictionary<int, Vec2>();

        /// <summary>Every link, from → to, with its curve (state left Faint: the view model sets it).</summary>
        public List<MapPathView> Paths { get; } = new List<MapPathView>();

        public List<MapBlob> Blobs { get; } = new List<MapBlob>();

        /// <summary>Lays out <paramref name="nodes"/> (a map run's) for map seed <paramref name="seed"/>.</summary>
        public static MapLayout Of(IReadOnlyList<MapNode> nodes, int seed)
        {
            MapLayout layout = new MapLayout();
            int rows = 0;
            int lanes = 1;
            foreach (MapNode node in nodes ?? new MapNode[0])
            {
                if (node != null)
                {
                    rows = Math.Max(rows, node.Layer);
                    lanes = Math.Max(lanes, node.Lane + 1);
                }
            }

            layout.WorldHeight = TopPad + BottomPad + rows * RowStep;
            float bottomY = layout.WorldHeight - BottomPad;
            float phase = (DeterministicRandom.Hash(seed, 7) % 628) / 100f;
            float laneStep = lanes > 1 ? (WorldWidth - 2f * SideMargin) / (lanes - 1) : 0f;
            layout.Trailhead = new Vec2(WorldWidth / 2f + Meander(-1, phase) * 0.5f, bottomY + RowStep * 1.35f);

            foreach (MapNode node in nodes ?? new MapNode[0])
            {
                if (node == null)
                {
                    continue;
                }

                DeterministicRandom rng = new DeterministicRandom((int)DeterministicRandom.Hash(seed, 1000 + node.NodeId));
                float jitterX = (rng.NextFloat() - 0.5f) * 0.44f * Math.Max(laneStep, 160f);
                float jitterY = (rng.NextFloat() - 0.5f) * 0.36f * RowStep;
                bool top = node.Layer >= rows && rows > 0;
                float x = top ? WorldWidth / 2f : lanes > 1 ? SideMargin + node.Lane * laneStep + jitterX : WorldWidth / 2f + jitterX;
                x += Meander(node.Layer, phase) * (top ? 0.3f : 1f);
                float y = bottomY - node.Layer * RowStep + (top ? 0f : jitterY);
                x = Math.Max(SideMargin * 0.6f, Math.Min(WorldWidth - SideMargin * 0.6f, x));
                layout.Positions[node.NodeId] = new Vec2(x, y);
            }

            foreach (MapNode node in nodes ?? new MapNode[0])
            {
                if (node == null)
                {
                    continue;
                }

                if (node.Layer == 0)
                {
                    layout.Paths.Add(Curve(-1, node.NodeId, layout.Trailhead, layout.Positions[node.NodeId], seed));
                }

                foreach (int next in node.Next ?? new int[0])
                {
                    if (layout.Positions.TryGetValue(next, out Vec2 to))
                    {
                        layout.Paths.Add(Curve(node.NodeId, next, layout.Positions[node.NodeId], to, seed));
                    }
                }
            }

            DeterministicRandom decor = new DeterministicRandom((int)DeterministicRandom.Hash(seed, 77));
            int big = 6 + (int)(layout.WorldHeight / 600f);
            for (int i = 0; i < big; i++)
            {
                float y = layout.WorldHeight * (i + decor.NextFloat()) / big;
                float x = -80f + decor.NextFloat() * (WorldWidth + 160f);
                int tone = decor.Next(10) < 2 ? 2 : decor.Next(2);
                float radius = tone == 2 ? 90f + decor.NextFloat() * 90f : 160f + decor.NextFloat() * 200f;
                layout.Blobs.Add(new MapBlob(new Vec2(x, y), radius, tone));
            }

            int tufts = (int)(layout.WorldHeight / 45f);
            for (int i = 0; i < tufts; i++)
            {
                Vec2 at = new Vec2(decor.NextFloat() * WorldWidth, decor.NextFloat() * layout.WorldHeight);
                if (NearTrail(layout, at, 70f))
                {
                    continue;
                }

                layout.Blobs.Add(new MapBlob(at, 14f + decor.NextFloat() * 20f, 3));
            }

            return layout;
        }

        /// <summary>A gentle side-to-side sway of the trail by row.</summary>
        private static float Meander(int layer, float phase)
        {
            return (float)Math.Sin(layer * 0.85 + phase) * 70f;
        }

        private static MapPathView Curve(int from, int to, Vec2 a, Vec2 b, int seed)
        {
            DeterministicRandom rng = new DeterministicRandom((int)DeterministicRandom.Hash(seed, (from + 2) * 4099 + to));
            float dy = b.Y - a.Y;
            float wiggle1 = (rng.NextFloat() - 0.5f) * 150f;
            float wiggle2 = (rng.NextFloat() - 0.5f) * 150f;
            Vec2 c1 = new Vec2(a.X + wiggle1, a.Y + dy * 0.42f);
            Vec2 c2 = new Vec2(b.X + wiggle2, b.Y - dy * 0.42f);
            MapPathView path = new MapPathView { FromId = from, ToId = to };
            for (int i = 0; i <= PathSamples; i++)
            {
                float t = i / (float)PathSamples;
                float u = 1f - t;
                float x = u * u * u * a.X + 3f * u * u * t * c1.X + 3f * u * t * t * c2.X + t * t * t * b.X;
                float y = u * u * u * a.Y + 3f * u * u * t * c1.Y + 3f * u * t * t * c2.Y + t * t * t * b.Y;
                path.Points.Add(new Vec2(x, y));
            }

            return path;
        }

        private static bool NearTrail(MapLayout layout, Vec2 at, float distance)
        {
            foreach (Vec2 node in layout.Positions.Values)
            {
                if (Math.Abs(node.X - at.X) < distance * 1.5f && Math.Abs(node.Y - at.Y) < distance * 1.5f)
                {
                    return true;
                }
            }

            foreach (MapPathView path in layout.Paths)
            {
                foreach (Vec2 point in path.Points)
                {
                    if (Math.Abs(point.X - at.X) < distance && Math.Abs(point.Y - at.Y) < distance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>The region header: where the player is and how far along.</summary>
    public sealed class RegionHeaderView
    {
        public string RegionId;
        public string Name;

        /// <summary>e.g. "Lv 1-10".</summary>
        public string LevelBand;

        /// <summary>e.g. "Stage 2 of 4".</summary>
        public string StageText;

        public int Stage;
        public int Stages;
        public int StagesCleared;
        public bool BossCleared;
        public string SealName;
        public bool SealOwned;

        /// <summary>The seal's progress, 0-1: stages cleared (the lair counting as the last).</summary>
        public float SealProgress;

        /// <summary>The beast level cap ("binding limit") the owned seals give.</summary>
        public int BindingLimit;
    }

    /// <summary>What a tap on a location does.</summary>
    public enum MapTapKind
    {
        None,

        /// <summary>Open the encounter preview for it.</summary>
        Preview,

        /// <summary>A Trader or Camp: not built yet (PR 3); show the message as a toast.</summary>
        ComingSoon,

        /// <summary>Not reachable (or already cleared): show the message as a toast.</summary>
        Refused
    }

    public sealed class MapTapResult
    {
        public MapTapKind Kind;
        public int NodeId = -1;
        public string Message;
    }

    /// <summary>
    /// The Map tab: the current region and stage as a spatial map (<see cref="MapLayout"/>), every
    /// location's state (cleared, current, reachable, locked, bypassed), the trails, and the region
    /// header (name, level band, the seal's progress, the binding limit). A tap on a reachable
    /// battle location opens its encounter preview; a Trader or Camp says it is coming soon.
    /// </summary>
    public sealed class MapViewModel
    {
        private readonly GameSession _session;

        public MapViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public RegionHeaderView Header { get; private set; }

        public MapLayout Layout { get; private set; }

        public List<MapNodeView> Nodes { get; } = new List<MapNodeView>();

        public List<MapPathView> Paths
        {
            get { return Layout?.Paths ?? new List<MapPathView>(); }
        }

        /// <summary>The map-world Y the view should centre on when shown: where the player stands, else the lowest reachable location.</summary>
        public float FocusY { get; private set; }

        /// <summary>Rebuilds everything from the save (after a battle, after loading).</summary>
        public void Refresh()
        {
            _session.EnsureExpedition();
            MapRun run = _session.Save?.Campaign?.ActiveRun;
            Nodes.Clear();
            if (run == null || string.IsNullOrEmpty(run.RegionId))
            {
                Layout = MapLayout.Of(new MapNode[0], 0);
                Header = new RegionHeaderView { Name = "No expedition" };
                return;
            }

            Layout = MapLayout.Of(run.Nodes, run.Seed);
            Header = BuildHeader(run);
            MapNode current = run.Find(run.CurrentNodeId);
            int currentLayer = current == null ? -1 : current.Layer;
            float focus = float.MaxValue;
            foreach (MapNode node in run.Nodes)
            {
                MapNodeState state = StateOf(run, node, currentLayer);
                MapNodeView view = new MapNodeView
                {
                    NodeId = node.NodeId,
                    Type = node.Type,
                    State = state,
                    Position = Layout.Positions[node.NodeId],
                    Radius = node.Type == MapNodeType.Boss || node.Type == MapNodeType.Gate ? MapLayout.BigNodeRadius : MapLayout.NodeRadius,
                    Name = _session.Content.LocationNames?.Resolve(node) ?? LocationNameTable.FallbackName(node.Kind),
                    Level = node.Level,
                    Layer = node.Layer,
                    KindLabel = KindLabel(node.Type)
                };
                Nodes.Add(view);
                if (state == MapNodeState.Current || (current == null && state == MapNodeState.Reachable))
                {
                    focus = Math.Min(focus, view.Position.Y);
                }
            }

            FocusY = focus == float.MaxValue ? Layout.Trailhead.Y : focus;
            if (current == null)
            {
                FocusY = (FocusY + Layout.Trailhead.Y) / 2f;
            }

            foreach (MapPathView path in Layout.Paths)
            {
                path.State = PathState(run, path);
            }
        }

        public MapNodeView Find(int nodeId)
        {
            return Nodes.Find(n => n.NodeId == nodeId);
        }

        /// <summary>The reachable locations, lowest first (tests, scripted walkthroughs).</summary>
        public List<MapNodeView> Reachable()
        {
            List<MapNodeView> reachable = Nodes.FindAll(n => n.State == MapNodeState.Reachable);
            reachable.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
            return reachable;
        }

        /// <summary>
        /// The map's "Next battle": the reachable battle location to fight next — the lowest level,
        /// a plain battle before a den on a tie, then the one furthest left — or null when none is
        /// reachable (only a Camp or Trader ahead).
        /// </summary>
        public MapNodeView Recommended()
        {
            MapNodeView best = null;
            foreach (MapNodeView node in Nodes)
            {
                if (node.State != MapNodeState.Reachable || node.Type == MapNodeType.Rest || node.Type == MapNodeType.Shop)
                {
                    continue;
                }

                if (best == null || Rank(node).CompareTo(Rank(best)) < 0)
                {
                    best = node;
                }
            }

            return best;
        }

        /// <summary>
        /// Where the results' Continue goes on to with auto-advance (<see cref="PlayerSettings.AutoAdvance"/>):
        /// after a win, the <see cref="Recommended"/> location's id; otherwise −1 (stop at the map).
        /// </summary>
        public int AutoAdvanceTarget(PlayerSettings settings, bool victory)
        {
            if (settings == null || !settings.AutoAdvance || !victory)
            {
                return -1;
            }

            return Recommended()?.NodeId ?? -1;
        }

        private static (int, int, float) Rank(MapNodeView node)
        {
            return (node.Level, node.Type == MapNodeType.Battle ? 0 : 1, node.Position.X);
        }

        /// <summary>What a tap on location <paramref name="nodeId"/> does. Changes nothing.</summary>
        public MapTapResult Tap(int nodeId)
        {
            MapNodeView node = Find(nodeId);
            if (node == null)
            {
                return new MapTapResult { Kind = MapTapKind.None };
            }

            switch (node.State)
            {
                case MapNodeState.Cleared:
                case MapNodeState.Current:
                    return new MapTapResult { Kind = MapTapKind.Refused, NodeId = nodeId, Message = node.Name + " is already cleared." };
                case MapNodeState.Bypassed:
                    return new MapTapResult { Kind = MapTapKind.Refused, NodeId = nodeId, Message = "That trail is behind you now." };
                case MapNodeState.Locked:
                    return new MapTapResult { Kind = MapTapKind.Refused, NodeId = nodeId, Message = "Clear the way to " + node.Name + " first." };
            }

            if (node.Type == MapNodeType.Shop)
            {
                return new MapTapResult { Kind = MapTapKind.ComingSoon, NodeId = nodeId, Message = "The trader's stall opens soon." };
            }

            if (node.Type == MapNodeType.Rest)
            {
                return new MapTapResult { Kind = MapTapKind.ComingSoon, NodeId = nodeId, Message = "Camp opens soon." };
            }

            return new MapTapResult { Kind = MapTapKind.Preview, NodeId = nodeId };
        }

        /// <summary>A location type's short label.</summary>
        public static string KindLabel(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Elite:
                    return "Den";
                case MapNodeType.Rest:
                    return "Camp";
                case MapNodeType.Shop:
                    return "Trader";
                case MapNodeType.Gate:
                    return "Pass";
                case MapNodeType.Boss:
                    return "Lair";
                default:
                    return "Wilds";
            }
        }

        private static MapNodeState StateOf(MapRun run, MapNode node, int currentLayer)
        {
            if (node.NodeId == run.CurrentNodeId)
            {
                return MapNodeState.Current;
            }

            if (run.IsCleared(node.NodeId))
            {
                return MapNodeState.Cleared;
            }

            if (CampaignRules.CanEnter(run, node.NodeId))
            {
                return MapNodeState.Reachable;
            }

            return node.Layer <= currentLayer ? MapNodeState.Bypassed : MapNodeState.Locked;
        }

        private static MapPathState PathState(MapRun run, MapPathView path)
        {
            bool toCleared = run.IsCleared(path.ToId);
            if (path.FromId < 0)
            {
                return toCleared ? MapPathState.Walked : run.CurrentNodeId < 0 ? MapPathState.Open : MapPathState.Faint;
            }

            if (run.IsCleared(path.FromId) && toCleared)
            {
                return MapPathState.Walked;
            }

            return path.FromId == run.CurrentNodeId && CampaignRules.CanEnter(run, path.ToId) ? MapPathState.Open : MapPathState.Faint;
        }

        private RegionHeaderView BuildHeader(MapRun run)
        {
            RegionData region = _session.Content.Campaign.GetRegion(run.RegionId);
            RegionProgress progress = _session.Save.Campaign.FindRegion(run.RegionId);
            SealData seal = region == null || string.IsNullOrEmpty(region.BossRewardSealId) ? null : _session.Content.Campaign.GetSeal(region.BossRewardSealId);
            int stages = Math.Max(1, region?.Stages ?? 1);
            int cleared = progress == null ? 0 : progress.BossCleared ? stages : Math.Min(stages, progress.StagesCleared);
            return new RegionHeaderView
            {
                RegionId = run.RegionId,
                Name = region?.DisplayName ?? run.RegionId,
                LevelBand = region == null ? string.Empty : "Lv " + region.MinLevel + "-" + region.MaxLevel,
                Stage = run.Stage,
                Stages = stages,
                StageText = "Stage " + (run.Stage + 1) + " of " + stages,
                StagesCleared = cleared,
                BossCleared = progress != null && progress.BossCleared,
                SealName = seal?.DisplayName,
                SealOwned = seal != null && _session.Save.Campaign.HasSeal(seal.SealId),
                SealProgress = cleared / (float)stages,
                BindingLimit = CampaignRules.BeastCap(_session.Save, _session.Content.Campaign)
            };
        }
    }
}
