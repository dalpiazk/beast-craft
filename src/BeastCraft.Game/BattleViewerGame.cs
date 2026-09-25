using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Save;
using BeastCraft.Session;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BeastCraft.Game
{
    /// <summary>
    /// The battle viewer every host runs: one real PvE battle (<see cref="DemoBattle"/>) stepped a
    /// turn at a time through <see cref="BattlePlayback"/> and drawn in portrait on a fixed
    /// 1080x1920 logical canvas (<see cref="PortraitLayout"/>), scaled uniformly and letterboxed
    /// into the window or screen inside its safe area (<see cref="ViewerHost.SafeArea"/>). The
    /// screen, top to bottom: header, turn-order portraits (the acting unit highlighted), the board
    /// fitted to the arena, a one-line log toast, the acting unit's skills, and the playback
    /// controls (pause/play, x1/x2/x3, skip).
    /// <para>
    /// Desktop: Space steps (or finishes the turn playing), A toggles auto-play, 1-3 set the speed,
    /// S skips to the end, Tab cycles the selected skill, Esc quits; the mouse clicks the buttons
    /// and hovers or clicks the skills. Touch (<see cref="ViewerHost.Touch"/>): tap a button or a
    /// skill, tap the board to step, two fingers toggle auto-play, Back quits. With
    /// <c>--screenshot</c> it renders a single frame to a PNG and exits (<see cref="ViewerOptions"/>).
    /// </para>
    /// <para>
    /// The viewer only reads the battle: turns are the session's own
    /// (<see cref="BattleSession.Begin"/>), and every animation is a pure function of the recorded
    /// results (<see cref="TurnAnimation"/>, <see cref="Presentation.Vfx.VfxTimeline"/>).
    /// </para>
    /// </summary>
    // The namespace BeastCraft.Game would win over Microsoft.Xna.Framework.Game here, hence the full name.
    public sealed partial class BattleViewerGame : Microsoft.Xna.Framework.Game
    {
        private const int AutoPauseMs = 260;
        private const int ControlPause = 0;
        private const int ControlSkip = 4;

        private readonly ViewerOptions _options;
        private readonly ViewerHost _host;
        private readonly GraphicsDeviceManager _graphics;
        private readonly PortraitLayout _screen = new PortraitLayout();
        private readonly HexLayout _layout = new HexLayout(0, 0);
        private SpriteBatch _batch;
        private SpriteRenderer _draw;
        private SpriteAtlas _atlas;
        private ITextRenderer _text;

        private GameContent _content;
        private BattlePlayback _playback;
        private Dictionary<string, string> _speciesByUnit;
        private Dictionary<string, string> _names;
        private BoardFit _boardFit;
        private CanvasFit _canvasFit;
        private CameraRig _camera;
        private TurnCamera _turnCamera;
        private CameraView _cameraRest;
        private int _cameraIdleMs;
        private PlayerSettings _settings = new PlayerSettings();
        private PlayerSettingsStore _settingsStore;
        private VfxSettings _vfxSettings = VfxSettings.Default;
        private bool _settingsOpen;

        private TurnAnimation _animation;
        private int _clockMs;
        private int _idleMs;
        private int _idleClockMs;
        private bool _auto;
        private int _speed;
        private int _selectedSkill;
        private int _hoveredSkill = -1;
        private KeyboardState _previousKeys;
        private MouseState _previousMouse;
        private bool _previousBack;
        private int _gestureTouches;
        private Vector2 _gestureEnd;
        private readonly List<string> _log = new List<string>();

        public BattleViewerGame(ViewerOptions options, ViewerHost host)
        {
            _options = options;
            _host = host;
            _speed = Math.Max(1, Math.Min(3, options.Speed));
            _selectedSkill = options.SelectSkill;
            if (host.Touch)
            {
                // Full screen at the device's own resolution, portrait only; the canvas is letterboxed into it.
                _graphics = new GraphicsDeviceManager(this)
                {
                    IsFullScreen = true,
                    SupportedOrientations = DisplayOrientation.Portrait,
                    SynchronizeWithVerticalRetrace = true
                };
                TouchPanel.EnableMouseTouchPoint = false;
                return;
            }

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = ViewerHost.DesktopWidth,
                PreferredBackBufferHeight = ViewerHost.DesktopHeight,
                SynchronizeWithVerticalRetrace = true
            };
            IsMouseVisible = true;
            Window.AllowUserResizing = true;
            Window.Title = host.WindowTitle;
        }

        /// <summary>Set when the spike could not start or could not write its screenshot.</summary>
        public string FailureMessage { get; private set; }

        protected override void LoadContent()
        {
            _batch = new SpriteBatch(GraphicsDevice);
            _draw = new SpriteRenderer(_batch);
            _text = new PixelText(GraphicsDevice);

            List<string> errors = new List<string>();
            _content = _host.Content != null
                           ? GameContent.Load(_host.Content, errors)
                           : GameContent.Load(GameContent.FindRoot(_options.ContentRoot), errors);
            if (_content == null)
            {
                Fail("Could not load the content:\n" + string.Join("\n", errors));
                return;
            }

            _atlas = new SpriteAtlas(GraphicsDevice, _content);
            LoadFont();
            LoadSettings();

            BattleSetup setup = DemoBattle.Create(_content, _options.Seed, out _speciesByUnit, out string error, null, _options.Encounter,
                                                  _options.Level, _options.EnemyLevel);
            BattleSessionRun run = setup == null ? null : BattleSession.Begin(setup);
            if (run == null || run.Battle == null)
            {
                Fail("Could not begin the battle: " + (error ?? run.Result.Error));
                return;
            }

            _playback = new BattlePlayback(run);
            _camera = new CameraRig(_playback.Grid.Radius, _screen.Board);
            _cameraRest = _camera.FitAll;
            _boardFit = _camera.Fit(_cameraRest);
            _names = UnitNames(_content, _speciesByUnit);

            if (_options.Screenshot)
            {
                PrepareScreenshot();
            }
        }

        protected override void UnloadContent()
        {
            _atlas?.Dispose();
            _text?.Dispose();
            _pixel?.Dispose();
            _batch?.Dispose();
        }

        protected override void Update(GameTime gameTime)
        {
            if (FailureMessage != null || _options.Screenshot)
            {
                base.Update(gameTime);
                return;
            }

            KeyboardState keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.Escape) || BackPressed())
            {
                Exit();
            }

            bool step = Pressed(keys, Keys.Space);
            if (Pressed(keys, Keys.A))
            {
                _auto = !_auto;
            }

            for (int speed = 1; speed <= 3; speed++)
            {
                if (Pressed(keys, Keys.D0 + speed) || Pressed(keys, Keys.NumPad0 + speed))
                {
                    _speed = speed;
                }
            }

            if (Pressed(keys, Keys.S))
            {
                SkipToEnd();
            }

            if (Pressed(keys, Keys.Tab))
            {
                int count = ActingSkills().Count;
                _selectedSkill = count == 0 || _selectedSkill + 1 >= count ? -1 : _selectedSkill + 1;
            }

            _previousKeys = keys;
            if (_host.Touch)
            {
                ReadTouch(ref step);
            }
            else
            {
                ReadMouse(ref step);
            }

            int elapsed = (int)gameTime.ElapsedGameTime.TotalMilliseconds;
            _idleClockMs += elapsed;
            int played = elapsed * _speed;

            if (_animation != null)
            {
                _clockMs += played;
                if (step)
                {
                    _clockMs = _animation.DurationMs;
                    step = false;
                }

                if (_clockMs >= _animation.DurationMs)
                {
                    _cameraRest = CameraNow();
                    _cameraIdleMs = 0;
                    _animation = null;
                    _turnCamera = null;
                    _idleMs = 0;
                }
            }
            else
            {
                _idleMs += played;
                _cameraIdleMs += played;
                if (step || (_auto && _idleMs >= AutoPauseMs))
                {
                    PlayNextTurn();
                }
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            if (_options.Screenshot)
            {
                SaveScreenshot();
                Exit();
                return;
            }

            GraphicsDevice.SetRenderTarget(null);
            PresentationParameters back = GraphicsDevice.PresentationParameters;
            RenderScene(back.BackBufferWidth, back.BackBufferHeight, _host.SafeArea != null ? _host.SafeArea() : _options.SafeInsets);
            base.Draw(gameTime);
        }

        // ------------------------------------------------------------------------------------------
        // Battle flow
        // ------------------------------------------------------------------------------------------

        private void PlayNextTurn()
        {
            PlayedTurn turn = _playback.Advance();
            if (turn == null)
            {
                _auto = false;
                return;
            }

            CameraView from = CameraNow();
            _animation = new TurnAnimation(turn, _layout, _content.Vfx, _options.Seed, _vfxSettings);
            _turnCamera = new TurnCamera(_animation, _layout, _camera, from);
            _clockMs = 0;
            Log(turn);
        }

        /// <summary>
        /// Where the camera looks now: the turn's own framing while one plays
        /// (<see cref="TurnCamera"/>), else easing back from where the last turn left it toward the
        /// whole arena (<see cref="CameraSettings.ReturnMs"/>, on the played clock, so it respects
        /// the speed).
        /// </summary>
        private CameraView CameraNow()
        {
            if (_camera == null)
            {
                return default;
            }

            if (_animation != null && _turnCamera != null)
            {
                return _turnCamera.Sample(_clockMs);
            }

            return _camera.Ease(_cameraRest, _camera.FitAll, _cameraIdleMs, _camera.Settings.ReturnMs);
        }

        /// <summary>The skip button: plays every remaining turn at once and shows the result.</summary>
        private void SkipToEnd()
        {
            _animation = null;
            _turnCamera = null;
            _cameraRest = _camera.FitAll;
            PlayedTurn turn;
            while ((turn = _playback.Advance()) != null)
            {
                Log(turn);
            }

            _auto = false;
        }

        /// <summary>Plays turns silently up to the one to show, and sets the clock inside it.</summary>
        private void PrepareScreenshot()
        {
            PlayedTurn shown = null;
            for (int i = 0; i < _options.Turns; i++)
            {
                PlayedTurn turn = _playback.Advance();
                if (turn == null)
                {
                    break;
                }

                shown = turn;
                Log(turn);
            }

            int beatIndex = 0;
            if (!string.IsNullOrEmpty(_options.Skill))
            {
                while (shown != null && (beatIndex = BeatIndex(shown, _options.Skill)) < 0)
                {
                    shown = _playback.Advance();
                    if (shown != null)
                    {
                        Log(shown);
                    }
                }

                if (shown == null)
                {
                    Fail("No turn fired skill '" + _options.Skill + "' before the battle ended.");
                    return;
                }
            }

            if (shown == null)
            {
                return;
            }

            _animation = new TurnAnimation(shown, _layout, _content.Vfx, _options.Seed, _vfxSettings);
            _turnCamera = new TurnCamera(_animation, _layout, _camera, _camera.FitAll);
            _clockMs = _options.AtMs ?? _animation.MidVfxMs(beatIndex);
            Console.WriteLine("Screenshot: turn " + (shown.Index + 1) + " (" + Name(shown.Turn.Unit.Id) + "), " + _clockMs + " ms into its " +
                              _animation.DurationMs + " ms animation" +
                              (_playback.IsOver ? "; the battle ended: " + _playback.Outcome : string.Empty) + ".");
        }

        private static int BeatIndex(PlayedTurn turn, string skillId)
        {
            for (int i = 0; i < turn.Beats.Count; i++)
            {
                if (turn.Beats[i].SkillId == skillId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Renders the frame at K x 540x960 (the portrait canvas at K/2) into a PNG.</summary>
        private void SaveScreenshot()
        {
            if (FailureMessage != null)
            {
                return;
            }

            int scale = Math.Max(1, _options.Scale);
            using (RenderTarget2D target = new RenderTarget2D(GraphicsDevice, ViewerHost.DesktopWidth * scale, ViewerHost.DesktopHeight * scale))
            {
                GraphicsDevice.SetRenderTarget(target);
                RenderScene(target.Width, target.Height, _options.SafeInsets);
                GraphicsDevice.SetRenderTarget(null);

                string path = Path.GetFullPath(_options.ScreenshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(path))
                {
                    target.SaveAsPng(stream, target.Width, target.Height);
                }

                Console.WriteLine("Wrote " + path + " (" + target.Width + "x" + target.Height + ").");
            }
        }

        private void Fail(string message)
        {
            FailureMessage = message;
            if (_options.Screenshot)
            {
                Exit();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------------------------------

        private bool Pressed(KeyboardState keys, Keys key)
        {
            return keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
        }

        /// <summary>Android's Back button (MonoGame reports it as GamePad Back); touch hosts only.</summary>
        private bool BackPressed()
        {
            if (!_host.Touch)
            {
                return false;
            }

            bool back = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed;
            bool pressed = back && !_previousBack;
            _previousBack = back;
            return pressed;
        }

        /// <summary>Desktop mouse: hovering a skill shows its diagram; a click is a tap.</summary>
        private void ReadMouse(ref bool step)
        {
            MouseState mouse = Mouse.GetState();
            Vec2 at = _canvasFit.Scale > 0f ? _canvasFit.ToCanvas(mouse.X, mouse.Y) : new Vec2(-1f, -1f);
            _hoveredSkill = IsActive && !_settingsOpen ? SkillCardAt(at) : -1;
            if (IsActive && mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed)
            {
                Tap(at, ref step);
            }

            _previousMouse = mouse;
        }

        /// <summary>
        /// Touch as gestures, judged when the last finger lifts: two or more fingers down at once
        /// toggle auto-play; one finger is a tap where it lifted.
        /// </summary>
        private void ReadTouch(ref bool step)
        {
            TouchCollection touches = TouchPanel.GetState();
            int down = 0;
            foreach (TouchLocation touch in touches)
            {
                if (touch.State == TouchLocationState.Pressed || touch.State == TouchLocationState.Moved)
                {
                    down++;
                }

                _gestureEnd = touch.Position;
            }

            _gestureTouches = Math.Max(_gestureTouches, down);
            if (down > 0 || _gestureTouches == 0)
            {
                return;
            }

            if (_gestureTouches >= 2)
            {
                _auto = !_auto;
            }
            else
            {
                Tap(_canvasFit.ToCanvas(_gestureEnd.X, _gestureEnd.Y), ref step);
            }

            _gestureTouches = 0;
        }

        /// <summary>
        /// A tap or click at canvas point <paramref name="at"/>: the settings gear, the settings
        /// overlay while it is open (a row changes that setting; anywhere else closes it), a
        /// control, a skill card, else the board (step).
        /// </summary>
        private void Tap(Vec2 at, ref bool step)
        {
            if (_screen.SettingsButton.Contains(at.X, at.Y))
            {
                _settingsOpen = !_settingsOpen;
                return;
            }

            if (_settingsOpen)
            {
                for (int row = 0; row < PortraitLayout.SettingsRowCount; row++)
                {
                    if (_screen.SettingsRow(row).Contains(at.X, at.Y))
                    {
                        ChangeSetting(row);
                        return;
                    }
                }

                if (!_screen.SettingsPanel.Contains(at.X, at.Y))
                {
                    _settingsOpen = false;
                }

                return;
            }

            for (int i = 0; i < PortraitLayout.ControlCount; i++)
            {
                if (!_screen.Control(i).Contains(at.X, at.Y))
                {
                    continue;
                }

                if (i == ControlPause)
                {
                    _auto = !_auto;
                }
                else if (i == ControlSkip)
                {
                    SkipToEnd();
                }
                else
                {
                    _speed = i;
                }

                return;
            }

            int card = SkillCardAt(at);
            if (card >= 0)
            {
                _selectedSkill = card == _selectedSkill ? -1 : card;
                return;
            }

            if (_screen.Board.Contains(at.X, at.Y) || _screen.Toast.Contains(at.X, at.Y))
            {
                step = true;
            }
        }

        /// <summary>
        /// The UI font (<see cref="GameContent.UiFontPath"/>) in place of the pixel font, which
        /// stays only as the fallback when the TTF cannot be loaded.
        /// </summary>
        private void LoadFont()
        {
            TtfText font = TtfText.TryLoad(_content.Source, GameContent.UiFontPath, out string error);
            if (font == null)
            {
                Console.WriteLine("UI font not loaded (" + error + "); using the pixel font.");
                return;
            }

            _text.Dispose();
            _text = font;
        }

        /// <summary>
        /// The effects settings: the saved ones (<see cref="PlayerSettingsStore"/> in the default
        /// save folder) in a window, the defaults in a screenshot; then the command-line flags on top.
        /// </summary>
        private void LoadSettings()
        {
            _settings = new PlayerSettings();
            if (!_options.Screenshot)
            {
                try
                {
                    _settingsStore = new PlayerSettingsStore(SaveLocations.Default(), new JsonSaveSerializer(true));
                    _settings = _settingsStore.Load();
                }
                catch (Exception)
                {
                    // No writable save folder: play with the defaults and remember nothing.
                    _settingsStore = null;
                }
            }

            _options.ApplyTo(_settings);
            _vfxSettings = VfxSettings.From(_settings);
            _settingsOpen = _options.ShowSettings;
        }

        /// <summary>
        /// A settings-overlay row tapped: 0 cycles the effects intensity (Full, Reduced, Minimal),
        /// 1 toggles screen shake, 2 flashes, 3 closes. A change is saved at once and applies from
        /// the next turn played.
        /// </summary>
        private void ChangeSetting(int row)
        {
            switch (row)
            {
                case 0:
                    _settings.EffectsIntensity = _settings.EffectsIntensity == EffectsIntensity.Full
                                                     ? EffectsIntensity.Reduced
                                                     : _settings.EffectsIntensity == EffectsIntensity.Reduced ? EffectsIntensity.Minimal : EffectsIntensity.Full;
                    break;
                case 1:
                    _settings.ScreenShake = !_settings.ScreenShake;
                    break;
                case 2:
                    _settings.Flashes = !_settings.Flashes;
                    break;
                default:
                    _settingsOpen = false;
                    return;
            }

            _vfxSettings = VfxSettings.From(_settings);
            _settingsStore?.Save(_settings);
        }

        private int SkillCardAt(Vec2 at)
        {
            int count = ActingSkills().Count;
            for (int i = 0; i < count; i++)
            {
                if (_screen.SkillCard(i, count).Contains(at.X, at.Y))
                {
                    return i;
                }
            }

            return -1;
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        private string Name(string unitId)
        {
            return unitId != null && _names.TryGetValue(unitId, out string name) ? name : unitId;
        }

        private void Log(PlayedTurn turn)
        {
            string actor = Name(turn.Turn.Unit.Id);
            if (turn.Beats.Count == 0)
            {
                _log.Add(actor + (turn.Turn.Stunned ? ": STUNNED" : turn.Turn.MovementSpent > 0 ? ": MOVES" : ": WAITS"));
                return;
            }

            foreach (SkillBeat beat in turn.Beats)
            {
                int damage = 0;
                foreach (BeatTarget target in beat.Targets)
                {
                    damage += target.Damage;
                }

                string what = beat.SkillName ?? beat.SkillId;
                _log.Add(actor + ": " + what + (damage > 0 ? " " + damage.ToString(CultureInfo.InvariantCulture) : string.Empty));
            }
        }

        private static Dictionary<string, string> UnitNames(GameContent content, Dictionary<string, string> speciesByUnit)
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in speciesByUnit)
            {
                string display = entry.Value;
                if (content.Battle.GetSpecies(entry.Value) != null)
                {
                    display = content.Battle.GetSpecies(entry.Value).DisplayName;
                }
                else if (content.Enemies.Get(entry.Value) != null)
                {
                    display = content.Enemies.Get(entry.Value).DisplayName;
                }

                counts.TryGetValue(display, out int seen);
                counts[display] = seen + 1;
                names[entry.Key] = seen == 0 ? display : display + " " + (seen + 1).ToString(CultureInfo.InvariantCulture);
            }

            return names;
        }
    }
}
