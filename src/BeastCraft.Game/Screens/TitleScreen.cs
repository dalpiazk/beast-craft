using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The title (<see cref="TitleViewModel"/>): Continue when a save exists, New Game (asking
    /// first when it would replace the save) and Settings over a warm painted-style backdrop with
    /// the starter beasts. A save that could only be restored from its backup says so; one that
    /// cannot be loaded at all says why. Back asks before quitting.
    /// </summary>
    public sealed class TitleScreen : GameScreen
    {
        private static readonly string[] Beasts = { "beast/griffin/illustrated", "beast/phoenix/illustrated", "beast/golem/illustrated" };

        private readonly TitleViewModel _model;
        private readonly Button _continue;
        private readonly Button _newGame;

        public TitleScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new TitleViewModel(ctx.Session);
            float width = 680f;
            float x = (PortraitLayout.CanvasWidth - width) / 2f;
            _continue = AddButton(null, "continue", new Rect(x, 1180f, width, 130f), "Continue", "primary", () => EnterGame(true));
            _newGame = AddButton(null, "new-game", new Rect(x, 1350f, width, 130f), "New Game", "secondary", NewGame);
            _settings = AddButton(null, "settings", new Rect(x, 1520f, width, 130f), "Settings", "secondary", OpenSettings, "gear");
        }

        private readonly Button _settings;

        public override string Name
        {
            get { return "title"; }
        }

        public override void Enter()
        {
            // Continue first when there is a save; the buttons close up without it.
            bool save = _model.CanContinue;
            _continue.Visible = save;
            _newGame.StyleKey = save ? "secondary" : "primary";
            float y = save ? 1350f : 1180f;
            _newGame.Bounds = new Rect(_newGame.Bounds.X, y, _newGame.Bounds.Width, 130f);
            _settings.Bounds = new Rect(_settings.Bounds.X, y + 170f, _settings.Bounds.Width, 130f);
        }

        public override bool HandleBack()
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Leave Beast Craft?", "Your progress is saved.", "Stay", "Quit", Ctx.Game.Exit, "danger"));
            return true;
        }

        /// <summary>Continue (when <paramref name="preferContinue"/> and a save exists) or start a new game, then the map.</summary>
        public void EnterGame(bool preferContinue)
        {
            if (preferContinue && _model.CanContinue)
            {
                LoadOutcome outcome = _model.Continue();
                if (!outcome.Success)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Save not loaded", outcome.Message + " You can start a new game.", null, "OK", null));
                    return;
                }

                Ctx.Stack.Push(new HomeScreen(Ctx));
                if (outcome.Message != null)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Save restored", outcome.Message, null, "OK", null));
                }

                return;
            }

            StartNewGame();
        }

        /// <summary>A new game straight away (no question), then the map.</summary>
        public void StartNewGame()
        {
            _model.NewGame();
            Ctx.Stack.Push(new HomeScreen(Ctx));
        }

        public void OpenSettings()
        {
            Ctx.Stack.PushModal(new SettingsModal(Ctx, new SettingsViewModel(Ctx.Session.Settings, Ctx.Session.SaveSettings)));
        }

        private void NewGame()
        {
            if (_model.NewGameNeedsConfirm)
            {
                Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Start a new game?", "This replaces your saved game.", "Cancel", "Start", StartNewGame, "danger"));
                return;
            }

            StartNewGame();
        }

        public override void Draw()
        {
            Gradient("title_top", "title_bottom", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            Painter.Soft(new Vec2(180f, 420f), 420f, Painter.C("map_meadow"));
            Painter.Soft(new Vec2(930f, 760f), 380f, Painter.C("map_meadow"));
            Painter.Soft(new Vec2(540f, 1150f), 700f, Painter.C("map_grass"), 0.35f);
            Painter.Soft(new Vec2(540f, 330f), 360f, Painter.C("cream", 0.55f), 0.5f);

            float titleSize = Ctx.Style.TextSizes.Title * 1.5f;
            Painter.TextIn("Beast Craft", new Rect(0, 240f, PortraitLayout.CanvasWidth, titleSize), titleSize, Painter.C("cream"), TextAlign.Center, true, Painter.C("plum"));
            Painter.TextIn("Bind beasts. Brave the Gloam.", new Rect(0, 380f, PortraitLayout.CanvasWidth, 40f), Ctx.Style.TextSizes.Body + 4f, Painter.C("plum"), TextAlign.Center);

            for (int i = 0; i < Beasts.Length; i++)
            {
                Rect slot = new Rect(60f + i * 330f, 560f + (i == 1 ? -40f : 20f), 300f, 400f);
                Painter.Soft(new Vec2(slot.Center.X, slot.Bottom - 10f), 140f, Painter.C("plum", 0.3f), 0.25f);
                Painter.Art(Painter.Sprite(Beasts[i]), slot, i == 2);
            }

            base.Draw();
            Painter.TextIn("Pre-alpha - local play", new Rect(0, 1840f, PortraitLayout.CanvasWidth, 30f), Ctx.Style.TextSizes.Small, Painter.C("plum", 0.7f), TextAlign.Center);
        }
    }
}
