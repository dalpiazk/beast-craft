using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Presentation.Audio;
using BeastCraft.Presentation.Content;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace BeastCraft.Game.Audio
{
    /// <summary>
    /// <see cref="IMusicPlayer"/> on MonoGame: a track's OGG Vorbis stems (<c>audio/music/</c>,
    /// <c>audio/ambient/</c>) decode with NVorbis and stream through one
    /// <see cref="DynamicSoundEffectInstance"/>, mixed on the game thread in <see cref="Update"/>. Every
    /// stem is read the same number of frames per buffer, so the stems stay sample-aligned on one clock
    /// (each loops back to its start at its end: they must be the same length). Each stem's weight
    /// follows the intensity (<see cref="MusicMix.LayerWeights"/>), faded over
    /// <see cref="MusicMix.LayerFadeMs"/>; a new track crossfades with the old, which is dropped once
    /// silent. A missing or unreadable stem is logged once and left out; a track with no playable stem
    /// is silence. Any failure of the audio device switches the player off (logged once).
    /// </summary>
    public sealed class MonoGameMusicPlayer : IMusicPlayer, IDisposable
    {
        /// <summary>Frames per submitted buffer (about 93 ms at 44.1 kHz).</summary>
        public const int FramesPerBuffer = 4096;

        /// <summary>How many buffers stay queued ahead of the device.</summary>
        public const int BuffersAhead = 3;

        private readonly AudioCueLibrary _cues;
        private readonly IContentSource _source;
        private readonly Action<string> _log;
        private readonly List<Voice> _voices = new List<Voice>();
        private readonly HashSet<string> _unavailable = new HashSet<string>(StringComparer.Ordinal);
        private float _intensity;
        private bool _off;

        public MonoGameMusicPlayer(AudioCueLibrary cues, IContentSource source, Action<string> log = null)
        {
            _cues = cues ?? AudioCueLibrary.Empty;
            _source = source;
            _log = log ?? (message => Console.Error.WriteLine(message));
        }

        public float Volume { get; set; } = 1f;

        public string Current { get; private set; }

        public void Play(string trackId, int crossfadeMs)
        {
            if (trackId == Current)
            {
                return;
            }

            FadeOutAll(crossfadeMs);
            Current = trackId;
            AudioCue cue = _cues.Resolve(trackId);
            if (_off || cue == null)
            {
                return;
            }

            try
            {
                Voice voice = Open(cue, crossfadeMs);
                if (voice != null)
                {
                    _voices.Add(voice);
                }
            }
            catch (Exception e)
            {
                SwitchOff(e);
            }
        }

        public void Stop(int fadeMs)
        {
            FadeOutAll(fadeMs);
            Current = null;
        }

        public void SetIntensity(float intensity)
        {
            _intensity = MusicMix.Clamp01(intensity);
            foreach (Voice voice in _voices)
            {
                voice.TargetWeights = MusicMix.LayerWeights(_intensity, voice.Stems.Count);
            }
        }

        public void Update(float elapsedMs)
        {
            if (_off)
            {
                return;
            }

            try
            {
                for (int i = _voices.Count - 1; i >= 0; i--)
                {
                    Voice voice = _voices[i];
                    voice.Fade = MusicMix.Step(voice.Fade, voice.FadeTarget, elapsedMs, voice.FadeMs);
                    for (int s = 0; s < voice.Weights.Length; s++)
                    {
                        voice.Weights[s] = MusicMix.Step(voice.Weights[s], voice.TargetWeights[s], elapsedMs, MusicMix.LayerFadeMs);
                    }

                    if (voice.FadeTarget <= 0f && voice.Fade <= 0f)
                    {
                        voice.Dispose();
                        _voices.RemoveAt(i);
                        continue;
                    }

                    voice.Instance.Volume = Math.Max(0f, Math.Min(1f, Volume * voice.Gain * voice.Fade));
                    while (voice.Instance.PendingBufferCount < BuffersAhead)
                    {
                        Feed(voice);
                    }

                    if (voice.Instance.State != SoundState.Playing)
                    {
                        voice.Instance.Play();
                    }
                }
            }
            catch (Exception e)
            {
                SwitchOff(e);
            }
        }

        private Voice Open(AudioCue cue, int fadeMs)
        {
            List<VorbisReader> stems = new List<VorbisReader>();
            int rate = 0;
            int channels = 0;
            foreach (string file in cue.Files)
            {
                VorbisReader reader = OpenStem(file);
                if (reader == null)
                {
                    continue;
                }

                if (stems.Count == 0)
                {
                    rate = reader.SampleRate;
                    channels = reader.Channels;
                }
                else if (reader.SampleRate != rate || reader.Channels != channels)
                {
                    Unavailable(file, "not the base stem's format (" + reader.SampleRate + " Hz, " + reader.Channels + " ch; the base is " + rate + " Hz, " + channels + " ch)");
                    reader.Dispose();
                    continue;
                }

                stems.Add(reader);
            }

            if (stems.Count == 0 || (channels != 1 && channels != 2))
            {
                foreach (VorbisReader reader in stems)
                {
                    reader.Dispose();
                }

                return null;
            }

            Voice voice = new Voice
            {
                Stems = stems,
                Gain = cue.Gain,
                Instance = new DynamicSoundEffectInstance(rate, channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo),
                Fade = 0f,
                FadeTarget = 1f,
                FadeMs = Math.Max(1, fadeMs),
                Weights = MusicMix.LayerWeights(_intensity, stems.Count),
                TargetWeights = MusicMix.LayerWeights(_intensity, stems.Count),
                Mix = new float[FramesPerBuffer * channels],
                Read = new float[FramesPerBuffer * channels],
                Pcm = new byte[FramesPerBuffer * channels * 2]
            };
            voice.Instance.Volume = 0f;
            return voice;
        }

        private VorbisReader OpenStem(string file)
        {
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
                Stream stream = _source.Open(file);
                if (!stream.CanSeek)
                {
                    // Looping seeks back to the start; an asset stream cannot, so keep the file in memory.
                    MemoryStream copy = new MemoryStream();
                    using (stream)
                    {
                        stream.CopyTo(copy);
                    }

                    copy.Position = 0;
                    stream = copy;
                }

                return new VorbisReader(stream, true);
            }
            catch (Exception e)
            {
                Unavailable(file, "unreadable (" + e.Message + ")");
                return null;
            }
        }

        /// <summary>Mixes the next buffer of every stem at its weight and queues it.</summary>
        private static void Feed(Voice voice)
        {
            Array.Clear(voice.Mix, 0, voice.Mix.Length);
            for (int s = 0; s < voice.Stems.Count; s++)
            {
                VorbisReader stem = voice.Stems[s];
                int filled = 0;
                while (filled < voice.Read.Length)
                {
                    int read = stem.ReadSamples(voice.Read, filled, voice.Read.Length - filled);
                    if (read <= 0)
                    {
                        // The loop point: back to the start, on the same frame for every stem of equal length.
                        stem.SeekTo(0);
                        read = stem.ReadSamples(voice.Read, filled, voice.Read.Length - filled);
                        if (read <= 0)
                        {
                            Array.Clear(voice.Read, filled, voice.Read.Length - filled);
                            break;
                        }
                    }

                    filled += read;
                }

                float weight = voice.Weights[s];
                if (weight <= 0f)
                {
                    continue;
                }

                for (int i = 0; i < voice.Mix.Length; i++)
                {
                    voice.Mix[i] += voice.Read[i] * weight;
                }
            }

            for (int i = 0; i < voice.Mix.Length; i++)
            {
                short sample = MusicMix.ToPcm16(voice.Mix[i]);
                voice.Pcm[2 * i] = (byte)(sample & 0xFF);
                voice.Pcm[2 * i + 1] = (byte)((sample >> 8) & 0xFF);
            }

            voice.Instance.SubmitBuffer(voice.Pcm);
        }

        private void FadeOutAll(int fadeMs)
        {
            foreach (Voice voice in _voices)
            {
                voice.FadeTarget = 0f;
                voice.FadeMs = Math.Max(1, fadeMs);
            }
        }

        private void Unavailable(string file, string why)
        {
            if (_unavailable.Add(file))
            {
                _log("[Audio] " + file + " is " + why + "; that stem plays nothing.");
            }
        }

        private void SwitchOff(Exception e)
        {
            _off = true;
            _log("[Audio] Music is off: " + e.Message);
            Dispose();
        }

        public void Dispose()
        {
            foreach (Voice voice in _voices)
            {
                voice.Dispose();
            }

            _voices.Clear();
        }

        /// <summary>One playing track: its stems, their weights, its fade and its stream.</summary>
        private sealed class Voice : IDisposable
        {
            public List<VorbisReader> Stems;
            public float Gain;
            public DynamicSoundEffectInstance Instance;
            public float Fade;
            public float FadeTarget;
            public float FadeMs;
            public float[] Weights;
            public float[] TargetWeights;
            public float[] Mix;
            public float[] Read;
            public byte[] Pcm;

            public void Dispose()
            {
                Instance?.Stop();
                Instance?.Dispose();
                foreach (VorbisReader stem in Stems)
                {
                    stem.Dispose();
                }
            }
        }
    }
}
