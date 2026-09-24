using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Encounters;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The default PvE run's encounters: the game's own <see cref="EncounterGenerator"/> drawing
    /// <c>--compositions</c> lineups per shape of <c>encounter-library.json</c>, turned into the
    /// simulator's <see cref="Encounter"/>s. One generator, seeded from <c>--seed</c> alone, draws
    /// every shape in file order (one seen set per shape, so a shape's lineups are distinct where the
    /// budget allows) whatever <c>--encounters</c> filters: the same seed, files and composition
    /// count give the same compositions, and a shape's compositions do not depend on which other
    /// shapes a run includes. <c>--enemy-element</c> is applied after the draw, so it never changes
    /// what is drawn.
    /// </summary>
    public static class GeneratedEncounters
    {
        public static List<EncounterShape> Generate(EncounterLibraryData data, EnemyCatalog enemies, SimOptions options, List<string> errors)
        {
            EncounterLibrary library = EncounterLibrary.Build(data);
            EncounterGenerator generator = new EncounterGenerator(library, enemies, options.Seed);
            EnemyFactory factory = new EnemyFactory(enemies);
            List<EncounterShape> result = new List<EncounterShape>();

            foreach (EncounterShapeData shape in library.Shapes)
            {
                EncounterShape built = new EncounterShape
                {
                    Id = shape.ShapeId,
                    DisplayName = shape.DisplayName,
                    Description = shape.Description,
                    Arena = EncounterLibrary.ParseArena(shape.Arena),
                    Data = shape
                };

                HashSet<string> seen = new HashSet<string>(System.StringComparer.Ordinal);
                for (int k = 0; k < options.Compositions; k++)
                {
                    EncounterLineup lineup = generator.Draw(shape.ShapeId, seen);
                    if (lineup == null)
                    {
                        errors.Add("Shape '" + shape.ShapeId + "': no draw in " + EncounterGenerator.MaxAttempts + " attempts met the threat budget [" +
                                   Number(shape.ThreatMin) + ", " + Number(shape.ThreatMax) + "] and MinDistinctTypes " + shape.MinDistinctTypes + ".");
                        return null;
                    }

                    Encounter encounter = new Encounter
                    {
                        Id = shape.ShapeId + "-" + (k + 1).ToString("00", CultureInfo.InvariantCulture),
                        Arena = lineup.Arena,
                        Threat = lineup.Threat,
                        ElementScheme = options.EnemyElementOverride.HasValue ? "override" : SchemeName(lineup.ElementScheme)
                    };

                    for (int i = 0; i < lineup.Enemies.Count; i++)
                    {
                        EncounterLineupEnemy enemy = lineup.Enemies[i];
                        encounter.Enemies.Add(factory.Slot(enemy.EnemyId, options.EnemyElementOverride ?? enemy.Element, i + 1));
                    }

                    built.Compositions.Add(encounter);
                }

                result.Add(built);
            }

            return result;
        }

        /// <summary>The report's name for a scheme.</summary>
        public static string SchemeName(ElementScheme scheme)
        {
            switch (scheme)
            {
                case ElementScheme.Uniform:
                    return "one element";
                case ElementScheme.PerType:
                    return "per type";
                case ElementScheme.PerUnit:
                    return "per unit";
                default:
                    return "none";
            }
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
