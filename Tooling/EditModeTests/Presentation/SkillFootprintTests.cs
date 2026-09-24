using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Board;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="SkillFootprint"/>: a skill's range and area as hex cells relative to its caster,
    /// for real skills of every shape (and a seven-hex caster), and held to the battle's own
    /// targeting (<see cref="SkillTargetResolver"/>) for the fixed shapes.
    /// </summary>
    public class SkillFootprintTests
    {
        private static SkillSO Skill(string id)
        {
            SkillSO skill = VfxLibraryTests.Content.Battle.GetSkill(id);
            Assert.IsNotNull(skill, id);
            return skill;
        }

        private static SkillSO EnemySkill(string enemyId, string skillId)
        {
            foreach (SkillSO skill in VfxLibraryTests.Content.Enemies.Kit(enemyId, Element.Earth))
            {
                if (skill.SkillId == skillId)
                {
                    return skill;
                }
            }

            Assert.Fail(enemyId + " has no " + skillId);
            return null;
        }

        [Test]
        public void EmberShot_SingleTargetRange3_ReachesTheWholeRing_AndHitsOneTile()
        {
            SkillFootprint footprint = SkillFootprint.Of(Skill("ember_shot"));

            Assert.AreEqual(SkillTargetShape.SingleTarget, footprint.Shape);
            Assert.AreEqual(3 * 3 * 4, footprint.Reach.Count, "every tile 1-3 hexes away: 3R(R+1)");
            foreach (HexCoordinate tile in footprint.Reach)
            {
                Assert.That(tile.Distance(HexCoordinate.Zero), Is.InRange(1, 3));
            }

            Assert.AreEqual(new HexCoordinate(3, -3), footprint.Focus);
            CollectionAssert.AreEqual(new[] { new HexCoordinate(3, -3) }, footprint.Area);
            CollectionAssert.AreEqual(new[] { HexCoordinate.Zero }, footprint.Caster);
            Assert.AreEqual(4, footprint.Extent);
        }

        [Test]
        public void FlameWave_LineRange4_RunsStraightFromTheCasterToItsFocus()
        {
            SkillFootprint footprint = SkillFootprint.Of(Skill("flame_wave"));

            CollectionAssert.AreEquivalent(new[] { new HexCoordinate(1, -1), new HexCoordinate(2, -2), new HexCoordinate(3, -3), new HexCoordinate(4, -4) },
                                           footprint.Area);
            Assert.AreEqual(new HexCoordinate(4, -4), footprint.Focus);
            Assert.AreEqual(3 * 4 * 5, footprint.Reach.Count);

            SkillFootprint aimedRight = SkillFootprint.Of(Skill("flame_wave"), UnitFootprint.Single, new HexCoordinate(1, 0));
            CollectionAssert.Contains(aimedRight.Area, new HexCoordinate(4, 0));
            Assert.AreEqual(4, aimedRight.Area.Count);
        }

        [Test]
        public void StoneChallenge_AreaBurstRange3_IsTheDiscWithTheCaster()
        {
            SkillFootprint footprint = SkillFootprint.Of(Skill("stone_challenge"));

            Assert.AreEqual(1 + 3 * 3 * 4, footprint.Area.Count, "a radius-3 disc: 37 tiles");
            CollectionAssert.Contains(footprint.Area, HexCoordinate.Zero);
            Assert.IsNull(footprint.Focus);
            Assert.AreEqual(SkillTargetSide.Enemy, footprint.Side);
        }

        [Test]
        public void SunfireNova_CrossRange3_IsSixArmsOfThree()
        {
            SkillFootprint footprint = SkillFootprint.Of(Skill("sunfire_nova"));

            Assert.AreEqual(18, footprint.Area.Count);
            CollectionAssert.DoesNotContain(footprint.Area, HexCoordinate.Zero);
            foreach (HexCoordinate axis in HexCoordinate.AxialDirections)
            {
                CollectionAssert.Contains(footprint.Area, new HexCoordinate(axis.Q * 3, axis.R * 3));
            }
        }

        [Test]
        public void SelfAndAllShapes()
        {
            SkillFootprint self = SkillFootprint.Of(Skill("rebirth_flame"));
            CollectionAssert.AreEqual(new[] { HexCoordinate.Zero }, self.Area);
            Assert.IsEmpty(self.Reach);
            Assert.IsFalse(self.IsGlobal);

            SkillFootprint spring = SkillFootprint.Of(Skill("sacred_spring"));
            Assert.IsTrue(spring.IsGlobal);
            Assert.AreEqual(SkillTargetSide.Ally, spring.Side);
            Assert.IsEmpty(spring.Area);
            Assert.AreEqual(2, spring.Extent);
        }

        [Test]
        public void SevenHexCaster_CountsRangeFromItsNearestTile()
        {
            SkillFootprint crush = SkillFootprint.Of(EnemySkill("giant", "crush"), UnitFootprint.Hex7);
            Assert.AreEqual(7, crush.Caster.Count);
            Assert.AreEqual(12, crush.Reach.Count, "range 1 from a seven-hex body: the ring two out from its anchor");
            foreach (HexCoordinate tile in crush.Reach)
            {
                Assert.AreEqual(2, tile.Distance(HexCoordinate.Zero));
            }

            SkillFootprint quake = SkillFootprint.Of(EnemySkill("giant", "quake"), UnitFootprint.Hex7);
            Assert.AreEqual(19, quake.Area.Count, "a radius-1 burst from each of seven tiles: the radius-2 disc");
        }

        [TestCase("stone_challenge", UnitFootprint.Single)]
        [TestCase("sunfire_nova", UnitFootprint.Single)]
        [TestCase("granite_bulwark", UnitFootprint.Single)]
        [TestCase("miasma", UnitFootprint.Single)]
        [TestCase("quake", UnitFootprint.Hex7)]
        public void FixedShapes_MatchTheBattlesOwnTargeting(string skillId, UnitFootprint casterFootprint)
        {
            SkillSO skill = casterFootprint == UnitFootprint.Single ? Skill(skillId) : EnemySkill("giant", skillId);
            SkillFootprint footprint = SkillFootprint.Of(skill, casterFootprint);

            // A caster at the centre of a large board and a one-tile unit of the target side on every other tile.
            HexGrid grid = new HexGrid(ArenaSize.Large);
            StatBlock stats = new StatBlock(100, 10, 10, 10, 10, 10, 3, 0);
            BattleUnit caster = new BattleUnit("caster", BattleTeam.Player, stats, HexCoordinate.Zero, footprint: casterFootprint);
            BattleTeam side = skill.TargetSide == SkillTargetSide.Ally ? BattleTeam.Player : BattleTeam.Enemy;
            HashSet<HexCoordinate> body = new HashSet<HexCoordinate>(Footprints.Tiles(HexCoordinate.Zero, casterFootprint));
            List<BattleUnit> units = new List<BattleUnit> { caster };
            foreach (HexCoordinate tile in grid.Tiles)
            {
                if (!body.Contains(tile))
                {
                    units.Add(new BattleUnit("u" + tile.Q + "_" + tile.R, side, stats, tile));
                }
            }

            HashSet<HexCoordinate> hit = new HashSet<HexCoordinate>();
            foreach (BattleUnit unit in SkillTargetResolver.ResolveTargets(skill, caster, units, grid, new Random(1)))
            {
                hit.Add(unit.Position);
            }

            HashSet<HexCoordinate> expected = new HashSet<HexCoordinate>();
            foreach (HexCoordinate tile in footprint.Area)
            {
                if (!body.Contains(tile) && grid.IsInBounds(tile))
                {
                    expected.Add(tile);
                }
            }

            if (side == BattleTeam.Player && new List<HexCoordinate>(footprint.Area).Contains(HexCoordinate.Zero))
            {
                expected.Add(HexCoordinate.Zero);
            }

            CollectionAssert.AreEquivalent(expected, hit, skillId);
        }
    }
}
