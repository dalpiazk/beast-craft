using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Playback
{
    /// <summary>One skill beat of a <see cref="TurnAnimation"/>: when it starts, and its effect's timeline.</summary>
    public sealed class ScheduledBeat
    {
        public ScheduledBeat(SkillBeat beat, int startMs, VfxTimeline timeline)
        {
            Beat = beat;
            StartMs = startMs;
            Timeline = timeline;
        }

        public SkillBeat Beat { get; }

        /// <summary>When the beat starts, ms into the turn.</summary>
        public int StartMs { get; }

        public VfxTimeline Timeline { get; }

        /// <summary>When it ends, ms into the turn.</summary>
        public int EndMs
        {
            get { return StartMs + Timeline.DurationMs; }
        }
    }

    /// <summary>
    /// How one played turn is shown, as a pure function of time into the turn: the acting unit walks
    /// from its start tile to its end tile (<see cref="MoveMs"/>), then each skill beat plays its VFX
    /// timeline in turn, a short gap apart. It answers where a unit stands and how much HP it shows
    /// at any moment — a target's bar drops as each hit lands, and everything settles on the turn's
    /// after-snapshot when the turn ends. Seeded (by turn index) so every replay and screenshot of a
    /// turn is identical.
    /// </summary>
    public sealed class TurnAnimation
    {
        /// <summary>How long the acting unit takes to walk, when it moved.</summary>
        public const int WalkMs = 240;

        /// <summary>The pause between beats, and after the last one.</summary>
        public const int GapMs = 90;

        private readonly PlayedTurn _turn;
        private readonly HexLayout _layout;
        private readonly List<ScheduledBeat> _beats = new List<ScheduledBeat>();

        public TurnAnimation(PlayedTurn turn, HexLayout layout, VfxLibrary vfx, int seed)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
            _layout = layout;
            string actor = turn.Turn.Unit.Id;
            MoveMs = turn.Turn.StartPosition != turn.Turn.EndPosition ? WalkMs : 0;

            int clock = MoveMs;
            for (int i = 0; i < turn.Beats.Count; i++)
            {
                SkillBeat beat = turn.Beats[i];
                VfxEffectData effect = vfx == null ? null : vfx.Resolve(beat.SkillId, beat.Element);
                List<VfxTarget> targets = new List<VfxTarget>();
                foreach (BeatTarget target in beat.Targets)
                {
                    targets.Add(new VfxTarget(target.UnitId, CenterAfter(target.UnitId), target.Damage, target.Crit));
                }

                Vec2 from = actor == beat.CasterId && turn.After.ContainsKey(actor) ? CenterAfter(actor) : targets.Count > 0 ? targets[0].Position : Vec2.Zero;
                VfxTimeline timeline = new VfxTimeline(effect, from, targets, unchecked(seed * 997 + turn.Index * 31 + i));
                _beats.Add(new ScheduledBeat(beat, clock, timeline));
                clock += timeline.DurationMs + GapMs;
            }

            DurationMs = Math.Max(clock, MoveMs + GapMs);
        }

        public PlayedTurn Turn
        {
            get { return _turn; }
        }

        /// <summary>The walk's length (0 when the unit stayed put).</summary>
        public int MoveMs { get; }

        /// <summary>When the whole turn has played.</summary>
        public int DurationMs { get; }

        public IReadOnlyList<ScheduledBeat> Beats
        {
            get { return _beats; }
        }

        /// <summary>The beat playing at <paramref name="ms"/> (null in a walk or a gap).</summary>
        public ScheduledBeat BeatAt(int ms)
        {
            foreach (ScheduledBeat beat in _beats)
            {
                if (ms >= beat.StartMs && ms < beat.EndMs)
                {
                    return beat;
                }
            }

            return null;
        }

        /// <summary>
        /// A moment to show a beat mid-play (for screenshots): after the hit-stop, part-way through
        /// its flipbook, while its particles fly. <paramref name="beatIndex"/> out of range gives the turn's end.
        /// </summary>
        public int MidVfxMs(int beatIndex)
        {
            if (beatIndex < 0 || beatIndex >= _beats.Count)
            {
                return DurationMs;
            }

            ScheduledBeat beat = _beats[beatIndex];
            return beat.StartMs + beat.Timeline.HitStopEndMs + Math.Max(1, beat.Timeline.FlipbookDurationMs / 2);
        }

        /// <summary>Where <paramref name="unitId"/>'s footprint centre is drawn at <paramref name="ms"/>.</summary>
        public Vec2 UnitCenter(string unitId, int ms)
        {
            if (unitId == _turn.Turn.Unit.Id && MoveMs > 0 && ms < MoveMs && _turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                Vec2 from = _layout.FootprintCenter(before.Position, before.Footprint);
                return Vec2.Lerp(from, CenterAfter(unitId), Math.Max(0, ms) / (float)MoveMs);
            }

            return CenterAfter(unitId);
        }

        /// <summary>
        /// The HP <paramref name="unitId"/> shows at <paramref name="ms"/>: its before-HP less every
        /// hit that has landed, never below its after-HP (heals and damage over time settle at the
        /// end), and exactly its after-HP once the turn is over.
        /// </summary>
        public int ShownHp(string unitId, int ms)
        {
            if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after))
            {
                return 0;
            }

            if (ms >= DurationMs || !_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                return after.Hp;
            }

            int landed = 0;
            foreach (ScheduledBeat beat in _beats)
            {
                if (ms < beat.StartMs + beat.Timeline.ImpactMs)
                {
                    continue;
                }

                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    if (target.UnitId == unitId)
                    {
                        landed += target.Damage;
                    }
                }
            }

            return Math.Max(after.Hp, before.Hp - landed);
        }

        /// <summary>
        /// Whether <paramref name="unitId"/> is still drawn at <paramref name="ms"/>: a unit knocked
        /// out this turn stays (fading, see <see cref="ShownFading"/>) until the beat that felled it
        /// has finished playing, or until the turn ends when no hit accounts for it (damage over
        /// time); one already down before the turn is not drawn.
        /// </summary>
        public bool ShownStanding(string unitId, int ms)
        {
            if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after))
            {
                return false;
            }

            if (!after.Defeated)
            {
                return true;
            }

            bool wasDown = _turn.Before.TryGetValue(unitId, out UnitSnapshot before) && before.Defeated;
            return !wasDown && ms < Knockout(unitId, out _);
        }

        /// <summary>Whether <paramref name="unitId"/> has taken its fatal hit by <paramref name="ms"/> but is still drawn (the host fades it).</summary>
        public bool ShownFading(string unitId, int ms)
        {
            if (!ShownStanding(unitId, ms) || !_turn.After.TryGetValue(unitId, out UnitSnapshot after) || !after.Defeated)
            {
                return false;
            }

            Knockout(unitId, out int impactMs);
            return ms >= impactMs;
        }

        /// <summary>When a unit felled this turn stops being drawn; <paramref name="impactMs"/> is when its fatal hit landed.</summary>
        private int Knockout(string unitId, out int impactMs)
        {
            impactMs = DurationMs;
            if (!_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                return DurationMs;
            }

            int landed = 0;
            foreach (ScheduledBeat beat in _beats)
            {
                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    if (target.UnitId == unitId)
                    {
                        landed += target.Damage;
                    }
                }

                if (landed >= before.Hp)
                {
                    impactMs = beat.StartMs + beat.Timeline.ImpactMs;
                    return beat.EndMs;
                }
            }

            return DurationMs;
        }

        private Vec2 CenterAfter(string unitId)
        {
            return _turn.After.TryGetValue(unitId, out UnitSnapshot snapshot) ? _layout.FootprintCenter(snapshot.Position, snapshot.Footprint) : Vec2.Zero;
        }
    }
}
