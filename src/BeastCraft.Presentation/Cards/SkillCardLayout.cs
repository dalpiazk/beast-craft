using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Text;

namespace BeastCraft.Presentation.Cards
{
    /// <summary>
    /// Where everything on the skill detail card goes, for one <see cref="SkillCard"/> in the room
    /// it may take (its foot on the room's foot, as tall as its content needs: <see cref="Card"/>), measured by the renderer's text widths: the icon and name across the top, the
    /// tag chips, then the stats line (cooldown, range, uses), the power lines and the scaling —
    /// each word-wrapped to the card's full width — and below them the split: the hex range diagram
    /// on the left and the description (rich text, wrapped to its column) on the right, with a hint
    /// line under it when it has glossary terms. The power block gives way (fewer lines) before the
    /// split gets shorter than <see cref="MinSplitHeight"/>. All positions are canvas pixels; the
    /// text layouts' runs are relative to their <c>*At</c> origins (the description's to <see cref="Text"/>). Pure maths.
    /// </summary>
    public sealed class SkillCardLayout
    {
        public const float Padding = 28f;
        public const float IconSize = 120f;
        public const float NameSize = 34f;
        public const float StatsSize = 21f;
        public const float PowerSize = 19f;
        public const float ScalingSize = 16f;
        public const float DescriptionSize = 21f;
        public const float HintSize = 15f;
        public const float DiagramWidth = 440f;
        public const float MinSplitHeight = 300f;

        private SkillCardLayout()
        {
        }

        /// <summary>The card itself: the area's width, as tall as its content, its foot on the area's.</summary>
        public Rect Card { get; private set; }

        public Rect Icon { get; private set; }

        /// <summary>The name's top-left and the width it may take.</summary>
        public Rect Name { get; private set; }

        /// <summary>The top of the tag chips' row; they start at <see cref="Name"/>'s left.</summary>
        public float ChipsY { get; private set; }

        public Vec2 StatsAt { get; private set; }

        public RichTextLayout Stats { get; private set; }

        public Vec2 PowerAt { get; private set; }

        /// <summary>The power lines, one paragraph each, then the scaling line (wrapped, full width).</summary>
        public RichTextLayout Power { get; private set; }

        public Vec2 ScalingAt { get; private set; }

        public RichTextLayout Scaling { get; private set; }

        public Rect Diagram { get; private set; }

        /// <summary>The description's column (its runs are relative to its top-left).</summary>
        public Rect Text { get; private set; }

        public RichTextLayout Description { get; private set; }

        /// <summary>The block the description's lines actually take (what a glossary popup keeps clear of).</summary>
        public Rect DescriptionBlock
        {
            get { return new Rect(Text.X, Text.Y, Text.Width, Description.Height); }
        }

        /// <summary>Where the "tap a highlighted word" hint goes (null when the description has no terms).</summary>
        public float? HintY { get; private set; }

        /// <summary>
        /// Lays <paramref name="card"/> out in <paramref name="area"/>, the most room it may take
        /// (the card is as tall as its content needs, its foot on the area's). <paramref name="measure"/> is
        /// the width of a string at a size; <paramref name="lineHeight"/> the line height at a size.
        /// </summary>
        public static SkillCardLayout Of(SkillCard card, Rect area, Func<string, float, float> measure, Func<float, float> lineHeight)
        {
            // Laid out top-down from the area's top, then moved down so the card's foot sits on the area's.
            SkillCardLayout layout = new SkillCardLayout();
            float left = area.X + Padding;
            float width = area.Width - 2f * Padding;
            layout.Icon = new Rect(left, area.Y + Padding, IconSize, IconSize);
            float nameLeft = layout.Icon.Right + Padding;
            layout.Name = new Rect(nameLeft, layout.Icon.Y + 8f, area.Right - Padding - nameLeft, NameSize);
            layout.ChipsY = layout.Icon.Y + 70f;

            float y = layout.Icon.Bottom + 22f;
            layout.StatsAt = new Vec2(left, y);
            layout.Stats = RichTextLayout.Of(Plain(StatsText(card)), s => measure(s, StatsSize), width, lineHeight(StatsSize));
            y += layout.Stats.Height + 10f;

            // The split may start no lower than this, so the diagram and description keep their room.
            float splitLimit = area.Bottom - Padding - MinSplitHeight;
            float powerLine = lineHeight(PowerSize);
            float scalingLine = card.Scaling == null ? 0f : lineHeight(ScalingSize) + 4f;
            int powerLines = Math.Max(1, (int)((splitLimit - 20f - y - scalingLine) / powerLine));
            layout.PowerAt = new Vec2(left, y);
            layout.Power = RichTextLayout.Of(Plain(string.Join("\n", card.Power ?? new string[0])), s => measure(s, PowerSize), width, powerLine, powerLines);
            y += layout.Power.Height + 4f;
            layout.ScalingAt = new Vec2(left, y);
            layout.Scaling = RichTextLayout.Of(Plain(card.Scaling), s => measure(s, ScalingSize), width, lineHeight(ScalingSize), card.Scaling == null ? 0 : 1);
            y += layout.Scaling.Height;

            float split = Math.Min(splitLimit, y + 20f);
            float diagramWidth = Math.Min(DiagramWidth, width / 2f);
            float textLeft = left + diagramWidth + Padding;
            float textWidth = area.Right - Padding - textLeft;

            bool terms = false;
            foreach (RichSpan span in card.Description ?? new RichSpan[0])
            {
                terms |= span.Term != null;
            }

            float hint = terms ? lineHeight(HintSize) + 8f : 0f;
            float descriptionLine = lineHeight(DescriptionSize);
            float room = area.Bottom - Padding - split;
            int maxLines = Math.Max(1, (int)((room - hint) / descriptionLine));
            layout.Description = RichTextLayout.Of(card.Description, s => measure(s, DescriptionSize), textWidth, descriptionLine, maxLines);

            // The split is as tall as the diagram needs or the description takes, whichever is more.
            float splitHeight = Math.Min(room, Math.Max(MinSplitHeight, layout.Description.Height + hint));
            float bottom = split + splitHeight + Padding;
            float shift = area.Bottom - bottom;
            layout.Card = new Rect(area.X, area.Y + shift, area.Width, bottom - area.Y);
            layout.Icon = Down(layout.Icon, shift);
            layout.Name = Down(layout.Name, shift);
            layout.ChipsY += shift;
            layout.StatsAt = new Vec2(layout.StatsAt.X, layout.StatsAt.Y + shift);
            layout.PowerAt = new Vec2(layout.PowerAt.X, layout.PowerAt.Y + shift);
            layout.ScalingAt = new Vec2(layout.ScalingAt.X, layout.ScalingAt.Y + shift);
            layout.Diagram = new Rect(left, split + shift, diagramWidth, splitHeight);
            layout.Text = new Rect(textLeft, split + shift, textWidth, splitHeight);
            layout.HintY = terms ? layout.Text.Bottom - lineHeight(HintSize) : (float?)null;
            return layout;
        }

        private static Rect Down(Rect rect, float by)
        {
            return new Rect(rect.X, rect.Y + by, rect.Width, rect.Height);
        }

        /// <summary>The glossary term under canvas point (<paramref name="x"/>, <paramref name="y"/>) in the description, or null.</summary>
        public GlossaryTerm TermAt(float x, float y)
        {
            return Description.TermAt(x - Text.X, y - Text.Y);
        }

        /// <summary>The canvas rectangle of <paramref name="term"/>'s first run in the description (null when it is not there).</summary>
        public Rect? TermRect(GlossaryTerm term)
        {
            RichRun? run = Description.RunOf(term);
            if (!run.HasValue)
            {
                return null;
            }

            return new Rect(Text.X + run.Value.X, Text.Y + run.Value.Y, run.Value.Width, Description.LineHeight);
        }

        /// <summary>
        /// Where <paramref name="term"/>'s definition popup of <paramref name="width"/> x
        /// <paramref name="height"/> goes: clear of the term and the description block, below them
        /// if it fits in the card, else above, clamped into the card (see <see cref="PopupPlacement"/>).
        /// A term that is not in the description anchors on the description's column.
        /// </summary>
        public PopupPlacement PlacePopup(GlossaryTerm term, float width, float height)
        {
            Rect anchor = TermRect(term) ?? new Rect(Text.X, Text.Y, Text.Width, Math.Max(1f, Description.Height));
            return PopupPlacement.Place(anchor, DescriptionBlock, width, height, Card.Inset(12f));
        }

        /// <summary>"Cooldown 2   |   Range 3   |   Once per battle".</summary>
        public static string StatsText(SkillCard card)
        {
            List<string> parts = new List<string> { card.Cooldown, card.Range };
            if (card.Uses != null)
            {
                parts.Add(card.Uses);
            }

            return string.Join("   |   ", parts.FindAll(p => !string.IsNullOrEmpty(p)));
        }

        private static RichSpan[] Plain(string text)
        {
            return string.IsNullOrEmpty(text) ? new RichSpan[0] : new[] { new RichSpan(text, null) };
        }
    }
}
