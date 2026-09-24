using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// <see cref="EncounterLibraryData"/> with its names parsed and its shapes and templates indexed
    /// by id: what <see cref="EncounterGenerator"/> and the game's encounter set-up read. Expects a
    /// library that passed <see cref="EncounterLibraryValidator"/>; unknown ids give null, and an
    /// unparsable name falls back to its default rather than throwing.
    /// </summary>
    public sealed class EncounterLibrary
    {
        private readonly Dictionary<string, EncounterShapeData> _shapes = new Dictionary<string, EncounterShapeData>(StringComparer.Ordinal);
        private readonly Dictionary<string, EncounterTemplateData> _templates = new Dictionary<string, EncounterTemplateData>(StringComparer.Ordinal);
        private readonly List<EncounterShapeData> _shapeOrder = new List<EncounterShapeData>();
        private readonly List<ElementScheme> _schemes = new List<ElementScheme>();
        private readonly List<int> _schemeWeights = new List<int>();

        private EncounterLibrary(EncounterLibraryData data)
        {
            Data = data;
        }

        /// <summary>The authored data.</summary>
        public EncounterLibraryData Data { get; }

        /// <summary><see cref="EncounterLibraryData.DifficultyScale"/> (1 when not above 0).</summary>
        public double DifficultyScale
        {
            get { return Data.DifficultyScale > 0.0 ? Data.DifficultyScale : 1.0; }
        }

        /// <summary>Every shape, in file order.</summary>
        public IReadOnlyList<EncounterShapeData> Shapes
        {
            get { return _shapeOrder; }
        }

        /// <summary>The element schemes that can be drawn, in draw order, beside <see cref="SchemeWeights"/>.</summary>
        public IReadOnlyList<ElementScheme> Schemes
        {
            get { return _schemes; }
        }

        /// <summary>The weight of each of <see cref="Schemes"/>.</summary>
        public IReadOnlyList<int> SchemeWeights
        {
            get { return _schemeWeights; }
        }

        /// <summary>
        /// A library over <paramref name="data"/>. Null entries and repeated ids are skipped (the
        /// first wins).
        /// </summary>
        public static EncounterLibrary Build(EncounterLibraryData data)
        {
            EncounterLibrary library = new EncounterLibrary(data ?? new EncounterLibraryData());

            foreach (EncounterShapeData shape in library.Data.Shapes ?? new EncounterShapeData[0])
            {
                if (shape != null && !string.IsNullOrEmpty(shape.ShapeId) && !library._shapes.ContainsKey(shape.ShapeId))
                {
                    library._shapes.Add(shape.ShapeId, shape);
                    library._shapeOrder.Add(shape);
                }
            }

            foreach (EncounterTemplateData template in library.Data.Templates ?? new EncounterTemplateData[0])
            {
                if (template != null && !string.IsNullOrEmpty(template.EncounterId) && !library._templates.ContainsKey(template.EncounterId))
                {
                    library._templates.Add(template.EncounterId, template);
                }
            }

            foreach (SchemeWeightData entry in library.Data.SchemeWeights ?? new SchemeWeightData[0])
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Scheme) && Enum.IsDefined(typeof(ElementScheme), entry.Scheme))
                {
                    library._schemes.Add((ElementScheme)Enum.Parse(typeof(ElementScheme), entry.Scheme));
                    library._schemeWeights.Add(entry.Weight < 0 ? 0 : entry.Weight);
                }
            }

            return library;
        }

        /// <summary>The shape with <paramref name="shapeId"/>, or null.</summary>
        public EncounterShapeData GetShape(string shapeId)
        {
            return !string.IsNullOrEmpty(shapeId) && _shapes.TryGetValue(shapeId, out EncounterShapeData shape) ? shape : null;
        }

        /// <summary>The template with <paramref name="encounterId"/>, or null.</summary>
        public EncounterTemplateData GetTemplate(string encounterId)
        {
            return !string.IsNullOrEmpty(encounterId) && _templates.TryGetValue(encounterId, out EncounterTemplateData template) ? template : null;
        }

        /// <summary>An <see cref="ArenaSize"/> name, or Medium when it does not parse.</summary>
        public static ArenaSize ParseArena(string name)
        {
            EncounterLibraryValidator.TryParseArena(name, out ArenaSize arena);
            return arena;
        }
    }
}
