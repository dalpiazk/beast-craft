using System.Collections.Generic;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// The element matchup chart: how hard an attacking element lands on a defending one.
    /// <para>
    /// <strong>Attacker-side.</strong> Every row is written from the attacking element's point of
    /// view — "Fire is strong against Nature" — and a matchup is only ever looked up in that
    /// direction. The chart is not symmetric and does not pretend to be: Fire being weak against
    /// Water does not by itself make Water strong against Fire (here it happens to be, because
    /// Water's own row says so).
    /// </para>
    /// <para>
    /// <strong>Tunable starting default, not confirmed balance.</strong> The 2x / 0.5x / 1x values
    /// and which pairs sit where are a first-pass engineering default, chosen so the element system
    /// is complete rather than half-built. Expect both to move once balance work has real fights to
    /// measure against. <see cref="Strong"/>, <see cref="Weak"/> and the tables below are the only
    /// places to change.
    /// </para>
    /// <para>
    /// <see cref="Element.None"/> on either side is always 1x: a neutral skill hits everything
    /// evenly, and a unit with no affinity takes everything evenly. So does any value outside the
    /// enum's defined range, matching this namespace's non-throwing stance.
    /// </para>
    /// </summary>
    public static class ElementChart
    {
        /// <summary>The multiplier for a matchup the attacker is strong in.</summary>
        public const float Strong = 2f;

        /// <summary>The multiplier for a matchup the attacker is weak in.</summary>
        public const float Weak = 0.5f;

        /// <summary>The multiplier for every other matchup.</summary>
        public const float Neutral = 1f;

        /// <summary>Highest defined <see cref="Element"/> value; the table is sized from it.</summary>
        private const int MaxElement = (int)Element.Dark;

        /// <summary>[attack, defend] multipliers, indexed by the enum's int value.</summary>
        private static readonly float[,] Table = BuildTable();

        /// <summary>
        /// The multiplier <paramref name="attack"/> deals against a single
        /// <paramref name="defend"/> element: <see cref="Strong"/>, <see cref="Weak"/> or
        /// <see cref="Neutral"/>.
        /// </summary>
        public static float GetMultiplier(Element attack, Element defend)
        {
            int a = (int)attack;
            int d = (int)defend;

            if (a <= 0 || a > MaxElement || d <= 0 || d > MaxElement)
            {
                return Neutral;
            }

            return Table[a, d];
        }

        /// <summary>
        /// The multiplier <paramref name="attack"/> deals against a unit carrying every element in
        /// <paramref name="defenders"/>: the <em>product</em> of each single matchup. A dual-element
        /// defender weak to the attack twice takes 4x; strong-then-weak cancels to 1x.
        /// <para>
        /// A null or empty list is a unit with no affinity and returns 1x, as does a
        /// <see cref="Element.None"/> attack. <see cref="Element.None"/> entries in the list
        /// contribute 1x and so change nothing. Duplicates are not collapsed — listing the same
        /// element twice is an authoring mistake, and it is taken at face value rather than hidden.
        /// </para>
        /// </summary>
        public static float GetMultiplier(Element attack, IReadOnlyList<Element> defenders)
        {
            if (defenders == null)
            {
                return Neutral;
            }

            float multiplier = Neutral;

            for (int i = 0; i < defenders.Count; i++)
            {
                multiplier *= GetMultiplier(attack, defenders[i]);
            }

            return multiplier;
        }

        private static float[,] BuildTable()
        {
            float[,] table = new float[MaxElement + 1, MaxElement + 1];

            for (int a = 0; a <= MaxElement; a++)
            {
                for (int d = 0; d <= MaxElement; d++)
                {
                    table[a, d] = Neutral;
                }
            }

            // Attacker-side rows: Row(attack, strong against..., weak against...).
            Row(table, Element.Fire, new[] { Element.Nature, Element.Metal }, new[] { Element.Water, Element.Earth });
            Row(table, Element.Water, new[] { Element.Fire, Element.Earth }, new[] { Element.Lightning, Element.Nature });
            Row(table, Element.Earth, new[] { Element.Lightning, Element.Metal }, new[] { Element.Water, Element.Air });
            Row(table, Element.Air, new[] { Element.Earth, Element.Nature }, new[] { Element.Ice, Element.Lightning });
            Row(table, Element.Lightning, new[] { Element.Water, Element.Air }, new[] { Element.Earth, Element.Metal });
            Row(table, Element.Ice, new[] { Element.Nature, Element.Air }, new[] { Element.Fire, Element.Metal });
            Row(table, Element.Nature, new[] { Element.Water, Element.Earth, Element.Dark }, new[] { Element.Fire, Element.Ice });
            Row(table, Element.Metal, new[] { Element.Ice, Element.Light }, new[] { Element.Fire, Element.Lightning });
            Row(table, Element.Light, new[] { Element.Dark }, new Element[0]);
            Row(table, Element.Dark, new[] { Element.Light }, new Element[0]);

            return table;
        }

        private static void Row(float[,] table, Element attack, Element[] strong, Element[] weak)
        {
            for (int i = 0; i < strong.Length; i++)
            {
                table[(int)attack, (int)strong[i]] = Strong;
            }

            for (int i = 0; i < weak.Length; i++)
            {
                table[(int)attack, (int)weak[i]] = Weak;
            }
        }
    }
}
