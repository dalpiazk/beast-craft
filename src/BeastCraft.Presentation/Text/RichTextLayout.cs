using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Layout;

namespace BeastCraft.Presentation.Text
{
    /// <summary>One placed run of rich text: its words, where it sits and, for a glossary term, which.</summary>
    public readonly struct RichRun
    {
        public RichRun(string text, float x, float y, float width, int line, GlossaryTerm term)
        {
            Text = text;
            X = x;
            Y = y;
            Width = width;
            Line = line;
            Term = term;
        }

        public string Text { get; }

        /// <summary>Left edge, relative to the block's top-left.</summary>
        public float X { get; }

        /// <summary>Top of its line, relative to the block's top-left.</summary>
        public float Y { get; }

        public float Width { get; }

        /// <summary>Which line (0 = the first).</summary>
        public int Line { get; }

        /// <summary>The glossary term, or null for plain text.</summary>
        public GlossaryTerm Term { get; }
    }

    /// <summary>
    /// Rich text (<see cref="Glossary.Parse"/>) laid out into lines of a given width: words are
    /// packed greedily, a word wider than a whole line is broken between characters (as many as
    /// fit on each line, at least one), a newline starts a new line, a term's words keep
    /// their term even when a phrase breaks across lines, and consecutive words of the same term
    /// on one line are merged into one run, so each run is one draw call and one tap target. Pure
    /// maths over a measuring function (the renderer's text width at the drawn size); the
    /// renderer draws the runs and hit-tests taps with <see cref="TermAt"/>.
    /// </summary>
    public sealed class RichTextLayout
    {
        private readonly List<RichRun> _runs = new List<RichRun>();

        private RichTextLayout(float lineHeight)
        {
            LineHeight = lineHeight;
        }

        public IReadOnlyList<RichRun> Runs
        {
            get { return _runs; }
        }

        public float LineHeight { get; }

        /// <summary>How many lines the text takes.</summary>
        public int Lines { get; private set; }

        /// <summary>The block's height: its lines times the line height.</summary>
        public float Height
        {
            get { return Lines * LineHeight; }
        }

        /// <summary>
        /// Lays <paramref name="spans"/> out in lines at most <paramref name="width"/> wide,
        /// <paramref name="lineHeight"/> apart, measuring text with <paramref name="measure"/>.
        /// With <paramref name="maxLines"/> above 0, words past that many lines are dropped.
        /// </summary>
        public static RichTextLayout Of(IReadOnlyList<RichSpan> spans, Func<string, float> measure, float width, float lineHeight, int maxLines = 0)
        {
            RichTextLayout layout = new RichTextLayout(lineHeight);
            float space = measure(" ");
            float x = 0f;
            int line = 0;
            bool lineEmpty = true;

            foreach (RichSpan span in spans ?? new RichSpan[0])
            {
                string text = span.Text ?? string.Empty;
                int i = 0;
                while (i < text.Length)
                {
                    if (text[i] == ' ')
                    {
                        i++;
                        if (!lineEmpty)
                        {
                            x += space;
                        }

                        continue;
                    }

                    if (text[i] == '\n')
                    {
                        i++;
                        line++;
                        x = 0f;
                        lineEmpty = true;
                        continue;
                    }

                    int end = i;
                    while (end < text.Length && text[end] != ' ' && text[end] != '\n')
                    {
                        end++;
                    }

                    string word = text.Substring(i, end - i);
                    i = end;
                    float w = measure(word);
                    if (!lineEmpty && x + w > width)
                    {
                        line++;
                        x = 0f;
                        lineEmpty = true;
                    }

                    // A word wider than a whole line: break it between characters.
                    while (w > width && word.Length > 1)
                    {
                        int fit = 1;
                        while (fit < word.Length && measure(word.Substring(0, fit + 1)) <= width)
                        {
                            fit++;
                        }

                        if (maxLines > 0 && line >= maxLines)
                        {
                            layout.Lines = maxLines;
                            return layout;
                        }

                        string piece = word.Substring(0, fit);
                        layout.Place(piece, 0f, line, measure(piece), span.Term, measure);
                        word = word.Substring(fit);
                        w = measure(word);
                        line++;
                        x = 0f;
                        lineEmpty = true;
                    }

                    if (maxLines > 0 && line >= maxLines)
                    {
                        layout.Lines = maxLines;
                        return layout;
                    }

                    layout.Place(word, x, line, w, span.Term, measure);
                    x += w;
                    lineEmpty = false;
                }
            }

            layout.Lines = lineEmpty && line > 0 ? line : line + 1;
            if (layout._runs.Count == 0)
            {
                layout.Lines = 0;
            }

            return layout;
        }

        /// <summary>The term whose run contains the point (<paramref name="x"/>, <paramref name="y"/>), relative to the block's top-left; null when none does.</summary>
        public GlossaryTerm TermAt(float x, float y)
        {
            foreach (RichRun run in _runs)
            {
                if (run.Term != null && new Rect(run.X, run.Y, run.Width, LineHeight).Contains(x, y))
                {
                    return run.Term;
                }
            }

            return null;
        }

        /// <summary>The first run of <paramref name="term"/> (for placing its definition), or null.</summary>
        public RichRun? RunOf(GlossaryTerm term)
        {
            foreach (RichRun run in _runs)
            {
                if (run.Term != null && run.Term == term)
                {
                    return run;
                }
            }

            return null;
        }

        private void Place(string word, float x, int line, float width, GlossaryTerm term, Func<string, float> measure)
        {
            int last = _runs.Count - 1;
            if (last >= 0 && _runs[last].Line == line && _runs[last].Term == term && x > 0f)
            {
                // The same term (or plain text) continues on this line: one run, re-measured whole.
                RichRun previous = _runs[last];
                string gap = new string(' ', 1);
                string joined = x > previous.X + previous.Width ? previous.Text + gap + word : previous.Text + word;
                _runs[last] = new RichRun(joined, previous.X, previous.Y, x + width - previous.X, line, term);
                return;
            }

            _runs.Add(new RichRun(word, x, line * LineHeight, width, line, term));
        }
    }
}
