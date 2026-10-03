using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
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
    /// Home: the bottom nav (<see cref="HomeViewModel"/>: Map, Roster, Grove, Avatar, Inventory) over
    /// the current tab. The Map tab is the region map (<see cref="MapViewModel"/>): a painted-style
    /// meadow (placeholder: a soft gradient with blobs) the finger drags up and down, the stage's
    /// locations placed on it (<see cref="MapLayout"/>) and joined by winding trails, each drawn by
    /// type and state, and the region header (name, level band, the seal's progress, the binding
    /// limit). Tapping a reachable location opens its encounter; a Camp opens the camp; a Trader
    /// opens the shop (<see cref="ShopScreen"/>). Two one-tap shortcuts sit on the map: the idle chip
    /// (how long the idle rewards have piled up; tap to claim) and Next battle (the recommended
    /// location's encounter, the last team already picked). Coming back to the app claims the idle
    /// rewards here. The Roster tab is the roster page (<see cref="RosterPage"/>); Grove, Avatar and
    /// Inventory each push their own full screen.
    /// <para>
    /// <strong>The discovery layer</strong> (a discovery region's map): soft painted fog over the ground
    /// not yet seen (locations under it are not drawn), the points of interest seen as badges (tap one
    /// for its popup, <see cref="PoiModal"/>; a Kinship site opens its trial's preview), the region's
    /// "Explored" percentage in the header (tap it for the region progress panel,
    /// <see cref="RegionProgressModal"/>), a won trial's choice offered before anything else
    /// (<see cref="TrialPickModal.Kinship"/>), and the 100% reward's toast.
    /// </para>
    /// </summary>
    public sealed class HomeScreen : GameScreen
    {
        private const float NavHeight = 170f;

        private readonly HomeViewModel _home = new HomeViewModel();
        private readonly MapViewModel _map;
        private readonly ScrollView _scroll;
        private readonly Tabs _tabs;
        private readonly Group _page;
        private readonly RosterPage _roster;
        private readonly Button _gear;
        private readonly Button _idle;
        private readonly Button _next;
        private readonly Button _explored;
        private readonly Button _difficulty;
        private readonly List<Hotspot> _spots = new List<Hotspot>();
        private readonly TabFade _tabFade = new TabFade();
        private float _idleRefreshMs;

        public HomeScreen(ScreenContext ctx) : base(ctx)
        {
            _map = new MapViewModel(ctx.Session);
            _scroll = Ui.Add(new ScrollView { Id = "map", Bounds = new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight) });
            _page = Ui.Add(new Group { Id = "page", Bounds = new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Visible = false });
            _roster = new RosterPage(ctx, Ui, new Rect(0, 0, PortraitLayout.CanvasWidth, NavBox.Y - 20f));
            _roster.Root.Visible = false;
            Ui.Add(new Panel { Id = "header", Bounds = HeaderBox, StyleKey = "header" });
            _gear = AddButton(null, "gear", new Rect(HeaderBox.Right - 120f, HeaderBox.Y + 24f, 96f, 96f), null, "ghost", OpenSettings, "gear");
            _idle = AddButton(null, "idle", new Rect(HeaderBox.Right - 420f, HeaderBox.Bottom + 18f, 420f, 84f), Loc("ui.home.idle"), "chip", ClaimIdle, "hourglass");
            _explored = AddButton(null, "explored", new Rect(HeaderBox.X, HeaderBox.Bottom + 18f, 400f, 84f), Loc("ui.home.explored"), "chip", OpenRegionProgress, "map");
            _difficulty = AddButton(null, "difficulty", new Rect(HeaderBox.X, HeaderBox.Bottom + 18f, 400f, 84f), Loc("ui.home.normal"), "chip", AskDifficulty, "battle");
            _difficulty.Visible = false;
            _next = AddButton(null, "next-battle", new Rect(NavBox.X + 90f, NavBox.Y - 150f, NavBox.Width - 180f, 124f), Loc("ui.home.next_battle"), "primary", OpenRecommended,
                              "battle");
            if (!UiKit.Enabled)
            {
                // The kit's painted pills float directly over the page, as the tab strips already do
                // (TabStrip.Build) -- the dark plum bar is the code-drawn look's own device for
                // unselected cream text contrast, which the kit's parchment pills do not need.
                Ui.Add(new Panel { Id = "nav-panel", Bounds = NavBox, StyleKey = "nav" });
            }

            _tabs = Ui.Add(new Tabs { Id = "nav", Bounds = NavBox.Inset(10f) });
            _tabs.Items.AddRange(Array.ConvertAll(HomeViewModel.TabKeys, key => Loc(key)));
            _tabs.Glyphs.AddRange(new[] { "map", "roster", "grove", "avatar", "inventory" });
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
                if (node.Hidden)
                {
                    continue;
                }

                float r = node.Radius + 16f;
                Hotspot spot = _scroll.Add(new Hotspot { Id = "node" + node.NodeId, Tag = node.NodeId, Bounds = new Rect(node.Position.X - r, node.Position.Y - r, 2f * r, 2f * r) });
                spot.Clicked += s => TapNode((int)s.Tag);
                _spots.Add(spot);
            }

            foreach (PoiView poi in _map.Pois)
            {
                float r = poi.Radius + 18f;
                Hotspot spot = _scroll.Add(new Hotspot { Id = "poi-" + poi.PoiId, Tag = poi.PoiId, Bounds = new Rect(poi.Position.X - r, poi.Position.Y - r, 2f * r, 2f * r) });
                spot.Clicked += s => TapPoi((string)s.Tag);
                _spots.Add(spot);
            }

            _explored.Text = _map.Header.CompletionText;
            _explored.Selected = _map.Header.CompletionRewarded;
            _difficulty.Text = Loc(_map.Header.IsHard ? "ui.home.hard" : "ui.home.normal");
            _difficulty.Selected = _map.Header.IsHard;
            _difficulty.Bounds = new Rect(HeaderBox.X, HeaderBox.Bottom + (_map.Header.CompletionPercent >= 0 ? 120f : 18f), 400f, 84f);
            Ctx.Session.RefreshGrove();
            foreach (string toast in Ctx.Session.PendingToasts)
            {
                Ctx.Game.Toast(toast);
            }

            Ctx.Session.PendingToasts.Clear();

            MapNodeView next = _map.Recommended();
            _next.Tag = next?.NodeId;
            _next.Text = next == null ? Loc("ui.home.next_battle") : Loc("ui.home.next_named", next.Name);
            RefreshIdle();
            ShowTab();
            OfferPendingPick();
        }

        /// <summary>
        /// A beast waiting to join (a won trial's pick, or a save with no beast at all) is offered before
        /// anything else; otherwise the map's hints.
        /// </summary>
        private void OfferPendingPick()
        {
            int pending = Ctx.Session.PendingPick;
            if (pending == 1)
            {
                Ctx.Stack.Push(new StarterPickScreen(Ctx));
                return;
            }

            if (pending > 1)
            {
                if (!Ctx.Stack.IsOpen("trial-pick"))
                {
                    Ctx.Stack.PushModal(new TrialPickModal(Ctx, Enter));
                    ShowHints(BeastCraft.Tutorial.HintTriggers.PickOpen, -1, pending);
                }

                return;
            }

            if (Ctx.Session.PendingKinship)
            {
                if (!Ctx.Stack.IsOpen("kinship-pick"))
                {
                    Ctx.Stack.PushModal(TrialPickModal.Kinship(Ctx, Enter));
                }

                return;
            }

            if (OfferConsent())
            {
                return;
            }

            ShowHints(BeastCraft.Tutorial.HintTriggers.MapOpen);
        }

        /// <summary>
        /// The one-time consent screen (#62), once the first-run picks are done and before play: until the player
        /// answers it. Not in scripted screenshots or the walkthrough (their own <c>--screen consent</c> shows it).
        /// </summary>
        private bool OfferConsent()
        {
            bool scripted = Ctx.Options.Screenshot || !string.IsNullOrEmpty(Ctx.Options.WalkthroughDir);
            if (scripted || !ConsentViewModel.ShouldAsk(Ctx.Session.Settings) || Ctx.Stack.IsOpen("consent"))
            {
                return false;
            }

            Ctx.Stack.PushModal(new ConsentModal(Ctx, new ConsentViewModel(Ctx.Session), () => ShowHints(BeastCraft.Tutorial.HintTriggers.MapOpen)));
            return true;
        }

        public override void Update(float elapsedMs, FrameInput input)
        {
            base.Update(elapsedMs, input);
            _tabFade.Update(elapsedMs);
            if (Ctx.Session.ResumeClaimPending)
            {
                ClaimIdle(true);
            }

            _idleRefreshMs -= elapsedMs;
            if (_idleRefreshMs <= 0f)
            {
                RefreshIdle();
            }
        }

        /// <summary>The idle chip's tap (and the claim on coming back): claim, and say what it paid.</summary>
        public void ClaimIdle()
        {
            ClaimIdle(false);
        }

        private void ClaimIdle(bool quiet)
        {
            IdleClaimView claim = Ctx.Session.ClaimIdle();
            if (claim?.Message != null)
            {
                Ctx.Game.Toast(claim.Message);
            }
            else if (!quiet)
            {
                Ctx.Game.Toast(Loc(Ctx.Session.IdleStatus().Started ? "ui.idle.nothing_yet" : "ui.idle.clock_started"));
            }

            RefreshIdle();
        }

        /// <summary>Next battle: the recommended location's encounter, the last team already picked.</summary>
        public void OpenRecommended()
        {
            MapNodeView next = _map.Recommended();
            if (next == null)
            {
                Ctx.Game.Toast(Loc("ui.home.no_battle"));
                return;
            }

            TapNode(next.NodeId);
        }

        /// <summary>After results: with auto-advance on and a win, straight on to the next encounter.</summary>
        public bool AutoAdvance(bool victory)
        {
            int target = _map.AutoAdvanceTarget(Ctx.Session.Settings, victory);
            if (target < 0)
            {
                return false;
            }

            TapNode(target);
            return true;
        }

        private void RefreshIdle()
        {
            _idleRefreshMs = 1000f;
            IdleStatusView status = Ctx.Session.IdleStatus();
            _idle.Text = status.Text;
            _idle.Selected = status.Capped;
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

        /// <summary>
        /// Selects bottom-nav tab <paramref name="tab"/>. Grove is not shown inline: it pushes the full
        /// Grove hub screen (<see cref="GroveScreen"/>, its own inner tabs Glade/Garden/Board/Npc), so
        /// <see cref="HomeViewModel.Tab"/> is left on whatever it already was — the nav bar keeps
        /// showing that tab selected underneath, exactly as the Roster tab's "Compendium" chip pushes a
        /// screen without changing the Roster tab's own selection.
        /// </summary>
        public void SelectTab(HomeTab tab)
        {
            if (tab == HomeTab.Grove)
            {
                Ctx.Stack.Push(new GroveScreen(Ctx));
                return;
            }

            if (tab == HomeTab.Avatar)
            {
                Ctx.Stack.Push(new AvatarScreen(Ctx));
                return;
            }

            if (tab == HomeTab.Inventory)
            {
                Ctx.Stack.Push(new InventoryScreen(Ctx));
                return;
            }

            _home.Select(tab);
            ShowTab();
        }

        public void OpenSettings()
        {
            Ctx.Stack.Push(new SettingsScreen(Ctx));
        }

        /// <summary>A tap on location <paramref name="nodeId"/>: its encounter, or a toast.</summary>
        public void TapNode(int nodeId)
        {
            TapNode(nodeId, false);
        }

        private void TapNode(int nodeId, bool leaveConfirmed)
        {
            MapTapResult tap = _map.Tap(nodeId, leaveConfirmed);
            switch (tap.Kind)
            {
                case MapTapKind.ConfirmLeave:
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.home.leave_kinship"), tap.Message, Loc("ui.home.stay"), Loc("ui.home.go_on"), () => TapNode(nodeId, true)));
                    break;
                case MapTapKind.KinshipChoice:
                    Ctx.Game.Toast(tap.Message);
                    OfferPendingPick();
                    break;
                case MapTapKind.Preview:
                    Ctx.Stack.Push(new EncounterScreen(Ctx, nodeId));
                    break;
                case MapTapKind.Story:
                    OpenStory(nodeId);
                    break;
                case MapTapKind.Camp:
                    OpenCamp(nodeId);
                    break;
                case MapTapKind.Shop:
                    Ctx.Stack.Push(new ShopScreen(Ctx, nodeId));
                    break;
                case MapTapKind.Pick:
                    Ctx.Game.Toast(tap.Message);
                    OfferPendingPick();
                    break;
                case MapTapKind.ComingSoon:
                case MapTapKind.Refused:
                    Ctx.Game.Toast(tap.Message);
                    break;
            }
        }

        /// <summary>A tap on point of interest <paramref name="poiId"/>: its popup, or (a Kinship site) its trial's preview.</summary>
        public void TapPoi(string poiId)
        {
            MapTapResult tap = _map.TapPoi(poiId);
            switch (tap.Kind)
            {
                case MapTapKind.Poi:
                    Ctx.Stack.PushModal(new PoiModal(Ctx, new PoiViewModel(Ctx.Session, poiId), Enter));
                    break;
                case MapTapKind.KinshipTrial:
                    Ctx.Stack.Push(new EncounterScreen(Ctx, EncounterViewModel.ForKinship(Ctx.Session, poiId)));
                    break;
                case MapTapKind.Pick:
                case MapTapKind.KinshipChoice:
                    Ctx.Game.Toast(tap.Message);
                    OfferPendingPick();
                    break;
            }
        }

        /// <summary>The region progress panel (the header's "Explored" chip).</summary>
        public void OpenRegionProgress()
        {
            if (_map.Header.CompletionPercent < 0)
            {
                return;
            }

            Ctx.Stack.PushModal(new RegionProgressModal(Ctx, new RegionProgressViewModel(Ctx.Session), Enter));
        }

        /// <summary>
        /// The header's Normal or Hard chip (post-game regions only): asks, then plays the stage on the map
        /// again on the other difficulty, on a new map.
        /// </summary>
        public void AskDifficulty()
        {
            if (!_map.Header.HardAvailable)
            {
                return;
            }

            RunDifficulty target = _map.Header.IsHard ? RunDifficulty.Normal : RunDifficulty.Hard;
            bool hard = target == RunDifficulty.Hard;
            string title = hard ? Loc("ui.home.to_hard_title") : Loc("ui.home.to_normal_title");
            string body = hard ? Loc("ui.home.to_hard_body", _map.Header.Stage + 1) : Loc("ui.home.to_normal_body", _map.Header.Stage + 1);
            string confirm = hard ? Loc("ui.home.to_hard") : Loc("ui.home.to_normal");
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, title, body, Loc("ui.home.stay"), confirm, () =>
            {
                if (!_map.SwitchDifficulty(target))
                {
                    Ctx.Game.Toast(Loc("ui.home.difficulty_refused"));
                    return;
                }

                Enter();
            }));
        }

        /// <summary>A story location: the mentor's scene, then the visit (gifts; at Hearthglen's end, the way on).</summary>
        public void OpenStory(int nodeId)
        {
            MapRun run = Ctx.Session.Save.Campaign.ActiveRun;
            FixedNodeData authored = Ctx.Content.Campaign.FixedNode(run.RegionId, nodeId);
            StoryViewModel story = new StoryViewModel(Ctx.Session, nodeId, authored?.SceneId);
            Ctx.Stack.PushModal(new StoryModal(Ctx, story, true, result => AfterStory(result)));
        }

        private void AfterStory(CampaignResult result)
        {
            if (result == null || !result.Success)
            {
                Ctx.Game.Toast(result?.Error ?? Loc("ui.home.nothing_happens"));
                return;
            }

            string gifts = StoryViewModel.GiftText(Ctx.Session, result);
            if (gifts.Length > 0)
            {
                Ctx.Game.Toast(Loc("ui.home.keeper_gave", gifts));
            }

            if (result.Outcome == CampaignOutcome.TutorialCleared)
            {
                RegionData next = Ctx.Content.Campaign.GetRegion(CampaignProgress.StartingRegionId);
                Ctx.Stack.PushModal(new RegionCardModal(Ctx, next, Enter));
                return;
            }

            Enter();
            ShowHints(BeastCraft.Tutorial.HintTriggers.StoryDone, nodeId: result.Node?.NodeId ?? -1);
        }

        /// <summary>A camp: its scene (if any), then the camp itself (train a beast; Hearthglen's catch-up).</summary>
        public void OpenCamp(int nodeId)
        {
            CampViewModel camp = new CampViewModel(Ctx.Session, nodeId);
            void OpenIt()
            {
                Ctx.Stack.PushModal(new CampModal(Ctx, camp, Enter));
                ShowHints(BeastCraft.Tutorial.HintTriggers.CampOpen, nodeId);
            }

            if (!string.IsNullOrEmpty(camp.SceneId))
            {
                Ctx.Stack.PushModal(new StoryModal(Ctx, new StoryViewModel(Ctx.Session, nodeId, camp.SceneId), false, _ => OpenIt()));
                return;
            }

            OpenIt();
        }

        /// <summary>The first reachable battle location's encounter (scripted walkthroughs).</summary>
        public void OpenFirstEncounter()
        {
            MapNodeView node = _map.Reachable().Find(n => n.Type != MapNodeType.Rest && n.Type != MapNodeType.Shop && n.Type != MapNodeType.Story);
            if (node == null)
            {
                throw new InvalidOperationException("No reachable battle on the map.");
            }

            TapNode(node.NodeId);
        }

        /// <summary>The Roster tab's page.</summary>
        public RosterPage Roster
        {
            get { return _roster; }
        }

        private void ShowTab()
        {
            _tabFade.Reset();
            _tabs.Selected = (int)_home.Tab;
            bool map = _home.Tab == HomeTab.Map;
            bool roster = _home.Tab == HomeTab.Roster;
            _scroll.Visible = map;
            _page.Visible = !_home.TabAvailable;
            _roster.Root.Visible = roster;
            if (roster)
            {
                _roster.Refresh();
            }

            Ui.Find("header").Visible = map;
            _gear.Visible = map;
            _idle.Visible = map;
            _explored.Visible = map && _map.Header.CompletionPercent >= 0;
            _difficulty.Visible = map && _map.Header.HardAvailable;
            _next.Visible = map && _next.Tag != null;
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            if (_home.Tab != HomeTab.Map)
            {
                PageBackground("cream", "creamDeep");
            }

            base.Draw();
            if (_home.Tab == HomeTab.Map)
            {
                DrawHeader();
            }
            else if (_home.Tab == HomeTab.Roster)
            {
                _roster.DrawHeader();
            }
            else
            {
                DrawComingSoon();
            }

            if (_home.Tab != HomeTab.Map)
            {
                float fade = _tabFade.Alpha(AnimationsEnabled);
                if (fade > 0f)
                {
                    Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Painter.C("cream", fade));
                }
            }
        }

        protected override void DrawCustom(Widget widget)
        {
            if (widget == _scroll)
            {
                DrawMap();
                return;
            }

            if (_roster.Root.Visible)
            {
                _roster.DrawCustom(widget);
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

            float pulse = 0.5f + 0.5f * (float)Math.Sin(Painter.TimeMs / 260f);
            DrawFog(top, bottom);

            // The trailhead: a little flag where every expedition starts.
            Vec2 head = layout.Trailhead;
            Painter.Soft(head, 70f, Painter.C("trail", 0.9f), 0.45f);
            Painter.Glyph("flag", new Rect(head.X - 36f, head.Y - 70f, 72f, 72f), Painter.C("plum"));

            foreach (MapNodeView node in _map.Nodes)
            {
                if (node.Hidden || node.Position.Y + node.Radius < top || node.Position.Y - node.Radius > bottom)
                {
                    continue;
                }

                DrawNode(node, pulse);
            }

            foreach (PoiView poi in _map.Pois)
            {
                if (poi.Position.Y + poi.Radius * 2f < top || poi.Position.Y - poi.Radius * 2f > bottom)
                {
                    continue;
                }

                DiscoveryArt.Marker(Ctx, poi.Kind, poi.Position, poi.Radius, poi.State == PoiState.Found, pulse);
            }

            DrawLabels(top, bottom);
        }

        /// <summary>
        /// The fog (placeholder painting until the art lands): over every cell not yet seen, a soft
        /// cloud — a shaded underlayer, a pale body and a light top — sized to overlap its neighbours
        /// so the edge of the seen ground reads as a soft, painted bank of mist.
        /// </summary>
        private void DrawFog(float top, float bottom)
        {
            if (!_map.HasFog)
            {
                return;
            }

            float r = Math.Max(_map.FogCellSize.X, _map.FogCellSize.Y) * 1.25f;
            foreach (Vec2 cell in _map.FogCells)
            {
                if (cell.Y + r < top || cell.Y - r > bottom)
                {
                    continue;
                }

                Painter.Soft(new Vec2(cell.X, cell.Y + r * 0.18f), r * 1.1f, Painter.C("map_fog_shade"), 0.45f);
            }

            foreach (Vec2 cell in _map.FogCells)
            {
                if (cell.Y + r < top || cell.Y - r > bottom)
                {
                    continue;
                }

                Painter.Soft(cell, r, Painter.C("map_fog"), 0.55f);
                float wobble = (float)Math.Sin((cell.X * 0.013f) + (cell.Y * 0.021f));
                Painter.Soft(new Vec2(cell.X - r * 0.22f * wobble, cell.Y - r * 0.25f), r * 0.55f, Painter.C("white", 0.35f), 0.4f);
            }
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
            List<MapNodeView> labelled = _map.Nodes.FindAll(n => !n.Hidden && (n.State == MapNodeState.Reachable || n.State == MapNodeState.Current || n.Type == MapNodeType.Boss ||
                                                                                n.Type == MapNodeType.Gate));
            labelled.Sort((a, b) => a.Position.X.CompareTo(b.Position.X));
            foreach (MapNodeView node in labelled)
            {
                Vec2 c = node.Position;
                if (c.Y + node.Radius + 80f < top || c.Y - node.Radius - 80f > bottom)
                {
                    continue;
                }

                string text = node.Type == MapNodeType.Rest || node.Type == MapNodeType.Shop || node.Type == MapNodeType.Story ? node.Name : Loc("ui.home.node_level", node.Name, node.Level);
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

            // Points of interest waiting to be visited: their names, where they fit.
            foreach (PoiView poi in _map.Pois)
            {
                Vec2 c = poi.Position;
                if (poi.State != PoiState.Revealed || c.Y + poi.Radius + 80f < top || c.Y - poi.Radius - 80f > bottom)
                {
                    continue;
                }

                string text = poi.Kind == PoiKind.KinshipSite ? Loc("ui.home.node_level", poi.Name, poi.Level) : poi.Name;
                float width = Math.Min(460f, Ctx.Text.Measure(text, size) + 40f);
                Rect below = Clamp(new Rect(c.X - width / 2f, c.Y + poi.Radius + 12f, width, 44f));
                if (Overlaps(placed, below))
                {
                    continue;
                }

                placed.Add(below);
                Painter.Framed(below, 22f, 3f, Painter.C(DiscoveryArt.ColorKey(poi.Kind)), Painter.C("cream", 0.95f));
                Painter.TextIn(text, below.Inset(12f), size, Painter.C("ink"), TextAlign.Center);
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
            float lineX = x;
            if (header.IsHard)
            {
                // The Hard badge, ahead of the level and stage.
                Painter.Badge(new Rect(x, box.Y + 84f, 110f, 38f), Loc("ui.home.hard"), Ctx.Style.TextSizes.Small + 2f);
                lineX = x + 126f;
            }

            Painter.TextIn(line, new Rect(lineX, box.Y + 88f, box.Width - 200f - (lineX - x), 30f), Ctx.Style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left);

            // The seal: its stone, its name, the stages toward it.
            Rect seal = new Rect(x, box.Y + 140f, 76f, 76f);
            Painter.Disc(seal.Center, 38f, Painter.C("plum"));
            Painter.Glyph("seal", seal.Inset(6f), header.SealOwned ? Painter.C("gold") : Painter.C("moss"));
            string sealName = header.SealName ?? Loc("ui.home.no_seal");
            Painter.TextIn(sealName, new Rect(seal.Right + 18f, box.Y + 144f, 380f, 28f), Ctx.Style.TextSizes.Small + 3f, Painter.C("ink"), TextAlign.Left);
            string progress = header.IsTutorial ? Loc("ui.home.tutorial_progress", header.StagesCleared, header.Stages) : header.SealOwned ? Loc("ui.home.claimed") : Loc("ui.home.stage_progress", header.StagesCleared, header.Stages);
            Painter.Progress(new Rect(seal.Right + 18f, box.Y + 184f, 380f, 30f), header.SealProgress, -1f, "gold", "gold", "track", progress);

            // The binding limit (the beast level cap the seals give).
            Rect limit = new Rect(box.Right - 330f, box.Y + 146f, 300f, 76f);
            Painter.Framed(limit, 30f, 4f, Painter.C("plumSoft"), Painter.C("creamDeep"));
            Painter.TextIn(Loc("ui.home.binding_limit"), new Rect(limit.X, limit.Y + 8f, limit.Width, 22f), Ctx.Style.TextSizes.Small, Painter.C("inkSoft"), TextAlign.Center, false);
            Painter.TextIn(Loc("ui.common.level", header.BindingLimit), new Rect(limit.X, limit.Y + 38f, limit.Width, 30f), Ctx.Style.TextSizes.Body + 4f,
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
            string[] glyphs = { "map", "roster", "grove", "avatar", "inventory" };
            Painter.Glyph(glyphs[(int)_home.Tab], new Rect(c.X - 100f, c.Y - 100f, 200f, 200f), Painter.C("plum"));
            Painter.TextIn(Loc(_home.TabNameKey), new Rect(card.X, card.Y + 450f, card.Width, 60f), Ctx.Style.TextSizes.Heading + 8f, Painter.C("plum"), TextAlign.Center);
            Rect badge = new Rect(card.Center.X - 160f, card.Y + 540f, 320f, 64f);
            Painter.Framed(badge, 32f, 4f, Painter.C("plum"), Painter.C("peach"));
            Painter.TextIn(Loc("ui.home.coming_soon"), badge, Ctx.Style.TextSizes.Body, Painter.C("white"), TextAlign.Center);
            float y = card.Y + 660f;
            foreach (string line in Painter.Wrap(Loc(_home.ComingSoonKey), Ctx.Style.TextSizes.Body, card.Width - 160f))
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
                case MapNodeType.Story:
                    return "node_camp";
                case MapNodeType.Trial:
                    return "node_elite";
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
                case MapNodeType.Story:
                    return "grove";
                case MapNodeType.Trial:
                    return "star";
                default:
                    return "battle";
            }
        }
    }
}
