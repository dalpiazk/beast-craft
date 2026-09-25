using System;

namespace BeastCraft.Presentation.Text
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Glossary/glossary.json</c>: the battle terms the
    /// skill detail card highlights in skill text, with their definitions. Presentation only.
    /// Public fields, JSON keys are the field names (read with <c>FieldJson</c>). Built into a
    /// <see cref="Glossary"/>; checked by <see cref="GlossaryValidator"/>.
    /// </summary>
    [Serializable]
    public class GlossaryData
    {
        /// <summary>Path of the glossary relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Glossary/glossary.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>The terms, in file order (the order breaks ties between equally long matches).</summary>
        public GlossaryTermData[] Terms = new GlossaryTermData[0];
    }

    /// <summary>One glossary term.</summary>
    [Serializable]
    public class GlossaryTermData
    {
        /// <summary>Stable lowercase snake_case key.</summary>
        public string TermId;

        /// <summary>The term's name: the definition's title, and what <c>[[shown text|Term]]</c> markup names.</summary>
        public string Term;

        /// <summary>One of <see cref="GlossaryValidator.Categories"/>: Status, Combat, Stance or Passive.</summary>
        public string Category;

        /// <summary>
        /// The words highlighted as this term wherever they appear in skill text as a whole word
        /// (see <see cref="Glossary.Parse"/> for the case rule).
        /// </summary>
        public string[] Forms = new string[0];

        /// <summary>What tapping the term shows: one or two plain sentences.</summary>
        public string Definition;
    }
}
