using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Tutorial
{
    /// <summary>
    /// An adaptive fight's enemy element (<c>FixedNodeData.AdaptiveElements</c>, Hearthglen's finale):
    /// the first element, in <see cref="Element"/> order, that is neutral (<see cref="ElementChart.Neutral"/>,
    /// 1x) in both directions against every one of the team's elements — so the element chart neither
    /// punishes nor favours any pick combination. <see cref="Element.None"/> when no element is
    /// neutral against them all (None is always 1x). Deterministic: the same elements always give the
    /// same answer, whatever their order.
    /// </summary>
    public static class ElementAdaptation
    {
        public static Element NeutralElement(IEnumerable<Element> team)
        {
            List<Element> elements = new List<Element>();
            foreach (Element element in team ?? new Element[0])
            {
                if (element != Element.None && !elements.Contains(element))
                {
                    elements.Add(element);
                }
            }

            for (Element candidate = Element.Fire; candidate <= Element.Dark; candidate++)
            {
                if (IsNeutral(candidate, elements))
                {
                    return candidate;
                }
            }

            return Element.None;
        }

        /// <summary>Whether <paramref name="candidate"/> hits and is hit by every one of <paramref name="team"/> at 1x.</summary>
        public static bool IsNeutral(Element candidate, IEnumerable<Element> team)
        {
            foreach (Element element in team ?? new Element[0])
            {
                if (ElementChart.GetMultiplier(candidate, element) != ElementChart.Neutral || ElementChart.GetMultiplier(element, candidate) != ElementChart.Neutral)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Every element of <paramref name="species"/> (null entries skipped).</summary>
        public static List<Element> ElementsOf(IEnumerable<CreatureSpeciesSO> species)
        {
            List<Element> elements = new List<Element>();
            foreach (CreatureSpeciesSO one in species ?? new CreatureSpeciesSO[0])
            {
                foreach (Element element in one == null || one.Elements == null ? new Element[0] : one.Elements)
                {
                    elements.Add(element);
                }
            }

            return elements;
        }
    }
}
