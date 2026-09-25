using System;
using System.Collections.Generic;

namespace BeastCraft.Presentation.Text
{
    /// <summary>One term of a <see cref="Glossary"/>.</summary>
    public sealed class GlossaryTerm
    {
        public GlossaryTerm(string termId, string term, string category, IReadOnlyList<string> forms, string definition, int order)
        {
            TermId = termId;
            Term = term;
            Category = category;
            Forms = forms ?? new string[0];
            Definition = definition;
            Order = order;
        }

        public string TermId { get; }

        public string Term { get; }

        public string Category { get; }

        public IReadOnlyList<string> Forms { get; }

        public string Definition { get; }

        /// <summary>Its place in the file (earlier wins a tie).</summary>
        public int Order { get; }

        public override string ToString()
        {
            return Term;
        }
    }

    /// <summary>
    /// A run of rich text: plain, or a glossary term (<see cref="Term"/> set). <see cref="Marked"/>
    /// when it came from explicit <c>[[shown|Term]]</c> markup; <see cref="Broken"/> when that
    /// markup names no term (shown as plain text; the validator reports it).
    /// </summary>
    public readonly struct RichSpan
    {
        public RichSpan(string text, GlossaryTerm term, bool marked = false, bool broken = false)
        {
            Text = text;
            Term = term;
            Marked = marked;
            Broken = broken;
        }

        public string Text { get; }

        public GlossaryTerm Term { get; }

        public bool Marked { get; }

        public bool Broken { get; }

        /// <summary>Whether the span is a glossary term (highlighted, tappable).</summary>
        public bool IsTerm
        {
            get { return Term != null; }
        }

        public override string ToString()
        {
            return Term == null ? Text : "{" + Text + "=" + Term.TermId + "}";
        }
    }

    /// <summary>
    /// The battle glossary as lookups, and the one parser that turns skill text into rich text
    /// (<see cref="Parse"/>). Pure and deterministic: the same text and glossary always give the
    /// same spans. Built from data that passed <see cref="GlossaryValidator"/>; never throws.
    /// </summary>
    public sealed class Glossary
    {
        /// <summary>Opens explicit term markup: <c>[[shown text|Term]]</c> or <c>[[Term]]</c>.</summary>
        public const string MarkOpen = "[[";

        /// <summary>Closes explicit term markup.</summary>
        public const string MarkClose = "]]";

        private readonly List<GlossaryTerm> _terms = new List<GlossaryTerm>();
        private readonly Dictionary<string, GlossaryTerm> _byName = new Dictionary<string, GlossaryTerm>(StringComparer.Ordinal);

        // Every (surface text, term) a word may match, longest first, then by term and form order.
        private readonly List<KeyValuePair<string, GlossaryTerm>> _surfaces = new List<KeyValuePair<string, GlossaryTerm>>();

        private Glossary()
        {
        }

        /// <summary>An empty glossary (plain text only).</summary>
        public static readonly Glossary Empty = new Glossary();

        public IReadOnlyList<GlossaryTerm> Terms
        {
            get { return _terms; }
        }

        /// <summary>The glossary over <paramref name="data"/> (null: <see cref="Empty"/>). Entries without a TermId or Term are skipped.</summary>
        public static Glossary Build(GlossaryData data)
        {
            Glossary glossary = new Glossary();
            if (data == null || data.Terms == null)
            {
                return glossary;
            }

            List<(string Surface, GlossaryTerm Term, int Form)> surfaces = new List<(string, GlossaryTerm, int)>();
            foreach (GlossaryTermData entry in data.Terms)
            {
                if (entry == null || string.IsNullOrEmpty(entry.TermId) || string.IsNullOrEmpty(entry.Term) || glossary._byName.ContainsKey(entry.TermId) ||
                    glossary._byName.ContainsKey(entry.Term))
                {
                    continue;
                }

                GlossaryTerm term = new GlossaryTerm(entry.TermId, entry.Term, entry.Category, entry.Forms ?? new string[0], entry.Definition, glossary._terms.Count);
                glossary._terms.Add(term);
                glossary._byName[entry.TermId] = term;
                glossary._byName[entry.Term] = term;

                string[] forms = entry.Forms ?? new string[0];
                for (int f = 0; f < forms.Length; f++)
                {
                    foreach (string surface in Surfaces(forms[f]))
                    {
                        surfaces.Add((surface, term, f));
                    }
                }
            }

            surfaces.Sort((a, b) =>
            {
                int byLength = b.Surface.Length.CompareTo(a.Surface.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                int byTerm = a.Term.Order.CompareTo(b.Term.Order);
                return byTerm != 0 ? byTerm : a.Form.CompareTo(b.Form);
            });

            foreach ((string surface, GlossaryTerm term, int _) in surfaces)
            {
                glossary._surfaces.Add(new KeyValuePair<string, GlossaryTerm>(surface, term));
            }

            return glossary;
        }

        /// <summary>The term named <paramref name="name"/> — its exact <c>TermId</c> or <c>Term</c> (case-sensitive) — or null.</summary>
        public GlossaryTerm Find(string name)
        {
            return name != null && _byName.TryGetValue(name, out GlossaryTerm term) ? term : null;
        }

        /// <summary>
        /// The exact texts a form matches (the case rule): a form written all in lowercase matches
        /// itself and itself with a capital first letter (a sentence may start with it); a form
        /// with any capital (a name, such as a stance) matches only itself.
        /// </summary>
        public static IEnumerable<string> Surfaces(string form)
        {
            if (string.IsNullOrEmpty(form))
            {
                yield break;
            }

            yield return form;
            if (form == form.ToLowerInvariant() && char.IsLetter(form[0]))
            {
                string capital = char.ToUpperInvariant(form[0]) + form.Substring(1);
                if (capital != form)
                {
                    yield return capital;
                }
            }
        }

        /// <summary>
        /// <paramref name="text"/> as rich text. First the explicit markup: <c>[[shown|Term]]</c>
        /// (or <c>[[Term]]</c>) becomes a span of the shown text for that term (by TermId or Term,
        /// case-sensitive; an unknown one a <see cref="RichSpan.Broken"/> plain span; a <c>[[</c>
        /// with no <c>]]</c> stays literal). Then, in the plain text between, every glossary form
        /// that stands as a whole word (not inside a longer run of letters and digits) under the
        /// case rule (<see cref="Surfaces"/>) becomes a term span: scanning left to right, at each
        /// word start the longest matching form wins, then the earlier term, then the earlier form.
        /// Adjacent plain text is one span. Concatenating the spans' text gives the text without
        /// the markup.
        /// </summary>
        public List<RichSpan> Parse(string text)
        {
            List<RichSpan> spans = new List<RichSpan>();
            if (string.IsNullOrEmpty(text))
            {
                return spans;
            }

            int at = 0;
            while (at < text.Length)
            {
                int open = text.IndexOf(MarkOpen, at, StringComparison.Ordinal);
                int close = open < 0 ? -1 : text.IndexOf(MarkClose, open + MarkOpen.Length, StringComparison.Ordinal);
                if (open < 0 || close < 0)
                {
                    Match(text.Substring(at), spans);
                    break;
                }

                Match(text.Substring(at, open - at), spans);
                string inner = text.Substring(open + MarkOpen.Length, close - open - MarkOpen.Length);
                int bar = inner.LastIndexOf('|');
                string shown = bar < 0 ? inner : inner.Substring(0, bar);
                string name = bar < 0 ? inner : inner.Substring(bar + 1);
                GlossaryTerm term = Find(name.Trim());
                Append(spans, new RichSpan(shown, term, true, term == null));
                at = close + MarkClose.Length;
            }

            return spans;
        }

        /// <summary>The plain text of <paramref name="text"/>: its markup replaced by the shown words.</summary>
        public string Plain(string text)
        {
            System.Text.StringBuilder plain = new System.Text.StringBuilder();
            foreach (RichSpan span in Parse(text))
            {
                plain.Append(span.Text);
            }

            return plain.ToString();
        }

        /// <summary>The distinct terms <paramref name="text"/> names, in order of first appearance.</summary>
        public List<GlossaryTerm> TermsIn(string text)
        {
            List<GlossaryTerm> terms = new List<GlossaryTerm>();
            foreach (RichSpan span in Parse(text))
            {
                if (span.Term != null && !terms.Contains(span.Term))
                {
                    terms.Add(span.Term);
                }
            }

            return terms;
        }

        private void Match(string text, List<RichSpan> spans)
        {
            int plainStart = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!IsWordStart(text, i))
                {
                    i++;
                    continue;
                }

                KeyValuePair<string, GlossaryTerm>? found = null;
                foreach (KeyValuePair<string, GlossaryTerm> surface in _surfaces)
                {
                    int end = i + surface.Key.Length;
                    if (end <= text.Length && string.CompareOrdinal(text, i, surface.Key, 0, surface.Key.Length) == 0 && (end == text.Length || !IsWordChar(text[end])))
                    {
                        found = surface;
                        break;
                    }
                }

                if (!found.HasValue)
                {
                    i++;
                    continue;
                }

                if (i > plainStart)
                {
                    Append(spans, new RichSpan(text.Substring(plainStart, i - plainStart), null));
                }

                Append(spans, new RichSpan(text.Substring(i, found.Value.Key.Length), found.Value.Value));
                i += found.Value.Key.Length;
                plainStart = i;
            }

            if (plainStart < text.Length)
            {
                Append(spans, new RichSpan(text.Substring(plainStart), null));
            }
        }

        private static void Append(List<RichSpan> spans, RichSpan span)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                return;
            }

            int last = spans.Count - 1;
            if (span.Term == null && !span.Broken && last >= 0 && spans[last].Term == null && !spans[last].Broken)
            {
                spans[last] = new RichSpan(spans[last].Text + span.Text, null);
                return;
            }

            spans.Add(span);
        }

        private static bool IsWordStart(string text, int i)
        {
            return IsWordChar(text[i]) && (i == 0 || !IsWordChar(text[i - 1]));
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c);
        }
    }
}
