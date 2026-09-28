using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using BeastCraft.Tutorial;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>Shared drawing for the pickers, the dialogue box and the camp.</summary>
    internal static class TutorialArt
    {
        /// <summary>The stance chip's colour key.</summary>
        public static string StanceKey(CombatStance stance)
        {
            return stance == CombatStance.Vanguard ? "node_gate" : stance == CombatStance.Ranged ? "node_elite" : "node_battle";
        }

        /// <summary>A small rounded chip with the stance's name.</summary>
        public static void StanceChip(ScreenContext ctx, CombatStance stance, Rect box)
        {
            ctx.Painter.Framed(box, box.Height / 2f, 3f, ctx.Painter.C("plum"), ctx.Painter.C(StanceKey(stance)));
            ctx.Painter.TextIn(stance.ToString(), box, ctx.Style.TextSizes.Small + 1f, ctx.Painter.C("cream"), TextAlign.Center);
        }

        /// <summary>The mentor's placeholder portrait: their initial on a disc (until the Keeper's portrait art lands).</summary>
        public static void Portrait(ScreenContext ctx, string speaker, string portraitKey, Vec2 center, float radius)
        {
            ctx.Painter.Disc(center, radius + 6f, ctx.Painter.C("plum"));
            ctx.Painter.Disc(center, radius, ctx.Painter.C("leaf"));
            ctx.Painter.Soft(new Vec2(center.X - radius * 0.3f, center.Y - radius * 0.35f), radius * 0.45f, ctx.Painter.C("white", 0.3f));
            if (!string.IsNullOrEmpty(portraitKey) && ctx.Painter.Sprite(portraitKey) != null)
            {
                ctx.Painter.Art(ctx.Painter.Sprite(portraitKey), new Rect(center.X - radius, center.Y - radius, 2f * radius, 2f * radius), false);
                return;
            }

            string initial = string.IsNullOrEmpty(speaker) ? "?" : speaker.Replace("The ", string.Empty).Substring(0, 1);
            ctx.Painter.TextIn(initial, new Rect(center.X - radius, center.Y - radius, 2f * radius, 2f * radius), ctx.Style.TextSizes.Title, ctx.Painter.C("cream"), TextAlign.Center,
                               true, ctx.Painter.C("plum"));
        }

        /// <summary>Wrapped text from <paramref name="y"/> down; returns the y below it.</summary>
        public static float Paragraph(ScreenContext ctx, string text, float x, float y, float width, float size, Color ink, int maxLines = 0)
        {
            foreach (string line in ctx.Painter.Wrap(text ?? string.Empty, size, width, maxLines))
            {
                ctx.Painter.TextIn(line, new Rect(x, y, width, size), size, ink, TextAlign.Left, false);
                y += ctx.Text.LineHeight(size);
            }

            return y;
        }
    }

    /// <summary>
    /// The beast picker as a full screen (<see cref="StarterPickViewModel"/>): the New Game pick of all
    /// ten beasts (large illustrated portraits, stance and element, a personality blurb, the stance
    /// explainer), with the way to skip the tutorial, which runs the same picker three times in a row
    /// (one stance at a time). A choice starts the game on the map.
    /// </summary>
    public sealed class StarterPickScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float Top = 360f;
        private const float BottomBar = 230f;

        private readonly StarterPickViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Button _choose;
        private readonly Button _skip;
        private readonly Dictionary<Widget, PickOptionView> _cards = new Dictionary<Widget, PickOptionView>();
        private string _selected;

        public StarterPickScreen(ScreenContext ctx, PickMode mode = PickMode.NewGame) : base(ctx)
        {
            _model = new StarterPickViewModel(ctx.Session, mode);
            _scroll = Ui.Add(new ScrollView { Id = "picks", Bounds = new Rect(0, Top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - Top - BottomBar) });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => HandleBack(), "back");
            _skip = AddButton(null, "skip", new Rect(PortraitLayout.CanvasWidth - Pad - 360f, 40f, 360f, 100f), "Skip the tutorial", "ghost", Skip);
            _skip.Visible = mode == PickMode.NewGame;
            _choose = AddButton(null, "choose", new Rect(Pad + 60f, PortraitLayout.CanvasHeight - BottomBar + 60f, PortraitLayout.CanvasWidth - 2f * Pad - 120f, 140f), "Choose",
                                "primary", Confirm);
            Build();
        }

        public override string Name
        {
            get { return "starter-pick"; }
        }

        public StarterPickViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Selects <paramref name="speciesId"/> (a tap on its card; scripted runs).</summary>
        public void Select(string speciesId)
        {
            _selected = speciesId;
            foreach (KeyValuePair<Widget, PickOptionView> card in _cards)
            {
                ((Button)card.Key).Selected = card.Value.SpeciesId == speciesId;
            }

            PickOptionView option = _model.Options.Find(o => o.SpeciesId == speciesId);
            _choose.Enabled = option != null;
            _choose.Text = option == null ? "Choose" : "Choose " + option.Name;
        }

        /// <summary>Confirms the selected beast: the game starts (or the skip's next pick is asked for).</summary>
        public void Confirm()
        {
            if (_selected == null)
            {
                return;
            }

            if (_model.Choose(_selected, out string error))
            {
                Ctx.Stack.Replace(new HomeScreen(Ctx));
                return;
            }

            if (error != null)
            {
                Ctx.Game.Toast(error);
                return;
            }

            _selected = null;
            Build();
        }

        /// <summary>The skip: the same picker, three picks in a row, then straight to Verdant Hollow.</summary>
        public void Skip()
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Skip the tutorial?",
                                                 "You still choose your three beasts, one stance at a time, and get the Keeper's gifts, but skip Hearthglen's fights and tips.", "Stay",
                                                 "Skip", () => Ctx.Stack.Replace(new StarterPickScreen(Ctx, PickMode.Skip))));
        }

        public override bool HandleBack()
        {
            if (_model.Undo())
            {
                _selected = null;
                Build();
                return true;
            }

            Ctx.Stack.Pop();
            return true;
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _cards.Clear();
            float width = (PortraitLayout.CanvasWidth - 2f * Pad - 24f) / 2f;
            for (int i = 0; i < _model.Options.Count; i++)
            {
                PickOptionView option = _model.Options[i];
                Rect card = new Rect(Pad + (i % 2) * (width + 24f), 10f + (i / 2) * 640f, width, 616f);
                Button button = _scroll.Add(new Button { Id = "pick-" + option.SpeciesId, Bounds = card, StyleKey = "chip" });
                string id = option.SpeciesId;
                button.Clicked += () => Select(id);
                _cards[button] = option;
            }

            _scroll.ContentHeight = 20f + ((_model.Options.Count + 1) / 2) * 640f;
            _scroll.ScrollTo(0f);
            Select(null);
        }

        public override void Draw()
        {
            Gradient("title_top", "title_bottom", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            Painter.Soft(new Vec2(540f, 260f), 520f, Painter.C("cream", 0.5f), 0.4f);
            UiStyle style = Ctx.Style;
            Painter.TextIn(_model.Title, new Rect(0, 150f, PortraitLayout.CanvasWidth, style.TextSizes.Heading + 12f), style.TextSizes.Heading + 12f, Painter.C("plum"), TextAlign.Center);
            Painter.TextIn(_model.Subtitle, new Rect(Pad, 212f, PortraitLayout.CanvasWidth - 2f * Pad, 30f), style.TextSizes.Body, Painter.C("ink"), TextAlign.Center);
            float x = Pad;
            float chipWidth = (PortraitLayout.CanvasWidth - 2f * Pad - 24f) / 3f;
            for (int i = 0; i < StarterPicks.StanceCycle.Length; i++)
            {
                CombatStance stance = StarterPicks.StanceCycle[i];
                bool on = _model.RequiredStance == null || _model.RequiredStance == stance;
                Rect box = new Rect(x + i * (chipWidth + 12f), 258f, chipWidth, 92f);
                Painter.Framed(box, 20f, 3f, Painter.C("plum", on ? 1f : 0.35f), Painter.C("cream", on ? 0.95f : 0.4f));
                string[] parts = StarterPickViewModel.StanceExplainer[i].Split(new[] { ": " }, 2, StringSplitOptions.None);
                Painter.TextIn(parts[0], new Rect(box.X, box.Y + 8f, box.Width, 34f), style.TextSizes.Body, Painter.C(TutorialArt.StanceKey(stance), on ? 1f : 0.45f), TextAlign.Center);
                Painter.TextIn(parts.Length > 1 ? parts[1] : string.Empty, new Rect(box.X + 8f, box.Y + 48f, box.Width - 16f, 30f), style.TextSizes.Small - 1f,
                               Painter.C("ink", on ? 1f : 0.45f), TextAlign.Center);
            }

            base.Draw();

            Rect bar = new Rect(0, PortraitLayout.CanvasHeight - BottomBar, PortraitLayout.CanvasWidth, BottomBar);
            Painter.Fill(bar, Painter.C("cream"));
            Painter.Fill(new Rect(0, bar.Y, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.TextIn(_model.Mode == PickMode.Skip ? "Pick " + _model.Step + " of 3" : "All three join at level 1", new Rect(0, bar.Y + 16f, PortraitLayout.CanvasWidth, 30f),
                           style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Center);
            Painter.Paint(_choose, Ui);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (!_cards.TryGetValue(widget, out PickOptionView option))
            {
                return;
            }

            UiStyle style = Ctx.Style;
            Rect card = widget.Bounds;
            Rect portrait = new Rect(card.X + 30f, card.Y + 20f, card.Width - 60f, 320f);
            Painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 12f), 150f, Painter.C("plum", 0.25f), 0.25f);
            Painter.Art(Painter.Sprite(option.ArtKey), portrait, false);
            Painter.TextIn(option.Name, new Rect(card.X + 20f, card.Y + 350f, card.Width - 40f, style.TextSizes.Heading), style.TextSizes.Heading - 2f, Painter.C("plum"), TextAlign.Center);
            float chipY = card.Y + 404f;
            Painter.ElementBadge(option.Element, new Rect(card.X + 40f, chipY, 46f, 46f));
            Painter.TextIn(option.Element.ToString(), new Rect(card.X + 94f, chipY, 150f, 46f), style.TextSizes.Small + 2f, Painter.C("ink"), TextAlign.Left);
            TutorialArt.StanceChip(Ctx, option.Stance, new Rect(card.Right - 220f, chipY + 2f, 190f, 42f));
            TutorialArt.Paragraph(Ctx, option.Blurb, card.X + 28f, card.Y + 470f, card.Width - 56f, style.TextSizes.Small + 1f, Painter.C("inkSoft"), 5);
        }
    }

    /// <summary>
    /// A trial's beast pick (the Kinship "choice" popup, on the toolkit): the pending pick's options (the
    /// required stance only), one card each; the choice joins the team and the map carries on. It cannot
    /// be closed without choosing: the pick is the trial's reward.
    /// </summary>
    public sealed class TrialPickModal : GameModal
    {
        private readonly StarterPickViewModel _model;
        private readonly Action _done;
        private readonly Dictionary<Widget, PickOptionView> _rows = new Dictionary<Widget, PickOptionView>();
        private readonly Button _confirm;
        private readonly Rect _card;
        private string _selected;

        public TrialPickModal(ScreenContext ctx, Action done) : base(ctx)
        {
            _model = new StarterPickViewModel(ctx.Session, PickMode.Trial);
            _done = done;
            float rowHeight = 190f;
            float height = 330f + _model.Options.Count * (rowHeight + 16f) + 180f;
            _card = new Rect(60f, Math.Max(80f, (PortraitLayout.CanvasHeight - height) / 2f), PortraitLayout.CanvasWidth - 120f, height);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            for (int i = 0; i < _model.Options.Count; i++)
            {
                PickOptionView option = _model.Options[i];
                Button row = Ui.Add(new Button
                {
                    Id = "pick-" + option.SpeciesId,
                    Bounds = new Rect(_card.X + 40f, _card.Y + 300f + i * (rowHeight + 16f), _card.Width - 80f, rowHeight),
                    StyleKey = "chip"
                });
                string id = option.SpeciesId;
                row.Clicked += () => Select(id);
                _rows[row] = option;
            }

            _confirm = Ui.Add(new Button { Id = "confirm", Bounds = new Rect(_card.X + 120f, _card.Bottom - 160f, _card.Width - 240f, 120f), Text = "Choose", StyleKey = "primary", Enabled = false });
            _confirm.Clicked += Confirm;
        }

        public override string Name
        {
            get { return "trial-pick"; }
        }

        public StarterPickViewModel Model
        {
            get { return _model; }
        }

        public override bool HandleBack()
        {
            Ctx.Game.Toast("Choose who joins you first.");
            return true;
        }

        public void Select(string speciesId)
        {
            _selected = speciesId;
            foreach (KeyValuePair<Widget, PickOptionView> row in _rows)
            {
                ((Button)row.Key).Selected = row.Value.SpeciesId == speciesId;
            }

            PickOptionView option = _model.Options.Find(o => o.SpeciesId == speciesId);
            _confirm.Enabled = option != null;
            _confirm.Text = option == null ? "Choose" : "Welcome, " + option.Name + "!";
        }

        public void Confirm()
        {
            if (_selected == null)
            {
                return;
            }

            PickOptionView option = _model.Options.Find(o => o.SpeciesId == _selected);
            if (!_model.Choose(_selected, out string error))
            {
                Ctx.Game.Toast(error ?? "That beast cannot join now.");
                return;
            }

            Close();
            Ctx.Game.Toast(option?.Name + " joins your team at level 1!");
            _done?.Invoke();
        }

        public override void Draw()
        {
            base.Draw();
            UiStyle style = Ctx.Style;
            Ctx.Painter.TextIn(_model.Title, new Rect(_card.X, _card.Y + 50f, _card.Width, style.TextSizes.Heading + 8f), style.TextSizes.Heading + 8f, Ctx.Painter.C("plum"), TextAlign.Center);
            TutorialArt.Paragraph(Ctx, _model.Subtitle + " Tap one to meet it.", _card.X + 60f, _card.Y + 130f, _card.Width - 120f, style.TextSizes.Body, Ctx.Painter.C("ink"));
            if (_model.RequiredStance.HasValue)
            {
                int index = Array.IndexOf(StarterPicks.StanceCycle, _model.RequiredStance.Value);
                TutorialArt.Paragraph(Ctx, StarterPickViewModel.StanceExplainer[index], _card.X + 60f, _card.Y + 222f, _card.Width - 120f, style.TextSizes.Small + 2f,
                                      Ctx.Painter.C("inkSoft"));
            }

            foreach (KeyValuePair<Widget, PickOptionView> row in _rows)
            {
                Rect box = row.Key.Bounds;
                PickOptionView option = row.Value;
                Rect portrait = new Rect(box.X + 12f, box.Y + 10f, 170f, box.Height - 20f);
                Ctx.Painter.Art(Ctx.Painter.Sprite(option.ArtKey), portrait, false);
                Ctx.Painter.TextIn(option.Name, new Rect(box.X + 200f, box.Y + 18f, 360f, 40f), style.TextSizes.Body + 6f, Ctx.Painter.C("plum"), TextAlign.Left);
                Ctx.Painter.ElementBadge(option.Element, new Rect(box.X + 200f, box.Y + 70f, 40f, 40f));
                Ctx.Painter.TextIn(option.Element.ToString(), new Rect(box.X + 250f, box.Y + 70f, 160f, 40f), style.TextSizes.Small + 2f, Ctx.Painter.C("ink"), TextAlign.Left);
                TutorialArt.StanceChip(Ctx, option.Stance, new Rect(box.Right - 220f, box.Y + 22f, 190f, 42f));
                TutorialArt.Paragraph(Ctx, option.Blurb, box.X + 200f, box.Y + 118f, box.Width - 230f, style.TextSizes.Small, Ctx.Painter.C("inkSoft"), 2);
            }
        }

    }

    /// <summary>
    /// The dialogue box (<see cref="StoryViewModel"/>): the mentor's portrait (a placeholder disc with
    /// their initial), their name and one line at a time over the map; Next moves on. A story location
    /// is then visited (its gifts, and at Hearthglen's end the way on to Verdant Hollow); a camp's scene
    /// only talks.
    /// </summary>
    public sealed class StoryModal : GameModal
    {
        private readonly StoryViewModel _model;
        private readonly bool _visit;
        private readonly Action<CampaignResult> _done;
        private readonly Button _next;
        private readonly Rect _box;

        public StoryModal(ScreenContext ctx, StoryViewModel model, bool visit, Action<CampaignResult> done) : base(ctx)
        {
            _model = model;
            _visit = visit;
            _done = done;
            _box = new Rect(36f, PortraitLayout.CanvasHeight - 760f, PortraitLayout.CanvasWidth - 72f, 560f);
            Ui.Add(new Panel { Bounds = _box, StyleKey = "modal" });
            _next = Ui.Add(new Button { Id = "next", Bounds = new Rect(_box.Right - 360f, _box.Bottom - 140f, 320f, 110f), StyleKey = "primary" });
            _next.Clicked += Advance;
            Label();
        }

        public override string Name
        {
            get { return "story"; }
        }

        public StoryViewModel Model
        {
            get { return _model; }
        }

        public override bool HandleBack()
        {
            Advance();
            return true;
        }

        /// <summary>The next line; after the last, the visit (or just the close).</summary>
        public void Advance()
        {
            if (_model.Next())
            {
                Label();
                return;
            }

            Close();
            CampaignResult result = _visit ? _model.Complete() : null;
            _done?.Invoke(result);
        }

        /// <summary>Every remaining line at once (scripted runs).</summary>
        public void Finish()
        {
            while (_model.Next())
            {
            }

            Advance();
        }

        private void Label()
        {
            _next.Text = _model.IsLast ? "Continue" : "Next";
        }

        public override void Draw()
        {
            Ctx.Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Ctx.Painter.C("scrim", 0.6f));
            Ctx.Painter.Paint(Ui, Ui);
            DialogueLineView line = _model.Current;
            if (line == null)
            {
                return;
            }

            UiStyle style = Ctx.Style;
            TutorialArt.Portrait(Ctx, line.Speaker, line.PortraitKey, new Vec2(_box.X + 150f, _box.Y + 150f), 100f);
            Ctx.Painter.TextIn(line.Speaker, new Rect(_box.X + 290f, _box.Y + 60f, _box.Width - 330f, style.TextSizes.Heading), style.TextSizes.Heading - 2f, Ctx.Painter.C("plum"),
                               TextAlign.Left);
            if (!string.IsNullOrEmpty(_model.Title))
            {
                Ctx.Painter.TextIn(_model.Title, new Rect(_box.X + 290f, _box.Y + 112f, _box.Width - 330f, 30f), style.TextSizes.Small + 2f, Ctx.Painter.C("inkSoft"), TextAlign.Left);
            }

            TutorialArt.Paragraph(Ctx, line.Text, _box.X + 60f, _box.Y + 280f, _box.Width - 120f, style.TextSizes.Body + 4f, Ctx.Painter.C("ink"));
            Ctx.Painter.TextIn((_model.Index + 1) + " / " + _model.Lines.Count, new Rect(_box.X + 60f, _box.Bottom - 110f, 200f, 40f), style.TextSizes.Small, Ctx.Painter.C("inkSoft"),
                               TextAlign.Left);
        }
    }

    /// <summary>
    /// A camp (<see cref="CampViewModel"/>), any region: pick a beast to train (the lowest is suggested),
    /// see what it earned (and, in Hearthglen, who caught up to the leader), and the idle rewards' chip.
    /// </summary>
    public sealed class CampModal : GameModal
    {
        private readonly CampViewModel _model;
        private readonly Action _done;
        private readonly Dictionary<Widget, CampBeastView> _rows = new Dictionary<Widget, CampBeastView>();
        private readonly Button _train;
        private readonly Button _idle;
        private readonly Rect _card;
        private string _selected;

        public CampModal(ScreenContext ctx, CampViewModel model, Action done) : base(ctx)
        {
            _model = model;
            _done = done;
            float rowHeight = 150f;
            float height = 330f + model.Beasts.Count * (rowHeight + 14f) + 420f;
            _card = new Rect(60f, Math.Max(60f, (PortraitLayout.CanvasHeight - height) / 2f), PortraitLayout.CanvasWidth - 120f, height);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            for (int i = 0; i < model.Beasts.Count; i++)
            {
                CampBeastView beast = model.Beasts[i];
                Button row = Ui.Add(new Button { Id = "beast-" + beast.BeastId, Bounds = new Rect(_card.X + 40f, _card.Y + 300f + i * (rowHeight + 14f), _card.Width - 80f, rowHeight), StyleKey = "chip" });
                string id = beast.BeastId;
                row.Clicked += () => Select(id);
                _rows[row] = beast;
            }

            float y = _card.Y + 300f + model.Beasts.Count * (rowHeight + 14f) + 20f;
            _idle = Ui.Add(new Button { Id = "idle", Bounds = new Rect(_card.X + 40f, y + 130f, _card.Width - 80f, 90f), StyleKey = "chip", Glyph = "hourglass" });
            _idle.Clicked += ClaimIdle;
            _train = Ui.Add(new Button { Id = "train", Bounds = new Rect(_card.X + 120f, _card.Bottom - 160f, _card.Width - 240f, 120f), StyleKey = "primary" });
            _train.Clicked += Train;
            Select(model.Suggested);
            RefreshIdle();
        }

        public override string Name
        {
            get { return "camp"; }
        }

        public CampViewModel Model
        {
            get { return _model; }
        }

        public void Select(string beastId)
        {
            if (_model.Done)
            {
                return;
            }

            _selected = beastId;
            foreach (KeyValuePair<Widget, CampBeastView> row in _rows)
            {
                ((Button)row.Key).Selected = row.Value.BeastId == beastId;
            }

            CampBeastView beast = _model.Beasts.Find(b => b.BeastId == beastId);
            _train.Enabled = beast != null;
            _train.Text = beast == null ? "Train" : "Train " + beast.Name;
        }

        /// <summary>Trains the selected beast; a second tap (Continue) closes the camp.</summary>
        public void Train()
        {
            if (_model.Done)
            {
                Close();
                _done?.Invoke();
                return;
            }

            if (_selected == null || !_model.Train(_selected, out string error))
            {
                Ctx.Game.Toast("Pick a beast to train first.");
                return;
            }

            _train.Text = "Continue";
            foreach (KeyValuePair<Widget, CampBeastView> row in _rows)
            {
                CampBeastView fresh = _model.Beasts.Find(b => b.BeastId == row.Value.BeastId);
                row.Value.Level = fresh.Level;
                row.Value.XpFraction = fresh.XpFraction;
            }
        }

        private void ClaimIdle()
        {
            IdleClaimView claim = Ctx.Session.ClaimIdle();
            Ctx.Game.Toast(claim?.Message ?? (Ctx.Session.IdleStatus().Started ? "Nothing to collect yet." : "The idle clock has started."));
            RefreshIdle();
        }

        private void RefreshIdle()
        {
            _idle.Text = Ctx.Session.IdleStatus().Text + "  (tap to collect)";
        }

        public override bool HandleBack()
        {
            if (_model.Done)
            {
                Close();
                _done?.Invoke();
                return true;
            }

            return false;
        }

        public override void Draw()
        {
            base.Draw();
            UiStyle style = Ctx.Style;
            Ctx.Painter.TextIn(_model.Title, new Rect(_card.X, _card.Y + 50f, _card.Width, style.TextSizes.Heading + 8f), style.TextSizes.Heading + 8f, Ctx.Painter.C("plum"),
                               TextAlign.Center);
            string text = _model.Done ? string.Join(" ", _model.Summary) : _model.Explainer;
            TutorialArt.Paragraph(Ctx, text, _card.X + 60f, _card.Y + 130f, _card.Width - 120f, style.TextSizes.Body, Ctx.Painter.C(_model.Done ? "leafDeep" : "ink"), 4);
            foreach (KeyValuePair<Widget, CampBeastView> row in _rows)
            {
                Rect box = row.Key.Bounds;
                CampBeastView beast = row.Value;
                Ctx.Painter.Art(Ctx.Painter.Sprite(Ctx.Content.Battle.GetSpecies(beast.SpeciesId)?.ArtKey), new Rect(box.X + 14f, box.Y + 8f, 150f, box.Height - 16f), false);
                Ctx.Painter.TextIn(beast.Name + "   Lv " + beast.Level.ToString(CultureInfo.InvariantCulture), new Rect(box.X + 190f, box.Y + 24f, box.Width - 220f, 40f),
                                   style.TextSizes.Body + 4f, Ctx.Painter.C("ink"), TextAlign.Left);
                Ctx.Painter.Progress(new Rect(box.X + 190f, box.Y + 86f, box.Width - 240f, 34f), beast.XpFraction, -1f, "leaf", "moss", "track");
                if (beast.BeastId == _model.Suggested && !_model.Done)
                {
                    Ctx.Painter.TextIn("suggested", new Rect(box.Right - 230f, box.Y + 26f, 200f, 32f), style.TextSizes.Small, Ctx.Painter.C("inkSoft"), TextAlign.Right);
                }
            }

            Ctx.Painter.TextIn("Idle rewards", new Rect(_idle.Bounds.X, _idle.Bounds.Y - 56f, _idle.Bounds.Width, 40f), style.TextSizes.Body + 2f, Ctx.Painter.C("plum"), TextAlign.Left);
        }
    }

    /// <summary>
    /// A tutorial hint over the current screen: a card with the hint and Got it / Turn hints off, and a
    /// ring round what it points at (its anchor: a widget id on the screen, or top, center, bottom).
    /// Dismissing it marks it seen (saved); the next hint due for the same moment follows.
    /// </summary>
    public sealed class HintModal : GameModal
    {
        private readonly HintData _hint;
        private readonly Rect? _anchor;
        private readonly Action _after;
        private readonly Rect _card;

        public HintModal(ScreenContext ctx, HintData hint, Rect? anchor, Action after) : base(ctx)
        {
            _hint = hint;
            _anchor = anchor;
            _after = after;
            UiStyle style = ctx.Style;
            float width = 900f;
            float textHeight = ctx.Painter.Wrap(hint.Text, style.TextSizes.Body + 2f, width - 100f).Count * ctx.Text.LineHeight(style.TextSizes.Body + 2f);
            float height = 130f + textHeight + 170f;
            float y = hint.Anchor == "top" ? 260f : hint.Anchor == "bottom" ? PortraitLayout.CanvasHeight - height - 560f : (PortraitLayout.CanvasHeight - height) / 2f;
            if (anchor.HasValue && hint.Anchor != "top" && hint.Anchor != "bottom" && hint.Anchor != "center")
            {
                y = anchor.Value.Center.Y > PortraitLayout.CanvasHeight / 2f ? anchor.Value.Y - height - 40f : anchor.Value.Bottom + 40f;
                y = Math.Max(60f, Math.Min(PortraitLayout.CanvasHeight - height - 60f, y));
            }

            _card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, y, width, height);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "banner" });
            Button ok = Ui.Add(new Button { Id = "got-it", Bounds = new Rect(_card.Right - 330f, _card.Bottom - 130f, 290f, 100f), Text = "Got it", StyleKey = "primary" });
            ok.Clicked += Dismiss;
            Button off = Ui.Add(new Button { Id = "hints-off", Bounds = new Rect(_card.X + 40f, _card.Bottom - 120f, 340f, 84f), Text = "Turn hints off", StyleKey = "ghost" });
            off.Clicked += TurnOff;
        }

        public override string Name
        {
            get { return "hint:" + _hint.HintId; }
        }

        public HintData Hint
        {
            get { return _hint; }
        }

        public override bool HandleBack()
        {
            Dismiss();
            return true;
        }

        public void Dismiss()
        {
            HintService.Dismiss(Ctx.Session, _hint.HintId);
            Close();
            _after?.Invoke();
        }

        private void TurnOff()
        {
            HintService.Dismiss(Ctx.Session, _hint.HintId);
            HintService.TurnOff(Ctx.Session);
            Close();
            Ctx.Game.Toast("Hints are off. Turn them back on in Settings.");
        }

        public override void Draw()
        {
            Ctx.Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Ctx.Painter.C("scrim", 0.35f));
            if (_anchor.HasValue)
            {
                Rect a = _anchor.Value;
                float pulse = 0.6f + 0.4f * (float)Math.Sin(Ctx.Painter.TimeMs / 220f);
                Ctx.Painter.RoundedOutline(new Rect(a.X - 14f, a.Y - 14f, a.Width + 28f, a.Height + 28f), 28f, 8f, Ctx.Painter.C("gold", pulse));
            }

            Ctx.Painter.Paint(Ui, Ui);
            UiStyle style = Ctx.Style;
            Ctx.Painter.Glyph("star", new Rect(_card.X + 40f, _card.Y + 36f, 56f, 56f), Ctx.Painter.C("goldDeep"));
            Ctx.Painter.TextIn(_hint.Title, new Rect(_card.X + 110f, _card.Y + 40f, _card.Width - 150f, style.TextSizes.Heading), style.TextSizes.Heading - 2f, Ctx.Painter.C("plum"),
                               TextAlign.Left);
            TutorialArt.Paragraph(Ctx, _hint.Text, _card.X + 50f, _card.Y + 116f, _card.Width - 100f, style.TextSizes.Body + 2f, Ctx.Painter.C("ink"));
        }
    }

    /// <summary>
    /// The way on from Hearthglen: a short story card naming the region ahead (Verdant Hollow), its
    /// description, and Onward (the map, now showing it).
    /// </summary>
    public sealed class RegionCardModal : GameModal
    {
        private readonly string _title;
        private readonly string _text;
        private readonly Action _after;
        private readonly Rect _card;

        public RegionCardModal(ScreenContext ctx, RegionData region, Action after) : base(ctx)
        {
            _title = region?.DisplayName ?? "Onward";
            _text = region?.Description ?? string.Empty;
            _after = after;
            _card = new Rect(60f, 420f, PortraitLayout.CanvasWidth - 120f, 1000f);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            Button go = Ui.Add(new Button { Id = "onward", Bounds = new Rect(_card.Center.X - 220f, _card.Bottom - 170f, 440f, 130f), Text = "Onward", StyleKey = "primary" });
            go.Clicked += Onward;
        }

        public override string Name
        {
            get { return "region-card"; }
        }

        public override bool HandleBack()
        {
            Onward();
            return true;
        }

        public void Onward()
        {
            Close();
            _after?.Invoke();
        }

        public override void Draw()
        {
            base.Draw();
            UiStyle style = Ctx.Style;
            Rect art = new Rect(_card.X + 60f, _card.Y + 60f, _card.Width - 120f, 360f);
            Ctx.Painter.RoundedRect(art, 30f, Ctx.Painter.C("map_grass"));
            Ctx.Painter.Soft(new Vec2(art.X + art.Width * 0.3f, art.Y + art.Height * 0.55f), 200f, Ctx.Painter.C("map_meadow"), 0.6f);
            Ctx.Painter.Soft(new Vec2(art.X + art.Width * 0.75f, art.Y + art.Height * 0.4f), 160f, Ctx.Painter.C("leafDeep", 0.6f), 0.7f);
            Ctx.Painter.Glyph("map", new Rect(art.Center.X - 90f, art.Center.Y - 90f, 180f, 180f), Ctx.Painter.C("cream"));
            Ctx.Painter.TextIn("Hearthglen is behind you", new Rect(_card.X, art.Bottom + 40f, _card.Width, 36f), style.TextSizes.Body + 2f, Ctx.Painter.C("inkSoft"), TextAlign.Center);
            Ctx.Painter.TextIn(_title, new Rect(_card.X, art.Bottom + 96f, _card.Width, style.TextSizes.Title), style.TextSizes.Title, Ctx.Painter.C("plum"), TextAlign.Center);
            TutorialArt.Paragraph(Ctx, _text, _card.X + 70f, art.Bottom + 200f, _card.Width - 140f, style.TextSizes.Body + 2f, Ctx.Painter.C("ink"));
        }
    }
}
