using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The title (<see cref="TitleViewModel"/>): Continue (the most recently played slot, when one
    /// exists) and Load game (<see cref="SaveSlotsScreen"/>, when any slot holds a save), New Game
    /// (always opens the slot list too, so the player picks an empty slot or confirms replacing one —
    /// the one way a game starts), Settings and Credits (<see cref="CreditsScreen"/>), over a warm
    /// painted-style backdrop with the starter beasts. Every button is a 48dp+ target anchored toward
    /// the bottom of the canvas. A save that could only be restored from its backup says so; one that
    /// cannot be loaded at all says why. Back asks before quitting.
    /// </summary>
    public sealed class TitleScreen : GameScreen
    {
        private static readonly string[] Beasts = { "beast/griffin/illustrated", "beast/phoenix/illustrated", "beast/golem/illustrated" };

        private const float ButtonWidth = 680f;
        private const float ButtonHeight = 130f;

        private readonly TitleViewModel _model;
        private readonly Button _continue;
        private readonly Button _loadGame;
        private readonly Button _newGame;
        private readonly Button _settings;
        private readonly Button _credits;

        public TitleScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new TitleViewModel(ctx.Session);
            float x = (PortraitLayout.CanvasWidth - ButtonWidth) / 2f;
            _continue = AddButton(null, "continue", Row(x, 0), Loc("ui.title.continue"), "primary", () => EnterGame(true));
            _loadGame = AddButton(null, "load-game", Row(x, 1), Loc("ui.title.load_game"), "secondary", OpenSlots);
            _newGame = AddButton(null, "new-game", Row(x, 2), Loc("ui.title.new_game"), "secondary", NewGame);
            _settings = AddButton(null, "settings", Row(x, 3), Loc("ui.title.settings"), "secondary", OpenSettings, "gear");
            float creditsWidth = 360f;
            _credits = AddButton(null, "credits", new Rect((PortraitLayout.CanvasWidth - creditsWidth) / 2f, 1690f, creditsWidth, 80f), Loc("ui.title.credits"), "chip",
                                  () => Ctx.Stack.Push(new CreditsScreen(Ctx)));
        }

        /// <summary>
        /// Row <paramref name="index"/> (0 = Continue's slot) of the fixed bottom-anchored stack: each
        /// a comfortable 130px (far past the 48dp/~126px minimum touch target).
        /// </summary>
        private static Rect Row(float x, int index)
        {
            const float top = 1080f;
            const float gap = 20f;
            return new Rect(x, top + index * (ButtonHeight + gap), ButtonWidth, ButtonHeight);
        }

        public override string Name
        {
            get { return "title"; }
        }

        public override void Enter()
        {
            // Continue and Load game only where a save exists; New Game, Settings and Credits always.
            bool save = _model.CanContinue;
            _continue.Visible = save;
            _loadGame.Visible = _model.CanManageSlots;
            _newGame.StyleKey = save ? "secondary" : "primary";
        }

        public override bool HandleBack()
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.title.leave_title"), Loc("ui.title.leave_body"), Loc("ui.title.stay"), Loc("ui.title.quit"), Ctx.Game.Exit, "danger"));
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
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.title.not_loaded_title"), Loc("ui.title.not_loaded_body", outcome.Message), null, Loc("ui.common.ok"), null));
                    return;
                }

                Ctx.Stack.Push(new HomeScreen(Ctx));
                if (outcome.Message != null)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.title.restored_title"), outcome.Message, null, Loc("ui.common.ok"), null));
                }

                // Continue claimed the idle rewards: say what they paid (nothing is silent).
                if (Ctx.Session.LastContinueClaim?.Message != null)
                {
                    Ctx.Game.Toast(Ctx.Session.LastContinueClaim.Message);
                }

                // The last battle never finished (the app closed mid-battle): its consumables came back.
                if (outcome.RefundMessage != null)
                {
                    Ctx.Game.Toast(outcome.RefundMessage);
                }

                return;
            }

            StartNewGame();
        }

        /// <summary>
        /// A new game straight away (no question), in the first empty slot (or the current one when every
        /// slot is full, as a scripted run may be): the first beast's pick, then Hearthglen's map (or the
        /// skip's three picks).
        /// </summary>
        public void StartNewGame()
        {
            _model.PrepareNewGame();
            Ctx.Stack.Push(new StarterPickScreen(Ctx));
        }

        /// <summary>The save slot list.</summary>
        public void OpenSlots()
        {
            Ctx.Stack.Push(new SaveSlotsScreen(Ctx));
        }

        public void OpenSettings()
        {
            Ctx.Stack.Push(new SettingsScreen(Ctx));
        }

        /// <summary>
        /// New Game always opens the slot list (producer decision, menu-screens pass #67): pick an
        /// empty slot there, or a used one, which asks to replace it first
        /// (<see cref="SaveSlotsScreen.Play"/>) — the one way a game starts, whether or not any slot
        /// already holds one.
        /// </summary>
        private void NewGame()
        {
            OpenSlots();
        }

        public override void Draw()
        {
            Gradient("title_top", "title_bottom", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            Painter.Soft(new Vec2(180f, 420f), 420f, Painter.C("map_meadow"));
            Painter.Soft(new Vec2(930f, 760f), 380f, Painter.C("map_meadow"));
            Painter.Soft(new Vec2(540f, 1150f), 700f, Painter.C("map_grass"), 0.35f);
            Painter.Soft(new Vec2(540f, 330f), 360f, Painter.C("cream", 0.55f), 0.5f);

            float titleSize = Ctx.Style.TextSizes.Title * 1.5f;
            Painter.TextIn(Loc("ui.title.name"), new Rect(0, 240f, PortraitLayout.CanvasWidth, titleSize), titleSize, Painter.C("cream"), TextAlign.Center, true, Painter.C("plum"));
            Painter.TextIn(Loc("ui.title.tagline"), new Rect(0, 380f, PortraitLayout.CanvasWidth, 40f), Ctx.Style.TextSizes.Body + 4f, Painter.C("plum"), TextAlign.Center);

            for (int i = 0; i < Beasts.Length; i++)
            {
                Rect slot = new Rect(60f + i * 330f, 560f + (i == 1 ? -40f : 20f), 300f, 400f);
                Painter.Soft(new Vec2(slot.Center.X, slot.Bottom - 10f), 140f, Painter.C("plum", 0.3f), 0.25f);
                Painter.Art(Painter.Sprite(Beasts[i]), slot, i == 2);
            }

            base.Draw();
            Painter.TextIn(Loc("ui.title.footer"), new Rect(0, 1840f, PortraitLayout.CanvasWidth, 30f), Ctx.Style.TextSizes.Small, Painter.C("plum", 0.7f), TextAlign.Center);
        }
    }
}
