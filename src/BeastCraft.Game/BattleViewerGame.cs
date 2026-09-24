using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Session;
using BeastCraft.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BeastCraft.Game
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The battle viewer every host runs: one real PvE battle (<see cref="DemoBattle"/>) stepped a
    /// turn at a time through <see cref="BattlePlayback"/> and drawn pixel-perfect: everything
    /// renders into a 640x360 target that is scaled up by a whole number with point sampling and
    /// centred, letterboxed, in the window or screen. On desktop Space plays the next turn (or
    /// finishes the one playing), A toggles auto-play, Esc quits; on a touch device
    /// (<see cref="ViewerHost.Touch"/>) a tap steps, a two-finger tap or the on-screen AUTO button
    /// toggles auto-play and Back quits. With <c>--screenshot</c> it renders a single frame to a
    /// PNG and exits (<see cref="ViewerOptions"/>).
    /// <para>
    /// The viewer only reads the battle: turns are the session's own
    /// (<see cref="BattleSession.Begin"/>), and every animation is a pure function of the recorded
    /// results (<see cref="TurnAnimation"/>, <see cref="VfxTimeline"/>).
    /// </para>
    /// </summary>
    // The namespace BeastCraft.Game would win over Microsoft.Xna.Framework.Game here, hence the full name.
    public sealed class BattleViewerGame : Microsoft.Xna.Framework.Game
    {
        public const int VirtualWidth = 640;
        public const int VirtualHeight = 360;

        private const int BoardAreaX = 0;
        private const int BoardAreaY = 14;
        private const int BoardAreaWidth = 400;
        private const int BoardAreaHeight = 334;
        private const int PanelX = 412;
        private const int AutoPauseMs = 260;

        private readonly ViewerOptions _options;
        private readonly ViewerHost _host;
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _batch;
        private SpriteRenderer _draw;
        private RenderTarget2D _frame;
        private SpriteAtlas _atlas;
        private PixelText _text;

        private GameContent _content;
        private BattlePlayback _playback;
        private Dictionary<string, string> _speciesByUnit;
        private Dictionary<string, string> _names;
        private HexLayout _layout;

        private TurnAnimation _animation;
        private int _clockMs;
        private int _idleMs;
        private int _idleClockMs;
        private bool _auto;
        private KeyboardState _previousKeys;
        private bool _previousBack;
        private int _gestureTouches;
        private Vector2 _gestureEnd;
        private readonly List<string> _log = new List<string>();

        public BattleViewerGame(ViewerOptions options, ViewerHost host)
        {
            _options = options;
            _host = host;
            if (host.Touch)
            {
                // Full screen at the device's own resolution; the frame is letterboxed into it.
                _graphics = new GraphicsDeviceManager(this)
                {
                    IsFullScreen = true,
                    SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight,
                    SynchronizeWithVerticalRetrace = true
                };
                TouchPanel.EnableMouseTouchPoint = false;
                return;
            }

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = VirtualWidth * 2,
                PreferredBackBufferHeight = VirtualHeight * 2,
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
            _draw = new SpriteRenderer(_batch) { UnitSize = HexLayout.ColumnStep };
            _frame = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight);
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

            BattleSetup setup = DemoBattle.Create(_content, _options.Seed, out _speciesByUnit, out string error, null, DemoBattle.DefaultEncounterId,
                                                  _options.Level, _options.EnemyLevel);
            BattleSessionRun run = setup == null ? null : BattleSession.Begin(setup);
            if (run == null || run.Battle == null)
            {
                Fail("Could not begin the battle: " + (error ?? run.Result.Error));
                return;
            }

            _playback = new BattlePlayback(run);
            _layout = HexLayout.Centered(BoardAreaX, BoardAreaY, BoardAreaWidth, BoardAreaHeight);
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
            _frame?.Dispose();
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

            _previousKeys = keys;
            if (_host.Touch)
            {
                ReadTouch(ref step);
            }

            int elapsed = (int)gameTime.ElapsedGameTime.TotalMilliseconds;
            _idleClockMs += elapsed;

            if (_animation != null)
            {
                _clockMs += elapsed;
                if (step)
                {
                    _clockMs = _animation.DurationMs;
                    step = false;
                }

                if (_clockMs >= _animation.DurationMs)
                {
                    _animation = null;
                    _idleMs = 0;
                }
            }
            else
            {
                _idleMs += elapsed;
                if (step || (_auto && _idleMs >= AutoPauseMs))
                {
                    PlayNextTurn();
                }
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.SetRenderTarget(_frame);
            GraphicsDevice.Clear(new Color(0x1c, 0x14, 0x28));
            if (FailureMessage == null)
            {
                DrawFrame();
            }

            GraphicsDevice.SetRenderTarget(null);

            if (_options.Screenshot)
            {
                SaveScreenshot();
                Exit();
                return;
            }

            GraphicsDevice.Clear(Color.Black);
            Rectangle target = FrameTarget();
            _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            _batch.Draw(_frame, target, Color.White);
            _batch.End();
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

            _animation = new TurnAnimation(turn, _layout, _content.Vfx, _options.Seed);
            _clockMs = 0;
            Log(turn);
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

            _animation = new TurnAnimation(shown, _layout, _content.Vfx, _options.Seed);
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

        private void SaveScreenshot()
        {
            if (FailureMessage != null)
            {
                return;
            }

            int scale = Math.Max(1, _options.Scale);
            using (RenderTarget2D big = new RenderTarget2D(GraphicsDevice, VirtualWidth * scale, VirtualHeight * scale))
            {
                GraphicsDevice.SetRenderTarget(big);
                GraphicsDevice.Clear(Color.Black);
                _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
                _batch.Draw(_frame, new Rectangle(0, 0, big.Width, big.Height), Color.White);
                _batch.End();
                GraphicsDevice.SetRenderTarget(null);

                string path = Path.GetFullPath(_options.ScreenshotPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(path))
                {
                    big.SaveAsPng(stream, big.Width, big.Height);
                }

                Console.WriteLine("Wrote " + path + " (" + big.Width + "x" + big.Height + ").");
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

        private bool Pressed(KeyboardState keys, Keys key)
        {
            return keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
        }

        /// <summary>
        /// Where the 640x360 frame goes on the back buffer: scaled by the largest whole number that
        /// fits (at least 1) and centred, so a phone's wider aspect ratio gets black bars.
        /// </summary>
        private Rectangle FrameTarget()
        {
            int backWidth = GraphicsDevice.PresentationParameters.BackBufferWidth;
            int backHeight = GraphicsDevice.PresentationParameters.BackBufferHeight;
            int scale = Math.Max(1, Math.Min(backWidth / VirtualWidth, backHeight / VirtualHeight));
            int width = VirtualWidth * scale;
            int height = VirtualHeight * scale;
            return new Rectangle((backWidth - width) / 2, (backHeight - height) / 2, width, height);
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

        /// <summary>
        /// Touch as gestures, judged when the last finger lifts: two or more fingers down at once
        /// toggle auto-play; one finger toggles it on the AUTO button and steps anywhere else.
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

            if (_gestureTouches >= 2 || AutoButton().Contains(ToFrame(_gestureEnd)))
            {
                _auto = !_auto;
            }
            else
            {
                step = true;
            }

            _gestureTouches = 0;
        }

        /// <summary>A back-buffer point in frame pixels.</summary>
        private Point ToFrame(Vector2 screen)
        {
            Rectangle target = FrameTarget();
            int scale = target.Width / VirtualWidth;
            return new Point((int)Math.Floor((screen.X - target.X) / scale), (int)Math.Floor((screen.Y - target.Y) / scale));
        }

        /// <summary>The on-screen AUTO button (touch hosts), at the foot of the right-hand panel.</summary>
        private static Rectangle AutoButton()
        {
            return new Rectangle(PanelX, VirtualHeight - 30, VirtualWidth - PanelX - 6, 22);
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        private void DrawFrame()
        {
            ScheduledBeat beat = _animation == null ? null : _animation.BeatAt(_clockMs);
            VfxFrame vfx = beat == null ? null : beat.Timeline.Sample(_clockMs - beat.StartMs);
            Vec2 shake = vfx == null ? Vec2.Zero : vfx.Shake;
            Matrix camera = Matrix.CreateTranslation((float)Math.Round(shake.X), (float)Math.Round(shake.Y), 0f);

            _draw.SetTransform(camera);
            _draw.SetBlend(BlendState.AlphaBlend);
            DrawBoard();
            DrawUnits(beat, vfx);
            if (vfx != null)
            {
                DrawVfx(beat, vfx, false);
                _draw.SetBlend(BlendState.Additive);
                DrawVfx(beat, vfx, true);
                DrawFlash(beat, vfx);
                _draw.SetBlend(BlendState.AlphaBlend);
                DrawDamageNumbers(beat, vfx);
            }

            _draw.SetTransform(Matrix.Identity);
            DrawHud(beat);
            _draw.Flush();
        }

        private void DrawBoard()
        {
            ArtSprite grass = _atlas.Sprite("hex_grass");
            ArtSprite rock = _atlas.Sprite("hex_scorched");
            foreach (HexCoordinate tile in _playback.Grid.Tiles)
            {
                ArtSprite sprite = _playback.Grid.IsInDeploymentZone(tile, BattleTeam.Enemy) ? rock : grass;
                _draw.DrawSprite(sprite, 0, At(_layout.Center(tile)), 1f, Color.White);
            }
        }

        private void DrawUnits(ScheduledBeat beat, VfxFrame vfx)
        {
            IReadOnlyDictionary<string, UnitSnapshot> state = _animation != null ? _animation.Turn.After : _playback.Current;
            List<UnitSnapshot> units = new List<UnitSnapshot>(state.Values);
            units.Sort((a, b) => Center(a).Y.CompareTo(Center(b).Y));

            string actor = _animation == null ? null : _animation.Turn.Turn.Unit.Id;
            ArtSprite mask = _atlas.Sprite("hex_mask");
            ArtSprite outline = _atlas.Sprite("hex_outline");

            // Footprints first, so every sprite stands on top of every tile tint.
            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Color team = unit.Team == BattleTeam.Player ? _atlas.Palette("c", Color.Blue) : _atlas.Palette("r", Color.Red);
                foreach (HexCoordinate tile in Footprints.Tiles(Position(unit), unit.Footprint))
                {
                    Vector2 at = At(_layout.Center(tile));
                    _draw.DrawSprite(mask, 0, at, 1f, team * 0.35f);
                    if (unit.Id == actor)
                    {
                        _draw.DrawSprite(outline, 0, at, 1f, _atlas.Palette("Y", Color.Yellow));
                    }
                }
            }

            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Vector2 at = At(Center(unit));
                float scale = UnitScale(unit);
                bool fading = _animation != null && _animation.ShownFading(unit.Id, _clockMs);
                ArtSprite sprite = SpriteFor(unit.Id);
                DrawUnitSprite(sprite, unit, at, fading ? Color.White * 0.4f : Color.White);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.Hp;
                DrawHpBar((int)at.X, (int)Math.Round(at.Y - HeadHeight(sprite, scale)) - 3, scale <= 1f ? 24 : 40, hp, unit.MaxHp);
            }
        }

        private void DrawHpBar(int centerX, int y, int width, int hp, int maxHp)
        {
            int x = centerX - width / 2;
            float fraction = maxHp <= 0 ? 0f : Math.Max(0f, Math.Min(1f, hp / (float)maxHp));
            string fill = fraction > 0.5f ? "l" : fraction > 0.25f ? "y" : "o";
            _draw.Fill(_atlas.Pixel, new Rectangle(x - 1, y - 1, width + 2, 4), _atlas.Palette("K", Color.Black));
            _draw.Fill(_atlas.Pixel, new Rectangle(x, y, width, 2), _atlas.Palette("1", Color.DarkGray));
            _draw.Fill(_atlas.Pixel, new Rectangle(x, y, (int)Math.Ceiling(width * fraction), 2), _atlas.Palette(fill, Color.Green));
        }

        private void DrawVfx(ScheduledBeat beat, VfxFrame vfx, bool additivePass)
        {
            VfxEffectData effect = beat.Timeline.Effect;

            VfxSpriteData projectile = effect.Projectile;
            if (projectile != null && projectile.Additive == additivePass)
            {
                foreach (Vec2 at in vfx.Projectiles)
                {
                    DrawCentered(projectile.Sheet, 0, at, projectile.Scale, _atlas.Palette(projectile.Tint, Color.White));
                }
            }

            VfxFlipbookData flipbook = effect.Flipbook;
            if (flipbook != null && flipbook.Additive == additivePass && vfx.FlipbookFrame >= 0)
            {
                foreach (VfxTarget target in beat.Timeline.Targets)
                {
                    DrawCentered(flipbook.Sheet, vfx.FlipbookFrame, target.Position, flipbook.Scale, _atlas.Palette(flipbook.Tint, Color.White));
                }
            }

            VfxParticleData particles = effect.Particles;
            if (particles != null && particles.Additive == additivePass)
            {
                foreach (ParticleState particle in vfx.Particles)
                {
                    string ch = particles.Colors.Length == 0 ? null : particles.Colors[particle.ColorIndex];
                    DrawCentered(particles.Sheet, 0, particle.Position, 1, _atlas.Palette(ch, Color.White) * particle.Alpha);
                }
            }
        }

        private void DrawFlash(ScheduledBeat beat, VfxFrame vfx)
        {
            VfxFlashData flash = beat.Timeline.Effect.HitFlash;
            if (flash == null || vfx.FlashAlpha <= 0f)
            {
                return;
            }

            Color tint = _atlas.Palette(flash.Tint, Color.White) * (vfx.FlashAlpha * 0.8f);
            foreach (BeatTarget target in beat.Beat.Targets)
            {
                if (!_animation.Turn.After.TryGetValue(target.UnitId, out UnitSnapshot unit))
                {
                    continue;
                }

                DrawUnitSprite(SpriteFor(unit.Id), unit, At(Center(unit)), tint);
            }
        }

        private void DrawDamageNumbers(ScheduledBeat beat, VfxFrame vfx)
        {
            VfxDamageNumberData spec = beat.Timeline.Effect.DamageNumber;
            if (spec == null)
            {
                return;
            }

            foreach (DamageNumberState number in vfx.DamageNumbers)
            {
                string ch = number.Crit && !string.IsNullOrEmpty(spec.CritColor) ? spec.CritColor : spec.Color;
                string text = number.Value.ToString(CultureInfo.InvariantCulture) + (number.Crit ? "!" : string.Empty);
                Color color = _atlas.Palette(ch, Color.White) * number.Alpha;
                // Above the unit's head: a big unit is drawn at twice the size.
                bool big = _animation.Turn.After.TryGetValue(number.UnitId ?? string.Empty, out UnitSnapshot unit) && unit.Footprint != UnitFootprint.Single;
                int lift = big ? 66 : 38;
                _text.DrawCentered(_draw.Batch(SamplerState.PointClamp), text, (int)Math.Round(number.Position.X), (int)Math.Round(number.Position.Y) - lift, color, 2,
                                   _atlas.Palette("K", Color.Black) * number.Alpha);
            }
        }

        private void DrawCentered(string sheet, int frame, Vec2 at, int scale, Color color)
        {
            _draw.DrawSprite(_atlas.Sprite(sheet), frame, At(at), scale, color);
        }

        /// <summary>
        /// A unit's sprite, pivot (feet) on <paramref name="at"/>, facing the other side, at its
        /// footprint's size; a sprite with an <c>idle</c> clip plays it on the viewer's clock.
        /// </summary>
        private void DrawUnitSprite(ArtSprite sprite, UnitSnapshot unit, Vector2 at, Color color)
        {
            if (sprite == null)
            {
                return;
            }

            bool flip = unit.Team == BattleTeam.Enemy;
            ArtAnimationData idle = sprite.Data.Animation("idle");
            ArtSprite sheet = idle == null ? null : _atlas.Sprite(string.IsNullOrEmpty(idle.Sheet) ? sprite.Name : idle.Sheet);
            if (sheet != null)
            {
                _draw.DrawFrame(sprite, sheet.Texture, sheet.Frame(idle.FrameAt(_clockMs + _idleClockMs)), at, UnitScale(unit), color, flip, 0f);
                return;
            }

            _draw.DrawSprite(sprite, 0, at, UnitScale(unit), color, flip);
        }

        /// <summary>A unit's draw scale: one hex for a one-tile unit, two for a large one.</summary>
        private static float UnitScale(UnitSnapshot unit)
        {
            return unit.Footprint == UnitFootprint.Single ? 1f : 2f;
        }

        /// <summary>How far a sprite drawn at <paramref name="scale"/> reaches above its pivot, in board pixels.</summary>
        private float HeadHeight(ArtSprite sprite, float scale)
        {
            if (sprite == null)
            {
                return 24f * scale;
            }

            return sprite.Pivot.Y / Math.Max(1f, sprite.Data.PixelsPerUnit) * _draw.UnitSize * scale;
        }

        private static Vector2 At(Vec2 point)
        {
            return new Vector2((float)Math.Round(point.X), (float)Math.Round(point.Y));
        }

        private void DrawHud(ScheduledBeat beat)
        {
            Color ink = _atlas.Palette("4", Color.White);
            SpriteBatch batch = _draw.Batch(SamplerState.PointClamp);
            Color dim = _atlas.Palette("3", Color.Gray);
            Color gold = _atlas.Palette("y", Color.Gold);
            Color shadow = _atlas.Palette("K", Color.Black);

            int turnNumber = _playback.Played.Count;
            _text.Draw(batch, _host.HudTitle, 6, 4, gold, 1, shadow);
            _text.Draw(batch, "TURN " + turnNumber + "   SEED " + _options.Seed.ToString(CultureInfo.InvariantCulture), 200, 4, ink, 1, shadow);

            // Right-hand panel: the acting unit and its skill, the turn order, the log.
            batch.Draw(_atlas.Pixel, new Rectangle(PanelX - 6, 0, VirtualWidth - PanelX + 6, VirtualHeight), _atlas.Palette("p", Color.Purple) * 0.55f);
            int y = 16;
            if (_animation != null)
            {
                _text.Draw(batch, "NOW: " + Name(_animation.Turn.Turn.Unit.Id), PanelX, y, gold, 1, shadow);
                y += 8;
                if (beat != null)
                {
                    _text.Draw(batch, beat.Beat.SkillName ?? beat.Beat.SkillId, PanelX + 8, y, ink, 1, shadow);
                }

                y += 10;
            }

            _text.Draw(batch, "TURN ORDER", PanelX, y, dim, 1, shadow);
            y += 8;
            foreach (BattleUnit unit in _playback.Forecast(8))
            {
                Color team = unit.Team == BattleTeam.Player ? _atlas.Palette("C", Color.LightBlue) : _atlas.Palette("u", Color.Pink);
                batch.Draw(_atlas.Pixel, new Rectangle(PanelX, y + 1, 3, 3), team);
                _text.Draw(batch, Name(unit.Id), PanelX + 6, y, ink, 1, shadow);
                int shown = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.CurrentHp;
                string hp = shown.ToString(CultureInfo.InvariantCulture) + "/" + unit.Stats.Hp.ToString(CultureInfo.InvariantCulture);
                _text.Draw(batch, hp, VirtualWidth - 6 - PixelText.Measure(hp), y, dim, 1, shadow);
                y += 8;
            }

            y += 6;
            _text.Draw(batch, "LOG", PanelX, y, dim, 1, shadow);
            y += 8;
            int first = Math.Max(0, _log.Count - 18);
            for (int i = first; i < _log.Count; i++)
            {
                _text.Draw(batch, _log[i], PanelX, y, ink, 1, shadow);
                y += 7;
            }

            if (_playback.IsOver && (_animation == null || _clockMs >= _animation.DurationMs))
            {
                string banner = _playback.Outcome == BattleOutcome.PlayerVictory ? "VICTORY" : _playback.Outcome == BattleOutcome.EnemyVictory ? "DEFEAT" : "STALEMATE";
                batch.Draw(_atlas.Pixel, new Rectangle(0, VirtualHeight / 2 - 18, PanelX - 6, 36), shadow * 0.75f);
                _text.DrawCentered(batch, banner, BoardAreaWidth / 2, VirtualHeight / 2 - 10, gold, 4);
            }

            string help = _host.Touch
                              ? "TAP: STEP   TWO FINGERS: AUTO " + (_auto ? "ON" : "OFF") + "   BACK: QUIT"
                              : "SPACE: STEP   A: AUTO " + (_auto ? "ON" : "OFF") + "   ESC: QUIT";
            _text.Draw(batch, help, 6, VirtualHeight - 9, dim, 1, shadow);

            if (_host.Touch)
            {
                Rectangle button = AutoButton();
                batch.Draw(_atlas.Pixel, button, shadow * 0.8f);
                batch.Draw(_atlas.Pixel, new Rectangle(button.X + 1, button.Y + 1, button.Width - 2, button.Height - 2),
                            (_auto ? gold : dim) * 0.35f);
                _text.DrawCentered(batch, _auto ? "AUTO: ON" : "AUTO: OFF", button.Center.X, button.Y + 4, _auto ? gold : ink, 2, shadow);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        private bool Standing(UnitSnapshot unit)
        {
            return _animation != null ? _animation.ShownStanding(unit.Id, _clockMs) : !unit.Defeated;
        }

        private HexCoordinate Position(UnitSnapshot unit)
        {
            if (_animation != null && _animation.Turn.Before.TryGetValue(unit.Id, out UnitSnapshot before) && _clockMs < _animation.MoveMs)
            {
                return before.Position;
            }

            return unit.Position;
        }

        private Vec2 Center(UnitSnapshot unit)
        {
            return _animation != null ? _animation.UnitCenter(unit.Id, _clockMs) : _layout.FootprintCenter(unit.Position, unit.Footprint);
        }

        /// <summary>
        /// The sprite a unit is drawn with: its species' or enemy's ArtKey (data) looked up in the art
        /// manifest; the content validator holds every shipped key to an entry, so the fallback (the
        /// brute) only shows for content loaded without one.
        /// </summary>
        private ArtSprite SpriteFor(string unitId)
        {
            string id = _speciesByUnit.TryGetValue(unitId, out string species) ? species : null;
            string artKey = _content.Battle.GetSpecies(id)?.ArtKey ?? _content.Enemies.Get(id)?.ArtKey;
            return _atlas.ByArtKey(artKey) ?? _atlas.ByArtKey("enemy/brute");
        }

        private string Name(string unitId)
        {
            return _names.TryGetValue(unitId, out string name) ? name : unitId;
        }

        private void Log(PlayedTurn turn)
        {
            string actor = Name(turn.Turn.Unit.Id);
            if (turn.Beats.Count == 0)
            {
                _log.Add(Trim(actor + (turn.Turn.Stunned ? ": STUNNED" : turn.Turn.MovementSpent > 0 ? ": MOVES" : ": WAITS")));
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
                _log.Add(Trim(actor + ": " + what + (damage > 0 ? " " + damage.ToString(CultureInfo.InvariantCulture) : string.Empty)));
            }
        }

        private static string Trim(string line)
        {
            const int max = (VirtualWidth - PanelX - 4) / 4;
            return line.Length <= max ? line : line.Substring(0, max);
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
