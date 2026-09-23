using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    public class ElementChartTests
    {
        /// <summary>
        /// The full attacker-side chart v2, as approved: attack, then its 2x, 0.5x and 1.25x targets.
        /// Every pair not listed is 1x.
        /// </summary>
        private static readonly Dictionary<Element, Element[][]> Spec = new Dictionary<Element, Element[][]>
        {
            { Element.Fire, new[] { new[] { Element.Nature, Element.Metal }, new[] { Element.Water, Element.Earth }, new Element[0] } },
            { Element.Water, new[] { new[] { Element.Fire, Element.Metal }, new[] { Element.Lightning, Element.Nature }, new Element[0] } },
            { Element.Earth, new[] { new[] { Element.Lightning, Element.Ice }, new[] { Element.Water, Element.Air }, new Element[0] } },
            { Element.Air, new[] { new[] { Element.Fire, Element.Earth }, new[] { Element.Ice, Element.Nature }, new Element[0] } },
            { Element.Lightning, new[] { new[] { Element.Water, Element.Air }, new[] { Element.Earth, Element.Metal, Element.Light }, new Element[0] } },
            { Element.Ice, new[] { new[] { Element.Nature, Element.Air }, new[] { Element.Fire, Element.Metal }, new Element[0] } },
            { Element.Nature, new[] { new[] { Element.Water, Element.Earth, Element.Dark }, new[] { Element.Lightning, Element.Ice }, new Element[0] } },
            { Element.Metal, new[] { new[] { Element.Lightning, Element.Ice, Element.Light }, new[] { Element.Fire, Element.Air, Element.Dark }, new Element[0] } },
            { Element.Light, new[] { new[] { Element.Dark }, new Element[0], new[] { Element.Water, Element.Air, Element.Ice, Element.Earth } } },
            { Element.Dark, new[] { new[] { Element.Light }, new Element[0], new[] { Element.Fire, Element.Lightning, Element.Nature, Element.Metal } } },
        };

        /// <summary>The eight main elements, Fire through Metal (Light and Dark are the generalists).</summary>
        private static readonly Element[] Main =
        {
            Element.Fire, Element.Water, Element.Earth, Element.Air,
            Element.Lightning, Element.Ice, Element.Nature, Element.Metal,
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
            Element[] all = (Element[])Enum.GetValues(typeof(Element));
            Assert.AreEqual(11, all.Length);
            int pairs = 0;

            foreach (Element attack in all)
            {
                foreach (Element defend in all)
                {
                    float expected = ElementChart.Neutral;

                    if (Spec.TryGetValue(attack, out Element[][] row))
                    {
                        if (Array.IndexOf(row[0], defend) >= 0)
                        {
                            expected = ElementChart.Strong;
                        }
                        else if (Array.IndexOf(row[1], defend) >= 0)
                        {
                            expected = ElementChart.Weak;
                        }
                        else if (Array.IndexOf(row[2], defend) >= 0)
                        {
                            expected = ElementChart.Mild;
                        }
                    }

                    Assert.AreEqual(expected, ElementChart.GetMultiplier(attack, defend), attack + " vs " + defend);
                    pairs++;
                }
            }

            Assert.AreEqual(121, pairs);
        }

        [Test]
        public void Multipliers_HaveTheirDocumentedValues()
        {
            Assert.AreEqual(2f, ElementChart.Strong);
            Assert.AreEqual(1.25f, ElementChart.Mild);
            Assert.AreEqual(0.5f, ElementChart.Weak);
            Assert.AreEqual(1f, ElementChart.Neutral);
        }

        /// <summary>
        /// Chart v2's balance invariant: within the main eight, every attacking row is 2x against
        /// exactly two and 0.5x against exactly two, and every defending column takes 2x from exactly
        /// two and 0.5x from exactly two. No main-eight matchup uses the 1.25x tier.
        /// </summary>
        [Test]
        public void MainEight_AreNormalized_TwoStrongTwoWeak_ByRowAndColumn()
        {
            foreach (Element e in Main)
            {
                int rowStrong = 0;
                int rowWeak = 0;
                int colStrong = 0;
                int colWeak = 0;

                foreach (Element other in Main)
                {
                    float dealt = ElementChart.GetMultiplier(e, other);
                    float taken = ElementChart.GetMultiplier(other, e);
                    Assert.That(dealt, Is.EqualTo(ElementChart.Strong).Or.EqualTo(ElementChart.Weak).Or.EqualTo(ElementChart.Neutral), e + " vs " + other);

                    rowStrong += dealt == ElementChart.Strong ? 1 : 0;
                    rowWeak += dealt == ElementChart.Weak ? 1 : 0;
                    colStrong += taken == ElementChart.Strong ? 1 : 0;
                    colWeak += taken == ElementChart.Weak ? 1 : 0;
                }

                Assert.AreEqual(2, rowStrong, e + " row: 2x targets");
                Assert.AreEqual(2, rowWeak, e + " row: 0.5x targets");
                Assert.AreEqual(2, colStrong, e + " column: 2x attackers");
                Assert.AreEqual(2, colWeak, e + " column: 0.5x attackers");
            }
        }

        [Test]
        public void MainEight_NoMatchupIsStrongBothWays()
        {
            foreach (Element a in Main)
            {
                foreach (Element d in Main)
                {
                    bool both = ElementChart.GetMultiplier(a, d) == ElementChart.Strong && ElementChart.GetMultiplier(d, a) == ElementChart.Strong;
                    Assert.IsFalse(both, a + " and " + d + " are 2x into each other");
                }
            }
        }

        [Test]
        public void Element_IsNeutralAgainstItself()
        {
            foreach (Element e in (Element[])Enum.GetValues(typeof(Element)))
            {
                Assert.AreEqual(ElementChart.Neutral, ElementChart.GetMultiplier(e, e), e.ToString());
            }
        }

        [Test]
        public void LightAndDark_AreGeneralists()
        {
            // Each is 2x into the other.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Light, Element.Dark));
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Dark, Element.Light));

            // Each is a mild 1.25x into four main elements (the two sets split the main eight), and
            // neutral into the rest: never 0.5x on offence.
            Element[] lightMild = { Element.Water, Element.Air, Element.Ice, Element.Earth };
            Element[] darkMild = { Element.Fire, Element.Lightning, Element.Nature, Element.Metal };
            List<Element> split = new List<Element>(lightMild);
            split.AddRange(darkMild);
            CollectionAssert.AreEquivalent(Main, split);

            foreach (Element e in Main)
            {
                Assert.AreEqual(Array.IndexOf(lightMild, e) >= 0 ? 1.25f : 1f, ElementChart.GetMultiplier(Element.Light, e), "Light vs " + e);
                Assert.AreEqual(Array.IndexOf(darkMild, e) >= 0 ? 1.25f : 1f, ElementChart.GetMultiplier(Element.Dark, e), "Dark vs " + e);
            }

            // Defensively: Nature 2x and Metal 0.5x into Dark; Metal 2x and Lightning 0.5x into Light.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Nature, Element.Dark));
            Assert.AreEqual(0.5f, ElementChart.GetMultiplier(Element.Metal, Element.Dark));
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Metal, Element.Light));
            Assert.AreEqual(0.5f, ElementChart.GetMultiplier(Element.Lightning, Element.Light));

            // Every other main element is neutral into them.
            foreach (Element e in Main)
            {
                if (e != Element.Nature && e != Element.Metal)
                {
                    Assert.AreEqual(1f, ElementChart.GetMultiplier(e, Element.Dark), e + " vs Dark");
                }

                if (e != Element.Metal && e != Element.Lightning)
                {
                    Assert.AreEqual(1f, ElementChart.GetMultiplier(e, Element.Light), e + " vs Light");
                }
            }
        }

        [Test]
        public void V2_Changes_FromV1()
        {
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Air, Element.Fire), "a gust snuffs flame");
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Water, Element.Metal), "rust");
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Earth, Element.Ice), "rock shatters ice");
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Metal, Element.Lightning), "lightning rod");
            Assert.AreEqual(0.5f, ElementChart.GetMultiplier(Element.Nature, Element.Lightning), "wood insulates");
            Assert.AreEqual(0.5f, ElementChart.GetMultiplier(Element.Air, Element.Nature), "forests withstand wind");
            Assert.AreEqual(0.5f, ElementChart.GetMultiplier(Element.Metal, Element.Air), "a blade can't cut wind");

            // Now neutral.
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Water, Element.Earth));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Earth, Element.Metal));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Air, Element.Lightning));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Nature, Element.Fire));
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

            // Nature is strong against Dark, but Dark is only mildly strong against Nature.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Nature, Element.Dark));
            Assert.AreEqual(1.25f, ElementChart.GetMultiplier(Element.Dark, Element.Nature));

            // Air is strong against Fire, and Fire is neutral against Air.
            Assert.AreEqual(2f, ElementChart.GetMultiplier(Element.Air, Element.Fire));
            Assert.AreEqual(1f, ElementChart.GetMultiplier(Element.Fire, Element.Air));
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

            // The mild tier multiplies too: Dark into Fire and Metal is 1.25 * 1.25; Light into Water and Dark is 1.25 * 2.
            Assert.AreEqual(1.5625f, ElementChart.GetMultiplier(Element.Dark, new[] { Element.Fire, Element.Metal }));
            Assert.AreEqual(2.5f, ElementChart.GetMultiplier(Element.Light, new[] { Element.Water, Element.Dark }));

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
