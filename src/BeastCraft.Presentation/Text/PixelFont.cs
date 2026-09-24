using System.Collections.Generic;

namespace BeastCraft.Presentation.Text
{
    /// <summary>
    /// A built-in 3x5 pixel font (upper-case letters, digits and a little punctuation), defined in
    /// code so the spike needs no font asset and no content pipeline. A host turns
    /// <see cref="Glyph"/> into a texture once and draws text as sprites. Lower case draws as upper
    /// case; an unknown character draws as <c>?</c>.
    /// </summary>
    public static class PixelFont
    {
        /// <summary>Glyph width in pixels.</summary>
        public const int GlyphWidth = 3;

        /// <summary>Glyph height in pixels.</summary>
        public const int GlyphHeight = 5;

        /// <summary>Horizontal advance per character (glyph plus one pixel of spacing).</summary>
        public const int Advance = GlyphWidth + 1;

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            { ' ', new[] { "...", "...", "...", "...", "..." } },
            { '0', new[] { "###", "#.#", "#.#", "#.#", "###" } },
            { '1', new[] { ".#.", "##.", ".#.", ".#.", "###" } },
            { '2', new[] { "##.", "..#", ".#.", "#..", "###" } },
            { '3', new[] { "##.", "..#", ".#.", "..#", "##." } },
            { '4', new[] { "#.#", "#.#", "###", "..#", "..#" } },
            { '5', new[] { "###", "#..", "##.", "..#", "##." } },
            { '6', new[] { ".##", "#..", "###", "#.#", "###" } },
            { '7', new[] { "###", "..#", ".#.", ".#.", ".#." } },
            { '8', new[] { "###", "#.#", "###", "#.#", "###" } },
            { '9', new[] { "###", "#.#", "###", "..#", "##." } },
            { 'A', new[] { ".#.", "#.#", "###", "#.#", "#.#" } },
            { 'B', new[] { "##.", "#.#", "##.", "#.#", "##." } },
            { 'C', new[] { ".##", "#..", "#..", "#..", ".##" } },
            { 'D', new[] { "##.", "#.#", "#.#", "#.#", "##." } },
            { 'E', new[] { "###", "#..", "##.", "#..", "###" } },
            { 'F', new[] { "###", "#..", "##.", "#..", "#.." } },
            { 'G', new[] { ".##", "#..", "#.#", "#.#", ".##" } },
            { 'H', new[] { "#.#", "#.#", "###", "#.#", "#.#" } },
            { 'I', new[] { "###", ".#.", ".#.", ".#.", "###" } },
            { 'J', new[] { "..#", "..#", "..#", "#.#", ".#." } },
            { 'K', new[] { "#.#", "#.#", "##.", "#.#", "#.#" } },
            { 'L', new[] { "#..", "#..", "#..", "#..", "###" } },
            { 'M', new[] { "#.#", "###", "###", "#.#", "#.#" } },
            { 'N', new[] { "##.", "#.#", "#.#", "#.#", "#.#" } },
            { 'O', new[] { ".#.", "#.#", "#.#", "#.#", ".#." } },
            { 'P', new[] { "##.", "#.#", "##.", "#..", "#.." } },
            { 'Q', new[] { ".#.", "#.#", "#.#", "##.", ".##" } },
            { 'R', new[] { "##.", "#.#", "##.", "#.#", "#.#" } },
            { 'S', new[] { ".##", "#..", ".#.", "..#", "##." } },
            { 'T', new[] { "###", ".#.", ".#.", ".#.", ".#." } },
            { 'U', new[] { "#.#", "#.#", "#.#", "#.#", "###" } },
            { 'V', new[] { "#.#", "#.#", "#.#", "#.#", ".#." } },
            { 'W', new[] { "#.#", "#.#", "###", "###", "#.#" } },
            { 'X', new[] { "#.#", "#.#", ".#.", "#.#", "#.#" } },
            { 'Y', new[] { "#.#", "#.#", ".#.", ".#.", ".#." } },
            { 'Z', new[] { "###", "..#", ".#.", "#..", "###" } },
            { '.', new[] { "...", "...", "...", "...", ".#." } },
            { ',', new[] { "...", "...", "...", ".#.", "#.." } },
            { ':', new[] { "...", ".#.", "...", ".#.", "..." } },
            { '-', new[] { "...", "...", "###", "...", "..." } },
            { '+', new[] { "...", ".#.", "###", ".#.", "..." } },
            { '/', new[] { "..#", "..#", ".#.", "#..", "#.." } },
            { '!', new[] { ".#.", ".#.", ".#.", "...", ".#." } },
            { '?', new[] { "##.", "..#", ".#.", "...", ".#." } },
            { '(', new[] { ".#.", "#..", "#..", "#..", ".#." } },
            { ')', new[] { ".#.", "..#", "..#", "..#", ".#." } },
            { '>', new[] { "#..", ".#.", "..#", ".#.", "#.." } },
            { '<', new[] { "..#", ".#.", "#..", ".#.", "..#" } },
            { '%', new[] { "#.#", "..#", ".#.", "#..", "#.#" } },
            { '#', new[] { "#.#", "###", "#.#", "###", "#.#" } },
            { '\'', new[] { ".#.", ".#.", "...", "...", "..." } },
            { '_', new[] { "...", "...", "...", "...", "###" } },
            { '=', new[] { "...", "###", "...", "###", "..." } },
        };

        /// <summary>Every character the font draws, in a fixed order (a host packs them into one atlas row in this order).</summary>
        public static readonly string Characters = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ.,:-+/!?()><%#'_=";

        /// <summary>The glyph's rows, top to bottom ('#' = ink).</summary>
        public static string[] Glyph(char c)
        {
            char upper = char.ToUpperInvariant(c);
            return Glyphs.TryGetValue(upper, out string[] rows) ? rows : Glyphs['?'];
        }

        /// <summary>The atlas index of <paramref name="c"/> in <see cref="Characters"/> (that of '?' when unknown).</summary>
        public static int IndexOf(char c)
        {
            int index = Characters.IndexOf(char.ToUpperInvariant(c));
            return index < 0 ? Characters.IndexOf('?') : index;
        }

        /// <summary>Width of <paramref name="text"/> in pixels at scale 1 (no trailing spacing).</summary>
        public static int Measure(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length * Advance - 1;
        }
    }
}
