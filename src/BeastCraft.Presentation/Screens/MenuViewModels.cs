using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// The title screen: Continue (only when a save exists), New Game (asking first when it would
    /// replace a save) and Settings; Back asks before quitting.
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
            get { return _session.HasSave; }
        }

        /// <summary>Whether New Game should ask first (it would replace the save in the slot).</summary>
        public bool NewGameNeedsConfirm
        {
            get { return _session.HasSave; }
        }

        public LoadOutcome Continue()
        {
            return _session.Continue();
        }

        public void NewGame()
        {
            _session.NewGame();
        }
    }

    /// <summary>The bottom-nav tabs, left to right (producer decision): Map is home.</summary>
    public enum HomeTab
    {
        Map = 0,
        Roster = 1,
        Camp = 2,
        Avatar = 3,
        Inventory = 4
    }

    /// <summary>
    /// The home screen's bottom nav: Map, Roster, Camp, Avatar, Inventory. Only Map works in this
    /// build; the others show a "coming soon" page. Back on another tab returns to the Map; on the
    /// Map it is not handled here (the stack pops back to the title).
    /// </summary>
    public sealed class HomeViewModel
    {
        public static readonly string[] TabNames = { "Map", "Roster", "Camp", "Avatar", "Inventory" };

        /// <summary>What each coming-soon tab will hold (shown on its placeholder page).</summary>
        public static readonly string[] ComingSoonText =
        {
            null,
            "Browse your beasts, their stats, skills and gear.",
            "Your team base: organise the party, set skills and gear, and claim idle rewards.",
            "Your Beastbinder: level, skills, gear and looks.",
            "Materials, consumables and spare gear."
        };

        public HomeTab Tab { get; private set; } = HomeTab.Map;

        /// <summary>Whether the current tab is built (only the Map, for now).</summary>
        public bool TabAvailable
        {
            get { return Tab == HomeTab.Map; }
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
        public const int RowCount = 4;

        private readonly PlayerSettings _settings;
        private readonly Func<bool> _save;

        public SettingsViewModel(PlayerSettings settings, Func<bool> save)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _save = save;
        }

        public int Saves { get; private set; }

        public List<SettingRow> Rows()
        {
            string intensity = _settings.EffectsIntensity == EffectsIntensity.Minimal ? "Minimal" : _settings.EffectsIntensity == EffectsIntensity.Reduced ? "Reduced" : "Full";
            return new List<SettingRow>
            {
                new SettingRow { Label = "Effects", Value = intensity, On = true },
                new SettingRow { Label = "Screen shake", Value = _settings.ScreenShake ? "On" : "Off", On = _settings.ScreenShake },
                new SettingRow { Label = "Flashes", Value = _settings.Flashes ? "On" : "Off", On = _settings.Flashes },
                new SettingRow { Label = "Team suggestions", Value = _settings.TeamSuggestionsEnabled ? "On" : "Off", On = _settings.TeamSuggestionsEnabled }
            };
        }

        /// <summary>Changes row <paramref name="row"/>'s setting (effects cycle Full, Reduced, Minimal; the rest toggle) and saves.</summary>
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
                default:
                    return;
            }

            if (_save == null || _save())
            {
                Saves++;
            }
        }
    }
}
