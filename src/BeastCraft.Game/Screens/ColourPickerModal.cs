using System;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The wardrobe's custom colour: three bars (hue, saturation, brightness) tapped or dragged
    /// (<see cref="SliderBar"/>), a preview of the colour, and Use colour, which hands the colour back
    /// (the wardrobe stores it through <c>CosmeticRules.TrySetColor</c>). Opaque: there is no alpha. The six
    /// swatches stay on the wardrobe as quick picks.
    /// </summary>
    public sealed class ColourPickerModal : GameModal
    {
        /// <summary>The bars are drawn in this many steps.</summary>
        private const int Steps = 48;

        private const float BarHeight = 70f;

        private readonly SliderBar _hue;
        private readonly SliderBar _saturation;
        private readonly SliderBar _value;
        private readonly Rect _preview;

        public ColourPickerModal(ScreenContext ctx, string categoryName, Hsv start, Action<Hsv> use) : base(ctx)
        {
            UiStyle style = ctx.Style;
            float width = 900f;
            float height = 1040f;
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 46f, width - 120f, 60f), Text = Loc("ui.avatar.custom_colour_title", categoryName), Size = style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            _preview = new Rect(card.Center.X - 160f, card.Y + 140f, 320f, 150f);

            float x = card.X + 90f;
            float barWidth = width - 180f;
            _hue = AddBar(panel, "hue", Loc("ui.avatar.hue"), new Rect(x, card.Y + 380f, barWidth, BarHeight), start.H);
            _saturation = AddBar(panel, "saturation", Loc("ui.avatar.saturation"), new Rect(x, card.Y + 540f, barWidth, BarHeight), start.S);
            _value = AddBar(panel, "value", Loc("ui.avatar.brightness"), new Rect(x, card.Y + 700f, barWidth, BarHeight), start.V);

            float buttonsY = card.Bottom - 160f;
            Button cancel = panel.Add(new Button { Id = "cancel", Bounds = new Rect(card.X + 60f, buttonsY, (width - 150f) / 2f, 110f), Text = Loc("ui.common.cancel"), StyleKey = "secondary" });
            cancel.Clicked += Close;
            Button ok = panel.Add(new Button { Id = "confirm", Bounds = new Rect(card.X + 90f + (width - 150f) / 2f, buttonsY, (width - 150f) / 2f, 110f), Text = Loc("ui.avatar.use_colour"), StyleKey = "primary" });
            ok.Clicked += () =>
            {
                Close();
                use?.Invoke(Colour);
            };
        }

        public override string Name
        {
            get { return "colour-picker"; }
        }

        /// <summary>The colour the bars make now.</summary>
        public Hsv Colour
        {
            get { return new Hsv(_hue.Value, _saturation.Value, _value.Value); }
        }

        private SliderBar AddBar(Panel panel, string id, string label, Rect bounds, float value)
        {
            panel.Add(new Label { Bounds = new Rect(bounds.X, bounds.Y - 56f, bounds.Width, 44f), Text = label, Size = Ctx.Style.TextSizes.Body, ColorKey = "ink" });
            return panel.Add(new SliderBar { Id = id, Bounds = bounds, Value = value });
        }

        public override void Draw()
        {
            base.Draw();
            UiPainter painter = Ctx.Painter;
            Hsv colour = Colour;
            painter.Framed(_preview, 20f, 5f, painter.C("plum"), painter.C(colour.ToHex()));
            DrawBar(_hue, t => new Hsv(t, 1f, 1f));
            DrawBar(_saturation, t => new Hsv(colour.H, t, Math.Max(colour.V, 0.35f)));
            DrawBar(_value, t => new Hsv(colour.H, colour.S, t));
        }

        /// <summary>A bar as <see cref="Steps"/> colour steps along it, with a knob at its value.</summary>
        private void DrawBar(SliderBar bar, Func<float, Hsv> at)
        {
            UiPainter painter = Ctx.Painter;
            Rect box = bar.Bounds;
            float step = box.Width / Steps;
            for (int i = 0; i < Steps; i++)
            {
                painter.Fill(new Rect(box.X + i * step, box.Y, step + 1f, box.Height), painter.C(at((i + 0.5f) / Steps).ToHex()));
            }

            float knob = box.X + bar.Value * box.Width;
            painter.Framed(new Rect(knob - 14f, box.Y - 10f, 28f, box.Height + 20f), 10f, 4f, painter.C("plum"), painter.C("white", 0.35f));
        }
    }
}
