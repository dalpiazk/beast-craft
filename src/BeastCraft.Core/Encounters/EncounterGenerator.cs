using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// Draws generated encounters from an <see cref="EncounterLibrary"/>'s shapes: the one
    /// generator the game and the balance simulator share, so the simulator calibrates exactly the
    /// lineups the game fields.
    /// <para>
    /// A <see cref="Draw"/> picks one of the shape's variants (by weight), draws each slot's count
    /// and then each unit's type, and keeps the draw only if its summed <see cref="EnemyData.Threat"/>
    /// is inside the shape's budget, it has at least <see cref="EncounterShapeData.MinDistinctTypes"/>
    /// types and it seats on the arena (<see cref="EncounterFit"/>); units are ordered front to back
    /// (Vanguard, Skirmisher, Ranged; library order within a stance: <see cref="EncounterFit.ComparePlacement"/>,
    /// the order the validator's worst-case fit check packs in). It then draws an
    /// <see cref="ElementScheme"/> by the library's weights and deals the elements from a shuffled
    /// deck of the ten, reshuffled when empty, shared by every draw of this generator — so every
    /// element is dealt before any is dealt twice.
    /// </para>
    /// <para>
    /// Deterministic: one <see cref="Random"/> seeded from the constructor's seed drives everything,
    /// so the same library, enemies, seed and sequence of draws give the same lineups. Callers derive
    /// the seed (for example with <c>LootRoller.DeriveSeed</c> from the save's seed and the map node);
    /// the battle itself takes a separate seed.
    /// </para>
    /// </summary>
    public sealed class EncounterGenerator
    {
        /// <summary>Redraws allowed per lineup before the shape is reported as unsatisfiable (a null draw).</summary>
        public const int MaxAttempts = 2000;

        /// <summary>Redraws spent avoiding a lineup already in the caller's seen set before accepting a repeat.</summary>
        public const int DistinctAttempts = 200;

        private readonly EncounterLibrary _library;
        private readonly EnemyCatalog _enemies;
        private readonly Random _rng;
        private readonly ElementDeck _deck;
        private readonly Dictionary<string, int> _order = new Dictionary<string, int>(StringComparer.Ordinal);

        public EncounterGenerator(EncounterLibrary library, EnemyCatalog enemies, int seed)
        {
            _library = library;
            _enemies = enemies;
            _rng = new Random(unchecked((seed * 486187739) + 0x5EED));
            _deck = new ElementDeck(_rng);

            for (int i = 0; enemies != null && i < enemies.Enemies.Count; i++)
            {
                _order[enemies.Enemies[i].EnemyId] = i;
            }
        }

        /// <summary>
        /// One lineup of <paramref name="shapeId"/>, or null when the shape is unknown or no draw in
        /// <see cref="MaxAttempts"/> satisfies it. With a <paramref name="seen"/> set, a lineup
        /// already in it is redrawn (up to <see cref="DistinctAttempts"/> times) and the accepted
        /// lineup's key is added to it: pass one set per shape to get distinct lineups.
        /// </summary>
        public EncounterLineup Draw(string shapeId, HashSet<string> seen = null)
        {
            EncounterShapeData shape = _library == null ? null : _library.GetShape(shapeId);
            if (shape == null || _enemies == null || shape.Variants == null || shape.Variants.Length == 0)
            {
                return null;
            }

            ArenaSize arena = EncounterLibrary.ParseArena(shape.Arena);
            List<KeyValuePair<EnemyData, int>> counts = DrawCounts(shape, arena, seen, out double threat);
            if (counts == null)
            {
                return null;
            }

            ElementScheme scheme = PickScheme();
            Element shared = scheme == ElementScheme.Uniform ? _deck.Next() : Element.None;
            List<EncounterLineupEnemy> enemies = new List<EncounterLineupEnemy>();

            foreach (KeyValuePair<EnemyData, int> entry in counts)
            {
                Element typeElement = scheme == ElementScheme.PerType ? _deck.Next() : shared;
                CombatStance stance = _enemies.StanceOf(entry.Key.EnemyId);
                for (int i = 0; i < entry.Value; i++)
                {
                    Element element = scheme == ElementScheme.PerUnit ? _deck.Next() : typeElement;
                    enemies.Add(new EncounterLineupEnemy(entry.Key.EnemyId, entry.Key.DisplayName, element, stance));
                }
            }

            return new EncounterLineup(shape.ShapeId, arena, threat, scheme, enemies);
        }

        private List<KeyValuePair<EnemyData, int>> DrawCounts(EncounterShapeData shape, ArenaSize arena, HashSet<string> seen, out double threat)
        {
            int totalWeight = 0;
            foreach (EncounterVariantData variant in shape.Variants)
            {
                totalWeight += variant.Weight;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int pick = _rng.Next(totalWeight);
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
                    int n = _rng.Next(slot.Min, slot.Max + 1);
                    for (int i = 0; i < n; i++)
                    {
                        string type = slot.Types[_rng.Next(slot.Types.Length)];
                        counts.TryGetValue(type, out int current);
                        counts[type] = current + 1;
                    }
                }

                threat = 0.0;
                int total = 0;
                foreach (KeyValuePair<string, int> entry in counts)
                {
                    threat += _enemies.Get(entry.Key).Threat * entry.Value;
                    total += entry.Value;
                }

                if (total == 0 || counts.Count < shape.MinDistinctTypes || threat < shape.ThreatMin - 1e-9 || threat > shape.ThreatMax + 1e-9)
                {
                    continue;
                }

                List<KeyValuePair<EnemyData, int>> sorted = new List<KeyValuePair<EnemyData, int>>();
                foreach (KeyValuePair<string, int> entry in counts)
                {
                    sorted.Add(new KeyValuePair<EnemyData, int>(_enemies.Get(entry.Key), entry.Value));
                }

                sorted.Sort((a, b) => EncounterFit.ComparePlacement(_enemies.StanceOf(a.Key.EnemyId), _order[a.Key.EnemyId], _enemies.StanceOf(b.Key.EnemyId),
                                                                   _order[b.Key.EnemyId]));

                // Every draw must seat on the shape's arena (large enemies need room). The validator's
                // worst-case check makes this pass for every draw of a valid library; it is a safety net.
                if (!EncounterFit.Fits(arena, Footprints(sorted)))
                {
                    continue;
                }

                string key = Describe(sorted);
                if (seen != null && seen.Contains(key) && attempt < DistinctAttempts)
                {
                    continue;
                }

                if (seen != null)
                {
                    seen.Add(key);
                }

                return sorted;
            }

            threat = 0.0;
            return null;
        }

        private List<UnitFootprint> Footprints(List<KeyValuePair<EnemyData, int>> sorted)
        {
            List<UnitFootprint> footprints = new List<UnitFootprint>();
            foreach (KeyValuePair<EnemyData, int> entry in sorted)
            {
                UnitFootprint footprint = _enemies.FootprintOf(entry.Key.EnemyId);
                for (int i = 0; i < entry.Value; i++)
                {
                    footprints.Add(footprint);
                }
            }

            return footprints;
        }

        private ElementScheme PickScheme()
        {
            IReadOnlyList<ElementScheme> schemes = _library.Schemes;
            IReadOnlyList<int> weights = _library.SchemeWeights;
            int total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                total += weights[i];
            }

            if (total <= 0)
            {
                return ElementScheme.None;
            }

            int pick = _rng.Next(total);
            for (int i = 0; i < schemes.Count; i++)
            {
                if (pick < weights[i])
                {
                    return schemes[i];
                }

                pick -= weights[i];
            }

            return schemes[schemes.Count - 1];
        }

        /// <summary>"giant x1, archer x2" in placement order: the key of a seen set.</summary>
        private static string Describe(List<KeyValuePair<EnemyData, int>> counts)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<EnemyData, int> entry in counts)
            {
                parts.Add(entry.Key.EnemyId + " x" + entry.Value);
            }

            return string.Join(", ", parts);
        }

        /// <summary>The ten elements, dealt in shuffled order and reshuffled when the deck runs out.</summary>
        private sealed class ElementDeck
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

    /// <summary>One generated encounter: its shape, arena, summed threat, element scheme and enemies front to back.</summary>
    public sealed class EncounterLineup
    {
        public EncounterLineup(string shapeId, ArenaSize arena, double threat, ElementScheme elementScheme, IReadOnlyList<EncounterLineupEnemy> enemies)
        {
            ShapeId = shapeId;
            Arena = arena;
            Threat = threat;
            ElementScheme = elementScheme;
            Enemies = enemies;
        }

        public string ShapeId { get; }

        public ArenaSize Arena { get; }

        /// <summary>The summed <see cref="EnemyData.Threat"/> of every unit.</summary>
        public double Threat { get; }

        public ElementScheme ElementScheme { get; }

        /// <summary>Every enemy, in placement order (front to back).</summary>
        public IReadOnlyList<EncounterLineupEnemy> Enemies { get; }
    }

    /// <summary>One enemy of a lineup. Also what the scouting preview reads (<see cref="IEncounterPreviewSource"/>).</summary>
    public sealed class EncounterLineupEnemy : IEncounterPreviewSource
    {
        public EncounterLineupEnemy(string enemyId, string displayName, Element element, CombatStance stance)
        {
            EnemyId = enemyId;
            DisplayName = displayName;
            Element = element;
            Stance = stance;
        }

        /// <summary>The <see cref="EnemyData.EnemyId"/>.</summary>
        public string EnemyId { get; }

        public string DisplayName { get; }

        public Element Element { get; }

        public CombatStance Stance { get; }
    }
}
