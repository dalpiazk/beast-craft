using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    public class ElementChartTests
    {
        /// <summary>The full attacker-side chart, as specified: attack, strong against, weak against.</summary>
        private static readonly Dictionary<Element, Element[][]> Spec = new Dictionary<Element, Element[][]>
        {
            { Element.Fire, new[] { new[] { Element.Nature, Element.Metal }, new[] { Element.Water, Element.Earth } } },
            { Element.Water, new[] { new[] { Element.Fire, Element.Earth }, new[] { Element.Lightning, Element.Nature } } },
            { Element.Earth, new[] { new[] { Element.Lightning, Element.Metal }, new[] { Element.Water, Element.Air } } },
            { Element.Air, new[] { new[] { Element.Earth, Element.Nature }, new[] { Element.Ice, Element.Lightning } } },
            { Element.Lightning, new[] { new[] { Element.Water, Element.Air }, new[] { Element.Earth, Element.Metal } } },
            { Element.Ice, new[] { new[] { Element.Nature, Element.Air }, new[] { Element.Fire, Element.Metal } } },
            { Element.Nature, new[] { new[] { Element.Water, Element.Earth, Element.Dark }, new[] { Element.Fire, Element.Ice } } },
            { Element.Metal, new[] { new[] { Element.Ice, Element.Light }, new[] { Element.Fire, Element.Lightning } } },
            { Element.Light, new[] { new[] { Element.Dark }, new Element[0] } },
            { Element.Dark, new[] { new[] { Element.Light }, new Element[0] } },
        };

        [Test]
        public void EnumValues_AreStable()
        {
            Assert.AreEqual(0, (int)Element.None);
            Assert.AreEqual(1, (int)Element.Fire);
            Assert.AreEqual(2, (int)Element.Water);
            Assert.AreEqual(3, (int)Element.Earth);
            Assert.AreEqual(4, (int)Element.Air);
            Assert.AreEqual(5, (int)Element.Lightning);
            Assert.AreEqual(6, (int)Element.Ice);
            Assert.AreEqual(7, (int)Element.Nature);
            Assert.AreEqual(8, (int)Element.Metal);
            Assert.AreEqual(9, (int)Element.Light);
            Assert.AreEqual(10, (int)Element.Dark);
        }

        [Test]
        public void EveryPair_MatchesSpec()
        {
            foreach (Element attack in (Element[])Enum.GetValues(typeof(Element)))
            {
                foreach (Element defend in (Element[])Enum.GetValues(typeof(Element)))
                {
                    float expected = 1f;

                    if (Spec.TryGetValue(attack, out Element[][] row))
                    {
                        if (Array.IndexOf(row[0], defend) >= 0)
                        {
                            expected = 2f;
                        }
                        else if (Array.IndexOf(row[1], defend) >= 0)
                        {
                            expected = 0.5f;
                        }
                    }

                    Assert.AreEqual(expected, ElementChart.GetMultiplier(attack, defend), attack + " vs " + defend);
                }
            }
        }

        [Test]
        public void None_OnEitherSide_IsNeutral()
        {
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.None, Element.Fire));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, Element.None));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.None, Element.None));
        }

        [Test]
        public void UndefinedValues_AreNeutral()
        {
            Assert.AreEqual(1f, ElementChart.GetMultiplier((Element)99, Element.Fire));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, (Element)(-1)));
        }

        [Test]
        public void Chart_IsAttackerSided_NotSymmetric()
        {
            // Light is strong against Dark, and Dark is strong against Light: each row speaks only for itself.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Light, Element.Dark));
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Dark, Element.Light));

            // Nature is strong against Dark, but Dark is neutral against Nature.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Nature, Element.Dark));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Dark, Element.Nature));
        }

        [Test]
        public void List_NullOrEmpty_IsNeutral()
        {
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, (IReadOnlyList<Element>)null));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, new Element[0]));
        }

        [Test]
        public void List_MultipliesAcrossDefenders()
        {
            // Fire is strong against both Nature and Metal: 2 * 2.
            Assert.AreEqual(4f, ElementChart.GetMultiplier(Element.Fire, new[] { Element.Nature, Element.Metal }));

            // Fire is weak against both Water and Earth: 0.5 * 0.5.
            Assert.AreEqual(0.25f, ElementChart.GetMultiplier(Element.Fire, new[] { Element.Water, Element.Earth }));

            // Strong then weak cancels out.
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, new[] { Element.Nature, Element.Water }));

            // A None entry contributes nothing.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Fire, new[] { Element.None, Element.Nature }));
        }

        [Test]
        public void List_NoneAttack_IsNeutral()
        {
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.None, new[] { Element.Fire, Element.Water }));
        }
    }
}
