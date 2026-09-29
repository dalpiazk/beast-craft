using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeastCraft.Localization
{
    /// <summary>
    /// One locale's player-facing text by stable key: <c>content/data/Localization/en.json</c> is the
    /// single source of English (English is the only locale; languages are decided after launch). The
    /// content data files hold keys in their player-facing fields (<see cref="ContentTextRules"/>),
    /// resolved through this table when a file is loaded (<see cref="ContentText"/>).
    /// <para>
    /// File shape: <c>{ "_readme": "...", "Locale": "en", "Strings": { "creature.phoenix.name": "Phoenix", ... } }</c>,
    /// the keys sorted (the <c>Tooling/ContentKeys</c> tool writes it that way).
    /// </para>
    /// <para>
    /// A key the table lacks is logged (<see cref="MissingKeyLog"/>, once per key) and shown as
    /// <c>[missing: key]</c> in a Debug build, so it is easy to spot on screen; a Release build shows
    /// the key itself (<see cref="VisibleMissingKeys"/>). The content tests fail on any missing key
    /// before it could ship.
    /// </para>
    /// </summary>
    public sealed class StringTable
    {
        /// <summary>The English table, the source of truth.</summary>
        public const string SourceProjectRelativePath = "content/data/Localization/en.json";

        /// <summary>The folder every locale's table lives in, as <c>&lt;locale&gt;.json</c>.</summary>
        public const string FolderProjectRelativePath = "content/data/Localization";

        /// <summary>The source locale.</summary>
        public const string SourceLocale = "en";

        private readonly Dictionary<string, string> _strings;
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);

        public StringTable(string locale, IDictionary<string, string> strings)
        {
            Locale = string.IsNullOrEmpty(locale) ? SourceLocale : locale;
            _strings = new Dictionary<string, string>(strings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether a missing key shows as <c>[missing: key]</c> (Debug builds, the default there) or as
        /// the key itself (Release builds). Settable for tests.
        /// </summary>
        public static bool VisibleMissingKeys { get; set; } =
#if DEBUG
            true;
#else
            false;
#endif

        /// <summary>Where a missing key is reported (once per key and table). Defaults to the console's error stream.</summary>
        public static Action<string> MissingKeyLog { get; set; } = message => Console.Error.WriteLine(message);

        /// <summary>The locale code, e.g. <c>en</c>.</summary>
        public string Locale { get; }

        public int Count
        {
            get { return _strings.Count; }
        }

        /// <summary>Every key, for the content checks.</summary>
        public IEnumerable<string> Keys
        {
            get { return _strings.Keys; }
        }

        /// <summary>Reads a table file's text. Throws a <see cref="JsonException"/> (or <see cref="FormatException"/>) when it is malformed.</summary>
        public static StringTable Parse(string json)
        {
            JsonObject root = JsonNode.Parse(json ?? string.Empty) as JsonObject ?? throw new FormatException("The string table is not a JSON object.");
            JsonObject strings = root["Strings"] as JsonObject ?? throw new FormatException("The string table has no \"Strings\" object.");
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in strings)
            {
                if (!(pair.Value is JsonValue value) || !value.TryGetValue(out string text))
                {
                    throw new FormatException("The string \"" + pair.Key + "\" is not text.");
                }

                map[pair.Key] = text;
            }

            string locale = root["Locale"] is JsonValue code && code.TryGetValue(out string parsed) ? parsed : SourceLocale;
            return new StringTable(locale, map);
        }

        public bool Contains(string key)
        {
            return key != null && _strings.ContainsKey(key);
        }

        public bool TryGet(string key, out string text)
        {
            if (key != null && _strings.TryGetValue(key, out text))
            {
                return true;
            }

            text = null;
            return false;
        }

        /// <summary>
        /// The text for <paramref name="key"/>; for a key the table lacks, the missing-key fallback (see
        /// the class remarks). Null or empty gives "".
        /// </summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_strings.TryGetValue(key, out string text))
            {
                return text;
            }

            lock (_reportedMissing)
            {
                if (_reportedMissing.Add(key))
                {
                    MissingKeyLog?.Invoke("[Localization] Missing string key '" + key + "' in the " + Locale + " table.");
                }
            }

            return VisibleMissingKeys ? "[missing: " + key + "]" : key;
        }

        /// <summary><see cref="Get(string)"/> with <c>{0}</c>-style arguments (invariant culture).</summary>
        public string Format(string key, params object[] args)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key), args);
        }
    }
}
