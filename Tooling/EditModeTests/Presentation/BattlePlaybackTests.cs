using System.Collections.Generic;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Text;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The viewer's side of a battle: the demo battle built from the real content, stepped turn by
    /// turn through <see cref="BattlePlayback"/> (and proven identical to
    /// <see cref="BattleSession.Run"/>), the skill beats read off each turn, the hex layout, and the
    /// built-in pixel font.
    /// </summary>
    public class BattlePlaybackTests
    {
        private static BattleSetup Demo(out Dictionary<string, string> species, int seed = DemoBattle.DefaultSeed)
        {
            BattleSetup setup = DemoBattle.Create(VfxLibraryTests.Content, seed, out species, out string error);
            Assert.IsNotNull(setup, error);
            return setup;
        }

        [Test]
        public void DemoBattle_IsARealEncounter_WithPhoenixInTheTeam()
        {
            BattleSetup setup = Demo(out Dictionary<string, string> species);

            Assert.AreEqual("phoenix", species["beast:b1"]);
            Assert.AreEqual(4, setup.TeamBeastIds.Count);
            Assert.AreEqual(3, setup.Encounter.Enemies.Count, "the Hollow Warden template: a champion and two brutes");
            Assert.AreEqual("champion", species["enemy1"]);
            Assert.AreEqual("brute", species["enemy2"]);
        }

        [Test]
        public void DemoBattle_CanFightAGeneratedShape_OnAnyArenaItSeatsOn()
        {
            BattleSetup horde = DemoBattle.Create(VfxLibraryTests.Content, DemoBattle.DefaultSeed, out _, out string error, null, "horde", 50, 50);
            Assert.IsNotNull(horde, error);
            Assert.AreEqual(ArenaSize.Large, horde.Encounter.Arena, "a horde is drawn for its own arena");
            Assert.GreaterOrEqual(horde.Encounter.Enemies.Count, 16);
            BattlePlayback large = new BattlePlayback(BattleSession.Begin(horde));
            Assert.AreEqual(11, large.Grid.Width);

            BattleSetup squad = DemoBattle.Create(VfxLibraryTests.Content, DemoBattle.DefaultSeed, out _, out error, null, "squad", 30, 30, ArenaSize.Small);
            Assert.IsNotNull(squad, error);
            Assert.AreEqual(ArenaSize.Small, squad.Encounter.Arena, "--arena overrides the shape's own");
            BattlePlayback small = new BattlePlayback(BattleSession.Begin(squad));
            Assert.AreEqual((5, 7), (small.Grid.Width, small.Grid.Height));
            Assert.IsNotNull(small.Advance(), "the squad seats on the Small arena and fights");

            Assert.IsNull(DemoBattle.Create(VfxLibraryTests.Content, DemoBattle.DefaultSeed, out _, out error, null, "no_such_encounter"));
            StringAssert.Contains("no_such_encounter", error);
        }

        [Test]
        public void Stepping_IsExactlyBattleSessionRun()
        {
            BattleSessionResult run = BattleSession.Run(Demo(out _));
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(Demo(out _)));

            int turns = 0;
            while (playback.Advance() != null)
            {
                turns++;
            }

            Assert.IsTrue(playback.IsOver);
            Assert.IsTrue(playback.Result.Success, playback.Result.Error);
            Assert.AreEqual(run.Battle.ActionCount, turns);
            Assert.AreEqual(Trace(run), Trace(playback.Result));
            Assert.AreNotEqual(BattleOutcome.Stalemate, playback.Outcome);
        }

        [Test]
        public void EachTurn_ChainsItsSnapshots_AndTheBeatsNameRealSkills()
        {
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(Demo(out _)));
            IReadOnlyDictionary<string, UnitSnapshot> previous = playback.Initial;
            bool phoenixFired = false;

            PlayedTurn turn;
            while ((turn = playback.Advance()) != null)
            {
                Assert.AreSame(previous, turn.Before, "a turn starts where the last one ended");
                Assert.AreEqual(turn.Turn.EndPosition, turn.After[turn.Turn.Unit.Id].Position);

                foreach (SkillBeat beat in turn.Beats)
                {
                    Assert.AreEqual(turn.Turn.Unit.Id, beat.CasterId);
                    Assert.IsTrue(VfxLibraryTests.Content.KnownSkillIds.Contains(beat.SkillId), beat.SkillId);
                    phoenixFired |= beat.CasterId == "beast:b1" && beat.SkillId == "ember_shot";

                    int damage = 0;
                    foreach (BeatTarget target in beat.Targets)
                    {
                        damage += target.Damage;
                    }

                    int hits = 0;
                    SkillActivation activation = FindActivation(turn.Turn, beat.SkillId);
                    foreach (DamageHit hit in activation.Hits)
                    {
                        hits += hit.Roll.Amount;
                    }

                    Assert.AreEqual(hits, damage, "a beat's damage is its hits' damage");
                }

                previous = turn.After;
            }

            Assert.IsTrue(phoenixFired, "the Phoenix fires Ember Shot at least once");
            Assert.IsEmpty(playback.Forecast(5), "no forecast once the battle is over");
        }

        [Test]
        public void Forecast_ListsLivingUnits()
        {
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(Demo(out _)));

            List<BattleUnit> next = playback.Forecast(6);

            Assert.AreEqual(6, next.Count);
            Assert.IsTrue(next.TrueForAll(u => !u.IsDefeated));
        }

        [Test]
        public void TurnAnimation_SchedulesBeats_AndDropsHpAsHitsLand()
        {
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(Demo(out _)));
            HexLayout layout = new HexLayout(200, 180);
            bool sawKnockout = false;

            PlayedTurn turn;
            while ((turn = playback.Advance()) != null)
            {
                TurnAnimation animation = new TurnAnimation(turn, layout, VfxLibraryTests.Content.Vfx, 1);
                TurnAnimation again = new TurnAnimation(turn, layout, VfxLibraryTests.Content.Vfx, 1);
                Assert.AreEqual(turn.Beats.Count, animation.Beats.Count);
                Assert.AreEqual(again.DurationMs, animation.DurationMs, "seeded: the same turn animates the same way");

                int previousEnd = animation.MoveMs;
                foreach (ScheduledBeat beat in animation.Beats)
                {
                    Assert.GreaterOrEqual(beat.StartMs, previousEnd, "beats play one after another");
                    previousEnd = beat.EndMs;
                    Assert.AreSame(beat, animation.BeatAt(beat.StartMs));
                }

                Assert.GreaterOrEqual(animation.DurationMs, previousEnd);

                foreach (UnitSnapshot unit in turn.After.Values)
                {
                    Assert.AreEqual(unit.Hp, animation.ShownHp(unit.Id, animation.DurationMs), "every bar settles on the after-HP");
                    Assert.LessOrEqual(animation.ShownHp(unit.Id, animation.DurationMs - 1), turn.Before[unit.Id].Hp > unit.Hp ? turn.Before[unit.Id].Hp : unit.Hp);
                    Assert.AreEqual(!unit.Defeated, animation.ShownStanding(unit.Id, animation.DurationMs));
                    if (unit.Defeated && !turn.Before[unit.Id].Defeated)
                    {
                        sawKnockout = true;
                        Assert.IsTrue(animation.ShownStanding(unit.Id, 0), "a unit felled this turn is still up as the turn starts");
                    }
                }

                if (animation.Beats.Count > 0)
                {
                    ScheduledBeat first = animation.Beats[0];
                    int impact = first.StartMs + first.Timeline.ImpactMs;
                    foreach (BeatTarget target in first.Beat.Targets)
                    {
                        if (target.Damage > 0 && !turn.Before[target.UnitId].Defeated)
                        {
                            Assert.AreEqual(turn.Before[target.UnitId].Hp, animation.ShownHp(target.UnitId, impact - 1), "no damage shows before impact");
                            int expected = System.Math.Max(turn.After[target.UnitId].Hp, turn.Before[target.UnitId].Hp - target.Damage);
                            Assert.AreEqual(expected, animation.ShownHp(target.UnitId, impact), "the bar drops at impact (a shield or a later heal can hold it up)");
                        }
                    }

                    int mid = animation.MidVfxMs(0);
                    Assert.AreSame(first, animation.BeatAt(mid));
                    Assert.IsFalse(first.Timeline.Sample(mid - first.StartMs).HitStop);
                }
            }

            Assert.IsTrue(sawKnockout, "someone falls in the demo battle");
        }

        [Test]
        public void HexLayout_CentresRoundTrip_AndNeighboursSitOneStepApart()
        {
            HexLayout layout = new HexLayout(320, 180);
            HexGrid grid = new HexGrid(ArenaSize.Large);

            Assert.AreEqual((320, 180), layout.CenterPixel(HexCoordinate.Zero));
            Assert.AreEqual((352, 180), layout.CenterPixel(new HexCoordinate(1, 0)));
            Assert.AreEqual((336, 207), layout.CenterPixel(new HexCoordinate(0, 1)));
            Assert.AreEqual((304, 162), layout.TileTopLeft(HexCoordinate.Zero));

            foreach (HexCoordinate tile in grid.Tiles)
            {
                Vec2 centre = layout.Center(tile);
                Assert.AreEqual(tile, layout.TileAt(centre.X, centre.Y));
                Assert.AreEqual(tile, layout.TileAt(centre.X + 7f, centre.Y - 7f), "a point inside the tile maps to it");
            }

            Assert.AreEqual((8 * 32 + 16, 36 + 10 * 27), HexLayout.BoardSize(8, 11), "Medium: eight columns, the odd rows half a column further right");
            Assert.AreEqual((11 * 32, 36 + 10 * 27), HexLayout.DiscSize(5));
            Assert.AreEqual(layout.Center(new HexCoordinate(2, -1)), layout.FootprintCenter(new HexCoordinate(2, -1), UnitFootprint.Hex7));
            Vec2 triangle = layout.FootprintCenter(HexCoordinate.Zero, UnitFootprint.Triangle);
            Assert.AreEqual((320f + 352f + 336f) / 3f, triangle.X, 1e-3);
        }

        [Test]
        public void PixelFont_EveryCharacterIsAThreeByFiveGlyph()
        {
            foreach (char c in PixelFont.Characters)
            {
                string[] rows = PixelFont.Glyph(c);
                Assert.AreEqual(PixelFont.GlyphHeight, rows.Length, "'" + c + "'");
                foreach (string row in rows)
                {
                    Assert.AreEqual(PixelFont.GlyphWidth, row.Length, "'" + c + "'");
                }

                Assert.AreEqual(c, PixelFont.Characters[PixelFont.IndexOf(c)]);
            }

            Assert.AreEqual(PixelFont.Glyph('a'), PixelFont.Glyph('A'));
            Assert.AreEqual(PixelFont.Glyph('?'), PixelFont.Glyph('~'));
            Assert.AreEqual(11, PixelFont.Measure("abc"));
        }

        private static SkillActivation FindActivation(BattleTurnResult turn, string skillId)
        {
            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (outcome.Fired && outcome.Activation != null && outcome.Activation.Skill.SkillId == skillId)
                {
                    return outcome.Activation;
                }
            }

            foreach (SkillActivation activation in turn.AvatarActivations)
            {
                if (activation.Skill.SkillId == skillId)
                {
                    return activation;
                }
            }

            Assert.Fail("no activation of " + skillId);
            return null;
        }

        private static string Trace(BattleSessionResult result)
        {
            StringBuilder trace = new StringBuilder();
            trace.Append(result.Outcome).Append('/').Append(result.Battle.ElapsedTicks).Append('/').Append(result.Battle.ActionCount);
            foreach (BattleTurnResult turn in result.Battle.Turns)
            {
                trace.Append('|').Append(turn.Unit.Id).Append('@').Append(turn.EndPosition);
            }

            foreach (BattleUnit unit in result.Units)
            {
                trace.Append('#').Append(unit.Id).Append('=').Append(unit.CurrentHp);
            }

            return trace.ToString();
        }
    }
}
