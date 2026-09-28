using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>How one attacking element fares against one defending element, as the chart names it.</summary>
    public enum MatchupKind
    {
        Neutral = 0,
        Strong = 1,
        Mild = 2,
        Weak = 3
    }

    /// <summary>One cell of the element chart: an attacking element (row) against a defending one (column).</summary>
    public sealed class ElementCell
    {
        public Element Attack;
        public Element Defend;

        /// <summary><see cref="ElementChart.GetMultiplier(Element, Element)"/> of the pair.</summary>
        public float Multiplier;

        public MatchupKind Kind;

        /// <summary>The row or the column is one of the player's team elements.</summary>
        public bool Highlighted;
    }

    /// <summary>
    /// The element chart screen: the 10x10 matrix of <see cref="ElementChart"/> (attacker rows,
    /// defender columns), each cell Strong (2x), Mild (1.25x), Weak (0.5x) or Neutral, with the
    /// player's team elements highlighted. Read straight from the chart, never re-authored.
    /// </summary>
    public sealed class ElementChartViewModel
    {
        /// <summary>The ten elements in chart order (<see cref="Element.None"/> left out).</summary>
        public static readonly Element[] Elements =
        {
            Element.Fire, Element.Water, Element.Earth, Element.Air, Element.Lightning, Element.Ice, Element.Nature, Element.Metal, Element.Light, Element.Dark
        };

        public ElementChartViewModel(IEnumerable<Element> teamElements = null)
        {
            if (teamElements != null)
            {
                foreach (Element element in teamElements)
                {
                    if (element != Element.None)
                    {
                        TeamElements.Add(element);
                    }
                }
            }

            foreach (Element attack in Elements)
            {
                List<ElementCell> row = new List<ElementCell>();
                foreach (Element defend in Elements)
                {
                    float multiplier = ElementChart.GetMultiplier(attack, defend);
                    row.Add(new ElementCell
                    {
                        Attack = attack,
                        Defend = defend,
                        Multiplier = multiplier,
                        Kind = KindOf(multiplier),
                        Highlighted = TeamElements.Contains(attack) || TeamElements.Contains(defend)
                    });
                }

                Rows.Add(row);
            }
        }

        /// <summary>The team's elements (highlighted rows and columns).</summary>
        public HashSet<Element> TeamElements { get; } = new HashSet<Element>();

        /// <summary>Rows by attacking element, columns by defending element, both in <see cref="Elements"/> order.</summary>
        public List<List<ElementCell>> Rows { get; } = new List<List<ElementCell>>();

        public ElementCell Cell(Element attack, Element defend)
        {
            int a = Array.IndexOf(Elements, attack);
            int d = Array.IndexOf(Elements, defend);
            return a < 0 || d < 0 ? null : Rows[a][d];
        }

        /// <summary>The chart's name for a multiplier: its four values (anything else, a dual-type product, by which side of 1 it falls).</summary>
        public static MatchupKind KindOf(float multiplier)
        {
            if (multiplier == ElementChart.Strong)
            {
                return MatchupKind.Strong;
            }

            if (multiplier == ElementChart.Mild)
            {
                return MatchupKind.Mild;
            }

            if (multiplier == ElementChart.Weak)
            {
                return MatchupKind.Weak;
            }

            if (multiplier == ElementChart.Neutral)
            {
                return MatchupKind.Neutral;
            }

            return multiplier > 1f ? (multiplier >= ElementChart.Strong ? MatchupKind.Strong : MatchupKind.Mild) : MatchupKind.Weak;
        }

        /// <summary>The legend: each kind's label and multiplier.</summary>
        public static string Label(MatchupKind kind)
        {
            switch (kind)
            {
                case MatchupKind.Strong:
                    return "Strong x2";
                case MatchupKind.Mild:
                    return "Mild x1.25";
                case MatchupKind.Weak:
                    return "Weak x0.5";
                default:
                    return "Neutral x1";
            }
        }
    }
}
