namespace BeastCraft.Localization
{
    /// <summary>
    /// The text Core's rules hand the player (a refusal a toast shows, a location's fallback name), by
    /// <c>ui.rules.*</c> key in <c>content/data/Localization/en.json</c>. The rules are static and take
    /// no content, so the table they read is set once, when the game's content loads
    /// (<c>GameContent.Load</c>); the screens' own text goes through <c>GameContent.Text</c> instead.
    /// Before a table is set, <see cref="Get"/> falls back the way a missing key does
    /// (<see cref="StringTable.Get"/>). Diagnostics the player never reads (validators, logs,
    /// exceptions, guards that only a content or code error can reach) stay in English.
    /// </summary>
    public static class RulesText
    {
        private static readonly StringTable Empty = new StringTable(StringTable.SourceLocale, null);

        /// <summary>The table the rules read (null until the content loads).</summary>
        public static StringTable Table { get; set; }

        /// <summary>The text for <paramref name="key"/> (see <see cref="StringTable.Get"/>).</summary>
        public static string Get(string key)
        {
            return (Table ?? Empty).Get(key);
        }

        /// <summary><see cref="Get"/> with <c>{0}</c>-style arguments (see <see cref="StringTable.Format"/>).</summary>
        public static string Format(string key, params object[] args)
        {
            return (Table ?? Empty).Format(key, args);
        }
    }
}
