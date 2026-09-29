using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BeastCraft.Presentation.Audio
{
    /// <summary>What a cue is, resolved from <see cref="AudioCueData"/>: its kind, its files' content paths, its linear gain and haptic.</summary>
    public sealed class AudioCue
    {
        public string Id;
        public AudioCueKind Kind;

        /// <summary>The files as content-relative paths (<c>audio/sfx/sfx_ui_tap_oneshot.wav</c>): variants for a sound effect, stems (base first) for a track.</summary>
        public IReadOnlyList<string> Files;

        /// <summary>The gain as a linear factor (<see cref="AudioCueData.GainDb"/>).</summary>
        public float Gain;

        /// <summary>The pulse the cue brings, or null for none.</summary>
        public HapticPulse? Haptic;
    }

    public enum AudioCueKind
    {
        Sfx,
        Music,
        Ambient
    }

    /// <summary>
    /// The cues by id (<see cref="AudioCueLibraryData"/>): <see cref="Resolve"/> gives a cue or null for
    /// an id the file does not list. Built once with the content; <see cref="Validate"/> checks the
    /// file (unique ids, a known kind, the naming contract, at most <see cref="MaxStems"/> stems).
    /// </summary>
    public sealed class AudioCueLibrary
    {
        /// <summary>A track's most stems (layers on one clock).</summary>
        public const int MaxStems = 4;

        /// <summary>The naming contract's file names (Pipeline/README.md, "Audio"; docs/design/audio.md).</summary>
        private static readonly Regex SfxName = new Regex("^sfx_[a-z0-9]+(?:_[a-z0-9]+)*_oneshot\\.wav$");

        private static readonly Regex MusicName = new Regex("^mus_[a-z0-9]+(?:_[a-z0-9]+)*_loop\\.ogg$");
        private static readonly Regex AmbientName = new Regex("^amb_[a-z0-9]+(?:_[a-z0-9]+)*_loop\\.ogg$");
        private static readonly Regex IdPattern = new Regex("^(?:sfx|music|ambient)(?:\\.[a-z0-9_]+)+$");

        private readonly Dictionary<string, AudioCue> _cues = new Dictionary<string, AudioCue>(StringComparer.Ordinal);

        private AudioCueLibrary()
        {
        }

        /// <summary>An empty library: every cue resolves to null.</summary>
        public static AudioCueLibrary Empty { get; } = new AudioCueLibrary();

        public int Count
        {
            get { return _cues.Count; }
        }

        public IEnumerable<string> Ids
        {
            get { return _cues.Keys; }
        }

        /// <summary>The library over <paramref name="data"/> (null: empty). Expects data that passed <see cref="Validate"/>; a bad entry is skipped, a repeated id keeps the first.</summary>
        public static AudioCueLibrary Build(AudioCueLibraryData data)
        {
            AudioCueLibrary library = new AudioCueLibrary();
            foreach (AudioCueData cue in data?.Cues ?? new AudioCueData[0])
            {
                if (cue == null || string.IsNullOrEmpty(cue.Id) || library._cues.ContainsKey(cue.Id) || !TryKind(cue.Kind, out AudioCueKind kind))
                {
                    continue;
                }

                List<string> files = new List<string>();
                foreach (string file in cue.Files ?? new string[0])
                {
                    if (!string.IsNullOrEmpty(file))
                    {
                        files.Add(FolderOf(kind) + file);
                    }
                }

                TryHaptic(cue.Haptic, out HapticPulse? haptic);
                library._cues[cue.Id] = new AudioCue { Id = cue.Id, Kind = kind, Files = files, Gain = GainOf(cue.GainDb), Haptic = haptic };
            }

            return library;
        }

        /// <summary>The cue <paramref name="id"/>, or null when the file does not list it.</summary>
        public AudioCue Resolve(string id)
        {
            return id != null && _cues.TryGetValue(id, out AudioCue cue) ? cue : null;
        }

        /// <summary>The content folder a kind's files are in: <c>audio/sfx/</c>, <c>audio/music/</c>, <c>audio/ambient/</c>.</summary>
        public static string FolderOf(AudioCueKind kind)
        {
            switch (kind)
            {
                case AudioCueKind.Music:
                    return "audio/music/";
                case AudioCueKind.Ambient:
                    return "audio/ambient/";
                default:
                    return "audio/sfx/";
            }
        }

        /// <summary>A gain in dB as a linear factor (0 dB = 1).</summary>
        public static float GainOf(float db)
        {
            return (float)Math.Pow(10.0, db / 20.0);
        }

        /// <summary>The problems in <paramref name="data"/>, empty when it is fine.</summary>
        public static List<string> Validate(AudioCueLibraryData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No audio cues.");
                return errors;
            }

            if (data.SchemaVersion != AudioCueLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion " + data.SchemaVersion + " is not " + AudioCueLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < (data.Cues ?? new AudioCueData[0]).Length; i++)
            {
                AudioCueData cue = data.Cues[i];
                string at = "Cues[" + i + "]";
                if (cue == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(cue.Id) || !IdPattern.IsMatch(cue.Id))
                {
                    errors.Add(at + ": the id '" + cue.Id + "' is not a dotted lowercase cue id starting sfx., music. or ambient.");
                }
                else if (!seen.Add(cue.Id))
                {
                    errors.Add(at + ": the id '" + cue.Id + "' is used twice.");
                }

                if (!TryKind(cue.Kind, out AudioCueKind kind))
                {
                    errors.Add(at + " (" + cue.Id + "): the kind '" + cue.Kind + "' is not sfx, music or ambient.");
                    continue;
                }

                if (cue.Id != null && !cue.Id.StartsWith(kind == AudioCueKind.Sfx ? "sfx." : kind == AudioCueKind.Music ? "music." : "ambient.", StringComparison.Ordinal))
                {
                    errors.Add(at + " (" + cue.Id + "): the id does not start with its kind.");
                }

                string[] files = cue.Files ?? new string[0];
                if (files.Length == 0)
                {
                    errors.Add(at + " (" + cue.Id + "): no files.");
                }

                if (kind != AudioCueKind.Sfx && files.Length > MaxStems)
                {
                    errors.Add(at + " (" + cue.Id + "): " + files.Length + " stems; at most " + MaxStems + ".");
                }

                Regex name = kind == AudioCueKind.Sfx ? SfxName : kind == AudioCueKind.Music ? MusicName : AmbientName;
                foreach (string file in files)
                {
                    if (file == null || !name.IsMatch(file))
                    {
                        errors.Add(at + " (" + cue.Id + "): '" + file + "' does not follow the naming contract for " + cue.Kind + ".");
                    }
                }

                if (cue.GainDb < -40f || cue.GainDb > 12f)
                {
                    errors.Add(at + " (" + cue.Id + "): GainDb " + cue.GainDb + " is outside -40 to +12.");
                }

                if (!TryHaptic(cue.Haptic, out _))
                {
                    errors.Add(at + " (" + cue.Id + "): the haptic '" + cue.Haptic + "' is not light, medium, selection or empty.");
                }
            }

            return errors;
        }

        private static bool TryKind(string kind, out AudioCueKind parsed)
        {
            switch (kind)
            {
                case "sfx":
                    parsed = AudioCueKind.Sfx;
                    return true;
                case "music":
                    parsed = AudioCueKind.Music;
                    return true;
                case "ambient":
                    parsed = AudioCueKind.Ambient;
                    return true;
                default:
                    parsed = AudioCueKind.Sfx;
                    return false;
            }
        }

        private static bool TryHaptic(string haptic, out HapticPulse? pulse)
        {
            switch (haptic)
            {
                case null:
                case "":
                    pulse = null;
                    return true;
                case "light":
                    pulse = HapticPulse.Light;
                    return true;
                case "medium":
                    pulse = HapticPulse.Medium;
                    return true;
                case "selection":
                    pulse = HapticPulse.Selection;
                    return true;
                default:
                    pulse = null;
                    return false;
            }
        }
    }
}
