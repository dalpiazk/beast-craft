using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Presentation.Audio;
using BeastCraft.Presentation.Content;
using Microsoft.Xna.Framework.Audio;

namespace BeastCraft.Game.Audio
{
    /// <summary>
    /// <see cref="IAudio"/> on MonoGame: each cue's WAV files load once through
    /// <see cref="SoundEffect.FromStream"/> from the content (<c>audio/sfx/</c>), and play on a small
    /// pool of instances per file (<see cref="VoicesPerFile"/>; the oldest is cut when all are busy),
    /// at the cue's gain times the effects volume. A file that is missing or will not load is logged
    /// once and plays nothing from then on; so is a device with no audio output (the first failure
    /// switches the player off), so a desktop without sound files or sound hardware runs silent.
    /// </summary>
    public sealed class MonoGameAudio : IAudio, IDisposable
    {
        /// <summary>At most this many copies of one file sound at once.</summary>
        public const int VoicesPerFile = 4;

        private readonly AudioCueLibrary _cues;
        private readonly IContentSource _source;
        private readonly Action<string> _log;
        private readonly Dictionary<string, SoundEffect> _effects = new Dictionary<string, SoundEffect>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SoundEffectInstance>> _voices = new Dictionary<string, List<SoundEffectInstance>>(StringComparer.Ordinal);
        private readonly HashSet<string> _unavailable = new HashSet<string>(StringComparer.Ordinal);
        private readonly Random _random = new Random();
        private bool _off;

        public MonoGameAudio(AudioCueLibrary cues, IContentSource source, Action<string> log = null)
        {
            _cues = cues ?? AudioCueLibrary.Empty;
            _source = source;
            _log = log ?? (message => Console.Error.WriteLine(message));
        }

        public float Volume { get; set; } = 1f;

        public void Play(string cueId)
        {
            AudioCue cue = _cues.Resolve(cueId);
            if (_off || cue == null || cue.Files.Count == 0 || Volume <= 0f)
            {
                return;
            }

            string file = cue.Files[cue.Files.Count == 1 ? 0 : _random.Next(cue.Files.Count)];
            try
            {
                SoundEffect effect = Load(file);
                if (effect == null)
                {
                    return;
                }

                SoundEffectInstance voice = Voice(file, effect);
                voice.Volume = Math.Max(0f, Math.Min(1f, Volume * cue.Gain));
                voice.Play();
            }
            catch (Exception e)
            {
                // No audio device, or the platform refused: stay silent from here on.
                _off = true;
                _log("[Audio] Sound effects are off: " + e.Message);
            }
        }

        private SoundEffect Load(string file)
        {
            if (_effects.TryGetValue(file, out SoundEffect loaded))
            {
                return loaded;
            }

            if (_unavailable.Contains(file))
            {
                return null;
            }

            if (_source == null || !_source.Exists(file))
            {
                Unavailable(file, "missing");
                return null;
            }

            try
            {
                using (Stream stream = _source.Open(file))
                using (MemoryStream copy = new MemoryStream())
                {
                    // TitleContainer streams (Android assets) do not seek; FromStream reads a RIFF header, so hand it a copy.
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    SoundEffect effect = SoundEffect.FromStream(copy);
                    _effects[file] = effect;
                    return effect;
                }
            }
            catch (NoAudioHardwareException)
            {
                throw;
            }
            catch (Exception e)
            {
                Unavailable(file, "unreadable (" + e.Message + ")");
                return null;
            }
        }

        private SoundEffectInstance Voice(string file, SoundEffect effect)
        {
            if (!_voices.TryGetValue(file, out List<SoundEffectInstance> pool))
            {
                pool = new List<SoundEffectInstance>();
                _voices[file] = pool;
            }

            foreach (SoundEffectInstance idle in pool)
            {
                if (idle.State == SoundState.Stopped)
                {
                    return idle;
                }
            }

            if (pool.Count < VoicesPerFile)
            {
                SoundEffectInstance created = effect.CreateInstance();
                pool.Add(created);
                return created;
            }

            // All busy: cut the oldest and reuse it at the back of the queue.
            SoundEffectInstance oldest = pool[0];
            pool.RemoveAt(0);
            pool.Add(oldest);
            oldest.Stop();
            return oldest;
        }

        private void Unavailable(string file, string why)
        {
            if (_unavailable.Add(file))
            {
                _log("[Audio] " + file + " is " + why + "; that sound plays nothing.");
            }
        }

        public void Dispose()
        {
            foreach (List<SoundEffectInstance> pool in _voices.Values)
            {
                foreach (SoundEffectInstance voice in pool)
                {
                    voice.Dispose();
                }
            }

            foreach (SoundEffect effect in _effects.Values)
            {
                effect.Dispose();
            }

            _voices.Clear();
            _effects.Clear();
        }
    }
}
