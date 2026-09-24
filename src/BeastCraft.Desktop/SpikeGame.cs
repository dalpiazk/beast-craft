using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Desktop.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Session;
using BeastCraft.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace BeastCraft.Desktop
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// The desktop spike: one real PvE battle (<see cref="DemoBattle"/>) stepped a turn at a time
    /// through <see cref="BattlePlayback"/> and drawn pixel-perfect: everything renders into a
    /// 640x360 target that is scaled up by a whole number with point sampling. Space plays the next
    /// turn (or finishes the one playing), A toggles auto-play, Esc quits. With
    /// <c>--screenshot</c> it renders a single frame to a PNG and exits (<see cref="SpikeOptions"/>).
    /// <para>
    /// The viewer only reads the battle: turns are the session's own
    /// (<see cref="BattleSession.Begin"/>), and every animation is a pure function of the recorded
    /// results (<see cref="TurnAnimation"/>, <see cref="VfxTimeline"/>).
    /// </para>
    /// </summary>
    public sealed class SpikeGame : Game
    {
        public const int VirtualWidth = 640;
        public const int VirtualHeight = 360;

        private const int BoardAreaX = 0;
        private const int BoardAreaY = 14;
        private const int BoardAreaWidth = 400;
        private const int BoardAreaHeight = 334;
        private const int PanelX = 412;
        private const int AutoPauseMs = 260;

        private readonly SpikeOptions _options;
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _batch;
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
        private bool _auto;
        private KeyboardState _previousKeys;
        private readonly List<string> _log = new List<string>();

        public SpikeGame(SpikeOptions options)
        {
            _options = options;
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = VirtualWidth * 2,
                PreferredBackBufferHeight = VirtualHeight * 2,
                SynchronizeWithVerticalRetrace = true
            };
            IsMouseVisible = true;
            Window.AllowUserResizing = true;
            Window.Title = "Beast Craft - desktop spike (Space: step  A: auto  Esc: quit)";
        }

        /// <summary>Set when the spike could not start or could not write its screenshot.</summary>
        public string FailureMessage { get; private set; }

        protected override void LoadContent()
        {
            _batch = new SpriteBatch(GraphicsDevice);
            _frame = new RenderTarget2D(GraphicsDevice, VirtualWidth, VirtualHeight);
            _text = new PixelText(GraphicsDevice);

            List<string> errors = new List<string>();
            _content = GameContent.Load(GameContent.FindRoot(_options.ContentRoot), errors);
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
            if (keys.IsKeyDown(Keys.Escape))
            {
                Exit();
            }

            bool step = Pressed(keys, Keys.Space);
            if (Pressed(keys, Keys.A))
            {
                _auto = !_auto;
            }

            _previousKeys = keys;
            int elapsed = (int)gameTime.ElapsedGameTime.TotalMilliseconds;

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
            int scale = Math.Max(1, Math.Min(GraphicsDevice.PresentationParameters.BackBufferWidth / VirtualWidth,
                                             GraphicsDevice.PresentationParameters.BackBufferHeight / VirtualHeight));
            int width = VirtualWidth * scale;
            int height = VirtualHeight * scale;
            Rectangle target = new Rectangle((GraphicsDevice.PresentationParameters.BackBufferWidth - width) / 2,
                                             (GraphicsDevice.PresentationParameters.BackBufferHeight - height) / 2, width, height);
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

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        private void DrawFrame()
        {
            ScheduledBeat beat = _animation == null ? null : _animation.BeatAt(_clockMs);
            VfxFrame vfx = beat == null ? null : beat.Timeline.Sample(_clockMs - beat.StartMs);
            Vec2 shake = vfx == null ? Vec2.Zero : vfx.Shake;
            Matrix camera = Matrix.CreateTranslation((float)Math.Round(shake.X), (float)Math.Round(shake.Y), 0f);

            _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, camera);
            DrawBoard();
            DrawUnits(beat, vfx);
            if (vfx != null)
            {
                DrawVfx(beat, vfx, false);
            }

            _batch.End();

            if (vfx != null)
            {
                _batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.PointClamp, null, null, null, camera);
                DrawVfx(beat, vfx, true);
                DrawFlash(beat, vfx);
                _batch.End();

                _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, camera);
                DrawDamageNumbers(beat, vfx);
                _batch.End();
            }

            _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            DrawHud(beat);
            _batch.End();
        }

        private void DrawBoard()
        {
            Texture2D grass = _atlas.Texture("hex_grass");
            Texture2D rock = _atlas.Texture("hex_scorched");
            foreach (HexCoordinate tile in _playback.Grid.Tiles)
            {
                (int x, int y) = _layout.TileTopLeft(tile);
                Texture2D texture = _playback.Grid.IsInDeploymentZone(tile, BattleTeam.Enemy) ? rock : grass;
                _batch.Draw(texture, new Vector2(x, y), Color.White);
            }
        }

        private void DrawUnits(ScheduledBeat beat, VfxFrame vfx)
        {
            IReadOnlyDictionary<string, UnitSnapshot> state = _animation != null ? _animation.Turn.After : _playback.Current;
            List<UnitSnapshot> units = new List<UnitSnapshot>(state.Values);
            units.Sort((a, b) => Center(a).Y.CompareTo(Center(b).Y));

            string actor = _animation == null ? null : _animation.Turn.Turn.Unit.Id;
            Texture2D mask = _atlas.Texture("hex_mask");
            Texture2D outline = _atlas.Texture("hex_outline");

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
                    (int x, int y) = _layout.TileTopLeft(tile);
                    _batch.Draw(mask, new Vector2(x, y), team * 0.35f);
                    if (unit.Id == actor)
                    {
                        _batch.Draw(outline, new Vector2(x, y), _atlas.Palette("Y", Color.Yellow));
                    }
                }
            }

            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Vec2 center = Center(unit);
                int scale = unit.Footprint == UnitFootprint.Single ? 1 : 2;
                Texture2D sprite = _atlas.Texture(SpriteFor(unit.Id));
                int size = 32 * scale;
                Vector2 at = new Vector2((int)Math.Round(center.X) - size / 2, (int)Math.Round(center.Y) - size + 8 * scale);
                SpriteEffects flip = unit.Team == BattleTeam.Enemy ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                bool fading = _animation != null && _animation.ShownFading(unit.Id, _clockMs);
                _batch.Draw(sprite, at, null, fading ? Color.White * 0.4f : Color.White, 0f, Vector2.Zero, scale, flip, 0f);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.Hp;
                DrawHpBar((int)at.X + size / 2, (int)at.Y - 3, scale == 1 ? 24 : 40, hp, unit.MaxHp);
            }
        }

        private void DrawHpBar(int centerX, int y, int width, int hp, int maxHp)
        {
            int x = centerX - width / 2;
            float fraction = maxHp <= 0 ? 0f : Math.Max(0f, Math.Min(1f, hp / (float)maxHp));
            string fill = fraction > 0.5f ? "l" : fraction > 0.25f ? "y" : "o";
            _batch.Draw(_atlas.Pixel, new Rectangle(x - 1, y - 1, width + 2, 4), _atlas.Palette("K", Color.Black));
            _batch.Draw(_atlas.Pixel, new Rectangle(x, y, width, 2), _atlas.Palette("1", Color.DarkGray));
            _batch.Draw(_atlas.Pixel, new Rectangle(x, y, (int)Math.Ceiling(width * fraction), 2), _atlas.Palette(fill, Color.Green));
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

                Vec2 center = Center(unit);
                int scale = unit.Footprint == UnitFootprint.Single ? 1 : 2;
                int size = 32 * scale;
                Vector2 at = new Vector2((int)Math.Round(center.X) - size / 2, (int)Math.Round(center.Y) - size + 8 * scale);
                SpriteEffects flip = unit.Team == BattleTeam.Enemy ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                _batch.Draw(_atlas.Texture(SpriteFor(unit.Id)), at, null, tint, 0f, Vector2.Zero, scale, flip, 0f);
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
                _text.DrawCentered(_batch, text, (int)Math.Round(number.Position.X), (int)Math.Round(number.Position.Y) - lift, color, 2,
                                   _atlas.Palette("K", Color.Black) * number.Alpha);
            }
        }

        private void DrawCentered(string sheet, int frame, Vec2 at, int scale, Color color)
        {
            Texture2D texture = _atlas.Texture(sheet);
            if (texture == null)
            {
                return;
            }

            Rectangle source = _atlas.Frame(sheet, frame);
            Vector2 position = new Vector2((int)Math.Round(at.X) - source.Width * scale / 2, (int)Math.Round(at.Y) - source.Height * scale / 2);
            _batch.Draw(texture, position, source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        private void DrawHud(ScheduledBeat beat)
        {
            Color ink = _atlas.Palette("4", Color.White);
            Color dim = _atlas.Palette("3", Color.Gray);
            Color gold = _atlas.Palette("y", Color.Gold);
            Color shadow = _atlas.Palette("K", Color.Black);

            int turnNumber = _playback.Played.Count;
            _text.Draw(_batch, "BEAST CRAFT  DESKTOP SPIKE", 6, 4, gold, 1, shadow);
            _text.Draw(_batch, "TURN " + turnNumber + "   SEED " + _options.Seed.ToString(CultureInfo.InvariantCulture), 200, 4, ink, 1, shadow);

            // Right-hand panel: the acting unit and its skill, the turn order, the log.
            _batch.Draw(_atlas.Pixel, new Rectangle(PanelX - 6, 0, VirtualWidth - PanelX + 6, VirtualHeight), _atlas.Palette("p", Color.Purple) * 0.55f);
            int y = 16;
            if (_animation != null)
            {
                _text.Draw(_batch, "NOW: " + Name(_animation.Turn.Turn.Unit.Id), PanelX, y, gold, 1, shadow);
                y += 8;
                if (beat != null)
                {
                    _text.Draw(_batch, beat.Beat.SkillName ?? beat.Beat.SkillId, PanelX + 8, y, ink, 1, shadow);
                }

                y += 10;
            }

            _text.Draw(_batch, "TURN ORDER", PanelX, y, dim, 1, shadow);
            y += 8;
            foreach (BattleUnit unit in _playback.Forecast(8))
            {
                Color team = unit.Team == BattleTeam.Player ? _atlas.Palette("C", Color.LightBlue) : _atlas.Palette("u", Color.Pink);
                _batch.Draw(_atlas.Pixel, new Rectangle(PanelX, y + 1, 3, 3), team);
                _text.Draw(_batch, Name(unit.Id), PanelX + 6, y, ink, 1, shadow);
                int shown = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.CurrentHp;
                string hp = shown.ToString(CultureInfo.InvariantCulture) + "/" + unit.Stats.Hp.ToString(CultureInfo.InvariantCulture);
                _text.Draw(_batch, hp, VirtualWidth - 6 - PixelText.Measure(hp), y, dim, 1, shadow);
                y += 8;
            }

            y += 6;
            _text.Draw(_batch, "LOG", PanelX, y, dim, 1, shadow);
            y += 8;
            int first = Math.Max(0, _log.Count - 18);
            for (int i = first; i < _log.Count; i++)
            {
                _text.Draw(_batch, _log[i], PanelX, y, ink, 1, shadow);
                y += 7;
            }

            if (_playback.IsOver && (_animation == null || _clockMs >= _animation.DurationMs))
            {
                string banner = _playback.Outcome == BattleOutcome.PlayerVictory ? "VICTORY" : _playback.Outcome == BattleOutcome.EnemyVictory ? "DEFEAT" : "STALEMATE";
                _batch.Draw(_atlas.Pixel, new Rectangle(0, VirtualHeight / 2 - 18, PanelX - 6, 36), shadow * 0.75f);
                _text.DrawCentered(_batch, banner, BoardAreaWidth / 2, VirtualHeight / 2 - 10, gold, 4);
            }

            string help = "SPACE: STEP   A: AUTO " + (_auto ? "ON" : "OFF") + "   ESC: QUIT";
            _text.Draw(_batch, help, 6, VirtualHeight - 9, dim, 1, shadow);
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

        private string SpriteFor(string unitId)
        {
            string species = _speciesByUnit.TryGetValue(unitId, out string id) ? id : null;
            string[] candidates = { "beast_" + species, "enemy_" + species, species == "brute" ? "enemy_brute_gloamed" : null };
            foreach (string candidate in candidates)
            {
                if (candidate != null && _atlas.Texture(candidate) != null)
                {
                    return candidate;
                }
            }

            return "enemy_brute_gloamed";
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
