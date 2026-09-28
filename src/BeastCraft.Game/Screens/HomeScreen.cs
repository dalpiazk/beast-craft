using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using Microsoft.Xna.Framework;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// Home: the bottom nav (<see cref="HomeViewModel"/>: Map, Roster, Camp, Avatar, Inventory) over
    /// the current tab. The Map tab is the region map (<see cref="MapViewModel"/>): a painted-style
    /// meadow (placeholder: a soft gradient with blobs) the finger drags up and down, the stage's
    /// locations placed on it (<see cref="MapLayout"/>) and joined by winding trails, each drawn by
    /// type and state, and the region header (name, level band, the seal's progress, the binding
    /// limit). Tapping a reachable location opens its encounter; a Trader or Camp says it is coming
    /// soon. The other tabs show a coming-soon page.
    /// </summary>
    public sealed class HomeScreen : GameScreen
    {
        private const float NavHeight = 170f;

        private readonly HomeViewModel _home = new HomeViewModel();
        private readonly MapViewModel _map;
        private readonly ScrollView _scroll;
        private readonly Tabs _tabs;
        private readonly Group _page;
        private readonly Button _gear;
        private readonly List<Hotspot> _spots = new List<Hotspot>();

        public HomeScreen(ScreenContext ctx) : base(ctx)
        {
            _map = new MapViewModel(ctx.Session);
            _scroll = Ui.Add(new ScrollView { Id = "map", Bounds = new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight) });
            _page = Ui.Add(new Group { Id = "page", Bounds = new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Visible = false });
            Ui.Add(new Panel { Id = "header", Bounds = HeaderBox, StyleKey = "header" });
            _gear = AddButton(null, "gear", new Rect(HeaderBox.Right - 120f, HeaderBox.Y + 24f, 96f, 96f), null, "ghost", OpenSettings, "gear");
            Ui.Add(new Panel { Id = "nav-panel", Bounds = NavBox, StyleKey = "nav" });
            _tabs = Ui.Add(new Tabs { Id = "nav", Bounds = NavBox.Inset(10f) });
            _tabs.Items.AddRange(HomeViewModel.TabNames);
            _tabs.Glyphs.AddRange(new[] { "map", "roster", "camp", "avatar", "inventory" });
            _tabs.Changed += index => SelectTab((HomeTab)index);
        }

        public override string Name
        {
            get { return "home"; }
        }

        private static Rect HeaderBox
        {
            get { return new Rect(24f, 24f, PortraitLayout.CanvasWidth - 48f, 250f); }
        }

        private static Rect NavBox
        {
            get { return new Rect(24f, PortraitLayout.CanvasHeight - NavHeight - 24f, PortraitLayout.CanvasWidth - 48f, NavHeight); }
        }

        public MapViewModel Map
        {
            get { return _map; }
        }

        public HomeTab Tab
        {
            get { return _home.Tab; }
        }

        public override void Enter()
        {
            _map.Refresh();
            _scroll.ContentHeight = _map.Layout.WorldHeight;
            _scroll.CenterOn(_map.FocusY, _map.FocusY);
            foreach (Hotspot spot in _spots)
            {
                _scroll.Remove(spot);
            }

            _spots.Clear();
            foreach (MapNodeView node in _map.Nodes)
            {
                float r = node.Radius + 16f;
                Hotspot spot = _scroll.Add(new Hotspot { Id = "node" + node.NodeId, Tag = node.NodeId, Bounds = new Rect(node.Position.X - r, node.Position.Y - r, 2f * r, 2f * r) });
                spot.Clicked += s => TapNode((int)s.Tag);
                _spots.Add(spot);
            }
        }

        public override bool HandleBack()
        {
            if (_home.HandleBack())
            {
                ShowTab();
                return true;
            }

            return false;
        }

        public void SelectTab(HomeTab tab)
        {
            _home.Select(tab);
            ShowTab();
        }

        public void OpenSettings()
        {
            Ctx.Stack.PushModal(new SettingsModal(Ctx, new SettingsViewModel(Ctx.Session.Settings, Ctx.Session.SaveSettings)));
        }

        /// <summary>A tap on location <paramref name="nodeId"/>: its encounter, or a toast.</summary>
        public void TapNode(int nodeId)
        {
            MapTapResult tap = _map.Tap(nodeId);
            switch (tap.Kind)
            {
                case MapTapKind.Preview:
                    Ctx.Stack.Push(new EncounterScreen(Ctx, nodeId));
                    break;
                case MapTapKind.ComingSoon:
                case MapTapKind.Refused:
                    Ctx.Game.Toast(tap.Message);
                    break;
            }
        }

        /// <summary>The first reachable battle location's encounter (scripted walkthroughs).</summary>
        public void OpenFirstEncounter()
        {
            MapNodeView node = _map.Reachable().Find(n => n.Type != MapNodeType.Rest && n.Type != MapNodeType.Shop);
            if (node == null)
            {
                throw new InvalidOperationException("No reachable battle on the map.");
            }

            TapNode(node.NodeId);
        }

        private void ShowTab()
        {
            _tabs.Selected = (int)_home.Tab;
            bool map = _home.TabAvailable;
            _scroll.Visible = map;
            _page.Visible = !map;
            Ui.Find("header").Visible = map;
            _gear.Visible = map;
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            if (!_home.TabAvailable)
            {
                Gradient("cream", "creamDeep", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            }

            base.Draw();
            if (_home.TabAvailable)
            {
                DrawHeader();
            }
            else
            {
                DrawComingSoon();
            }
        }

        protected override void DrawCustom(Widget widget)
        {
            if (widget == _scroll)
            {
                DrawMap();
            }
        }

        private void DrawMap()
        {
            MapLayout layout = _map.Layout;
            float top = _scroll.ScrollY - 40f;
            float bottom = _scroll.ScrollY + _scroll.Bounds.Height + 40f;
            float worldTop = Math.Min(0f, _scroll.ScrollY);
            float worldBottom = Math.Max(layout.WorldHeight, _scroll.ScrollY + _scroll.Bounds.Height);
            Gradient("map_top", "map_bottom", new Rect(0, worldTop, MapLayout.WorldWidth, worldBottom - worldTop));

            foreach (MapBlob blob in layout.Blobs)
            {
                if (blob.Center.Y + blob.Radius < top || blob.Center.Y - blob.Radius > bottom)
                {
                    continue;
                }

                switch (blob.Tone)
                {
                    case 0:
                        Painter.Soft(blob.Center, blob.Radius, Painter.C("map_meadow"), 0.7f);
                        break;
                    case 1:
                        Painter.Soft(blob.Center, blob.Radius, Painter.C("map_grass"), 0.6f);
                        break;
                    case 2:
                        Painter.Soft(blob.Center, blob.Radius * 1.15f, Painter.C("map_grass"), 0.55f);
                        Painter.Soft(blob.Center, blob.Radius, Painter.C("map_pond"), 0.5f);
                        Painter.Soft(new Vec2(blob.Center.X - blob.Radius * 0.2f, blob.Center.Y - blob.Radius * 0.12f), blob.Radius * 0.45f, Painter.C("white", 0.35f), 0.4f);
                        break;
                    default:
                        Painter.Soft(blob.Center, blob.Radius, Painter.C("map_tuft"));
                        break;
                }
            }

            foreach (MapPathView path in _map.Paths)
            {
                DrawTrail(path);
            }

            // The trailhead: a little flag where every expedition starts.
            Vec2 head = layout.Trailhead;
            Painter.Soft(head, 70f, Painter.C("trail", 0.9f), 0.45f);
            Painter.Glyph("flag", new Rect(head.X - 36f, head.Y - 70f, 72f, 72f), Painter.C("plum"));

            float pulse = 0.5f + 0.5f * (float)Math.Sin(Painter.TimeMs / 260f);
            foreach (MapNodeView node in _map.Nodes)
            {
                if (node.Position.Y + node.Radius < top || node.Position.Y - node.Radius > bottom)
                {
                    continue;
                }

                DrawNode(node, pulse);
            }

            DrawLabels(top, bottom);
        }

        private void DrawTrail(MapPathView path)
        {
            List<Vec2> points = path.Points;
            if (path.State == MapPathState.Walked)
            {
                Painter.Polyline(points, 26f, Painter.C("trail_edge"));
                Painter.Polyline(points, 18f, Painter.C("trail"));
                return;
            }

            bool open = path.State == MapPathState.Open;
            float spacing = open ? 30f : 34f;
            float radius = open ? 8f : 5f;
            Color color = open ? Painter.C("cream") : Painter.C("trail_edge", 0.55f);
            float carried = spacing / 2f;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vec2 a = points[i];
                Vec2 b = points[i + 1];
                float length = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                float at = carried;
                while (at < length)
                {
                    Vec2 dot = Vec2.Lerp(a, b, at / length);
                    if (open)
                    {
                        Painter.Disc(dot, radius + 3f, Painter.C("trail_edge"));
                    }

                    Painter.Disc(dot, radius, color);
                    at += spacing;
                }

                carried = at - length;
            }
        }

        private void DrawNode(MapNodeView node, float pulse)
        {
            Vec2 c = node.Position;
            float r = node.Radius;
            bool dim = node.State == MapNodeState.Locked || node.State == MapNodeState.Bypassed;
            float alpha = node.State == MapNodeState.Bypassed ? 0.55f : 1f;
            string typeKey = TypeKey(node.Type);

            // Shadow on the ground, then the reachable glow.
            Painter.Soft(new Vec2(c.X, c.Y + r * 0.75f), r * 1.1f, Painter.C("plumDeep", 0.35f * alpha), 0.3f);
            if (node.State == MapNodeState.Reachable)
            {
                Painter.Soft(c, r * (1.45f + 0.15f * pulse), Painter.C("gold", 0.55f + 0.25f * pulse));
            }

            if (node.State == MapNodeState.Current)
            {
                Painter.Disc(c, r + 12f, Painter.C("gold"));
            }

            Painter.Disc(c, r, Painter.C("plum", alpha));
            Painter.Disc(c, r - 6f, dim ? Painter.C("stone", alpha) : Painter.C(typeKey, alpha));
            Painter.Disc(new Vec2(c.X - r * 0.25f, c.Y - r * 0.3f), r * 0.35f, Painter.C("white", dim ? 0.12f : 0.22f));
            float g = r * 1.05f;
            Painter.Glyph(Glyph(node.Type), new Rect(c.X - g / 2f, c.Y - g / 2f, g, g), dim ? Painter.C("inkSoft", alpha) : Painter.C("cream"));

            if (node.State == MapNodeState.Cleared || node.State == MapNodeState.Current)
            {
                Vec2 badge = new Vec2(c.X + r * 0.72f, c.Y - r * 0.72f);
                Painter.Disc(badge, 22f, Painter.C("plum"));
                Painter.Disc(badge, 18f, Painter.C("leaf"));
                Painter.Glyph("check", new Rect(badge.X - 15f, badge.Y - 15f, 30f, 30f), Painter.C("white"));
            }
            else if (node.State == MapNodeState.Locked)
            {
                Vec2 badge = new Vec2(c.X + r * 0.72f, c.Y - r * 0.72f);
                Painter.Disc(badge, 20f, Painter.C("plum", 0.85f));
                Painter.Glyph("lock", new Rect(badge.X - 14f, badge.Y - 15f, 28f, 28f), Painter.C("cream"));
            }

        }

        /// <summary>
        /// Name tags (name and level) under the reachable, current, pass and lair locations; a tag
        /// that would overlap one already placed goes above its location instead, or is left out.
        /// </summary>
        private void DrawLabels(float top, float bottom)
        {
            float size = Ctx.Style.TextSizes.Small + 2f;
            List<Rect> placed = new List<Rect>();
            List<MapNodeView> labelled = _map.Nodes.FindAll(n => n.State == MapNodeState.Reachable || n.State == MapNodeState.Current || n.Type == MapNodeType.Boss ||
                                                                 n.Type == MapNodeType.Gate);
            labelled.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
            foreach (MapNodeView node in labelled)
            {
                Vec2 c = node.Position;
                if (c.Y + node.Radius + 80f < top || c.Y - node.Radius - 80f > bottom)
                {
                    continue;
                }

                string text = node.Name + (node.Type == MapNodeType.Rest || node.Type == MapNodeType.Shop ? string.Empty : "  Lv " + node.Level.ToString(CultureInfo.InvariantCulture));
                float width = Math.Min(460f, Ctx.Text.Measure(text, size) + 40f);
                Rect below = Clamp(new Rect(c.X - width / 2f, c.Y + node.Radius + 14f, width, 44f));
                Rect above = Clamp(new Rect(c.X - width / 2f, c.Y - node.Radius - 58f, width, 44f));
                Rect? tag = !Overlaps(placed, below) ? below : !Overlaps(placed, above) ? above : (Rect?)null;
                if (!tag.HasValue)
                {
                    continue;
                }

                placed.Add(tag.Value);
                Painter.Framed(tag.Value, 22f, 3f, Painter.C("plum"), Painter.C("cream", 0.95f));
                Painter.TextIn(text, tag.Value.Inset(12f), size, Painter.C("ink"), TextAlign.Center);
            }
        }

        private static Rect Clamp(Rect tag)
        {
            float x = Math.Max(12f, Math.Min(PortraitLayout.CanvasWidth - 12f - tag.Width, tag.X));
            return new Rect(x, tag.Y, tag.Width, tag.Height);
        }

        private static bool Overlaps(List<Rect> placed, Rect tag)
        {
            foreach (Rect other in placed)
            {
                if (tag.X < other.Right + 8f && other.X < tag.Right + 8f && tag.Y < other.Bottom + 4f && other.Y < tag.Bottom + 4f)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawHeader()
        {
            RegionHeaderView header = _map.Header;
            Rect box = HeaderBox;
            float x = box.X + 36f;
            Painter.TextIn(header.Name, new Rect(x, box.Y + 30f, box.Width - 200f, Ctx.Style.TextSizes.Heading), Ctx.Style.TextSizes.Heading, Painter.C("plum"), TextAlign.Left);
            string line = header.LevelBand + "   " + header.StageText;
            Painter.TextIn(line, new Rect(x, box.Y + 88f, box.Width - 200f, 30f), Ctx.Style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left);

            // The seal: its stone, its name, the stages toward it.
            Rect seal = new Rect(x, box.Y + 140f, 76f, 76f);
            Painter.Disc(seal.Center, 38f, Painter.C("plum"));
            Painter.Glyph("seal", seal.Inset(6f), header.SealOwned ? Painter.C("gold") : Painter.C("moss"));
            string sealName = header.SealName ?? "No seal";
            Painter.TextIn(sealName, new Rect(seal.Right + 18f, box.Y + 144f, 380f, 28f), Ctx.Style.TextSizes.Small + 3f, Painter.C("ink"), TextAlign.Left);
            string progress = header.SealOwned ? "Claimed" : header.StagesCleared + " / " + header.Stages + " stages";
            Painter.Progress(new Rect(seal.Right + 18f, box.Y + 184f, 380f, 30f), header.SealProgress, -1f, "gold", "gold", "track", progress);

            // The binding limit (the beast level cap the seals give).
            Rect limit = new Rect(box.Right - 330f, box.Y + 146f, 300f, 76f);
            Painter.Framed(limit, 30f, 4f, Painter.C("plumSoft"), Painter.C("creamDeep"));
            Painter.TextIn("Binding limit", new Rect(limit.X, limit.Y + 8f, limit.Width, 22f), Ctx.Style.TextSizes.Small, Painter.C("inkSoft"), TextAlign.Center, false);
            Painter.TextIn("Lv " + header.BindingLimit.ToString(CultureInfo.InvariantCulture), new Rect(limit.X, limit.Y + 38f, limit.Width, 30f), Ctx.Style.TextSizes.Body + 4f,
                           Painter.C("plum"), TextAlign.Center, false);

            // Gold, beside the gear.
            string gold = Ctx.Session.Save.Gold.ToString(CultureInfo.InvariantCulture);
            Rect coin = new Rect(box.Right - 330f, box.Y + 44f, 48f, 48f);
            Painter.Glyph("coin", coin, Painter.C("goldDeep"));
            Painter.TextIn(gold, new Rect(coin.Right + 10f, coin.Y, 150f, coin.Height), Ctx.Style.TextSizes.Body + 2f, Painter.C("ink"), TextAlign.Left);
        }

        private void DrawComingSoon()
        {
            Rect card = new Rect(90f, 420f, 900f, 900f);
            Painter.Panel(card, Ctx.Style.Panel("panel"));
            Vec2 c = new Vec2(card.Center.X, card.Y + 250f);
            Painter.Disc(c, 150f, Painter.C("plum"));
            Painter.Disc(c, 140f, Painter.C("gold"));
            string[] glyphs = { "map", "roster", "camp", "avatar", "inventory" };
            Painter.Glyph(glyphs[(int)_home.Tab], new Rect(c.X - 100f, c.Y - 100f, 200f, 200f), Painter.C("plum"));
            Painter.TextIn(_home.TabName, new Rect(card.X, card.Y + 450f, card.Width, 60f), Ctx.Style.TextSizes.Heading + 8f, Painter.C("plum"), TextAlign.Center);
            Rect badge = new Rect(card.Center.X - 160f, card.Y + 540f, 320f, 64f);
            Painter.Framed(badge, 32f, 4f, Painter.C("plum"), Painter.C("peach"));
            Painter.TextIn("Coming soon", badge, Ctx.Style.TextSizes.Body, Painter.C("white"), TextAlign.Center);
            float y = card.Y + 660f;
            foreach (string line in Painter.Wrap(_home.ComingSoon, Ctx.Style.TextSizes.Body, card.Width - 160f))
            {
                Painter.TextIn(line, new Rect(card.X + 80f, y, card.Width - 160f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Center);
                y += Ctx.Text.LineHeight(Ctx.Style.TextSizes.Body);
            }
        }

        /// <summary>A location type's colour key.</summary>
        public static string TypeKey(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Elite:
                    return "node_elite";
                case MapNodeType.Gate:
                    return "node_gate";
                case MapNodeType.Boss:
                    return "node_boss";
                case MapNodeType.Shop:
                    return "node_shop";
                case MapNodeType.Rest:
                    return "node_camp";
                default:
                    return "node_battle";
            }
        }

        /// <summary>A location type's glyph.</summary>
        public static string Glyph(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Elite:
                    return "elite";
                case MapNodeType.Gate:
                    return "gate";
                case MapNodeType.Boss:
                    return "boss";
                case MapNodeType.Shop:
                    return "shop";
                case MapNodeType.Rest:
                    return "camp";
                default:
                    return "battle";
            }
        }
    }
}
