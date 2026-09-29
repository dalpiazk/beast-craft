using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Ui;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Audio
{
    /// <summary>
    /// What the game asks of sound and touch feedback, in one place: <see cref="Cue"/> plays a sound
    /// effect and its haptic, <see cref="Clicked"/> answers a tap, <see cref="ScreenChanged"/> picks the
    /// music or ambience for the screen showing (<see cref="TrackFor"/>), <see cref="SetIntensity"/>
    /// layers the battle music, and <see cref="Update"/> keeps the volumes on the player's settings
    /// every frame. It holds the engine-neutral seams (<see cref="IAudio"/>, <see cref="IMusicPlayer"/>,
    /// <see cref="IHaptics"/>), so the tests and the scripted screenshots run it on the null ones.
    /// Haptics only pulse while <see cref="PlayerSettings.Haptics"/> is on, at most one every
    /// <see cref="HapticGapMs"/> (a multi-hit burst is one buzz). A cue id the library does not list is
    /// logged once and plays nothing.
    /// </summary>
    public sealed class AudioDirector
    {
        /// <summary>The least time between two haptic pulses.</summary>
        public const float HapticGapMs = 60f;

        public const string UiTap = "sfx.ui.tap";
        public const string UiConfirm = "sfx.ui.confirm";
        public const string Rewards = "sfx.rewards";
        public const string LevelUp = "sfx.level_up";

        private readonly Func<PlayerSettings> _settings;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private float _clockMs;
        private float _lastPulseMs = float.MinValue;

        public AudioDirector(AudioCueLibrary cues, IAudio audio, IMusicPlayer music, IHaptics haptics, Func<PlayerSettings> settings)
        {
            Cues = cues ?? AudioCueLibrary.Empty;
            Audio = audio ?? new NullAudio();
            Music = music ?? new NullMusicPlayer();
            Haptics = haptics ?? new NullHaptics();
            _settings = settings ?? (() => null);
        }

        /// <summary>Everything silent and still (scripted runs, the tests' default).</summary>
        public static AudioDirector Silent(AudioCueLibrary cues = null)
        {
            return new AudioDirector(cues, null, null, null, null);
        }

        public AudioCueLibrary Cues { get; }

        public IAudio Audio { get; }

        public IMusicPlayer Music { get; }

        public IHaptics Haptics { get; }

        /// <summary>Where a missing cue is reported (once per id). Defaults to the console's error stream.</summary>
        public Action<string> Log { get; set; } = message => Console.Error.WriteLine(message);

        /// <summary>Plays <paramref name="cueId"/> and, with haptics on, its pulse.</summary>
        public void Cue(string cueId)
        {
            AudioCue cue = Cues.Resolve(cueId);
            if (cue == null)
            {
                Report(cueId);
                return;
            }

            Audio.Play(cueId);
            if (cue.Haptic.HasValue)
            {
                Pulse(cue.Haptic.Value);
            }
        }

        /// <summary>A pulse, when the player has haptics on and the last one was at least <see cref="HapticGapMs"/> ago; returns whether it pulsed.</summary>
        public bool Pulse(HapticPulse pulse)
        {
            PlayerSettings settings = _settings();
            if (settings != null && !settings.Haptics)
            {
                return false;
            }

            if (_clockMs - _lastPulseMs < HapticGapMs)
            {
                return false;
            }

            _lastPulseMs = _clockMs;
            Haptics.Pulse(pulse);
            return true;
        }

        /// <summary>A tap on <paramref name="widget"/>: a primary button confirms (<see cref="UiConfirm"/>), any other button, tab or hotspot taps (<see cref="UiTap"/>).</summary>
        public void Clicked(Widget widget)
        {
            if (widget == null)
            {
                return;
            }

            Cue(widget is Button button && button.StyleKey == "primary" ? UiConfirm : UiTap);
        }

        /// <summary>Plays the track for <paramref name="screenName"/> in <paramref name="regionId"/> (<see cref="TrackFor"/>), crossfading; a screen with no track of its own keeps what plays.</summary>
        public void ScreenChanged(string screenName, string regionId)
        {
            string track = TrackFor(screenName, regionId);
            if (track == null || track == Music.Current)
            {
                return;
            }

            if (Cues.Resolve(track) == null)
            {
                Report(track);
                Music.Stop(MusicMix.CrossfadeMs);
                return;
            }

            Music.Play(track, MusicMix.CrossfadeMs);
            Music.SetIntensity(0f);
        }

        /// <summary>The battle music's layering, 0-1 (<see cref="BattleIntensity"/>).</summary>
        public void SetIntensity(float intensity)
        {
            Music.SetIntensity(MusicMix.Clamp01(intensity));
        }

        /// <summary>A frame: the volumes follow the settings, the music's fades move on.</summary>
        public void Update(float elapsedMs)
        {
            _clockMs += Math.Max(0f, elapsedMs);
            PlayerSettings settings = _settings();
            Audio.Volume = MusicMix.SfxVolume(settings);
            Music.Volume = MusicMix.MusicVolume(settings);
            Music.Update(elapsedMs);
        }

        /// <summary>
        /// The track a screen plays (docs/design/audio.md, "Cue list"): the title theme on the title and
        /// save slots, the region's battle theme in battle, the Grove's ambience in the Grove, the
        /// region's ambience everywhere else in an expedition; null (keep what plays) for the results
        /// and for a screen outside any region.
        /// </summary>
        public static string TrackFor(string screenName, string regionId)
        {
            switch (screenName)
            {
                case "title":
                case "save-slots":
                    return "music.title";
                case "results":
                    return null;
                case "grove":
                    return "ambient.grove";
                case "battle":
                    return string.IsNullOrEmpty(regionId) ? null : "music.battle." + regionId;
                default:
                    return string.IsNullOrEmpty(regionId) ? null : "ambient." + regionId;
            }
        }

        /// <summary>
        /// How hard the battle music plays: it builds as the enemy side falls, from a quarter (the fight
        /// opens) to full (the last enemy is nearly down), as the share of the enemies' total HP lost.
        /// </summary>
        public static float BattleIntensity(int enemyHpLeft, int enemyHpTotal)
        {
            if (enemyHpTotal <= 0)
            {
                return 0.25f;
            }

            float lost = 1f - MusicMix.Clamp01(enemyHpLeft / (float)enemyHpTotal);
            return 0.25f + 0.75f * lost;
        }

        private void Report(string cueId)
        {
            if (_reported.Add(cueId ?? string.Empty))
            {
                Log?.Invoke("[Audio] No cue '" + cueId + "' in " + AudioCueLibraryData.ProjectRelativePath + "; nothing plays.");
            }
        }
    }
}
