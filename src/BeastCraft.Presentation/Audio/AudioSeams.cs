namespace BeastCraft.Presentation.Audio
{
    /// <summary>
    /// Plays sound effects by cue id (<see cref="AudioCueLibrary"/>, <c>content/data/Audio/audio-cues.json</c>).
    /// Engine-neutral: the MonoGame hosts implement it (<c>BeastCraft.Game.Audio</c>); everything else
    /// and the tests use <see cref="NullAudio"/>. A cue whose files are missing plays nothing (the
    /// implementation logs it once): no audio assets exist yet.
    /// </summary>
    public interface IAudio
    {
        /// <summary>The sound effects' volume, 0-1 (the settings' master times effects volume, 0 when muted).</summary>
        float Volume { get; set; }

        /// <summary>Plays <paramref name="cueId"/> once (a variant at random when it has several).</summary>
        void Play(string cueId);
    }

    /// <summary>
    /// Plays one looping track at a time, music or ambience, with a crossfade between tracks. A track is
    /// 1-4 stems on one clock (sample-aligned); <see cref="SetIntensity"/> fades the upper stems in and
    /// out (<see cref="MusicMix.LayerWeights"/>). Engine-neutral, like <see cref="IAudio"/>.
    /// </summary>
    public interface IMusicPlayer
    {
        /// <summary>The music volume, 0-1 (the settings' master times music volume, 0 when muted).</summary>
        float Volume { get; set; }

        /// <summary>The track playing (or fading in), null for none.</summary>
        string Current { get; }

        /// <summary>Starts <paramref name="trackId"/>, crossfading from the current track over <paramref name="crossfadeMs"/>; the same track keeps playing.</summary>
        void Play(string trackId, int crossfadeMs);

        /// <summary>Fades the current track out over <paramref name="fadeMs"/>.</summary>
        void Stop(int fadeMs);

        /// <summary>How much of the track's layers play, 0 (the base stem only) to 1 (every stem); faded, not cut.</summary>
        void SetIntensity(float intensity);

        /// <summary>Advances the fades and keeps the stream fed (the game loop, once a frame).</summary>
        void Update(float elapsedMs);
    }

    /// <summary>A haptic pulse's strength (docs/design/audio.md, "Haptics").</summary>
    public enum HapticPulse
    {
        /// <summary>A light tap: hits and knockouts (Android EFFECT_CLICK).</summary>
        Light = 0,

        /// <summary>A firmer tap (Android EFFECT_HEAVY_CLICK); unused by default, there for later.</summary>
        Medium = 1,

        /// <summary>The lightest tick: key UI confirms (Android EFFECT_TICK).</summary>
        Selection = 2
    }

    /// <summary>Vibrates the device. Engine-neutral: the Android host implements it; the desktop and the tests use <see cref="NullHaptics"/>.</summary>
    public interface IHaptics
    {
        void Pulse(HapticPulse pulse);
    }

    /// <summary>No sound: the tests, the scripted screenshots and any host without audio.</summary>
    public sealed class NullAudio : IAudio
    {
        public float Volume { get; set; } = 1f;

        public void Play(string cueId)
        {
        }
    }

    /// <summary>No music; remembers the track it was asked for, so the choice can be checked.</summary>
    public sealed class NullMusicPlayer : IMusicPlayer
    {
        public float Volume { get; set; } = 1f;

        public string Current { get; private set; }

        /// <summary>The last intensity asked for.</summary>
        public float Intensity { get; private set; }

        public void Play(string trackId, int crossfadeMs)
        {
            Current = trackId;
        }

        public void Stop(int fadeMs)
        {
            Current = null;
        }

        public void SetIntensity(float intensity)
        {
            Intensity = intensity;
        }

        public void Update(float elapsedMs)
        {
        }
    }

    /// <summary>No haptics: the desktop and any device without a vibrator.</summary>
    public sealed class NullHaptics : IHaptics
    {
        public void Pulse(HapticPulse pulse)
        {
        }
    }
}
