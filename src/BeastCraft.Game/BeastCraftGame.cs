using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Game.Audio;
using BeastCraft.Game.Rendering;
using BeastCraft.Game.Screens;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Audio;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using BeastCraft.Save;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BeastCraft.Game
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The game every host runs: a <see cref="ScreenStack"/> of screens (title, home with the region
    /// map, encounter, battle, results) with modals over them, drawn on the fixed 1080x1920 portrait
    /// canvas (<see cref="PortraitLayout"/>) scaled uniformly and letterboxed into the window or
    /// screen inside its safe area (<see cref="ViewerHost.SafeArea"/>). The host reads the input
    /// once a frame, sends Back (Android Back, desktop Esc) to the stack, taps and drags to the top
    /// modal's or screen's widgets (the battle reads its own), draws the top screen, the modals, the
    /// toast and the transition veil, and autosaves when the app goes to the background or closes.
    /// <para>
    /// It starts at the title. The battle demo — the old viewer, one command-line battle, with the
    /// <c>--screenshot</c> mode — runs instead when a demo flag is given; <c>--screen</c> starts at
    /// another screen and <c>--walkthrough</c> takes scripted screenshots of the core loop
    /// (<see cref="ViewerOptions"/>).
    /// </para>
    /// </summary>
    // The namespace BeastCraft.Game would win over Microsoft.Xna.Framework.Game here, hence the full name.
    public sealed class BeastCraftGame : Microsoft.Xna.Framework.Game
    {
        /// <summary>
        /// The map seed scripted runs (walkthrough, --screen screenshots) draw their expedition with:
        /// one whose first location the starter team wins at level 1, so the walkthrough shows a
        /// victory (<c>--map-seed 20260927</c> shows the defeat path).
        /// </summary>
        public const int ScriptedMapSeed = 4;

        private readonly ViewerOptions _options;
        private readonly ViewerHost _host;
        private readonly GraphicsDeviceManager _graphics;
        private readonly ScreenStack _stack = new ScreenStack();
        private readonly ToastQueue _toast = new ToastQueue();
        private readonly Queue<(string Name, Action Step)> _script = new Queue<(string, Action)>();
        private SpriteBatch _batch;
        private SpriteRenderer _draw;
        private SpriteAtlas _atlas;
#if DEBUG || PERF_OVERLAY
        private Diagnostics.PerfOverlay _perf;
        private double _perfElapsedMs;
        private long _perfFrames;
        private bool _perfF3;
#endif
        private ITextRenderer _text;
        private Texture2D _pixel;
        private UiPainter _painter;
        private GameContent _content;
        private ScreenContext _ctx;
        private AudioDirector _audio;
        private readonly List<IDisposable> _audioPlayers = new List<IDisposable>();
        private KeyboardState _previousKeys;
        private MouseState _previousMouse;
        private bool _previousBack;
        private bool _pointerDown;
        private int _touchId = -1;
        private string _pendingCapture;
        private string _scriptDir;
        private int _captures;
        private bool _exitAfterScript;

        public BeastCraftGame(ViewerOptions options, ViewerHost host)
        {
            _options = options;
            _host = host;
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
            }
            else
            {
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

            // Leaving the app (Android pause, desktop focus loss or close) saves the game; coming back claims the idle rewards.
            Deactivated += (sender, args) => OnBackgrounded();
            Exiting += (sender, args) => OnBackgrounded();
            Activated += (sender, args) => OnResumed();
        }

        /// <summary>Set when the game could not start, a screenshot could not be written, or a scripted run failed.</summary>
        public string FailureMessage { get; private set; }

        /// <summary>The screen stack (tests of the host, scripted runs).</summary>
        public ScreenStack Stack
        {
            get { return _stack; }
        }

        /// <summary>The player's game, or null in the battle demo.</summary>
        public GameSession Session
        {
            get { return _ctx?.Session; }
        }

        /// <summary>
        /// Guards the save against two threads: the game loop (<see cref="Update"/> and
        /// <see cref="Draw"/>, which change and autosave it) and a platform lifecycle callback
        /// (Android's OnPause calls <see cref="OnBackgrounded"/>). Invariant: the save is only read,
        /// changed or written while holding this lock, so a background autosave never serialises a
        /// half-applied battle or claim, and OnPause stays synchronous — it waits for the frame in
        /// flight, then writes the save before returning, before Android may stop the process.
        /// (Monitor locks are re-entrant: on hosts whose loop runs on the UI thread nothing waits.)
        /// </summary>
        private readonly object _saveGate = new object();

        /// <summary>The app is going to the background or closing: autosave (a no-op before a game is loaded). Synchronous; see <see cref="_saveGate"/>.</summary>
        public void OnBackgrounded()
        {
            lock (_saveGate)
            {
                Background();
            }
        }

        private void Background()
        {
            GameSession session = _ctx?.Session;
            if (session == null || _options.Screenshot || !string.IsNullOrEmpty(_options.WalkthroughDir))
            {
                return;
            }

            session.Autosave(AutosaveReason.Background);
            if (_host.Notifier == null)
            {
                return;
            }

            DateTime? full = session.IdleCapUtc();
            if (session.Settings.IdleNotifications && full.HasValue && full.Value > DateTime.UtcNow)
            {
                _host.Notifier.Schedule(LocalNotification.IdleFull, full.Value, NotificationText(LocalNotification.IdleFull));
            }

            DateTime? grove = session.GroveReadyUtc();
            if (session.Settings.GroveNotifications && grove.HasValue && grove.Value > DateTime.UtcNow)
            {
                _host.Notifier.Schedule(LocalNotification.GroveReady, grove.Value, NotificationText(LocalNotification.GroveReady));
            }
        }

        /// <summary>A local notification's words from the text table (<c>ui.notify.*</c>; the Grove's reuse its ready toast).</summary>
        private LocalNotificationText NotificationText(LocalNotification kind)
        {
            bool grove = kind == LocalNotification.GroveReady;
            return new LocalNotificationText
            {
                Title = _ctx.Loc("ui.title.name"),
                Body = _ctx.Loc(grove ? "ui.grove.ready_toast" : "ui.notify.idle_body"),
                Channel = _ctx.Loc(grove ? "ui.notify.grove_channel" : "ui.notify.idle_channel"),
                ChannelDescription = _ctx.Loc(grove ? "ui.notify.grove_channel_desc" : "ui.notify.idle_channel_desc")
            };
        }

        /// <summary>
        /// The app is back in front: cancel the local notifications (idle and Grove), and claim the idle rewards once
        /// the map is showing (<see cref="GameSession.ResumeClaimPending"/>).
        /// </summary>
        public void OnResumed()
        {
            _host.Notifier?.Cancel(LocalNotification.IdleFull);
            _host.Notifier?.Cancel(LocalNotification.GroveReady);
            if (_ctx?.Session?.Save != null && string.IsNullOrEmpty(_options.WalkthroughDir) && !_options.Screenshot)
            {
                _ctx.Session.ResumeClaimPending = true;
            }
        }

        /// <summary>The settings modal's view-model, with the host's notifications (and their permission) wired in.</summary>
        public SettingsViewModel NewSettingsModel()
        {
            GameSession session = _ctx.Session;
            SettingsViewModel model = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings, _host.Notifier != null, _host.Haptics != null);
            model.IdleNotificationsChanged += on => NotificationSettingChanged(LocalNotification.IdleFull, on);
            model.GroveNotificationsChanged += on => NotificationSettingChanged(LocalNotification.GroveReady, on);
            model.ConsentChanged += () => session.Telemetry.Apply();
            return model;
        }

        /// <summary>A notification setting turned on (ask for the permission) or off (cancel it).</summary>
        private void NotificationSettingChanged(LocalNotification kind, bool on)
        {
            if (on)
            {
                _host.Notifier?.RequestPermission();
            }
            else
            {
                _host.Notifier?.Cancel(kind);
            }
        }

        /// <summary>Stops with <paramref name="message"/> (a screenshot or scripted run exits; a window shows it).</summary>
        public void Fail(string message)
        {
            FailureMessage = message;
            if (_options.Screenshot || !string.IsNullOrEmpty(_options.WalkthroughDir))
            {
                Exit();
            }
        }

        /// <summary>Shows <paramref name="message"/> briefly at the bottom of the screen.</summary>
        public void Toast(string message)
        {
            _toast.Show(message);
        }

        protected override void LoadContent()
        {
            _batch = new SpriteBatch(GraphicsDevice);
            _draw = new SpriteRenderer(_batch);
            _text = new PixelText(GraphicsDevice);
            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            List<string> errors = new List<string>();
            _content = _host.Content != null ? GameContent.Load(_host.Content, errors) : GameContent.Load(GameContent.FindRoot(_options.ContentRoot), errors);
            if (_content == null)
            {
                Fail("Could not load the content:\n" + string.Join("\n", errors));
                return;
            }

            _atlas = new SpriteAtlas(GraphicsDevice, _content);
            LoadFont();
            _painter = new UiPainter(GraphicsDevice, _draw, _text, _atlas, _content.Style);
            bool scripted = _options.Screenshot || !string.IsNullOrEmpty(_options.WalkthroughDir);
            _stack.Animate = !scripted;
            _ctx = new ScreenContext
            {
                Game = this,
                Device = GraphicsDevice,
                Draw = _draw,
                Atlas = _atlas,
                Text = _text,
                Painter = _painter,
                Content = _content,
                Options = _options,
                Host = _host,
                Stack = _stack,
                Toast = _toast
            };
            _audio = CreateAudio(scripted);
            _ctx.Audio = _audio;
#if DEBUG || PERF_OVERLAY
            // The frame-time overlay (#64): off unless asked for, so scripted screenshots stay as they are.
            _perf = new Diagnostics.PerfOverlay { Visible = _options.PerfOverlay };
            if (_options.PerfSeconds.HasValue)
            {
                // A measurement run keeps full speed when its window is not focused (MonoGame sleeps 20 ms a frame then).
                InactiveSleepTime = TimeSpan.Zero;
            }
#else
            if (_options.PerfOverlay || _options.PerfSeconds.HasValue)
            {
                Console.WriteLine("The frame-time overlay is not in this build (build Debug, or Release with -p:PerfOverlay=true).");
            }
#endif
            _stack.TopChanged += top => _audio.ScreenChanged(top?.Name, RegionOf(top));

            if (_options.IsDemo)
            {
                StartDemo();
                return;
            }

            _ctx.Session = new GameSession(_content, Storage(scripted), SeedSource(scripted), new SystemGameClock(_host.MonotonicClock));
            _ctx.Session.Telemetry.Use(_host.Analytics, _host.CrashReporter);
            if (_options.StarterLevel.HasValue)
            {
                _ctx.Session.StarterLevel = _options.StarterLevel.Value;
            }

            _stack.Push(new TitleScreen(_ctx));
            if (!string.IsNullOrEmpty(_options.WalkthroughDir))
            {
                _scriptDir = _options.WalkthroughDir;
                Walkthrough();
                _exitAfterScript = true;
            }
            else if (_options.StartScreen != "title" || _options.Screenshot)
            {
                GoTo(_options.StartScreen, _options.Screenshot ? _options.ScreenshotPath : null);
                _exitAfterScript = _options.Screenshot;
            }
        }

        protected override void UnloadContent()
        {
            foreach (IDisposable player in _audioPlayers)
            {
                player.Dispose();
            }

            _painter?.Dispose();
            _atlas?.Dispose();
            _text?.Dispose();
            _pixel?.Dispose();
            _batch?.Dispose();
        }

        protected override void Update(GameTime gameTime)
        {
#if DEBUG || PERF_OVERLAY
            _perf?.OnUpdate();
#endif
            lock (_saveGate)
            {
                UpdateFrame(gameTime);
            }
#if DEBUG || PERF_OVERLAY
            PerfTick(gameTime);
#endif
        }

        private void UpdateFrame(GameTime gameTime)
        {
            if (FailureMessage != null || (_options.IsDemo && _options.Screenshot))
            {
                base.Update(gameTime);
                return;
            }

            float elapsed = (float)gameTime.ElapsedGameTime.TotalMilliseconds;
            _audio?.Update(elapsed);
            if (_script.Count > 0 || _exitAfterScript)
            {
                // Scripted: one step a frame, each drawn (and captured) before the next.
                if (_pendingCapture == null)
                {
                    if (_script.Count > 0)
                    {
                        (string name, Action step) = _script.Dequeue();
                        RunStep(name, step);
                    }
                    else
                    {
                        Console.WriteLine("Scripted run done: " + _captures + " screenshot(s).");
                        Exit();
                    }
                }

                base.Update(gameTime);
                return;
            }

            _painter.TimeMs += elapsed;
            _stack.Update(elapsed);
            _toast.Tick(elapsed);
            FrameInput input = ReadInput();
            if (BackPressed(input))
            {
                Back();
            }

            GameModal modal = _stack.TopModal as GameModal;
            GameScreen screen = _stack.Top as GameScreen;
            RoutePointer(input, modal != null ? modal.Ui : screen != null && !screen.UsesRawInput ? screen.Ui : null);
            modal?.Update(elapsed);
            screen?.Update(elapsed, modal == null ? input : null);
            _previousKeys = input.Keys;
            _previousMouse = input.Mouse;
            base.Update(gameTime);
        }

#if DEBUG || PERF_OVERLAY
        /// <summary>F3 toggles the frame-time overlay; with --perf-seconds, the summary is printed and the app exits once the time is up.</summary>
        private void PerfTick(GameTime gameTime)
        {
            if (_perf == null)
            {
                return;
            }

            KeyboardState keys = Keyboard.GetState();
            if (keys.IsKeyDown(Keys.F3) && !_perfF3)
            {
                _perf.Visible = !_perf.Visible;
            }

            _perfF3 = keys.IsKeyDown(Keys.F3);
            _perfElapsedMs += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_options.PerfSeconds.HasValue && _perfElapsedMs >= _options.PerfSeconds.Value * 1000.0)
            {
                _perf.TextureBytes = _atlas?.TextureBytes ?? 0;
                Console.WriteLine(_perf.Summary());
                _options.PerfSeconds = null;
                Exit();
            }
        }

#endif

        protected override void Draw(GameTime gameTime)
        {
            lock (_saveGate)
            {
                DrawFrame(gameTime);
            }
        }

        private void DrawFrame(GameTime gameTime)
        {
            if (_options.IsDemo && _options.Screenshot)
            {
                Capture(_options.ScreenshotPath);
                Exit();
                return;
            }

            if (_pendingCapture != null)
            {
                Capture(_pendingCapture);
                _pendingCapture = null;
            }

            GraphicsDevice.SetRenderTarget(null);
            PresentationParameters back = GraphicsDevice.PresentationParameters;
            RenderScene(back.BackBufferWidth, back.BackBufferHeight, _host.SafeArea != null ? _host.SafeArea() : _options.SafeInsets);
            base.Draw(gameTime);
#if DEBUG || PERF_OVERLAY
            if (_perf != null)
            {
                if (_perfFrames++ % 60 == 0)
                {
                    _perf.TextureBytes = _atlas?.TextureBytes ?? 0;
                }

                _perf.OnDrawEnd(GraphicsDevice.Metrics);
            }
#endif
        }

        /// <summary>The back button: the stack's rule; at the root it quits.</summary>
        public void Back()
        {
            if (_stack.Back() == BackOutcome.Quit)
            {
                Exit();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Draws one frame onto a <paramref name="width"/> x <paramref name="height"/> target: the
        /// portrait canvas fitted inside <paramref name="insets"/> (black bars around it), the top
        /// screen, the modals, the toast and the transition veil, in canvas pixels.
        /// </summary>
        private void RenderScene(int width, int height, SafeInsets insets)
        {
            CanvasFit fit = CanvasFit.Of(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight, width, height, insets);
            GraphicsDevice.Clear(Color.Black);
            Matrix canvas = Matrix.CreateScale(fit.Scale) * Matrix.CreateTranslation(fit.OffsetX, fit.OffsetY, 0f);
            _draw.ResetStats();
            _draw.SetBlend(BlendState.AlphaBlend);
            _draw.SetClip(null);
            _draw.SetTransform(canvas);
            _draw.UnitSize = HexLayout.ColumnStep;
            if (FailureMessage != null || _ctx == null || _stack.Top == null)
            {
                DrawFailure();
                _draw.Flush();
                return;
            }

            _ctx.CanvasFit = fit;
            _painter.Begin(fit, canvas);
            (_stack.Top as GameScreen)?.Draw();
            _draw.SetBlend(BlendState.AlphaBlend);
            _draw.UnitSize = HexLayout.ColumnStep;
            foreach (IModal modal in _stack.Modals)
            {
                _painter.Begin(fit, canvas);
                (modal as GameModal)?.Draw();
            }

            _painter.Begin(fit, canvas);
            DrawToast();
            if (_stack.Transition != TransitionKind.None)
            {
                float veil = 1f - _stack.TransitionProgress;
                _painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), _painter.C("cream", veil * 0.85f));
            }

#if DEBUG || PERF_OVERLAY
            _perf?.Draw(_draw, _text, _pixel);
#endif
            _draw.Flush();
        }

        private void DrawToast()
        {
            if (!_toast.Visible)
            {
                return;
            }

            float alpha = _toast.Alpha;
            float size = _painter.Style.TextSizes.Body + 2f;
            float width = Math.Min(960f, _text.Measure(_toast.Message, size) + 120f);
            Rect box = new Rect((PortraitLayout.CanvasWidth - width) / 2f, PortraitLayout.CanvasHeight - 360f, width, 100f);
            _painter.Panel(box, _painter.Style.Panel("toast"), alpha);
            _painter.TextIn(_toast.Message, box.Inset(24f), size, _painter.C("cream", alpha), TextAlign.Center);
        }

        private void DrawFailure()
        {
            Color ink = _atlas == null ? new Color(0x1c, 0x14, 0x28) : _atlas.Palette("K", new Color(0x1c, 0x14, 0x28));
            _draw.Fill(_pixel, new Rectangle(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), ink);
            float y = 200f;
            foreach (string line in (FailureMessage ?? "NO BATTLE").Split('\n'))
            {
                _text.Draw(_draw, _text.Fit(line, 15f, PortraitLayout.CanvasWidth - 2f * PortraitLayout.Margin), new Vector2(PortraitLayout.Margin, y), 15f, Color.White);
                y += _text.LineHeight(15f);
            }
        }

        /// <summary>Renders the frame at K x 540x960 (the portrait canvas at K/2) into a PNG at <paramref name="path"/>.</summary>
        private void Capture(string path)
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

                string full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (FileStream stream = File.Create(full))
                {
                    target.SaveAsPng(stream, target.Width, target.Height);
                }

                _captures++;
                Console.WriteLine("Wrote " + full + " (" + target.Width + "x" + target.Height + ").");
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

        // ------------------------------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------------------------------

        private FrameInput ReadInput()
        {
            return new FrameInput
            {
                Keys = Keyboard.GetState(),
                Mouse = _host.Touch ? default : Mouse.GetState(),
                Touches = _host.Touch ? TouchPanel.GetState() : default,
                IsActive = IsActive
            };
        }

        /// <summary>Esc (desktop), or Android's Back button (MonoGame reports it as GamePad Back), pressed this frame.</summary>
        private bool BackPressed(FrameInput input)
        {
            bool esc = input.Keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape);
            if (!_host.Touch)
            {
                return esc;
            }

            bool back = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed;
            bool pressed = back && !_previousBack;
            _previousBack = back;
            bool requested = _backRequested;
            _backRequested = false;
            return pressed || esc || requested;
        }

        private volatile bool _backRequested;

        /// <summary>
        /// The platform's Back, when it does not arrive as a key (Android 13+ routes Back through
        /// <c>OnBackInvokedCallback</c>; without one the system just sends the app to the background).
        /// Handled on the next update. Thread-safe.
        /// </summary>
        public void RequestBack()
        {
            _backRequested = true;
        }

        /// <summary>The mouse's left button, or the first finger, as press / move / release in canvas pixels to <paramref name="ui"/>.</summary>
        private void RoutePointer(FrameInput input, UiRoot ui)
        {
            if (ui == null)
            {
                _pointerDown = false;
                _touchId = -1;
                return;
            }

            CanvasFit fit = _ctx.CanvasFit;
            if (fit.Scale <= 0f)
            {
                return;
            }

            if (_host.Touch)
            {
                foreach (TouchLocation touch in input.Touches)
                {
                    if (_touchId >= 0 && touch.Id != _touchId)
                    {
                        continue;
                    }

                    Vec2 at = fit.ToCanvas(touch.Position.X, touch.Position.Y);
                    if (touch.State == TouchLocationState.Pressed && _touchId < 0)
                    {
                        _touchId = touch.Id;
                        ui.OnPointerDown(at);
                    }
                    else if (touch.State == TouchLocationState.Moved && _touchId == touch.Id)
                    {
                        ui.OnPointerMove(at);
                    }
                    else if (touch.State == TouchLocationState.Released && _touchId == touch.Id)
                    {
                        _touchId = -1;
                        _audio?.Clicked(ui.OnPointerUp(at));
                    }
                }

                return;
            }

            if (!input.IsActive)
            {
                return;
            }

            Vec2 point = fit.ToCanvas(input.Mouse.X, input.Mouse.Y);
            bool down = input.Mouse.LeftButton == ButtonState.Pressed;
            if (down && !_pointerDown)
            {
                ui.OnPointerDown(point);
            }
            else if (down)
            {
                ui.OnPointerMove(point);
            }
            else if (_pointerDown)
            {
                _audio?.Clicked(ui.OnPointerUp(point));
            }

            _pointerDown = down;
            if (input.Mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue)
            {
                ScrollView view = ui.HitTest(point)?.ScrollAncestor();
                view?.ScrollTo(view.ScrollY - (input.Mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue) * 0.8f);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Saves
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Where the save lives: a throwaway in-memory storage for a screenshot, a fresh temporary
        /// folder for a walkthrough, else <c>--save-dir</c>, the host's root (the Android app's files
        /// directory) or the per-user folder (<see cref="SaveLocations"/>).
        /// </summary>
        private ISaveStorage Storage(bool scripted)
        {
            if (_options.Screenshot)
            {
                return new MemorySaveStorage();
            }

            if (!string.IsNullOrEmpty(_options.WalkthroughDir))
            {
                string temp = Path.Combine(Path.GetTempPath(), "beastcraft-walkthrough-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Console.WriteLine("Walkthrough save folder: " + SaveLocations.DefaultDirectory(temp));
                return new FileSaveStorage(SaveLocations.DefaultDirectory(temp));
            }

            string root = _options.SaveDir ?? _host.SaveRoot ?? SaveLocations.DefaultRoot();
            try
            {
                return new FileSaveStorage(SaveLocations.DefaultDirectory(root));
            }
            catch (Exception exception)
            {
                Console.WriteLine("No save folder (" + exception.Message + "); playing without saving.");
                return new MemorySaveStorage();
            }
        }

        private Func<int> SeedSource(bool scripted)
        {
            if (_options.MapSeed.HasValue)
            {
                int seed = _options.MapSeed.Value;
                return () => seed;
            }

            if (scripted)
            {
                return () => ScriptedMapSeed;
            }

            return () => Environment.TickCount;
        }

        /// <summary>
        /// The sound: MonoGame's effects and music players over the content's cues, and the host's haptics
        /// (none on desktop). Scripted runs (screenshots, the walkthrough) are silent and still, so they
        /// stay deterministic and need no audio device; a device that cannot play falls back to silence.
        /// </summary>
        private AudioDirector CreateAudio(bool scripted)
        {
            Func<PlayerSettings> settings = () => _ctx?.Session?.Settings;
            if (scripted)
            {
                return new AudioDirector(_content.AudioCues, null, null, null, settings);
            }

            IAudio effects = null;
            IMusicPlayer music = null;
            try
            {
                MonoGameAudio sfx = new MonoGameAudio(_content.AudioCues, _content.Source);
                _audioPlayers.Add(sfx);
                effects = sfx;
                MonoGameMusicPlayer player = new MonoGameMusicPlayer(_content.AudioCues, _content.Source);
                _audioPlayers.Add(player);
                music = player;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("[Audio] No sound on this device: " + e.Message);
            }

            return new AudioDirector(_content.AudioCues, effects, music, _host.Haptics, settings);
        }

        /// <summary>The region whose music a screen plays: a battle's own, else the expedition's (or the last region played).</summary>
        private string RegionOf(IScreen screen)
        {
            if (screen is BattleScreen battle)
            {
                return battle.RegionId;
            }

            CampaignProgress campaign = _ctx?.Session?.Save?.Campaign;
            return campaign == null ? null : campaign.HasActiveRun ? campaign.ActiveRun.RegionId : campaign.CurrentRegionId;
        }

        /// <summary>The battle demo: the command line's battle as the only screen (Back quits), with the saved effects settings (the defaults in a screenshot).</summary>
        private void StartDemo()
        {
            PlayerSettings settings = new PlayerSettings();
            Func<bool> save = null;
            if (!_options.Screenshot)
            {
                try
                {
                    PlayerSettingsStore store = new PlayerSettingsStore(new FileSaveStorage(SaveLocations.DefaultDirectory(_options.SaveDir ?? _host.SaveRoot ?? SaveLocations.DefaultRoot())),
                                                                        new JsonSaveSerializer(true));
                    settings = store.Load();
                    save = () => store.Save(settings);
                }
                catch (Exception)
                {
                    // No writable save folder: play with the defaults and remember nothing.
                }
            }

            BattleScreen battle = BattleScreen.Demo(_ctx, settings, save, out string error);
            if (battle == null)
            {
                Fail(error);
                return;
            }

            if (FailureMessage == null)
            {
                _stack.Push(battle);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Scripted runs: --screen and --walkthrough
        // ------------------------------------------------------------------------------------------

        private void RunStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception exception)
            {
                Fail("Scripted step '" + name + "' failed: " + exception.Message);
                Console.Error.WriteLine(exception);
                return;
            }

            if (FailureMessage == null && name != null && _scriptDir != null)
            {
                _pendingCapture = Path.Combine(_scriptDir, name + ".png");
            }
        }

        private void Step(string capture, Action step)
        {
            _script.Enqueue((capture, step));
        }

        /// <summary>
        /// The new player's first session, captured: the title, the first beast's pick, Hearthglen's
        /// map with its hints, the Keeper's shrine, the first fight (encounter, battle and results, with
        /// their hints), the first trial and its pick, the second trial's pick, the camp (the Keeper's
        /// scene, then the training and catch-up), the finale, the Keeper's farewell, the way on, and
        /// Verdant Hollow's map; then a coming-soon tab and the settings. Fights between the captured
        /// ones are fought headless (retried until won, as a player would).
        /// </summary>
        private void Walkthrough()
        {
            Step("01-title", () => { });
            Step("02-starter-pick", () => Title().StartNewGame());
            Step("03-starter-selected", () => Pick().Select("golem"));
            Step("04-hearthglen-map-hint", () => Pick().Confirm());
            Step("05-keeper-shrine", () =>
            {
                DismissHints();
                Home().OpenStory(0);
            });
            Step("06-first-encounter-hint", () =>
            {
                Modal<StoryModal>().Finish();
                DismissHints();
                Home().TapNode(1);
            });
            Step("07-first-encounter-party-hint", () => Modal<HintModal>().Dismiss());
            Step("08-battle-hint", () =>
            {
                DismissHints();
                Encounter().StartBattle();
                Battle().Update(16f, null);
            });
            Step("09-battle", () =>
            {
                DismissHints();
                Battle().ShowTurn(3);
            });
            Step("10-results-hint", () =>
            {
                DismissHints();
                Battle().SkipToEnd();
                Battle().HandBack();
            });
            Step("11-map-after", () =>
            {
                DismissHints();
                bool won = Results().Model.Victory;
                Results().Continue();
                DismissHints();
                if (!won)
                {
                    FightToWin(1);
                    Home().Enter();
                    DismissHints();
                }
            });
            Step("12-trial-encounter", () =>
            {
                FightToWin(2);
                FightToWin(3);
                Home().Enter();
                DismissHints();
                Home().TapNode(4);
            });
            Step("13-trial-pick-second", () =>
            {
                DismissHints();
                _stack.Pop();
                FightToWin(4);
                Home().Enter();
            });
            Step("14-trial-pick-second-chosen", () =>
            {
                DismissHints();
                TrialPickModal pick = Modal<TrialPickModal>();
                pick.Select(pick.Model.Options[0].SpeciesId);
            });
            Step("15-map-two-beasts", () =>
            {
                Modal<TrialPickModal>().Confirm();
                DismissHints();
            });
            Step("16-trial-pick-third", () =>
            {
                FightToWin(5);
                FightToWin(6);
                FightToWin(7);
                Home().Enter();
                DismissHints();
                TrialPickModal pick = Modal<TrialPickModal>();
                pick.Select(pick.Model.Options[0].SpeciesId);
            });
            Step("17-camp-keeper", () =>
            {
                Modal<TrialPickModal>().Confirm();
                DismissHints();
                Home().TapNode(8);
            });
            Step("18-camp", () =>
            {
                Modal<StoryModal>().Finish();
                DismissHints();
            });
            Step("19-camp-trained", () => Modal<CampModal>().Train());
            Step("20-finale-encounter", () =>
            {
                Modal<CampModal>().Train();
                FightToWin(9);
                Home().Enter();
                DismissHints();
                Home().TapNode(10);
            });
            Step("21-finale-battle", () =>
            {
                DismissHints();
                Encounter().StartBattle();
                DismissHints();
                Battle().ShowTurn(4);
            });
            Step("22-finale-results", () =>
            {
                Battle().SkipToEnd();
                Battle().HandBack();
                DismissHints();
                if (!Results().Model.Victory)
                {
                    Results().Continue();
                    FightToWin(10);
                    Home().Enter();
                }
            });
            Step("23-keeper-farewell", () =>
            {
                if (_stack.Top is ResultsScreen)
                {
                    Results().Continue();
                }

                DismissHints();
                Home().OpenStory(11);
            });
            Step("24-the-way-on", () => Modal<StoryModal>().Finish());
            Step("25-verdant-hollow-map", () =>
            {
                Modal<RegionCardModal>().Onward();
                DismissHints();
            });
            Step("26-grove", () => Home().SelectTab(HomeTab.Grove));
            Step("27-settings", () =>
            {
                _stack.Pop();
                Home().SelectTab(HomeTab.Map);
                Home().OpenSettings();
            });
        }

        /// <summary>Dismisses every tutorial hint showing (each may bring the next one due).</summary>
        private void DismissHints()
        {
            for (int guard = 0; guard < 20 && _stack.TopModal is HintModal hint; guard++)
            {
                hint.Dismiss();
            }
        }

        /// <summary>
        /// Fights location <paramref name="nodeId"/> headless with every owned beast (the walkthrough's
        /// uncaptured fights), retrying a loss as a player would, until it is won; then any pick it
        /// earned is made (the first option).
        /// </summary>
        private void FightToWin(int nodeId)
        {
            GameSession session = _ctx.Session;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                NodeBattle battle = NodeBattle.For(session, nodeId, out string error);
                if (battle == null)
                {
                    throw new InvalidOperationException("Location " + nodeId + ": " + error);
                }

                if (battle.Begin(session.Save.Beasts.ConvertAll(b => b.BeastId), null, out error) == null)
                {
                    throw new InvalidOperationException("Location " + nodeId + ": " + error);
                }

                ResultsViewModel results = battle.Complete();
                Console.WriteLine("Walkthrough fight at " + results.Subtitle + ": " + results.Outcome + ".");
                if (results.Victory)
                {
                    return;
                }
            }

            throw new InvalidOperationException("Location " + nodeId + " was not won in 12 attempts.");
        }

        /// <summary>A scripted new game past Hearthglen (the skip: Golem, Phoenix, Griffin), on the first campaign region's map.</summary>
        private void StartScriptedGame()
        {
            Title().StartNewGame();
            Pick().Skip();
            (_stack.TopModal as ConfirmModal)?.TapWidget("confirm");
            foreach (string species in new[] { "golem", "phoenix", "griffin" })
            {
                Pick().Select(species);
                Pick().Confirm();
            }
        }

        /// <summary>Scripts the way to <paramref name="screen"/> (capturing it to <paramref name="capture"/> when given).</summary>
        private void GoTo(string screen, string capture)
        {
            _scriptDir = capture == null ? null : Path.GetDirectoryName(Path.GetFullPath(capture));
            string name = capture == null ? null : Path.GetFileNameWithoutExtension(capture);
            List<Action> steps = new List<Action>();
            switch (screen)
            {
                case "title":
                    break;
                case "title-continue":
                    steps.Add(() =>
                    {
                        StartScriptedGame();
                        _stack.PopTo("title");
                    });
                    break;
                case "settings":
                case "settings-gameplay":
                case "settings-visuals":
                case "settings-audio":
                case "settings-privacy":
                    steps.Add(() => Title().OpenSettings());
                    break;
                case "credits":
                    steps.Add(() => _stack.Push(new CreditsScreen(_ctx)));
                    break;
                case "starter-pick":
                    steps.Add(() => Title().StartNewGame());
                    break;
                case "save-slots":
                    steps.Add(() =>
                    {
                        StartScriptedGame();
                        _stack.Push(new SaveSlotsScreen(_ctx));
                    });
                    break;
                case "hearthglen":
                    steps.Add(() =>
                    {
                        Title().StartNewGame();
                        Pick().Select("golem");
                        Pick().Confirm();
                    });
                    break;
                default:
                    steps.Add(() =>
                    {
                        if (_ctx.Session.AnySave && capture == null)
                        {
                            Title().EnterGame(true);
                        }
                        else
                        {
                            StartScriptedGame();
                        }
                    });
                    break;
            }

            if (screen == "roster" || screen == "grove" || screen == "avatar" || screen == "inventory")
            {
                HomeTab tab = (HomeTab)Array.IndexOf(HomeViewModel.TabNames, char.ToUpperInvariant(screen[0]) + screen.Substring(1));
                steps.Add(() => Home().SelectTab(tab));
            }

            if (screen == "settings-gameplay" || screen == "settings-visuals" || screen == "settings-audio" || screen == "settings-privacy")
            {
                SettingsTab tab = screen == "settings-visuals" ? SettingsTab.Visuals
                                      : screen == "settings-audio" ? SettingsTab.Audio
                                      : screen == "settings-privacy" ? SettingsTab.Privacy : SettingsTab.Gameplay;
                steps.Add(() => Top<SettingsScreen>().SelectTab(tab));
            }

            if (screen == "grove-glade" || screen == "grove-garden" || screen == "grove-board" || screen == "grove-npc")
            {
                GroveTab tab = screen == "grove-garden" ? GroveTab.Garden : screen == "grove-board" ? GroveTab.Board : screen == "grove-npc" ? GroveTab.Npc : GroveTab.Glade;
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Grove);
                    Top<GroveScreen>().SelectTab(tab);
                });
            }

            if (screen == "avatar-skills" || screen == "avatar-gear" || screen == "avatar-wardrobe")
            {
                AvatarTab tab = screen == "avatar-gear" ? AvatarTab.Gear : screen == "avatar-wardrobe" ? AvatarTab.Wardrobe : AvatarTab.Skills;
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Avatar);
                    Top<AvatarScreen>().SelectTab(tab);
                });
            }

            if (screen == "inventory-materials" || screen == "inventory-looks")
            {
                InventoryTab tab = screen == "inventory-looks" ? InventoryTab.Looks : InventoryTab.Materials;
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Inventory);
                    Top<InventoryScreen>().SelectTab(tab);
                });
            }

            if (screen == "shop-sell")
            {
                steps.Add(() =>
                {
                    SetupShop();
                    Top<ShopScreen>().SelectTab(ShopTab.Sell);
                });
            }

            // Verification aids for #46, not player-facing screens: the Glade's habitat canvas with placed decor, the
            // wardrobe's colour picker, and the Inventory with a new (unseen) piece of gear.
            if (screen == "grove-canvas")
            {
                steps.Add(() =>
                {
                    GameSession session = _ctx.Session;
                    session.Save.Grove.HabitatsUnlocked.Add("mossy_glade");
                    GladeViewModel glade = new GladeViewModel(session);
                    glade.SelectHabitat("mossy_glade");
                    string[] pieces = { "firefly_lantern", "ember_brazier", "wind_chimes" };
                    for (int i = 0; i < pieces.Length; i++)
                    {
                        session.Save.Grove.UnlockedDecorIds.Add(pieces[i]);
                        glade.PlaceDecor(pieces[i]);
                        glade.MoveDecor(pieces[i], 0.15f + 0.35f * i, 0.2f + 0.3f * i);
                    }

                    Home().SelectTab(HomeTab.Grove);
                    Top<GroveScreen>().SelectTab(GroveTab.Glade);
                    Top<GroveScreen>().ScrollGlade(1f);
                });
            }

            if (screen == "colour-picker")
            {
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Avatar);
                    Top<AvatarScreen>().SelectTab(AvatarTab.Wardrobe);
                    Top<AvatarScreen>().OpenColourPicker(0);
                });
            }

            if (screen == "inventory-new")
            {
                steps.Add(() =>
                {
                    GameSession session = _ctx.Session;
                    foreach (BeastCraft.Battle.GearSO candidate in session.Content.Economy.Gear.BeastGearAssets)
                    {
                        session.Save.Gear.AddBeastGear(candidate.GearId);
                        break;
                    }

                    Home().SelectTab(HomeTab.Inventory);
                });
            }

            // A verification aid, not a player-facing screen: the BeastPickerModal (equip gear to a
            // beast, buy a tome for one) at the full ten-species roster, to check it scrolls its rows
            // instead of overflowing past Close at a large roster size.
            if (screen == "picker10")
            {
                steps.Add(() =>
                {
                    GameSession session = _ctx.Session;
                    foreach (BeastCraft.Creatures.CreatureSpeciesSO species in session.Content.Species)
                    {
                        BeastCraft.Tutorial.StarterPicks.AddBeast(session.Save, session.Content.SkillLibrary, species.SpeciesId, 1);
                    }

                    BeastCraft.Battle.GearSO piece = null;
                    foreach (BeastCraft.Battle.GearSO candidate in session.Content.Economy.Gear.BeastGearAssets)
                    {
                        if (candidate.MinimumLevel <= 1)
                        {
                            piece = candidate;
                            break;
                        }
                    }

                    string instanceId = session.Save.Gear.AddBeastGear(piece.GearId);
                    session.Autosave(AutosaveReason.PlayerEdit);
                    Home().SelectTab(HomeTab.Inventory);
                    Top<InventoryScreen>().TapWidget("equip-" + instanceId);
                });
            }

            if (screen == "soothe")
            {
                steps.Add(() => SetupSoothe());
            }

            if (screen == "shop")
            {
                steps.Add(() => SetupShop());
            }

            if (screen == "colour-forms")
            {
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Roster);
                    Home().Roster.Open(Home().Roster.Model.Owned[0].BeastId);
                    Top<BeastDetailScreen>().SelectTab(2);
                    Top<BeastDetailScreen>().ScrollPage(1f);
                });
            }

            if (screen == "compendium")
            {
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Roster);
                    Home().Roster.OpenCompendium();
                });
            }

            if (screen == "achievements")
            {
                steps.Add(() => _stack.Push(new AchievementsScreen(_ctx)));
            }

            if (screen == "look-tokens")
            {
                steps.Add(() => _stack.Push(new LookTokenShopScreen(_ctx)));
            }

            if (screen == "beast-detail" || screen == "beast-derived" || screen == "beast-skills" || screen == "beast-gear")
            {
                int detailTab = screen == "beast-skills" ? 1 : screen == "beast-gear" ? 2 : 0;
                steps.Add(() =>
                {
                    Home().SelectTab(HomeTab.Roster);
                    Home().Roster.Open(Home().Roster.Model.Owned[0].BeastId);
                    Top<BeastDetailScreen>().SelectTab(detailTab);
                    if (screen == "beast-derived")
                    {
                        // The derived numbers: the turn rates, the element matchups, crits, the level-gap curve.
                        Top<BeastDetailScreen>().ScrollPage(1f);
                    }
                });
            }

            if (screen == "element-chart")
            {
                steps.Add(() => _stack.Push(new ElementChartScreen(_ctx, GlossaryScreen.TeamElements(_ctx))));
            }

            if (screen == "glossary")
            {
                steps.Add(() => _stack.Push(new GlossaryScreen(_ctx, "level_gap")));
            }

            if (screen.StartsWith("kinship-", StringComparison.Ordinal) || screen == "region-progress")
            {
                // The discovery layer: walk (headless, every fight counted won: a screenshot script) up to
                // the first stage's Kinship site's row, so the fog has lifted around the trail and the site calls.
                steps.Add(() => WalkToKinship());
            }

            if (screen == "kinship-poi")
            {
                steps.Add(() =>
                {
                    // Walk on until the fog reveals another point of interest (the map's own layout decides where).
                    for (int layer = 1; layer < 10 && Home().Map.Pois.Find(p => p.Kind != PoiKind.KinshipSite && p.State == PoiState.Revealed) == null; layer++)
                    {
                        WalkToKinship(layer);
                    }

                    Home().TapPoi(Home().Map.Pois.Find(p => p.Kind != PoiKind.KinshipSite && p.State == PoiState.Revealed)?.PoiId ??
                                  throw new InvalidOperationException("No point of interest revealed on the map."));
                });
            }

            if (screen == "kinship-trial")
            {
                steps.Add(() => Home().TapPoi(Home().Map.Pois.Find(p => p.Kind == PoiKind.KinshipSite).PoiId));
            }

            if (screen == "kinship-choice")
            {
                steps.Add(() =>
                {
                    GameSession session = _ctx.Session;
                    PointOfInterest site = DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).Find(p => p.Kind == PoiKind.KinshipSite);
                    KinshipRules.ResolveTrial(session.Save, session.Content.Discovery, site.PoiId, BeastCraft.Battle.BattleOutcome.PlayerVictory,
                                              session.Save.Beasts.ConvertAll(b => b.BeastId), false);
                    Home().Enter();
                    TrialPickModal pick = Modal<TrialPickModal>();
                    pick.Select(pick.Model.Options[0].SpeciesId);
                });
            }

            if (screen == "region-progress")
            {
                steps.Add(() => Home().OpenRegionProgress());
            }

            // Verification aids for #60, not player-facing screens: r11 on Hard, on the map (the header badge and the
            // Normal or Hard chip) and in the encounter preview (the badge), every earlier boss down.
            if (screen == "map-hard" || screen == "encounter-hard")
            {
                steps.Add(() =>
                {
                    PlayerSave save = _ctx.Session.Save;
                    save.Tutorial.HearthglenCleared = true;
                    foreach (RegionData region in _ctx.Content.Campaign.Regions)
                    {
                        if (!region.IsPostGame && !region.IsTutorial)
                        {
                            save.Campaign.Unlock(region.RegionId);
                            save.Campaign.FindRegion(region.RegionId).BossCleared = true;
                        }
                    }

                    save.Campaign.Unlock("r11");
                    CampaignRules.Retreat(save);
                    CampaignRules.StartRun(save, _ctx.Content.Campaign, "r11", 0, 7, RunDifficulty.Hard);
                    Home().Enter();
                });
            }

            // A verification aid for #62: the one-time consent screen over the map (scripted runs never offer it by themselves).
            if (screen == "consent")
            {
                steps.Add(() => _stack.PushModal(new ConsentModal(_ctx, new ConsentViewModel(_ctx.Session), null)));
            }

            if (screen == "encounter-hard")
            {
                steps.Add(() => Home().OpenFirstEncounter());
            }

            if (screen == "encounter" || screen == "encounter-insight" || screen == "battle" || screen == "results" || screen == "battle-log" || screen == "results-log" ||
                screen == "pause" || screen == "retreat-confirm")
            {
                steps.Add(() => Home().OpenFirstEncounter());
            }

            if (screen == "encounter-insight")
            {
                steps.Add(() => Encounter().ScrollToInsight());
            }

            if (screen == "battle" || screen == "results" || screen == "battle-log" || screen == "results-log" || screen == "pause" || screen == "retreat-confirm")
            {
                steps.Add(() => Encounter().StartBattle());
            }

            if (screen == "battle" && capture != null)
            {
                steps.Add(() => Battle().ShowTurn(3));
            }

            if (screen == "pause" || screen == "retreat-confirm")
            {
                steps.Add(() => Battle().OpenPauseMenu());
            }

            if (screen == "retreat-confirm")
            {
                steps.Add(() => Modal<PauseMenuModal>().TapWidget("retreat"));
            }

            if (screen == "battle-log")
            {
                // Far enough in for hits to log.
                steps.Add(() => Battle().ShowTurn(14));
            }

            if (screen == "battle-log")
            {
                steps.Add(() => Battle().OpenLog().OpenHit(-1));
            }

            if (screen == "results" || screen == "results-log")
            {
                steps.Add(() => Battle().HandBack());
            }

            if (screen == "results-log")
            {
                steps.Add(() => Results().OpenLog().OpenHit(-1));
            }

            for (int i = 0; i < steps.Count; i++)
            {
                Step(i == steps.Count - 1 ? name : null, steps[i]);
            }

            if (steps.Count == 0)
            {
                Step(name, () => { });
            }
        }

        /// <summary>
        /// Walks the expedition in progress up to the row of its map's Kinship site (a screenshot script:
        /// every fight on the way is counted won, camps train the first beast, traders are passed), then
        /// shows the map again.
        /// </summary>
        private void WalkToKinship(int toLayer = -1)
        {
            GameSession session = _ctx.Session;
            PointOfInterest site = DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).Find(p => p.Kind == PoiKind.KinshipSite);
            int layer = toLayer >= 0 ? toLayer : site == null ? 3 : site.Layer;
            MapRun run = session.Save.Campaign.ActiveRun;
            for (int guard = 0; guard < 20 && (run.CurrentNodeId < 0 || run.Find(run.CurrentNodeId).Layer < layer); guard++)
            {
                List<MapNode> choices = CampaignRules.Choices(run);
                MapNode next = choices.Find(n => n.Type == MapNodeType.Battle) ?? choices[0];
                if (next.Type == MapNodeType.Rest)
                {
                    CampaignRules.Camp(session.Save, session.Content.Campaign, next.NodeId, session.Save.Beasts[0].BeastId);
                }
                else if (next.Type == MapNodeType.Shop)
                {
                    CampaignRules.Trade(session.Save, session.Content.Campaign, next.NodeId, null);
                }
                else
                {
                    CampaignRules.ResolveBattle(session.Save, session.Content.Campaign, next.NodeId, BeastCraft.Battle.BattleOutcome.PlayerVictory);
                }
            }

            session.Autosave(AutosaveReason.Results);
            Home().Enter();
        }

        /// <summary>
        /// Walks the current expedition (camping/trading through Rest/Shop, winning any Elite in the
        /// way) until an ordinary Battle-node location is reachable, grants a few of that region's
        /// soothing items, then opens that location's encounter (its "Soothe" section is what
        /// <c>--screen soothe</c> is for).
        /// </summary>
        private void SetupSoothe()
        {
            GameSession session = _ctx.Session;
            Home().Enter();
            MapNodeView target = Home().Map.Reachable().Find(n => n.Type == MapNodeType.Battle);
            for (int guard = 0; guard < 40 && target == null; guard++)
            {
                MapNodeView next = Home().Map.Reachable()[0];
                if (next.Type == MapNodeType.Rest)
                {
                    CampaignRules.Camp(session.Save, session.Content.Campaign, next.NodeId, session.Save.Beasts[0].BeastId);
                }
                else if (next.Type == MapNodeType.Shop)
                {
                    CampaignRules.Trade(session.Save, session.Content.Campaign, next.NodeId, null);
                }
                else
                {
                    CampaignRules.ResolveBattle(session.Save, session.Content.Campaign, next.NodeId, BeastCraft.Battle.BattleOutcome.PlayerVictory);
                }

                Home().Enter();
                target = Home().Map.Reachable().Find(n => n.Type == MapNodeType.Battle);
            }

            if (target == null)
            {
                throw new InvalidOperationException("No ordinary Battle location found for --screen soothe.");
            }

            string regionId = session.Save.Campaign.ActiveRun.RegionId;
            BeastCraft.Grove.SoothingRegionData soothing = session.Content.GroveLibrary.Soothing(regionId);
            if (soothing != null && soothing.ItemIds != null && soothing.ItemIds.Length > 0)
            {
                session.Save.Grove.Items.Add(soothing.ItemIds[0], 5);
            }

            Home().TapNode(target.NodeId);
        }

        /// <summary>Walks the current expedition (camping through Rest nodes, winning any Elite in the way) until a Shop node is reachable, then opens its Trader.</summary>
        private void SetupShop()
        {
            GameSession session = _ctx.Session;
            Home().Enter();
            MapNodeView target = Home().Map.Reachable().Find(n => n.Type == MapNodeType.Shop);
            for (int guard = 0; guard < 40 && target == null; guard++)
            {
                MapNodeView next = Home().Map.Reachable()[0];
                if (next.Type == MapNodeType.Rest)
                {
                    CampaignRules.Camp(session.Save, session.Content.Campaign, next.NodeId, session.Save.Beasts[0].BeastId);
                }
                else if (next.Type != MapNodeType.Shop)
                {
                    CampaignRules.ResolveBattle(session.Save, session.Content.Campaign, next.NodeId, BeastCraft.Battle.BattleOutcome.PlayerVictory);
                }

                Home().Enter();
                target = Home().Map.Reachable().Find(n => n.Type == MapNodeType.Shop);
            }

            if (target == null)
            {
                throw new InvalidOperationException("No Shop location found for --screen shop.");
            }

            Home().TapNode(target.NodeId);
        }

        private T Top<T>() where T : class
        {
            return _stack.Top as T ?? throw new InvalidOperationException("Expected the " + typeof(T).Name + ", but the top screen is " + (_stack.Top?.Name ?? "none") + ".");
        }

        private TitleScreen Title()
        {
            return Top<TitleScreen>();
        }

        private HomeScreen Home()
        {
            return Top<HomeScreen>();
        }

        private StarterPickScreen Pick()
        {
            return Top<StarterPickScreen>();
        }

        private T Modal<T>() where T : class
        {
            return _stack.TopModal as T ?? throw new InvalidOperationException("Expected the " + typeof(T).Name + " modal, but the top modal is " + (_stack.TopModal?.Name ?? "none") + ".");
        }

        private EncounterScreen Encounter()
        {
            return Top<EncounterScreen>();
        }

        private BattleScreen Battle()
        {
            return Top<BattleScreen>();
        }

        private ResultsScreen Results()
        {
            return Top<ResultsScreen>();
        }
    }
}
