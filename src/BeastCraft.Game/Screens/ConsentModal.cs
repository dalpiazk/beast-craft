using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The one-time consent screen (<see cref="ConsentViewModel"/>, #62): what is asked in plain words, a row
    /// each for anonymous gameplay events and crash reports (both off until turned on), and Continue. Back
    /// does not skip it: the player answers, and can change either later in the settings.
    /// </summary>
    public sealed class ConsentModal : GameModal
    {
        private const float Width = 920f;
        private const float RowHeight = 110f;

        private readonly ConsentViewModel _model;
        private readonly Action _done;
        private readonly Button _analytics;
        private readonly Button _crashes;

        public ConsentModal(ScreenContext ctx, ConsentViewModel model, Action done) : base(ctx)
        {
            _model = model;
            _done = done;
            UiStyle style = ctx.Style;
            string body = Loc("ui.consent.body");
            List<string> lines = ctx.Painter.Wrap(body, style.TextSizes.Body, Width - 120f);
            float bodyHeight = lines.Count * ctx.Text.LineHeight(style.TextSizes.Body);
            float height = 150f + bodyHeight + 50f + 2f * (RowHeight + 20f) + 40f + 150f;
            Rect card = new Rect((PortraitLayout.CanvasWidth - Width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, Width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 50f, Width - 120f, 60f), Text = Loc("ui.consent.title"), Size = style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 140f, Width - 120f, bodyHeight), Text = body, Size = style.TextSizes.Body, ColorKey = "ink", Align = TextAlign.Center, Wrap = true });
            float y = card.Y + 140f + bodyHeight + 50f;
            _analytics = panel.Add(new Button { Id = "analytics", Bounds = new Rect(card.X + 60f, y, Width - 120f, RowHeight), StyleKey = "secondary" });
            _analytics.Clicked += () => _model.Analytics = !_model.Analytics;
            _crashes = panel.Add(new Button { Id = "crash-reports", Bounds = new Rect(card.X + 60f, y + RowHeight + 20f, Width - 120f, RowHeight), StyleKey = "secondary" });
            _crashes.Clicked += () => _model.CrashReports = !_model.CrashReports;
            Button go = panel.Add(new Button { Id = "continue", Bounds = new Rect(card.Center.X - 220f, card.Bottom - 150f, 440f, 110f), Text = Loc("ui.consent.continue"), StyleKey = "primary" });
            go.Clicked += Confirm;
        }

        public override string Name
        {
            get { return "consent"; }
        }

        public ConsentViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Back does nothing here: the screen is answered with Continue.</summary>
        public override bool HandleBack()
        {
            return true;
        }

        /// <summary>Stores the choices (<see cref="ConsentViewModel.Confirm"/>) and closes.</summary>
        public void Confirm()
        {
            _model.Confirm();
            Close();
            _done?.Invoke();
        }

        public override void Draw()
        {
            base.Draw();
            DrawRow(_analytics, Loc("ui.settings.analytics"), _model.Analytics);
            DrawRow(_crashes, Loc("ui.settings.crash_reports"), _model.CrashReports);
        }

        private void DrawRow(Button row, string label, bool on)
        {
            Rect box = row.Bounds;
            float size = Ctx.Style.TextSizes.Body + 4f;
            Ctx.Painter.TextIn(label, new Rect(box.X + 40f, box.Y, box.Width * 0.65f, box.Height), size, Ctx.Painter.C("ink"), TextAlign.Left);
            Ctx.Painter.TextIn(Loc(on ? "ui.settings.on" : "ui.settings.off"), new Rect(box.Center.X, box.Y, box.Width / 2f - 40f, box.Height), size, Ctx.Painter.C(on ? "leafDeep" : "berry"),
                               TextAlign.Right);
        }
    }
}
