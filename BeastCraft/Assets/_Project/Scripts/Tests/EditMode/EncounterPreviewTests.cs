using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The pre-battle encounter preview: grouping (element, stance and name, in lineup order),
    /// the three detail levels' redaction, the dominant-element tie break, and the empty and
    /// null edges.
    /// </summary>
    public class EncounterPreviewTests
    {
        private sealed class Enemy : IEncounterPreviewSource
        {
            public Enemy(string name, Element element, CombatStance stance)
            {
                DisplayName = name;
                Element = element;
                Stance = stance;
            }

            public Element Element { get; }

            public CombatStance Stance { get; }

            public string DisplayName { get; }
        }

        private static Enemy Giant(Element element)
        {
            return new Enemy("Giant", element, CombatStance.Vanguard);
        }

        private static Enemy Archer(Element element)
        {
            return new Enemy("Archer", element, CombatStance.Ranged);
        }

        // ---------------------------------------------------------------------------------------
        // Grouping (Full).
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Full_GroupsByElementStanceAndName_InFirstSeenOrder()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource>
            {
                Archer(Element.Water), Giant(Element.Fire), Archer(Element.Water), Archer(Element.Dark), Giant(Element.Fire)
            };

            EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Medium);

            Assert.AreEqual(ScoutingDetail.Full, preview.Detail, "Full is the default detail");
            Assert.AreEqual(ArenaSize.Medium, preview.Arena);
            Assert.AreEqual(5, preview.TotalEnemies);
            Assert.AreEqual(3, preview.Groups.Count);
            AssertGroup(preview.Groups[0], "Archer", Element.Water, CombatStance.Ranged, 2);
            AssertGroup(preview.Groups[1], "Giant", Element.Fire, CombatStance.Vanguard, 2);
            AssertGroup(preview.Groups[2], "Archer", Element.Dark, CombatStance.Ranged, 1);
        }

        [Test]
        public void Full_SameElementButDifferentStanceOrName_StaySeparate()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource>
            {
                new Enemy("Wolf", Element.Ice, CombatStance.Skirmisher),
                new Enemy("Wolf", Element.Ice, CombatStance.Vanguard),
                new Enemy("wolf", Element.Ice, CombatStance.Skirmisher),
                new Enemy("Wolf", Element.Ice, CombatStance.Skirmisher)
            };

            EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Small, ScoutingDetail.Full);

            Assert.AreEqual(3, preview.Groups.Count, "stance and (ordinal, case-sensitive) name both split a group");
            AssertGroup(preview.Groups[0], "Wolf", Element.Ice, CombatStance.Skirmisher, 2);
            AssertGroup(preview.Groups[1], "Wolf", Element.Ice, CombatStance.Vanguard, 1);
            AssertGroup(preview.Groups[2], "wolf", Element.Ice, CombatStance.Skirmisher, 1);
        }

        [Test]
        public void NullEntriesAreSkipped_AndCountsAlwaysSumToTheTotal()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource> { null, Giant(Element.Earth), null, Giant(Element.Earth) };

            foreach (ScoutingDetail detail in new[] { ScoutingDetail.Full, ScoutingDetail.ElementsOnly, ScoutingDetail.DominantElementOnly })
            {
                EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Large, detail);
                int sum = 0;
                foreach (EncounterPreviewGroup group in preview.Groups)
                {
                    sum += group.Count;
                }

                Assert.AreEqual(2, preview.TotalEnemies, detail.ToString());
                Assert.AreEqual(preview.TotalEnemies, sum, detail.ToString());
            }
        }

        // ---------------------------------------------------------------------------------------
        // Redaction.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ElementsOnly_HidesNamesAndStances_AndMergesGroupsSharingAnElement()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource>
            {
                Giant(Element.Fire), Archer(Element.Water), Archer(Element.Fire), Archer(Element.Fire), Giant(Element.None)
            };

            EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Medium, ScoutingDetail.ElementsOnly);

            Assert.AreEqual(ScoutingDetail.ElementsOnly, preview.Detail);
            Assert.AreEqual(5, preview.TotalEnemies);
            Assert.AreEqual(3, preview.Groups.Count, "the Fire Giant and the Fire Archers become one line");
            AssertGroup(preview.Groups[0], null, Element.Fire, null, 3);
            AssertGroup(preview.Groups[1], null, Element.Water, null, 1);
            AssertGroup(preview.Groups[2], null, Element.None, null, 1);
        }

        [Test]
        public void DominantElementOnly_LeavesOneLine_WithTheMostCommonElementAndTheTotal()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource>
            {
                Giant(Element.Fire), Archer(Element.Water), Archer(Element.Water), Archer(Element.Dark)
            };

            EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Large, ScoutingDetail.DominantElementOnly);

            Assert.AreEqual(1, preview.Groups.Count);
            AssertGroup(preview.Groups[0], null, Element.Water, null, 4);
            Assert.AreEqual(4, preview.TotalEnemies);
            Assert.AreEqual(Element.Water, preview.DominantElement);
        }

        [Test]
        public void DominantElement_TieGoesToTheLowestEnumValue_WhateverTheLineupOrder()
        {
            List<IEncounterPreviewSource> darkFirst = new List<IEncounterPreviewSource>
            {
                Archer(Element.Dark), Archer(Element.Dark), Giant(Element.Earth), Giant(Element.Earth), Giant(Element.Metal)
            };
            List<IEncounterPreviewSource> withNone = new List<IEncounterPreviewSource>
            {
                Giant(Element.Fire), Giant(Element.None)
            };

            EncounterPreview tie = EncounterPreview.Build(darkFirst, ArenaSize.Medium, ScoutingDetail.DominantElementOnly);
            EncounterPreview noneTie = EncounterPreview.Build(withNone, ArenaSize.Medium, ScoutingDetail.DominantElementOnly);

            AssertGroup(tie.Groups[0], null, Element.Earth, null, 5);
            Assert.AreEqual(Element.None, noneTie.Groups[0].Element, "None (0) wins a tie it is part of");
            Assert.AreEqual(Element.Earth, EncounterPreview.Build(darkFirst, ArenaSize.Medium).DominantElement,
                            "the dominant element is the same answer at every detail level");
        }

        [Test]
        public void UnknownDetailValue_IsTreatedAsFull()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource> { Giant(Element.Fire), Archer(Element.Water) };

            EncounterPreview preview = EncounterPreview.Build(lineup, ArenaSize.Medium, (ScoutingDetail)99);

            Assert.AreEqual(ScoutingDetail.Full, preview.Detail);
            Assert.AreEqual(2, preview.Groups.Count);
            AssertGroup(preview.Groups[0], "Giant", Element.Fire, CombatStance.Vanguard, 1);
        }

        // ---------------------------------------------------------------------------------------
        // Empty and null.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EmptyOrNullLineup_IsAnEmptyPreview_AtEveryDetail()
        {
            foreach (ScoutingDetail detail in new[] { ScoutingDetail.Full, ScoutingDetail.ElementsOnly, ScoutingDetail.DominantElementOnly })
            {
                EncounterPreview empty = EncounterPreview.Build(new List<IEncounterPreviewSource>(), ArenaSize.Small, detail);
                EncounterPreview none = EncounterPreview.Build(null, ArenaSize.Small, detail);

                Assert.AreEqual(0, empty.TotalEnemies, detail.ToString());
                Assert.IsNotNull(empty.Groups);
                Assert.IsEmpty(empty.Groups);
                Assert.IsNull(empty.DominantElement);
                Assert.AreEqual(0, none.TotalEnemies);
                Assert.IsEmpty(none.Groups);
            }
        }

        [Test]
        public void AllNoneElementEncounter_KeepsNoneAsAVisibleElement()
        {
            List<IEncounterPreviewSource> lineup = new List<IEncounterPreviewSource> { Giant(Element.None), Archer(Element.None) };

            EncounterPreview elements = EncounterPreview.Build(lineup, ArenaSize.Medium, ScoutingDetail.ElementsOnly);
            EncounterPreview dominant = EncounterPreview.Build(lineup, ArenaSize.Medium, ScoutingDetail.DominantElementOnly);

            Assert.AreEqual(1, elements.Groups.Count);
            AssertGroup(elements.Groups[0], null, Element.None, null, 2);
            Assert.AreEqual(Element.None, dominant.DominantElement);
        }

        private static void AssertGroup(EncounterPreviewGroup group, string name, Element element, CombatStance? stance, int count)
        {
            Assert.AreEqual(name, group.DisplayName, "name");
            Assert.AreEqual(element, group.Element, "element");
            Assert.AreEqual(stance, group.Stance, "stance");
            Assert.AreEqual(count, group.Count, "count");
        }
    }
}
