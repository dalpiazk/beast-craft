using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Text;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Save;
using BeastCraft.Session;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The battle screen (formerly the whole app, <c>BattleViewerGame</c>): one real PvE battle
    /// stepped a turn at a time through <see cref="BattlePlayback"/> and drawn in portrait on the
    /// fixed 1080x1920 logical canvas (<see cref="PortraitLayout"/>), which the host scales and
    /// letterboxes into the window or screen inside its safe area. The screen, top to bottom:
    /// header, turn-order portraits (the acting unit highlighted), the board fitted to the arena, a
    /// one-line log toast, the acting unit's skills, and the playback controls (pause/play,
    /// x1/x2/x3, skip).
    /// <para>
    /// Two modes. <b>Demo</b> (<see cref="Demo"/>): the command line's battle
    /// (<see cref="DemoBattle"/>), exactly the old viewer — the <c>--screenshot</c> flags, and Back
    /// (Esc) quits. <b>Campaign</b> (<see cref="Campaign"/>): a map location's battle begun by
    /// <see cref="NodeBattle"/>, playing by itself; once it is decided a Continue button (or Back)
    /// hands it back for the results, and Back before that offers to skip to the result.
    /// </para>
    /// <para>
    /// Desktop: Space steps (or finishes the turn playing), A toggles auto-play, 1-3 set the speed,
    /// S skips to the end, Tab cycles the selected skill; the mouse clicks the buttons and hovers or
    /// clicks the skills. Touch: tap a button or a skill to open its detail card (tap a highlighted
    /// word for its definition; tap off the card to close it), tap the gear for the effects
    /// settings, tap the board to step, two fingers toggle auto-play.
    /// </para>
    /// <para>
    /// The screen only reads the battle: turns are the session's own
    /// (<see cref="BattleSession.Begin"/>), and every animation is a pure function of the recorded
    /// results (<see cref="TurnAnimation"/>, <see cref="Presentation.Vfx.VfxTimeline"/>).
    /// </para>
    /// </summary>
    public sealed partial class BattleScreen : GameScreen
    {
        private const int AutoPauseMs = 260;
        private const int ControlPause = 0;
        private const int ControlSkip = 4;

        private readonly ViewerOptions _options;
        private readonly PortraitLayout _screen = new PortraitLayout();
        private readonly HexLayout _layout = new HexLayout(0, 0);
        private readonly SpriteRenderer _draw;
        private readonly SpriteAtlas _atlas;
        private readonly ITextRenderer _text;
        private readonly GameContent _content;
        private readonly BattlePlayback _playback;
        private readonly Dictionary<string, string> _speciesByUnit;
        private readonly string _hudTitle;
        private readonly int _seed;
        private readonly NodeBattle _campaign;
        private readonly Action<NodeBattle> _finished;

        /// <summary>The battle's layout (its obstacles and the backdrop they are painted on), or null on the open board.</summary>
        private readonly Encounters.BattleLayoutEntryData _battleLayout;

        /// <summary>
        /// The region the battle is fought in (<c>--region</c>, else the encounter's, else r01, or the
        /// map location's): its enemy art, and its battlefield layouts (the obstacles, and the backdrop drawn).
        /// </summary>
        private readonly string _regionId;

        /// <summary>The region the units are drawn in (its enemy art): the battlefield, or on an open board the region it stands in for.</summary>
        private readonly string _artRegionId;

        private readonly Dictionary<string, string> _names;
        private BoardFit _boardFit;
        private CanvasFit _canvasFit;
        private readonly CameraRig _camera;
        private TurnCamera _turnCamera;
        private CameraView _cameraRest;
        private int _cameraIdleMs;
        private PlayerSettings _settings = new PlayerSettings();
        private Func<bool> _saveSettings;
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
        private int _gestureTouches;
        private Vector2 _gestureEnd;
        private readonly List<string> _log = new List<string>();

        private BattleScreen(ScreenContext ctx, BattleSessionRun run, Dictionary<string, string> speciesByUnit, string regionId, int seed, string hudTitle,
                             NodeBattle campaign, Action<NodeBattle> finished, string artRegionId = null) : base(ctx)
        {
            _artRegionId = artRegionId ?? regionId;
            _options = ctx.Options;
            _draw = ctx.Draw;
            _atlas = ctx.Atlas;
            _text = ctx.Text;
            _content = ctx.Content;
            _speciesByUnit = speciesByUnit;
            _regionId = regionId;
            _seed = seed;
            _hudTitle = hudTitle;
            _campaign = campaign;
            _finished = finished;
            _speed = Math.Max(1, Math.Min(3, _options.Speed));
            _selectedSkill = campaign == null ? _options.SelectSkill : -1;

            _playback = new BattlePlayback(run);
            _battleLayout = run.Result.Layout;
            if (_battleLayout != null)
            {
                Console.WriteLine("Battlefield: " + _battleLayout.ArtKey + " (" + _battleLayout.Cells.Length + " obstacles).");
            }

            _camera = new CameraRig(_playback.Grid.Width, _playback.Grid.Height, _screen.Board);
            _cameraRest = _camera.FitAll;
            _boardFit = _camera.Fit(_cameraRest);
            _names = UnitNames(_content, _speciesByUnit);
        }

        public override string Name
        {
            get { return "battle"; }
        }

        /// <summary>The battle input comes raw (keys, mouse, touch gestures) rather than through the widget toolkit.</summary>
        public override bool UsesRawInput
        {
            get { return true; }
        }

        /// <summary>A campaign battle (a map location's), rather than the command line's demo.</summary>
        public bool IsCampaign
        {
            get { return _campaign != null; }
        }

        /// <summary>Whether the battle is decided and its last turn has finished playing.</summary>
        public bool IsDone
        {
            get { return _playback.IsOver && (_animation == null || _clockMs >= _animation.DurationMs); }
        }

        public BattlePlayback Playback
        {
            get { return _playback; }
        }

        /// <summary>
        /// The command line's battle (<see cref="DemoBattle"/> with the <see cref="ViewerOptions"/>),
        /// or null with <paramref name="error"/> set when it cannot begin.
        /// </summary>
        public static BattleScreen Demo(ScreenContext ctx, PlayerSettings settings, Func<bool> saveSettings, out string error)
        {
            ViewerOptions options = ctx.Options;
            GameContent content = ctx.Content;
            string regionId = options.Region ?? (options.Lineup == null ? DemoBattle.RegionOf(content, options.Encounter) : null) ?? DemoBattle.DefaultRegionId;
            if (!DemoBattle.IsRegion(content, regionId))
            {
                error = "Unknown region '" + regionId + "'.";
                return null;
            }

            BattleSetup setup = DemoBattle.Create(content, options.Seed, out Dictionary<string, string> speciesByUnit, out error, options.Team, options.Encounter,
                                                  options.Level, options.EnemyLevel, options.Arena, options.Lineup, regionId);
            BattleSessionRun run = setup == null ? null : BattleSession.Begin(setup);
            if (run == null || run.Battle == null)
            {
                error = "Could not begin the battle: " + (error ?? run.Result.Error);
                return null;
            }

            BattleScreen screen = new BattleScreen(ctx, run, speciesByUnit, regionId, options.Seed, ctx.Host.HudTitle, null, null);
            screen.LoadSettings(settings, saveSettings);
            if (options.Screenshot)
            {
                screen.PrepareScreenshot();
            }

            return screen;
        }

        /// <summary>
        /// A map location's battle, begun by <paramref name="battle"/>: it plays by itself; once it
        /// is decided, <paramref name="finished"/> is called (Continue, or Back) with it.
        /// </summary>
        public static BattleScreen Campaign(ScreenContext ctx, NodeBattle battle, string title, Action<NodeBattle> finished)
        {
            BattleScreen screen = new BattleScreen(ctx, battle.Run, battle.SpeciesByUnit, battle.RegionId, battle.Seed, title, battle, finished, battle.ArtRegionId);
            screen.LoadSettings(ctx.Session.Settings, ctx.Session.SaveSettings);
            screen._auto = true;
            screen._speed = SettingsViewModel.Speed(ctx.Session.Settings);
            return screen;
        }

        public override bool HandleBack()
        {
            if (_campaign == null)
            {
                // The demo: Back (Esc) quits, as it always has.
                return false;
            }

            if (IsDone)
            {
                HandBack();
                return true;
            }

            Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Skip to the result?", "The battle plays out in an instant; the outcome is the same.", "Keep watching", "Skip",
                                                 SkipToEnd));
            return true;
        }

        public override void Update(float elapsedMs, FrameInput input)
        {
            if (input != null)
            {
                KeyboardState keys = input.Keys;
                bool step = Pressed(keys, Keys.Space);
                if (Pressed(keys, Keys.A))
                {
                    _auto = !_auto;
                }

                for (int speed = 1; speed <= 3; speed++)
                {
                    if (Pressed(keys, Keys.D0 + speed) || Pressed(keys, Keys.NumPad0 + speed))
                    {
                        SetSpeed(speed);
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
                    _popupTerm = null;
                }

                if (_campaign != null && IsDone && Pressed(keys, Keys.Enter))
                {
                    HandBack();
                }

                _previousKeys = keys;
                if (Ctx.Host.Touch)
                {
                    ReadTouch(input, ref step);
                }
                else
                {
                    ReadMouse(input, ref step);
                }

                Advance((int)elapsedMs, step);
            }
            else
            {
                Advance((int)elapsedMs, false);
            }
        }

        private void Advance(int elapsed, bool step)
        {
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
            _animation = new TurnAnimation(turn, _layout, _content.Vfx, _seed, _vfxSettings, _playback.Grid);
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
        public void SkipToEnd()
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

        /// <summary>A decided campaign battle goes back for its results (once).</summary>
        public void HandBack()
        {
            // The battle's one-shot hand-back (NodeBattle.TryHandBack), claimed before the callback
            // runs, so a second Back, tap or Enter, or a re-entrant call from the callback, is a no-op.
            if (_campaign == null || !_campaign.TryHandBack())
            {
                return;
            }

            SkipToEnd();
            _finished?.Invoke(_campaign);
        }

        /// <summary>
        /// Plays turns silently up to the <paramref name="turns"/>th and shows it mid-animation (a
        /// scripted screenshot of a campaign battle); the battle carries on from there.
        /// </summary>
        public void ShowTurn(int turns)
        {
            PlayedTurn shown = null;
            for (int i = 0; i < turns; i++)
            {
                PlayedTurn turn = _playback.Advance();
                if (turn == null)
                {
                    break;
                }

                shown = turn;
                Log(turn);
            }

            if (shown == null)
            {
                return;
            }

            _auto = false;
            _animation = new TurnAnimation(shown, _layout, _content.Vfx, _seed, _vfxSettings, _playback.Grid);
            _turnCamera = new TurnCamera(_animation, _layout, _camera, _camera.FitAll);
            _clockMs = _animation.MidVfxMs(0);
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
                    Ctx.Game.Fail("No turn fired skill '" + _options.Skill + "' before the battle ended.");
                    return;
                }
            }

            if (shown == null)
            {
                return;
            }

            _animation = new TurnAnimation(shown, _layout, _content.Vfx, _seed, _vfxSettings, _playback.Grid);
            _turnCamera = new TurnCamera(_animation, _layout, _camera, _camera.FitAll);
            _clockMs = _options.AtMs ?? _animation.MidVfxMs(beatIndex);
            if (!string.IsNullOrEmpty(_options.Glossary))
            {
                _popupTerm = _content.Glossary.Find(_options.Glossary);
                if (_popupTerm == null)
                {
                    Ctx.Game.Fail("No glossary term '" + _options.Glossary + "'.");
                    return;
                }
            }

            Console.WriteLine("Screenshot: turn " + (shown.Index + 1) + " (" + UnitName(shown.Turn.Unit.Id) + "), " + _clockMs + " ms into its " +
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

        // ------------------------------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------------------------------

        private bool Pressed(KeyboardState keys, Keys key)
        {
            return keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
        }

        /// <summary>Desktop mouse: hovering a skill shows its diagram; a click is a tap.</summary>
        private void ReadMouse(FrameInput input, ref bool step)
        {
            MouseState mouse = input.Mouse;
            Vec2 at = _canvasFit.Scale > 0f ? _canvasFit.ToCanvas(mouse.X, mouse.Y) : new Vec2(-1f, -1f);
            _hoveredSkill = input.IsActive && !_settingsOpen ? SkillCardAt(at) : -1;
            if (input.IsActive && mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed)
            {
                Tap(at, ref step);
            }

            _previousMouse = mouse;
        }

        /// <summary>
        /// Touch as gestures, judged when the last finger lifts: two or more fingers down at once
        /// toggle auto-play; one finger is a tap where it lifted.
        /// </summary>
        private void ReadTouch(FrameInput input, ref bool step)
        {
            int down = 0;
            foreach (TouchLocation touch in input.Touches)
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
        /// campaign battle's Continue once it is decided, a control, a skill card, else the board
        /// (step).
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

            if (_campaign != null && IsDone && ContinueButton.Contains(at.X, at.Y))
            {
                HandBack();
                return;
            }

            // The pinned skill card: any tap (on the popup or anywhere else) closes an open definition; else a tap on a term opens its.
            IReadOnlyList<SkillSO> skills = ActingSkills();
            bool pinned = _selectedSkill >= 0 && _selectedSkill < skills.Count;
            if (pinned && _popupTerm != null)
            {
                _popupTerm = null;
                return;
            }

            if (pinned && _screen.SkillDetail.Contains(at.X, at.Y))
            {
                _popupTerm = CardLayout(SkillCard.Of(skills[_selectedSkill], _content.Glossary)).TermAt(at.X, at.Y);
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
                    SetSpeed(i);
                }

                return;
            }

            int card = SkillCardAt(at);
            if (card >= 0)
            {
                _selectedSkill = card == _selectedSkill ? -1 : card;
                _popupTerm = null;
                return;
            }

            if (_screen.Board.Contains(at.X, at.Y) || _screen.Toast.Contains(at.X, at.Y))
            {
                // Off the card: close it; with no card open, step.
                if (pinned)
                {
                    _selectedSkill = -1;
                    return;
                }

                step = true;
            }
        }

        /// <summary>The playback speed (1-3); in a campaign battle it is also the player's saved preference (<see cref="PlayerSettings.BattleSpeed"/>).</summary>
        private void SetSpeed(int speed)
        {
            _speed = speed;
            if (_campaign != null && _settings.BattleSpeed != speed)
            {
                _settings.BattleSpeed = speed;
                _saveSettings?.Invoke();
            }
        }

        /// <summary>A decided campaign battle's Continue button, under the result banner.</summary>
        private Rect ContinueButton
        {
            get
            {
                Rect board = _screen.Board;
                return new Rect(board.Center.X - 230f, board.Center.Y + 80f, 460f, 120f);
            }
        }

        /// <summary>
        /// The effects settings: <paramref name="settings"/> (the saved ones in a window, the defaults
        /// in a screenshot), then the command-line flags on top.
        /// </summary>
        private void LoadSettings(PlayerSettings settings, Func<bool> save)
        {
            _settings = settings ?? new PlayerSettings();
            _saveSettings = save;
            _options.ApplyTo(_settings);
            _vfxSettings = VfxSettings.From(_settings);
            _settingsOpen = _campaign == null && _options.ShowSettings;
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
            _saveSettings?.Invoke();
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

        private string UnitName(string unitId)
        {
            if (_playback.Avatar != null && unitId == _playback.Avatar.Id)
            {
                return CampaignAvatar.DisplayName;
            }

            return unitId != null && _names.TryGetValue(unitId, out string name) ? name : unitId;
        }

        private void Log(PlayedTurn turn)
        {
            string actor = UnitName(turn.Turn.Unit.Id);
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
