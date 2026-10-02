using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Creatures;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Game.Ui;
using BeastCraft.Grove;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The Roster tab's page (<see cref="RosterViewModel"/>), inside the home screen: every owned
    /// beast as an illustrated card (level, stance, element; a leaf badge when in the party), a sort
    /// button that cycles Joined, Level, Name, Element and Stance, then a silhouette for each species
    /// not found yet ("Meet it at a Kinship site"). Tapping a beast opens its detail screen.
    /// </summary>
    public sealed class RosterPage
    {
        private const float Pad = 36f;
        private const float CardHeight = 360f;

        private readonly ScreenContext _ctx;
        private readonly RosterViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Button _sort;
        private readonly Dictionary<Widget, RosterEntry> _cards = new Dictionary<Widget, RosterEntry>();
        private float _silhouettesY;

        public RosterPage(ScreenContext ctx, Widget parent, Rect area)
        {
            _ctx = ctx;
            _model = new RosterViewModel(ctx.Session);
            Root = parent.Add(new Group { Id = "roster-page", Bounds = area });
            Root.Add(new Panel { Bounds = new Rect(area.X + 24f, area.Y + 24f, area.Width - 48f, 250f), StyleKey = "header" });
            _sort = Root.Add(new Button { Id = "roster-sort", Bounds = new Rect(area.Right - 400f, area.Y + 70f, 340f, 84f), StyleKey = "chip" });
            _sort.Clicked += () =>
            {
                _model.NextSort();
                Build();
            };
            Button glossary = Root.Add(new Button { Id = "roster-glossary", Bounds = new Rect(area.Right - 520f, area.Y + 70f, 100f, 84f), Text = "?", StyleKey = "chip" });
            glossary.Clicked += () => ctx.Stack.Push(new GlossaryScreen(ctx, null));
            Button compendium = Root.Add(new Button { Id = "roster-compendium", Bounds = new Rect(area.X + 48f, area.Y + 168f, area.Width - 96f, 70f), Text = ctx.Loc("ui.roster.compendium"), StyleKey = "chip" });
            compendium.Clicked += OpenCompendium;
            _scroll = Root.Add(new ScrollView { Id = "roster", Bounds = new Rect(area.X, area.Y + 294f, area.Width, area.Height - 294f) });
        }

        public Group Root { get; }

        public RosterViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Re-reads the save (the page is shown, or a detail screen changed something) and lays the grid out.</summary>
        public void Refresh()
        {
            _model.Refresh();
            Build();
        }

        /// <summary>Opens <paramref name="beastId"/>'s detail screen.</summary>
        public void Open(string beastId)
        {
            _ctx.Stack.Push(new BeastDetailScreen(_ctx, beastId));
        }

        /// <summary>Opens the compendium screen (the header's "Compendium" chip).</summary>
        public void OpenCompendium()
        {
            _ctx.Stack.Push(new CompendiumScreen(_ctx));
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _cards.Clear();
            _sort.Text = _ctx.Loc("ui.roster.sort", _model.SortName);
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float cardWidth = (width - 2f * 20f) / 3f;
            float y = 10f;
            for (int i = 0; i < _model.Owned.Count; i++)
            {
                RosterEntry entry = _model.Owned[i];
                Rect card = new Rect(Pad + (i % 3) * (cardWidth + 20f), y + (i / 3) * (CardHeight + 20f), cardWidth, CardHeight);
                Button button = _scroll.Add(new Button { Id = "beast-" + entry.BeastId, Bounds = card, StyleKey = "chip", Selected = entry.InParty });
                string id = entry.BeastId;
                button.Clicked += () => Open(id);
                _cards[button] = entry;
            }

            y += ((_model.Owned.Count + 2) / 3) * (CardHeight + 20f) + 20f;
            _silhouettesY = y;
            y += 70f;
            for (int i = 0; i < _model.Silhouettes.Count; i++)
            {
                RosterEntry entry = _model.Silhouettes[i];
                Rect card = new Rect(Pad + (i % 3) * (cardWidth + 20f), y + (i / 3) * (CardHeight + 20f), cardWidth, CardHeight);
                Panel panel = _scroll.Add(new Panel { Bounds = card, StyleKey = "slot" });
                _cards[panel] = entry;
            }

            y += ((_model.Silhouettes.Count + 2) / 3) * (CardHeight + 20f) + 30f;
            _scroll.ContentHeight = y;
        }

        /// <summary>The page's header (drawn after the widgets).</summary>
        public void DrawHeader()
        {
            UiPainter painter = _ctx.Painter;
            Rect area = Root.Bounds;
            painter.TextIn(_ctx.Loc("ui.roster.title"), new Rect(area.X + 60f, area.Y + 54f, 400f, _ctx.Style.TextSizes.Heading), _ctx.Style.TextSizes.Heading, painter.C("plum"), TextAlign.Left);
            string count = _ctx.Loc("ui.roster.count", _model.Owned.Count, _model.Silhouettes.Count);
            painter.TextIn(count, new Rect(area.X + 60f, area.Y + 120f, 460f, 30f), _ctx.Style.TextSizes.Body, painter.C("inkSoft"), TextAlign.Left);
        }

        /// <summary>A card's picture and text, or the silhouettes' heading.</summary>
        public bool DrawCustom(Widget widget)
        {
            UiPainter painter = _ctx.Painter;
            UiStyle style = _ctx.Style;
            if (widget == _scroll)
            {
                if (_model.Silhouettes.Count > 0)
                {
                    painter.TextIn(_ctx.Loc("ui.roster.not_found"), new Rect(Pad, _silhouettesY, 600f, style.TextSizes.Heading - 4f), style.TextSizes.Heading - 4f, painter.C("plum"), TextAlign.Left);
                    painter.TextIn(_ctx.Loc("ui.roster.for_compendium"), new Rect(PortraitLayout.CanvasWidth - Pad - 400f, _silhouettesY + 6f, 400f, 26f), style.TextSizes.Small + 2f,
                                   painter.C("inkSoft"), TextAlign.Right);
                }

                return true;
            }

            if (!_cards.TryGetValue(widget, out RosterEntry entry))
            {
                return false;
            }

            Rect card = widget.Bounds;
            Rect portrait = new Rect(card.X + 18f, card.Y + 16f, card.Width - 36f, 200f);
            painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 6f), 80f, painter.C("plum", 0.25f), 0.3f);
            if (entry.Silhouette)
            {
                painter.Art(painter.Sprite(entry.ArtKey), portrait, false, new Color(34, 22, 38, 225));
                painter.TextIn(entry.Name, new Rect(card.X + 12f, card.Y + 232f, card.Width - 24f, 34f), style.TextSizes.Body + 4f, painter.C("plumSoft"), TextAlign.Center);
                painter.TextIn(entry.Hint, new Rect(card.X + 12f, card.Y + 290f, card.Width - 24f, 30f), style.TextSizes.Small + 1f, painter.C("inkSoft"), TextAlign.Center);
                return true;
            }

            string tintHex = ColourFormPresentation.WornTint(_ctx.Session.Save, entry.BeastId, entry.SpeciesId, _ctx.Content.GroveLibrary, _ctx.Content.Economy?.Cosmetics);
            painter.Art(painter.Sprite(entry.ArtKey), portrait, false, string.IsNullOrEmpty(tintHex) ? (Color?)null : painter.C(tintHex));
            painter.TextIn(entry.Name, new Rect(card.X + 12f, card.Y + 232f, card.Width - 24f, 34f), style.TextSizes.Body + 2f, painter.C("ink"), TextAlign.Center);
            painter.ElementBadge(entry.Element, new Rect(card.X + 18f, card.Y + 284f, 48f, 48f));
            painter.TextIn(_ctx.Loc("ui.common.level", entry.Level), new Rect(card.X + 76f, card.Y + 280f, card.Width - 90f, 26f), style.TextSizes.Body,
                           painter.C("plum"), TextAlign.Left);
            painter.TextIn(_ctx.Loc("ui.common.pair", entry.Stance, entry.Element), new Rect(card.X + 76f, card.Y + 314f, card.Width - 90f, 24f), style.TextSizes.Small, painter.C("inkSoft"),
                           TextAlign.Left);
            if (entry.InParty)
            {
                Vec2 badge = new Vec2(card.Right - 32f, card.Y + 32f);
                painter.Disc(badge, 24f, painter.C("plum"));
                painter.Disc(badge, 19f, painter.C("leaf"));
                painter.Glyph("check", new Rect(badge.X - 15f, badge.Y - 15f, 30f, 30f), painter.C("white"));
            }

            return true;
        }
    }

    /// <summary>
    /// One owned beast in full (<see cref="BeastDetailViewModel"/>), in three tabs. <b>Stats</b>: the
    /// raw stats with the gear's share, turns per 100 gauge ticks against the party and a
    /// level-matched average enemy, the element chart both ways, crits, the level-gap curve.
    /// <b>Skills</b>: the three slots (each card with its targeting rule in words, the taunt rule,
    /// cooldown, power, scaling, level, XP and the next breakthrough's gate), Change and Upgrade, and
    /// the kit's other skills. <b>Gear</b>: each slot's gear on and off, the stance's behaviour, the
    /// bonds with their partners, the looks worn. Links to the element chart and the glossary.
    /// </summary>
    public sealed class BeastDetailScreen : GameScreen
    {
        private const float Pad = HeaderMetrics.Pad;
        private const float TopBar = HeaderMetrics.Tall;

        private static readonly string[] TabKeys = { "ui.beast.tab_stats", "ui.beast.tab_skills", "ui.beast.tab_gear" };

        private readonly BeastDetailViewModel _model;
        private readonly ScreenHeader _header;
        private readonly ScrollView _scroll;
        private readonly CardList _cards;
        private readonly List<Button> _tabs = new List<Button>();
        private int _tab;

        public BeastDetailScreen(ScreenContext ctx, string beastId, int tab = 0) : base(ctx)
        {
            _model = new BeastDetailViewModel(ctx.Session, beastId);
            _tab = tab;
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) });
            _cards = new CardList(_scroll);
            _header = new ScreenHeader(Ui, TopBar, () => Ctx.Stack.Pop());
            AddButton(null, "element-chart", new Rect(PortraitLayout.CanvasWidth - Pad - 300f, 40f, 180f, 90f), Loc("ui.beast.chart"), "chip", OpenChart);
            AddButton(null, "glossary", new Rect(PortraitLayout.CanvasWidth - Pad - 100f, 40f, 100f, 90f), "?", "chip", () => Ctx.Stack.Push(new GlossaryScreen(Ctx, null)));
            float tabWidth = (PortraitLayout.CanvasWidth - 2f * Pad - 2f * 16f) / 3f;
            for (int i = 0; i < TabKeys.Length; i++)
            {
                int index = i;
                _tabs.Add(AddButton(null, "tab-" + i, new Rect(Pad + i * (tabWidth + 16f), 214f, tabWidth, 92f), Loc(TabKeys[i]), "chip", () => SelectTab(index)));
            }

            Build();
        }

        public override string Name
        {
            get { return "beast-detail"; }
        }

        public BeastDetailViewModel Model
        {
            get { return _model; }
        }

        public override void Enter()
        {
            base.Enter();
            _model.Refresh();
            Build();
        }

        public void SelectTab(int tab)
        {
            _tab = Math.Max(0, Math.Min(TabKeys.Length - 1, tab));
            _scroll.ScrollTo(0f);
            Build();
        }

        /// <summary>Scrolls the page to <paramref name="fraction"/> (0-1) of the way down (scripted screenshots).</summary>
        public void ScrollPage(float fraction)
        {
            _scroll.ScrollTo(_scroll.MaxScroll * Math.Max(0f, Math.Min(1f, fraction)));
        }

        public void OpenChart()
        {
            List<Element> team = new List<Element>();
            foreach (string id in Ctx.Session.Party())
            {
                Save.OwnedBeast beast = Ctx.Session.Save.FindBeast(id);
                CreatureSpeciesSO species = beast?.Progress == null ? null : Ctx.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                team.AddRange(species?.Elements ?? new Element[0]);
            }

            team.AddRange(_model.Elements ?? new Element[0]);
            Ctx.Stack.Push(new ElementChartScreen(Ctx, team));
        }

        // ------------------------------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------------------------------

        private void Build()
        {
            _cards.Begin();
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Selected = i == _tab;
            }

            if (!_model.Exists)
            {
                _cards.End(0f);
                return;
            }

            float y = 10f;
            switch (_tab)
            {
                case 1:
                    y = BuildSkills(y);
                    break;
                case 2:
                    y = BuildGear(y);
                    break;
                default:
                    y = BuildStats(y);
                    break;
            }

            _cards.End(y);
        }

        private float Width
        {
            get { return PortraitLayout.CanvasWidth - 2f * Pad; }
        }

        private float Body
        {
            get { return Ctx.Style.TextSizes.Body; }
        }

        private float Line
        {
            get { return Ctx.Text.LineHeight(Body); }
        }

        private int Lines(string text, float size, float width)
        {
            return string.IsNullOrEmpty(text) ? 0 : Painter.Wrap(text, size, width).Count;
        }

        /// <summary>Wrapped text from (x, y) in the panel; returns the y below it.</summary>
        private float Wrapped(string text, float x, float y, float width, float size, string color)
        {
            foreach (string line in Painter.Wrap(text ?? string.Empty, size, width))
            {
                Painter.TextIn(line, new Rect(x, y, width, size), size, Painter.C(color), TextAlign.Left, false);
                y += Ctx.Text.LineHeight(size);
            }

            return y;
        }

        // ---- Stats ---------------------------------------------------------------------------------

        private float BuildStats(float y)
        {
            float descLines = Lines(_model.Description, Body, Width - 440f);
            y = _cards.Card(y, Math.Max(340f, 200f + descLines * Line), "card", DrawIdentity);
            y = _cards.Card(y, 150f + _model.Stats.Count * 56f + Math.Max(1, _model.GearContributions.Count) * 44f + 30f, "panel", DrawStatTable);
            float turnsTop = y;
            y = _cards.Card(y, 230f + _model.TurnRates.Count * 56f, "panel", DrawTurnRates);
            AddButton(_scroll, "glossary-atb", new Rect(PortraitLayout.CanvasWidth - Pad - 150f, turnsTop + 22f, 120f, 70f), Loc("ui.beast.atb_help"), "chip", OpenTurnsHelp);
            y = _cards.Card(y, 330f, "panel", DrawElements);
            AddButton(_scroll, "element-chart-2", new Rect(PortraitLayout.CanvasWidth - Pad - 250f, y - 26f - 330f + 22f, 220f, 70f), Loc("ui.beast.full_chart"), "chip", OpenChart);
            y = _cards.Card(y, 190f, "panel", DrawCrits);
            y = _cards.Card(y, 290f, "panel", DrawLevelGap);
            AddButton(_scroll, "glossary-gap", new Rect(PortraitLayout.CanvasWidth - Pad - 150f, y - 26f - 290f + 22f, 120f, 70f), Loc("ui.beast.gap_help"), "chip",
                      () => Ctx.Stack.Push(new GlossaryScreen(Ctx, "level_gap")));
            return y;
        }

        /// <summary>The Turns panel's "?": what its columns mean, how the average enemy is taken, and the way to the glossary.</summary>
        public void OpenTurnsHelp()
        {
            string text = Loc("ui.beast.turns_help");
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.beast.turns_title"), text, Loc("ui.common.close"), Loc("ui.beast.glossary"), () => Ctx.Stack.Push(new GlossaryScreen(Ctx, "atb")), "secondary"));
        }

        private void DrawIdentity(Rect box)
        {
            UiStyle style = Ctx.Style;
            Rect portrait = new Rect(box.X + 20f, box.Y + 20f, 340f, 300f);
            Painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 10f), 120f, Painter.C("plum", 0.25f), 0.3f);
            string tintHex = ColourFormPresentation.WornTint(Ctx.Session.Save, _model.BeastId, _model.Species?.SpeciesId, Ctx.Content.GroveLibrary, Ctx.Content.Economy?.Cosmetics);
            Painter.Art(Painter.Sprite(_model.ArtKey), portrait, false, string.IsNullOrEmpty(tintHex) ? (Color?)null : Painter.C(tintHex));
            float x = box.X + 400f;
            float w = box.Right - x - 30f;
            float y = box.Y + 30f;
            float badge = 50f;
            foreach (Element element in _model.Elements)
            {
                Painter.ElementBadge(element, new Rect(x, y, badge, badge));
                x += badge + 10f;
            }

            Painter.TextIn(Loc("ui.common.pair", string.Join(" / ", Array.ConvertAll(ToArray(_model.Elements), e => e.ToString())), _model.Stance), new Rect(x + 6f, y + 8f, box.Right - x - 36f, 30f),
                           Body, Painter.C("ink"), TextAlign.Left);
            x = box.X + 400f;
            y += 76f;
            Painter.Progress(new Rect(x, y, w, 40f), _model.XpFraction, -1f, "leaf", "moss", "track", Loc("ui.beast.level_xp", _model.Level, _model.XpText));
            y += 66f;
            Wrapped(_model.Description, x, y, w, Body, "inkSoft");
        }

        private static Element[] ToArray(IReadOnlyList<Element> elements)
        {
            Element[] array = new Element[elements?.Count ?? 0];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = elements[i];
            }

            return array;
        }

        private void DrawStatTable(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.stats"));
            float[] cols = { box.X + 40f, box.X + 330f, box.X + 560f, box.X + 790f };
            float y = box.Y + 90f;
            string[] head = { Loc("ui.beast.col_stat"), Loc("ui.beast.col_base"), Loc("ui.beast.col_gear"), Loc("ui.beast.col_total") };
            for (int i = 0; i < head.Length; i++)
            {
                Painter.TextIn(head[i], new Rect(cols[i], y, 200f, 28f), Ctx.Style.TextSizes.Small + 2f, Painter.C("inkSoft"), i == 0 ? TextAlign.Left : TextAlign.Right);
            }

            y += 46f;
            foreach (StatLine line in _model.Stats)
            {
                string suffix = line.Stat == StatType.CritChance ? "%" : string.Empty;
                Painter.TextIn(line.Label, new Rect(cols[0], y, 250f, Body), Body, Painter.C("ink"), TextAlign.Left);
                Painter.TextIn(line.Base + suffix, new Rect(cols[1], y, 200f, Body), Body, Painter.C("ink"), TextAlign.Right);
                Painter.TextIn(line.Gear == 0 ? "-" : (line.Gear > 0 ? "+" : string.Empty) + line.Gear + suffix, new Rect(cols[2], y, 200f, Body), Body,
                               Painter.C(line.Gear > 0 ? "leafDeep" : line.Gear < 0 ? "berry" : "inkSoft"), TextAlign.Right);
                Painter.TextIn(line.Total + suffix, new Rect(cols[3], y, 200f, Body), Body + 2f, Painter.C("plum"), TextAlign.Right);
                y += 56f;
            }

            y += 10f;
            if (_model.GearContributions.Count == 0)
            {
                Painter.TextIn(Loc("ui.beast.no_gear_worn"), new Rect(cols[0], y, box.Width - 80f, Body), Body, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            foreach (GearView gear in _model.GearContributions)
            {
                string text = gear.Inactive ? Loc("ui.beast.gear_line_inactive", gear.Name, string.Join(Loc("ui.common.list_sep"), gear.Bonuses), gear.MinimumLevel) : Loc("ui.beast.gear_line", gear.Name, string.Join(Loc("ui.common.list_sep"), gear.Bonuses));
                Painter.TextIn(text, new Rect(cols[0], y, box.Width - 80f, Body), Body - 1f, Painter.C("inkSoft"), TextAlign.Left);
                y += 44f;
            }
        }

        private void DrawTurnRates(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.turns_title"));
            Wrapped(Loc("ui.beast.turns_note"), box.X + 40f, box.Y + 100f,
                    box.Width - 80f, Ctx.Style.TextSizes.Small + 1f, "inkSoft");
            float[] cols = { box.X + 40f, box.X + 520f, box.X + 680f, box.X + 850f, box.X + 980f };
            float y = box.Y + 166f;
            string[] head = { Loc("ui.beast.col_unit"), Loc("ui.beast.col_speed"), Loc("ui.beast.col_fill"), Loc("ui.beast.col_turns"), Loc("ui.beast.col_vs") };
            for (int i = 0; i < head.Length; i++)
            {
                Painter.TextIn(head[i], new Rect(cols[i] - (i == 0 ? 0f : 150f), y, 150f, 26f), Ctx.Style.TextSizes.Small + 2f, Painter.C("inkSoft"), i == 0 ? TextAlign.Left : TextAlign.Right);
            }

            y += 44f;
            foreach (TurnRateView rate in _model.TurnRates)
            {
                string color = rate.IsSelf ? "plum" : rate.IsEnemy ? "berry" : "ink";
                Painter.TextIn(rate.Name, new Rect(cols[0], y, 420f, Body), Body, Painter.C(color), TextAlign.Left);
                Painter.TextIn(rate.Speed.ToString(CultureInfo.InvariantCulture), new Rect(cols[1] - 150f, y, 150f, Body), Body, Painter.C(color), TextAlign.Right);
                Painter.TextIn(rate.FillRate.ToString(CultureInfo.InvariantCulture), new Rect(cols[2] - 150f, y, 150f, Body), Body, Painter.C(color), TextAlign.Right);
                Painter.TextIn(rate.TurnsPer100Ticks.ToString("0.00", CultureInfo.InvariantCulture), new Rect(cols[3] - 150f, y, 150f, Body), Body + 2f, Painter.C(color),
                               TextAlign.Right);
                Painter.TextIn(DerivedStats.Times(rate.Relative), new Rect(cols[4] - 150f, y, 150f, Body), Body, Painter.C("inkSoft"), TextAlign.Right);
                y += 56f;
            }
        }

        private void DrawElements(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.elements"));
            float cell = (box.Width - 250f) / 10f;
            float x0 = box.X + 210f;
            float y = box.Y + 96f;
            Painter.TextIn(Loc("ui.beast.its_hits", _model.AttackElement), new Rect(box.X + 30f, y + 60f, 180f, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(Loc("ui.beast.hits_on_it"), new Rect(box.X + 30f, y + 150f, 180f, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C("ink"), TextAlign.Left);
            for (int i = 0; i < _model.Matchups.Count; i++)
            {
                ElementMatchView match = _model.Matchups[i];
                float x = x0 + i * cell;
                Painter.ElementBadge(match.Element, new Rect(x + (cell - 46f) / 2f, y, 46f, 46f));
                MultiplierCell(new Rect(x + 3f, y + 50f, cell - 6f, 72f), match.Dealt, true);
                MultiplierCell(new Rect(x + 3f, y + 132f, cell - 6f, 72f), match.Taken, false);
            }
        }

        /// <summary>A multiplier in a coloured cell: good for the beast in leaf, bad in berry, neutral in cream.</summary>
        private void MultiplierCell(Rect box, float multiplier, bool dealt)
        {
            bool good = dealt ? multiplier > 1f : multiplier < 1f;
            bool bad = dealt ? multiplier < 1f : multiplier > 1f;
            Painter.RoundedRect(box, 12f, Painter.C(good ? "leaf" : bad ? "berry" : "creamDeep"));
            Painter.TextIn(DerivedStats.Times(multiplier).Substring(1), box, Ctx.Style.TextSizes.Small, Painter.C(good || bad ? "white" : "ink"), TextAlign.Center);
        }

        private void DrawCrits(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.crits"));
            float y = box.Y + 96f;
            string text = Loc("ui.beast.crit_line", _model.CritChance, DerivedStats.Times(_model.CritMultiplier), DerivedStats.Times(Math.Round(_model.ExpectedCritFactor, 3)));
            Painter.TextIn(text, new Rect(box.X + 40f, y, box.Width - 80f, Body), Body, Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(Loc("ui.beast.crit_note"), new Rect(box.X + 40f, y + 50f, box.Width - 80f, 24f), Ctx.Style.TextSizes.Small + 1f,
                           Painter.C("inkSoft"), TextAlign.Left);
        }

        private void DrawLevelGap(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.level_gap", _model.Level));
            int count = _model.LevelGap.Count;
            float cell = (box.Width - 250f) / Math.Max(1, count);
            float x0 = box.X + 210f;
            float y = box.Y + 96f;
            Painter.TextIn(Loc("ui.beast.enemy_lv"), new Rect(box.X + 30f, y, 170f, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C("inkSoft"), TextAlign.Left);
            Painter.TextIn(Loc("ui.beast.you_deal"), new Rect(box.X + 30f, y + 62f, 170f, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(Loc("ui.beast.you_take"), new Rect(box.X + 30f, y + 134f, 170f, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C("ink"), TextAlign.Left);
            for (int i = 0; i < count; i++)
            {
                LevelGapPoint point = _model.LevelGap[i];
                float x = x0 + i * cell;
                Painter.TextIn((point.Gap > 0 ? "+" : string.Empty) + point.Gap, new Rect(x, y, cell, 26f), Ctx.Style.TextSizes.Small + 1f, Painter.C(point.Gap == 0 ? "plum" : "inkSoft"),
                               TextAlign.Center);
                MultiplierCell(new Rect(x + 2f, y + 44f, cell - 4f, 64f), (float)point.Dealt, true);
                MultiplierCell(new Rect(x + 2f, y + 116f, cell - 4f, 64f), (float)point.Taken, false);
            }
        }

        // ---- Skills --------------------------------------------------------------------------------

        private float BuildSkills(float y)
        {
            AddLabel(_scroll, new Rect(Pad, y, Width, 40f), Loc("ui.beast.equipped", BeastCraft.Progression.BeastSkillBook.EquipSlotCount), Ctx.Style.TextSizes.Heading - 4f, "plum");
            y += 60f;
            for (int slot = 0; slot < _model.Slots.Count; slot++)
            {
                SkillEntryView entry = _model.Slots[slot];
                int index = slot;
                float height = entry.Skill == null ? 170f : SkillCardHeight(entry.Card) + 280f;
                float top = y;
                y = _cards.Card(y, height, "panel", box => DrawSkillEntry(box, entry, index));
                AddButton(_scroll, "change-" + slot, new Rect(Pad + Width - 460f, top + 24f, 200f, 76f), Loc(entry.Skill == null ? "ui.beast.fill" : "ui.beast.change"), "chip", () => ChooseSkill(index));
                if (entry.Skill != null)
                {
                    AddButton(_scroll, "upgrade-" + slot, new Rect(Pad + Width - 240f, top + 24f, 210f, 76f), Loc("ui.beast.upgrade"), "primary", () => Upgrade(entry.SkillId));
                }
            }

            AddLabel(_scroll, new Rect(Pad, y, Width, 40f), Loc("ui.beast.kit"), Ctx.Style.TextSizes.Heading - 4f, "plum");
            y += 60f;
            foreach (SkillEntryView entry in _model.Learnable)
            {
                if (entry.EquippedSlot >= 0)
                {
                    continue;
                }

                float rule = Lines(entry.Card?.TargetingRule, Body, Width - 80f) * Line;
                SkillEntryView captured = entry;
                float top = y;
                y = _cards.Card(y, 150f + rule, entry.Known ? "card" : "slot", box => DrawKitEntry(box, captured));
                if (entry.Known)
                {
                    AddButton(_scroll, "equip-" + entry.SkillId, new Rect(Pad + Width - 240f, top + 20f, 210f, 70f), Loc("ui.beast.equip"), "chip", () => ChooseSlot(captured.SkillId));
                }
            }

            return y;
        }

        private float SkillCardHeight(SkillCard card)
        {
            float w = Width - 80f;
            int lines = card.Power.Count + Lines(card.TargetingRule, Body, w) + Lines(card.TauntRule, Ctx.Style.TextSizes.Small + 1f, w);
            return lines * Line + 60f;
        }

        private void DrawSkillEntry(Rect box, SkillEntryView entry, int slot)
        {
            UiStyle style = Ctx.Style;
            Painter.TextIn(Loc("ui.beast.slot", slot + 1), new Rect(box.X + 36f, box.Y + 26f, 200f, 26f), style.TextSizes.Small + 2f, Painter.C("inkSoft"), TextAlign.Left);
            if (entry.Skill == null)
            {
                Painter.TextIn(Loc("ui.beast.empty"), new Rect(box.X + 36f, box.Y + 80f, 400f, Body), Body, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            SkillCard card = entry.Card;
            SkillProgressView progress = entry.Progress;
            float x = box.X + 36f;
            float w = box.Width - 72f;
            Painter.TextIn(card.Name, new Rect(x, box.Y + 62f, w - 460f, style.TextSizes.Heading - 4f), style.TextSizes.Heading - 4f, Painter.C("plum"), TextAlign.Left);
            float y = box.Y + 116f;
            List<string> parts = new List<string> { card.Element ?? Loc("ui.beast.no_element") };
            if (card.Category != null)
            {
                parts.Add(card.Category);
            }

            parts.Add(card.Range);
            parts.Add(card.Cooldown);
            if (card.Uses != null)
            {
                parts.Add(card.Uses);
            }

            string tags = string.Join(Loc("ui.common.dash_sep"), parts);
            Painter.TextIn(tags, new Rect(x, y, w, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f, Painter.C("inkSoft"), TextAlign.Left);
            y += 44f;
            y = Wrapped(card.TargetingRule, x, y, w, Body, "ink");
            y = Wrapped(card.TauntRule, x, y, w, style.TextSizes.Small + 1f, "inkSoft");
            y += 8f;
            foreach (string power in card.Power)
            {
                Painter.TextIn(power, new Rect(x, y, w, Body), Body, Painter.C("plum"), TextAlign.Left);
                y += Line;
            }

            y += 10f;
            if (progress != null)
            {
                float value = progress.XpToNext <= 0 ? 1f : Math.Min(1f, (float)progress.Xp / progress.XpToNext);
                string xp = progress.XpToNext <= 0 ? Loc("ui.beast.max") : Loc("ui.beast.xp_of", progress.Xp, progress.XpToNext);
                Painter.Progress(new Rect(x, y, w, 40f), value, -1f, progress.AwaitingBreakthrough ? "gold" : "leaf", "moss", "track",
                                 Loc("ui.beast.skill_progress", progress.Level, progress.LevelCap, progress.Tier, progress.TierCount, xp));
                y += 54f;
                Painter.TextIn(progress.GateText, new Rect(x, y, w, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f,
                               Painter.C(progress.AwaitingBreakthrough ? "goldDeep" : "inkSoft"), TextAlign.Left);
                Painter.TextIn(Loc("ui.beast.power_scaling", progress.PowerMultiplier.ToString("0.00", CultureInfo.InvariantCulture), card.Scaling ?? string.Empty),
                               new Rect(x, y + 38f, w, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f, Painter.C("inkSoft"), TextAlign.Left);
            }
        }

        private void DrawKitEntry(Rect box, SkillEntryView entry)
        {
            UiStyle style = Ctx.Style;
            float x = box.X + 36f;
            float w = box.Width - 72f;
            Painter.TextIn(entry.Card.Name, new Rect(x, box.Y + 26f, w - 260f, Body + 4f), Body + 4f, Painter.C(entry.Known ? "plum" : "inkSoft"), TextAlign.Left);
            string state = entry.Known ? Loc("ui.beast.learned", entry.Progress.Level) : Loc("ui.beast.tome_teaches", entry.LearnLevel);
            Painter.TextIn(string.Join(Loc("ui.common.dash_sep"), state, entry.Card.Range, entry.Card.Cooldown), new Rect(x, box.Y + 72f, w - 260f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f,
                           Painter.C("inkSoft"), TextAlign.Left);
            Wrapped(entry.Card.TargetingRule, x, box.Y + 114f, w, Body, entry.Known ? "ink" : "inkSoft");
        }

        /// <summary>The known skills that could go in <paramref name="slot"/>.</summary>
        public void ChooseSkill(int slot)
        {
            List<ChoiceOption> options = new List<ChoiceOption>();
            foreach (SkillEntryView entry in _model.Learnable)
            {
                if (!entry.Known || entry.EquippedSlot == slot)
                {
                    continue;
                }

                string id = entry.SkillId;
                string label = entry.EquippedSlot >= 0 ? Loc("ui.beast.swap_with", entry.Card.Name, entry.EquippedSlot + 1) : entry.Card.Name;
                options.Add(new ChoiceOption(label, true, null, () => Apply(_model.EquipSkill(slot, id, out string message), message)));
            }

            if (_model.Slots[slot].Skill != null)
            {
                options.Add(new ChoiceOption(Loc("ui.beast.empty_slot"), true, null, () => Apply(_model.UnequipSkill(slot, out string message), message)));
            }

            Ctx.Stack.PushModal(new ChoiceModal(Ctx, Loc("ui.beast.slot", slot + 1), Loc(options.Count == 0 ? "ui.beast.no_other_skill" : "ui.beast.pick_skill"),
                                                options));
        }

        /// <summary>Which slot a known skill goes in.</summary>
        public void ChooseSlot(string skillId)
        {
            List<ChoiceOption> options = new List<ChoiceOption>();
            for (int slot = 0; slot < _model.Slots.Count; slot++)
            {
                int index = slot;
                string held = _model.Slots[slot].Card?.Name ?? Loc("ui.beast.empty_lower");
                options.Add(new ChoiceOption(Loc("ui.beast.slot_holding", slot + 1, held), true, null, () => Apply(_model.EquipSkill(index, skillId, out string message), message)));
            }

            Ctx.Stack.PushModal(new ChoiceModal(Ctx, Loc("ui.beast.equip_where"), Loc("ui.beast.equip_where_body"), options));
        }

        /// <summary>Training and breakthroughs for <paramref name="skillId"/>: each material held, its XP and tier, what it can do now and why not.</summary>
        public void Upgrade(string skillId)
        {
            SkillEntryView entry = _model.Slots.Find(s => s.SkillId == skillId) ?? _model.Learnable.Find(s => s.SkillId == skillId);
            SkillProgressView progress = entry?.Progress;
            if (progress == null)
            {
                return;
            }

            List<ChoiceOption> options = new List<ChoiceOption>();
            foreach (MaterialOptionView material in _model.Materials(skillId))
            {
                string id = material.MaterialId;
                string held = Loc("ui.beast.material", material.Name, material.Owned, material.Tier, material.XpValue);
                if (progress.AwaitingBreakthrough)
                {
                    options.Add(new ChoiceOption(Loc("ui.beast.break_through", held), material.CanBreakthrough, material.Reason,
                                                 () => Apply(_model.Breakthrough(skillId, id, out string message), message)));
                }
                else
                {
                    options.Add(new ChoiceOption(Loc("ui.beast.train", held), material.CanTrain, material.Reason, () => Apply(_model.Train(skillId, id, out string message), message)));
                }
            }

            string xpText = progress.XpToNext > 0 ? Loc("ui.beast.upgrade_xp", progress.Xp, progress.XpToNext, progress.Level + 1) : Loc("ui.beast.max_level");
            string next = progress.NextPowerMultiplier > progress.PowerMultiplier
                              ? Loc("ui.beast.next_level_power", progress.NextPowerMultiplier.ToString("0.00", CultureInfo.InvariantCulture))
                              : string.Empty;
            string text = Loc("ui.beast.upgrade_text", progress.Level, progress.Tier, xpText, progress.PowerMultiplier.ToString("0.00", CultureInfo.InvariantCulture), next, progress.GateText);
            Ctx.Stack.PushModal(new ChoiceModal(Ctx, entry.Card.Name, text, options));
        }

        private void Apply(bool ok, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Ctx.Game.Toast(message);
            }

            Build();
        }

        // ---- Gear, stance, bonds, looks ------------------------------------------------------------

        private float BuildGear(float y)
        {
            foreach (GearSlotView slot in _model.Gear)
            {
                GearSlotView captured = slot;
                float height = 150f + Math.Max(1, slot.Options.Count) * 96f;
                float top = y;
                y = _cards.Card(y, height, "panel", box => DrawGearSlot(box, captured));
                if (slot.Worn != null)
                {
                    AddButton(_scroll, "unequip-" + slot.Slot, new Rect(Pad + Width - 260f, top + 22f, 230f, 76f), Loc("ui.beast.take_off"), "secondary", () =>
                    {
                        Apply(_model.UnequipGear(captured.Slot, out string message), message);
                    });
                }

                for (int i = 0; i < slot.Options.Count; i++)
                {
                    GearView option = slot.Options[i];
                    if (option.InstanceId == slot.Worn?.InstanceId)
                    {
                        continue;
                    }

                    Button equip = AddButton(_scroll, "equip-" + option.InstanceId, new Rect(Pad + Width - 220f, top + 128f + i * 96f, 190f, 76f), Loc("ui.beast.equip"), "chip", () =>
                    {
                        Apply(_model.EquipGear(captured.Slot, option.InstanceId, out string message), message);
                    });
                    equip.Enabled = option.Equippable;
                }
            }

            string stance = _model.StanceBehaviour ?? string.Empty;
            y = _cards.Card(y, 130f + Lines(stance, Body, Width - 80f) * Line, "card", box =>
            {
                SectionHeader.Draw(Ctx, box, Loc("ui.beast.stance", _model.Stance));
                Wrapped(stance, box.X + 40f, box.Y + 90f, box.Width - 80f, Body, "ink");
            });

            float bondsHeight = 110f;
            foreach (BondView bond in _model.Bonds)
            {
                bondsHeight += 120f + Lines(bond.Description, Ctx.Style.TextSizes.Small + 1f, Width - 80f) * Ctx.Text.LineHeight(Ctx.Style.TextSizes.Small + 1f);
            }

            y = _cards.Card(y, _model.Bonds.Count == 0 ? 170f : bondsHeight, "panel", DrawBonds);
            float looksTop = y;
            y = _cards.Card(y, 150f + Math.Max(1, _model.Looks.Count) * 48f, "card", DrawLooks);
            AddButton(_scroll, "look-token-shop", new Rect(Pad + Width - 260f, looksTop + 24f, 220f, 76f), Loc("ui.beast.look_shop"), "chip", () => Ctx.Stack.Push(new LookTokenShopScreen(Ctx)));

            if (_model.ColourForms.Count > 0)
            {
                const float rowHeight = 96f;
                float colourTop = y;
                y = _cards.Card(y, 100f + _model.ColourForms.Count * rowHeight, "card", DrawColourForms);
                for (int i = 0; i < _model.ColourForms.Count; i++)
                {
                    ColourFormRow form = _model.ColourForms[i];
                    string id = form.ColourFormId;
                    float rowY = colourTop + 88f + i * rowHeight;
                    if (!form.Owned)
                    {
                        Button unlock = AddButton(_scroll, "unlock-colour-" + id, new Rect(Pad + Width - 230f, rowY, 190f, 76f), Loc("ui.beast.unlock"), "chip", () =>
                        {
                            ColourFormResult result = _model.UnlockColourForm(id);
                            Ctx.Game.Toast(result.Success ? Loc("ui.beast.unlocked") : result.Error);
                            Build();
                        });
                        unlock.Enabled = form.ItemHeld >= form.ItemCount;
                    }
                    else if (!form.Worn)
                    {
                        AddButton(_scroll, "wear-colour-" + id, new Rect(Pad + Width - 230f, rowY, 190f, 76f), Loc("ui.beast.wear"), "chip", () =>
                        {
                            _model.WearColourForm(id);
                            Build();
                        });
                    }
                    else
                    {
                        string categoryId = form.CosmeticCategoryId;
                        AddButton(_scroll, "unwear-colour-" + id, new Rect(Pad + Width - 260f, rowY, 220f, 76f), Loc("ui.beast.wear_natural"), "secondary", () =>
                        {
                            _model.WearNaturalColour(categoryId);
                            Build();
                        });
                    }
                }
            }

            return y;
        }

        private void DrawGearSlot(Rect box, GearSlotView slot)
        {
            SectionHeader.Draw(Ctx, box, slot.SlotName);
            float x = box.X + 40f;
            float w = box.Width - 80f;
            string worn = slot.Worn == null
                               ? Loc("ui.beast.nothing_worn")
                               : Loc(slot.Worn.Inactive ? "ui.beast.wearing_inactive" : "ui.beast.wearing", slot.Worn.Name, string.Join(Loc("ui.common.list_sep"), slot.Worn.Bonuses));
            Painter.TextIn(worn, new Rect(x, box.Y + 80f, w - 260f, Body), Body - 1f, Painter.C(slot.Worn == null ? "inkSoft" : "leafDeep"), TextAlign.Left);
            if (slot.Options.Count == 0)
            {
                Painter.TextIn(Loc("ui.beast.no_gear_for_slot"), new Rect(x, box.Y + 138f, w, Body), Body - 1f, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            for (int i = 0; i < slot.Options.Count; i++)
            {
                GearView option = slot.Options[i];
                float y = box.Y + 128f + i * 96f;
                Painter.TextIn(option.MinimumLevel > 1 ? Loc("ui.beast.gear_min_level", option.Name, option.MinimumLevel) : option.Name, new Rect(x, y + 4f, w - 250f, Body), Body,
                               Painter.C("ink"), TextAlign.Left);
                string bonuses = string.Join(Loc("ui.common.list_sep"), option.Bonuses);
                string note = option.Reason != null ? Loc("ui.common.pair", bonuses, option.Reason) : bonuses;
                Painter.TextIn(note, new Rect(x, y + 44f, w - 250f, Ctx.Style.TextSizes.Small + 1f), Ctx.Style.TextSizes.Small + 1f, Painter.C("inkSoft"), TextAlign.Left);
            }
        }

        private void DrawBonds(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.bonds"));
            float x = box.X + 40f;
            float w = box.Width - 80f;
            float y = box.Y + 90f;
            if (_model.Bonds.Count == 0)
            {
                Painter.TextIn(Loc("ui.beast.none"), new Rect(x, y, w, Body), Body, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            float small = Ctx.Style.TextSizes.Small + 1f;
            foreach (BondView bond in _model.Bonds)
            {
                string tiers = string.Join("/", bond.TierCounts.ConvertAll(c => c.ToString(CultureInfo.InvariantCulture)));
                Painter.TextIn(bond.PartyTier > 0 ? Loc("ui.beast.bond_active", bond.Name, bond.PartyTier) : bond.Name, new Rect(x, y, w, Body), Body,
                               Painter.C(bond.PartyTier > 0 ? "leafDeep" : "plum"), TextAlign.Left);
                y += 42f;
                string partners = bond.Partners.Count == 0 ? Loc("ui.beast.no_partner") : Loc("ui.beast.with_partners", string.Join(Loc("ui.common.list_sep"), bond.Partners));
                Painter.TextIn(Loc("ui.beast.bond_tiers", bond.Condition, tiers, partners), new Rect(x, y, w, small), small, Painter.C("ink"), TextAlign.Left);
                y += 38f;
                y = Wrapped(bond.Description, x, y, w, small, "inkSoft") + 40f;
            }
        }

        private void DrawLooks(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.looks_worn"));
            float y = box.Y + 90f;
            if (_model.Looks.Count == 0)
            {
                Painter.TextIn(Loc("ui.beast.default_look"), new Rect(box.X + 40f, y, box.Width - 80f, Body), Body, Painter.C("inkSoft"), TextAlign.Left);
            }

            foreach (LookView look in _model.Looks)
            {
                Painter.TextIn(Loc("ui.common.label_value", look.Category, look.Option), new Rect(box.X + 40f, y, box.Width - 80f, Body), Body, Painter.C("ink"), TextAlign.Left);
                y += 48f;
            }

            Painter.TextIn(Loc("ui.beast.wardrobe_note"), new Rect(box.X + 40f, box.Bottom - 50f, box.Width - 80f, 24f), Ctx.Style.TextSizes.Small + 1f,
                           Painter.C("inkSoft"), TextAlign.Left);
        }

        /// <summary>The species' colour forms (docs/design/grove.md, "Colour evolutions" — D3/D4): locked, owned, or worn.</summary>
        private void DrawColourForms(Rect box)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.beast.colour_forms"));
            float y = box.Y + 90f;
            const float rowHeight = 96f;
            foreach (ColourFormRow form in _model.ColourForms)
            {
                Painter.TextIn(form.DisplayName, new Rect(box.X + 40f, y, box.Width - 300f, Body), Body, Painter.C(form.Worn ? "leafDeep" : "ink"), TextAlign.Left);
                string status = form.Worn ? Loc("ui.beast.worn")
                               : form.Owned ? Loc("ui.beast.owned")
                               : Loc("ui.beast.item_progress", form.ItemDisplay, form.ItemHeld, form.ItemCount);
                Painter.TextIn(status, new Rect(box.X + 40f, y + 42f, box.Width - 300f, Ctx.Style.TextSizes.Small + 1f), Ctx.Style.TextSizes.Small + 1f,
                               Painter.C(form.Worn ? "leafDeep" : form.Owned ? "goldDeep" : form.ItemHeld >= form.ItemCount ? "goldDeep" : "berry"), TextAlign.Left);
                y += rowHeight;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            PageBackground("cream", "parchment");
            base.Draw();
            if (!_model.Exists)
            {
                _header.Paint(Ctx, Ui, string.Empty);
                RepaintExtras();
                Painter.TextIn(Loc("ui.beast.no_such_beast"), new Rect(180f, 60f, 600f, Ctx.Style.TextSizes.Heading), Ctx.Style.TextSizes.Heading, Painter.C("berry"), TextAlign.Left);
                return;
            }

            _header.Paint(Ctx, Ui, _model.Name, string.Join(Loc("ui.common.dash_sep"), Loc("ui.common.level", _model.Level), _model.Stance, _model.Element));
            RepaintExtras();
        }

        /// <summary>Repaints the screen's own fixed widgets above the header wash: Chart/? and the tab row (<see cref="ScreenHeader.Paint"/> only repaints Back and the title/subtitle).</summary>
        private void RepaintExtras()
        {
            foreach (Widget widget in Ui.Children)
            {
                if (widget != _scroll && widget != _header.Back)
                {
                    Painter.Paint(widget, Ui);
                }
            }
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_cards.TryDraw(widget, out Action<Rect> draw))
            {
                draw(widget.Bounds);
            }
        }
    }

    /// <summary>One choice in a <see cref="ChoiceModal"/>: its label, whether it can be taken, why not, and what it does.</summary>
    public sealed class ChoiceOption
    {
        public ChoiceOption(string label, bool enabled, string reason, Action run)
        {
            Label = label;
            Enabled = enabled;
            Reason = reason;
            Run = run;
        }

        public string Label { get; }
        public bool Enabled { get; }
        public string Reason { get; }
        public Action Run { get; }
    }

    /// <summary>A titled list of choices (a skill for a slot, a slot for a skill, a material to train with), each a button with its reason when it cannot be taken, and Close.</summary>
    public sealed class ChoiceModal : GameModal
    {
        public ChoiceModal(ScreenContext ctx, string title, string message, IReadOnlyList<ChoiceOption> options) : base(ctx)
        {
            UiStyle style = ctx.Style;
            float width = 960f;
            List<string> lines = ctx.Painter.Wrap(message ?? string.Empty, style.TextSizes.Body, width - 120f);
            float body = lines.Count * ctx.Text.LineHeight(style.TextSizes.Body);
            float rows = 0f;
            foreach (ChoiceOption option in options)
            {
                rows += option.Enabled || option.Reason == null ? 112f : 150f;
            }

            float height = Math.Min(PortraitLayout.CanvasHeight - 120f, 150f + body + 40f + rows + 160f);
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 46f, width - 120f, 60f), Text = title, Size = style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 130f, width - 120f, body), Text = message ?? string.Empty, Size = style.TextSizes.Body, ColorKey = "ink", Wrap = true });
            float y = card.Y + 150f + body + 20f;
            for (int i = 0; i < options.Count; i++)
            {
                ChoiceOption option = options[i];
                Button button = panel.Add(new Button { Id = "choice" + i, Bounds = new Rect(card.X + 60f, y, width - 120f, 92f), Text = option.Label, StyleKey = "chip", Enabled = option.Enabled });
                button.Clicked += () =>
                {
                    Close();
                    option.Run?.Invoke();
                };
                y += 112f;
                if (!option.Enabled && option.Reason != null)
                {
                    panel.Add(new Label { Bounds = new Rect(card.X + 70f, y - 14f, width - 140f, 30f), Text = option.Reason, Size = style.TextSizes.Small + 1f, ColorKey = "berry" });
                    y += 38f;
                }
            }

            Button close = panel.Add(new Button { Id = "close", Bounds = new Rect(card.Center.X - 200f, card.Bottom - 140f, 400f, 100f), Text = ctx.Loc("ui.common.close"), StyleKey = "secondary" });
            close.Clicked += Close;
            Name = "choice:" + title;
        }

        public override string Name { get; }
    }
}
