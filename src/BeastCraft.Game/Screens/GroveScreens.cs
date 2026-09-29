using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The Grove hub (docs/design/grove.md, D4): one screen, inner tabs Glade (Beast Grove), Garden
    /// (Wildgarden), Board (Expeditions) and Npc (the Grove Keeper and friends). Reached from the Home
    /// tab bar's Grove slot (<see cref="HomeScreen.SelectTab"/> pushes this rather than showing it
    /// inline). Each tab is its own scrolling page over its own sub view-model
    /// (<see cref="GroveHubViewModel"/>); decor placement is a simple slot grid (tap an empty slot to
    /// place, tap a filled one to put it away) rather than free drag — the right call on a portrait
    /// phone, and the design doc says so is fine for v1.
    /// </summary>
    public sealed class GroveScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float HeaderWash = 160f;
        private const float TabsY = 170f;
        private const float TabsHeight = 84f;
        private const float ContentTop = TabsY + TabsHeight + 20f;
        private const float ChipHeight = 78f;

        private readonly GroveHubViewModel _hub;
        private readonly Tabs _tabs;
        private readonly ScrollView _gladeScroll;
        private readonly ScrollView _gardenScroll;
        private readonly ScrollView _boardScroll;
        private readonly ScrollView _npcScroll;
        private readonly Dictionary<Widget, Action<Rect>> _gladeDrawers = new Dictionary<Widget, Action<Rect>>();
        private readonly Dictionary<Widget, Action<Rect>> _gardenDrawers = new Dictionary<Widget, Action<Rect>>();
        private readonly Dictionary<Widget, Action<Rect>> _boardDrawers = new Dictionary<Widget, Action<Rect>>();
        private readonly Dictionary<Widget, Action<Rect>> _npcDrawers = new Dictionary<Widget, Action<Rect>>();

        public GroveScreen(ScreenContext ctx) : base(ctx)
        {
            _hub = new GroveHubViewModel(ctx.Session);
            Rect page = new Rect(0, ContentTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - ContentTop);
            _gladeScroll = Ui.Add(new ScrollView { Id = "glade-page", Bounds = page });
            _gardenScroll = Ui.Add(new ScrollView { Id = "garden-page", Bounds = page, Visible = false });
            _boardScroll = Ui.Add(new ScrollView { Id = "board-page", Bounds = page, Visible = false });
            _npcScroll = Ui.Add(new ScrollView { Id = "npc-page", Bounds = page, Visible = false });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            Ui.Add(new Panel { Id = "tabs-panel", Bounds = new Rect(Pad, TabsY, PortraitLayout.CanvasWidth - 2f * Pad, TabsHeight), StyleKey = "nav" });
            _tabs = Ui.Add(new Tabs { Id = "grove-tabs", Bounds = new Rect(Pad, TabsY, PortraitLayout.CanvasWidth - 2f * Pad, TabsHeight).Inset(6f) });
            _tabs.Items.AddRange(new[] { "Glade", "Garden", "Board", "Folk" });
            _tabs.Glyphs.AddRange(new[] { "grove", "lore", "map", "kinship" });
            _tabs.Changed += index => SelectTab((GroveTab)index);
            BuildAll();
        }

        public override string Name
        {
            get { return "grove"; }
        }

        public GroveHubViewModel Model
        {
            get { return _hub; }
        }

        public override void Enter()
        {
            base.Enter();
            _hub.RefreshAll();
            BuildAll();
        }

        public void SelectTab(GroveTab tab)
        {
            _hub.Select(tab);
            ShowTab();
        }

        private void ShowTab()
        {
            _tabs.Selected = (int)_hub.Tab;
            _gladeScroll.Visible = _hub.Tab == GroveTab.Glade;
            _gardenScroll.Visible = _hub.Tab == GroveTab.Garden;
            _boardScroll.Visible = _hub.Tab == GroveTab.Board;
            _npcScroll.Visible = _hub.Tab == GroveTab.Npc;
        }

        private void BuildAll()
        {
            BuildGlade();
            BuildGarden();
            BuildBoard();
            BuildNpc();
            ShowTab();
        }

        private float Width
        {
            get { return PortraitLayout.CanvasWidth - 2f * Pad; }
        }

        private float Heading
        {
            get { return Ctx.Style.TextSizes.Heading - 4f; }
        }

        private float Body
        {
            get { return Ctx.Style.TextSizes.Body; }
        }

        private float Small
        {
            get { return Ctx.Style.TextSizes.Small; }
        }

        private float LineH(float size)
        {
            return Ctx.Text.LineHeight(size);
        }

        private float Card(ScrollView scroll, Dictionary<Widget, Action<Rect>> drawers, float y, float height, string style, Action<Rect> draw)
        {
            Panel panel = scroll.Add(new Panel { Bounds = new Rect(Pad, y, Width, height), StyleKey = style });
            drawers[panel] = draw;
            return y + height + 20f;
        }

        private void DrawWrapped(Rect box, string text, string colorKey = "ink", float pad = 20f)
        {
            float y = box.Y + pad;
            foreach (string line in Painter.Wrap(text ?? string.Empty, Body, box.Width - 2f * pad))
            {
                Painter.TextIn(line, new Rect(box.X + pad, y, box.Width - 2f * pad, Body), Body, Painter.C(colorKey), TextAlign.Left, false);
                y += LineH(Body);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Glade
        // ------------------------------------------------------------------------------------------

        private void BuildGlade()
        {
            _gladeScroll.ClearChildren();
            _gladeDrawers.Clear();
            float width = Width;
            float y = 10f;
            AddLabel(_gladeScroll, new Rect(Pad, y, width, Heading), "Habitats", Heading, "plum");
            y += LineH(Heading) + 16f;

            float x = Pad;
            float chipSize = Ctx.Style.Button("chip").TextSize;
            foreach (HabitatRow habitat in _hub.Glade.Habitats)
            {
                string text = habitat.DisplayName + (habitat.Unlocked ? string.Empty : " (locked)");
                float w = Math.Min(width, Ctx.Text.Measure(text, chipSize) + 60f);
                if (x + w > Pad + width)
                {
                    x = Pad;
                    y += ChipHeight + 14f;
                }

                string id = habitat.HabitatId;
                Button chip = AddButton(_gladeScroll, "habitat-" + id, new Rect(x, y, w, ChipHeight), text, "chip", () =>
                {
                    _hub.Glade.SelectHabitat(id);
                    BuildGlade();
                });
                chip.Selected = id == _hub.Glade.SelectedHabitatId;
                chip.Enabled = habitat.Unlocked;
                x += w + 14f;
            }

            y += ChipHeight + 30f;

            HabitatRow selected = _hub.Glade.Habitats.Find(h => h.HabitatId == _hub.Glade.SelectedHabitatId);
            if (selected != null)
            {
                AddLabel(_gladeScroll, new Rect(Pad, y, width, Body), "Decor (" + selected.PlacedCount + " / " + selected.SlotCount + ")", Body, "ink");
                y += LineH(Body) + 12f;

                const int cols = 4;
                float cell = (width - (cols - 1) * 16f) / cols;
                for (int i = 0; i < _hub.Glade.Slots.Count; i++)
                {
                    DecorSlotRow slot = _hub.Glade.Slots[i];
                    int col = i % cols;
                    int row = i / cols;
                    Rect bounds = new Rect(Pad + col * (cell + 16f), y + row * (cell + 16f), cell, cell);
                    string label = slot.DecorId == null ? "+" : slot.DisplayName;
                    Button b = AddButton(_gladeScroll, "slot-" + i, bounds, label, slot.DecorId == null ? "slot" : "card", () => TapSlot(slot));
                    b.Caption = slot.DecorId == null ? "empty" : null;
                }

                int rows = (_hub.Glade.Slots.Count + cols - 1) / cols;
                y += rows * (cell + 16f) + 20f;
            }

            AddLabel(_gladeScroll, new Rect(Pad, y, width, Heading), "Beasts", Heading, "plum");
            y += LineH(Heading) + 16f;
            if (_hub.Glade.Beasts.Count == 0)
            {
                AddLabel(_gladeScroll, new Rect(Pad, y, width, Body), "No beasts yet.", Body, "inkSoft");
                y += LineH(Body) + 16f;
            }

            foreach (GladeBeastRow beast in _hub.Glade.Beasts)
            {
                float height = 210f;
                float top = y;
                y = Card(_gladeScroll, _gladeDrawers, y, height, "card", box => DrawBeastCard(box, beast));
                string bid = beast.BeastId;
                Button feed = AddButton(_gladeScroll, "feed-" + bid, new Rect(Pad + width - 460f, top + 24f, 210f, 76f), "Feed", "chip", () =>
                {
                    GladeActionOutcome o = _hub.Glade.Feed(bid);
                    Ctx.Game.Toast(o.Message);
                    BuildGlade();
                });
                feed.Enabled = beast.CanFeed;
                Button play = AddButton(_gladeScroll, "play-" + bid, new Rect(Pad + width - 230f, top + 24f, 210f, 76f), "Play", "chip", () =>
                {
                    GladeActionOutcome o = _hub.Glade.Play(bid);
                    Ctx.Game.Toast(o.Message);
                    BuildGlade();
                });
                play.Enabled = beast.CanPlay;
                if (beast.PendingGifts > 0)
                {
                    Button gift = AddButton(_gladeScroll, "gift-" + bid, new Rect(Pad + width - 460f, top + 112f, 210f, 76f), "Collect gift (" + beast.PendingGifts + ")",
                                            "primary", () =>
                    {
                        GladeActionOutcome o = _hub.Glade.CollectGift(bid);
                        Ctx.Game.Toast(o.Message);
                        BuildGlade();
                    });
                    AddButton(_gladeScroll, "gift-all-" + bid, new Rect(Pad + width - 230f, top + 112f, 210f, 76f), "Collect all", "chip", () =>
                    {
                        GladeActionOutcome o = _hub.Glade.CollectAllGifts(bid);
                        Ctx.Game.Toast(o.Message);
                        BuildGlade();
                    });
                }
            }

            _gladeScroll.ContentHeight = y + 30f;
        }

        private void DrawBeastCard(Rect box, GladeBeastRow beast)
        {
            Rect portrait = new Rect(box.X + 20f, box.Y + 16f, 150f, 150f);
            Color? tint = string.IsNullOrEmpty(beast.TintHex) ? (Color?)null : Painter.C(beast.TintHex);
            Painter.Art(Painter.Sprite(beast.ArtKey), portrait, false, tint);
            float labelX = portrait.Right + 20f;
            float labelW = Math.Max(60f, box.Width - 540f);
            Painter.TextIn(beast.Name, new Rect(labelX, box.Y + 20f, labelW, Body + 2f), Body + 2f, Painter.C("ink"), TextAlign.Left);
            Painter.ElementBadge(beast.Element, new Rect(labelX, box.Y + 66f, 40f, 40f));
            string tierText = beast.Tier <= 0 ? "Not yet bonded" : "Tier " + beast.Tier + (beast.NextTierXp > 0 ? "  (" + beast.Xp + " / " + beast.NextTierXp + " XP)" : "  (max)");
            Painter.TextIn(tierText, new Rect(labelX + 56f, box.Y + 72f, labelW - 56f, Small + 2f), Small + 2f, Painter.C("inkSoft"), TextAlign.Left);
            float frac = beast.NextTierXp > 0 ? Math.Min(1f, (float)beast.Xp / beast.NextTierXp) : 1f;
            Painter.Progress(new Rect(labelX, box.Y + 128f, box.Width - (labelX - box.X) - 20f, 28f), frac, -1f, "gold", "gold", "track",
                             beast.Tier <= 0 ? null : "Tier " + beast.Tier);
        }

        private void TapSlot(DecorSlotRow slot)
        {
            if (slot.DecorId != null)
            {
                GladeActionOutcome outcome = _hub.Glade.RemoveDecor(slot.DecorId);
                Ctx.Game.Toast(outcome.Message);
                BuildGlade();
                return;
            }

            List<ChoiceOption> options = new List<ChoiceOption>();
            foreach (DecorAvailableRow decor in _hub.Glade.AvailableDecor)
            {
                string id = decor.DecorId;
                options.Add(new ChoiceOption(decor.DisplayName, true, null, () =>
                {
                    GladeActionOutcome outcome = _hub.Glade.PlaceDecor(id);
                    Ctx.Game.Toast(outcome.Message);
                    BuildGlade();
                }));
            }

            if (options.Count == 0)
            {
                Ctx.Game.Toast("No decor to place yet: grow, craft or find some.");
                return;
            }

            Ctx.Stack.PushModal(new ChoiceModal(Ctx, "Place decor", "Pick a piece for this slot.", options));
        }

        // ------------------------------------------------------------------------------------------
        // Garden
        // ------------------------------------------------------------------------------------------

        private void BuildGarden()
        {
            _gardenScroll.ClearChildren();
            _gardenDrawers.Clear();
            float width = Width;
            float y = 10f;
            AddLabel(_gardenScroll, new Rect(Pad, y, width, Heading), "Plots", Heading, "plum");
            y += LineH(Heading) + 16f;

            const int cols = 2;
            float cell = (width - (cols - 1) * 20f) / cols;
            const float plotHeight = 190f;
            for (int i = 0; i < _hub.Garden.Plots.Count; i++)
            {
                PlotRow plot = _hub.Garden.Plots[i];
                int col = i % cols;
                int row = i / cols;
                Rect bounds = new Rect(Pad + col * (cell + 20f), y + row * (plotHeight + 20f), cell, plotHeight);
                bool crossPicked = plot.PlotId == _hub.Garden.CrossFirstPlotId;
                string style = crossPicked ? "banner" : plot.SeedId == null ? "slot" : plot.Ready ? "card" : "panel";
                Panel panel = _gardenScroll.Add(new Panel { Bounds = bounds, StyleKey = style });
                _gardenDrawers[panel] = box => DrawPlot(box, plot);
                Hotspot tap = _gardenScroll.Add(new Hotspot { Bounds = bounds });
                tap.Clicked += _ => TapPlot(plot);
            }

            int plotRows = (_hub.Garden.Plots.Count + cols - 1) / cols;
            y += plotRows * (plotHeight + 20f) + 10f;

            AddLabel(_gardenScroll, new Rect(Pad, y, width, Heading), "Herbarium", Heading, "plum");
            y += LineH(Heading) + 16f;
            foreach (HerbariumRow row in _hub.Garden.Herbarium)
            {
                int lines = Math.Max(1, Painter.Wrap(row.Entry, Small + 1f, width - 48f).Count);
                float height = 30f + LineH(Body) + lines * LineH(Small + 1f) + 16f;
                y = Card(_gardenScroll, _gardenDrawers, y, height, row.Discovered ? "card" : "slot", box => DrawHerbarium(box, row));
            }

            y += 10f;
            AddLabel(_gardenScroll, new Rect(Pad, y, width, Heading), "Recipes", Heading, "plum");
            y += LineH(Heading) + 16f;
            foreach (RecipeRow recipe in _hub.Garden.Recipes)
            {
                const float height = 140f;
                float top = y;
                y = Card(_gardenScroll, _gardenDrawers, y, height, "panel", box => DrawRecipe(box, recipe));
                string rid = recipe.RecipeId;
                Button craft = AddButton(_gardenScroll, "craft-" + rid, new Rect(Pad + width - 220f, top + height / 2f - 38f, 190f, 76f), "Craft", "chip", () =>
                {
                    GardenActionOutcome o = _hub.Garden.Craft(rid);
                    Ctx.Game.Toast(o.Message);
                    BuildGarden();
                });
                craft.Enabled = recipe.CanCraft;
            }

            y += 10f;
            AddLabel(_gardenScroll, new Rect(Pad, y, width, Heading), "Grove items", Heading, "plum");
            y += LineH(Heading) + 16f;
            if (_hub.Garden.Inventory.Count == 0)
            {
                AddLabel(_gardenScroll, new Rect(Pad, y, width, Body), "Nothing grown or crafted yet.", Body, "inkSoft");
                y += LineH(Body) + 10f;
            }

            foreach (GroveItemRow item in _hub.Garden.Inventory)
            {
                AddLabel(_gardenScroll, new Rect(Pad, y, width, Body), item.DisplayName + "  x" + item.Quantity, Body, "ink");
                y += LineH(Body) + 8f;
            }

            _gardenScroll.ContentHeight = y + 40f;
        }

        private void DrawPlot(Rect box, PlotRow plot)
        {
            if (plot.SeedId == null)
            {
                Painter.TextIn("Empty plot", new Rect(box.X, box.Y + 60f, box.Width, Body), Body, Painter.C("inkSoft"), TextAlign.Center);
                Painter.TextIn("Tap to plant", new Rect(box.X, box.Bottom - 44f, box.Width, 30f), Small, Painter.C("plum"), TextAlign.Center);
                return;
            }

            Painter.TextIn(plot.SeedName, new Rect(box.X + 16f, box.Y + 16f, box.Width - 32f, Body), Body, Painter.C("ink"), TextAlign.Center);
            if (plot.Ready)
            {
                Painter.TextIn("Ready! Tap to harvest.", new Rect(box.X, box.Y + 70f, box.Width, Body), Body, Painter.C("leafDeep"), TextAlign.Center);
            }
            else
            {
                Painter.Progress(new Rect(box.X + 20f, box.Y + 100f, box.Width - 40f, 26f), (float)plot.Progress, -1f, "gold", "gold", "track",
                                 (int)(plot.Progress * 100.0) + "%");
            }
        }

        private void DrawHerbarium(Rect box, HerbariumRow row)
        {
            Painter.TextIn(row.DisplayName, new Rect(box.X + 24f, box.Y + 14f, box.Width - 48f, Body), Body, Painter.C(row.Discovered ? "plum" : "plumSoft"), TextAlign.Left);
            float y = box.Y + 14f + LineH(Body);
            foreach (string line in Painter.Wrap(row.Entry, Small + 1f, box.Width - 48f))
            {
                Painter.TextIn(line, new Rect(box.X + 24f, y, box.Width - 48f, Small + 1f), Small + 1f, Painter.C(row.Discovered ? "inkSoft" : "plumSoft"), TextAlign.Left, false);
                y += LineH(Small + 1f);
            }
        }

        private void DrawRecipe(Rect box, RecipeRow recipe)
        {
            Painter.TextIn(recipe.OutputDisplay, new Rect(box.X + 24f, box.Y + 16f, box.Width - 260f, Body), Body, Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(string.Join(", ", recipe.InputsText), new Rect(box.X + 24f, box.Y + 62f, box.Width - 260f, Small + 1f), Small + 1f,
                           Painter.C(recipe.CanCraft ? "leafDeep" : "berry"), TextAlign.Left);
        }

        private void TapPlot(PlotRow plot)
        {
            if (plot.SeedId == null)
            {
                List<ChoiceOption> options = new List<ChoiceOption>();
                foreach (SeedOptionRow seed in _hub.Garden.AvailableSeeds)
                {
                    string id = seed.SeedId;
                    options.Add(new ChoiceOption(seed.DisplayName + " (" + seed.GrowthHours + "h)", true, null, () =>
                    {
                        GardenActionOutcome o = _hub.Garden.Plant(plot.PlotId, id);
                        Ctx.Game.Toast(o.Message);
                        BuildGarden();
                    }));
                }

                if (options.Count == 0)
                {
                    Ctx.Game.Toast("No seeds unlocked yet.");
                    return;
                }

                Ctx.Stack.PushModal(new ChoiceModal(Ctx, "Plant", "Pick a seed for this plot.", options));
                return;
            }

            if (!plot.Ready)
            {
                Ctx.Game.Toast("Still growing.");
                return;
            }

            if (_hub.Garden.CrossFirstPlotId >= 0)
            {
                GardenActionOutcome pick = _hub.Garden.PickForCross(plot.PlotId);
                Ctx.Game.Toast(pick.Message);
                BuildGarden();
                return;
            }

            List<ChoiceOption> ready = new List<ChoiceOption>
            {
                new ChoiceOption("Harvest alone", true, null, () =>
                {
                    GardenActionOutcome o = _hub.Garden.Harvest(plot.PlotId);
                    Ctx.Game.Toast(o.Message);
                    BuildGarden();
                }),
                new ChoiceOption("Pick for cross-pollination", true, null, () =>
                {
                    GardenActionOutcome o = _hub.Garden.PickForCross(plot.PlotId);
                    Ctx.Game.Toast(o.Message);
                    BuildGarden();
                })
            };
            Ctx.Stack.PushModal(new ChoiceModal(Ctx, "Ready to harvest", "Harvest it alone, or pick it (then tap another ready plot) to cross-pollinate.", ready));
        }

        // ------------------------------------------------------------------------------------------
        // Board
        // ------------------------------------------------------------------------------------------

        private void BuildBoard()
        {
            _boardScroll.ClearChildren();
            _boardDrawers.Clear();
            float width = Width;
            float y = 10f;
            AddLabel(_boardScroll, new Rect(Pad, y, width, Heading), "Destinations", Heading, "plum");
            y += LineH(Heading) + 16f;

            foreach (DestinationRow dest in _hub.Board.Destinations)
            {
                const float height = 190f;
                float top = y;
                string style = dest.State == DestinationState.Locked ? "slot" : dest.State == DestinationState.Ready ? "card" : "panel";
                y = Card(_boardScroll, _boardDrawers, y, height, style, box => DrawDestination(box, dest));
                string did = dest.DestinationId;
                if (dest.State == DestinationState.Available)
                {
                    AddButton(_boardScroll, "send-" + did, new Rect(Pad + width - 230f, top + height - 90f, 190f, 76f), "Send", "primary", () => OpenSend(dest));
                }
                else if (dest.State == DestinationState.Ready)
                {
                    AddButton(_boardScroll, "collect-" + did, new Rect(Pad + width - 230f, top + height - 90f, 190f, 76f), "Collect", "primary", () =>
                    {
                        BoardActionOutcome o = _hub.Board.Collect(did);
                        Ctx.Game.Toast(o.Message);
                        BuildBoard();
                    });
                }
            }

            _boardScroll.ContentHeight = y + 30f;
        }

        private void DrawDestination(Rect box, DestinationRow dest)
        {
            Painter.TextIn(dest.DisplayName, new Rect(box.X + 24f, box.Y + 16f, box.Width - 48f, Body + 2f), Body + 2f, Painter.C("ink"), TextAlign.Left);
            string stateText;
            switch (dest.State)
            {
                case DestinationState.Locked:
                    stateText = "Locked";
                    break;
                case DestinationState.Available:
                    stateText = dest.PartySize + " beast" + (dest.PartySize == 1 ? string.Empty : "s") + ", " + dest.DurationHours + "h away";
                    break;
                case DestinationState.Away:
                    stateText = "Away: " + string.Join(", ", dest.AwayBeastNames) + "  -  back in " + dest.HoursRemaining.ToString("0.0") + "h";
                    break;
                default:
                    stateText = "Ready to collect!";
                    break;
            }

            Painter.TextIn(stateText, new Rect(box.X + 24f, box.Y + 62f, box.Width - 48f, Small + 2f),
                           Small + 2f, Painter.C(dest.State == DestinationState.Ready ? "leafDeep" : dest.State == DestinationState.Locked ? "plumSoft" : "inkSoft"),
                           TextAlign.Left);
        }

        private void OpenSend(DestinationRow dest)
        {
            Ctx.Stack.PushModal(new SendPartyModal(Ctx, _hub.Board, dest.DestinationId, "Send to " + dest.DisplayName, dest.PartySize, message =>
            {
                Ctx.Game.Toast(message);
                BuildBoard();
            }));
        }

        // ------------------------------------------------------------------------------------------
        // Npc
        // ------------------------------------------------------------------------------------------

        private void BuildNpc()
        {
            _npcScroll.ClearChildren();
            _npcDrawers.Clear();
            float width = Width;
            float y = 10f;
            float x = Pad;
            float chipSize = Ctx.Style.Button("chip").TextSize;
            foreach (NpcSummaryRow npc in _hub.Npc.Npcs)
            {
                float w = Math.Min(width, Ctx.Text.Measure(npc.DisplayName, chipSize) + 60f);
                if (x + w > Pad + width)
                {
                    x = Pad;
                    y += ChipHeight + 14f;
                }

                string id = npc.NpcId;
                Button chip = AddButton(_npcScroll, "npc-" + id, new Rect(x, y, w, ChipHeight), npc.DisplayName, "chip", () =>
                {
                    _hub.Npc.SelectNpc(id);
                    BuildNpc();
                });
                chip.Selected = id == _hub.Npc.SelectedNpcId;
                x += w + 14f;
            }

            y += ChipHeight + 30f;

            if (!string.IsNullOrEmpty(_hub.Npc.TalkLine))
            {
                int lines = Math.Max(1, Painter.Wrap(_hub.Npc.TalkLine, Body, width - 40f).Count);
                float height = 40f + lines * LineH(Body);
                y = Card(_npcScroll, _npcDrawers, y, height, "banner", box => DrawWrapped(box, _hub.Npc.TalkLine, "ink", 20f));
            }

            AddLabel(_npcScroll, new Rect(Pad, y, width, Heading), "Requests", Heading, "plum");
            y += LineH(Heading) + 16f;
            if (_hub.Npc.Requests.Count == 0)
            {
                AddLabel(_npcScroll, new Rect(Pad, y, width, Body), "Nothing asked right now.", Body, "inkSoft");
                y += LineH(Body) + 16f;
            }

            foreach (NpcRequestRow req in _hub.Npc.Requests)
            {
                int lines = Math.Max(1, Painter.Wrap(req.Text, Small + 1f, width - 48f).Count);
                float height = Math.Max(150f, 30f + LineH(Small + 2f) + lines * LineH(Small + 1f) + 30f);
                float top = y;
                y = Card(_npcScroll, _npcDrawers, y, height, req.Fulfilled ? "card" : "panel", box => DrawRequest(box, req));
                if (!req.Fulfilled)
                {
                    string rid = req.RequestId;
                    Button fulfil = AddButton(_npcScroll, "fulfil-" + rid, new Rect(Pad + width - 230f, top + height / 2f - 38f, 190f, 76f), "Fulfil", "chip", () =>
                    {
                        NpcActionOutcome o = _hub.Npc.FulfillRequest(rid);
                        Ctx.Game.Toast(o.Message);
                        BuildNpc();
                    });
                    fulfil.Enabled = req.CanFulfill;
                }
            }

            y += 10f;
            AddLabel(_npcScroll, new Rect(Pad, y, width, Heading), "Story", Heading, "plum");
            y += LineH(Heading) + 16f;
            if (_hub.Npc.SideStories.Count == 0)
            {
                AddLabel(_npcScroll, new Rect(Pad, y, width, Body), "No side story here.", Body, "inkSoft");
                y += LineH(Body) + 16f;
            }

            foreach (SideStoryRow story in _hub.Npc.SideStories)
            {
                string text = story.Complete ? "Complete." : story.CurrentText ?? "Nothing new yet.";
                int lines = Math.Max(1, Painter.Wrap(text, Small + 1f, width - 48f).Count);
                float height = 30f + LineH(Body) + lines * LineH(Small + 1f) + 20f + (story.CanAdvance ? 96f : 10f);
                float top = y;
                y = Card(_npcScroll, _npcDrawers, y, height, "panel", box => DrawSideStory(box, story, text));
                if (story.CanAdvance)
                {
                    string sid = story.StoryId;
                    AddButton(_npcScroll, "advance-" + sid, new Rect(Pad + width - 260f, top + height - 86f, 220f, 70f), "Continue", "chip", () =>
                    {
                        NpcActionOutcome o = _hub.Npc.AdvanceSideStory(sid);
                        Ctx.Game.Toast(o.Message);
                        BuildNpc();
                    });
                }
            }

            _npcScroll.ContentHeight = y + 40f;
        }

        private void DrawRequest(Rect box, NpcRequestRow req)
        {
            Painter.TextIn(req.Fulfilled ? "Fulfilled" : req.ItemDisplay + "  " + req.Held + " / " + req.Count, new Rect(box.X + 24f, box.Y + 14f, box.Width - 280f, Small + 2f),
                           Small + 2f, Painter.C(req.Fulfilled ? "leafDeep" : req.CanFulfill ? "goldDeep" : "berry"), TextAlign.Left);
            float y = box.Y + 14f + LineH(Small + 2f) + 4f;
            foreach (string line in Painter.Wrap(req.Text, Small + 1f, box.Width - 48f))
            {
                Painter.TextIn(line, new Rect(box.X + 24f, y, box.Width - 48f, Small + 1f), Small + 1f, Painter.C("ink"), TextAlign.Left, false);
                y += LineH(Small + 1f);
            }
        }

        private void DrawSideStory(Rect box, SideStoryRow story, string text)
        {
            Painter.TextIn(story.DisplayName + "  (" + story.ChaptersCompleted + " / " + story.ChapterCount + ")", new Rect(box.X + 24f, box.Y + 14f, box.Width - 48f, Body),
                           Body, Painter.C(story.Complete ? "leafDeep" : "plum"), TextAlign.Left);
            float y = box.Y + 14f + LineH(Body) + 6f;
            foreach (string line in Painter.Wrap(text, Small + 1f, box.Width - 48f))
            {
                Painter.TextIn(line, new Rect(box.X + 24f, y, box.Width - 48f, Small + 1f), Small + 1f, Painter.C("ink"), TextAlign.Left, false);
                y += LineH(Small + 1f);
            }

            if (story.CanAdvance && !string.IsNullOrEmpty(story.ItemDisplay))
            {
                Painter.TextIn(story.ItemDisplay + "  " + story.ItemHeld + " / " + story.ItemCount, new Rect(box.X + 24f, box.Bottom - 92f, box.Width - 280f, Small + 1f),
                               Small + 1f, Painter.C(story.ItemHeld >= story.ItemCount ? "leafDeep" : "berry"), TextAlign.Left);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            Gradient("cream", "creamDeep", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            UiStyle style = Ctx.Style;
            Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, HeaderWash), Painter.C("cream"));
            Painter.Fill(new Rect(0, HeaderWash - 5f, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.Paint(Ui.Find("back"), Ui);
            Painter.TextIn("Grove", new Rect(180f, 44f, 600f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_gladeDrawers.TryGetValue(widget, out Action<Rect> gladeDraw))
            {
                gladeDraw(widget.Bounds);
                return;
            }

            if (_gardenDrawers.TryGetValue(widget, out Action<Rect> gardenDraw))
            {
                gardenDraw(widget.Bounds);
                return;
            }

            if (_boardDrawers.TryGetValue(widget, out Action<Rect> boardDraw))
            {
                boardDraw(widget.Bounds);
                return;
            }

            if (_npcDrawers.TryGetValue(widget, out Action<Rect> npcDraw))
            {
                npcDraw(widget.Bounds);
            }
        }
    }

    /// <summary>The Board's send picker: up to a destination's party size, from every owned beast (sending never locks one — docs/design/grove.md, D1's producer decision).</summary>
    public sealed class SendPartyModal : GameModal
    {
        private readonly BoardViewModel _board;
        private readonly string _destinationId;
        private readonly int _partySize;
        private readonly Action<string> _onDone;
        private readonly List<string> _selected = new List<string>();
        private readonly Dictionary<Button, string> _chips = new Dictionary<Button, string>();
        private Button _send;

        public SendPartyModal(ScreenContext ctx, BoardViewModel board, string destinationId, string title, int partySize, Action<string> onDone) : base(ctx)
        {
            _board = board;
            _destinationId = destinationId;
            _partySize = Math.Max(1, partySize);
            _onDone = onDone;
            UiStyle style = ctx.Style;
            float width = 960f;
            int rows = board.Beasts.Count;
            float height = Math.Min(PortraitLayout.CanvasHeight - 120f, 260f + rows * 108f + 170f);
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 40f, width - 120f, 60f), Text = title, Size = style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            panel.Add(new Label
            {
                Bounds = new Rect(card.X + 60f, card.Y + 108f, width - 120f, 40f),
                Text = "Send up to " + _partySize + ". Beasts stay available for battle the whole time.",
                Size = style.TextSizes.Small + 2f,
                ColorKey = "inkSoft",
                Align = TextAlign.Center,
                Wrap = true
            });
            float y = card.Y + 170f;
            foreach (BoardBeastOptionRow beast in board.Beasts)
            {
                string id = beast.BeastId;
                Button chip = panel.Add(new Button { Id = "beast-" + id, Bounds = new Rect(card.X + 60f, y, width - 120f, 92f), Text = beast.Name, StyleKey = "chip" });
                chip.Clicked += () => Toggle(chip, id);
                _chips[chip] = id;
                y += 108f;
            }

            Button send = panel.Add(new Button
            {
                Id = "send",
                Bounds = new Rect(card.X + 60f, card.Bottom - 150f, (width - 140f) / 2f, 100f),
                Text = "Send",
                StyleKey = "primary",
                Enabled = false
            });
            send.Clicked += Confirm;
            _send = send;
            Button cancel = panel.Add(new Button
            {
                Id = "cancel",
                Bounds = new Rect(card.X + 80f + (width - 140f) / 2f, card.Bottom - 150f, (width - 140f) / 2f, 100f),
                Text = "Cancel",
                StyleKey = "secondary"
            });
            cancel.Clicked += Close;
            Name = "send-party:" + destinationId;
        }

        public override string Name { get; }

        private void Toggle(Button chip, string beastId)
        {
            if (_selected.Contains(beastId))
            {
                _selected.Remove(beastId);
                chip.Selected = false;
                _send.Enabled = _selected.Count > 0;
                return;
            }

            if (_selected.Count >= _partySize)
            {
                return;
            }

            _selected.Add(beastId);
            chip.Selected = true;
            _send.Enabled = _selected.Count > 0;
        }

        private void Confirm()
        {
            if (_selected.Count == 0)
            {
                return;
            }

            BoardActionOutcome outcome = _board.Send(_destinationId, _selected);
            Close();
            _onDone?.Invoke(outcome.Message);
        }
    }
}
