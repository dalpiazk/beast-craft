using System;

namespace BeastCraft.Presentation.Audio
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Audio/audio-cues.json</c>: every sound the game can ask
    /// for, by cue id, with its files and gain (docs/design/audio.md). Presentation only. Public
    /// fields, JSON keys are the field names (read with <c>FieldJson</c>); checked by
    /// <see cref="AudioCueLibrary.Validate"/>. Listing a cue whose files do not exist yet is fine: it
    /// plays nothing until they land.
    /// </summary>
    [Serializable]
    public class AudioCueLibraryData
    {
        /// <summary>The file's path relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Audio/audio-cues.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        public AudioCueData[] Cues = new AudioCueData[0];
    }

    /// <summary>One cue: a sound effect (one or more variants), a music track (1-4 stems, base first) or an ambience bed.</summary>
    [Serializable]
    public class AudioCueData
    {
        /// <summary>The stable id the code asks for, e.g. <c>sfx.hit.fire</c>, <c>music.battle.r01</c>, <c>ambient.grove</c>.</summary>
        public string Id;

        /// <summary><c>sfx</c>, <c>music</c> or <c>ambient</c>: which folder of <c>content/audio/</c> the files are in.</summary>
        public string Kind;

        /// <summary>
        /// The file names, in the folder <see cref="Kind"/> names, following the pipeline's naming
        /// contract (<c>sfx_&lt;name&gt;_oneshot.wav</c>, <c>mus_&lt;name&gt;_loop.ogg</c>,
        /// <c>amb_&lt;name&gt;_loop.ogg</c>). A sound effect's files are variants (one plays at random);
        /// a track's are its stems, base layer first, all the same length and sample rate.
        /// </summary>
        public string[] Files = new string[0];

        /// <summary>The cue's level in dB relative to its normalised file (0 = as mastered; -6 about half).</summary>
        public float GainDb;

        /// <summary>The haptic pulse the cue brings, when the player has haptics on: <c>light</c>, <c>medium</c>, <c>selection</c> or empty for none.</summary>
        public string Haptic;
    }
}
