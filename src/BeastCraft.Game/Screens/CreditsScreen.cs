using System.Collections.Generic;
using System.Reflection;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// About &amp; Credits (menu-screens pass, #67): the game's name, tagline and build version, a
    /// DRAFT producer-credit placeholder (<c>ui.credits.producer_draft</c> — the producer fills this in
    /// later; no personal name here), a short third-party notices summary pointing at
    /// <c>THIRD-PARTY-NOTICES.md</c>, and the AI-assistance disclosure
    /// (<c>docs/art/art-brief.md</c>, decision 10: some beast and enemy art was made with an
    /// AI-assisted pipeline, each asset producer-approved). Scrollable; reached from the title and
    /// every Settings screen's footer link. Built on the shared <see cref="Components"/> layer.
    /// </summary>
    public sealed class CreditsScreen : GameScreen
    {
        private readonly ScreenHeader _header;
        private readonly CardList _cards;

        public CreditsScreen(ScreenContext ctx) : base(ctx)
        {
            Rect page = new Rect(0, HeaderMetrics.Standard, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - HeaderMetrics.Standard);
            _cards = new CardList(Ui.Add(new ScrollView { Id = "credits-page", Bounds = page }));
            _header = new ScreenHeader(Ui, HeaderMetrics.Standard, () => Ctx.Stack.Pop());
            Build();
        }

        public override string Name => "credits";

        private void Build()
        {
            _cards.Begin();
            float width = _cards.Width;
            float small = Ctx.Style.TextSizes.Small + 1f;
            float y = 10f;

            // About: the name, tagline, footer note and the build's version.
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev";
            y = AddCard(y, 300f, "ui.title.name", box =>
            {
                AddLine(box, 70f, Loc("ui.title.tagline"), Ctx.Style.TextSizes.Body, "ink");
                AddLine(box, 116f, Loc("ui.credits.version", version), small, "inkSoft");
                AddLine(box, 156f, Loc("ui.title.footer"), small, "inkSoft");
            });

            // Producer credit: a DRAFT placeholder (producer decision, 2026-09-30) until it is filled in.
            y = AddCard(y, 150f, "ui.credits.section_producer", box => AddLine(box, 70f, Loc("ui.credits.producer_draft"), Ctx.Style.TextSizes.Body, "ink"));

            // Third-party notices: a short summary; the full text and licences are in THIRD-PARTY-NOTICES.md.
            y = AddTextCard(y, small, "ui.credits.section_third_party", "ui.credits.third_party_summary");

            // The AI-assistance disclosure (docs/art/art-brief.md, decision 10).
            y = AddTextCard(y, small, "ui.credits.section_ai", "ui.credits.ai_disclosure");

            _cards.End(y);
        }

        /// <summary>A card at <paramref name="y"/> with a <see cref="SectionHeader"/> and whatever <paramref name="content"/> draws beneath it; returns the next y.</summary>
        private float AddCard(float y, float height, string headingKey, System.Action<Rect> content)
        {
            return _cards.Card(y, height, "card", box =>
            {
                SectionHeader.Draw(Ctx, box, Loc(headingKey));
                content?.Invoke(box);
            });
        }

        /// <summary>A card with a heading and one wrapped paragraph; returns the next y.</summary>
        private float AddTextCard(float y, float size, string headingKey, string bodyKey)
        {
            string body = Loc(bodyKey);
            float textWidth = _cards.Width - 72f;
            List<string> lines = Ctx.Painter.Wrap(body, size, textWidth);
            float bodyHeight = lines.Count * Ctx.Text.LineHeight(size);
            return AddCard(y, 96f + bodyHeight + 30f, headingKey, box => AddLine(box, 90f, body, size, "ink", bodyHeight, true));
        }

        /// <summary>One line (or, with <paramref name="wrap"/>, a wrapped block of <paramref name="height"/>) under the heading, inset to match <see cref="SectionHeader"/>.</summary>
        private void AddLine(Rect box, float yOffset, string text, float size, string colorKey, float height = 36f, bool wrap = false)
        {
            Rect line = new Rect(box.X + 36f, box.Y + yOffset, box.Width - 72f, height);
            if (wrap)
            {
                float lineHeight = Ctx.Text.LineHeight(size);
                float lineY = line.Y;
                foreach (string part in Ctx.Painter.Wrap(text, size, line.Width))
                {
                    Ctx.Painter.TextIn(part, new Rect(line.X, lineY, line.Width, size), size, Ctx.Painter.C(colorKey), TextAlign.Left, false);
                    lineY += lineHeight;
                }

                return;
            }

            Ctx.Painter.TextIn(text, line, size, Ctx.Painter.C(colorKey), TextAlign.Left);
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            _header.Paint(Ctx, Ui, Loc("ui.credits.title"));
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_cards.TryDraw(widget, out System.Action<Rect> draw))
            {
                draw(widget.Bounds);
            }
        }
    }
}
