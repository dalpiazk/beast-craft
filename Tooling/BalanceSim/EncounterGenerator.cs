using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Encounters;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Draws the default PvE run's encounters: for each shape in <c>encounter-library.json</c>,
    /// <c>--compositions</c> random compositions of enemy types under the shape's threat budget,
    /// each with an element scheme drawn by the library's weights. Seeded from <c>--seed</c> alone,
    /// so the same seed, files and composition count give the same compositions; every shape is
    /// generated in file order whatever <c>--encounters</c> filters, so a shape's compositions do not
    /// depend on which other shapes a run includes.
    /// <para>
    /// Elements are dealt from a shuffled deck of the ten elements (reshuffled when empty) shared by
    /// the whole generation, so every element is dealt before any is dealt twice: any run that deals
    /// ten or more elements (the default run deals far more) fields all ten.
    /// </para>
    /// </summary>
    public static class EncounterGenerator
    {
        /// <summary>Redraws allowed per composition before the shape is reported as unsatisfiable.</summary>
        public const int MaxAttempts = 2000;

        /// <summary>Redraws spent trying to avoid repeating an earlier composition of the same shape before accepting a repeat.</summary>
        public const int DistinctAttempts = 200;

        public static List<EncounterShape> Generate(EncounterLibraryData library, EnemyCatalog enemies, SimOptions options, List<string> errors)
        {
            Dictionary<string, int> order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < enemies.Enemies.Count; i++)
            {
                order[enemies.Enemies[i].EnemyId] = i;
            }

            Random rng = new Random(unchecked((options.Seed * 486187739) + 0x5EED));
            ElementDeck deck = new ElementDeck(rng);
            EnemyFactory factory = new EnemyFactory(enemies);
            List<EncounterShape> result = new List<EncounterShape>();

            foreach (EncounterShapeData shape in library.Shapes)
            {
                EncounterLibraryValidator.TryParseArena(shape.Arena, out ArenaSize arena);
                EncounterShape built = new EncounterShape
                {
                    Id = shape.ShapeId,
                    DisplayName = shape.DisplayName,
                    Description = shape.Description,
                    Arena = arena,
                    Data = shape
                };

                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int k = 0; k < options.Compositions; k++)
                {
                    List<KeyValuePair<EnemyData, int>> counts = DrawCounts(shape, arena, enemies, order, rng, seen, out double threat);
                    if (counts == null)
                    {
                        errors.Add("Shape '" + shape.ShapeId + "': no draw in " + MaxAttempts + " attempts met the threat budget [" +
                                   Number(shape.ThreatMin) + ", " + Number(shape.ThreatMax) + "] and MinDistinctTypes " + shape.MinDistinctTypes + ".");
                        return null;
                    }

                    Encounter encounter = new Encounter
                    {
                        Id = shape.ShapeId + "-" + (k + 1).ToString("00", CultureInfo.InvariantCulture),
                        Arena = arena,
                        Threat = threat
                    };

                    AssignElements(encounter, counts, library.SchemeWeights, factory, deck, rng, options);
                    built.Compositions.Add(encounter);
                }

                result.Add(built);
            }

            return result;
        }

        /// <summary>
        /// One composition's type counts, front-to-back (Vanguard, then Skirmisher, then Ranged;
        /// library order within a stance), or null when no draw fits the budget.
        /// </summary>
        private static List<KeyValuePair<EnemyData, int>> DrawCounts(EncounterShapeData shape, ArenaSize arena, EnemyCatalog enemies, Dictionary<string, int> order,
                                                                     Random rng, HashSet<string> seen, out double threat)
        {
            int totalWeight = 0;
            foreach (EncounterVariantData variant in shape.Variants)
            {
                totalWeight += variant.Weight;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int pick = rng.Next(totalWeight);
                EncounterVariantData chosen = shape.Variants[0];
                foreach (EncounterVariantData variant in shape.Variants)
                {
                    if (pick < variant.Weight)
                    {
                        chosen = variant;
                        break;
                    }

                    pick -= variant.Weight;
                }

                Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (EncounterSlotData slot in chosen.Slots)
                {
                    int n = rng.Next(slot.Min, slot.Max + 1);
                    for (int i = 0; i < n; i++)
                    {
                        string type = slot.Types[rng.Next(slot.Types.Length)];
                        counts.TryGetValue(type, out int current);
                        counts[type] = current + 1;
                    }
                }

                threat = 0.0;
                int total = 0;
                foreach (KeyValuePair<string, int> entry in counts)
                {
                    threat += enemies.Get(entry.Key).Threat * entry.Value;
                    total += entry.Value;
                }

                if (total == 0 || counts.Count < shape.MinDistinctTypes || threat < shape.ThreatMin - 1e-9 || threat > shape.ThreatMax + 1e-9)
                {
                    continue;
                }

                List<KeyValuePair<EnemyData, int>> sorted = new List<KeyValuePair<EnemyData, int>>();
                foreach (KeyValuePair<string, int> entry in counts)
                {
                    sorted.Add(new KeyValuePair<EnemyData, int>(enemies.Get(entry.Key), entry.Value));
                }

                sorted.Sort((a, b) =>
                {
                    int byStance = StanceRank(enemies.StanceOf(a.Key.EnemyId)).CompareTo(StanceRank(enemies.StanceOf(b.Key.EnemyId)));
                    return byStance != 0 ? byStance : order[a.Key.EnemyId].CompareTo(order[b.Key.EnemyId]);
                });

                // Every draw must seat on the shape's arena (large enemies need room). The validator's
                // worst-case check makes this pass for every draw of a valid file; it is a safety net.
                if (!EncounterFit.Fits(arena, Footprints(sorted, enemies)))
                {
                    continue;
                }

                string key = Describe(sorted);
                if (seen.Contains(key) && attempt < DistinctAttempts)
                {
                    continue;
                }

                seen.Add(key);
                return sorted;
            }

            threat = 0.0;
            return null;
        }

        /// <summary>The footprint of every unit of a sorted draw, in placement order.</summary>
        private static List<UnitFootprint> Footprints(List<KeyValuePair<EnemyData, int>> sorted, EnemyCatalog enemies)
        {
            List<UnitFootprint> footprints = new List<UnitFootprint>();
            foreach (KeyValuePair<EnemyData, int> entry in sorted)
            {
                UnitFootprint footprint = enemies.FootprintOf(entry.Key.EnemyId);
                for (int i = 0; i < entry.Value; i++)
                {
                    footprints.Add(footprint);
                }
            }

            return footprints;
        }

        /// <summary>Front-to-back placement rank: melee screen first, ranged at the back.</summary>
        private static int StanceRank(CombatStance stance)
        {
            switch (stance)
            {
                case CombatStance.Vanguard:
                    return 0;
                case CombatStance.Skirmisher:
                    return 1;
                default:
                    return 2;
            }
        }

        private static void AssignElements(Encounter encounter, List<KeyValuePair<EnemyData, int>> counts, SchemeWeightData[] weights, EnemyFactory factory,
                                           ElementDeck deck, Random rng, SimOptions options)
        {
            ElementScheme scheme = PickScheme(weights, rng);
            encounter.ElementScheme = SchemeName(scheme);
            Element shared = scheme == ElementScheme.Uniform ? deck.Next() : Element.None;
            int unit = 0;

            foreach (KeyValuePair<EnemyData, int> entry in counts)
            {
                Element typeElement = scheme == ElementScheme.PerType ? deck.Next() : shared;
                for (int i = 0; i < entry.Value; i++)
                {
                    Element element = scheme == ElementScheme.PerUnit ? deck.Next() : typeElement;
                    if (options.EnemyElementOverride.HasValue)
                    {
                        element = options.EnemyElementOverride.Value;
                    }

                    unit++;
                    encounter.Enemies.Add(factory.Slot(entry.Key.EnemyId, element, unit));
                }
            }

            if (options.EnemyElementOverride.HasValue)
            {
                encounter.ElementScheme = "override";
            }
        }

        /// <summary>One draw over the library's scheme weights, in file order.</summary>
        private static ElementScheme PickScheme(SchemeWeightData[] weights, Random rng)
        {
            int total = 0;
            foreach (SchemeWeightData entry in weights)
            {
                total += entry.Weight;
            }

            int pick = rng.Next(total);
            ElementScheme last = ElementScheme.None;
            foreach (SchemeWeightData entry in weights)
            {
                last = (ElementScheme)Enum.Parse(typeof(ElementScheme), entry.Scheme);
                if (pick < entry.Weight)
                {
                    return last;
                }

                pick -= entry.Weight;
            }

            return last;
        }

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

        /// <summary>"giant x1, archer x2" in placement order.</summary>
        private static string Describe(List<KeyValuePair<EnemyData, int>> counts)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<EnemyData, int> entry in counts)
            {
                parts.Add(entry.Key.EnemyId + " x" + entry.Value);
            }

            return string.Join(", ", parts);
        }

        private static string Number(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>The ten elements, dealt in shuffled order and reshuffled when the deck runs out.</summary>
        private class ElementDeck
        {
            private readonly Random _rng;
            private readonly List<Element> _cards = new List<Element>();

            public ElementDeck(Random rng)
            {
                _rng = rng;
            }

            public Element Next()
            {
                if (_cards.Count == 0)
                {
                    for (int e = (int)Element.Fire; e <= (int)Element.Dark; e++)
                    {
                        _cards.Add((Element)e);
                    }

                    for (int i = _cards.Count - 1; i > 0; i--)
                    {
                        int j = _rng.Next(i + 1);
                        Element swap = _cards[i];
                        _cards[i] = _cards[j];
                        _cards[j] = swap;
                    }
                }

                Element card = _cards[_cards.Count - 1];
                _cards.RemoveAt(_cards.Count - 1);
                return card;
            }
        }
    }
}
