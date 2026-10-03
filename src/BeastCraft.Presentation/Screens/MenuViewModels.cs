using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Localization;
using BeastCraft.Presentation.Audio;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// The title screen: Continue (only when a save exists: the most recently played slot), Load game
    /// (the slot list, when any slot holds a save), New Game and Settings; Back asks before quitting.
    /// New Game always opens the slot list too (menu-screens pass #67: `Game.Screens.TitleScreen.NewGame`
    /// no longer asks this view-model first) — picking an empty slot there, or a used one, which asks to
    /// replace it (`SaveSlotsViewModel.NewGameReplaces`), is the one way a game starts.
    /// </summary>
    public sealed class TitleViewModel
    {
        private readonly GameSession _session;

        public TitleViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public bool CanContinue
        {
            get { return _session.AnySave; }
        }

        /// <summary>Whether the slot list is offered (some slot holds a save).</summary>
        public bool CanManageSlots
        {
            get { return _session.AnySave; }
        }

        /// <summary>Continues the most recently played slot.</summary>
        public LoadOutcome Continue()
        {
            string slot = _session.MostRecentSlot();
            if (slot != null)
            {
                _session.UseSlot(slot);
            }

            return _session.Continue();
        }

        /// <summary>Moves to the first empty slot for a new game. False when every slot holds a save (the current slot stays).</summary>
        public bool PrepareNewGame()
        {
            string slot = _session.FirstEmptySlot();
            return slot != null && _session.UseSlot(slot);
        }

        /// <summary>A new game in Hearthglen with <paramref name="firstSpeciesId"/> as the New Game pick.</summary>
        public bool NewGame(string firstSpeciesId)
        {
            return _session.NewGame(firstSpeciesId);
        }

        /// <summary>A new game that skips Hearthglen, with the three picks made in a row.</summary>
        public bool NewGameSkippingTutorial(IReadOnlyList<string> species)
        {
            return _session.NewGameSkippingTutorial(species);
        }
    }

    /// <summary>
    /// The bottom-nav tabs, left to right (producer decisions): Map is home; the team base is the
    /// Grove (the map's Camp locations keep their name).
    /// </summary>
    public enum HomeTab
    {
        Map = 0,
        Roster = 1,
        Grove = 2,
        Avatar = 3,
        Inventory = 4
    }

    /// <summary>
    /// The home screen's bottom nav: Map, Roster, Grove, Avatar, Inventory. Map and Roster show inline;
    /// Grove, Avatar and Inventory each open their own full screen (<c>Game.Screens.GroveScreen</c>,
    /// <c>AvatarScreen</c>, <c>InventoryScreen</c>) rather than showing inline, so this view-model's
    /// own <see cref="Tab"/> never actually becomes <see cref="HomeTab.Grove"/>,
    /// <see cref="HomeTab.Avatar"/> or <see cref="HomeTab.Inventory"/> (see
    /// <c>HomeScreen.SelectTab</c>) — the nav bar keeps showing whichever of Map/Roster was selected
    /// underneath. Back on another tab returns to the Map; on the Map it is not handled here (the
    /// stack pops back to the title).
    /// </summary>
    public sealed class HomeViewModel
    {
        /// <summary>The tabs' ids, in <see cref="HomeTab"/> order (also the <c>--screen</c> names); their labels are <see cref="TabKeys"/>.</summary>
        public static readonly string[] TabNames = { "Map", "Roster", "Grove", "Avatar", "Inventory" };

        /// <summary>The tabs' labels as text keys (<c>ui.home.tab_*</c>), in <see cref="HomeTab"/> order.</summary>
        public static readonly string[] TabKeys = { "ui.home.tab_map", "ui.home.tab_roster", "ui.home.tab_grove", "ui.home.tab_avatar", "ui.home.tab_inventory" };

        /// <summary>What each coming-soon tab will hold (shown on its placeholder page), as text keys. Grove's is unused — see the class remarks.</summary>
        public static readonly string[] ComingSoonKeys =
        {
            null,
            null,
            "ui.home.coming_soon_grove",
            null,
            "ui.home.coming_soon_inventory"
        };

        public HomeTab Tab { get; private set; } = HomeTab.Map;

        /// <summary>Whether the current tab is built (the Map, the Roster, Grove and the Avatar identity card).</summary>
        public bool TabAvailable
        {
            get { return Tab == HomeTab.Map || Tab == HomeTab.Roster || Tab == HomeTab.Grove || Tab == HomeTab.Avatar; }
        }

        /// <summary>The current tab's label key (<see cref="TabKeys"/>).</summary>
        public string TabNameKey
        {
            get { return TabKeys[(int)Tab]; }
        }

        /// <summary>The current tab's placeholder text key (<see cref="ComingSoonKeys"/>; null for none).</summary>
        public string ComingSoonKey
        {
            get { return ComingSoonKeys[(int)Tab]; }
        }

        public void Select(HomeTab tab)
        {
            if (Enum.IsDefined(typeof(HomeTab), tab))
            {
                Tab = tab;
            }
        }

        /// <summary>Back: from another tab to the Map (true); on the Map, not handled (false).</summary>
        public bool HandleBack()
        {
            if (Tab == HomeTab.Map)
            {
                return false;
            }

            Tab = HomeTab.Map;
            return true;
        }
    }

    /// <summary>
    /// The settings screen (menu-screens pass, #67): a full screen, grouped into four sections —
    /// Gameplay, Visuals &amp; accessibility, Audio, Privacy — each a plain set of fields the screen
    /// binds directly to its widgets (toggles, sliders, a chip row for the two cycling choices,
    /// Effects intensity and Battle speed), replacing the single long cycling list of rows a
    /// <c>SettingsModal</c> used to show. Every setter saves at once (<see cref="Saves"/> counts how
    /// many times). The alert settings (idle, Grove) and haptics are only meaningful where the host
    /// has them (<see cref="NotificationsAvailable"/>, <see cref="HapticsAvailable"/>); their setters
    /// do nothing where the host does not, so a screen built without checking first is still safe.
    /// </summary>
    public sealed class SettingsViewModel
    {
        private readonly PlayerSettings _settings;
        private readonly Func<bool> _save;
        private readonly StringTable _text;

        /// <param name="settings">The settings this changes.</param>
        /// <param name="text">The text table the labels come from (<c>ui.settings.*</c>; <c>GameContent.Text</c>).</param>
        /// <param name="save">Saves the settings after each change.</param>
        /// <param name="notificationsAvailable">Whether the host can post notifications (Android): otherwise the idle/Grove alert setters do nothing.</param>
        /// <param name="hapticsAvailable">Whether the host can vibrate (Android): otherwise the haptics setter does nothing.</param>
        public SettingsViewModel(PlayerSettings settings, StringTable text, Func<bool> save, bool notificationsAvailable = false, bool hapticsAvailable = false)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _save = save;
            NotificationsAvailable = notificationsAvailable;
            HapticsAvailable = hapticsAvailable;
        }

        /// <summary>How many changes have been saved so far (tests only; the screen does not read this).</summary>
        public int Saves { get; private set; }

        public bool NotificationsAvailable { get; }

        public bool HapticsAvailable { get; }

        /// <summary>Raised after the idle-notification setting changes (the host asks for the permission, or cancels).</summary>
        public event Action<bool> IdleNotificationsChanged;

        /// <summary>Raised after the Grove-notification setting changes (the host asks for the permission, or cancels).</summary>
        public event Action<bool> GroveNotificationsChanged;

        /// <summary>Raised after a consent setting (analytics, crash reports) changes: the session's telemetry starts or stops to match.</summary>
        public event Action ConsentChanged;

        // ------------------------------------------------------------------------------------------
        // Gameplay
        // ------------------------------------------------------------------------------------------

        /// <summary>The battle speed, 1-3 (<see cref="Speed"/>).</summary>
        public int BattleSpeed
        {
            get { return Speed(_settings); }
        }

        /// <summary>Sets the battle speed directly (clamped to 1-3). Saves.</summary>
        public void SetBattleSpeed(int speed)
        {
            _settings.BattleSpeed = speed < 1 ? 1 : speed > 3 ? 3 : speed;
            Persist();
        }

        public bool AutoAdvance
        {
            get { return _settings.AutoAdvance; }
        }

        public void SetAutoAdvance(bool on)
        {
            _settings.AutoAdvance = on;
            Persist();
        }

        public bool TeamSuggestionsEnabled
        {
            get { return _settings.TeamSuggestionsEnabled; }
        }

        public void SetTeamSuggestions(bool on)
        {
            _settings.TeamSuggestionsEnabled = on;
            Persist();
        }

        public bool TutorialHints
        {
            get { return _settings.TutorialHints; }
        }

        public void SetTutorialHints(bool on)
        {
            _settings.TutorialHints = on;
            Persist();
        }

        /// <summary>A local notification when idle rewards reach their cap. Only where <see cref="NotificationsAvailable"/>.</summary>
        public bool IdleNotifications
        {
            get { return _settings.IdleNotifications; }
        }

        /// <summary>Does nothing where <see cref="NotificationsAvailable"/> is false.</summary>
        public void SetIdleNotifications(bool on)
        {
            if (!NotificationsAvailable)
            {
                return;
            }

            _settings.IdleNotifications = on;
            Persist();
            IdleNotificationsChanged?.Invoke(on);
        }

        /// <summary>A local notification when something in the Grove is ready. Only where <see cref="NotificationsAvailable"/>.</summary>
        public bool GroveNotifications
        {
            get { return _settings.GroveNotifications; }
        }

        /// <summary>Does nothing where <see cref="NotificationsAvailable"/> is false.</summary>
        public void SetGroveNotifications(bool on)
        {
            if (!NotificationsAvailable)
            {
                return;
            }

            _settings.GroveNotifications = on;
            Persist();
            GroveNotificationsChanged?.Invoke(on);
        }

        // ------------------------------------------------------------------------------------------
        // Visuals & accessibility
        // ------------------------------------------------------------------------------------------

        public EffectsIntensity EffectsIntensity
        {
            get { return _settings.EffectsIntensity; }
        }

        /// <summary>The current intensity's label (<c>ui.settings.effects_*</c>), for the chip row.</summary>
        public string EffectsIntensityLabel
        {
            get
            {
                return _text.Get(EffectsIntensity == EffectsIntensity.Minimal ? "ui.settings.effects_minimal"
                                      : EffectsIntensity == EffectsIntensity.Reduced ? "ui.settings.effects_reduced" : "ui.settings.effects_full");
            }
        }

        public void SetEffectsIntensity(EffectsIntensity intensity)
        {
            _settings.EffectsIntensity = intensity;
            Persist();
        }

        public bool ScreenShake
        {
            get { return _settings.ScreenShake; }
        }

        public void SetScreenShake(bool on)
        {
            _settings.ScreenShake = on;
            Persist();
        }

        public bool Flashes
        {
            get { return _settings.Flashes; }
        }

        public void SetFlashes(bool on)
        {
            _settings.Flashes = on;
            Persist();
        }

        // ------------------------------------------------------------------------------------------
        // Audio
        // ------------------------------------------------------------------------------------------

        public int MasterVolume
        {
            get { return Clamp(_settings.MasterVolume); }
        }

        /// <summary>Sets the master volume (0-100, clamped) directly — a slider's exact value, no stepping. Saves.</summary>
        public void SetMasterVolume(int percent)
        {
            _settings.MasterVolume = Clamp(percent);
            Persist();
        }

        /// <summary>
        /// The master volume's live value during a drag, applied at once (so the player hears it move)
        /// but never saved — code review: writing to disk on every drag frame is needless I/O; the
        /// screen calls <see cref="SetMasterVolume"/> once the drag ends (or on a plain tap) to persist
        /// the exact value the drag settled on.
        /// </summary>
        public void SetMasterVolumeLive(int percent)
        {
            _settings.MasterVolume = Clamp(percent);
        }

        public int MusicVolume
        {
            get { return Clamp(_settings.MusicVolume); }
        }

        public void SetMusicVolume(int percent)
        {
            _settings.MusicVolume = Clamp(percent);
            Persist();
        }

        /// <summary>The music volume's live value during a drag, never saved — see <see cref="SetMasterVolumeLive"/>.</summary>
        public void SetMusicVolumeLive(int percent)
        {
            _settings.MusicVolume = Clamp(percent);
        }

        public int SfxVolume
        {
            get { return Clamp(_settings.SfxVolume); }
        }

        public void SetSfxVolume(int percent)
        {
            _settings.SfxVolume = Clamp(percent);
            Persist();
        }

        /// <summary>The sound effects volume's live value during a drag, never saved — see <see cref="SetMasterVolumeLive"/>.</summary>
        public void SetSfxVolumeLive(int percent)
        {
            _settings.SfxVolume = Clamp(percent);
        }

        /// <summary>A volume's slider position, 0-1 (<see cref="MusicMix.Percent"/>).</summary>
        public static float Fraction(int percent)
        {
            return MusicMix.Percent(percent);
        }

        public bool Muted
        {
            get { return _settings.Muted; }
        }

        public void SetMuted(bool on)
        {
            _settings.Muted = on;
            Persist();
        }

        /// <summary>Light vibration on hits, knockouts and key confirms. Only where <see cref="HapticsAvailable"/>.</summary>
        public bool Haptics
        {
            get { return _settings.Haptics; }
        }

        /// <summary>Does nothing where <see cref="HapticsAvailable"/> is false.</summary>
        public void SetHaptics(bool on)
        {
            if (!HapticsAvailable)
            {
                return;
            }

            _settings.Haptics = on;
            Persist();
        }

        // ------------------------------------------------------------------------------------------
        // Privacy (#62)
        // ------------------------------------------------------------------------------------------

        public bool AnalyticsConsent
        {
            get { return _settings.AnalyticsConsent; }
        }

        public void SetAnalyticsConsent(bool on)
        {
            _settings.AnalyticsConsent = on;
            Persist();
            ConsentChanged?.Invoke();
        }

        public bool CrashReportConsent
        {
            get { return _settings.CrashReportConsent; }
        }

        public void SetCrashReportConsent(bool on)
        {
            _settings.CrashReportConsent = on;
            Persist();
            ConsentChanged?.Invoke();
        }

        // ------------------------------------------------------------------------------------------

        private void Persist()
        {
            if (_save == null || _save())
            {
                Saves++;
            }
        }

        private static int Clamp(int percent)
        {
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

        /// <summary>The battle speed a setting holds, 1-3 (anything else reads as 1).</summary>
        public static int Speed(PlayerSettings settings)
        {
            int speed = settings == null ? 1 : settings.BattleSpeed;
            return speed >= 1 && speed <= 3 ? speed : 1;
        }
    }

    /// <summary>
    /// The one-time consent screen (#62): shown once, after the first starter pick and before play, until the
    /// player answers it. Both choices start off; the player turns on what they agree to and continues, and
    /// can change either later in the settings. Nothing is set up or sent before that (<see cref="GameSession.Telemetry"/>).
    /// </summary>
    public sealed class ConsentViewModel
    {
        private readonly GameSession _session;

        public ConsentViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Analytics = session.Settings.AnalyticsConsent;
            CrashReports = session.Settings.CrashReportConsent;
        }

        /// <summary>Whether the screen still has to be shown (never answered).</summary>
        public static bool ShouldAsk(PlayerSettings settings)
        {
            return settings != null && !settings.ConsentAsked;
        }

        /// <summary>The analytics choice on the screen (off until the player turns it on).</summary>
        public bool Analytics { get; set; }

        /// <summary>The crash-report choice on the screen (off until the player turns it on).</summary>
        public bool CrashReports { get; set; }

        /// <summary>Stores both choices, marks the screen answered, saves the settings and starts only what was agreed to.</summary>
        public void Confirm()
        {
            PlayerSettings settings = _session.Settings;
            settings.AnalyticsConsent = Analytics;
            settings.CrashReportConsent = CrashReports;
            settings.ConsentAsked = true;
            _session.SaveSettings();
            _session.Telemetry.Apply();
        }
    }

    /// <summary>One row of the backdrop picker: a region's menu backdrop, reached (selectable) or still locked.</summary>
    public sealed class BackdropRow
    {
        public string RegionId;
        public string DisplayName;
        public bool Reached;
        public bool Selected;
    }

    /// <summary>
    /// Settings &gt; Visuals' backdrop picker (#52 step 3, UI kit): the menu backdrop drawn behind every
    /// out-of-combat screen (<c>Game.Screens.GameScreen.PageBackground</c>), one per region —
    /// Hearthglen (<see cref="CampaignProgress.TutorialRegionId"/>) first, then every
    /// <c>regions.json</c> region in campaign order — reached or still locked
    /// (<see cref="CampaignProgress.ReachedBackdropIds"/>, unlocked the first time the player enters
    /// that region, <c>CampaignRules.StartRun</c>) and which one is currently selected
    /// (<see cref="CampaignProgress.SelectedBackdropId"/>). Selecting persists through the normal save
    /// flow (<see cref="GameSession.Autosave"/>).
    /// </summary>
    public sealed class BackdropPickerViewModel
    {
        private readonly GameSession _session;

        public BackdropPickerViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<BackdropRow> Rows { get; } = new List<BackdropRow>();

        /// <summary>Re-reads the save and the region library (a new region was just reached, or a pick was made elsewhere).</summary>
        public void Refresh()
        {
            Rows.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            RegionLibrary library = _session.Content.Campaign;
            CampaignProgress campaign = save?.Campaign;
            List<RegionData> regions = new List<RegionData>();
            if (library?.Tutorial != null)
            {
                regions.Add(library.Tutorial);
            }

            if (library != null)
            {
                regions.AddRange(library.Regions);
            }

            foreach (RegionData region in regions)
            {
                if (region == null || string.IsNullOrEmpty(region.RegionId))
                {
                    continue;
                }

                bool reached = campaign != null && campaign.HasReachedBackdrop(region.RegionId);
                Rows.Add(new BackdropRow
                {
                    RegionId = region.RegionId,
                    DisplayName = region.DisplayName,
                    Reached = reached,
                    Selected = reached && campaign.SelectedBackdropId == region.RegionId
                });
            }
        }

        /// <summary>Selects <paramref name="regionId"/>'s backdrop. False (nothing changes) when it has not been reached. Autosaves and refreshes on success.</summary>
        public bool Select(string regionId)
        {
            PlayerSave save = _session.Save;
            if (save == null || !save.Campaign.SelectBackdrop(regionId))
            {
                return false;
            }

            _session.Autosave(AutosaveReason.PlayerEdit);
            Refresh();
            return true;
        }
    }
}
