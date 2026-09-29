using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Creatures;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Text;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The element chart (<see cref="ElementChartViewModel"/>): the 10x10 matrix, attacking elements
    /// down the side and defending ones across the top, each cell Strong (x2), Mild (x1.25), Weak
    /// (x0.5) or Neutral, the player's team elements' rows and columns highlighted. Reached from the
    /// glossary, the beast detail screen and the encounter preview.
    /// </summary>
    public sealed class ElementChartScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float TopBar = 190f;
        private const float Head = 150f;

        private readonly ElementChartViewModel _model;

        public ElementChartScreen(ScreenContext ctx, IEnumerable<Element> teamElements) : base(ctx)
        {
            _model = new ElementChartViewModel(teamElements);
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            AddButton(null, "glossary", new Rect(PortraitLayout.CanvasWidth - Pad - 100f, 50f, 100f, 90f), "?", "chip", () => Ctx.Stack.Push(new GlossaryScreen(Ctx, "element_chart")));
        }

        public override string Name
        {
            get { return "element-chart"; }
        }

        public ElementChartViewModel Model
        {
            get { return _model; }
        }

        private static float Cell
        {
            get { return (PortraitLayout.CanvasWidth - 2f * Pad - Head) / 10f; }
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            UiStyle style = Ctx.Style;
            Painter.TextIn(Loc("ui.insight.element_chart"), new Rect(180f, 44f, 600f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
            Painter.TextIn(Loc("ui.insight.rows_columns"), new Rect(180f, 112f, 700f, 30f), style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left);

            float cell = Cell;
            float x0 = Pad + Head;
            float y0 = TopBar + Head + 20f;
            Rect grid = new Rect(Pad - 10f, TopBar + 10f, PortraitLayout.CanvasWidth - 2f * Pad + 20f, Head + 10f * cell + 30f);
            Painter.Panel(grid, style.Panel("panel"));

            // Highlight the team's rows and columns under the cells.
            for (int i = 0; i < ElementChartViewModel.Elements.Length; i++)
            {
                if (_model.TeamElements.Contains(ElementChartViewModel.Elements[i]))
                {
                    Painter.RoundedRect(new Rect(Pad, y0 + i * cell - 3f, Head + 10f * cell, cell + 6f), 14f, Painter.C("gold", 0.45f));
                    Painter.RoundedRect(new Rect(x0 + i * cell - 3f, TopBar + 20f, cell + 6f, Head + 10f * cell), 14f, Painter.C("gold", 0.3f));
                }
            }

            for (int i = 0; i < ElementChartViewModel.Elements.Length; i++)
            {
                Element element = ElementChartViewModel.Elements[i];
                float badge = cell * 0.62f;
                Painter.ElementBadge(element, new Rect(x0 + i * cell + (cell - badge) / 2f, TopBar + 30f, badge, badge));
                Painter.TextIn(Short(element), new Rect(x0 + i * cell, TopBar + 34f + badge, cell, 20f), style.TextSizes.Small - 3f, Painter.C("inkSoft"), TextAlign.Center);
                Painter.ElementBadge(element, new Rect(Pad + 8f, y0 + i * cell + (cell - badge) / 2f, badge, badge));
                Painter.TextIn(Short(element), new Rect(Pad + 8f + badge + 6f, y0 + i * cell + cell / 2f - 10f, Head - badge - 16f, 20f), style.TextSizes.Small - 2f, Painter.C("ink"),
                               TextAlign.Left);
            }

            for (int r = 0; r < _model.Rows.Count; r++)
            {
                for (int c = 0; c < _model.Rows[r].Count; c++)
                {
                    ElementCell entry = _model.Rows[r][c];
                    Rect box = new Rect(x0 + c * cell + 3f, y0 + r * cell + 3f, cell - 6f, cell - 6f);
                    Painter.RoundedRect(box, 12f, Painter.C(KindColor(entry.Kind)));
                    string text = entry.Kind == MatchupKind.Neutral ? "1" : entry.Multiplier.ToString("0.##", CultureInfo.InvariantCulture);
                    Painter.TextIn(text, box, style.TextSizes.Body - 2f, Painter.C(entry.Kind == MatchupKind.Neutral ? "inkSoft" : "white"), TextAlign.Center);
                }
            }

            // The legend.
            float y = grid.Bottom + 40f;
            float x = Pad;
            foreach (MatchupKind kind in new[] { MatchupKind.Strong, MatchupKind.Mild, MatchupKind.Neutral, MatchupKind.Weak })
            {
                Rect swatch = new Rect(x, y, 56f, 56f);
                Painter.Framed(swatch, 12f, 3f, Painter.C("plumSoft"), Painter.C(KindColor(kind)));
                Painter.TextIn(ElementChartViewModel.Label(kind, Ctx.Content.Text), new Rect(swatch.Right + 14f, y + 14f, 170f, 30f), style.TextSizes.Body - 2f, Painter.C("ink"), TextAlign.Left);
                x += 252f;
            }

            y += 100f;
            string team = _model.TeamElements.Count == 0 ? Loc("ui.insight.no_team_elements") : Loc("ui.insight.team_elements", string.Join(Loc("ui.common.list_sep"), Sorted(_model.TeamElements)));
            foreach (string line in Painter.Wrap(Loc("ui.insight.chart_note", team), style.TextSizes.Body,
                                                 PortraitLayout.CanvasWidth - 2f * Pad))
            {
                Painter.TextIn(line, new Rect(Pad, y, PortraitLayout.CanvasWidth - 2f * Pad, style.TextSizes.Body), style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left, false);
                y += Ctx.Text.LineHeight(style.TextSizes.Body);
            }

            base.Draw();
        }

        private static List<string> Sorted(HashSet<Element> elements)
        {
            List<string> names = new List<string>();
            foreach (Element element in ElementChartViewModel.Elements)
            {
                if (elements.Contains(element))
                {
                    names.Add(element.ToString());
                }
            }

            return names;
        }

        /// <summary>A name short enough for a column (Lightning and Light share a badge letter, so the names tell them apart).</summary>
        private string Short(Element element)
        {
            switch (element)
            {
                case Element.Lightning:
                    return Loc("ui.insight.lightning_short");
                case Element.Nature:
                    return Loc("ui.insight.nature_short");
                default:
                    return element.ToString();
            }
        }

        /// <summary>A matchup's cell colour.</summary>
        public static string KindColor(MatchupKind kind)
        {
            switch (kind)
            {
                case MatchupKind.Strong:
                    return "leafDeep";
                case MatchupKind.Mild:
                    return "leaf";
                case MatchupKind.Weak:
                    return "berry";
                default:
                    return "creamDeep";
            }
        }
    }

    /// <summary>
    /// The glossary: every term with its definition (statuses, combat terms such as ATB, the gauge,
    /// the level gap, execute and variance, stances, passives), and the way to the element chart. A
    /// term to open at can be given (the screens' "?" buttons).
    /// </summary>
    public sealed class GlossaryScreen : GameScreen
    {
        private const float Pad = HeaderMetrics.Pad;
        private const float TopBar = HeaderMetrics.Compact;

        private readonly ScreenHeader _header;
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, GlossaryTerm> _cards = new Dictionary<Widget, GlossaryTerm>();
        private readonly string _focus;

        public GlossaryScreen(ScreenContext ctx, string focusTermId) : base(ctx)
        {
            _focus = focusTermId;
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) });
            _header = new ScreenHeader(Ui, TopBar, () => Ctx.Stack.Pop());
            AddButton(null, "element-chart", new Rect(PortraitLayout.CanvasWidth - Pad - 320f, 50f, 320f, 90f), Loc("ui.insight.element_chart"), "chip",
                      () => Ctx.Stack.Push(new ElementChartScreen(Ctx, TeamElements(Ctx))));
            Build();
        }

        public override string Name
        {
            get { return "glossary"; }
        }

        /// <summary>The last party's elements (for the chart's highlight), or none without a game.</summary>
        public static List<Element> TeamElements(ScreenContext ctx)
        {
            List<Element> elements = new List<Element>();
            if (ctx.Session?.Save == null)
            {
                return elements;
            }

            foreach (string id in ctx.Session.Party())
            {
                Save.OwnedBeast beast = ctx.Session.Save.FindBeast(id);
                CreatureSpeciesSO species = beast?.Progress == null ? null : ctx.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                elements.AddRange(species?.Elements ?? new Element[0]);
            }

            return elements;
        }

        private void Build()
        {
            UiStyle style = Ctx.Style;
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float y = 10f;
            float focusY = 0f;
            List<GlossaryTerm> terms = new List<GlossaryTerm>(Ctx.Content.Glossary?.Terms ?? new GlossaryTerm[0]);
            string[] order = { "Combat", "Status", "Stance", "Passive" };
            terms.Sort((a, b) =>
            {
                int byCategory = Array.IndexOf(order, a.Category).CompareTo(Array.IndexOf(order, b.Category));
                return byCategory != 0 ? byCategory : a.Order.CompareTo(b.Order);
            });
            foreach (GlossaryTerm term in terms)
            {
                int lines = Painter.Wrap(term.Definition, style.TextSizes.Body, width - 80f).Count;
                float height = 110f + lines * Ctx.Text.LineHeight(style.TextSizes.Body);
                if (term.TermId == _focus)
                {
                    focusY = y;
                }

                Panel panel = _scroll.Add(new Panel { Id = "term-" + term.TermId, Bounds = new Rect(Pad, y, width, height), StyleKey = term.TermId == _focus ? "banner" : "card" });
                _cards[panel] = term;
                y += height + 20f;
            }

            _scroll.ContentHeight = y + 30f;
            _scroll.ScrollTo(Math.Max(0f, focusY - 20f));
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            // No subtitle here: the title sits lower (60, not the header's usual 44) to stay centred
            // in the bar on its own, so it is drawn after the (title-less) header wash rather than
            // through ScreenHeader.Paint's own fixed title position.
            _header.Paint(Ctx, Ui, string.Empty);
            Painter.Paint(Ui.Find("element-chart"), Ui);
            Painter.TextIn(Loc("ui.insight.glossary"), new Rect(180f, 60f, 500f, Ctx.Style.TextSizes.Heading + 6f), Ctx.Style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (!_cards.TryGetValue(widget, out GlossaryTerm term))
            {
                return;
            }

            UiStyle style = Ctx.Style;
            Rect box = widget.Bounds;
            Painter.TextIn(term.Term, new Rect(box.X + 40f, box.Y + 28f, box.Width - 300f, style.TextSizes.Heading - 6f), style.TextSizes.Heading - 6f, Painter.C("plum"), TextAlign.Left);
            Painter.TextIn(term.Category, new Rect(box.Right - 260f, box.Y + 32f, 220f, 26f), style.TextSizes.Small + 1f, Painter.C("inkSoft"), TextAlign.Right);
            float y = box.Y + 86f;
            foreach (string line in Painter.Wrap(term.Definition, style.TextSizes.Body, box.Width - 80f))
            {
                Painter.TextIn(line, new Rect(box.X + 40f, y, box.Width - 80f, style.TextSizes.Body), style.TextSizes.Body, Painter.C("ink"), TextAlign.Left, false);
                y += Ctx.Text.LineHeight(style.TextSizes.Body);
            }
        }
    }

    /// <summary>
    /// The battle log (<see cref="BattleLogViewModel"/>): every event of the battle so far, oldest at
    /// the top, filterable by unit (a chip each, and All). A hit's line opens its damage breakdown:
    /// skill power, the attack and defence stats, the A*A/(A+D) base, the element, crit, variance,
    /// level-gap and execute multipliers, and the final amount. Opened in battle (the log strip) and
    /// from the Results screen; the battle waits while it is open.
    /// </summary>
    public sealed class BattleLogModal : GameModal
    {
        private const float RowHeight = 58f;
        private const float BreakdownLine = 40f;

        private readonly BattleLogViewModel _model;
        private readonly Rect _card;
        private readonly ScrollView _scroll;
        private readonly Group _chips;
        private readonly Dictionary<Widget, BattleLogEntry> _rows = new Dictionary<Widget, BattleLogEntry>();
        private BattleLogEntry _open;
        private bool _pinBottom = true;

        public BattleLogModal(ScreenContext ctx, BattleLogViewModel model, string title) : base(ctx)
        {
            _model = model ?? new BattleLogViewModel(null, ctx.Content.Text);
            _card = new Rect(30f, 90f, PortraitLayout.CanvasWidth - 60f, PortraitLayout.CanvasHeight - 180f);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            Ui.Add(new Label { Bounds = new Rect(_card.X + 50f, _card.Y + 40f, _card.Width - 260f, 50f), Text = title ?? Loc("ui.insight.battle_log"), Size = ctx.Style.TextSizes.Heading, ColorKey = "plum" });
            Button close = Ui.Add(new Button { Id = "close", Bounds = new Rect(_card.Right - 150f, _card.Y + 30f, 110f, 90f), StyleKey = "secondary", Glyph = "close" });
            close.Clicked += Close;
            _chips = Ui.Add(new Group { Bounds = new Rect(_card.X + 30f, _card.Y + 130f, _card.Width - 60f, 200f) });
            float chipsBottom = BuildChips();
            _scroll = Ui.Add(new ScrollView { Id = "log", Bounds = new Rect(_card.X + 20f, chipsBottom + 20f, _card.Width - 40f, _card.Bottom - chipsBottom - 50f) });
            Build();
            Name = "battle-log";
        }

        public override string Name { get; }

        public BattleLogViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Filters to <paramref name="unitId"/> (null: everyone).</summary>
        public void FilterTo(string unitId)
        {
            _model.Filter = unitId;
            _open = null;
            BuildChips();
            Build();
        }

        /// <summary>Opens the breakdown of the <paramref name="index"/>th hit shown (scripted screenshots); -1 = the last.</summary>
        public void OpenHit(int index)
        {
            List<BattleLogEntry> hits = _model.Filtered().FindAll(e => e.Breakdown != null);
            if (hits.Count == 0)
            {
                return;
            }

            _open = index < 0 || index >= hits.Count ? hits[hits.Count - 1] : hits[index];
            _pinBottom = false;
            Build();
            foreach (KeyValuePair<Widget, BattleLogEntry> row in _rows)
            {
                if (row.Value == _open)
                {
                    _scroll.ScrollTo(Math.Max(0f, row.Key.Bounds.Y - 200f));
                }
            }
        }

        private float BuildChips()
        {
            _chips.ClearChildren();
            UiStyle style = Ctx.Style;
            float x = _chips.Bounds.X;
            float y = _chips.Bounds.Y;
            float size = style.Button("chip").TextSize;

            // One chip each for everyone and the player's side; the enemies (up to two dozen) share one
            // chip that steps through them, so the filters stay on two rows.
            List<BattleLogUnit> units = new List<BattleLogUnit> { new BattleLogUnit { UnitId = null, Name = Loc("ui.insight.filter_all") } };
            List<BattleLogUnit> enemies = _model.Units.FindAll(u => u.Team != BeastCraft.Battle.BattleTeam.Player);
            units.AddRange(_model.Units.FindAll(u => u.Team == BeastCraft.Battle.BattleTeam.Player));
            int at = enemies.FindIndex(u => u.UnitId == _model.Filter);

            void Chip(string id, string text, bool selected, string target)
            {
                float w = Math.Min(_chips.Bounds.Width, Ctx.Text.Measure(text, size) + 50f);
                if (x + w > _chips.Bounds.Right)
                {
                    x = _chips.Bounds.X;
                    y += 76f;
                }

                Button chip = _chips.Add(new Button { Id = id, Bounds = new Rect(x, y, w, 64f), Text = text, StyleKey = "chip", Selected = selected });
                chip.Clicked += () => FilterTo(target);
                x += w + 12f;
            }

            foreach (BattleLogUnit unit in units)
            {
                Chip("filter-" + (unit.UnitId ?? "all"), unit.Name, _model.Filter == unit.UnitId, unit.UnitId);
            }

            if (enemies.Count > 0)
            {
                // Tapping steps to the next enemy (the last one back to the first).
                Chip("filter-enemies", Loc("ui.insight.filter_next", at >= 0 ? enemies[at].Name : Loc("ui.insight.filter_enemies")), at >= 0, enemies[(at + 1) % enemies.Count].UnitId);
            }

            return y + 64f;
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _rows.Clear();
            float y = 6f;
            float width = _scroll.Bounds.Width;
            foreach (BattleLogEntry entry in _model.Filtered())
            {
                bool open = entry == _open && entry.Breakdown != null;
                float height = entry.Kind == BattleLogKind.Turn ? RowHeight + 8f : RowHeight;
                if (open)
                {
                    height += entry.Breakdown.Lines(Ctx.Content.Text).Count * BreakdownLine + 30f;
                }

                Hotspot row = _scroll.Add(new Hotspot { Bounds = new Rect(0, y, width, height), Tag = entry });
                row.Clicked += spot =>
                {
                    BattleLogEntry tapped = (BattleLogEntry)spot.Tag;
                    if (tapped.Breakdown == null)
                    {
                        return;
                    }

                    _open = _open == tapped ? null : tapped;
                    _pinBottom = false;
                    float keep = _scroll.ScrollY;
                    Build();
                    _scroll.ScrollTo(keep);
                };
                _rows[row] = entry;
                y += height;
            }

            if (_model.Filtered().Count == 0)
            {
                y += RowHeight;
            }

            _scroll.ContentHeight = y + 20f;
            if (_pinBottom)
            {
                _scroll.ScrollTo(_scroll.MaxScroll);
            }
        }

        public override void Draw()
        {
            Ctx.Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Ctx.Painter.C("scrim"));
            Ctx.Painter.Paint(Ui, Ui, DrawCustom);
            UiPainter painter = Ctx.Painter;
            painter.TextIn(Loc("ui.insight.tap_hit"), new Rect(_card.X + 50f, _card.Y + 98f, _card.Width - 260f, 26f), Ctx.Style.TextSizes.Small + 1f, painter.C("inkSoft"),
                           TextAlign.Left);
            if (_model.Filtered().Count == 0)
            {
                painter.TextIn(Loc("ui.insight.nothing_yet"), new Rect(_scroll.Bounds.X + 30f, _scroll.Bounds.Y + 20f, 600f, 30f), Ctx.Style.TextSizes.Body, painter.C("inkSoft"), TextAlign.Left);
            }
        }

        private void DrawCustom(Widget widget)
        {
            if (!_rows.TryGetValue(widget, out BattleLogEntry entry))
            {
                return;
            }

            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            Rect box = widget.Bounds;
            float size = style.TextSizes.Body - 1f;
            if (entry.Kind == BattleLogKind.Turn)
            {
                painter.RoundedRect(new Rect(box.X + 6f, box.Y + 10f, box.Width - 12f, RowHeight - 6f), 16f, painter.C("plumSoft", 0.25f));
                painter.TextIn(entry.Text, new Rect(box.X + 26f, box.Y + 10f, box.Width - 52f, RowHeight - 6f), size, painter.C("plum"), TextAlign.Left);
                return;
            }

            bool open = entry == _open && entry.Breakdown != null;
            if (open)
            {
                painter.RoundedRect(new Rect(box.X + 6f, box.Y + 2f, box.Width - 12f, box.Height - 6f), 16f, painter.C("gold", 0.35f));
            }

            string color = KindColor(entry);
            painter.Disc(new Vec2(box.X + 34f, box.Y + RowHeight / 2f), 9f, painter.C(color));
            string text = entry.Text + (entry.Breakdown != null ? (open ? "  -" : "  +") : string.Empty);
            painter.TextIn(text, new Rect(box.X + 56f, box.Y, box.Width - 76f, RowHeight), size, painter.C(entry.Kind == BattleLogKind.Defeat ? "berry" : "ink"), TextAlign.Left);
            if (!open)
            {
                return;
            }

            float y = box.Y + RowHeight + 6f;
            foreach (KeyValuePair<string, string> line in entry.Breakdown.Lines(Ctx.Content.Text))
            {
                bool final = line.Key == Loc("ui.battle_log.final");
                painter.TextIn(line.Key, new Rect(box.X + 80f, y, 420f, BreakdownLine), size - 1f, painter.C(final ? "plum" : "inkSoft"), TextAlign.Left);
                painter.TextIn(line.Value, new Rect(box.X + 480f, y, box.Width - 540f, BreakdownLine), size - 1f, painter.C(final ? "plum" : "ink"), TextAlign.Right);
                y += BreakdownLine;
            }
        }

        private static string KindColor(BattleLogEntry entry)
        {
            switch (entry.Kind)
            {
                case BattleLogKind.Hit:
                    return entry.Crit ? "goldDeep" : "berry";
                case BattleLogKind.Heal:
                    return "leaf";
                case BattleLogKind.Shield:
                    return "sky";
                case BattleLogKind.DamageOverTime:
                    return "peach";
                case BattleLogKind.Bond:
                    return "gold";
                case BattleLogKind.Passive:
                    return "plumSoft";
                case BattleLogKind.Defeat:
                    return "plumDeep";
                default:
                    return "stoneDeep";
            }
        }
    }
}
