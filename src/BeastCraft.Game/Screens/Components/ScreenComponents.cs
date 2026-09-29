using System;
using System.Collections.Generic;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens.Components
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// A small shared layer of screen composites, wrapping the existing widget toolkit
    /// (<c>Widgets.cs</c>, <see cref="UiPainter"/>, <see cref="UiStyle"/>, <see cref="GameModal"/>) —
    /// never replacing them. Every full-page screen before this (Roster/beast detail, Grove,
    /// Compendium) hand-rolled its own top-bar constant, its own private <c>Card(...)</c>/drawer-
    /// dictionary pair, its own beast/item card drawing and its own chip-row layout; that duplication
    /// caused the layout bugs (clipped rows, header/divider collisions, tab overlap) the Avatar,
    /// Inventory and Shop/Trader screens are built against instead. See docs/design/screens.md,
    /// "Shared components".
    /// </summary>
    public static class HeaderMetrics
    {
        public const float Pad = 36f;

        /// <summary>Back button + title (+ optional one-line subtitle): most screens.</summary>
        public const float Standard = 210f;

        /// <summary>+ a stat/identity line under the title (the beast detail screen's shape).</summary>
        public const float Tall = 330f;
    }

    /// <summary>
    /// A screen's fixed header: the back button, a title, an optional subtitle/stat line, and the
    /// divider under it, all at one of <see cref="HeaderMetrics"/>'s two standard heights. Build once
    /// in the screen's constructor; call <see cref="Paint"/> from <c>Draw()</c> after
    /// <c>base.Draw()</c> (it repaints the wash over whatever a scroll view painted underneath, then
    /// the back button, since both live above the header line). A screen with its own extra fixed
    /// widgets there (an inner tab bar, an extra button) repaints them the same way, right after.
    /// </summary>
    public sealed class ScreenHeader
    {
        public ScreenHeader(UiRoot ui, float height, Action onBack)
        {
            Height = height;
            Back = ui.Add(new Button { Id = "back", Bounds = new Rect(HeaderMetrics.Pad, 40f, 110f, 110f), StyleKey = "secondary", Glyph = "back" });
            if (onBack != null)
            {
                Back.Clicked += onBack;
            }
        }

        public float Height { get; }

        public Button Back { get; }

        public void Paint(ScreenContext ctx, UiRoot ui, string title, string subtitle = null)
        {
            UiPainter painter = ctx.Painter;
            painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, Height), painter.C("cream"));
            painter.Fill(new Rect(0, Height - 5f, PortraitLayout.CanvasWidth, 5f), painter.C("plumSoft", 0.5f));
            painter.Paint(Back, ui);
            UiStyle style = ctx.Style;
            painter.TextIn(title ?? string.Empty, new Rect(180f, 44f, PortraitLayout.CanvasWidth - 180f - HeaderMetrics.Pad, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f,
                           painter.C("plum"), TextAlign.Left);
            if (!string.IsNullOrEmpty(subtitle))
            {
                painter.TextIn(subtitle, new Rect(180f, 112f, PortraitLayout.CanvasWidth - 180f - HeaderMetrics.Pad, 30f), style.TextSizes.Body, painter.C("inkSoft"), TextAlign.Left);
            }
        }

        /// <summary>Repaints one more fixed widget above the header wash (an inner tab bar, an extra header button) — call right after <see cref="Paint"/>.</summary>
        public static void Repaint(ScreenContext ctx, UiRoot ui, Widget widget)
        {
            if (widget != null)
            {
                ctx.Painter.Paint(widget, ui);
            }
        }
    }

    /// <summary>A card's heading line (the same look every card heading used, once).</summary>
    public static class SectionHeader
    {
        public static void Draw(ScreenContext ctx, Rect box, string text)
        {
            ctx.Painter.TextIn(text, new Rect(box.X + 36f, box.Y + 28f, box.Width - 72f, ctx.Style.TextSizes.Heading - 6f), ctx.Style.TextSizes.Heading - 6f, ctx.Painter.C("plum"),
                               TextAlign.Left);
        }
    }

    /// <summary>
    /// A vertical stack of panel "cards" in a <see cref="ScrollView"/>, each with its own custom draw
    /// callback — the <c>Card(y, height, style, draw)</c> + <c>Dictionary&lt;Widget, Action&lt;Rect&gt;&gt;</c>
    /// pair every hand-built page (beast detail, Grove, Compendium) wrote for itself. A screen keeps
    /// one <see cref="CardList"/> per <see cref="ScrollView"/> it shows, rebuilding it in a <c>Build()</c>
    /// method: <see cref="Begin"/>, add rows/cards, then <see cref="End"/>; route the screen's
    /// <c>DrawCustom(Widget)</c> through <see cref="TryDraw"/>.
    /// </summary>
    public sealed class CardList
    {
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, Action<Rect>> _drawers = new Dictionary<Widget, Action<Rect>>();
        private float _resumeScrollY;

        public CardList(ScrollView scroll)
        {
            _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        }

        public ScrollView Scroll => _scroll;

        public float Width => _scroll.Bounds.Width - 2f * HeaderMetrics.Pad;

        /// <summary>Clears the scroll view for a rebuild, remembering its scroll position.</summary>
        public void Begin()
        {
            _resumeScrollY = _scroll.ScrollY;
            _scroll.ClearChildren();
            _drawers.Clear();
        }

        /// <summary>Adds one full-width panel card at <paramref name="y"/>, drawn by <paramref name="draw"/> in its own bounds; returns the next <paramref name="y"/>.</summary>
        public float Card(float y, float height, string style, Action<Rect> draw)
        {
            Panel panel = _scroll.Add(new Panel { Bounds = new Rect(HeaderMetrics.Pad, y, Width, height), StyleKey = style });
            _drawers[panel] = draw;
            return y + height + 26f;
        }

        /// <summary>Adds any other widget (a button, a hotspot) straight to the scroll's content.</summary>
        public T Add<T>(T widget) where T : Widget
        {
            return _scroll.Add(widget);
        }

        /// <summary>Sets the scroll view's content height from the last <paramref name="y"/> and restores its scroll position.</summary>
        public void End(float y)
        {
            _scroll.ContentHeight = y + 40f;
            _scroll.ScrollTo(_resumeScrollY);
        }

        /// <summary>Looks a widget up for <c>DrawCustom</c>.</summary>
        public bool TryDraw(Widget widget, out Action<Rect> draw)
        {
            return _drawers.TryGetValue(widget, out draw);
        }
    }

    /// <summary>A wrapping row of chip buttons (a filter, a sort cycle, a set of options) — one label per chip, at most one selected.</summary>
    public static class ChipRow
    {
        public const float Height = 76f;
        public const float Gap = 14f;

        /// <summary>Adds the chips from (<paramref name="x"/>, <paramref name="y"/>), wrapping within <paramref name="maxWidth"/>; returns the bottom y.</summary>
        public static float Build(ScreenContext ctx, Widget parent, float x, float y, float maxWidth, IReadOnlyList<string> labels, int selected, string idPrefix, Action<int> onTap)
        {
            float chipTextSize = ctx.Style.TextSizes.Small + 2f;
            float cx = x;
            float rowY = y;
            for (int i = 0; i < labels.Count; i++)
            {
                float w = ctx.Text.Measure(labels[i], chipTextSize) + 56f;
                if (cx + w > x + maxWidth && cx > x)
                {
                    cx = x;
                    rowY += Height + Gap;
                }

                int index = i;
                Button chip = parent.Add(new Button { Id = idPrefix + i, Bounds = new Rect(cx, rowY, w, Height), Text = labels[i], StyleKey = "chip", Selected = i == selected });
                chip.Clicked += () => onTap?.Invoke(index);
                cx += w + Gap;
            }

            return rowY + Height;
        }
    }

    /// <summary>One row of a <see cref="StatTable"/>: a name and two right-aligned columns (e.g. base and total).</summary>
    public sealed class StatTableRow
    {
        public string Name;
        public string Left;
        public string Right;
    }

    /// <summary>A label/value(s) table: one name column, up to two right-aligned numeric columns with headers.</summary>
    public static class StatTable
    {
        public const float RowHeight = 56f;

        public static float Draw(ScreenContext ctx, Rect box, string leftHeader, string rightHeader, IReadOnlyList<StatTableRow> rows)
        {
            UiPainter painter = ctx.Painter;
            float size = ctx.Style.TextSizes.Body;
            float small = ctx.Style.TextSizes.Small + 1f;
            float x = box.X + 40f;
            float w = box.Width - 80f;
            float nameW = w * 0.46f;
            float colW = (w - nameW) / 2f;
            float y = box.Y + 28f;
            painter.TextIn(leftHeader ?? string.Empty, new Rect(x + nameW, y, colW, small), small, painter.C("inkSoft"), TextAlign.Right);
            painter.TextIn(rightHeader ?? string.Empty, new Rect(x + nameW + colW, y, colW, small), small, painter.C("inkSoft"), TextAlign.Right);
            y += ctx.Text.LineHeight(small) + 6f;
            foreach (StatTableRow row in rows)
            {
                painter.TextIn(row.Name, new Rect(x, y, nameW, size), size, painter.C("ink"), TextAlign.Left);
                painter.TextIn(row.Left, new Rect(x + nameW, y, colW, size), size, painter.C("inkSoft"), TextAlign.Right);
                painter.TextIn(row.Right, new Rect(x + nameW + colW, y, colW, size), size, painter.C("leafDeep"), TextAlign.Right);
                y += RowHeight;
            }

            return y;
        }
    }

    /// <summary>What one <see cref="ItemRow"/> shows: a title, an optional subtitle line (bonuses, a description) and an optional detail line (a disabled reason, a price).</summary>
    public sealed class ItemRowData
    {
        public string Title;
        public string TitleColor = "ink";
        public string Subtitle;
        public string Detail;
        public string DetailColor = "inkSoft";
    }

    /// <summary>A list row: title, subtitle, detail line — the shape Inventory's and the Trader's listings share.</summary>
    public static class ItemRow
    {
        public const float Height = 150f;

        /// <summary><paramref name="rightInset"/> leaves room for an <see cref="ActionRow"/> beside the text.</summary>
        public static void Draw(ScreenContext ctx, Rect box, ItemRowData row, float rightInset = 0f)
        {
            UiPainter painter = ctx.Painter;
            float x = box.X + 36f;
            float w = box.Width - 72f - rightInset;
            float body = ctx.Style.TextSizes.Body;
            float small = ctx.Style.TextSizes.Small + 1f;
            painter.TextIn(row.Title ?? string.Empty, new Rect(x, box.Y + 18f, w, body), body, painter.C(row.TitleColor), TextAlign.Left);
            if (!string.IsNullOrEmpty(row.Subtitle))
            {
                painter.TextIn(row.Subtitle, new Rect(x, box.Y + 62f, w, small), small, painter.C("inkSoft"), TextAlign.Left);
            }

            if (!string.IsNullOrEmpty(row.Detail))
            {
                painter.TextIn(row.Detail, new Rect(x, box.Y + 102f, w, small), small, painter.C(row.DetailColor), TextAlign.Left);
            }
        }
    }

    /// <summary>One action button an <see cref="ActionRow"/> places.</summary>
    public sealed class ActionButtonData
    {
        public string Id;
        public string Text;
        public bool Enabled = true;
        public Action OnClick;
        public string Style = "chip";
    }

    /// <summary>One or more buttons stacked top-to-bottom, right-aligned within an area (an item row's Equip/Sell, a slot's Take off).</summary>
    public static class ActionRow
    {
        public static void Build(Widget parent, Rect area, float buttonWidth, float buttonHeight, IReadOnlyList<ActionButtonData> buttons)
        {
            float y = area.Y;
            foreach (ActionButtonData data in buttons)
            {
                if (data == null)
                {
                    continue;
                }

                Button button = parent.Add(new Button { Id = data.Id, Bounds = new Rect(area.Right - buttonWidth, y, buttonWidth, buttonHeight), Text = data.Text, StyleKey = data.Style, Enabled = data.Enabled });
                if (data.OnClick != null)
                {
                    button.Clicked += data.OnClick;
                }

                y += buttonHeight + 14f;
            }
        }
    }

    /// <summary>A small portrait (or a plain disc while none is given) beside a name and subtitle — a beast picker's row.</summary>
    public static class BeastCard
    {
        public const float Height = 140f;

        public static void Draw(ScreenContext ctx, Rect box, string artKey, string tintHex, string name, string subtitle)
        {
            UiPainter painter = ctx.Painter;
            Rect portrait = new Rect(box.X + 16f, box.Y + 12f, box.Height - 24f, box.Height - 24f);
            if (!string.IsNullOrEmpty(artKey))
            {
                Color? tint = string.IsNullOrEmpty(tintHex) ? (Color?)null : painter.C(tintHex);
                painter.Art(painter.Sprite(artKey), portrait, false, tint);
            }
            else
            {
                painter.Disc(portrait.Center, portrait.Width / 2f, painter.C("plumSoft"));
            }

            float x = portrait.Right + 24f;
            float w = box.Right - x - 16f;
            painter.TextIn(name ?? string.Empty, new Rect(x, box.Y + 24f, w, ctx.Style.TextSizes.Body), ctx.Style.TextSizes.Body, painter.C("ink"), TextAlign.Left);
            if (!string.IsNullOrEmpty(subtitle))
            {
                painter.TextIn(subtitle, new Rect(x, box.Y + 70f, w, ctx.Style.TextSizes.Small + 1f), ctx.Style.TextSizes.Small + 1f, painter.C("inkSoft"), TextAlign.Left);
            }
        }
    }

    /// <summary>One choice a <see cref="BeastPickerModal"/> offers.</summary>
    public sealed class BeastPickerOption
    {
        public string Id;
        public string Name;
        public string Subtitle;
        public string ArtKey;
        public string TintHex;
        public bool Enabled = true;
        public string Reason;
    }

    /// <summary>A titled list of beasts to pick one of (equip gear to a beast, buy a tome for one), each row a <see cref="BeastCard"/>.</summary>
    public sealed class BeastPickerModal : GameModal
    {
        private readonly Dictionary<Widget, BeastPickerOption> _rows = new Dictionary<Widget, BeastPickerOption>();

        public BeastPickerModal(ScreenContext ctx, string title, string emptyMessage, IReadOnlyList<BeastPickerOption> options, Action<string> onPick) : base(ctx)
        {
            float width = 960f;
            float rowHeight = BeastCard.Height + 14f;
            float rowsHeight = options.Count == 0 ? 90f : options.Count * rowHeight;
            float height = Math.Min(PortraitLayout.CanvasHeight - 120f, 170f + rowsHeight + 160f);
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 46f, width - 120f, 60f), Text = title, Size = ctx.Style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            float y = card.Y + 140f;
            if (options.Count == 0)
            {
                panel.Add(new Label { Bounds = new Rect(card.X + 60f, y, width - 120f, 60f), Text = emptyMessage ?? "None available.", Size = ctx.Style.TextSizes.Body, ColorKey = "inkSoft", Align = TextAlign.Center });
            }

            foreach (BeastPickerOption option in options)
            {
                Rect rowBounds = new Rect(card.X + 60f, y, width - 120f, BeastCard.Height);
                Panel row = panel.Add(new Panel { Bounds = rowBounds, StyleKey = option.Enabled ? "card" : "slot" });
                _rows[row] = option;
                if (option.Enabled)
                {
                    Hotspot tap = panel.Add(new Hotspot { Id = "pick-" + option.Id, Bounds = rowBounds });
                    tap.Clicked += _ =>
                    {
                        Close();
                        onPick?.Invoke(option.Id);
                    };
                }

                y += rowHeight;
            }

            Button close = panel.Add(new Button { Id = "close", Bounds = new Rect(card.Center.X - 200f, card.Bottom - 140f, 400f, 100f), Text = "Close", StyleKey = "secondary" });
            close.Clicked += Close;
            Name = "beast-picker:" + title;
        }

        public override string Name { get; }

        public override void Draw()
        {
            base.Draw();
            foreach (KeyValuePair<Widget, BeastPickerOption> entry in _rows)
            {
                BeastPickerOption option = entry.Value;
                BeastCard.Draw(Ctx, entry.Key.Bounds, option.ArtKey, option.TintHex, option.Name, option.Enabled ? option.Subtitle : (option.Reason ?? option.Subtitle));
            }
        }
    }
}
