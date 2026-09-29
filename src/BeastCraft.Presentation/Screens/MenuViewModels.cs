using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// The title screen: Continue (only when a save exists: the most recently played slot), Save
    /// slots (the slot list, when any slot holds a save), New Game (in the first empty slot; when all
    /// three hold a game the slot list asks which to replace) and Settings; Back asks before quitting.
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

        /// <summary>Whether New Game must replace a save (every slot holds one): the slot list asks which.</summary>
        public bool NewGameNeedsConfirm
        {
            get { return _session.FirstEmptySlot() == null; }
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
        public static readonly string[] TabNames = { "Map", "Roster", "Grove", "Avatar", "Inventory" };

        /// <summary>What each coming-soon tab will hold (shown on its placeholder page). Grove's is unused — see the class remarks.</summary>
        public static readonly string[] ComingSoonText =
        {
            null,
            null,
            "Your team base: organise the party and claim idle rewards; your beasts' habitat, a garden and expeditions.",
            null,
            "Materials, consumables and spare gear."
        };

        public HomeTab Tab { get; private set; } = HomeTab.Map;

        /// <summary>Whether the current tab is built (the Map, the Roster, Grove and the Avatar identity card).</summary>
        public bool TabAvailable
        {
            get { return Tab == HomeTab.Map || Tab == HomeTab.Roster || Tab == HomeTab.Grove || Tab == HomeTab.Avatar; }
        }

        public string TabName
        {
            get { return TabNames[(int)Tab]; }
        }

        public string ComingSoon
        {
            get { return ComingSoonText[(int)Tab]; }
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

    /// <summary>One row of the settings modal.</summary>
    public sealed class SettingRow
    {
        public string Label;
        public string Value;

        /// <summary>Whether the value reads as "on" (drawn green) rather than "off".</summary>
        public bool On;
    }

    /// <summary>
    /// The settings modal (the battle's effects settings, plus the team-suggestion toggle): each
    /// row cycles or toggles its setting, which is saved at once.
    /// </summary>
    public sealed class SettingsViewModel
    {
        public const int Effects = 0;
        public const int ScreenShake = 1;
        public const int Flashes = 2;
        public const int TeamSuggestions = 3;
        public const int BattleSpeed = 4;
        public const int AutoAdvance = 5;
        public const int TutorialHints = 6;
        public const int IdleNotifications = 7;
        public const int RowCount = 8;

        private readonly PlayerSettings _settings;
        private readonly Func<bool> _save;

        /// <param name="notificationsAvailable">Whether the host can post notifications (Android): otherwise that row is hidden.</param>
        public SettingsViewModel(PlayerSettings settings, Func<bool> save, bool notificationsAvailable = false)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _save = save;
            NotificationsAvailable = notificationsAvailable;
        }

        public int Saves { get; private set; }

        public bool NotificationsAvailable { get; }

        /// <summary>Raised after the idle-notification setting changes (the host asks for the permission, or cancels).</summary>
        public event Action<bool> IdleNotificationsChanged;

        /// <summary>The rows, by the row constants (the notification row only where the host has notifications).</summary>
        public List<SettingRow> Rows()
        {
            string intensity = _settings.EffectsIntensity == EffectsIntensity.Minimal ? "Minimal" : _settings.EffectsIntensity == EffectsIntensity.Reduced ? "Reduced" : "Full";
            List<SettingRow> rows = new List<SettingRow>
            {
                new SettingRow { Label = "Effects", Value = intensity, On = true },
                new SettingRow { Label = "Screen shake", Value = _settings.ScreenShake ? "On" : "Off", On = _settings.ScreenShake },
                new SettingRow { Label = "Flashes", Value = _settings.Flashes ? "On" : "Off", On = _settings.Flashes },
                new SettingRow { Label = "Team suggestions", Value = _settings.TeamSuggestionsEnabled ? "On" : "Off", On = _settings.TeamSuggestionsEnabled },
                new SettingRow { Label = "Battle speed", Value = "x" + Speed(_settings), On = true },
                new SettingRow { Label = "Auto next battle", Value = _settings.AutoAdvance ? "On" : "Off", On = _settings.AutoAdvance },
                new SettingRow { Label = "Tutorial hints", Value = _settings.TutorialHints ? "On" : "Off", On = _settings.TutorialHints }
            };
            if (NotificationsAvailable)
            {
                rows.Add(new SettingRow { Label = "Idle full alert", Value = _settings.IdleNotifications ? "On" : "Off", On = _settings.IdleNotifications });
            }

            return rows;
        }

        /// <summary>The battle speed a setting holds, 1-3 (anything else reads as 1).</summary>
        public static int Speed(PlayerSettings settings)
        {
            int speed = settings == null ? 1 : settings.BattleSpeed;
            return speed >= 1 && speed <= 3 ? speed : 1;
        }

        /// <summary>Changes row <paramref name="row"/>'s setting (effects cycle Full, Reduced, Minimal; speed x1, x2, x3; the rest toggle) and saves.</summary>
        public void Change(int row)
        {
            switch (row)
            {
                case Effects:
                    _settings.EffectsIntensity = _settings.EffectsIntensity == EffectsIntensity.Full
                                                     ? EffectsIntensity.Reduced
                                                     : _settings.EffectsIntensity == EffectsIntensity.Reduced ? EffectsIntensity.Minimal : EffectsIntensity.Full;
                    break;
                case ScreenShake:
                    _settings.ScreenShake = !_settings.ScreenShake;
                    break;
                case Flashes:
                    _settings.Flashes = !_settings.Flashes;
                    break;
                case TeamSuggestions:
                    _settings.TeamSuggestionsEnabled = !_settings.TeamSuggestionsEnabled;
                    break;
                case BattleSpeed:
                    _settings.BattleSpeed = Speed(_settings) % 3 + 1;
                    break;
                case AutoAdvance:
                    _settings.AutoAdvance = !_settings.AutoAdvance;
                    break;
                case TutorialHints:
                    _settings.TutorialHints = !_settings.TutorialHints;
                    break;
                case IdleNotifications:
                    if (!NotificationsAvailable)
                    {
                        return;
                    }

                    _settings.IdleNotifications = !_settings.IdleNotifications;
                    break;
                default:
                    return;
            }

            if (_save == null || _save())
            {
                Saves++;
            }

            if (row == IdleNotifications)
            {
                IdleNotificationsChanged?.Invoke(_settings.IdleNotifications);
            }
        }
    }
}
